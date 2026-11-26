// A finished 3D session (puttr-app lab/LabSessionView.js): how it was filmed, the way into the coach's review
// (CoachedReview.cs) and the manual replay (LabExplore.cs), what the coach found and the drills for it, the Ball and
// Body overviews, the 3D stroke model (lab/StrokeModel3DCard.js: the Pose3D frames on the BodyViewer mannequin), and
// every putt with its body reads. The mannequin is ported from puttr-app's lab viewer:
//
//   viewer/strokeModelData.js   processFrames, hasWorldData, toWorldFrames, windowFrames
//   viewer/poseSmoothing.js     smoothFrames (One-Euro: min cutoff 1.2, beta 3, d cutoff 1; frozen under visibility 0.15)
//   viewer/strokeSceneMath.js   levelFrames, orientFrames, computeFit, groundY, handPoint, trailPoints
//   viewer/StrokeSceneGL.js     the mannequin (LIMBS, JOINT_BALLS, HEAD_R), cameras, hands path
//   LabFunctions.js             analyzePuttingStroke: setup, backstroke, impact, follow through
//
// The pose file comes from the server's MediaPipe (lab_sessions.blazePoseUrl): every frame of the
// replay video at 30 fps, 33 landmarks, image space and world space (metres, from the hips, y down).
// Putt j's stroke is at replay seconds 3 + 5j + 2 to 3 + 5j + 5 (LabReplayTiming.js).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using UnityEngine.Video;

namespace PuttrApp
{
    public static class Pose3D
    {
        public sealed class Joint { public float X, Y, Z, Vis; }
        public sealed class Frame { public float T; public Dictionary<string, Joint> J = new Dictionary<string, Joint>(); }

        /// <summary>processFrames + toWorldFrames: world coordinates when most frames have them, else the image's.</summary>
        public static List<Frame> Read(object blaze, out bool world)
        {
            var frames = new List<Frame>();
            var map = J.Obj(blaze);
            int withWorld = 0;
            if (map == null) { world = false; return frames; }
            foreach (KeyValuePair<string, object> entry in map)
            {
                object f = entry.Value;
                object lm = PuttrApp.J.Get(f, "landmarks");
                if (lm == null || PuttrApp.J.Get(f, "timestamp") == null) continue;
                object w = PuttrApp.J.Get(f, "world");
                if (w != null) withWorld++;
                var frame = new Frame { T = (float)PuttrApp.J.Num(f, "timestamp") };
                foreach (KeyValuePair<string, object> p in PuttrApp.J.Obj(lm))
                {
                    object src = w != null && PuttrApp.J.Get(w, p.Key) != null ? PuttrApp.J.Get(w, p.Key) : p.Value;
                    frame.J[p.Key] = new Joint { X = (float)PuttrApp.J.Num(src, "x"), Y = (float)PuttrApp.J.Num(src, "y"), Z = (float)PuttrApp.J.Num(src, "z"), Vis = (float)PuttrApp.J.Num(p.Value, "visibility", 0.5) };
                }
                frames.Add(frame);
            }
            frames.Sort((a, b) => a.T.CompareTo(b.T));
            world = frames.Count > 0 && withWorld / (float)frames.Count > 0.5f;
            if (!world)
                // Filmed before 3D depth: the picture's own coordinates, scaled to a body's size.
                foreach (Frame f in frames) foreach (Joint j in f.J.Values) { j.X = (j.X - 0.5f) * 1.8f; j.Y = (j.Y - 0.5f) * 1.8f; j.Z *= 1.8f; }
            return frames;
        }

        public static List<Frame> Window(List<Frame> frames, float start, float end)
        {
            var list = new List<Frame>();
            foreach (Frame f in frames)
                if (f.T >= start - 1e-3f && f.T <= end + 1e-3f) list.Add(new Frame { T = f.T - start, J = new Dictionary<string, Joint>(f.J) });
            return list;
        }

        // ── One-Euro smoothing (poseSmoothing.js) ─────────────────────────────

        sealed class LowPass { float? _y; public float Filter(float x, float a) { _y = _y == null ? x : a * x + (1 - a) * _y.Value; return _y.Value; } }

        sealed class OneEuro
        {
            readonly float _min, _beta, _d;
            readonly LowPass _x = new LowPass(), _dx = new LowPass();
            float? _lastT; float _lastRaw;
            public OneEuro(float min, float beta, float d) { _min = min; _beta = beta; _d = d; }
            static float Alpha(float cutoff, float dt) { float tau = 1f / (2f * Mathf.PI * cutoff); return 1f / (1f + tau / dt); }
            public float Filter(float t, float x)
            {
                if (_lastT == null) { _lastT = t; _lastRaw = x; _dx.Filter(0, 1); return _x.Filter(x, 1); }
                float dt = Mathf.Max(t - _lastT.Value, 1e-3f), rate = (x - _lastRaw) / dt;
                _lastT = t; _lastRaw = x;
                float dxHat = _dx.Filter(rate, Alpha(_d, dt));
                return _x.Filter(x, Alpha(_min + _beta * Mathf.Abs(dxHat), dt));
            }
        }

        public static List<Frame> Smooth(List<Frame> frames, float minCutoff = 1.2f, float beta = 3f, float dCutoff = 1f, float freezeBelow = 0.15f)
        {
            if (frames.Count < 2) return frames;
            var filters = new Dictionary<string, OneEuro[]>();
            var good = new Dictionary<string, Joint>();
            var output = new List<Frame>();
            foreach (Frame f in frames)
            {
                var o = new Frame { T = f.T };
                foreach (KeyValuePair<string, Joint> p in f.J)
                {
                    Joint l = p.Value;
                    if (l.Vis < freezeBelow) { o.J[p.Key] = good.TryGetValue(p.Key, out Joint held) ? new Joint { X = held.X, Y = held.Y, Z = held.Z, Vis = l.Vis } : l; continue; }
                    if (!filters.TryGetValue(p.Key, out OneEuro[] e)) filters[p.Key] = e = new[] { new OneEuro(minCutoff, beta, dCutoff), new OneEuro(minCutoff, beta, dCutoff), new OneEuro(minCutoff, beta, dCutoff) };
                    var s = new Joint { X = e[0].Filter(f.T, l.X), Y = e[1].Filter(f.T, l.Y), Z = e[2].Filter(f.T, l.Z), Vis = l.Vis };
                    o.J[p.Key] = s;
                    good[p.Key] = s;
                }
                output.Add(o);
            }
            return output;
        }

        // ── Placing the body (strokeSceneMath.js) ─────────────────────────────

        const float Vis = 0.12f;
        static bool Visible(Joint j) => j != null && j.Vis >= Vis;
        static Joint Get(Frame f, string name) => f.J.TryGetValue(name, out Joint j) ? j : null;

        /// <summary>levelFrames: the camera's roll, from the shins, taken out (at most 0.35 rad).</summary>
        public static void Level(List<Frame> frames)
        {
            float sx = 0, sy = 0; int n = 0;
            int step = Math.Max(1, frames.Count / 25);
            for (int i = 0; i < frames.Count; i += step)
                foreach (string side in new[] { "left", "right" })
                {
                    Joint knee = Get(frames[i], side + "_knee"), ankle = Get(frames[i], side + "_ankle");
                    if (!Visible(knee) || !Visible(ankle)) continue;
                    float vx = knee.X - ankle.X, vy = knee.Y - ankle.Y, len = Mathf.Sqrt(vx * vx + vy * vy);
                    if (len == 0) len = 1;
                    sx += vx / len; sy += vy / len; n++;
                }
            if (n < 4) return;
            float roll = Mathf.Clamp(Mathf.Atan2(sx / n, -(sy / n)), -0.35f, 0.35f);
            if (Mathf.Abs(roll) < 0.01f) return;
            float c = Mathf.Cos(-roll), s = Mathf.Sin(-roll);
            foreach (Frame f in frames) foreach (Joint j in f.J.Values) { float x = j.X * c - j.Y * s, y = j.X * s + j.Y * c; j.X = x; j.Y = y; }
        }

        /// <summary>orientFrames: turned so the hip line runs along x, facing the camera's side.</summary>
        public static void Orient(List<Frame> frames)
        {
            float hx = 0, hz = 0, fx = 0, fz = 0; int n = 0, nf = 0;
            int step = Math.Max(1, frames.Count / 25);
            for (int i = 0; i < frames.Count; i += step)
            {
                Frame m = frames[i];
                Joint lh = Get(m, "left_hip"), rh = Get(m, "right_hip");
                if (Visible(lh) && Visible(rh)) { hx += rh.X - lh.X; hz += rh.Z - lh.Z; n++; }
                Joint la = Get(m, "left_ankle"), ra = Get(m, "right_ankle"), lt = Get(m, "left_foot_index"), rt = Get(m, "right_foot_index");
                if (Visible(la) && Visible(ra) && Visible(lt) && Visible(rt)) { fx += (lt.X + rt.X) / 2 - (la.X + ra.X) / 2; fz += (lt.Z + rt.Z) / 2 - (la.Z + ra.Z) / 2; nf++; }
            }
            if (n == 0) return;
            float a = Mathf.Atan2(hz / n, hx / n);
            if (nf > 0 && -(fx / nf) * Mathf.Sin(a) + (fz / nf) * Mathf.Cos(a) > 0) a += Mathf.PI;
            if (Mathf.Abs(a) < 0.01f) return;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            foreach (Frame f in frames) foreach (Joint j in f.J.Values) { float x = j.X * c + j.Z * s, z = -j.X * s + j.Z * c; j.X = x; j.Z = z; }
        }

        /// <summary>computeFit: how tall the body stands (1 to 2.2 m) and its centre.</summary>
        public static (float span, Vector3 centre) Fit(List<Frame> frames)
        {
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            int step = Math.Max(1, frames.Count / 20);
            for (int i = 0; i < frames.Count; i += step)
                foreach (Joint j in frames[i].J.Values)
                {
                    if (j.Vis < Vis) continue;
                    var p = new Vector3(j.X, j.Y, j.Z);
                    min = Vector3.Min(min, p); max = Vector3.Max(max, p);
                }
            if (!(max.y > min.y)) return (1.6f, new Vector3(0, 0.1f, 0));
            return (Mathf.Clamp(max.y - min.y, 1f, 2.2f), (min + max) / 2);
        }

        /// <summary>handPoint: the fingers when seen, else 9 cm past the wrist along the forearm.</summary>
        public static Vector3? Hand(Frame f, string side)
        {
            Joint wrist = Get(f, side + "_wrist");
            if (!Visible(wrist)) return null;
            var fingers = new List<Joint>();
            foreach (string n in new[] { "_index", "_pinky", "_thumb" }) { Joint j = Get(f, side + n); if (j != null && j.Vis >= 0.08f) fingers.Add(j); }
            if (fingers.Count > 0)
            {
                Vector3 c = Vector3.zero;
                foreach (Joint j in fingers) c += new Vector3(j.X, j.Y, j.Z);
                return c / fingers.Count;
            }
            Joint elbow = Get(f, side + "_elbow");
            var w = new Vector3(wrist.X, wrist.Y, wrist.Z);
            if (elbow == null) return w;
            Vector3 d = w - new Vector3(elbow.X, elbow.Y, elbow.Z);
            return w + (d.magnitude > 0 ? d.normalized : Vector3.zero) * 0.09f;
        }

        /// <summary>toScene: the pose's y is down and z away from the camera; the scene's are up and toward it.</summary>
        public static Vector3 Scene(Joint j) => new Vector3(j.X, -j.Y, -j.Z);
        public static Vector3 Scene(Vector3 p) => new Vector3(p.x, -p.y, -p.z);

        // ── Phases (LabFunctions.analyzePuttingStroke) ────────────────────────

        /// <summary>Indices of setup, backstroke, impact and follow through in a putt's frames; null when it cannot be read.</summary>
        public static int[] Phases(List<Frame> frames, bool lefty)
        {
            var with = frames.FindAll(f => f.J.ContainsKey("left_wrist") && f.J.ContainsKey("right_wrist"));
            Frame sample = with.Find(f => f.J.ContainsKey("left_hip") && f.J.ContainsKey("right_hip"));
            if (sample == null) return null;
            Joint lw = sample.J["left_wrist"], rw = sample.J["right_wrist"], lh = sample.J["left_hip"], rh = sample.J["right_hip"];
            bool downTheLine = Mathf.Abs(rw.Z - lw.Z) + Mathf.Abs(rh.Z - lh.Z) > Mathf.Abs(rw.X - lw.X) + Mathf.Abs(rh.X - lh.X);
            var values = new List<float>(); var index = new List<int>();
            for (int i = 0; i < frames.Count; i++)
            {
                if (!frames[i].J.TryGetValue("left_wrist", out Joint l) || !frames[i].J.TryGetValue("right_wrist", out Joint r)) continue;
                if (r.Vis < 0.6f && l.Vis < 0.6f) continue;
                float v = downTheLine ? (lefty ? l.Z : r.Z) : (l.X + r.X) / 2 * (lefty ? -1 : 1);
                values.Add(v); index.Add(i);
            }
            if (values.Count < 4) return null;
            int win = downTheLine ? 10 : 5;
            var d = new List<float>();
            for (int i = 0; i < values.Count; i++)
            {
                int s = Math.Max(0, i - win / 2), e = Math.Min(values.Count, i + (win + 1) / 2);
                float sum = 0; for (int k = s; k < e; k++) sum += values[k];
                d.Add(sum / (e - s));
            }
            float b0 = d[0];
            for (int i = 0; i < d.Count; i++) d[i] -= b0;
            int back = 0; for (int i = 1; i < d.Count; i++) if (d[i] < d[back]) back = i;
            int setup = 0; for (int i = back - 1; i > 0; i--) if (d[i] > d[i - 1] && d[i] > d[i + 1]) { setup = i; break; }
            int impact = Math.Min(back + 1, d.Count - 1); float best = float.PositiveInfinity;
            for (int i = back + 1; i < d.Count; i++)
            {
                float diff = Mathf.Abs(d[i] - d[setup]);
                if (diff < best) { best = diff; impact = i; }
                else if (i + 1 < d.Count && best < Mathf.Abs(d[i + 1] - d[setup])) break;
            }
            int follow = -1;
            for (int i = impact + 2; i < d.Count - 1; i++) if (d[i] > d[i - 1] && d[i] > d[i + 1]) { follow = i; break; }
            if (follow < 0) { float top = float.NegativeInfinity; for (int i = impact + 1; i < d.Count; i++) if (d[i] > top) { top = d[i]; follow = i; } }
            if (follow < 0) follow = Math.Min(impact + 1, d.Count - 1);
            return new[] { index[setup], index[back], index[impact], index[follow] };
        }
    }

    /// <summary>The 3D scene: a mannequin built from cylinders and balls, a floor, the hands' path, and an orbiting camera, drawn to a texture.</summary>
    public sealed class BodyViewer : MonoBehaviour
    {
        const int Layer = 30;
        static readonly (string a, string b, float wa, float wb)[] Limbs =
        {
            ("left_shoulder", "left_elbow", 0.095f, 0.075f), ("left_elbow", "left_wrist", 0.075f, 0.058f),
            ("right_shoulder", "right_elbow", 0.095f, 0.075f), ("right_elbow", "right_wrist", 0.075f, 0.058f),
            ("left_hip", "left_knee", 0.15f, 0.11f), ("left_knee", "left_ankle", 0.115f, 0.075f),
            ("right_hip", "right_knee", 0.15f, 0.11f), ("right_knee", "right_ankle", 0.115f, 0.075f),
            ("left_ankle", "left_foot_index", 0.07f, 0.055f), ("right_ankle", "right_foot_index", 0.07f, 0.055f),
            ("left_shoulder", "right_shoulder", 0.10f, 0.10f), ("left_hip", "right_hip", 0.135f, 0.135f),
            ("left_shoulder", "left_hip", 0.11f, 0.11f), ("right_shoulder", "right_hip", 0.11f, 0.11f),
        };
        static readonly (string name, float r)[] Balls =
        {
            ("left_shoulder", 0.056f), ("right_shoulder", 0.056f), ("left_elbow", 0.048f), ("right_elbow", 0.048f),
            ("left_wrist", 0.042f), ("right_wrist", 0.042f), ("left_hip", 0.07f), ("right_hip", 0.07f),
            ("left_knee", 0.062f), ("right_knee", 0.062f), ("left_ankle", 0.052f), ("right_ankle", 0.052f),
        };

        public RenderTexture Texture { get; private set; }
        Camera _camera;
        Transform _root, _head;
        readonly List<Transform> _limbs = new List<Transform>(), _balls = new List<Transform>();
        LineRenderer _hands;
        List<Pose3D.Frame> _frames;
        Vector3 _centre;
        float _span = 1.6f, _ground, _time, _speed = 1f;
        public float Yaw = Mathf.PI / 2, Pitch = 0.08f, Zoom = 1f;
        public bool Playing = true;
        public float Length => _frames != null && _frames.Count > 0 ? _frames[_frames.Count - 1].T : 0f;
        public float Time01 { get => _time; set => _time = Mathf.Clamp(value, 0, Length); }
        public float Speed { get => _speed; set => _speed = value; }

        public static BodyViewer Make()
        {
            var go = new GameObject("Body viewer");
            DontDestroyOnLoad(go);
            go.transform.position = new Vector3(0, -5000, 0);
            return go.AddComponent<BodyViewer>().Build();
        }

        BodyViewer Build()
        {
            _root = transform;
            Texture = new RenderTexture(1024, 1024, 24) { antiAliasing = 4 };
            var cam = new GameObject("Body camera");
            cam.transform.SetParent(_root, false);
            _camera = cam.AddComponent<Camera>();
            _camera.cullingMask = 1 << Layer;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Ui.Surface;   // the window's own ground (Lab.uss .lab-model-stage)
            _camera.fieldOfView = 42f;
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 50f;
            _camera.targetTexture = Texture;
            var light = new GameObject("Body light").AddComponent<Light>();
            light.transform.SetParent(_root, false);
            light.type = LightType.Directional;
            light.intensity = 1.6f;
            light.transform.localRotation = Quaternion.Euler(40f, 30f, 0f);
            // Light only the mannequin, never the arcade's own scene.
            light.cullingMask = 1 << Layer;

            Material body = Paint(new Color(0.78f, 0.8f, 0.82f)), joint = Paint(Ui.Green), floor = Paint(new Color32(0x1A, 0x1F, 0x24, 0xFF));
            foreach (var limb in Limbs) _limbs.Add(Part(PrimitiveType.Cylinder, body, (limb.wa + limb.wb) / 2));
            foreach (var ball in Balls) _balls.Add(Part(PrimitiveType.Sphere, joint, ball.r * 2));
            _head = Part(PrimitiveType.Sphere, body, 0.18f);
            Transform ground = Part(PrimitiveType.Cylinder, floor, 1f);
            ground.localScale = new Vector3(3.4f, 0.005f, 3.4f);
            ground.name = "Floor";
            _ground_ = ground;
            _hands = new GameObject("Hands path").AddComponent<LineRenderer>();
            _hands.gameObject.layer = Layer;
            _hands.transform.SetParent(_root, false);
            _hands.useWorldSpace = false;
            _hands.widthMultiplier = 0.012f;
            _hands.material = Paint(Ui.Bright);
            _hands.positionCount = 0;
            return this;
        }

        Transform _ground_;

        static Material Paint(Color colour)
        {
            Material template = Resources.Load<Material>("PuttrArcade/Lit");
            var m = template != null ? new Material(template) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", colour);
            m.color = colour;
            return m;
        }

        Transform Part(PrimitiveType type, Material material, float width)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.layer = Layer;
            go.transform.SetParent(_root, false);
            go.GetComponent<Renderer>().sharedMaterial = material;
            go.transform.localScale = Vector3.one * width;
            return go.transform;
        }

        /// <summary>A putt's frames, smoothed, levelled and turned (the app's pipeline).</summary>
        public void Show(List<Pose3D.Frame> frames)
        {
            _frames = Pose3D.Smooth(frames);
            Pose3D.Level(_frames);
            Pose3D.Orient(_frames);
            (float span, Vector3 centre) = Pose3D.Fit(_frames);
            _span = span;
            _centre = Pose3D.Scene(centre);
            // groundY: the median of the lower ankle.
            var ys = new List<float>();
            foreach (Pose3D.Frame f in _frames)
            {
                float y = float.NegativeInfinity;
                foreach (string n in new[] { "left_ankle", "right_ankle" }) if (f.J.TryGetValue(n, out Pose3D.Joint a) && a.Vis >= 0.12f) y = Mathf.Max(y, a.Y);
                if (!float.IsNegativeInfinity(y)) ys.Add(y);
            }
            ys.Sort();
            _ground = ys.Count > 0 ? -ys[ys.Count / 2] : -0.9f;
            _ground_.localPosition = new Vector3(_centre.x, _ground, _centre.z);
            _time = 0;
            Pose(0);
        }

        void Update()
        {
            if (_frames == null || _frames.Count == 0) return;
            if (Playing)
            {
                _time += UnityEngine.Time.deltaTime * _speed;
                if (_time > Length + 0.6f) _time = 0;
                Perf.Wake(0.2f);
            }
            Pose(_time);
            float radius = _span * 1.55f / Zoom;
            Vector3 target = new Vector3(_centre.x, _centre.y, _centre.z);
            Vector3 eye = target + new Vector3(Mathf.Sin(Yaw) * Mathf.Cos(Pitch), Mathf.Sin(Pitch), Mathf.Cos(Yaw) * Mathf.Cos(Pitch)) * radius;
            _camera.transform.localPosition = eye;
            _camera.transform.LookAt(_root.TransformPoint(target));
        }

        public int FrameAt(float t)
        {
            int lo = 0, hi = _frames.Count - 1;
            while (lo < hi) { int mid = (lo + hi + 1) >> 1; if (_frames[mid].T <= t) lo = mid; else hi = mid - 1; }
            return lo;
        }

        public float TimeOf(int index) => _frames == null || _frames.Count == 0 ? 0 : _frames[Mathf.Clamp(index, 0, _frames.Count - 1)].T;

        void Pose(float t)
        {
            Pose3D.Frame f = _frames[FrameAt(t)];
            Vector3 P(string n) => f.J.TryGetValue(n, out Pose3D.Joint j) ? Pose3D.Scene(j) : Vector3.zero;
            bool Seen(string n) => f.J.TryGetValue(n, out Pose3D.Joint j) && j.Vis >= 0.12f;
            for (int i = 0; i < Limbs.Length; i++)
            {
                Transform limb = _limbs[i];
                bool on = Seen(Limbs[i].a) && Seen(Limbs[i].b);
                limb.gameObject.SetActive(on);
                if (!on) continue;
                Vector3 a = P(Limbs[i].a), b = P(Limbs[i].b), d = b - a;
                float w = (Limbs[i].wa + Limbs[i].wb) / 2;
                limb.localPosition = (a + b) / 2;
                limb.localRotation = Quaternion.FromToRotation(Vector3.up, d.sqrMagnitude > 0 ? d.normalized : Vector3.up);
                limb.localScale = new Vector3(w, d.magnitude / 2, w);
            }
            for (int i = 0; i < Balls.Length; i++)
            {
                bool on = Seen(Balls[i].name);
                _balls[i].gameObject.SetActive(on);
                if (on) _balls[i].localPosition = P(Balls[i].name);
            }
            // The head above the shoulders, along the line from the shoulders to the nose.
            if (Seen("left_shoulder") && Seen("right_shoulder"))
            {
                Vector3 neck = (P("left_shoulder") + P("right_shoulder")) / 2;
                Vector3 toward = Seen("nose") ? P("nose") - neck : Vector3.up * 0.25f;
                _head.localPosition = neck + toward.normalized * Mathf.Max(0.14f + 0.09f, toward.magnitude);
                _head.gameObject.SetActive(true);
            }
            else _head.gameObject.SetActive(false);
            // The hands' path up to now.
            var path = new List<Vector3>();
            for (int i = 0; i <= FrameAt(t); i++)
            {
                Vector3? l = Pose3D.Hand(_frames[i], "left"), r = Pose3D.Hand(_frames[i], "right");
                if (l != null && r != null) path.Add(Pose3D.Scene((l.Value + r.Value) / 2));
                else if (l != null || r != null) path.Add(Pose3D.Scene((l ?? r).Value));
            }
            _hands.positionCount = path.Count;
            _hands.SetPositions(path.ToArray());
        }

        void OnDestroy() { if (Texture != null) Texture.Release(); }
    }

    /// <summary>
    /// What a 3D session's screens read, loaded once and shared: the session row (refreshed from the status route while
    /// the cloud works), its putts, the pose file and the body reads made from it, and puttr-ai's per-putt record (the
    /// coach's findings). Changed fires after each piece arrives.
    ///
    ///   GET /lab/session/status/:id     the row (status 4: the 3D annotation is ready, blazePoseUrl and replayUrl set)
    ///   GET /lab/session/putts/:id      the Player's own putts (a coach reads /coaching/session/putts/:id)
    ///   blazePoseUrl                    the pose file: every frame's landmarks
    ///   GET ai.puttr.co/v2/lab/session/:id/record   insights (label, confidence, evidence, lessonIds); may be missing
    /// </summary>
    public sealed class LabSessionData
    {
        public object Row;
        public readonly string Id;
        public readonly bool Coach;
        public List<object> Putts;
        public object Blaze;
        public BodyFaults.Pose Pose;
        public BodyFaults.Session Body;
        public object Record;
        public bool PoseTried;
        public event Action Changed;
        int _polls;
        bool _gone;

        public LabSessionData(object row, bool coach)
        {
            Row = row;
            Id = J.Str(row, "id");
            Coach = coach;
        }

        public int Status => J.Get(Row, "status") != null ? J.Int(Row, "status") : 4;
        public bool Ready => Status >= 4;
        /// <summary>The Player's side as it was stamped on the session at capture (a coach must not read a lefty as a righty).</summary>
        public bool Lefty => Side.StartsWith("L", StringComparison.OrdinalIgnoreCase);
        public string Side => J.Str(Row, "handedness").Length > 0 ? J.Str(Row, "handedness") : Coach ? "RIGHTY" : J.Str(App.I != null ? App.I.Profile : null, "handedness", "RIGHTY");
        public int PuttCount => Putts != null && Putts.Count > 0 ? Putts.Count : 6;

        void Fire() { if (!_gone) Changed?.Invoke(); }

        /// <summary>Stops every callback (the screen has gone).</summary>
        public void Close() { _gone = true; Changed = null; }

        public void Start()
        {
            App.Run(LoadPutts());
            if (Ready) { App.Run(LoadPose()); App.Run(LoadRecord()); }
            else App.Run(Poll());
        }

        IEnumerator LoadPutts()
        {
            List<object> rows = null;
            if (!Coach) yield return Api.Get("/lab/session/putts/" + Id, d => rows = J.List(d), _ => { });
            if (rows == null || rows.Count == 0) yield return Api.Get("/coaching/session/putts/" + Id, d => rows = J.List(d), _ => { });
            Putts = rows ?? new List<object>();
            if (Pose != null) Body = BodyFaults.ReadSession(Pose, Lefty, PuttCount);
            Fire();
        }

        IEnumerator LoadPose()
        {
            string url = J.Str(Row, "blazePoseUrl");
            if (url.StartsWith("http"))
                using (UnityWebRequest req = UnityWebRequest.Get(url))
                {
                    req.timeout = 60;
                    yield return req.SendWebRequest();
                    if (_gone) yield break;
                    if (req.result == UnityWebRequest.Result.Success)
                    {
                        Blaze = J.Parse(req.downloadHandler.text);
                        try { Pose = BodyFaults.Pose.Read(Blaze); Body = BodyFaults.ReadSession(Pose, Lefty, PuttCount); }
                        catch (Exception e) { Perf.Note("lab: pose file unreadable (" + e.Message + ")"); Pose = null; Body = null; }
                    }
                }
            PoseTried = true;
            Fire();
        }

        IEnumerator LoadRecord()
        {
            long status = 0; string text = null;
            yield return ChatUi.Get(ChatUi.Ai + "/v2/lab/session/" + UnityWebRequest.EscapeURL(Id) + "/record", (s, t) => { status = s; text = t; });
            if (status == 200) { Record = J.Parse(text); Fire(); }
        }

        // checkAnnotationStatus: every 2 s until the cloud has finished (status 4), then the pose file and the record.
        IEnumerator Poll()
        {
            while (!_gone && !Ready && _polls++ < 900)
            {
                yield return new WaitForSecondsRealtime(2f);
                if (_gone) yield break;
                object row = null;
                yield return Api.Get("/lab/session/status/" + Id, d => row = J.First(d), _ => { });
                if (row == null) continue;
                Row = row;
                Fire();
            }
            if (_gone || !Ready) yield break;
            App.Run(LoadPose());
            App.Run(LoadRecord());
        }

        /// <summary>puttr-ai's insights with a label, strongest first.</summary>
        public List<object> Insights => J.List(J.Get(Record, "insights")).FindAll(i => J.Str(i, "label").Length > 0);

        /// <summary>Putt j's body reading, or null.</summary>
        public BodyFaults.Reading Reading(int j) => Body != null && j >= 0 && j < Body.Putts.Count ? Body.Putts[j] : null;
    }

    /// <summary>
    /// One 3D session for the Player who filmed it (puttr-app LabSessionView): the coach's face beside the session's day, the
    /// capture banner, Review with the coach and Explore manually (members), COACH FOUND, DRILLS TO FIX THIS, the Ball and
    /// Body overviews, the 3D Stroke Model, Club data when a Vertex rode along, and All putts with their body chips.
    /// A coach opens a Player's session with ForCoach (SessionOverview.cs).
    /// </summary>
    public sealed class LabSessionScreen : AppScreen
    {
        readonly App _app;
        readonly LabSessionData _data;
        readonly VisualElement _banner, _actions, _found, _drills, _ball, _body, _model, _club, _putts;
        StrokeModel _stroke;
        List<object> _faultLessons;
        string _faultKey;
        bool _left, _modelTried;

        /// <summary>A coach's view of a Player's session (Player Review, the lesson workspace): see SessionOverview.cs.</summary>
        public static AppScreen ForCoach(App app, string sessionId, string playerName, Func<VisualElement> playerPane = null) => new CoachSessionScreen(app, sessionId, playerName, playerPane);

        /// <summary>The same, from a session row already in hand.</summary>
        public static AppScreen ForCoach(App app, object session, string playerName, Func<VisualElement> playerPane = null) => new CoachSessionScreen(app, session, playerName, playerPane);

        public LabSessionScreen(App app, object row)
        {
            _app = app;
            _data = new LabSessionData(row, false);
            _banner = new VisualElement(); _actions = new VisualElement(); _found = new VisualElement(); _drills = new VisualElement();
            _ball = new VisualElement(); _body = new VisualElement(); _model = new VisualElement(); _club = new VisualElement(); _putts = new VisualElement();

            // The session's day as the page's title, beside the coach who reviews it (their name is on the review button).
            Coaches.Persona coach = CoachVoice.Persona;
            VisualElement face = Coaches.Avatar(coach);
            face.AddToClassList("lab-head-face");
            string day = ProfileMath.Date(row, "created_at")?.ToLocalTime().ToString("MMM d", System.Globalization.CultureInfo.InvariantCulture);
            VisualElement head = Ui.Box("lab-who", face, Ui.Box("lab-who-words", Ui.Title(day ?? "Session", "title-20"), Ui.Text(LabUi.Ago(row), "body-2 lab-line", Ui.Medium)));

            ScrollView scroll = Ui.Scroll();
            scroll.Add(Ui.Header("3D session", Leave));
            scroll.Add(head);
            scroll.Add(Ui.Box("pad", _banner, _actions));
            scroll.Add(_found);
            scroll.Add(_drills);
            scroll.Add(_ball);
            scroll.Add(_body);
            scroll.Add(_model);
            scroll.Add(_club);
            scroll.Add(_putts);
            scroll.Add(FreePuttScreen.Space(32));
            Root = LabUi.Style(scroll);

            // The Player's coach: the face, the name on the review button.
            App.Run(Api.Get("/coach/settings", d =>
            {
                CoachVoice.Load(d);
                Coaches.Persona p = CoachVoice.Persona;
                VisualElement fresh = Coaches.Avatar(p);
                fresh.AddToClassList("lab-head-face");
                head.Insert(0, fresh);
                head.Remove(face);
                face = fresh;
                DrawActions();
            }, _ => { }));

            _data.Changed += Draw;
            _data.Start();
            Draw();
        }

        void Close()
        {
            if (_left) return;
            _left = true;
            _data.Close();
            _stroke?.Dispose();
            _stroke = null;
        }

        void Leave()
        {
            Close();
            CoachVoice.Hush();
            _app.Pop();
        }

        // The mannequin's camera rests while another screen is in front (and stays resting if the stack is cleared).
        public override void Hidden() => _stroke?.Sleep();
        public override void Shown() => _stroke?.Wake();

        void Draw()
        {
            if (_left) return;
            DrawBanner();
            DrawActions();
            DrawFound();
            DrawDrills();
            DrawBall();
            DrawBody();
            DrawModel();
            DrawClub();
            DrawPutts();
        }

        // How the session was filmed, as a flat card: a green label when both views landed, an amber one with the setup
        // guide and Record again when not.
        void DrawBanner()
        {
            _banner.Clear();
            LabCapture.Info cap = LabCapture.Describe(LabCapture.Parse(_data.Row));
            if (cap == null) return;
            if (cap.Ok)
            {
                _banner.Add(Ui.Box("card lab-banner", Ui.Caps(string.IsNullOrEmpty(cap.Short) ? "Good capture" : cap.Short, "caps caps-green"), Ui.Text(cap.Detail, "body-2 lab-banner-text", Ui.Medium)));
                return;
            }
            Label title = Ui.Caps(cap.Title);
            title.style.color = Ui.Amber;
            title.style.whiteSpace = WhiteSpace.Normal;
            VisualElement guide = Ui.Secondary("Setup guide", () => SetupGuide.Play(Root, "https://storage.googleapis.com/images.puttr.co/howto/Session_Setup_Real_Footage.mp4"));
            VisualElement again = Ui.Button("Record again", () => Club.Require(_app, "3D sessions", () => _app.Push(new LabIntroScreen(_app))), "btn btn-ghost lab-banner-next");
            _banner.Add(Ui.Box("card lab-banner", title, Ui.Text(cap.Detail, "body-2 lab-banner-text", Ui.Medium), Ui.Box("lab-banner-btns", guide, again)));
        }

        // Review with the coach (lean-back) and Explore manually: members only, and only once the cloud has finished.
        void DrawActions()
        {
            _actions.Clear();
            if (!_data.Ready) return;
            bool member = Club.Member(_app.Profile);
            Coaches.Persona coach = CoachVoice.Persona;
            // The page's one main button; a lock before its words until the Player is a member.
            VisualElement review = Ui.Button("Review with " + coach.Name, () => Club.Require(_app, "Video analysis", () =>
            {
                _stroke?.Pause();
                _app.Push(new CoachedReviewScreen(_app, _data.Row, _data.Putts != null && _data.Putts.Count > 0 ? _data.Putts : null));
            }), "btn lab-review");
            if (!member)
            {
                Image locked = Ui.Icon("Lock", 16, Color.white);
                locked.style.marginRight = 8;
                review.Insert(0, locked);
            }
            VisualElement explore = Ui.Button("Explore manually", () => Club.Require(_app, "Video analysis", () => { _stroke?.Pause(); _app.Push(new LabReplayScreen(_app, _data)); }), "btn btn-ghost lab-explore");
            _actions.Add(review);
            _actions.Add(explore);
        }

        // COACH FOUND: the record's top two insights, the confidence as a tag, and the evidence lines it has.
        void DrawFound()
        {
            _found.Clear();
            List<object> insights = _data.Insights;
            if (insights.Count == 0) return;
            VisualElement card = Ui.Box("lab-block");
            for (int i = 0; i < Math.Min(2, insights.Count); i++)
            {
                object ins = insights[i];
                string conf = J.Str(ins, "confidence").ToLowerInvariant();
                Color c = conf == "high" ? Ui.Bright : conf == "medium" ? Ui.Amber : Ui.Faint;
                VisualElement row = Ui.Box("row", Ui.Text(J.Str(ins, "label"), "lab-item-title", Ui.Bold));
                if (conf.Length > 0)
                {
                    Label tag = Ui.Caps(conf, "caps lab-sev");
                    tag.style.color = c;
                    row.Add(tag);
                }
                VisualElement item = Ui.Box(i > 0 ? "lab-item" : "lab-item lab-item-first", row);
                foreach (var (key, label) in new[] { ("result", "Ball"), ("club", "Club"), ("body", "Body") })
                {
                    string words = J.Str(J.Get(ins, "evidence"), key).Trim();
                    if (words.Length == 0) continue;
                    Label line = Ui.Text("<b>" + label + ":</b> " + words.Replace("<", "‹"), "body-2 lab-item-line", Ui.Medium);
                    line.enableRichText = true;
                    item.Add(line);
                }
                card.Add(item);
            }
            _found.Add(Ui.Section("Coach found"));
            _found.Add(Ui.Box("pad", card));
        }

        // DRILLS TO FIX THIS: the top insight's lessons when the lesson list has them, else the drills tagged to the
        // session's top setup fault (GET /coach/lessons/:fault).
        void DrawDrills()
        {
            if (_left) return;
            _drills.Clear();
            List<object> insights = _data.Insights;
            object top = insights.Count > 0 ? insights[0] : null;
            var insightLessons = new List<object>();
            List<object> ids = J.List(J.Get(top, "lessonIds"));
            if (ids.Count > 0)
            {
                if (Library.Lessons == null) { Library.Load(DrawDrills); return; }
                foreach (object id in ids)
                {
                    string want = id is double d ? d.ToString(System.Globalization.CultureInfo.InvariantCulture) : id as string ?? "";
                    object lesson = Library.Lessons.Find(l => J.Str(l, "id") == want);
                    if (lesson != null) insightLessons.Add(lesson);
                }
            }
            bool useInsight = insightLessons.Count > 0;
            BodyFaults.Fault fault = useInsight ? null : BodyFaults.SessionFaults(_data.Body, _data.Lefty).FirstOrDefault();
            if (!useInsight && fault != null && fault.Key != _faultKey)
            {
                _faultKey = fault.Key;
                _faultLessons = null;
                App.Run(Api.Get("/coach/lessons/" + UnityWebRequest.EscapeURL(fault.Key), d => { _faultLessons = J.List(d); if (!_left) DrawDrills(); }, _ => { _faultLessons = new List<object>(); }));
            }
            string title = useInsight ? J.Str(top, "label") : fault?.Label;
            List<object> lessons = useInsight ? insightLessons : fault != null && fault.Key == _faultKey ? _faultLessons : null;
            if (string.IsNullOrEmpty(title) || lessons == null || lessons.Count == 0) return;
            VisualElement card = Ui.Box("lab-block", Ui.Box("lab-item lab-item-first",
                Ui.Text(title, "lab-item-title", Ui.Bold),
                Ui.Text(useInsight ? "Your biggest leak this session, read across ball, club and body. Work these to clean it up." : "Your biggest setup leak this session. Work these to clean it up.", "body-2 lab-item-line", Ui.Medium)));
            foreach (object lesson in lessons)
            {
                object l = lesson;
                VisualElement words = Ui.Box("lab-drill-words", Ui.Text(J.Str(l, "title"), "lab-item-title", Ui.Bold));
                if (J.Str(l, "description").Length > 0) words.Add(Ui.Text(J.Str(l, "description"), "body-2 lab-drill-desc", Ui.Medium));
                Image play = Ui.Icon("Play", 20, Color.white), go = Ui.Icon("CaretRight", 16, Ui.Faint);
                play.AddToClassList("lab-drill-icon");
                go.AddToClassList("lab-drill-end");
                card.Add(Ui.Box("lab-item lab-drill", play, words, go).OnTap(() => { _stroke?.Pause(); _app.Push(new LessonScreen(_app, l)); }));
            }
            _drills.Add(Ui.Section("Drills to fix this"));
            _drills.Add(Ui.Box("pad", card));
        }

        void DrawBall()
        {
            _ball.Clear();
            if (_data.Putts == null) { _ball.Add(Page.Loading()); return; }
            _ball.Add(Ui.Section("Ball overview"));
            _ball.Add(Ui.Box("pad", LabUi.BallOverview(_data.Putts, Root)));
        }

        void DrawBody()
        {
            _body.Clear();
            bool processing = !_data.Ready;
            if (_data.Body == null || !_data.Body.Any)
            {
                if (!processing) return;
                int st = _data.Status;
                string words = st == 1 ? "Uploading video..." : st == 2 ? "Analyzing video..." : st == 3 ? "Building 3D annotation..." : "Processing 3D annotation...";
                _body.Add(Ui.Section("Body overview"));
                _body.Add(Ui.Box("pad", Ui.Box("card lab-processing", Spinner.Small(), Ui.Text(words, "body-2 lab-processing-text", Ui.Medium))));
                return;
            }
            _body.Add(Ui.Section("Body overview"));
            _body.Add(Ui.Box("pad", LabUi.BodyOverview(_data.Body, _data.Lefty, false)));
        }

        // Off for now (Matt, 2026-11-24): the model is not accurate enough to show. Set to true to bring it back.
        const bool ShowStrokeModel = false;

        void DrawModel()
        {
            if (!ShowStrokeModel) return;
            if (_stroke != null || _data.Blaze == null || _modelTried) return;
            _modelTried = true; // reading the frames is costly: once per screen
            var model = new StrokeModel(_app, _data.Blaze, _data.Pose, _data.Lefty, _data.Putts != null ? _data.Putts.Count : 0);
            if (!model.Available) { model.Dispose(); return; }
            _stroke = model;
            _model.Add(Ui.Section("3D stroke model", "Full screen", model.Full));
            _model.Add(Ui.Box("pad", model.Card()));
        }

        // Club data: the Vertex summary, when any putt carries the putter sensor's stroke.
        void DrawClub()
        {
            _club.Clear();
            if (_data.Putts == null || !_data.Putts.Exists(p => J.Str(p, "sensorData").Length > 2)) return;
            VisualElement summary = VertexSummary.Build(_data.Putts);
            if (summary == null) return;
            _club.Add(Ui.Section("Club data · from your Vertex"));
            _club.Add(Ui.Box("pad", summary));
        }

        void DrawPutts()
        {
            _putts.Clear();
            if (_data.Putts == null || _data.Putts.Count == 0) return;
            _putts.Add(Ui.Section("All putts"));
            // One bordered block: a row a putt, its body reads under it.
            VisualElement list = Ui.Box("lab-block");
            for (int i = 0; i < _data.Putts.Count; i++)
                list.Add(Ui.Box(i == 0 ? "lab-pitem lab-pitem-first" : "lab-pitem", LabUi.PuttCard(_data.Putts[i], i, false, Root), LabUi.BodyChips(_data.Reading(i), _data.Lefty)));
            _putts.Add(Ui.Box("pad", list));
        }
    }

    /// <summary>
    /// The 3D Stroke Model (puttr-app StrokeModel3DCard): one putt's stroke on the mannequin, chosen with the putt chips,
    /// dragged to turn, played, scrubbed, slowed (1x, 0.5x, 0.25x) and jumped to Setup, Backstroke, Impact or Follow
    /// Through. The mannequin is a picture in a window; every control is on the solid bar under it. "Full screen" (the
    /// section's link) opens it over the whole screen. Sessions filmed before world landmarks show the flat projection
    /// with the app's note.
    /// </summary>
    public sealed class StrokeModel
    {
        static readonly (string label, float pct)[] Phases = { ("Setup", 0.1f), ("Backstroke", 0.35f), ("Impact", 0.55f), ("Follow Through", 0.75f) };
        static readonly float[] Speeds = { 1f, 0.5f, 0.25f };

        readonly App _app;
        readonly bool _lefty, _world;
        readonly List<Pose3D.Frame> _frames;
        readonly BodyViewer _viewer;
        readonly BodyFaults.Pose _pose;
        public readonly List<int> Putts = new List<int>();
        readonly List<Action> _views = new List<Action>();
        int _putt = -1, _speed;
        readonly float[] _phases = new float[4];

        public bool Available => Putts.Count > 0 && _viewer != null;

        public StrokeModel(App app, object blaze, BodyFaults.Pose pose, bool lefty, int puttCount)
        {
            _app = app;
            _lefty = lefty;
            _frames = Pose3D.Read(blaze, out _world);
            _pose = pose ?? BodyFaults.Pose.Read(blaze);
            if (_frames.Count == 0) return;
            float total = _frames[_frames.Count - 1].T;
            int max = puttCount > 0 ? puttCount : Mathf.Max(0, Mathf.RoundToInt((total - 3f) / 5f));
            for (int j = 0; j < max; j++)
            {
                (double s, double e) = BodyFaults.Window(j);
                int n = 0;
                foreach (Pose3D.Frame f in _frames) if (f.T >= s && f.T <= e && f.J.Count > 0) n++;
                if (n >= 8) Putts.Add(j);
            }
            if (Putts.Count == 0) return;
            _viewer = BodyViewer.Make();
            _viewer.Playing = false;
            Show(Putts[0]);
        }

        public void Dispose()
        {
            if (_viewer != null) UnityEngine.Object.Destroy(_viewer.gameObject);
        }

        public void Pause() { if (_viewer != null) _viewer.Playing = false; Refresh(); }

        /// <summary>Stops drawing the mannequin (no screen shows it now).</summary>
        public void Sleep() { Pause(); if (_viewer != null) _viewer.gameObject.SetActive(false); }

        /// <summary>Draws it again (a screen that shows it is in front).</summary>
        public void Wake() { if (_viewer != null) _viewer.gameObject.SetActive(true); }

        void Show(int j)
        {
            _putt = j;
            (double s, double e) = BodyFaults.Window(j);
            _viewer.Show(Pose3D.Window(_frames, (float)s, (float)e));
            float dur = _viewer.Length;
            // The detected phases (analyzePuttingStroke on this putt's window), else fixed shares of the stroke.
            BodyFaults.Stroke a = BodyFaults.Analyze(_pose, s, e, _lefty);
            bool any = false;
            double[] at = a == null ? new double[4] { double.NaN, double.NaN, double.NaN, double.NaN } : new[] { a.Setup, a.Back, a.Impact, a.Follow };
            for (int i = 0; i < 4; i++)
            {
                _phases[i] = double.IsNaN(at[i]) ? -1 : Mathf.Clamp((float)(at[i] - s), 0, dur);
                if (_phases[i] >= 0) any = true;
            }
            if (!any) for (int i = 0; i < 4; i++) _phases[i] = dur * Phases[i].pct;
            // Open on the angle it was filmed from: face on looks at the chest, down the line along the target line.
            if (a != null) _viewer.Yaw = a.DownTheLine ? Mathf.PI / 2 : 0f;
            _viewer.Pitch = 0.08f;
            _viewer.Time01 = 0;
            Refresh();
        }

        void Refresh() { foreach (Action a in _views.ToArray()) a(); }

        int ActivePhase()
        {
            int active = 0;
            for (int i = 0; i < 4; i++) if (_phases[i] >= 0 && _phases[i] <= _viewer.Time01 + 0.01f) active = i;
            return active;
        }

        /// <summary>The card on the session page.</summary>
        public VisualElement Card() => Build(false, null);

        /// <summary>The viewer, its chips and controls. full: the whole screen (with close).</summary>
        VisualElement Build(bool full, Action close)
        {
            VisualElement root = Ui.Box(full ? "lab-model-full" : "lab-model");
            var picture = new Image { image = _viewer.Texture, scaleMode = ScaleMode.ScaleAndCrop };
            picture.AddToClassList(full ? "lab-model-picture-full" : "lab-model-picture");
            // No words on the picture: the hint is the last line of the bar under it.
            Label hint = Ui.Caps("Drag the model to turn it", "caps lab-model-hint");
            VisualElement stage = Ui.Box(full ? "lab-model-stage-full" : "lab-model-stage", picture);
            Drag(stage);

            VisualElement putts = Ui.Box("lab-model-chips");
            VisualElement phases = Ui.Box("lab-model-phases");
            VisualElement play = LabUi.Round("Play", 44, Ui.Surface2, () => { _viewer.Playing = !_viewer.Playing; if (_viewer.Playing && _viewer.Time01 >= _viewer.Length) _viewer.Time01 = 0; Refresh(); }, 16);
            var track = new Scrub(t => { _viewer.Time01 = t * _viewer.Length; Refresh(); });
            Label speed = Ui.Text("1x", "lab-model-speed-text", Ui.Bold);
            VisualElement speedBox = Ui.Box("lab-model-speed", speed).OnTap(() => { _speed = (_speed + 1) % Speeds.Length; _viewer.Speed = Speeds[_speed]; Refresh(); });
            VisualElement controls = Ui.Box("row lab-model-controls", play, track, speedBox);

            string drawn = null;
            void Draw()
            {
                track.Value = _viewer.Length > 0 ? _viewer.Time01 / _viewer.Length : 0;
                // The chips are built again only when something on them changed (this runs every frame while it plays).
                string key = _putt + "|" + ActivePhase() + "|" + _viewer.Playing + "|" + _speed;
                if (key == drawn) return;
                drawn = key;
                putts.Clear();
                if (Putts.Count >= 2)
                    foreach (int j in Putts) { int pick = j; putts.Add(LabUi.Pill("Putt " + (j + 1), j == _putt, () => { if (pick != _putt) Show(pick); })); }
                phases.Clear();
                int active = ActivePhase();
                for (int i = 0; i < 4; i++)
                {
                    int k = i;
                    bool on = _phases[i] >= 0;
                    VisualElement pill = LabUi.Pill(Phases[i].label, active == i, on ? () => { _viewer.Time01 = _phases[k]; Refresh(); } : (Action)null);
                    if (!on) pill.AddToClassList("lab-pill-off");
                    phases.Add(pill);
                }
                play.Clear();
                play.Add(LabUi.Icon(_viewer.Playing ? "Pause" : "Play", 16, Color.white));
                speed.text = _speed == 0 ? "1x" : _speed == 1 ? "0.5x" : "0.25x";
                speed.style.color = _speed == 0 ? Color.white : (Color)Ui.Bright;
            }
            // Kept in step only while on screen (the card comes back when the screen in front of it goes).
            _views.Add(Draw);
            root.RegisterCallback<DetachFromPanelEvent>(_ => _views.Remove(Draw));
            root.RegisterCallback<AttachToPanelEvent>(_ => { if (!_views.Contains(Draw)) _views.Add(Draw); drawn = null; Draw(); });

            // The picture, then one solid bar: play, scrub and speed, the phases, the putts, the hint.
            VisualElement bar = Ui.Box(full ? "lab-model-bottom" : "lab-model-bar", controls, phases, Ui.Scroll(true).With(putts), hint);
            if (!full && !_world) bar.Add(Ui.Text("Filmed before 3D depth. Your next session unlocks the full 3D view.", "body-2 lab-model-note", Ui.Medium));
            root.Add(stage);
            root.Add(bar);
            if (full)
            {
                VisualElement x = LabUi.Round("X", 44, Ui.Surface2, close, 20);
                x.AddToClassList("lab-model-close");
                root.Add(x);
            }

            // The picture is drawn by the viewer's camera, not UI Toolkit: repaint it, and keep the controls in step while it plays.
            root.schedule.Execute(() =>
            {
                picture.MarkDirtyRepaint();
                if (_viewer.Playing) Draw();
            }).Every(33);
            Draw();
            return root;
        }

        /// <summary>Full screen opens playing, as the app's does.</summary>
        public void Full() => _app.Push(new FullScreen(this));

        void Play() { if (_viewer == null) return; _viewer.Playing = true; Refresh(); }

        sealed class FullScreen : AppScreen
        {
            readonly StrokeModel _model;

            public FullScreen(StrokeModel model)
            {
                _model = model;
                Root = LabUi.Style(Ui.Box("lab-model-screen"));
                Root.Add(model.Build(true, () => { model.Pause(); model._app.Pop(); }));
            }

            public override void Shown() { _model.Wake(); _model.Play(); }
            public override void Hidden() => _model.Sleep();
        }

        // Drag to turn the model: across turns it round, up and down tips the camera.
        void Drag(VisualElement stage)
        {
            Vector2? last = null;
            stage.RegisterCallback<PointerDownEvent>(e => { last = e.position; stage.CapturePointer(e.pointerId); });
            stage.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (last == null || !stage.HasPointerCapture(e.pointerId)) return;
                Vector2 d = (Vector2)e.position - last.Value;
                last = e.position;
                _viewer.Yaw -= d.x * 0.012f;
                _viewer.Pitch = Mathf.Clamp(_viewer.Pitch + d.y * 0.008f, -0.3f, 1.3f);
                Perf.Wake(0.3f);
            });
            stage.RegisterCallback<PointerUpEvent>(e => { last = null; if (stage.HasPointerCapture(e.pointerId)) stage.ReleasePointer(e.pointerId); });
        }
    }

    /// <summary>A scrub bar (the app's Slider): a track, its fill and a thumb; dragging or tapping sets 0 to 1.</summary>
    public sealed class Scrub : VisualElement
    {
        readonly VisualElement _fill, _thumb;
        readonly Action<float> _moved;
        float _value;

        public Scrub(Action<float> moved)
        {
            _moved = moved;
            AddToClassList("lab-scrub");
            _fill = Ui.Box("lab-scrub-fill");
            _thumb = Ui.Box("lab-scrub-thumb");
            VisualElement track = Ui.Box("lab-scrub-track", _fill);
            // Only the bar itself takes the pointer, so positions are measured across its whole width.
            track.pickingMode = _fill.pickingMode = _thumb.pickingMode = PickingMode.Ignore;
            Add(track);
            Add(_thumb);
            RegisterCallback<PointerDownEvent>(e => { this.CapturePointer(e.pointerId); Set(e.localPosition.x); });
            RegisterCallback<PointerMoveEvent>(e => { if (this.HasPointerCapture(e.pointerId)) Set(e.localPosition.x); });
            RegisterCallback<PointerUpEvent>(e => { if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId); });
        }

        void Set(float x)
        {
            float w = contentRect.width;
            if (w <= 0) return;
            Value = Mathf.Clamp01(x / w);
            _moved?.Invoke(_value);
            Perf.Wake(0.3f);
        }

        public float Value
        {
            get => _value;
            set
            {
                _value = Mathf.Clamp01(float.IsNaN(value) ? 0 : value);
                _fill.style.width = Length.Percent(_value * 100f);
                _thumb.style.left = Length.Percent(_value * 100f);
            }
        }
    }

    static class LabElements
    {
        /// <summary>Adds the children and gives the element back (for a horizontal scroll of chips).</summary>
        public static T With<T>(this T element, params VisualElement[] children) where T : VisualElement
        {
            foreach (VisualElement c in children) if (c != null) element.Add(c);
            return element;
        }
    }
}

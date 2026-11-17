// A finished 3D session: the stroke as a 3D mannequin, the replay video, each putt's phases, and a
// review in the coach's voice. Ported from puttr-app's lab viewer:
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
            _camera.backgroundColor = new Color32(0x10, 0x14, 0x18, 0xFF);
            _camera.fieldOfView = 42f;
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 50f;
            _camera.targetTexture = Texture;
            var light = new GameObject("Body light").AddComponent<Light>();
            light.transform.SetParent(_root, false);
            light.type = LightType.Directional;
            light.intensity = 1.6f;
            light.transform.localRotation = Quaternion.Euler(40f, 30f, 0f);

            Material body = Paint(new Color(0.78f, 0.8f, 0.82f)), joint = Paint(new Color32(0x2D, 0x8C, 0x3C, 0xFF)), floor = Paint(new Color32(0x1A, 0x1F, 0x24, 0xFF));
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
            _hands.material = Paint(new Color32(0x7A, 0xD4, 0x46, 0xFF));
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

    /// <summary>One 3D session: the mannequin or the video, the putts, the phases, and the coach's review.</summary>
    public sealed class LabSessionScreen : AppScreen
    {
        static readonly string[] PhaseNames = { "Setup", "Backstroke", "Impact", "Follow through" };
        static readonly float[] PhaseFallback = { 0.10f, 0.35f, 0.55f, 0.75f };

        readonly App _app;
        readonly object _row;
        readonly string _id;
        readonly VisualElement _stage, _controls, _putts, _review;
        readonly Image _picture;
        readonly Label _note;
        List<Pose3D.Frame> _all;
        object _blaze;
        List<object> _puttRows = new List<object>();
        BodyViewer _viewer;
        VideoPlayer _video;
        RenderTexture _videoTexture;
        int _putt, _view; // view 0 the 3D body, 1 the video
        int[] _phases;
        bool _lefty;

        public LabSessionScreen(App app, object row)
        {
            _app = app;
            _row = row;
            _id = J.Str(row, "id");
            _lefty = J.Str(row, "handedness").ToUpperInvariant().StartsWith("L");
            _picture = new Image { scaleMode = ScaleMode.ScaleToFit };
            _picture.AddToClassList("lab-picture");
            _note = Ui.Text("Loading your 3D session...", "muted center", Ui.Regular);
            _stage = Ui.Box("lab-stage", _picture, _note);
            _controls = new VisualElement(); _putts = new VisualElement(); _review = new VisualElement();
            ScrollView scroll = Ui.Scroll();
            scroll.Add(Ui.Header("3D session", Leave));
            scroll.Add(Ui.Box("pad", _stage, _controls, _review));
            scroll.Add(_putts);
            var foot = new VisualElement();
            foot.style.height = 28;
            scroll.Add(foot);
            Root = scroll;
            DrawControls();
            Load();
        }

        void Leave()
        {
            if (_viewer != null) UnityEngine.Object.Destroy(_viewer.gameObject);
            if (_video != null) UnityEngine.Object.Destroy(_video.gameObject);
            if (_videoTexture != null) _videoTexture.Release();
            CoachVoice.Hush();
            _app.Pop();
        }

        void Load()
        {
            int status = J.Int(_row, "status");
            string poseUrl = J.Str(_row, "blazePoseUrl");
            // A coach reviewing a Player's session reads the putts from the coaching side.
            App.Run(Api.Get("/lab/session/putts/" + _id, data => { _puttRows = J.List(data); if (_puttRows.Count == 0) Coaching(); else DrawPutts(); }, _ => Coaching()));
            if (status < 4 || !poseUrl.StartsWith("http")) { _note.text = status < 4 ? "Still processing in the cloud. Check back in a few minutes." : "This session has no 3D data."; return; }
            App.Run(FetchPose(poseUrl));
        }

        IEnumerator FetchPose(string url)
        {
            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                req.timeout = 60;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) { _note.text = "Could not load the 3D data. Check your connection."; yield break; }
                _blaze = J.Parse(req.downloadHandler.text);
                _all = Pose3D.Read(_blaze, out bool world);
                if (!world) _note.text = "Filmed before 3D depth. Your next session unlocks the full 3D view.";
                else _note.text = "";
            }
            _viewer = BodyViewer.Make();
            _picture.image = _viewer.Texture;
            ShowPutt(0);
        }

        // Putt j's stroke: replay seconds 3 + 5j + 2 to 3 + 5j + 5.
        void ShowPutt(int j)
        {
            _putt = j;
            if (_all == null || _viewer == null) return;
            float start = 3 + 5 * j + 2;
            List<Pose3D.Frame> window = Pose3D.Window(_all, start, start + 3);
            if (window.Count < 8) { _note.text = "Putt " + (j + 1) + " was not seen clearly enough to model."; return; }
            _note.text = "";
            _phases = Pose3D.Phases(window, _lefty);
            _viewer.Show(window);
            if (_video != null) { _video.time = 3 + 5 * j; _video.Play(); }
            DrawControls();
        }

        void DrawControls()
        {
            _controls.Clear();
            _controls.Add(FreePuttScreen.Choice(new[] { "3D body", "Video" }, _view, i => { _view = i; Switch(); }));
            ScrollView putts = Ui.Scroll(true);
            putts.style.marginTop = 10;
            for (int i = 0; i < LabMath.Putts; i++)
            {
                int index = i;
                putts.Add(Ui.Box(i == _putt ? "scope scope-on" : "scope", Ui.Text("Putt " + (i + 1), "scope-text", Ui.Bold)).OnTap(() => ShowPutt(index)));
            }
            _controls.Add(putts);
            if (_view == 0)
            {
                VisualElement phases = Ui.Box("row");
                phases.style.marginTop = 8;
                for (int i = 0; i < 4; i++)
                {
                    int p = i;
                    phases.Add(Ui.Box("grow phase", Ui.Text(PhaseNames[i], "phase-text", Ui.Bold)).OnTap(() => Phase(p)));
                }
                _controls.Add(phases);
                _controls.Add(Ui.Box("row",
                    Ui.Box("btn-small", Ui.Text(_viewer != null && !_viewer.Playing ? "Play" : "Pause", "btn-text", Ui.Bold)).OnTap(() => { if (_viewer != null) { _viewer.Playing = !_viewer.Playing; DrawControls(); } }),
                    FreePuttScreen.Space(8),
                    FreePuttScreen.Choice(new[] { "1x", "0.5x", "0.25x" }, _viewer == null ? 0 : _viewer.Speed >= 1 ? 0 : _viewer.Speed >= 0.5f ? 1 : 2, i => { if (_viewer != null) _viewer.Speed = new[] { 1f, 0.5f, 0.25f }[i]; DrawControls(); }).Width(190)).Margin(8));
                // The camera angle on a row of its own (beside the speeds it was squeezed to a letter a line).
                _controls.Add(FreePuttScreen.Choice(new[] { "Side", "Front", "Top" }, 0, i => View(i)).Margin(8));
            }
            DrawReview();
        }

        void Phase(int p)
        {
            if (_viewer == null) return;
            _viewer.Playing = false;
            _viewer.Time01 = _phases != null ? _viewer.TimeOf(_phases[p]) : _viewer.Length * PhaseFallback[p];
            DrawControls();
        }

        // The app's camera presets: down the line, face on, from above.
        void View(int i)
        {
            if (_viewer == null) return;
            _viewer.Yaw = i == 0 ? Mathf.PI / 2 : 0f;
            _viewer.Pitch = i == 2 ? 1.35f : 0.08f;
            _viewer.Zoom = i == 2 ? 1.05f : 1f;
        }

        void Switch()
        {
            if (_view == 1)
            {
                string url = J.Str(_row, "replayUrl");
                if (!url.StartsWith("http")) url = J.Str(_row, "videoUrl");
                if (!url.StartsWith("http")) { _app.Toast("This session's video is not ready."); _view = 0; DrawControls(); return; }
                if (_video == null)
                {
                    var go = new GameObject("Session video");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    _video = go.AddComponent<VideoPlayer>();
                    _videoTexture = new RenderTexture(720, 1280, 0);
                    _video.renderMode = VideoRenderMode.RenderTexture;
                    _video.targetTexture = _videoTexture;
                    _video.source = VideoSource.Url;
                    _video.url = url;
                    _video.audioOutputMode = VideoAudioOutputMode.None;
                    _video.isLooping = false;
                    _video.prepareCompleted += v => { v.time = 3 + 5 * _putt; v.Play(); };
                    _video.Prepare();
                }
                else { _video.time = 3 + 5 * _putt; _video.Play(); }
                _picture.image = _videoTexture;
            }
            else
            {
                if (_video != null) _video.Pause();
                if (_viewer != null) _picture.image = _viewer.Texture;
            }
            DrawControls();
        }

        void DrawPutts()
        {
            _putts.Clear();
            if (_puttRows.Count == 0) return;
            _putts.Add(Ui.Section("Putts"));
            VisualElement card = Ui.Box("glass");
            for (int i = 0; i < _puttRows.Count; i++)
            {
                object row = _puttRows[i];
                bool made = J.Get(row, "entry") is string e && e.StartsWith("[") && !e.StartsWith("[0,0");
                double? rollout = J.Get(row, "rollout") != null ? J.Num(row, "rollout") : (double?)null;
                Label result = Ui.Text(made ? "Made" : "Missed", "sub", Ui.Bold);
                result.style.color = made ? Ui.Bright : Ui.Red;
                int index = i;
                card.Add(Ui.Box("board-row", Ui.Text((i + 1).ToString(), "board-place", Ui.Bold), Ui.Box("grow", result), Ui.Text(AppPutt.FmtRollout(rollout) ?? "", "muted", Ui.Regular)).OnTap(() => ShowPutt(index)));
            }
            _putts.Add(Ui.Box("pad", card));
        }

        // ── The coach's review ───────────────────────────────────────────────

        void Coaching() => App.Run(Api.Get("/coaching/session/putts/" + _id, data => { _puttRows = J.List(data); DrawPutts(); }, _ => { }));

        void DrawReview()
        {
            _review.Clear();
            Coaches.Persona coach = CoachVoice.Persona;
            VisualElement button = Ui.Button("Review with " + coach.Name, () => Club.Require(_app, "Coach review", () => App.Run(Review())), "btn btn-ghost");
            _review.Add(button);
        }

        /// <summary>
        /// The coach talks through the session (CoachedReview, simplified): what was made, then a word on each putt
        /// while it plays. The words come from ai.puttr.co /v2/coach/narrate; the app's own lines stand in when it
        /// cannot be reached. The body faults the app adds (shoulders, eye line) are not computed here yet.
        /// </summary>
        IEnumerator Review()
        {
            int made = 0;
            var moments = new List<object>();
            var faultsOf = new List<BodyFaults.Result>();
            for (int i = 0; i < _puttRows.Count; i++)
            {
                object row = _puttRows[i];
                bool m = J.Get(row, "entry") is string e && e.StartsWith("[") && !e.StartsWith("[0,0");
                if (m) made++;
                // The setup faults the camera saw on this putt (shoulders, eyes, knees; ball, weight, stance).
                BodyFaults.Result setup = _blaze != null ? BodyFaults.Analyze(_blaze, i, _lefty) : null;
                faultsOf.Add(setup);
                var labels = new List<object>();
                if (setup != null) foreach (BodyFaults.Fault f in setup.Faults) labels.Add(f.Label);
                string side = m ? null : AppPutt.MissSide(PuttAssessment.Bp(row), PuttAssessment.Hb(row));
                moments.Add(m && labels.Count == 0
                    ? new Dictionary<string, object> { { "kind", "good" }, { "putt", i + 1 }, { "distance", J.Int(row, "distance") }, { "made", true } }
                    : new Dictionary<string, object> { { "kind", "fix" }, { "putt", i + 1 }, { "made", m }, { "miss", side != null ? side.ToLowerInvariant() : "line" }, { "faults", labels } });
            }
            moments.Insert(0, new Dictionary<string, object> { { "kind", "intro" }, { "made", made }, { "total", _puttRows.Count } });
            string first = HomeScreen.FirstName(_app.Profile);
            List<object> lines = null;
            string token = null;
            yield return Auth.Token(t => token = t);
            var body = new Dictionary<string, object> { { "personaId", CoachVoice.Persona.Id }, { "firstName", first }, { "moments", moments } };
            yield return Api.Send("POST", "https://ai.puttr.co/v2/coach/narrate", J.Write(body), token, (s, text) => { if (s == 200) lines = J.List(J.Get(J.Parse(text), "lines")); });
            var said = new List<string>();
            if (lines != null && lines.Count > 0) foreach (object l in lines) said.Add(l as string);
            else
            {
                said.Add("Nice work, " + (first.Length > 0 ? first : "golfer") + ". You made " + made + " of " + _puttRows.Count + " this session.");
                for (int i = 0; i < _puttRows.Count; i++)
                {
                    var d = (Dictionary<string, object>)moments[i + 1];
                    bool m = (bool)d["made"];
                    BodyFaults.Result setup = faultsOf[i];
                    if (setup != null && setup.Faults.Count > 0) said.Add(BodyFaults.Line(m, d.TryGetValue("miss", out object ms) && (string)ms != "line" ? (string)ms : null, setup.Faults));
                    else said.Add("Putt " + (i + 1) + ". " + (m ? "That's the stroke." : "Stay with it. Same line again."));
                }
            }
            for (int i = 0; i < said.Count; i++)
            {
                if (i > 0 && i - 1 < LabMath.Putts) ShowPutt(i - 1);
                // The faults on screen while the coach talks about them.
                _review.Clear();
                if (i > 0 && i - 1 < faultsOf.Count && faultsOf[i - 1] is BodyFaults.Result shown)
                {
                    VisualElement chips = Ui.Box("chips", Ui.Text("Putt " + i + " · " + (shown.DownTheLine ? "down the line" : "face on"), "chip", Ui.Bold));
                    foreach (BodyFaults.Fault f in shown.Faults) chips.Add(Ui.Text(f.Label, "chip chip-red", Ui.Bold));
                    if (shown.Faults.Count == 0) chips.Add(Ui.Text(shown.DownTheLine ? "Setup looks clean" : "Setup checks out", "chip chip-green", Ui.Bold));
                    _review.Add(chips);
                }
                CoachVoice.Say(said[i]);
                while (CoachVoice.Speaking) yield return null;
                yield return new WaitForSecondsRealtime(0.4f);
            }
            DrawReview();
        }
    }
}

// What the eight Vertex sensor game worlds share (PendulumScene, WaltzScene, PureScene, CarbonCopyScene,
// SquareUpScene, StretchScene, LockdownScene, TheYipsScene): the stroke behind the putt read back from the view, a
// putter that replays that stroke in real time (its length, its timing, the face turning), a metronome that clicks on
// the audio clock, and a readout that rides on the camera.
//
// Units: inches. The ball rolls toward +Z (the target), the putter goes back toward -Z. Face change + is closed
// (the face points left of the target for a right-handed Player).
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuttrArcade
{
    /// <summary>The stroke behind the last putt, as the app's sensor games describe it (Views/SensorViews.cs).</summary>
    public struct StrokeData
    {
        public bool Has, Sim;
        public float Tempo, Rhythm, Face, Lie, LenIn, Speed, Back, Fwd, BackRot, FwdRot;
    }

    public abstract class SensorStage : RuleScene
    {
        protected static bool Portrait => Screen.height > Screen.width;
        public static readonly Color ScoreGreen = Make.Hex(0x4CD964), ScoreAmber = Make.Hex(0xFFB300), ScoreRed = Make.Hex(0xFF5252);
        /// <summary>Vertex's colours for a 0 to 100 score: 75+ green, 40 to 74 amber, under 40 red.</summary>
        public static Color ScoreColour(float score) => score >= 75 ? ScoreGreen : score >= 40 ? ScoreAmber : ScoreRed;

        public static StrokeData StrokeOf(ShotView v) => new StrokeData
        {
            Has = v.Flag("has"), Sim = v.Flag("sim"),
            Tempo = v.Num("tempo"), Rhythm = v.Num("rhythm"), Face = v.Num("face"), Lie = v.Num("lie"), LenIn = v.Num("lenIn"),
            Speed = v.Num("speed"), Back = v.Num("back"), Fwd = v.Num("fwd"), BackRot = v.Num("backRot"), FwdRot = v.Num("fwdRot"),
        };
        public static float[] Floats(ShotView v, string key) => v.Get<float[]>(key) ?? new float[0];
        public static int[] Ints(ShotView v, string key) => v.Get<int[]>(key) ?? new int[0];
        public static bool[] Flags(ShotView v, string key) => v.Get<bool[]>(key) ?? new bool[0];
        public static string[] Strings(ShotView v, string key) => v.Get<string[]>(key) ?? new string[0];
        public static string F1(float x) => x.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        public static string Signed(float x, string f = "0.0") => (x > 0 ? "+" : "") + x.ToString(f, System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The stage camera (the one with the long far plane), or null before it is up.</summary>
        protected Camera Eye()
        {
            if (_eye != null && _eye.isActiveAndEnabled) return _eye;
            _eye = null;
            foreach (Camera c in Camera.allCameras) if (c.farClipPlane >= 8999f) { _eye = c; break; }
            return _eye;
        }
        Camera _eye;

        /// <summary>Built behind the app's setup page (no clicks there) rather than in a game.</summary>
        protected bool Backdrop => Arcade != null && Arcade.InBackdrop;

        protected static void NoShadow(GameObject go) { MeshRenderer r = go.GetComponent<MeshRenderer>(); if (r != null) r.shadowCastingMode = ShadowCastingMode.Off; }
        protected static GameObject Cylinder(string name, Transform parent, Vector3 at, float radius, float height, Material m) =>
            Shape(PrimitiveType.Cylinder, name, parent, at, new Vector3(radius * 2f, height / 2f, radius * 2f), m);

        // ── The putter ────────────────────────────────────────────────────────

        /// <summary>A mallet putter hanging from a pivot (the shoulders): rotate Pivot about X to swing it, turn Head about Y for the face.</summary>
        protected sealed class Putter
        {
            public Transform Pivot, Head, Face;
            public float Arm;        // pivot to the sole, inches
            public float FaceScale = 1f;  // turn the face this many times its real angle (1 to 2° is hard to see)
            public float Angle;      // the swing now, degrees (+ back)
            public float Scale = 1f; // the head's size against a real one
        }

        /// <param name="at">Where the sole of the head rests at address (local to parent).</param>
        /// <param name="arm">Pivot to sole, inches: about 38 for a real stroke's arc.</param>
        protected Putter BuildPutter(Transform parent, Vector3 at, Color accent, float arm = 38f, float scale = 1f)
        {
            var p = new Putter { Arm = arm * scale, Scale = scale };
            p.Pivot = new GameObject("Putter").transform;
            p.Pivot.SetParent(parent, false);
            p.Pivot.localPosition = at + Vector3.up * p.Arm;
            var hang = new GameObject("Hang").transform;   // the head's spot under the pivot
            hang.SetParent(p.Pivot, false);
            hang.localPosition = Vector3.down * p.Arm;
            p.Head = new GameObject("Head").transform;
            p.Head.SetParent(hang, false);
            p.Head.localScale = Vector3.one * scale;

            Material body = Make.Lit(new Color(0.16f, 0.17f, 0.19f), 0.82f, 0.85f);
            Material chrome = Make.Lit(new Color(0.78f, 0.8f, 0.83f), 0.92f, 1f);
            Material grip = Make.Lit(new Color(0.06f, 0.06f, 0.07f), 0.35f);
            // A mallet: a rounded body, wings behind, a sight line, a bright face insert.
            GameObject head = Make.MeshObject("Mallet", p.Head, Make.RoundedBox(new Vector3(4.4f, 1.0f, 2.2f), 0.45f), body);
            head.transform.localPosition = new Vector3(0, 0.55f, -0.2f);
            for (int s = -1; s <= 1; s += 2)
            {
                GameObject wing = Make.MeshObject("Wing", p.Head, Make.RoundedBox(new Vector3(0.9f, 0.8f, 2.6f), 0.35f), body);
                wing.transform.localPosition = new Vector3(s * 1.75f, 0.45f, -1.9f);
            }
            GameObject sight = Shape(PrimitiveType.Cube, "Sight line", p.Head, new Vector3(0, 1.07f, -0.9f), new Vector3(0.12f, 0.03f, 3.4f), Make.Emissive(Color.white, accent * 1.6f));
            NoShadow(sight);
            p.Face = Shape(PrimitiveType.Cube, "Face", p.Head, new Vector3(0, 0.55f, 0.92f), new Vector3(3.9f, 0.82f, 0.08f), Make.Emissive(accent * 0.5f, accent * 1.2f, 0.8f)).transform;
            // The shaft from the heel to the pivot, then the grip.
            float shaft = p.Arm / scale - 1.2f;
            Cylinder("Hosel", p.Head, new Vector3(-0.2f, 1.4f, -0.2f), 0.17f, 0.9f, chrome);
            Cylinder("Shaft", p.Head, new Vector3(-0.2f, 1.6f + shaft * 0.36f, -0.2f), 0.2f, shaft * 0.72f, chrome);
            Cylinder("Grip", p.Head, new Vector3(-0.2f, 1.6f + shaft * 0.86f, -0.2f), 0.42f, shaft * 0.28f, grip);
            return p;
        }

        /// <summary>Pose the putter: swung (degrees, + back) and its face turned (degrees of face change, + closed).</summary>
        protected static void Pose(Putter p, float swingDeg, float faceDeg, float lieDeg = 0f)
        {
            p.Angle = swingDeg;
            p.Pivot.localRotation = Quaternion.Euler(swingDeg, 0, 0);
            p.Head.localRotation = Quaternion.Euler(0, -faceDeg * p.FaceScale, lieDeg);
        }

        /// <summary>The swing angle for a head this many inches back along the arc.</summary>
        protected static float AngleFor(Putter p, float inches) => Mathf.Asin(Mathf.Clamp(inches * p.Scale / p.Arm, -0.95f, 0.95f)) * Mathf.Rad2Deg;

        /// <summary>
        /// The stroke in real time: back to the top over its backstroke time (the face opening by the backstroke rotation),
        /// down to impact over its forward time, accelerating (the face arriving at its face change), through, then back
        /// to address. impact fires at the strike. slow stretches time (1 is real).
        /// </summary>
        protected IEnumerator Swing(Putter p, StrokeData s, Action impact = null, float slow = 1f, Action<float, int> each = null)
        {
            float len = s.LenIn > 0.5f ? s.LenIn : 10f;
            float back = Mathf.Clamp(s.Back > 0.05f ? s.Back : 0.55f, 0.2f, 2f) * slow, fwd = Mathf.Clamp(s.Fwd > 0.05f ? s.Fwd : 0.28f, 0.1f, 1.2f) * slow;
            float top = AngleFor(p, len), through = AngleFor(p, len * 0.85f);
            float backRot = s.BackRot, face = s.Face;
            // Back: away slowly, quickest in the middle, settling at the top.
            for (float t = 0f; t < 1f; t += Time.deltaTime / back)
            {
                float u = (1f - Mathf.Cos(t * Mathf.PI)) / 2f;
                Pose(p, top * u, backRot * u, s.Lie * u * 0.5f);
                each?.Invoke(t, 0);
                yield return null;
            }
            Pose(p, top, backRot, s.Lie * 0.5f);
            // Down: accelerating into the ball.
            for (float t = 0f; t < 1f; t += Time.deltaTime / fwd)
            {
                float u = Mathf.Cos(t * Mathf.PI / 2f);
                Pose(p, top * u, Mathf.Lerp(face, backRot, u), Mathf.Lerp(s.Lie, s.Lie * 0.5f, u));
                each?.Invoke(t, 1);
                yield return null;
            }
            Pose(p, 0f, face, s.Lie);
            impact?.Invoke();
            // Through, slowing.
            float follow = fwd * 1.1f;
            for (float t = 0f; t < 1f; t += Time.deltaTime / follow)
            {
                Pose(p, -through * Mathf.Sin(t * Mathf.PI / 2f), face, s.Lie);
                each?.Invoke(t, 2);
                yield return null;
            }
            Pose(p, -through, face, s.Lie);
        }

        /// <summary>Back to address, smoothly.</summary>
        protected IEnumerator Settle(Putter p, float seconds = 0.6f)
        {
            float from = p.Angle;
            Quaternion head = p.Head.localRotation;
            for (float t = 0f; t < 1f; t += Time.deltaTime / seconds)
            {
                float u = Ease(t);
                p.Angle = Mathf.Lerp(from, 0f, u);
                p.Pivot.localRotation = Quaternion.Euler(p.Angle, 0, 0);
                p.Head.localRotation = Quaternion.Slerp(head, Quaternion.identity, u);
                yield return null;
            }
            Pose(p, 0f, 0f);
        }

        /// <summary>The head's position now, local to the scene root.</summary>
        protected Vector3 HeadAt(Putter p) => Root.InverseTransformPoint(p.Head.position);

        // ── A ball ────────────────────────────────────────────────────────────

        protected Transform BuildBall(Transform parent, Vector3 at, float radius = 0.84f)
        {
            Make.Ball(out Texture2D bc, out Texture2D bn);
            Transform b = Shape(PrimitiveType.Sphere, "Ball", parent, at + Vector3.up * radius, Vector3.one * radius * 2f, Make.Lit(Color.white, 0.65f, 0f, bc, bn)).transform;
            return b;
        }

        /// <summary>Roll a ball from where it is along +Z (and a little sideways), slowing to a stop.</summary>
        protected static IEnumerator RollBall(Transform ball, float distance, float sideways, float seconds, float radius = 0.84f)
        {
            Vector3 from = ball.localPosition, to = from + new Vector3(sideways, 0, distance);
            Vector3 last = from;
            for (float t = 0f; t < 1f; t += Time.deltaTime / Mathf.Max(0.05f, seconds))
            {
                float u = 1f - (1f - t) * (1f - t);
                Vector3 p = Vector3.Lerp(from, to, u);
                Vector3 step = p - last;
                if (step.sqrMagnitude > 1e-6f) ball.localRotation = Quaternion.AngleAxis(step.magnitude / radius * Mathf.Rad2Deg, Vector3.Cross(Vector3.up, step.normalized)) * ball.localRotation;
                ball.localPosition = p;
                last = p;
                yield return null;
            }
            ball.localPosition = to;
        }

        // ── The metronome ─────────────────────────────────────────────────────

        /// <summary>
        /// Clicks on the audio clock (scheduled ahead, so the frame rate never shifts a beat) and tells the scene when each
        /// click sounds. Even beats (Bpm), or a repeating pattern of gaps in seconds (Waltz: back, through, rest). Lives
        /// under the scene's root: it stops when the scene goes. Silent behind the setup page.
        /// </summary>
        protected sealed class Ticker
        {
            readonly AudioSource[] _src = new AudioSource[4];
            readonly AudioClip _clip;
            readonly Queue<(double at, int step)> _due = new Queue<(double, int)>();
            int _next;
            double _at;
            float[] _gaps;
            int _step;
            public bool Running, Muted;
            public float Volume = 0.8f;
            /// <summary>Pitch for each step of the pattern (an accent on the first, say).</summary>
            public float[] Pitches;
            /// <summary>The step of the click that sounded last, and the audio time it sounded at.</summary>
            public int LastStep = -1;
            public double LastAt = -10;
            public event Action<int> Beat;

            public Ticker(Transform root)
            {
                _clip = Resources.Load<AudioClip>("PuttrApp/Sounds/effects_metronome");
                var go = new GameObject("Metronome");
                go.transform.SetParent(root, false);
                for (int i = 0; i < _src.Length; i++) { _src[i] = go.AddComponent<AudioSource>(); _src[i].playOnAwake = false; _src[i].spatialBlend = 0f; }
            }

            public static double Now => AudioSettings.dspTime;
            /// <summary>Seconds since the last click.</summary>
            public float Since => LastAt < 0 ? 99f : (float)(Now - LastAt);
            /// <summary>The gap from the last click to the next.</summary>
            public float Gap => _gaps == null || _gaps.Length == 0 ? 1f : _gaps[(LastStep + _gaps.Length) % _gaps.Length];

            public void Start(float bpm) => Start(new[] { 60f / Mathf.Max(20f, bpm) });
            public void Start(float[] gaps)
            {
                bool same = Running && _gaps != null && gaps.Length == _gaps.Length;
                if (same) { for (int i = 0; i < gaps.Length; i++) if (Mathf.Abs(gaps[i] - _gaps[i]) > 0.001f) { same = false; break; } }
                if (same) return;
                _gaps = gaps;
                _step = 0;
                _due.Clear();
                foreach (AudioSource s in _src) if (s != null) s.Stop();
                _at = Now + 0.25;
                Running = true;
            }
            public void Stop() { Running = false; _due.Clear(); foreach (AudioSource s in _src) if (s != null) s.Stop(); }

            /// <summary>Call every frame (from the scene's Tick).</summary>
            public void Tick()
            {
                if (!Running || _gaps == null) return;
                double now = Now;
                if (_at < now - 0.5) _at = now + 0.05;   // fell behind (a pause): start again from now
                while (_at < now + 0.12)
                {
                    if (!Muted && _clip != null)
                    {
                        AudioSource s = _src[_next++ % _src.Length];
                        if (s != null)
                        {
                            s.clip = _clip;
                            s.volume = Volume;
                            s.pitch = Pitches != null && Pitches.Length > 0 ? Pitches[_step % Pitches.Length] : 1f;
                            s.PlayScheduled(_at);
                        }
                    }
                    _due.Enqueue((_at, _step));
                    _at += Mathf.Max(0.05f, _gaps[_step % _gaps.Length]);
                    _step = (_step + 1) % _gaps.Length;
                }
                while (_due.Count > 0 && _due.Peek().at <= now)
                {
                    (double at, int step) b = _due.Dequeue();
                    LastAt = b.at; LastStep = b.step;
                    Beat?.Invoke(b.step);
                }
            }
        }

        // ── A readout on the camera ───────────────────────────────────────────

        /// <summary>
        /// A dark plate with a kick line, a big number, its unit and a line under it, riding on the stage camera at a spot
        /// given in halves of the view (x -1 left to 1 right, y -1 bottom to 1 top). It pops in when shown.
        /// </summary>
        protected sealed class Readout
        {
            public readonly Transform Rig, Block;
            readonly Transform _plate;
            public readonly TextMesh Kick, Big, Unit, Line;
            public Vector2 Spot;
            public float Size = 1f;
            float _pop = -1f;

            public Readout(SensorStage scene, Vector2 spot, Color kick)
            {
                Spot = spot;
                Rig = new GameObject("Readout").transform;
                Rig.SetParent(scene.Root, false);
                Block = new GameObject("Block").transform;
                Block.SetParent(Rig, false);
                Material plate = Make.Glow(new Color(0.015f, 0.02f, 0.03f, 0.74f), false);
                plate.renderQueue = 3600;
                _plate = Make.MeshObject("Plate", Block, Plate(), plate).transform;
                _plate.GetComponent<MeshRenderer>().sortingOrder = 20;
                Kick = Words(Block, "", new Vector3(0, 0.15f, -0.01f), 0.05f, kick);
                Big = Words(Block, "", new Vector3(0.04f, -0.01f, -0.01f), 0.17f, Color.white, TextAnchor.MiddleRight, 160);
                Unit = Words(Block, "", new Vector3(0.07f, -0.03f, -0.01f), 0.06f, new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleLeft);
                Line = Words(Block, "", new Vector3(0, -0.15f, -0.01f), 0.042f, new Color(1f, 1f, 1f, 0.85f));
                foreach (TextMesh t in new[] { Kick, Big, Unit, Line })
                {
                    MeshRenderer m = t.GetComponent<MeshRenderer>();
                    m.sharedMaterial = new Material(m.sharedMaterial) { renderQueue = 3700 };
                    m.sortingOrder = 30;
                }
                Rig.gameObject.SetActive(false);
            }

            static Mesh Plate()
            {
                // A unit rounded rectangle (scaled to fit the words).
                var pts = new List<Vector3>();
                const float r = 0.08f;
                for (int c = 0; c < 4; c++)
                {
                    Vector2 o = new Vector2(c == 0 || c == 3 ? 0.5f - r : -0.5f + r, c < 2 ? 0.5f - r : -0.5f + r);
                    for (int i = 0; i <= 6; i++)
                    {
                        float a = (c * 90f + i * 15f) * Mathf.Deg2Rad;
                        pts.Add(new Vector3(o.x + Mathf.Cos(a) * r, o.y + Mathf.Sin(a) * r, 0f));
                    }
                }
                var verts = new List<Vector3> { Vector3.zero };
                verts.AddRange(pts);
                var tris = new List<int>();
                for (int i = 0; i < pts.Count; i++) { tris.Add(0); tris.Add(1 + (i + 1) % pts.Count); tris.Add(1 + i); }
                var mesh = new Mesh { name = "Plate" };
                mesh.SetVertices(verts);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateNormals();
                return mesh;
            }

            public void Show(string kick, string big, string unit, string line, Color colour)
            {
                Kick.text = kick; Big.text = big; Unit.text = unit; Line.text = line ?? "";
                Big.color = colour;
                // Centre the number and its unit together.
                float bigW = big.Length * 0.1f, unitW = unit.Length * 0.036f;
                float left = -(bigW + 0.02f + unitW) / 2f;
                Big.transform.localPosition = new Vector3(left + bigW, -0.01f, -0.01f);
                Unit.transform.localPosition = new Vector3(left + bigW + 0.02f, -0.035f, -0.01f);
                bool third = Line.text.Length > 0;
                float w = Mathf.Max(0.42f, Mathf.Max(Mathf.Max(Line.text.Length * 0.026f, kick.Length * 0.036f), bigW + unitW + 0.04f) + 0.1f);
                _plate.localScale = new Vector3(w, third ? 0.43f : 0.34f, 1f);
                _plate.localPosition = new Vector3(0, third ? 0f : 0.045f, 0f);
                Rig.gameObject.SetActive(true);
                Block.localScale = Vector3.one * 0.6f;
                _pop = 0f;
            }
            public void Hide() => Rig.gameObject.SetActive(false);

            public void Tick(Camera eye, Transform root)
            {
                if (eye == null || !Rig.gameObject.activeSelf) return;
                const float dist = 40f;
                float half = dist * Mathf.Tan(eye.fieldOfView * 0.5f * Mathf.Deg2Rad);
                Transform cam = eye.transform;
                Rig.position = cam.position + cam.forward * dist + cam.right * (Spot.x * eye.aspect * half) + cam.up * (Spot.y * half);
                Rig.rotation = cam.rotation;
                float s = root.lossyScale.x > 0f ? root.lossyScale.x : 1f;
                Rig.localScale = Vector3.one * (half / s) * Size;
                if (_pop >= 0f)
                {
                    _pop += Time.deltaTime / 0.35f;
                    float t = Mathf.Min(1f, _pop);
                    Block.localScale = Vector3.one * (t < 0.7f ? Mathf.Lerp(0.6f, 1.08f, Ease(t / 0.7f)) : Mathf.Lerp(1.08f, 1f, (t - 0.7f) / 0.3f));
                    if (t >= 1f) _pop = -1f;
                }
            }
        }

        /// <summary>Words that always face the camera (labels on props).</summary>
        protected void Face(Transform words)
        {
            // Toward the scene's own camera spot (the stage camera sits there in a game; the card pass moves its camera
            // there only after the first frame, and the setup page's swing round it is small).
            if (words == null) return;
            Vector3 from = CameraFrom;
            if ((words.position - from).sqrMagnitude > 1e-4f) words.rotation = Quaternion.LookRotation(words.position - from, Vector3.up);
        }
    }
}

// Around the Clock, up close on the hole (Matt, 2026-11-23: the clock is the hole itself, since the game measures
// where the ball enters it; drawn from Center Cut's viewpoint, see HoleView). The mat's real 4.25 inch cup is the
// clock's face: a brass bezel on its lip, the hours and minutes round it. The near half of the rim (nine o'clock
// round through six to three) is cut into the parts of the cup: the parts the putter has collected glow gold, the
// rest are dark brass, and a gold arrow points in at the part needed next. The putt rolls in and drops through the
// part it entered by: a new part strikes and lights up, one already held flickers, a miss rolls on by.
// Units: inches x K round the cup (at the origin); the golfer is down -Z, twelve o'clock is up +Z.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuttrArcade
{
    public sealed class ClockScene : RuleScene
    {
        const float K = 5f, CupR = 2.125f * K, BallR = 0.84f * K, Reach = 250f;
        const float WedgeIn = CupR + 0.3f * K, WedgeOut = CupR + 1.9f * K, HoursR = WedgeOut + 1.35f * K;
        static readonly string[] Hours = { "XII", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI" };

        readonly List<Renderer> _wedges = new List<Renderer>();
        readonly List<TextMesh> _labels = new List<TextMesh>();
        Transform _wedgeRoot, _hand, _pointer, _ball, _tee;
        Material _nextGlow;
        LineRenderer _trail;
        Material _gold, _brass, _flash, _next;
        int _n, _need = -1;
        float _handDeg, _handTo, _teeX, _teeTo;
        bool[] _got = new bool[0];

        public override Vector3 CameraFrom => At(HoleView.From(CupR));
        public override Vector3 CameraLook => At(HoleView.Look(CupR));
        public override float CameraFov => HoleView.Fov;
        public override float CardLens => 54f;
        public override float ShadowDistance => 500f;

        public override void Build()
        {
            _gold = Make.Emissive(new Color(1f, 0.78f, 0.3f), new Color(1.5f, 0.95f, 0.25f), 0.8f);
            _brass = Make.Lit(new Color(0.3f, 0.23f, 0.13f), 0.55f, 0.6f);
            _next = _nextGlow = Make.Emissive(new Color(0.5f, 0.38f, 0.18f), new Color(0.5f, 0.32f, 0.08f), 0.6f);
            _flash = Make.Emissive(new Color(1f, 0.95f, 0.8f), new Color(5f, 4f, 2f), 0.8f);
            Material rim = Make.Lit(new Color(0.85f, 0.62f, 0.25f), 0.75f, 0.85f);
            Material mark = Make.Emissive(new Color(0.9f, 0.72f, 0.36f), new Color(0.5f, 0.36f, 0.12f), 0.6f);

            // The study's wood floor round the mat, the mat and its real hole.
            HoleView.Floor(Root, CupR, -3.2f, Make.Lit(new Color(0.2f, 0.11f, 0.06f), 0.35f));
            HoleView.Build(Root, CupR, new Color(0.05f, 0.16f, 0.08f));
            Shape(PrimitiveType.Cube, "Skirting", Root, new Vector3(0, 3f, 7f * K + 7f), new Vector3(24f * K + 10f, 12f, 14f), Make.Lit(new Color(0.15f, 0.08f, 0.045f), 0.35f));

            // The clock is the hole: a brass bezel on the lip, another outside the parts, the minutes and the hours round it.
            Hoop("Bezel", Root, new Vector3(0, 0.14f, 0), CupR, CupR + 0.22f * K, rim);
            Hoop("Outer bezel", Root, new Vector3(0, 0.14f, 0), WedgeOut + 0.08f * K, WedgeOut + 0.26f * K, rim);
            for (int h = 0; h < 12; h++)
            {
                float a = h * 30f * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                TextMesh t = Words(Root, Hours[h], dir * HoursR + Vector3.up * 0.2f, 0.62f * K, new Color(0.95f, 0.8f, 0.48f));
                t.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            }
            for (int m = 0; m < 60; m++)
            {
                float a = m * 6f;
                bool hour = m % 5 == 0;
                GameObject tick = Shape(PrimitiveType.Cube, "Tick", Root, Quaternion.Euler(0, a, 0) * new Vector3(0, 0.12f, WedgeOut + (hour ? 0.56f : 0.48f) * K), new Vector3((hour ? 0.14f : 0.06f) * K, 0.1f, (hour ? 0.42f : 0.26f) * K), mark);
                tick.transform.localRotation = Quaternion.Euler(0, a, 0);
                tick.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }

            _wedgeRoot = new GameObject("Parts").transform;
            _wedgeRoot.SetParent(Root, false);
            _wedgeRoot.localPosition = new Vector3(0, 0.1f, 0);

            // The hand: a gold arrow outside the parts, pointing in at the part needed next.
            _hand = new GameObject("Hand").transform;
            _hand.SetParent(Root, false);
            _hand.localPosition = new Vector3(0, 0.3f, 0);
            _pointer = new GameObject("Pointer").transform;
            _pointer.SetParent(_hand, false);
            GameObject arrow = Make.MeshObject("Arrow", _pointer, Arrow(), Make.Emissive(new Color(1f, 0.75f, 0.2f), new Color(2.4f, 1.4f, 0.3f)));
            arrow.transform.localPosition = new Vector3(0, 0f, -(WedgeOut + 0.72f * K));
            arrow.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            _tee = Shape(PrimitiveType.Cube, "Tee", Root, new Vector3(0, 0.4f, -Reach + 20f), new Vector3(30f, 0.8f, 12f), Make.Lit(new Color(0.85f, 0.8f, 0.7f), 0.5f)).transform;
            Make.Ball(out Texture2D bc, out Texture2D bn);
            _ball = Shape(PrimitiveType.Sphere, "Ball", Root, Vector3.zero, Vector3.one * BallR * 2f, Make.Lit(Color.white, 0.6f, 0f, bc, bn)).transform;
            _ball.gameObject.SetActive(false);
            _trail = Stroke(Root, "Trail", new Color(1f, 0.85f, 0.5f, 0.6f), 0.9f);

            Sun(Root, new Vector3(55f, 10f, 0f), new Color(1f, 0.85f, 0.65f), 0.75f);
            Lamp(Root, new Vector3(0, 450f, -150f), new Vector3(0, 0, 0), new Color(1f, 0.88f, 0.7f), 380000f, 45f, 900f);
            Lamp(Root, new Vector3(0, 380f, 480f), new Vector3(0, 0, -40f), new Color(0.55f, 0.65f, 1f), 250000f, 45f, 900f);
        }

        // A flat triangle pointing +Z (toward the cup when the hand points away from it).
        static Mesh Arrow()
        {
            var mesh = new Mesh { name = "Arrow" };
            mesh.SetVertices(new List<Vector3> { new Vector3(0, 0, 0.42f * K), new Vector3(0.36f * K, 0, -0.3f * K), new Vector3(-0.36f * K, 0, -0.3f * K) });
            mesh.SetTriangles(new List<int> { 0, 1, 2 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>The parts cover this many degrees either side of straight in: the front 150 of the cup.</summary>
        public const float HalfArc = 75f;
        float Centre(int s) => -HalfArc + (s + 0.5f) * 2f * HalfArc / Mathf.Max(1, _n);

        void Lay(int n)
        {
            if (n == _n && _wedges.Count == n) return;
            _n = n;
            for (int i = _wedgeRoot.childCount - 1; i >= 0; i--) Object.Destroy(_wedgeRoot.GetChild(i).gameObject);
            _wedges.Clear();
            _labels.Clear();
            float w = 2f * HalfArc / n, gap = n >= 9 ? 1.2f : 1.8f;
            for (int s = 0; s < n; s++)
            {
                float from = -HalfArc + s * w + gap / 2f, to = -HalfArc + (s + 1) * w - gap / 2f;
                GameObject wedge = Make.MeshObject("Part " + (s + 1), _wedgeRoot, Sector(WedgeIn, WedgeOut, from, to), _brass);
                wedge.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                _wedges.Add(wedge.GetComponent<Renderer>());
                TextMesh label = Words(_wedgeRoot, (s + 1).ToString(), Rim(Centre(s), (WedgeIn + WedgeOut) / 2f) + Vector3.up * 0.12f, (n >= 9 ? 0.55f : 0.8f) * K, Color.white);
                label.transform.localRotation = Quaternion.Euler(90f, 0, 0);
                _labels.Add(label);
            }
        }

        void Show(bool[] got, int need)
        {
            _got = got ?? new bool[_n];
            _need = need;
            for (int s = 0; s < _wedges.Count; s++)
            {
                bool have = s < _got.Length && _got[s];
                _wedges[s].sharedMaterial = have ? _gold : s == need ? _next : _brass;
                _labels[s].color = have ? new Color(0.35f, 0.2f, 0.02f) : s == need ? new Color(1f, 0.85f, 0.45f) : new Color(0.8f, 0.7f, 0.5f);
            }
            _pointer.gameObject.SetActive(need >= 0);
            if (need >= 0) _handTo = Centre(need);
        }

        void Set(ShotView v)
        {
            Lay(Mathf.Max(1, v.Int("sectors", 5)));
            Show(v.Get<bool[]>("got"), v.Over ? -1 : v.Int("next", -1));
            _teeTo = TeeX(v.NextTee);
        }

        static float TeeX(int tee) => tee == 1 ? -8f * K : tee == 3 ? 8f * K : 0f;

        public override void Begin(ShotView start) { Set(start); _handDeg = _handTo; _teeX = _teeTo; }
        public override void Ready(ShotView v) { Set(v); }

        public override void Tick()
        {
            _handDeg = Mathf.LerpAngle(_handDeg, _handTo, Time.deltaTime * 4f);
            // The hand points out from the centre toward the part: at 0 it points at the golfer (six o'clock).
            _hand.localRotation = Quaternion.Euler(0, -_handDeg, 0);
            float beat = Mathf.Abs(Mathf.Sin(Time.time * 3f));
            _pointer.localPosition = new Vector3(0, 0, -beat * 0.3f * K);
            // The part needed next breathes, so the eye goes to it.
            if (_nextGlow != null) _nextGlow.SetColor("_EmissionColor", new Color(0.5f, 0.32f, 0.08f) * (0.6f + 1.4f * beat));
            _teeX = Mathf.Lerp(_teeX, _teeTo, Time.deltaTime * 3f);
            _tee.localPosition = new Vector3(_teeX, 0.4f, -Reach + 20f);
        }

        public override IEnumerator Play(ShotView v)
        {
            Vector3 start = new Vector3(TeeX(v.Tee), 0, -Reach);
            bool inside = v.Made && v.HasEntry && Mathf.Abs(v.EntryDeg) <= HalfArc;
            List<Vector3> pts;
            Vector3 rim = Vector3.zero;
            if (v.Made)
            {
                float deg = v.HasEntry ? v.EntryDeg : 0f;
                if (!inside) deg = v.HasEntry ? Mathf.Sign(v.EntryDeg) * Mathf.Max(HalfArc + 8f, Mathf.Abs(v.EntryDeg)) : 180f;
                rim = Rim(deg, CupR - BallR * 0.4f);
                Vector3 outward = Rim(Mathf.Clamp(deg, -115f, 115f), 1f);
                pts = Bezier(start, Vector3.Lerp(start, outward * 160f, 0.5f), outward * 110f, rim);
            }
            else
            {
                float endX = Mathf.Clamp(v.LineIn * K, -110f, 110f);
                if (Mathf.Abs(endX) < CupR + BallR) endX = (v.MissSide == "LEFT" ? -1f : 1f) * (CupR + BallR + 3f);
                float endZ = v.HasRollout ? Mathf.Clamp(v.RolloutIn * K, -120f, 140f) : 70f;
                pts = Track(v, K, Reach, start, new Vector3(endX, 0, endZ));
            }
            Lift(pts, BallR);
            _ball.gameObject.SetActive(true);
            _ball.localPosition = pts[0];
            yield return Roll(_ball, pts, 1.6f, _trail, BallR);

            if (v.Made)
            {
                Sound("clock_make", "cup_plop", 0.7f);   // puttr-app-2: the game's own sound, the shared one until recorded
                yield return Drop(_ball, rim, BallR);
                if (inside && _n > 0)
                {
                    int s = Mathf.Clamp(Mathf.FloorToInt((v.EntryDeg + HalfArc) / (2f * HalfArc / _n)), 0, _n - 1);
                    bool fresh = v.Points > 0;
                    Renderer w = _wedges[s];
                    Vector3 at = At(Rim(Centre(s), (WedgeIn + WedgeOut) / 2f) + Vector3.up * 2f);
                    if (fresh)
                    {
                        w.sharedMaterial = _flash;
                        Arcade.Sparks(at, 36);
                        if (v.Title == "CLOCK DONE!") Arcade.Celebrate(40, At(0, 8f, 0));
                        yield return new WaitForSeconds(0.45f);
                        w.sharedMaterial = _gold;
                        _labels[s].color = new Color(0.35f, 0.2f, 0.02f);
                        yield return new WaitForSeconds(0.35f);
                    }
                    else
                    {
                        Material keep = w.sharedMaterial;
                        for (int k = 0; k < 3; k++) { w.sharedMaterial = _brass; yield return new WaitForSeconds(0.1f); w.sharedMaterial = keep; yield return new WaitForSeconds(0.1f); }
                        yield return new WaitForSeconds(0.2f);
                    }
                }
                else yield return new WaitForSeconds(0.5f);
            }
            else yield return new WaitForSeconds(0.7f);
            _ball.gameObject.SetActive(false);
            _trail.positionCount = 0;
        }

        // ── Shapes and rolling ───────────────────────────────────────────────

        static Vector3 Rim(float deg, float r) { float a = deg * Mathf.Deg2Rad; return new Vector3(Mathf.Sin(a) * r, 0f, -Mathf.Cos(a) * r); }

        static Mesh Sector(float inner, float outer, float fromDeg, float toDeg, int seg = 32)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                Vector3 d = Rim(Mathf.Lerp(fromDeg, toDeg, i / (float)seg), 1f);
                verts.Add(d * inner); verts.Add(d * outer);
            }
            for (int i = 0; i < seg; i++)
            {
                int b = i * 2;
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b + 2); tris.Add(b + 3); tris.Add(b + 1);
            }
            var mesh = new Mesh { name = "Sector" };
            mesh.SetVertices(verts); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        static List<Vector3> Bezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int n = 40)
        {
            var pts = new List<Vector3>();
            for (int i = 0; i <= n; i++) { float t = i / (float)n, u = 1f - t; pts.Add(u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d); }
            return pts;
        }

        static void Lift(List<Vector3> pts, float y) { for (int i = 0; i < pts.Count; i++) pts[i] = new Vector3(pts[i].x, y, pts[i].z); }

        static List<Vector3> Track(ShotView v, float k, float reach, Vector3 start, Vector3 end)
        {
            var pts = new List<Vector3>();
            foreach (Vector2 p in v.Path) if (p.y * k >= -reach) pts.Add(new Vector3(Mathf.Clamp(p.x * k, -reach, reach), 0f, Mathf.Min(p.y * k, 150f)));
            if (pts.Count < 4) return Bezier(start, Vector3.Lerp(start, end, 0.4f) + Vector3.right * (start.x * -0.2f), Vector3.Lerp(start, end, 0.75f), end);
            if (pts[0].z > -reach + 2f) pts.Insert(0, new Vector3(pts[0].x, 0f, -reach));
            return pts;
        }

        static IEnumerator Roll(Transform ball, List<Vector3> pts, float seconds, LineRenderer trail, float r)
        {
            int n = pts.Count;
            if (n < 2) yield break;
            var cum = new float[n];
            for (int i = 1; i < n; i++) cum[i] = cum[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
            float total = Mathf.Max(0.001f, cum[n - 1]);
            var drawn = new List<Vector3> { pts[0] };
            int seg = 0;
            Vector3 last = pts[0];
            for (float t = 0f; ; t += Time.deltaTime / seconds)
            {
                float tt = Mathf.Min(1f, t);
                float d = (1f - (1f - tt) * (1f - tt)) * total;
                while (seg < n - 2 && cum[seg + 1] < d) { seg++; drawn.Add(pts[seg]); }
                Vector3 p = Vector3.Lerp(pts[seg], pts[seg + 1], Mathf.InverseLerp(cum[seg], cum[seg + 1], d));
                Vector3 step = p - last;
                if (step.sqrMagnitude > 1e-6f) ball.localRotation = Quaternion.AngleAxis(step.magnitude / r * Mathf.Rad2Deg, Vector3.Cross(Vector3.up, step.normalized)) * ball.localRotation;
                ball.localPosition = p;
                last = p;
                if (trail != null)
                {
                    trail.positionCount = drawn.Count + 1;
                    for (int i = 0; i < drawn.Count; i++) trail.SetPosition(i, drawn[i] - Vector3.up * (r - 0.6f));
                    trail.SetPosition(drawn.Count, p - Vector3.up * (r - 0.6f));
                }
                if (t >= 1f) break;
                yield return null;
            }
        }

        static IEnumerator Drop(Transform ball, Vector3 rim, float r)
        {
            Vector3 from = ball.localPosition;
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.3f)
            {
                Vector3 p = Vector3.Lerp(from, new Vector3(rim.x * 0.2f, rim.y, rim.z * 0.2f), t);
                p.y = from.y - t * t * r * 3.5f;
                ball.localPosition = p;
                yield return null;
            }
            ball.localPosition = new Vector3(0, -r * 4f, 0);
        }
    }
}

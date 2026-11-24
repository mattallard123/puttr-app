// Assessment as an exam floor seen from above: the green runs up to the cup with the twelve set stations marked on it
// at their distance and tee (4 to 11 ft, left, centre, right), each with its own chalk ring and label. The station
// that's up glows. The putt rolls from it to the cup in a gold line, then the station's grade plaque stands up out
// of the floor, coloured by the grade (A lime, B green, C amber, D red, F dark). The plaques stay up as the exam
// goes on, so the floor becomes the read. Units are inches; the cup is at the origin. Tee offsets are drawn wider
// than life (XS) so the three columns of stations read from above.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PuttrArcade
{
    public sealed class AssessmentScene : RuleScene
    {
        const float LaneW = 72f, BallR = 1.3f, CupR = 3f, XS = 2.4f;
        static readonly Color Gold = new Color(1f, 0.78f, 0.25f), Violet = new Color(0.62f, 0.55f, 1f);

        Transform _ball, _now;
        LineRenderer _trail;
        Material _nowMat;
        readonly Transform[] _plaques = new Transform[12];
        readonly TextMesh[] _letters = new TextMesh[12];
        readonly Renderer[] _frames = new Renderer[12];
        readonly string[] _shown = new string[12];
        int[] _feet = { 4, 4, 5, 6, 6, 7, 8, 8, 9, 10, 10, 11 }, _tees = { 1, 3, 2, 1, 3, 2, 1, 3, 2, 1, 3, 2 };

        static bool Portrait => Screen.height > Screen.width;
        public override float WideFrame => 0.74f;   // a lane down the middle: nothing of it is under the side cards
        public override Vector3 CameraFrom => At(Portrait ? new Vector3(8f, 196f, -212f) : new Vector3(-10f, 200f, -230f));
        public override Vector3 CameraLook => At(Portrait ? new Vector3(-8f, 6f, -60f) : new Vector3(-36f, 0f, -60f));
        public override float CameraFov => Portrait ? 46f : 40f;

        static Vector3 StationAt(int feet, int tee) => new Vector3((tee - 2) * 8f * XS, 0f, -feet * 12f);

        static Color GradeColor(string g)
        {
            if (string.IsNullOrEmpty(g)) return Color.white;
            switch (g[0])
            {
                case 'A': return new Color(0.78f, 1f, 0.3f);
                case 'B': return new Color(0.45f, 0.9f, 0.4f);
                case 'C': return new Color(1f, 0.72f, 0.2f);
                case 'D': return new Color(1f, 0.32f, 0.3f);
                default: return new Color(0.55f, 0.2f, 0.22f);
            }
        }

        public override void Build()
        {
            Make.Carpet(out Texture2D pile, out Texture2D pileNormal);
            Material turf = Make.Lit(new Color(0.13f, 0.44f, 0.22f), 0.15f, 0f, pile, pileNormal, 0.8f);
            turf.SetTextureScale("_BaseMap", new Vector2(5f, 12f));
            Material floor = Make.Lit(new Color(0.045f, 0.04f, 0.07f), 0.4f);
            Material hole = Make.Lit(new Color(0.01f, 0.01f, 0.01f), 0.1f);
            Material white = Make.Emissive(Color.white, new Color(0.7f, 0.7f, 0.7f));

            // The exam hall floor: dark, with a violet grid.
            Shape(PrimitiveType.Cube, "Floor", Root, new Vector3(0, -1.6f, 0f), new Vector3(1200f, 2f, 1200f), floor);
            Material grid = Make.Glow(new Color(Violet.r, Violet.g, Violet.b, 0.1f));
            for (int i = -14; i <= 14; i++)
            {
                LineRenderer a = Stroke(Root, "Grid", Color.white, 0.6f); a.sharedMaterial = grid;
                Points(a, new[] { new Vector3(i * 24f, 0.02f, -400f), new Vector3(i * 24f, 0.02f, 300f) });
                LineRenderer b = Stroke(Root, "Grid", Color.white, 0.6f); b.sharedMaterial = grid;
                Points(b, new[] { new Vector3(-400f, 0.02f, -60f + i * 24f), new Vector3(400f, 0.02f, -60f + i * 24f) });
            }

            // The green and the cup.
            Shape(PrimitiveType.Cube, "Green", Root, new Vector3(0, -0.5f, -66f), new Vector3(LaneW, 1f, 172f), turf);
            for (int s = -1; s <= 1; s += 2)
                Shape(PrimitiveType.Cube, "Edge", Root, new Vector3(s * (LaneW / 2f + 0.4f), 0.2f, -66f), new Vector3(0.8f, 0.6f, 172f), Make.Emissive(Color.black, Violet * 0.8f));
            Make.MeshObject("Cup", Root, Make.Disc(CupR, 48), hole).transform.localPosition = new Vector3(0, 0.03f, 0);
            Hoop("Lip", Root, new Vector3(0, 0.05f, 0), CupR, CupR + 0.5f, white);
            Hoop("Halo", Root, new Vector3(0, 0.06f, 0), CupR + 1.5f, CupR + 2.2f, Make.Emissive(Color.black, Gold * 1.5f));
            // Feet up the left side.
            for (int f = 3; f <= 11; f++)
                Words(Root, f.ToString(), new Vector3(-LaneW / 2f - 8f, 0.3f, -f * 12f), 5f, new Color(0.8f, 0.78f, 1f, 0.75f)).transform.localRotation = Quaternion.Euler(90f, 0, 0);

            // The stations: chalk rings and labels; a plaque each, lying flat until it's graded.
            Material chalk = Make.Glow(new Color(1f, 1f, 1f, 0.35f), false);
            Material post = Make.Lit(new Color(0.12f, 0.12f, 0.14f), 0.5f, 0.6f);
            Material face = Make.Lit(new Color(0.07f, 0.065f, 0.1f), 0.5f);
            for (int i = 0; i < 12; i++)
            {
                Vector3 at = StationAt(_feet[i], _tees[i]);
                for (int k = 0; k < 10; k++)
                {
                    GameObject dash = Make.MeshObject("Chalk", Root, Arc(5.2f, 6f, k * 36f, k * 36f + 22f), chalk);
                    dash.transform.localPosition = at + new Vector3(0, 0.05f, 0);
                }
                Words(Root, _feet[i] + (_tees[i] == 1 ? "L" : _tees[i] == 3 ? "R" : "C"), at + new Vector3(0, 0.1f, -9f), 3.2f, new Color(1f, 1f, 1f, 0.55f)).transform.localRotation = Quaternion.Euler(90f, 0, 0);

                var p = new GameObject("Plaque").transform;
                p.SetParent(Root, false);
                p.localPosition = at + new Vector3(_tees[i] == 1 ? -9f : _tees[i] == 3 ? 9f : 9f, 0, 3f);
                Shape(PrimitiveType.Cylinder, "Post", p, new Vector3(0, 7f, 0), new Vector3(1.2f, 7f, 1.2f), post);
                Shape(PrimitiveType.Cylinder, "Foot", p, new Vector3(0, 0.3f, 0), new Vector3(7f, 0.3f, 7f), post);
                var sign = new GameObject("Sign").transform;
                sign.SetParent(p, false);
                sign.localPosition = new Vector3(0, 19f, 0);
                sign.localRotation = Quaternion.Euler(28f, 0, 0);
                GameObject frame = Shape(PrimitiveType.Cube, "Frame", sign, Vector3.zero, new Vector3(17f, 14f, 1.2f), Make.Emissive(Color.black, Color.white));
                Shape(PrimitiveType.Cube, "Face", sign, new Vector3(0, 0, -0.4f), new Vector3(15.4f, 12.4f, 0.6f), face);
                _letters[i] = Words(sign, "", new Vector3(0, 0, -0.8f), 8.5f, Color.white);
                _frames[i] = frame.GetComponent<Renderer>();
                _plaques[i] = p;
                p.gameObject.SetActive(false);
            }

            // The station that's up.
            _nowMat = Make.Emissive(Color.black, Gold * 2f);
            _now = Hoop("Now", Root, Vector3.zero, 6.6f, 7.8f, _nowMat).transform;

            _ball = Shape(PrimitiveType.Sphere, "Ball", Root, Vector3.zero, Vector3.one * BallR * 2f, Make.Emissive(Color.white, new Color(0.3f, 0.3f, 0.3f), 0.7f)).transform;
            _trail = Stroke(Root, "Roll", Gold, 2.4f);

            Sun(Root, new Vector3(62f, 20f, 0f), new Color(0.95f, 0.92f, 1f), 0.35f);
            Lamp(Root, new Vector3(0, 320f, -150f), new Vector3(0, 0, -60f), new Color(1f, 0.97f, 0.92f), 380000f, 50f, 900f);
            Lamp(Root, new Vector3(-200f, 160f, 40f), new Vector3(0, 0, -60f), new Color(0.65f, 0.6f, 1f), 300000f, 45f, 900f);
        }

        // A dash of a ring, flat in XZ, from degrees a0 to a1.
        static Mesh Arc(float inner, float outer, float a0, float a1)
        {
            const int n = 6;
            var verts = new Vector3[(n + 1) * 2];
            var tris = new int[n * 6];
            for (int s = 0; s <= n; s++)
            {
                float a = Mathf.Lerp(a0, a1, s / (float)n) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                verts[s * 2] = dir * inner; verts[s * 2 + 1] = dir * outer;
            }
            for (int s = 0; s < n; s++)
            {
                int a = s * 2, t = s * 6;
                tris[t] = a; tris[t + 1] = a + 3; tris[t + 2] = a + 1;
                tris[t + 3] = a; tris[t + 4] = a + 2; tris[t + 5] = a + 3;
            }
            var m = new Mesh { vertices = verts, triangles = tris };
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        void Grade(int i, string g, float upright)
        {
            Transform p = _plaques[i];
            p.gameObject.SetActive(upright > 0.001f);
            Color c = GradeColor(g);
            _letters[i].text = g;
            _letters[i].color = c;
            _frames[i].sharedMaterial.SetColor("_EmissionColor", c * 1.6f);
            // Lying flat (back) to standing, with a little overshoot.
            float k = upright >= 1f ? 1f : 1f - Mathf.Pow(1f - upright, 3f) + Mathf.Sin(upright * Mathf.PI) * 0.12f;
            p.localRotation = Quaternion.Euler(Mathf.LerpUnclamped(-90f, 0f, k), 0, 0);
            p.localScale = Vector3.one * Mathf.Clamp01(upright * 3f);
            _shown[i] = upright >= 1f ? g : null;
        }

        void Show(ShotView v, int skip)
        {
            int[] feet = v.Get<int[]>("stationFeet"), tees = v.Get<int[]>("stationTee");
            if (feet != null && tees != null && feet.Length == 12) { _feet = feet; _tees = tees; }
            string[] grades = v.Get<string[]>("grades") ?? new string[12];
            for (int i = 0; i < 12; i++)
            {
                if (i == skip) continue;
                string g = i < grades.Length ? grades[i] : null;
                if (string.IsNullOrEmpty(g)) { _plaques[i].gameObject.SetActive(false); _shown[i] = null; }
                else if (_shown[i] != g) Grade(i, g, 1f);
            }
            int now = v.Over ? -1 : v.Int("now", -1);
            _now.gameObject.SetActive(now >= 0);
            if (now >= 0)
            {
                Vector3 at = StationAt(_feet[now], _tees[now]);
                _now.localPosition = at + new Vector3(0, 0.08f, 0);
                _ball.localPosition = at + Vector3.up * BallR;
                _ball.gameObject.SetActive(true);
            }
            else _ball.gameObject.SetActive(false);
        }

        public override void Begin(ShotView start) { Show(start, -1); }
        public override void Ready(ShotView view) { Show(view, -1); }

        public override void Tick()
        {
            if (_nowMat != null) _nowMat.SetColor("_EmissionColor", Gold * (1.4f + Mathf.Sin(Time.time * 4f) * 0.8f));
        }

        public override IEnumerator Play(ShotView v)
        {
            int station = v.Int("last", -1);
            _now.gameObject.SetActive(false);
            _trail.positionCount = 0;
            int feet = v.Feet > 0 ? v.Feet : 6, tee = v.Tee > 0 ? v.Tee : 2;
            Vector3 from = station >= 0 ? StationAt(_feet[station], _tees[station]) : StationAt(feet, tee);
            _ball.gameObject.SetActive(true);
            yield return Along(_ball, Roll(v, from, BallR), Mathf.Lerp(0.9f, 1.5f, Mathf.InverseLerp(3f, 11f, feet)), _trail);
            if (v.Made)
            {
                Vector3 top = _ball.localPosition;
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.18f) { _ball.localPosition = Vector3.Lerp(top, new Vector3(0, -BallR * 2f, 0), t); yield return null; }
            }
            _ball.gameObject.SetActive(false);
            if (station < 0) { yield return new WaitForSeconds(0.6f); yield break; }

            // The grade stands up.
            string g = v.Title;
            Sound("assessment_grade", "door_slam", 0.25f, 1.6f);   // puttr-app-2: the game's own sound, the shared one until recorded
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.55f) { Grade(station, g, t); yield return null; }
            Grade(station, g, 1f);
            if (g.StartsWith("A")) Arcade.Sparks(At(_plaques[station].localPosition + new Vector3(0, 20f, 0)), 18);
            yield return new WaitForSeconds(0.7f);
            // The gold line cools to the grade's colour and stays faint.
            var grad = new Gradient();
            Color c = GradeColor(g);
            grad.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) }, new[] { new GradientAlphaKey(0.15f, 0f), new GradientAlphaKey(0.4f, 1f) });
            _trail.colorGradient = grad;
        }

        // ── The roll (x drawn XS wider, like the stations) ───────────────────

        static List<Vector3> Roll(ShotView v, Vector3 start, float y)
        {
            float lim = LaneW / 2f - 3f;
            var pts = new List<Vector3> { new Vector3(start.x, y, start.z) };
            if (v.Path != null && v.Path.Count >= 2)
            {
                int step = Mathf.Max(1, v.Path.Count / 40);
                for (int i = 0; i < v.Path.Count; i += step)
                {
                    Vector2 p = v.Path[i];
                    if (p.y < start.z + 4f) continue;
                    pts.Add(new Vector3(Mathf.Clamp(p.x * XS, -lim, lim), y, Mathf.Min(p.y, 14f)));
                }
                Vector2 last = v.Path[v.Path.Count - 1];
                pts.Add(new Vector3(Mathf.Clamp(last.x * XS, -lim, lim), y, Mathf.Min(last.y, 14f)));
            }
            else
            {
                float endZ = v.Reached ? (v.Made ? 0f : Mathf.Clamp(v.HasRollout ? v.RolloutIn : 8f, 3f, 14f)) : Mathf.Min(-4f, v.HasRollout ? v.RolloutIn : -8f);
                Vector3 at = new Vector3((v.Made ? v.LineIn * 0.4f : v.LineIn + (v.MissSide == "LEFT" ? -3f : v.MissSide == "RIGHT" ? 3f : 0f)) * XS, y, endZ);
                Vector3 mid = Vector3.Lerp(pts[0], at, 0.5f) + new Vector3(-at.x * 0.4f, 0, 0);
                for (int i = 1; i <= 16; i++) { float t = i / 16f; pts.Add((1 - t) * (1 - t) * pts[0] + 2 * (1 - t) * t * mid + t * t * at); }
            }
            Vector3 end = pts[pts.Count - 1];
            if (v.Made && new Vector2(end.x, end.z).magnitude > 0.6f) pts.Add(new Vector3(0, y, 0));
            return pts;
        }

        static IEnumerator Along(Transform ball, List<Vector3> pts, float seconds, LineRenderer trail)
        {
            var cum = new float[pts.Count];
            for (int i = 1; i < pts.Count; i++) cum[i] = cum[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
            float total = Mathf.Max(0.01f, cum[pts.Count - 1]);
            var drawn = new List<Vector3>();
            int seg = 1;
            for (float t = 0f; t < 1f; t += Time.deltaTime / seconds)
            {
                float s = (1f - (1f - t) * (1f - t)) * total;
                while (seg < pts.Count - 1 && cum[seg] < s) seg++;
                Vector3 p = Vector3.Lerp(pts[seg - 1], pts[seg], Mathf.InverseLerp(cum[seg - 1], cum[seg], s));
                Vector3 step = p - ball.localPosition;
                if (step.sqrMagnitude > 0.0001f) ball.Rotate(Vector3.Cross(Vector3.up, step.normalized), step.magnitude / BallR * Mathf.Rad2Deg, Space.World);
                ball.localPosition = p;
                drawn.Add(new Vector3(p.x, 0.2f, p.z));
                if (trail != null) Points(trail, drawn);
                yield return null;
            }
            ball.localPosition = pts[pts.Count - 1];
        }
    }
}

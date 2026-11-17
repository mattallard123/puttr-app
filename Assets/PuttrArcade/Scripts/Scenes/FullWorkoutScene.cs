// Full Workout as THE BLOCK: 27 putts build a 3 x 3 x 3 cube that floats over the cup in a training bay. Layers are
// the segments (short 3-5 ft on the bottom, mid, long on top), columns are the tees (L, C, R) and depth is the
// distance inside the segment. Each putt rolls up the green to the cup, then its cubelet lifts out of the cup up the
// light beam into its slot, coloured by what it earned (5 glows lime, 4 green, 3 deep green, a miss dark red).
// Units are inches; the cup is at the origin and the putt rolls up +z.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PuttrArcade
{
    public sealed class FullWorkoutScene : RuleScene
    {
        const float LaneW = 40f, BallR = 1.1f, CupR = 2.4f, Cell = 18f, Pitch = 21f;
        static readonly Vector3 BlockAt = new Vector3(10f, 58f, 40f);
        static readonly Color Lime = new Color(0.62f, 1f, 0.3f);

        Transform _ball, _start, _now;
        LineRenderer _trail;
        readonly Transform[] _cubes = new Transform[27];
        readonly int[] _shown = new int[27];
        Material _nowMat, _beamMat;
        Mesh _cubeMesh;
        float _beam;

        static bool Portrait => Screen.height > Screen.width;
        public override Vector3 CameraFrom => At(Portrait ? new Vector3(-4f, 110f, -262f) : new Vector3(-10f, 96f, -250f));
        public override Vector3 CameraLook => At(Portrait ? new Vector3(-12f, 66f, 0f) : new Vector3(-40f, 50f, 0f));
        public override float CameraFov => Portrait ? 60f : 46f;

        static Vector3 SpotFor(int feet, int tee) => new Vector3((tee - 2) * 8f, 0f, -feet * 12f);

        /// <summary>The cubelet for a distance and tee: layer by segment, column by tee, depth by distance in the segment.</summary>
        static int Slot(int feet, int tee) { int f = Mathf.Clamp(feet, 3, 11) - 3; return (f / 3) * 9 + (f % 3) * 3 + (Mathf.Clamp(tee, 1, 3) - 1); }
        static Vector3 SlotAt(int slot)
        {
            int layer = slot / 9, depth = (slot / 3) % 3, col = slot % 3;
            return BlockAt + new Vector3((col - 1) * Pitch, (layer - 1) * Pitch, (depth - 1) * Pitch);
        }

        static Material For(int pts)
        {
            if (pts >= 5) return Make.Emissive(Lime, Lime * 1.3f, 0.6f);
            if (pts == 4) return Make.Emissive(new Color(0.3f, 0.78f, 0.28f), new Color(0.1f, 0.35f, 0.08f), 0.5f);
            if (pts == 3) return Make.Lit(new Color(0.12f, 0.45f, 0.18f), 0.5f);
            return Make.Emissive(new Color(0.32f, 0.07f, 0.1f), new Color(0.12f, 0.01f, 0.02f), 0.4f);
        }

        public override void Build()
        {
            Make.Carpet(out Texture2D pile, out Texture2D pileNormal);
            Material turf = Make.Lit(new Color(0.14f, 0.5f, 0.22f), 0.15f, 0f, pile, pileNormal, 0.8f);
            turf.SetTextureScale("_BaseMap", new Vector2(3f, 12f));
            Material floor = Make.Lit(new Color(0.03f, 0.05f, 0.045f), 0.5f);
            Material hole = Make.Lit(new Color(0.01f, 0.01f, 0.01f), 0.1f);
            Material white = Make.Emissive(Color.white, new Color(0.6f, 0.6f, 0.6f));
            Material pillar = Make.Lit(new Color(0.07f, 0.09f, 0.085f), 0.4f);
            _cubeMesh = Make.RoundedBox(Vector3.one * Cell, 1.2f);

            // The bay: a dark floor with a green grid, pillars with light strips behind.
            Shape(PrimitiveType.Cube, "Floor", Root, new Vector3(0, -1.6f, 100f), new Vector3(1200f, 2f, 1000f), floor);
            Material grid = Make.Glow(new Color(0.35f, 1f, 0.45f, 0.12f));
            for (int i = -12; i <= 12; i++)
            {
                LineRenderer a = Stroke(Root, "Grid", Color.white, 0.5f); a.sharedMaterial = grid;
                Points(a, new[] { new Vector3(i * 30f, 0.02f, -300f), new Vector3(i * 30f, 0.02f, 500f) });
            }
            for (int i = -10; i <= 16; i++)
            {
                LineRenderer a = Stroke(Root, "Grid", Color.white, 0.5f); a.sharedMaterial = grid;
                Points(a, new[] { new Vector3(-400f, 0.02f, i * 30f), new Vector3(400f, 0.02f, i * 30f) });
            }
            Material strip = Make.Emissive(Color.black, new Color(0.5f, 1.4f, 0.6f));
            for (int i = -3; i <= 3; i++)
            {
                float x = i * 90f, z = 230f + Mathf.Abs(i) * 10f;
                Shape(PrimitiveType.Cube, "Pillar", Root, new Vector3(x, 150f, z), new Vector3(26f, 300f, 26f), pillar);
                Shape(PrimitiveType.Cube, "Strip", Root, new Vector3(x, 150f, z - 13.2f), new Vector3(2f, 300f, 0.4f), strip)
                    .GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            Shape(PrimitiveType.Cube, "Back wall", Root, new Vector3(0, 200f, 270f), new Vector3(1200f, 500f, 4f), Make.Lit(new Color(0.025f, 0.035f, 0.035f), 0.2f));

            // The green: from 11 ft back to just past the cup, with the cup and the start spot.
            Shape(PrimitiveType.Cube, "Green", Root, new Vector3(0, -0.5f, -64f), new Vector3(LaneW, 1f, 156f), turf);
            for (int s = -1; s <= 1; s += 2)
                Shape(PrimitiveType.Cube, "Edge", Root, new Vector3(s * (LaneW / 2f + 0.4f), 0.2f, -64f), new Vector3(0.8f, 0.6f, 156f), Make.Emissive(Color.black, Lime * 0.9f));
            Make.MeshObject("Cup", Root, Make.Disc(CupR, 48), hole).transform.localPosition = new Vector3(0, 0.03f, 0);
            Hoop("Lip", Root, new Vector3(0, 0.05f, 0), CupR, CupR + 0.35f, white);
            // Segment ticks along the right edge: short, mid, long.
            string[] seg = { "SHORT", "MID", "LONG" };
            for (int k = 0; k < 3; k++)
            {
                float z = -(4 + k * 3) * 12f;
                Words(Root, seg[k], new Vector3(LaneW / 2f + 7f, 0.3f, z), 2.6f, new Color(0.7f, 1f, 0.6f, 0.7f)).transform.localRotation = Quaternion.Euler(90f, 0, 0);
            }

            // The beam from the cup up to the block.
            _beamMat = Make.Glow(new Color(0.55f, 1f, 0.45f, 0.10f));
            Vector3 under = BlockAt - new Vector3(0, Pitch * 1.5f, 0);
            var beam = Make.MeshObject("Beam", Root, Make.Lathe(new[] { new Vector2(CupR + 0.5f, 0f), new Vector2(Cell * 1.3f, under.magnitude) }, 32), _beamMat);
            beam.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // Leaning back from the cup to under the block's middle.
            beam.transform.localRotation = Quaternion.FromToRotation(Vector3.up, under.normalized);

            // The block: a wire frame, a ghost for every cubelet still to earn.
            Material ghost = Make.Glow(new Color(0.6f, 1f, 0.6f, 0.02f));
            for (int i = 0; i < 27; i++)
            {
                GameObject g = Make.MeshObject("Ghost", Root, _cubeMesh, ghost);
                g.transform.localPosition = SlotAt(i);
                g.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _shown[i] = -1;
            }
            float h = Pitch * 1.5f;
            Vector3 c = BlockAt;
            Color wire = new Color(0.7f, 1f, 0.7f, 0.35f);
            for (int a = -1; a <= 1; a += 2)
                for (int b = -1; b <= 1; b += 2)
                {
                    Points(Stroke(Root, "Wire", wire, 0.35f), new[] { c + new Vector3(-h, a * h, b * h), c + new Vector3(h, a * h, b * h) });
                    Points(Stroke(Root, "Wire", wire, 0.35f), new[] { c + new Vector3(a * h, -h, b * h), c + new Vector3(a * h, h, b * h) });
                    Points(Stroke(Root, "Wire", wire, 0.35f), new[] { c + new Vector3(a * h, b * h, -h), c + new Vector3(a * h, b * h, h) });
                }
            // The block's labels: layers on the left, tees along the bottom.
            string[] layers = { "SHORT 3-5", "MID 6-8", "LONG 9-11" };
            for (int k = 0; k < 3; k++)
                Words(Root, layers[k], BlockAt + new Vector3(-h - 3f, (k - 1) * Pitch, -h), 2.6f, new Color(0.75f, 1f, 0.7f, 0.8f), TextAnchor.MiddleRight);
            string[] cols = { "L", "C", "R" };
            for (int k = 0; k < 3; k++)
                Words(Root, cols[k], BlockAt + new Vector3((k - 1) * Pitch, -h - 4f, -h), 3f, new Color(0.75f, 1f, 0.7f, 0.8f));

            // The slot that's up next: a bright outline that breathes.
            _nowMat = Make.Glow(new Color(0.7f, 1f, 0.5f, 0.25f));
            _now = Make.MeshObject("Now", Root, Make.RoundedBox(Vector3.one * (Cell + 1.2f), 1.4f), _nowMat).transform;
            _now.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            _start = new GameObject("Start").transform;
            _start.SetParent(Root, false);
            Hoop("Spot", _start, new Vector3(0, 0.05f, 0), 3f, 3.6f, Make.Emissive(Color.black, Lime * 1.6f));
            _ball = Shape(PrimitiveType.Sphere, "Ball", Root, Vector3.zero, Vector3.one * BallR * 2f, Make.Emissive(Color.white, new Color(0.3f, 0.3f, 0.3f), 0.7f)).transform;
            _trail = Stroke(Root, "Trail", new Color(0.85f, 1f, 0.8f, 0.8f), 1.2f);

            Sun(Root, new Vector3(55f, 25f, 0f), new Color(0.85f, 1f, 0.9f), 0.45f);
            Lamp(Root, new Vector3(0, 260f, -120f), new Vector3(0, 40f, 20f), new Color(0.9f, 1f, 0.9f), 300000f, 55f, 900f);
            Lamp(Root, new Vector3(-160f, 200f, -60f), BlockAt, new Color(0.75f, 0.95f, 1f), 400000f, 30f, 900f);
            Lamp(Root, new Vector3(140f, 160f, -40f), BlockAt, new Color(0.85f, 1f, 0.7f), 300000f, 30f, 900f);
        }

        void Show(ShotView v, int skip)
        {
            int[] feet = v.Get<int[]>("feet"), tees = v.Get<int[]>("tees"), pts = v.Get<int[]>("pts");
            if (feet == null || tees == null || pts == null) return;
            for (int i = 0; i < pts.Length && i < feet.Length; i++)
            {
                if (i == skip) continue;
                int slot = Slot(feet[i], tees[i]);
                if (pts[i] < 0 || _shown[slot] == pts[i]) continue;
                Place(slot, pts[i]);
            }
        }

        Transform Place(int slot, int pts)
        {
            if (_cubes[slot] != null) Make.Kill(_cubes[slot].gameObject);
            Transform t = Make.MeshObject("Cubelet", Root, _cubeMesh, For(pts)).transform;
            t.localPosition = SlotAt(slot);
            _cubes[slot] = t;
            _shown[slot] = pts;
            return t;
        }

        void Aim(ShotView v)
        {
            Vector3 from = SpotFor(v.NextFeet, v.NextTee);
            _start.localPosition = from;
            _ball.localPosition = from + Vector3.up * BallR;
            _ball.gameObject.SetActive(!v.Over);
            _now.gameObject.SetActive(!v.Over);
            _now.localPosition = SlotAt(Slot(v.NextFeet, v.NextTee));
        }

        public override void Begin(ShotView start) { Show(start, -1); Aim(start); }
        public override void Ready(ShotView view) { Show(view, -1); Aim(view); }

        public override void Tick()
        {
            if (_nowMat != null) _nowMat.SetColor("_Color", new Color(0.7f, 1f, 0.5f, 0.12f + 0.14f * (0.5f + 0.5f * Mathf.Sin(Time.time * 3.5f))));
            _beam = Mathf.MoveTowards(_beam, 0f, Time.deltaTime * 0.8f);
            if (_beamMat != null) _beamMat.SetColor("_Color", new Color(0.55f, 1f, 0.45f, 0.08f + _beam * 0.25f));
        }

        public override IEnumerator Play(ShotView v)
        {
            _trail.positionCount = 0;
            _now.gameObject.SetActive(false);
            int feet = v.Feet > 0 ? v.Feet : 6, tee = v.Tee > 0 ? v.Tee : 2;
            Vector3 from = SpotFor(feet, tee);
            yield return Along(_ball, Roll(v, from, BallR), Mathf.Lerp(0.9f, 1.5f, Mathf.InverseLerp(3f, 11f, feet)), _trail);

            int pts = Mathf.RoundToInt((float)v.Points);
            int slot = Slot(feet, tee);
            if (v.Made)
            {
                Vector3 top = _ball.localPosition;
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.18f) { _ball.localPosition = Vector3.Lerp(top, new Vector3(0, -BallR * 2f, 0), t); yield return null; }
            }
            _ball.gameObject.SetActive(false);

            // The cubelet lifts out of the cup, up the beam, into its place.
            _beam = 1f;
            Transform cube = Place(slot, pts);
            Vector3 to = SlotAt(slot), cup = new Vector3(0, 2f, 0);
            float sec = v.Made ? 0.9f : 1.2f;
            for (float t = 0f; t < 1f; t += Time.deltaTime / sec)
            {
                float e = Ease(t);
                cube.localPosition = Vector3.Lerp(cup, to, e) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 10f;
                cube.localScale = Vector3.one * Mathf.Lerp(0.25f, 1f, Mathf.Clamp01(t * 1.6f));
                cube.localRotation = Quaternion.Euler(0, (1f - e) * 270f, (1f - e) * 40f);
                yield return null;
            }
            cube.localPosition = to; cube.localScale = Vector3.one; cube.localRotation = Quaternion.identity;
            if (pts >= 5) { Arcade.Sparks(At(to), 22); Sound("coin", 0.5f, 1.2f); }
            else if (!v.Made) Sound("ball_land", 0.5f, 0.6f);
            // A little settle.
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.25f) { cube.localScale = Vector3.one * (1f + Mathf.Sin(t * Mathf.PI) * 0.12f); yield return null; }
            cube.localScale = Vector3.one;
            yield return new WaitForSeconds(0.45f);
        }

        // ── The roll ─────────────────────────────────────────────────────────

        static List<Vector3> Roll(ShotView v, Vector3 start, float y)
        {
            var pts = new List<Vector3> { new Vector3(start.x, y, start.z) };
            if (v.Path != null && v.Path.Count >= 2)
            {
                int step = Mathf.Max(1, v.Path.Count / 40);
                for (int i = 0; i < v.Path.Count; i += step)
                {
                    Vector2 p = v.Path[i];
                    if (p.y < start.z + 4f) continue;
                    pts.Add(new Vector3(Mathf.Clamp(p.x, -LaneW / 2f + 2f, LaneW / 2f - 2f), y, Mathf.Min(p.y, 12f)));
                }
                Vector2 last = v.Path[v.Path.Count - 1];
                pts.Add(new Vector3(Mathf.Clamp(last.x, -LaneW / 2f + 2f, LaneW / 2f - 2f), y, Mathf.Min(last.y, 12f)));
            }
            else
            {
                float endZ = v.Reached ? (v.Made ? 0f : Mathf.Clamp(v.HasRollout ? v.RolloutIn : 8f, 2f, 12f)) : Mathf.Min(-3f, v.HasRollout ? v.RolloutIn : -8f);
                Vector3 at = new Vector3(v.Made ? v.LineIn * 0.4f : v.LineIn + (v.MissSide == "LEFT" ? -3f : v.MissSide == "RIGHT" ? 3f : 0f), y, endZ);
                Vector3 mid = Vector3.Lerp(pts[0], at, 0.5f) + new Vector3(-at.x * 0.5f, 0, 0);
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
                drawn.Add(new Vector3(p.x, 0.15f, p.z));
                if (trail != null) Points(trail, drawn);
                yield return null;
            }
            ball.localPosition = pts[pts.Count - 1];
        }
    }
}

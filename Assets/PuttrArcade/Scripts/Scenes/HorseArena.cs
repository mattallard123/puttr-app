// HORSE's arena: the textures and generated meshes for an empty NBA-style arena (HorseScene builds the set from
// these). Everything is procedural and cached, so a second game reuses the same meshes and textures.
// Units are inches. The hoop's rim centre is at the origin of the floor plan; the court runs toward -z.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuttrArcade
{
    static class HorseArena
    {
        // ── Court plan (NBA dimensions) ─────────────────────────────────────
        public const float Baseline = 63f;                 // the rim centre is 5'3" from the baseline
        public const float HalfWidth = 300f;               // 50 ft wide
        public const float HalfCourt = Baseline - 564f;    // 47 ft
        public const float FarBaseline = Baseline - 1128f;
        public const float LaneHalf = 96f;                 // 16 ft lane
        public const float FreeThrow = Baseline - 228f;    // 19 ft from the baseline
        public const float ThreeR = 285f, ThreeCorner = 264f;

        // ── Bowl plan: a rounded rectangle round the court, rows stepping outward ─
        public const float BowlHX = 440f, BowlFront = 300f, BowlR = 200f;
        const float CornerZ = BowlFront - BowlR, CornerX = BowlHX - BowlR;
        public const float Cut = -760f;                   // behind every camera spot: no seats are built past here
        public const float RowDepth = 36f;
        public const int LowerRows = 14, UpperRows = 18;
        public const float LowerRise = 16f, UpperRise = 23f;
        public static float LowerY(int row) => 20f + row * LowerRise;
        public const float ClubD = LowerRows * RowDepth;                 // the club fascia between the bowls
        public static float ClubBottom => LowerY(LowerRows - 1);
        public static float ClubTop => ClubBottom + 96f;
        public const float UpperD0 = ClubD + 24f;
        public static float UpperY(int row) => ClubTop + 14f + row * UpperRise;
        public static float BackD => UpperD0 + UpperRows * RowDepth;

        // ── A small mesh builder ────────────────────────────────────────────

        public sealed class Builder
        {
            readonly List<Vector3> _v = new List<Vector3>();
            readonly List<Vector3> _n = new List<Vector3>();
            readonly List<Vector2> _uv = new List<Vector2>();
            readonly List<Color> _c = new List<Color>();
            readonly List<int> _t = new List<int>();
            public Color Tint = Color.white;
            public int Count => _v.Count;

            /// <summary>A quad a b c d (round its edge) facing n; the winding is fixed to face n.</summary>
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector2 uvA = default, Vector2 uvB = default, Vector2 uvC = default, Vector2 uvD = default)
            {
                int i = _v.Count;
                _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
                for (int k = 0; k < 4; k++) { _n.Add(n); _c.Add(Tint); }
                _uv.Add(uvA); _uv.Add(uvB); _uv.Add(uvC); _uv.Add(uvD);
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) >= 0f) { _t.Add(i); _t.Add(i + 1); _t.Add(i + 2); _t.Add(i); _t.Add(i + 2); _t.Add(i + 3); }
                else { _t.Add(i); _t.Add(i + 2); _t.Add(i + 1); _t.Add(i); _t.Add(i + 3); _t.Add(i + 2); }
            }

            /// <summary>A quad with UVs 0..1 (a = bottom left, b = top left, c = top right, d = bottom right as seen).</summary>
            public void Card(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n) => Quad(a, b, c, d, n, new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));

            /// <summary>A box on axes x (right), y (up), z (forward), half sizes h. Bottom face left off unless asked.</summary>
            public void Box(Vector3 c, Vector3 x, Vector3 y, Vector3 z, Vector3 h, bool bottom = false)
            {
                Vector3 X = x * h.x, Y = y * h.y, Z = z * h.z;
                Face(c + Z, X, Y, z);   // front
                Face(c - Z, X, Y, -z);  // back
                Face(c + X, Z, Y, x);
                Face(c - X, Z, Y, -x);
                Face(c + Y, X, Z, y);
                if (bottom) Face(c - Y, X, Z, -y);
            }

            void Face(Vector3 c, Vector3 u, Vector3 v, Vector3 n) => Quad(c - u - v, c - u + v, c + u + v, c + u - v, n, new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));

            public void Tri(Vector3 a, Vector3 b, Vector3 c, Color ca, Color cb, Color cc)
            {
                int i = _v.Count;
                _v.Add(a); _v.Add(b); _v.Add(c);
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                _n.Add(n); _n.Add(n); _n.Add(n);
                _c.Add(ca); _c.Add(cb); _c.Add(cc);
                _uv.Add(Vector2.zero); _uv.Add(Vector2.zero); _uv.Add(Vector2.zero);
                _t.Add(i); _t.Add(i + 1); _t.Add(i + 2);
            }

            public Mesh Build(string name)
            {
                var m = new Mesh { name = name, indexFormat = _v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                m.SetVertices(_v);
                m.SetNormals(_n);
                m.SetUVs(0, _uv);
                m.SetColors(_c);
                m.SetTriangles(_t, 0);
                m.RecalculateBounds();
                return m;
            }
        }

        // ── Textures ────────────────────────────────────────────────────────

        static Texture2D _wood, _woodNormal, _ball, _haze;

        /// <summary>Maple strip flooring: 16 boards (36 in) across u, 192 in along v, butt joints staggered.</summary>
        public static Texture2D Hardwood(out Texture2D normal)
        {
            if (_wood != null) { normal = _woodNormal; return _wood; }
            const int w = 512, h = 1024, boards = 16, joints = 2;
            var rng = new System.Random(23);
            var cut = new float[boards, joints];
            var tone = new float[boards, joints + 1];
            for (int b = 0; b < boards; b++)
            {
                float start = (float)rng.NextDouble();
                for (int j = 0; j < joints; j++) cut[b, j] = (start + j / (float)joints + (float)rng.NextDouble() * 0.12f) % 1f;
                if (cut[b, 0] > cut[b, 1]) { float k = cut[b, 0]; cut[b, 0] = cut[b, 1]; cut[b, 1] = k; }
                for (int j = 0; j <= joints; j++) tone[b, j] = (float)rng.NextDouble();
            }
            Color light = new Color(0.86f, 0.64f, 0.40f), deep = new Color(0.72f, 0.48f, 0.27f);
            var px = new Color[w * h];
            var height = new float[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = y / (float)h;
                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)w * boards;
                    int b = Mathf.Min(boards - 1, (int)u);
                    float across = u - b;
                    // Which length of board this is (between joints), and how near a joint.
                    int seg = 0; float near = 1f;
                    for (int j = 0; j < joints; j++)
                    {
                        float d = Mathf.Abs(v - cut[b, j]); d = Mathf.Min(d, 1f - d);
                        near = Mathf.Min(near, d);
                        if (v >= cut[b, j]) seg++;
                    }
                    // Cuts are sorted, so the pieces before the first and after the last are one board across the tile's edge.
                    float t = tone[b, seg == joints ? 0 : seg];
                    float grain = Make.TileNoise(x * 0.18f, y * 16f / h, 92, 5 + b) - 0.5f;
                    float fine = Make.TileNoise(x * 0.9f, y * 64f / h, 460, 9) - 0.5f;
                    float figure = Mathf.Sin((x * 0.11f + Make.TileNoise(x * 0.05f, y * 8f / h, 25, 3 + b) * 6f) * 3.1f) * 0.5f;
                    Color c = Color.Lerp(deep, light, 0.25f + t * 0.6f);
                    c *= 1f + grain * 0.16f + fine * 0.08f + figure * 0.035f;
                    float hh = 0f;
                    float edge = Mathf.Min(across, 1f - across) * (w / (float)boards);   // px from the board's side
                    if (edge < 1.2f) { c *= 0.62f; hh = -1f; }
                    else if (near * h < 1.5f) { c *= 0.7f; hh = -1f; }
                    c.a = 1f;
                    px[y * w + x] = c;
                    height[y * w + x] = hh + grain * 0.15f;
                }
            }
            _wood = Make.NewTexture(w, h, true);
            _wood.SetPixels(px);
            _wood.Apply(true);
            _woodNormal = Make.NormalFromHeight(height, w, h, 1.4f);
            normal = _woodNormal;
            return _wood;
        }

        /// <summary>A basketball: pebbled orange, black channels (two great circles and the two curves).</summary>
        public static Texture2D Basketball()
        {
            if (_ball != null) return _ball;
            const int w = 512, h = 256;
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = y / (float)h, lat = (v - 0.5f) * Mathf.PI;
                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)w;
                    float pebble = Make.TileNoise(x * 0.9f, y * 0.9f, 460, 2);
                    Color c = new Color(0.86f, 0.40f, 0.12f) * (0.9f + pebble * 0.18f);
                    float cosLat = Mathf.Max(0.05f, Mathf.Cos(lat));
                    float du(float target) { float d = Mathf.Abs(u - target); d = Mathf.Min(d, 1f - d); return d * cosLat * Mathf.PI * 2f; }
                    float line = Mathf.Min(Mathf.Min(du(0f), du(0.5f)), Mathf.Abs(lat));
                    float bend = 0.13f * Mathf.Cos(lat * 1.6f);
                    line = Mathf.Min(line, Mathf.Min(du(0.25f + bend), du(0.75f - bend)));
                    if (line < 0.028f) c = new Color(0.06f, 0.04f, 0.03f);
                    c.a = 1f;
                    px[y * w + x] = c;
                }
            }
            _ball = Make.NewTexture(w, h, true);
            _ball.SetPixels(px);
            _ball.Apply(true);
            return _ball;
        }

        // ── Court ───────────────────────────────────────────────────────────

        /// <summary>A flat rectangle on the floor, UVs in tiles of tileX by tileZ inches.</summary>
        public static Mesh Floor(float x0, float x1, float z0, float z1, float y, float tileX, float tileZ)
        {
            var b = new Builder();
            b.Quad(new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0), Vector3.up,
                new Vector2(x0 / tileX, z0 / tileZ), new Vector2(x0 / tileX, z1 / tileZ), new Vector2(x1 / tileX, z1 / tileZ), new Vector2(x1 / tileX, z0 / tileZ));
            return b.Build("Floor");
        }

        // ── The Puttr mat, painted into the key ─────────────────────────────
        // The lane runs on toward the shooter past the free-throw line, and the mat's markings are laid in it as on the
        // real mat: a line for each distance, 3 ft nearest the hoop to 11 ft, and the Left, Center and Right tee spots
        // on each line (Left is the shooter's left, -x). The 7 ft line sits on the free-throw line.
        public const float MatTop = 70f, MatStep = 24f, TeeOff = 56f, PaintEnd = -280f, MatHalf = 68f;
        public static float MatZ(int feet) => -(MatTop + (feet - 3) * MatStep);
        public static float TeeX(int tee) => tee == 1 ? -TeeOff : tee == 3 ? TeeOff : 0f;

        /// <summary>The extended lane's edges, the distance lines and the tee spots, as one mesh.</summary>
        public static Mesh MatMarks(float y)
        {
            var b = new Builder();
            const float w = 2.6f;
            Strip(b, new[] { new Vector2(-LaneHalf, FreeThrow), new Vector2(-LaneHalf, PaintEnd), new Vector2(LaneHalf, PaintEnd), new Vector2(LaneHalf, FreeThrow) }, w, y);
            for (int f = 3; f <= 11; f++)
            {
                float z = MatZ(f);
                if (Mathf.Abs(z - FreeThrow) > 3f) Strip(b, new[] { new Vector2(-MatHalf, z), new Vector2(MatHalf, z) }, 1.6f, y);
                for (int t = 1; t <= 3; t++)
                {
                    // A tee spot: a dot on the line.
                    Vector3 c = new Vector3(TeeX(t), y + 0.02f, z);
                    const int n = 16;
                    const float r = 4.5f;
                    for (int k = 0; k < n; k += 2)
                    {
                        Vector3 P(int i) { float a = i / (float)n * Mathf.PI * 2f; return c + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r); }
                        b.Quad(c, P(k), P(k + 1), P(k + 2), Vector3.up);
                    }
                }
            }
            return b.Build("Mat marks");
        }

        static void Strip(Builder b, IList<Vector2> pts, float width, float y)
        {
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector2 p = pts[i], q = pts[i + 1], d = (q - p).normalized, s = new Vector2(-d.y, d.x) * (width / 2f);
                Vector2 e = d * (width / 2f);   // overlap the joins
                p -= e; q += e;
                b.Quad(new Vector3(p.x - s.x, y, p.y - s.y), new Vector3(p.x + s.x, y, p.y + s.y), new Vector3(q.x + s.x, y, q.y + s.y), new Vector3(q.x - s.x, y, q.y - s.y), Vector3.up);
            }
        }

        static List<Vector2> ArcPts(Vector2 c, float r, float fromDeg, float toDeg, int n)
        {
            var pts = new List<Vector2>();
            for (int i = 0; i <= n; i++)
            {
                float a = Mathf.Lerp(fromDeg, toDeg, i / (float)n) * Mathf.Deg2Rad;
                pts.Add(c + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * r);
            }
            return pts;
        }

        /// <summary>Every painted line on the court, 2 in wide, as one mesh.</summary>
        public static Mesh Lines(float y)
        {
            var b = new Builder();
            const float w = 2.6f;
            void L(params Vector2[] p) => Strip(b, p, w, y);
            // Boundary and the half-court line.
            L(new Vector2(-HalfWidth, Baseline), new Vector2(HalfWidth, Baseline));
            L(new Vector2(-HalfWidth, Baseline), new Vector2(-HalfWidth, FarBaseline));
            L(new Vector2(HalfWidth, Baseline), new Vector2(HalfWidth, FarBaseline));
            L(new Vector2(-HalfWidth, HalfCourt), new Vector2(HalfWidth, HalfCourt));
            L(new Vector2(-HalfWidth, FarBaseline), new Vector2(HalfWidth, FarBaseline));
            Strip(b, ArcPts(new Vector2(0, HalfCourt), 72f, 0f, 360f, 72), w, y);
            Strip(b, ArcPts(new Vector2(0, HalfCourt), 24f, 0f, 360f, 32), w, y);
            foreach (float end in new[] { 1f, -1f })
            {
                float bl = end > 0 ? Baseline : FarBaseline, rimZ = end > 0 ? 0f : FarBaseline - Baseline;
                float ft = bl - end * 228f;
                // The lane and the free-throw line.
                L(new Vector2(-LaneHalf, bl), new Vector2(-LaneHalf, ft), new Vector2(LaneHalf, ft), new Vector2(LaneHalf, bl));
                // Free-throw circle: solid outside the lane, dashed inside it.
                float flip = end > 0 ? 0f : 180f;
                Strip(b, ArcPts(new Vector2(0, ft), 72f, flip - 90f, flip + 90f, 36), w, y);
                for (int k = 0; k < 8; k++)
                {
                    float a0 = flip + 90f + k * 22.5f + 4f;
                    Strip(b, ArcPts(new Vector2(0, ft), 72f, a0, a0 + 12f, 4), w, y);
                }
                // Restricted area: a 4 ft arc and short straights to the backboard.
                float boardZ = rimZ + end * 15f;
                Strip(b, ArcPts(new Vector2(0, rimZ), 48f, flip - 90f, flip + 90f, 28), w, y);
                L(new Vector2(-48f, rimZ), new Vector2(-48f, boardZ));
                L(new Vector2(48f, rimZ), new Vector2(48f, boardZ));
                // Three-point line: corner straights, then the arc.
                float cz = Mathf.Sqrt(ThreeR * ThreeR - ThreeCorner * ThreeCorner);
                float amax = Mathf.Asin(ThreeCorner / ThreeR) * Mathf.Rad2Deg;
                L(new Vector2(-ThreeCorner, bl), new Vector2(-ThreeCorner, rimZ - end * cz));
                L(new Vector2(ThreeCorner, bl), new Vector2(ThreeCorner, rimZ - end * cz));
                Strip(b, ArcPts(new Vector2(0, rimZ), ThreeR, flip - amax, flip + amax, 64), w, y);
                // Lane marks: the block and the hash marks, and the 28 ft sideline hashes.
                foreach (float side in new[] { -1f, 1f })
                {
                    foreach (float d in new[] { 84f, 132f, 168f, 204f })
                        L(new Vector2(side * LaneHalf, bl - end * d), new Vector2(side * (LaneHalf + 8f), bl - end * d));
                    float blockZ = bl - end * 96f;
                    b.Quad(new Vector3(side * LaneHalf, y, blockZ), new Vector3(side * LaneHalf, y, blockZ - end * 12f), new Vector3(side * (LaneHalf + 8f), y, blockZ - end * 12f), new Vector3(side * (LaneHalf + 8f), y, blockZ), Vector3.up);
                    L(new Vector2(side * HalfWidth, bl - end * 336f), new Vector2(side * (HalfWidth - 36f), bl - end * 336f));
                }
            }
            return b.Build("Court lines");
        }

        // ── The bowl ────────────────────────────────────────────────────────
        //
        // A path round the near end of the court: up the left side, round the corner, along the baseline end, round
        // the other corner and back down the right side. d is how far out a row is; the corner arcs share centres,
        // so a row's aisles line up with the rows below it.

        public struct Spot { public Vector3 At, In; }

        static float SegLength(int seg, float d)
        {
            switch (seg)
            {
                case 0: case 4: return CornerZ - Cut;
                case 2: return CornerX * 2f;
                default: return (BowlR + d) * Mathf.PI / 2f;
            }
        }

        static Spot Along(int seg, float s, float d)
        {
            float r = BowlR + d;
            switch (seg)
            {
                case 0: return new Spot { At = new Vector3(-BowlHX - d, 0, Cut + s), In = Vector3.right };
                case 2: return new Spot { At = new Vector3(-CornerX + s, 0, BowlFront + d), In = Vector3.back };
                case 4: return new Spot { At = new Vector3(BowlHX + d, 0, CornerZ - s), In = Vector3.left };
                default:
                    {
                        float a = (seg == 1 ? Mathf.PI : Mathf.PI / 2f) - s / r;
                        var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                        var c = new Vector3(seg == 1 ? -CornerX : CornerX, 0, CornerZ);
                        return new Spot { At = c + dir * r, In = -dir };
                    }
            }
        }

        /// <summary>Points round the path at offset d (the same fractions every row, so strips join up).</summary>
        public static List<Spot> Path(float d)
        {
            var pts = new List<Spot>();
            int[] steps = { 14, 9, 10, 9, 14 };
            for (int seg = 0; seg < 5; seg++)
            {
                float len = SegLength(seg, d);
                for (int i = seg == 0 ? 0 : 1; i <= steps[seg]; i++) pts.Add(Along(seg, len * i / steps[seg], d));
            }
            return pts;
        }

        /// <summary>Seat spots round the path at offset d: aisles at each segment's ends and the middle of each.</summary>
        public static IEnumerable<Spot> Seats(float d, float pitch = 21f)
        {
            for (int seg = 0; seg < 5; seg++)
            {
                float len = SegLength(seg, d), mid = len / 2f;
                for (float s = 20f; s <= len - 20f; s += pitch)
                {
                    if (Mathf.Abs(s - mid) < 20f) continue;
                    if ((seg == 0 || seg == 4) && Mathf.Abs(s - len * 0.2f) < 20f) continue;
                    yield return Along(seg, s, d);
                }
            }
        }

        /// <summary>Vertical faces along the path at offset d, from y0 to y1, facing the court.</summary>
        public static void Wall(Builder b, float d, float y0, float y1)
        {
            List<Spot> p = Path(d);
            for (int i = 0; i < p.Count - 1; i++)
            {
                Vector3 a = p[i].At, c = p[i + 1].At, n = (p[i].In + p[i + 1].In).normalized;
                b.Quad(a + Vector3.up * y0, a + Vector3.up * y1, c + Vector3.up * y1, c + Vector3.up * y0, n);
            }
        }

        /// <summary>A flat tread from offset d0 to d1 at height y.</summary>
        public static void Tread(Builder b, float d0, float d1, float y)
        {
            List<Spot> p = Path(d0), q = Path(d1);
            for (int i = 0; i < p.Count - 1; i++)
                b.Quad(p[i].At + Vector3.up * y, q[i].At + Vector3.up * y, q[i + 1].At + Vector3.up * y, p[i + 1].At + Vector3.up * y, Vector3.up);
        }

        /// <summary>Seats for one row: a pan and a back (or only the back, far away), facing the court.</summary>
        public static void SeatRow(Builder b, float d, float y, bool pans)
        {
            foreach (Spot s in Seats(d))
            {
                Vector3 side = Vector3.Cross(Vector3.up, s.In);
                Vector3 at = s.At + s.In * 16f;
                if (pans) b.Box(at + Vector3.up * (y + 16f) + s.In * 1f, side, Vector3.up, s.In, new Vector3(9f, 1.5f, 8f));
                b.Box(at + Vector3.up * (y + 26f) - s.In * 8f, side, Vector3.up, s.In, new Vector3(9.2f, 10f, 1.6f));
            }
        }

        /// <summary>Logo cards along the path at offset d (facing the court), every `every` inches, centred at height y.</summary>
        public static void Logos(Builder b, float d, float y, float width, float height, float every, float phase = 0f)
        {
            for (int seg = 0; seg < 5; seg++)
            {
                float len = SegLength(seg, d);
                int n = Mathf.Max(1, Mathf.RoundToInt(len / every));
                for (int i = 0; i < n; i++)
                {
                    float s = (i + 0.5f + phase) / n * len;
                    if (s > len) continue;
                    Spot p = Along(seg, s, d);
                    Vector3 c = p.At + Vector3.up * y + p.In * (0.6f + (seg % 2 == 1 ? width * width / (8f * (BowlR + d)) : 0f));   // clear of the wall on a curve
                    Vector3 right = -Vector3.Cross(Vector3.up, p.In) * (width / 2f), up = Vector3.up * (height / 2f);
                    b.Card(c - right - up, c - right + up, c + right + up, c + right - up, p.In);
                }
            }
        }

        // ── Haze ────────────────────────────────────────────────────────────

        /// <summary>A light shaft: an open cone from the apex, bright at the top, gone by the bottom (vertex colors).</summary>
        public static Mesh Shaft(Vector3 from, Vector3 to, float r0, float r1, float alpha)
        {
            var b = new Builder();
            Vector3 axis = (to - from).normalized;
            Vector3 u = Vector3.Cross(axis, Vector3.up).normalized, v = Vector3.Cross(axis, u);
            const int n = 18;
            var top = new Color(1f, 0.97f, 0.9f, alpha); var bottom = new Color(1f, 0.97f, 0.9f, 0f);
            for (int i = 0; i < n; i++)
            {
                float a0 = i / (float)n * Mathf.PI * 2f, a1 = (i + 1) / (float)n * Mathf.PI * 2f;
                Vector3 d0 = u * Mathf.Cos(a0) + v * Mathf.Sin(a0), d1 = u * Mathf.Cos(a1) + v * Mathf.Sin(a1);
                Vector3 p0 = from + d0 * r0, p1 = from + d1 * r0, q0 = to + d0 * r1, q1 = to + d1 * r1;
                b.Tri(p0, q0, q1, top, bottom, bottom);
                b.Tri(p0, q1, p1, top, bottom, top);
            }
            return b.Build("Shaft");
        }
    }
}

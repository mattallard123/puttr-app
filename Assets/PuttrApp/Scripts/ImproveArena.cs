// The Improve tab's Arena hub (docs/improve-profile-3d, "Improve A"): its four live 3D scenes and the UI tricks
// that give the flat cards depth.
//
// Live 3D (Live3D.cs): today's session over the Puttr mat and box, the drill spots lit and a ball rolling the focus
// putt; the stroke replay, a putter swinging with a light trail; PQS as a 3D donut; the last 30 days as 27 columns
// standing on the mat. All in inches, as the arcade is.
//
// The mat and the box are the real ones, from the product photographs (marketing/assets/images/Puttr Studios):
// the mat 24 by 144 inches in the photographed carpet, bound at its edges, curving up the ramp into the box, the hole
// 5 in from the far end; printed on it at 3 to 11 ft a row of three thin tee circles (Left, Centre, Right, 8 in
// apart) and the distance in a light grey just short of the centre one; the ball return down the right. The box is
// the arcade's Puttr V2 console (WorldConsole.cs), ported here so each scene owns and frees what it makes.
//
// UI FX: gradients and soft shadows as small textures made once, a trend ribbon on a floor grid, the due ring.
using System;
using System.Collections.Generic;
using PuttrArcade;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public static class Arena
    {
        static readonly Color Spark = new Color(0.48f, 0.83f, 0.27f);

        /// <summary>A spot on the mat in scene inches: x right, z toward the hole (the hole is z 0), on the surface.</summary>
        public static Vector3 Spot(int feet, int tee, float lift = 0f) => new Vector3(((tee == 1 || tee == 3 ? tee : 2) - 2) * TeeGap, Top + lift, -feet * 12f);

        const float Top = 0.5f;        // the flat of the mat, in the scene
        const float TeeGap = 8f;       // Left and Right tees, either side of Centre
        const float MatEndZ = -139f;   // the Player's end: 144 in from the far end, the hole 5 in from that
        const float MarksEndZ = -24f;  // the printed marks lie on the flat, short of where the carpet starts to lift

        /// <summary>The mat's surface height at z (flat, then the curve and the ramp up to the far end), in the scene.</summary>
        static float SurfY(float z) => Top + LaneSpec.SurfaceY(Mathf.Min(z, LaneSpec.RampEndZ)) - LaneSpec.MatY;

        // ── The mat and the box ──────────────────────────────────────────────

        // Made once and kept (like the textures below): the photographed carpet and the printed marks.
        static Texture2D _carpet, _carpetNormal, _marks;
        // The photo is a very dark teal; lifted to the deep green of the mat in the product shots.
        static readonly Color CarpetTint = new Color(0.95f, 1.7f, 1.12f);

        static void Carpet()
        {
            if (_carpet != null) return;
            if (!Make.Photo("CarpetPhoto", 1.4f, out _carpet, out _carpetNormal)) Make.Carpet(out _carpet, out _carpetNormal);
        }

        /// <summary>The printed marks, white on clear: a tee circle at Left, Centre and Right on every foot from 3 to 11,
        /// the number just short of the Centre one. x across the mat, y from MatEndZ (v 0) to MarksEndZ (v 1).</summary>
        static Texture2D Marks()
        {
            if (_marks != null) return _marks;
            const int PerIn = 24;   // fine enough for the circles' hairline
            int w = 24 * PerIn, h = Mathf.RoundToInt((MarksEndZ - MatEndZ) * PerIn);
            var alpha = new float[w * h];
            Vector2 Px(float x, float z) => new Vector2((x + 12f) * PerIn, (z - MatEndZ) * PerIn);

            void Segment(Vector2 a, Vector2 b, float width, float strength)
            {
                float half = width * 0.5f;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - half - 1)), x1 = Mathf.Min(w - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + half + 1));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - half - 1)), y1 = Mathf.Min(h - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + half + 1));
                Vector2 ab = b - a;
                float len2 = Mathf.Max(1e-5f, ab.sqrMagnitude);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        var p = new Vector2(x + 0.5f, y + 0.5f);
                        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                        float d = Vector2.Distance(p, a + ab * t);
                        float cover = Mathf.Clamp01(half + 0.5f - d) * strength;
                        int i = y * w + x;
                        if (cover > alpha[i]) alpha[i] = cover;
                    }
            }
            void Line(List<Vector2> pts, float width, float strength) { for (int i = 0; i < pts.Count - 1; i++) Segment(pts[i], pts[i + 1], width, strength); }

            // A tee circle is the size of a golf ball (1.68 in across), in a fine line, as printed on the real mat.
            const float Ring = 0.84f, RingLine = 0.055f, Digit = 2.5f, DigitLine = 0.2f;
            for (int feet = 3; feet <= 11; feet++)
            {
                float z = -feet * 12f;
                for (int tee = -1; tee <= 1; tee++)
                {
                    // On the real mat the 11 ft middle tee is the Puttr icon, not a circle.
                    if (feet == 11 && tee == 0) { StampLogo("PuttrArcade/LogoMarkWhite", alpha, w, h, Px(0f, z), 3.2f * PerIn); continue; }
                    var ring = new List<Vector2>();
                    for (int k = 0; k <= 48; k++) { float a = k / 48f * Mathf.PI * 2f; ring.Add(Px(tee * TeeGap + Mathf.Cos(a) * Ring, z + Mathf.Sin(a) * Ring)); }
                    Line(ring, RingLine * PerIn, 1f);   // white, as printed
                }
                // The 11 ft row has no number: its middle tee is the Puttr icon (above).
                if (feet == 11) continue;
                // The number, a third of the way to the next row, upright for the Player.
                string text = feet.ToString();
                float advance = Digit * 0.78f, start = -(text.Length - 1) * advance * 0.5f;
                for (int c = 0; c < text.Length; c++)
                    foreach (List<Vector2> stroke in Glyph(text[c]))
                    {
                        var pts = new List<Vector2>();
                        foreach (Vector2 g in stroke) pts.Add(Px(start + c * advance + (g.x - 0.3f) * Digit, z - 4f + (g.y - 0.5f) * Digit));
                        Line(pts, DigitLine * PerIn, 0.95f);
                    }
            }
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha[i] * 255f));
            _marks = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "Mat marks", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            _marks.SetPixels32(px);
            _marks.Apply(true, true);
            return _marks;
        }

        /// <summary>A Puttr logo resource (read back through the GPU: it is not CPU-readable) printed white into
        /// the marks, centred here and this many pixels wide.</summary>
        static void StampLogo(string resource, float[] alpha, int w, int h, Vector2 centre, float width)
        {
            Texture2D logo = Resources.Load<Texture2D>(resource);
            if (logo == null) return;
            int lw = Mathf.Max(8, Mathf.RoundToInt(width)), lh = Mathf.Max(2, Mathf.RoundToInt(width * logo.height / logo.width));
            RenderTexture rt = RenderTexture.GetTemporary(lw, lh, 0, RenderTextureFormat.ARGB32);
            RenderTexture was = RenderTexture.active;
            Graphics.Blit(logo, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(lw, lh, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, lw, lh), 0, 0);
            copy.Apply();
            RenderTexture.active = was;
            RenderTexture.ReleaseTemporary(rt);
            Color32[] src = copy.GetPixels32();
            UnityEngine.Object.Destroy(copy);
            int x0 = Mathf.RoundToInt(centre.x - lw * 0.5f), y0 = Mathf.RoundToInt(centre.y - lh * 0.5f);
            for (int y = 0; y < lh; y++)
                for (int x = 0; x < lw; x++)
                {
                    int tx = x0 + x, ty = y0 + y;
                    if (tx < 0 || ty < 0 || tx >= w || ty >= h) continue;
                    // The print is read the right way up from the Player's end (rows run from MatEndZ upward).
                    float a = src[y * lw + x].a / 255f * 0.95f;
                    int i = ty * w + tx;
                    if (a > alpha[i]) alpha[i] = a;
                }
        }

        static List<Vector2> Arc(float cx, float cy, float rx, float ry, float fromDeg, float toDeg, int n = 24)
        {
            var pts = new List<Vector2>();
            for (int i = 0; i <= n; i++) { float a = Mathf.Lerp(fromDeg, toDeg, i / (float)n) * Mathf.Deg2Rad; pts.Add(new Vector2(cx + Mathf.Cos(a) * rx, cy + Mathf.Sin(a) * ry)); }
            return pts;
        }

        /// <summary>A thin sans digit as strokes, in a box 0.6 wide and 1 tall (y up), like the mat's own print.</summary>
        static List<List<Vector2>> Glyph(char c)
        {
            List<Vector2> P(params float[] xy) { var l = new List<Vector2>(); for (int i = 0; i < xy.Length; i += 2) l.Add(new Vector2(xy[i], xy[i + 1])); return l; }
            List<List<Vector2>> Six() => new List<List<Vector2>> { Arc(0.3f, 0.29f, 0.27f, 0.28f, 0f, 360f, 32), Arc(0.47f, 0.3f, 0.44f, 0.67f, 78f, 180f) };
            switch (c)
            {
                case '0': return new List<List<Vector2>> { Arc(0.3f, 0.5f, 0.28f, 0.48f, 0f, 360f, 40) };
                case '1': return new List<List<Vector2>> { P(0.1f, 0.78f, 0.34f, 0.98f, 0.34f, 0.02f) };
                case '3': return new List<List<Vector2>> { Arc(0.29f, 0.755f, 0.24f, 0.225f, 155f, -90f), Arc(0.3f, 0.27f, 0.27f, 0.26f, 90f, -155f) };
                case '4': return new List<List<Vector2>> { P(0.44f, 0.02f, 0.44f, 0.98f, 0.02f, 0.32f, 0.6f, 0.32f) };
                case '5': { var five = new List<List<Vector2>> { P(0.53f, 0.98f, 0.12f, 0.98f, 0.08f, 0.53f) }; five.Add(Arc(0.3f, 0.31f, 0.27f, 0.29f, 140f, -150f)); return five; }
                case '6': return Six();
                case '7': return new List<List<Vector2>> { P(0.02f, 0.98f, 0.58f, 0.98f, 0.2f, 0.02f) };
                case '8': return new List<List<Vector2>> { Arc(0.3f, 0.765f, 0.215f, 0.215f, 0f, 360f, 32), Arc(0.3f, 0.28f, 0.27f, 0.27f, 0f, 360f, 32) };
                case '9':
                    {
                        var nine = Six();
                        foreach (List<Vector2> s in nine) for (int i = 0; i < s.Count; i++) s[i] = new Vector2(0.6f - s[i].x, 1f - s[i].y);
                        return nine;
                    }
                default: return new List<List<Vector2>>();
            }
        }

        /// <summary>The floor, the real mat with its marks, the hole and the Puttr box at the far end. holeRing flashes as a ball drops.</summary>
        /// <param name="floor">False: only the Puttr (and the lights). The host's arena (Host.cs BrandStage) lays its own floor.</param>
        /// <param name="carpet">The carpet's tint: the Improve tab lifts the dark photo (CarpetTint); the host's lit arena
        /// wants it nearer the deep green of the product shots.</param>
        internal static void BuildMat(Live3D.Stage s, out Material holeRing, bool floor = true, Color? carpet = null)
        {
            s.Background = new Color32(12, 16, 14, 255);
            s.Light(new Color(1f, 0.97f, 0.92f), 1.45f, new Vector3(52f, -28f, 0f));
            s.Light(new Color(0.7f, 0.82f, 1f), 0.5f, new Vector3(22f, 160f, 0f));

            if (floor)
            {
                // The floor: dark, a faint grid on it.
                s.Box("Floor", new Vector3(0, -0.6f, -60), new Vector3(900, 1, 900), s.Lit(new Color(0.012f, 0.015f, 0.014f), 0.25f));
                Material grid = s.Glow(new Color(Spark.r, Spark.g, Spark.b, 0.035f));
                var lines = new List<(Vector3, Vector3, Quaternion)>();
                for (int i = -8; i <= 8; i++)
                {
                    lines.Add((new Vector3(i * 30f, -0.08f, -60), new Vector3(0.35f, 0.02f, 900), Quaternion.identity));
                    lines.Add((new Vector3(0, -0.08f, -60 + i * 30f), new Vector3(900, 0.02f, 0.35f), Quaternion.identity));
                }
                s.Boxes("Grid", lines, grid);
            }

            // The Puttr, built in the arcade's own coordinates (the hole at the origin, on the ramp) and lifted so the
            // flat of the mat is at Top.
            Transform g = s.Group("Puttr");
            g.localPosition = new Vector3(0, Top - LaneSpec.MatY, 0);
            float w = LaneSpec.MatHalfWidth, floorY = LaneSpec.MatY - 0.3f;

            // The carpet: the photographed pile, a deep green, one mesh with a square left out round the hole and a
            // patch with the hole cut in it, the cup under it.
            Carpet();
            const float tile = 1f / 12.8f, patch = 3f;
            Material carpetLit = s.Own(Make.Lit(carpet ?? CarpetTint, 0.1f, 0f, _carpet, _carpetNormal, 0.45f));
            float cos = Mathf.Cos(LaneSpec.RampAngleDeg * Mathf.Deg2Rad), pz = patch * cos;
            Func<float, float> Y = LaneSpec.SurfaceY, dY = LaneSpec.SurfaceSlope;
            Quaternion tilt = Quaternion.FromToRotation(Vector3.up, LaneSpec.SurfaceNormal(0f));
            Vector3 up = LaneSpec.SurfaceNormal(0f);
            var temp = new List<Mesh>
            {
                Make.Ribbon(MatEndZ, -pz, -w, w, Y, dY, 1f, tile),
                Make.Ribbon(pz, LaneSpec.RampEndZ, -w, w, Y, dY, 0.5f, tile),
                Make.Ribbon(-pz, pz, -w, -patch, Y, dY, 0.5f, tile),
                Make.Ribbon(-pz, pz, patch, w, Y, dY, 0.5f, tile),
                Make.HolePatch(patch, LaneSpec.HoleRadius, tile, Vector3.zero, cos),
                Make.Cup(LaneSpec.HoleRadius + 0.004f, 0.22f, false),
            };
            var parts = new List<CombineInstance>();
            for (int i = 0; i < temp.Count; i++)
                parts.Add(new CombineInstance { mesh = temp[i], transform = i < 4 ? Matrix4x4.identity : Matrix4x4.TRS(i == 5 ? up * 0.002f : Vector3.zero, tilt, Vector3.one) });
            s.Merge("Carpet", parts, carpetLit, g);
            foreach (Mesh m in temp) UnityEngine.Object.Destroy(m);
            GameObject cup = s.Mesh("Cup", Make.Cup(LaneSpec.HoleRadius, 4f), s.Lit(new Color(0.01f, 0.014f, 0.012f), 0.25f), g);
            cup.transform.localRotation = tilt;
            holeRing = s.Glow(new Color(Spark.r, Spark.g, Spark.b, 0f));
            GameObject ring = s.Mesh("Hole light", Make.Ring(LaneSpec.HoleRadius - 0.02f, LaneSpec.HoleRadius + 0.45f), holeRing, g);
            ring.transform.SetPositionAndRotation(g.TransformPoint(up * 0.03f), g.rotation * tilt);

            // The bound edges.
            var edges = new List<CombineInstance>();
            var edgeMeshes = new List<Mesh>();
            for (int side = -1; side <= 1; side += 2)
            {
                Mesh edge = Make.Ribbon(MatEndZ, LaneSpec.RampEndZ, side * w - 0.22f, side * w + 0.22f, z => LaneSpec.SurfaceY(z) + 0.02f, dY, 1f, 0.1f);
                edgeMeshes.Add(edge);
                edges.Add(new CombineInstance { mesh = edge, transform = Matrix4x4.identity });
            }
            s.Merge("Binding", edges, s.Lit(new Color(0.008f, 0.02f, 0.015f), 0.2f), g);
            foreach (Mesh m in edgeMeshes) UnityEngine.Object.Destroy(m);

            // The print: tee circles and distances, on the flat.
            GameObject marks = s.Primitive(PrimitiveType.Quad, "Marks", s.Own(Make.Transparent(new Color(0.86f, 0.88f, 0.86f, 1f), 0.05f, Marks())), g);
            marks.transform.localPosition = new Vector3(0, LaneSpec.MatY + 0.025f, (MatEndZ + MarksEndZ) * 0.5f);
            marks.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            marks.transform.localScale = new Vector3(w * 2f, MarksEndZ - MatEndZ, 1f);

            // The ball return down the right: a black channel between two rails.
            const float returnStart = -100f, shellFront = -11f;
            float returnLen = shellFront - 0.06f - returnStart, returnMid = returnStart + returnLen * 0.5f;
            s.Box("Ball return", new Vector3(w + 1.9f, floorY + 0.12f, returnMid), new Vector3(1.9f, 0.24f, returnLen), s.Lit(new Color(0.05f, 0.075f, 0.065f), 0.35f, 0.2f), g);
            s.Boxes("Return rails", new List<(Vector3, Vector3, Quaternion)>
            {
                (new Vector3(w + 0.85f, floorY + 0.3f, returnMid), new Vector3(0.22f, 0.6f, returnLen), Quaternion.identity),
                (new Vector3(w + 2.95f, floorY + 0.3f, returnMid), new Vector3(0.22f, 0.6f, returnLen), Quaternion.identity),
            }, s.Lit(new Color(0.012f, 0.012f, 0.014f), 0.45f, 0.2f), g);

            BuildBox(s, g, floorY);
        }

        // The console's measures, as World.cs has them.
        const float WallZ = LaneSpec.RampEndZ + 0.8f, BoxInner = LaneSpec.MatHalfWidth + 0.3f, BoxOuter = BoxInner + 0.8f, BoxTop = 14f;
        const float ShellFrontZ = -11f, ShellSheet = 0.12f, ShellRadius = 1.1f, HeaderBottom = 12.6f, LinerInset = 0.3f;

        /// <summary>The Puttr V2 box, as the arcade builds it (WorldConsole.cs): one sheet of matte black powder coat
        /// folded over the top, the inner panels lit by the light strip, the header with its two clips, the camera
        /// housing, the lid's window slot and etched mark, the ball exit on the right; closed at the back.</summary>
        static void BuildBox(Live3D.Stage s, Transform g, float floorY)
        {
            Material paint = s.Lit(new Color(0.034f, 0.034f, 0.037f), 0.38f, 0.15f);
            Material inner = s.Lit(new Color(0.15f, 0.155f, 0.16f), 0.45f, 0.3f); // bare sheet, lit by the strip
            Material trim = s.Lit(new Color(0.07f, 0.072f, 0.076f), 0.62f, 0.6f);
            Material glass = s.Lit(new Color(0.006f, 0.007f, 0.008f), 0.93f, 0f);

            float backZ = WallZ + 0.6f, shellLen = backZ - ShellFrontZ;
            s.Mesh("Shell", Wrap(BoxOuter, floorY, BoxTop, ShellRadius, ShellSheet, ShellFrontZ, backZ, 10), paint, g);

            float wallIn = BoxInner - 0.06f, ceiling = BoxTop - ShellSheet;
            float linerZ0 = ShellFrontZ + LinerInset, linerLen = backZ - linerZ0;
            var liners = new List<(Vector3, Vector3, Quaternion)>();
            for (int side = -1; side <= 1; side += 2)
            {
                liners.Add((new Vector3(side * (wallIn + 0.03f), (floorY + ceiling) * 0.5f, linerZ0 + linerLen * 0.5f), new Vector3(0.06f, ceiling - floorY, linerLen), Quaternion.identity));
                s.Mesh("Flange", Flange(side, wallIn, BoxOuter, floorY, BoxTop, ShellRadius, ShellSheet, linerZ0, 10), paint, g);
            }
            liners.Add((new Vector3(0, ceiling - 0.04f, linerZ0 + linerLen * 0.5f), new Vector3(wallIn * 2f, 0.08f, linerLen), Quaternion.identity));
            s.Boxes("Liners", liners, inner, g);
            // The back: the felt behind the hole, and the face under the end of the mat.
            s.Box("Back", new Vector3(0, (floorY + ceiling) * 0.5f, backZ - 0.3f), new Vector3(wallIn * 2f, ceiling - floorY, 0.4f), s.Lit(new Color(0.03f, 0.06f, 0.045f), 0.1f), g);

            float headerH = ceiling - 0.08f - HeaderBottom, headerZ = ShellFrontZ + 0.22f;
            s.Mesh("Header", Make.RoundedBox(new Vector3(wallIn * 2f, headerH, 0.16f), 0.05f), paint, g).transform.localPosition = new Vector3(0, HeaderBottom + headerH * 0.5f, headerZ);
            s.Mesh("Camera", Make.RoundedBox(new Vector3(7.5f, 0.6f, 1.6f), 0.12f), paint, g).transform.localPosition = new Vector3(0, ceiling - 0.38f, headerZ + 1.6f);
            var trims = new List<(Vector3, Vector3, Quaternion)>();
            for (int side = -1; side <= 1; side += 2) trims.Add((new Vector3(side * 6.2f, HeaderBottom + 0.32f, headerZ - 0.13f), new Vector3(0.9f, 0.42f, 0.12f), Quaternion.identity));
            float slotZ = WallZ - shellLen * 0.42f;
            trims.Add((new Vector3(0, BoxTop + 0.004f, slotZ), new Vector3(14.6f, 0.04f, 1.8f), Quaternion.identity));
            s.Boxes("Trim", trims, trim, g);
            var glassBits = new List<(Vector3, Vector3, Quaternion)>
            {
                (new Vector3(0, ceiling - 0.69f, headerZ + 1.6f), new Vector3(1.4f, 0.03f, 0.7f), Quaternion.identity),
                (new Vector3(0, BoxTop + 0.03f, slotZ), new Vector3(14.0f, 0.01f, 1.25f), Quaternion.identity),
            };
            s.Boxes("Glass", glassBits, glass, g);
            // The light strip under the lid, a cool white as in the photographs.
            s.Box("Light bar", new Vector3(0, ceiling - 0.13f, 0.5f), new Vector3(BoxInner * 2 - 3f, 0.08f, 0.9f), s.Emissive(Color.black, new Color(0.82f, 0.95f, 1f) * 3f), g);

            // The mark etched on the lid (a resource: shared, not this scene's to free).
            var lidLogo = Resources.Load<Texture2D>("PuttrArcade/Console/LidLogo");
            if (lidLogo == null) lidLogo = Resources.Load<Texture2D>("PuttrArcade/LogoMarkWhite");
            if (lidLogo != null)
            {
                const float lidW = 4.6f;
                GameObject mark = s.Primitive(PrimitiveType.Quad, "Lid mark", s.Own(Make.Transparent(new Color(0.3f, 0.285f, 0.26f, 0.9f), 0.5f, lidLogo)), g);
                mark.transform.localPosition = new Vector3(0, BoxTop + 0.02f, slotZ - 0.9f - lidW * 0.5f);
                mark.transform.localRotation = Quaternion.Euler(90f, 0, 0);
                mark.transform.localScale = new Vector3(lidW, lidW * lidLogo.height / lidLogo.width, 1f);
            }

            // The ball exit, low on the right near the back.
            Quaternion facingRight = Quaternion.Euler(0f, 0f, -90f);
            Vector3 exit = new Vector3(BoxOuter + 0.012f, floorY + 1.7f, WallZ - 2.6f);
            GameObject rim = s.Mesh("Exit rim", Make.Ring(1.05f, 1.3f, 40), s.Lit(new Color(0.13f, 0.13f, 0.14f), 0.55f, 0.4f), g);
            rim.transform.localPosition = exit; rim.transform.localRotation = facingRight;
            GameObject hole = s.Mesh("Exit", Make.Disc(1.06f, 40), glass, g);
            hole.transform.localPosition = exit + Vector3.right * 0.004f; hole.transform.localRotation = facingRight;
        }

        /// <summary>One sheet folded over the top: a U in X/Y, square at the bottom, rounded over the top, extruded along
        /// Z. Copied from WorldConsole.cs (private there).</summary>
        static Mesh Wrap(float halfW, float y0, float y1, float radius, float thick, float z0, float z1, int seg)
        {
            var outer = new List<Vector2>(); var innerPts = new List<Vector2>(); var norm = new List<Vector2>();
            float cy = y1 - radius;
            void Point(float x, float y, Vector2 n) { outer.Add(new Vector2(x, y)); innerPts.Add(new Vector2(x, y) - n * thick); norm.Add(n); }
            Point(-halfW, y0, Vector2.left);
            for (int i = 0; i <= seg; i++) { float a = Mathf.PI * (1f - 0.5f * i / seg); var n = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); Point(-halfW + radius + n.x * radius, cy + n.y * radius, n); }
            for (int i = 0; i <= seg; i++) { float a = Mathf.PI * (0.5f - 0.5f * i / seg); var n = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); Point(halfW - radius + n.x * radius, cy + n.y * radius, n); }
            Point(halfW, y0, Vector2.right);

            var verts = new List<Vector3>(); var normals = new List<Vector3>(); var tris = new List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd)
            {
                int st = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                normals.Add(na); normals.Add(nb); normals.Add(nc); normals.Add(nd);
                bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), na + nb + nc + nd) < 0f;
                if (!flip) { tris.Add(st); tris.Add(st + 1); tris.Add(st + 2); tris.Add(st); tris.Add(st + 2); tris.Add(st + 3); }
                else { tris.Add(st); tris.Add(st + 2); tris.Add(st + 1); tris.Add(st); tris.Add(st + 3); tris.Add(st + 2); }
            }
            Vector3 P(Vector2 p, float z) => new Vector3(p.x, p.y, z);
            Vector3 N(Vector2 n) => new Vector3(n.x, n.y, 0f);
            for (int k = 0; k < outer.Count - 1; k++)
            {
                Vector3 na = N(norm[k]), nb = N(norm[k + 1]);
                Quad(P(outer[k], z0), P(outer[k + 1], z0), P(outer[k + 1], z1), P(outer[k], z1), na, nb, nb, na);
                Quad(P(innerPts[k], z0), P(innerPts[k + 1], z0), P(innerPts[k + 1], z1), P(innerPts[k], z1), -na, -nb, -nb, -na);
                Quad(P(outer[k], z0), P(outer[k + 1], z0), P(innerPts[k + 1], z0), P(innerPts[k], z0), Vector3.back, Vector3.back, Vector3.back, Vector3.back);
                Quad(P(outer[k], z1), P(outer[k + 1], z1), P(innerPts[k + 1], z1), P(innerPts[k], z1), Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward);
            }
            foreach (int k in new[] { 0, outer.Count - 1 })
                Quad(P(outer[k], z0), P(innerPts[k], z0), P(innerPts[k], z1), P(outer[k], z1), Vector3.down, Vector3.down, Vector3.down, Vector3.down);
            var mesh = new Mesh { name = "Shell" };
            mesh.SetVertices(verts); mesh.SetNormals(normals); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>The front edge of one side's cavity, between the inner panel and the sheet, up into the fold.
        /// Copied from WorldConsole.cs.</summary>
        static Mesh Flange(int side, float inX, float halfW, float y0, float y1, float radius, float thick, float z, int seg)
        {
            float r = radius - thick, cx = halfW - radius, cy = y1 - radius, sheetIn = halfW - thick;
            var pts = new List<Vector2> { new Vector2(inX, y0), new Vector2(sheetIn, y0) };
            float aEnd = Mathf.Acos(Mathf.Clamp((inX - cx) / r, -1f, 1f));
            for (int i = 0; i <= seg; i++) { float a = aEnd * i / seg; pts.Add(new Vector2(cx + Mathf.Cos(a) * r, cy + Mathf.Sin(a) * r)); }
            var verts = new List<Vector3>(); var tris = new List<int>();
            foreach (Vector2 p in pts) verts.Add(new Vector3(side * p.x, p.y, z));
            for (int i = 1; i < pts.Count - 1; i++)
            {
                Vector3 a = verts[0], b = verts[i], c = verts[i + 1];
                if (Vector3.Cross(b - a, c - a).z < 0f) { tris.Add(0); tris.Add(i); tris.Add(i + 1); }
                else { tris.Add(0); tris.Add(i + 1); tris.Add(i); }
            }
            var mesh = new Mesh { name = "Flange" };
            mesh.SetVertices(verts); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ── Today's session ──────────────────────────────────────────────────

        /// <summary>How the session's scene is framed: a phone's tall hero, a tablet's tall pane, or a wide band (a phone on its side).</summary>
        public enum Frame { Phone, Pane, Band }

        /// <summary>The mat and box; with a session, its spots glow (a count beside each), and a ball rolls the focus putt into the hole.</summary>
        // The camera distance at which the whole mat, the Puttr logo at the Player's end to the top of the box, sits in
        // the picture above the session's words (the top half), worked out for each size of the picture.
        static readonly Dictionary<Live3D.Stage, (float w, float h, float d)> Fits = new Dictionary<Live3D.Stage, (float, float, float)>();
        static float Fit(Live3D.Stage s, float yaw)
        {
            float w = s.View.contentRect.width, h = s.View.contentRect.height;
            if (w <= 0f || h <= 0f) return 172f;
            if (Fits.TryGetValue(s, out var f) && Mathf.Approximately(f.w, w) && Mathf.Approximately(f.h, h)) return f.d;
            // The picture's shift is applied at draw time (Live3D): apply it here too, or the fit is for an unshifted picture.
            Camera cam = s.Camera;
            Matrix4x4 m = Matrix4x4.Perspective(cam.fieldOfView, w / h, cam.nearClipPlane, cam.farClipPlane);
            m[0, 2] = -2f * s.ShiftX;
            m[1, 2] = -2f * s.ShiftY;
            cam.projectionMatrix = m;
            Vector3[] ends = { new Vector3(-12f, 0f, MatEndZ), new Vector3(12f, 0f, MatEndZ), new Vector3(0f, 0f, -5f) };
            float best = 400f;
            for (float d = 120f; d <= 400f; d += 5f)
            {
                s.Orbit(new Vector3(0, 0, -64f), 0.24f, 0.52f, d);
                bool fits = true;
                foreach (Vector3 p in ends)
                {
                    Vector2? at = s.Project(p);
                    if (at == null || at.Value.y > h * 0.5f || at.Value.y < h * 0.03f || at.Value.x < 4f || at.Value.x > w - 4f) { fits = false; break; }
                }
                if (fits) { best = d; break; }
            }
            Fits[s] = (w, h, best);
            return best;
        }

        public static Action<Live3D.Stage> Today(List<TournamentMath.Dealt> putts, Frame frame)
        {
            return s =>
            {
                BuildMat(s, out Material ring);
                // The scene sits above the session's words (to their right on a wide band).
                // Phone and tablet pane: far enough back and high enough that the whole mat, the Puttr logo at the
                // Player's end included, sits above the session's words.
                s.ShiftY = frame == Frame.Band ? 0.04f : frame == Frame.Pane ? 0.3f : 0.32f;
                s.ShiftX = frame == Frame.Band ? 0.24f : 0f;
                s.PinLimit = frame == Frame.Band ? 1f : 0.5f;
                Color ringBase = ring.GetColor("_Color");
                // The distances are printed on the mat and the mark is on the box: no labels for them.

                Transform focusRing = null, ball = null;
                Vector3 from = Vector3.zero;
                if (putts != null && putts.Count > 0)
                {
                    var counts = new Dictionary<(int, int), int>();
                    var order = new List<(int feet, int tee)>();
                    foreach (TournamentMath.Dealt p in putts)
                    {
                        var key = (p.Feet, p.Tee);
                        if (!counts.ContainsKey(key)) { counts[key] = 0; order.Add(key); }
                        counts[key]++;
                    }
                    Material glow = s.Glow(new Color(Spark.r, Spark.g, Spark.b, 0.9f));
                    Material fill = s.Glow(new Color(Spark.r, Spark.g, Spark.b, 0.16f));
                    Material focus = s.Glow(new Color(0.75f, 1f, 0.55f, 1f));
                    for (int i = 0; i < order.Count; i++)
                    {
                        (int feet, int tee) = order[i];
                        Vector3 at = Spot(feet, tee, 0.03f);
                        bool first = i == 0;
                        // Lit round the printed tee circle, which still shows inside.
                        GameObject r = s.Mesh("Spot", Make.Ring(1.9f, first ? 2.7f : 2.4f), first ? focus : glow);
                        r.transform.localPosition = at;
                        s.Mesh("Spot fill", Make.Disc(1.9f), fill).transform.localPosition = at + Vector3.up * 0.01f;
                        if (first) focusRing = r.transform;
                        s.Pin(Ui.Text("×" + counts[(feet, tee)], "imp-pin-count", Ui.Bold), at + new Vector3(tee == 3 ? -5.4f : 5.4f, 0, 0));
                    }
                    from = Spot(putts[0].Feet, putts[0].Tee, LaneSpec.BallRadius);
                    ball = s.Primitive(PrimitiveType.Sphere, "Ball", s.Lit(new Color(0.95f, 0.95f, 0.93f), 0.65f)).transform;
                    ball.localScale = Vector3.one * LaneSpec.BallRadius * 2f;
                    ball.localPosition = from;
                }

                const float Roll = 2.1f, Drop = 0.3f, Rest = 1.5f, Cycle = Roll + Drop + Rest;
                float bend = from.x * 0.45f + (from.x == 0f ? 1.5f : 0f);
                s.Tick = dt =>
                {
                    float t = s.Time;
                    // A slow orbit round the middle of the mat, the box at the top of the picture.
                    // A slow orbit round the middle of the mat, the box at the top of the picture.
                    float yaw = (frame == Frame.Band ? 0.9f : 0.24f) + 0.2f * Mathf.Sin(t * 0.13f) + s.View.Yaw;
                    if (frame == Frame.Band) s.Orbit(new Vector3(0, 0, -62f), yaw, 0.6f, 150f);
                    else s.Orbit(new Vector3(0, 0, -64f), yaw, 0.52f, Fit(s, yaw));
                    if (ball == null) return false;
                    float c = t % Cycle;
                    bool rolling = c < Roll + Drop;
                    if (c < Roll)
                    {
                        float u = 1f - (1f - c / Roll) * (1f - c / Roll);   // slowing as it goes
                        float x = Mathf.Lerp(from.x, 0f, u) + Mathf.Sin(u * Mathf.PI) * bend;
                        // Along the flat, then up the curve and the ramp to the hole.
                        float z = Mathf.Lerp(from.z, 0f, u);
                        ball.localPosition = new Vector3(x, SurfY(z), z) + LaneSpec.SurfaceNormal(z) * LaneSpec.BallRadius;
                        ball.gameObject.SetActive(true);
                    }
                    else if (c < Roll + Drop)
                    {
                        float k = (c - Roll) / Drop;
                        ball.localPosition = new Vector3(0, SurfY(0f), 0) + LaneSpec.SurfaceNormal(0f) * (LaneSpec.BallRadius - k * 3f);
                    }
                    else ball.gameObject.SetActive(false);
                    // The hole's ring flashes as it drops; the focus spot breathes.
                    float flash = c > Roll && c < Roll + 1.1f ? 1f - (c - Roll) / 1.1f : 0f;
                    ring.SetColor("_Color", Color.Lerp(ringBase, new Color(0.8f, 1f, 0.6f, 0.9f), flash));
                    if (focusRing != null) focusRing.localScale = Vector3.one * (1f + 0.08f * Mathf.Sin(t * 3f));
                    // Smooth for the first two putts it rolls; after that it keeps going at the app's resting rate.
                    return rolling && s.Age < Cycle * 2f;
                };
            };
        }

        // ── Last 30 days ─────────────────────────────────────────────────────

        static Color Heat(int weighted)
        {
            float t = Mathf.Clamp01(weighted / 100f);
            return t < 0.5f ? Color.Lerp(Ui.Red, Ui.Amber, t * 2f) : Color.Lerp(Ui.Amber, Ui.Bright, (t - 0.5f) * 2f);
        }

        /// <summary>One column per spot, standing on its tee circle on the real mat: height and colour are make % against the Tour's.</summary>
        public static Action<Live3D.Stage> Month(List<AnalysisMath.Cell> grid)
        {
            return s =>
            {
                BuildMat(s, out _);
                var columns = new List<(Transform t, float height)>();
                foreach (AnalysisMath.Cell c in grid)
                {
                    bool any = c.Total > 0;
                    if (!any) continue; // an untried spot is just its printed tee circle
                    float height = 1.5f + 30f * Mathf.Clamp01(c.Weighted / 110f);
                    Color colour = Heat(c.Weighted);
                    // A round column on the tee circle, slim enough that the printed distance shows in front of it.
                    GameObject col = s.Primitive(PrimitiveType.Cylinder, "Column", s.Lit(colour * 0.9f, 0.35f));
                    col.transform.localPosition = Spot(c.Feet, c.Tee);
                    columns.Add((col.transform, height));
                }
                s.Tick = dt =>
                {
                    float t = s.Time;
                    s.Orbit(new Vector3(0, 8f, -62f), 0.5f + 0.22f * Mathf.Sin(t * 0.14f) + s.View.Yaw, 0.6f, 138f);
                    // They grow up out of the mat the first time they are seen.
                    float grow = Mathf.Clamp01(s.Age / 1.4f);
                    grow = 1f - (1f - grow) * (1f - grow) * (1f - grow);
                    foreach ((Transform col, float height) in columns)
                    {
                        float h = Mathf.Max(0.05f, height * grow);
                        Vector3 p = col.localPosition;
                        col.localScale = new Vector3(4.4f, h * 0.5f, 4.4f);
                        col.localPosition = new Vector3(p.x, Top + h * 0.5f, p.z);
                    }
                    return s.Age < 1.6f;
                };
            };
        }

        // ── Your stroke ──────────────────────────────────────────────────────

        /// <summary>A putter swinging like a pendulum along the target line, its head leaving a trail of light; the ball rolls away.</summary>
        public static Action<Live3D.Stage> Stroke()
        {
            return s =>
            {
                s.Background = new Color32(13, 17, 15, 255);
                s.ShiftY = 0.12f;
                s.Light(new Color(1f, 0.97f, 0.92f), 1.5f, new Vector3(45f, 30f, 0f));
                s.Light(new Color(0.6f, 0.75f, 1f), 0.5f, new Vector3(15f, 220f, 0f));
                s.Box("Floor", new Vector3(0, -0.6f, 0), new Vector3(900, 1, 900), s.Lit(new Color(0.02f, 0.028f, 0.024f), 0.3f));
                Material grid = s.Glow(new Color(Spark.r, Spark.g, Spark.b, 0.06f));
                var lines = new List<(Vector3, Vector3, Quaternion)>();
                for (int i = -10; i <= 10; i++)
                {
                    lines.Add((new Vector3(i * 16f, -0.08f, 40), new Vector3(0.25f, 0.02f, 600), Quaternion.identity));
                    lines.Add((new Vector3(0, -0.08f, 40 + i * 16f), new Vector3(600, 0.02f, 0.25f), Quaternion.identity));
                }
                s.Boxes("Grid", lines, grid);
                // The mat runs away from the putter along +x.
                Carpet();
                Material carpet = s.Own(Make.Lit(CarpetTint, 0.1f, 0f, _carpet, _carpetNormal, 0.45f));
                carpet.SetTextureScale("_BaseMap", new Vector2(220f / 12.8f, 24f / 12.8f));
                carpet.SetTextureScale("_BumpMap", new Vector2(220f / 12.8f, 24f / 12.8f));
                s.Box("Mat", new Vector3(50f, Top * 0.5f, 0), new Vector3(220f, Top, 24f), carpet);
                s.Box("Aim line", new Vector3(50f, Top + 0.02f, 0), new Vector3(200f, 0.02f, 0.25f), s.Glow(new Color(1f, 1f, 1f, 0.12f)));

                // The putter hangs from a pivot (the shoulders) and swings in the x-y plane.
                const float Shaft = 35f;
                Transform pivot = s.Group("Pivot");
                pivot.localPosition = new Vector3(0, Top + 1f + Shaft, 0);
                Material steel = s.Lit(new Color(0.75f, 0.77f, 0.8f), 0.85f, 0.9f), black = s.Lit(new Color(0.03f, 0.03f, 0.035f), 0.5f);
                GameObject shaft = s.Primitive(PrimitiveType.Cylinder, "Shaft", steel, pivot);
                shaft.transform.localPosition = new Vector3(0, -Shaft * 0.5f, 0);
                shaft.transform.localScale = new Vector3(0.36f, Shaft * 0.5f, 0.36f);
                GameObject grip = s.Primitive(PrimitiveType.Cylinder, "Grip", black, pivot);
                grip.transform.localPosition = new Vector3(0, -5.5f, 0);
                grip.transform.localScale = new Vector3(1f, 5.5f, 1f);
                Transform head = s.Group("Head", pivot);
                head.localPosition = new Vector3(0, -Shaft, 0);
                s.Box("Head", new Vector3(0, 0, 0), new Vector3(2.4f, 1.1f, 4.6f), s.Lit(new Color(0.12f, 0.13f, 0.14f), 0.7f, 0.6f), head);
                s.Box("Face", new Vector3(1.22f, 0, 0), new Vector3(0.06f, 0.9f, 4.3f), steel, head);
                s.Box("Sight line", new Vector3(0, 0.56f, 0), new Vector3(1.8f, 0.02f, 0.16f), s.Glow(new Color(1f, 1f, 1f, 0.9f), false), head);

                Transform ball = s.Primitive(PrimitiveType.Sphere, "Ball", s.Lit(new Color(0.95f, 0.95f, 0.93f), 0.65f)).transform;
                ball.localScale = Vector3.one * LaneSpec.BallRadius * 2f;
                const float BallX = 2.1f;

                // The trail: where the head has been in the last half second.
                var trail = new GameObject("Trail").AddComponent<LineRenderer>();
                trail.transform.SetParent(s.Root, false);
                trail.useWorldSpace = false;
                trail.alignment = LineAlignment.View;
                trail.numCapVertices = 2;
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                trail.sharedMaterial = s.Glow(new Color(Spark.r, Spark.g, Spark.b, 1f));
                trail.widthCurve = new AnimationCurve(new Keyframe(0, 0.3f), new Keyframe(1, 2.2f));
                var fade = new Gradient();
                fade.SetKeys(new[] { new GradientColorKey(Spark, 0), new GradientColorKey(new Color(0.8f, 1f, 0.6f), 1) }, new[] { new GradientAlphaKey(0f, 0), new GradientAlphaKey(0.95f, 1) });
                trail.colorGradient = fade;
                var points = new List<Vector3>();
                var times = new List<float>();

                // One stroke: address, back, through (impact at the bottom), hold, and back to address.
                const float Cycle = 3.4f, Back = -13f, Through = 15f;
                float Angle(float c)
                {
                    if (c < 0.3f) return 0f;
                    if (c < 1.05f) return Back * Mathf.SmoothStep(0f, 1f, (c - 0.3f) / 0.75f);
                    if (c < 1.45f) { float u = (c - 1.05f) / 0.4f; return Mathf.Lerp(Back, Through, u * u * (3f - 2f * u)); }
                    if (c < 2.1f) return Through;
                    if (c < 2.9f) return Through * (1f - Mathf.SmoothStep(0f, 1f, (c - 2.1f) / 0.8f));
                    return 0f;
                }
                // Impact: where the head passes the bottom.
                float impact = 1.05f + 0.4f * 0.43f;
                s.Tick = dt =>
                {
                    float t = s.Time, c = t % Cycle;
                    pivot.localRotation = Quaternion.Euler(0, 0, Angle(c));
                    Vector3 h = s.Root.InverseTransformPoint(head.position);
                    points.Add(h); times.Add(t);
                    while (times.Count > 0 && t - times[0] > 0.7f) { times.RemoveAt(0); points.RemoveAt(0); }
                    trail.positionCount = points.Count;
                    trail.SetPositions(points.ToArray());
                    // The ball rolls from impact, slowing, and comes back for the next one.
                    float roll = c - impact;
                    float x = roll <= 0f ? 0f : 80f * (1f - Mathf.Exp(-roll * 1.6f));
                    ball.localPosition = new Vector3(BallX + x, Top + LaneSpec.BallRadius, 0);
                    ball.gameObject.SetActive(c < Cycle - 0.25f);
                    float yaw = -0.15f + 0.12f * Mathf.Sin(t * 0.2f) + s.View.Yaw;
                    s.Orbit(new Vector3(14f, 12f, 0f), yaw, 0.22f, 70f);
                    bool swinging = c > 0.25f && c < 2.95f;
                    return swinging && s.Age < Cycle * 2f;
                };
            };
        }

        // ── PQS: a 3D donut ──────────────────────────────────────────────────

        /// <summary>The make index's share of the score and the accuracy's, as the donut colours them (the orbs' keys too).</summary>
        public static readonly Color IndexColour = new Color32(0x7A, 0xD4, 0x46, 0xFF), AccuracyColour = new Color32(0x3E, 0x9C, 0x8A, 0xFF);

        /// <summary>A slice of a thick ring lying flat (x-z), from startDeg clockwise from +z as seen from above,
        /// between y0 and y1: its top, both walls and both ends, flat shaded.</summary>
        static Mesh Slice(float rIn, float rOut, float y0, float y1, float startDeg, float sweepDeg)
        {
            int n = Mathf.Max(2, Mathf.CeilToInt(sweepDeg / 4f));
            var verts = new List<Vector3>(); var normals = new List<Vector3>(); var tris = new List<int>();
            Vector3 Dir(float deg) { float a = deg * Mathf.Deg2Rad; return new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)); }
            void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 n0, Vector3 n1, Vector3 n2, Vector3 n3)
            {
                int st = verts.Count;
                verts.Add(p0); verts.Add(p1); verts.Add(p2); verts.Add(p3);
                normals.Add(n0); normals.Add(n1); normals.Add(n2); normals.Add(n3);
                bool flip = Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), n0 + n1 + n2 + n3) < 0f;
                if (!flip) { tris.Add(st); tris.Add(st + 1); tris.Add(st + 2); tris.Add(st); tris.Add(st + 2); tris.Add(st + 3); }
                else { tris.Add(st); tris.Add(st + 2); tris.Add(st + 1); tris.Add(st); tris.Add(st + 3); tris.Add(st + 2); }
            }
            Vector3 upY = Vector3.up * y1, downY = Vector3.up * y0;
            for (int i = 0; i < n; i++)
            {
                Vector3 d0 = Dir(startDeg + sweepDeg * i / n), d1 = Dir(startDeg + sweepDeg * (i + 1) / n);
                Quad(d0 * rIn + upY, d0 * rOut + upY, d1 * rOut + upY, d1 * rIn + upY, Vector3.up, Vector3.up, Vector3.up, Vector3.up);
                Quad(d0 * rOut + downY, d0 * rOut + upY, d1 * rOut + upY, d1 * rOut + downY, d0, d0, d1, d1);
                Quad(d0 * rIn + downY, d0 * rIn + upY, d1 * rIn + upY, d1 * rIn + downY, -d0, -d0, -d1, -d1);
            }
            foreach ((float deg, float side) in new[] { (startDeg, -1f), (startDeg + sweepDeg, 1f) })
            {
                Vector3 d = Dir(deg), cap = Vector3.Cross(Vector3.up, d) * -side;
                Quad(d * rIn + downY, d * rOut + downY, d * rOut + upY, d * rIn + upY, cap, cap, cap, cap);
            }
            var mesh = new Mesh { name = "Slice" };
            mesh.SetVertices(verts); mesh.SetNormals(normals); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>The score out of 100 as a donut with depth, lying a little tilted toward the camera: the make
        /// index's part and the accuracy's part (PQS is 65% one and 35% the other) standing up from a low dark track
        /// for the rest. Still: it draws when it comes on screen and not again.</summary>
        public static Action<Live3D.Stage> PqsDonut(int score, float makeIndex, float accuracy)
        {
            return s =>
            {
                s.Background = new Color32(24, 26, 28, 255); // the card's own colour
                s.Light(new Color(1f, 0.98f, 0.95f), 1.25f, new Vector3(60f, -25f, 0f));
                s.Light(new Color(0.65f, 0.78f, 1f), 0.4f, new Vector3(20f, 150f, 0f));
                const float RIn = 7.3f, ROut = 10f, Gap = 1.6f;
                float total = 360f * Mathf.Clamp01(score / 100f);
                float a = 0.65f * Mathf.Max(0f, makeIndex), b = 0.35f * Mathf.Max(0f, accuracy);
                float indexSweep = a + b > 0f ? total * a / (a + b) : 0f, accuracySweep = total - indexSweep;
                float at = 0f;
                if (indexSweep > Gap * 2f) s.Mesh("Make index", Slice(RIn, ROut, 0f, 1.8f, at, indexSweep - Gap), s.Lit(IndexColour * 0.85f, 0.35f));
                at += indexSweep;
                if (accuracySweep > Gap * 2f) s.Mesh("Accuracy", Slice(RIn, ROut, 0f, 1.8f, at, accuracySweep - Gap), s.Lit(AccuracyColour * 0.85f, 0.35f));
                at += accuracySweep;
                if (360f - at > Gap * 2f) s.Mesh("Rest", Slice(RIn + 0.25f, ROut - 0.25f, 0f, 0.5f, at, 360f - at - Gap), s.Lit(new Color(0.13f, 0.14f, 0.15f), 0.3f));
                // Looking down at it from in front, the score's start at twelve o'clock.
                s.Look(Quaternion.Euler(56f, 0f, 0f) * new Vector3(0, 0, -35f) + Vector3.up * 0.9f, Vector3.up * 0.9f);
            };
        }

        // ── UI FX: textures made once ────────────────────────────────────────

        static readonly Dictionary<string, Texture2D> Made = new Dictionary<string, Texture2D>();

        static Texture2D Once(string key, int w, int h, Func<float, float, Color> at)
        {
            if (Made.TryGetValue(key, out Texture2D t) && t != null) return t;
            t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = key };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = at((x + 0.5f) / w, (y + 0.5f) / h);
            t.SetPixels(px);
            t.Apply(false, true);
            Made[key] = t;
            return t;
        }

        /// <summary>The page colour, clear at the top and solid at the bottom (y 0 is the bottom row).</summary>
        public static Texture2D FadeUp => Once("fade-up", 1, 128, (x, y) => new Color(0.043f, 0.047f, 0.051f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((0.86f - y) / 0.52f))));
        /// <summary>The page colour, solid at the top and clear below: behind the title bar.</summary>
        public static Texture2D FadeDown => Once("fade-down", 1, 64, (x, y) => new Color(0.043f, 0.047f, 0.051f, Mathf.SmoothStep(0f, 0.94f, Mathf.Clamp01((y - 0.05f) / 0.5f))));
        /// <summary>The page colour, solid on the left and clear on the right: behind words on the left of a wide scene.</summary>
        public static Texture2D FadeRight => Once("fade-right", 128, 1, (x, y) => new Color(0.043f, 0.047f, 0.051f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((0.68f - x) / 0.3f))));
        /// <summary>Dark at the bottom for words over a picture.</summary>
        public static Texture2D Shade => Once("shade", 1, 64, (x, y) => new Color(0.02f, 0.024f, 0.027f, Mathf.SmoothStep(0f, 0.95f, Mathf.Clamp01((0.62f - y) / 0.6f))));
        /// <summary>Dark on the left for a ticket's words.</summary>
        public static Texture2D ShadeLeft => Once("shade-left", 64, 1, (x, y) => new Color(0.02f, 0.024f, 0.027f, Mathf.Lerp(0.95f, 0.12f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((x - 0.25f) / 0.6f)))));
        /// <summary>A diagonal sheen across a card.</summary>
        public static Texture2D Sheen => Once("sheen", 64, 64, (x, y) =>
        {
            float d = Mathf.Abs((x - y * 0.6f) - 0.25f);
            return new Color(1f, 1f, 1f, 0.09f * Mathf.Clamp01(1f - d / 0.14f));
        });
        /// <summary>The columns' key: red at 0, amber at 50, green at Tour level.</summary>
        public static Texture2D HeatBar => Once("heat", 64, 1, (x, y) => Heat(Mathf.RoundToInt(x * 100f)));
        /// <summary>A soft shadow for under a card (nine-sliced).</summary>
        public static Texture2D Shadow => Once("shadow", 64, 64, (x, y) =>
        {
            float dx = Mathf.Max(0f, Mathf.Abs(x - 0.5f) * 2f - 0.25f) / 0.75f, dy = Mathf.Max(0f, Mathf.Abs(y - 0.5f) * 2f - 0.25f) / 0.75f;
            float d = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
            return new Color(0f, 0f, 0f, 0.7f * (1f - d) * (1f - d));
        });
        /// <summary>A soft green glow (nine-sliced), for under the main button.</summary>
        public static Texture2D GreenGlow => Once("green-glow", 64, 64, (x, y) =>
        {
            float dx = Mathf.Max(0f, Mathf.Abs(x - 0.5f) * 2f - 0.3f) / 0.7f, dy = Mathf.Max(0f, Mathf.Abs(y - 0.5f) * 2f - 0.3f) / 0.7f;
            float d = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
            return new Color(Spark.r, Spark.g, Spark.b, 0.45f * (1f - d) * (1f - d));
        });
        /// <summary>League Night's purple, lit from the right.</summary>
        public static Texture2D LeagueGlow(Color team) => Once("league-" + ColorUtility.ToHtmlStringRGB(team), 96, 48, (x, y) =>
        {
            float d = Mathf.Clamp01(Vector2.Distance(new Vector2(x * 2f, y), new Vector2(1.7f, 0.5f)) / 1.5f);
            Color dark = new Color(0.047f, 0.04f, 0.07f);
            return Color.Lerp(Color.Lerp(team, new Color(0.11f, 0.06f, 0.17f), 0.45f), dark, Mathf.SmoothStep(0f, 1f, d));
        });

        /// <summary>A card with a soft shadow under it: the wrapper goes in the layout, the card inside it.</summary>
        public static VisualElement Lifted(VisualElement card, string extra = null)
        {
            VisualElement shadow = Ui.Box("imp-shadow");
            shadow.style.backgroundImage = Shadow;
            shadow.pickingMode = PickingMode.Ignore;
            return Ui.Box("imp-lift" + (extra != null ? " " + extra : ""), shadow, card);
        }

        /// <summary>The main button with a green glow under it.</summary>
        public static VisualElement Glowing(VisualElement button)
        {
            VisualElement glow = Ui.Box("imp-glow");
            glow.style.backgroundImage = GreenGlow;
            glow.pickingMode = PickingMode.Ignore;
            return Ui.Box("imp-glowing", glow, button);
        }
    }

    /// <summary>The PQS trend (docs/design-guidelines.md, Charts): a solid bright green line over a flat grid. No glow, no fill.</summary>
    public sealed class TrendRibbon : VisualElement
    {
        readonly List<int> _values;

        public TrendRibbon(List<int> values)
        {
            _values = values;
            AddToClassList("imp-ribbon");
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext context)
        {
            float w = contentRect.width, h = contentRect.height;
            if (w <= 0 || h <= 0 || _values.Count < 2) return;
            Painter2D p = context.painter2D;
            // The grid: four level lines.
            p.lineWidth = 1f;
            p.strokeColor = Ui.Line;
            for (int i = 0; i < 4; i++)
            {
                float y = Mathf.Round(Mathf.Lerp(0.5f, h - 0.5f, i / 3f) - 0.5f) + 0.5f;
                p.BeginPath(); p.MoveTo(new Vector2(0f, y)); p.LineTo(new Vector2(w, y)); p.Stroke();
            }

            int min = int.MaxValue, max = int.MinValue;
            foreach (int v in _values) { min = Mathf.Min(min, v); max = Mathf.Max(max, v); }
            float lo = min - 4, hi = Mathf.Max(max + 4, lo + 10);
            var pts = new List<Vector2>();
            for (int i = 0; i < _values.Count; i++)
            {
                float u = i / (float)(_values.Count - 1);
                float k = (_values[i] - lo) / (hi - lo);
                pts.Add(new Vector2(Mathf.Lerp(8f, w - 8f, u), Mathf.Lerp(h - 8f, 8f, k)));
            }
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;
            p.lineWidth = 2f;
            p.strokeColor = Ui.Bright;
            p.BeginPath();
            p.MoveTo(pts[0]);
            for (int i = 1; i < pts.Count; i++) p.LineTo(pts[i]);
            p.Stroke();
            for (int i = 0; i < pts.Count; i++)
            {
                bool last = i == pts.Count - 1;
                p.fillColor = last ? Color.white : Ui.Bright;
                p.BeginPath(); p.Arc(pts[i], last ? 4f : 2f, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();
            }
        }
    }

    /// <summary>A ring round a coach's initials: how much of the time to the lesson's due date is left (or full, with no date).</summary>
    public sealed class DueRing : VisualElement
    {
        readonly float _left;
        readonly Color _colour;

        public DueRing(string initials, float left, Color colour)
        {
            _left = Mathf.Clamp01(left);
            _colour = colour;
            AddToClassList("imp-due");
            Add(Ui.Text(initials, "imp-due-text", Ui.Bold));
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext context)
        {
            float size = Mathf.Min(contentRect.width, contentRect.height);
            if (size <= 0f) return;
            Painter2D p = context.painter2D;
            float width = 4f, radius = size * 0.5f - width * 0.5f;
            p.lineWidth = width;
            p.lineCap = LineCap.Butt;
            p.strokeColor = Ui.Surface2;
            p.BeginPath(); p.Arc(contentRect.center, radius, Angle.Degrees(0), Angle.Degrees(360)); p.Stroke();
            if (_left <= 0f) return;
            p.lineCap = LineCap.Round;
            p.strokeColor = _colour;
            p.BeginPath(); p.Arc(contentRect.center, radius, Angle.Degrees(-90), Angle.Degrees(-90 + 360f * _left)); p.Stroke();
        }
    }
}

// Tracks course stills: the whole course from overhead, all its holes, for the course cards and the Track of
// the week (puttr-app tracks/CourseArt.js drew the course as line art; this paints it in the 3D world's own
// ground colours, with relief, trees, every hole's line, tee and flag, and the hole numbers laid over it).
//
// Painted on the CPU from the course polygons (no camera, no scene): a 1024 x 512 still costs a few frames,
// sliced so scrolling stays smooth. Cards ask for theirs as they scroll into view, one at a time; each still is
// kept in memory and as a JPEG in the cache folder (by slug and course version), so the next visit is instant.
//
// The course comes from GET /tracks/course/:slug (cached here and shared with the setup screen and Tee off);
// when that is refused (a Club course for a non-member) or fails, the catalog's small `art` outline is painted
// instead: fairways, greens and water, without hole lines.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public static class TracksArt
    {
        public const int CardW = 1024, CardH = 512;
        const string Rev = "r7";   // bump to repaint every cached still after a change to the painter (r7: framed tight on the holes)

        /// <summary>A painted course: the picture and where each hole's number goes (0..1 across, 0..1 down).</summary>
        public sealed class Still
        {
            public Texture2D Tex;
            internal Color32[] Pixels;   // until it is written to disk
            public readonly List<(int n, Vector2 at)> Holes = new List<(int, Vector2)>();
        }

        // ── Course documents, shared by the cards, the setup screen and Tee off ──

        static readonly Dictionary<string, TracksCourse> Courses = new Dictionary<string, TracksCourse>();
        static readonly Dictionary<string, List<Action<TracksCourse, string>>> Loading = new Dictionary<string, List<Action<TracksCourse, string>>>();

        /// <summary>The playable course for a slug: from memory, else GET /tracks/course/:slug. done(course or null, why it failed).</summary>
        public static IEnumerator Course(string slug, Action<TracksCourse, string> done)
        {
            if (string.IsNullOrEmpty(slug)) { done(null, "no_slug"); yield break; }
            if (Courses.TryGetValue(slug, out TracksCourse known)) { done(known, null); yield break; }
            if (Loading.TryGetValue(slug, out var waiting)) { waiting.Add(done); yield break; }
            var all = new List<Action<TracksCourse, string>> { done };
            Loading[slug] = all;
            TracksCourse got = null; string why = null;
            yield return Api.Get("/tracks/course/" + slug, d =>
            {
                try { got = TracksCourse.Read(d); } catch (Exception e) { why = "bad_course"; Perf.Note("Tracks course " + slug + " unreadable: " + e.Message); }
                if (got != null && string.IsNullOrEmpty(got.Slug)) got.Slug = slug;
            }, w => why = w ?? "failed");
            Loading.Remove(slug);
            if (got != null && got.Holes.Count > 0) Courses[slug] = got;
            else if (why == null) why = "no_holes";
            foreach (var a in all) a(got != null && got.Holes.Count > 0 ? got : null, why);
        }

        // ── Stills, painted as their cards come into view ──

        sealed class Want { public VisualElement El; public object Meta; public int W, H; public float Top, Bottom; public Action<Still> Done; public float Since; public bool Seen; public string Key; }

        static readonly Dictionary<string, Still> Stills = new Dictionary<string, Still>();
        static readonly List<Want> Wants = new List<Want>();
        static bool _pumping;
        static readonly bool Dbg = Array.IndexOf(Environment.GetCommandLineArgs(), "-app2TracksDebug") >= 0;

        static string Key(object meta, int w, int h, float top, float bottom) => J.Str(meta, "slug") + "-v" + J.Int(meta, "version", 1) + "-" + w + "x" + h + (top > 0 || bottom < 1 ? "-b" + Mathf.RoundToInt(top * 100) + "-" + Mathf.RoundToInt(bottom * 100) : "") + "-" + Rev;

        /// <summary>A still of the course for a card: done(still) at once when it is known, else once the element has scrolled
        /// into view (or near it) and the still is painted. Cards that leave the screen before their turn are dropped.</summary>
        public static void Show(VisualElement el, object meta, int w, int h, Action<Still> done, float top = 0f, float bottom = 1f)
        {
            if (string.IsNullOrEmpty(J.Str(meta, "slug"))) return;
            string key = Key(meta, w, h, top, bottom);
            if (Stills.TryGetValue(key, out Still s)) { done(s); return; }
            Wants.Add(new Want { El = el, Meta = meta, W = w, H = h, Top = top, Bottom = bottom, Done = done, Since = Time.realtimeSinceStartup, Key = key });
            if (!_pumping) App.Run(Pump());
        }

        static bool Visible(VisualElement el)
        {
            if (el.panel == null) return false;
            Rect r = el.worldBound, screen = el.panel.visualTree.worldBound;
            if (r.width <= 0 || r.height <= 0 || float.IsNaN(r.y)) return false;
            // Near counts: half a screen below or above, so the next card is ready when it arrives.
            return r.yMax >= screen.yMin - screen.height * 0.5f && r.yMin <= screen.yMax + screen.height * 0.5f && r.xMax >= screen.xMin && r.xMin <= screen.xMax;
        }

        static IEnumerator Pump()
        {
            _pumping = true;
            while (Wants.Count > 0)
            {
                float now = Time.realtimeSinceStartup;
                foreach (Want q in Wants) if (q.El.panel != null) q.Seen = true;
                Wants.RemoveAll(q => q.El.panel == null && (q.Seen || now - q.Since > 8f));
                Want next = Wants.Find(q => Visible(q.El));
                if (Dbg) Perf.Note("TracksArt pump: " + Wants.Count + " waiting, next " + (next?.Key ?? "none") + (Wants.Count > 0 ? " first at " + Wants[0].El.worldBound + " in " + (Wants[0].El.panel != null ? Wants[0].El.panel.visualTree.worldBound.ToString() : "no panel") : ""));
                if (next == null) { yield return new WaitForSecondsRealtime(0.15f); continue; }
                Still made = null;
                if (!Stills.TryGetValue(next.Key, out made))
                {
                    made = FromDisk(next.Key);
                    if (made == null)
                    {
                        TracksCourse course = null;
                        yield return Course(J.Str(next.Meta, "slug"), (c, _) => course = c);
                        if (course == null && J.Get(next.Meta, "art") != null) course = TracksCourse.FromArt(next.Meta);
                        if (course != null) yield return Paint(course, next.W, next.H, s => made = s, next.Top, next.Bottom);
                        if (made != null) ToDisk(next.Key, made);
                    }
                    if (made != null) Stills[next.Key] = made;
                    if (Dbg) Perf.Note("TracksArt made " + next.Key + ": " + (made != null));
                }
                // Every card waiting on this course gets it (the Collection list and Home can show the same course).
                foreach (Want q in Wants.FindAll(q => q.Key == next.Key)) { Wants.Remove(q); if (made != null && q.El.panel != null) { q.Done(made); Perf.Wake(); } }
                yield return null;
            }
            _pumping = false;
        }

        static string Folder => Path.Combine(Application.temporaryCachePath, "tracks-art");

        static Still FromDisk(string key)
        {
            try
            {
                string jpg = Path.Combine(Folder, key + ".jpg"), marks = Path.Combine(Folder, key + ".txt");
                if (!File.Exists(jpg)) return null;
                var tex = new Texture2D(2, 2, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp, name = key };
                if (!tex.LoadImage(File.ReadAllBytes(jpg), true)) return null;
                var s = new Still { Tex = tex };
                if (File.Exists(marks))
                    foreach (string line in File.ReadAllLines(marks))
                    {
                        string[] p = line.Split(',');
                        if (p.Length == 3 && int.TryParse(p[0], out int n) && float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float u) && float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                            s.Holes.Add((n, new Vector2(u, v)));
                    }
                return s;
            }
            catch (Exception) { return null; }
        }

        static void ToDisk(string key, Still s)
        {
            Color32[] px = s.Pixels;
            s.Pixels = null;
            if (px == null) return;
            int w = s.Tex.width, h = s.Tex.height;
            string folder = Folder;
            var sb = new StringBuilder();
            foreach (var hole in s.Holes) sb.Append(hole.n).Append(',').Append(hole.at.x.ToString("0.####", CultureInfo.InvariantCulture)).Append(',').Append(hole.at.y.ToString("0.####", CultureInfo.InvariantCulture)).Append('\n');
            string marks = sb.ToString();
#if UNITY_WEBGL && !UNITY_EDITOR
            return; // WebGL has no threads: a still is painted again rather than cached
#endif
            // The JPEG is made off the main thread, so caching a still never costs a frame.
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var rgba = new byte[px.Length * 4];
                    for (int i = 0; i < px.Length; i++) { rgba[i * 4] = px[i].r; rgba[i * 4 + 1] = px[i].g; rgba[i * 4 + 2] = px[i].b; rgba[i * 4 + 3] = 255; }
                    byte[] jpg = ImageConversion.EncodeArrayToJPG(rgba, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm, (uint)w, (uint)h, 0, 90);
                    Directory.CreateDirectory(folder);
                    File.WriteAllText(Path.Combine(folder, key + ".txt"), marks);
                    File.WriteAllBytes(Path.Combine(folder, key + ".jpg"), jpg);
                }
                catch (Exception) { /* a still that cannot be cached is painted again next time */ }
            });
        }

        static Texture2D _topShade;

        /// <summary>Dark at the top fading to clear: under a header that sits over a live picture.</summary>
        public static Texture2D TopShade()
        {
            if (_topShade != null) return _topShade;
            _topShade = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 64; y++) _topShade.SetPixel(0, y, new Color(0.035f, 0.043f, 0.05f, Mathf.SmoothStep(0f, 0.85f, y / 63f)));   // y 0 is the bottom
            _topShade.Apply();
            return _topShade;
        }

        /// <summary>The hole numbers over a still shown stretched to fill `over` (the card keeps the still's own shape).</summary>
        public static void Numbers(VisualElement over, Still s)
        {
            over.Query(className: "tracks-artnum").ForEach(e => e.RemoveFromHierarchy());
            foreach (var h in s.Holes)
            {
                Label n = Ui.Text(h.n.ToString(), "tracks-artnum", Ui.Bold);
                n.pickingMode = PickingMode.Ignore;
                n.style.left = Length.Percent(h.at.x * 100f);
                n.style.top = Length.Percent(h.at.y * 100f);
                over.Add(n);
            }
        }

        // ── Cheap noise: an integer hash, no sines (the course is millions of samples; the hole's Fbm is too slow for it) ──

        internal static float IHash(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xffffff) / 16777215f;
            }
        }

        static float IHashF(float x, float y) => IHash((int)(x * 13.7f), (int)(y * 7.3f));

        static float VNoise(float x, float y)
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
            float xf = x - xi, yf = y - yi, u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
            float a = IHash(xi, yi), b = IHash(xi + 1, yi), c = IHash(xi, yi + 1), d = IHash(xi + 1, yi + 1);
            return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
        }

        /// <summary>Four octaves of value noise, 0..1, the shape of TracksWorld.Fbm at a fraction of the cost.</summary>
        internal static float Fbm(float x, float y) { float s = 0, a = 0.5f, f = 1; for (int i = 0; i < 4; i++) { s += a * VNoise(x * f, y * f); f *= 2.03f; a *= 0.5f; } return s; }

        static float[] _tile;

        /// <summary>256 x 256 of soft noise, made once: the rough's mottle and the water's ripple, read by pixel.</summary>
        internal static float[] Tile()
        {
            if (_tile != null) return _tile;
            var t = new float[256 * 256];
            for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++) t[x + (y << 8)] = VNoise(x / 5f, y / 5f) * 0.65f + VNoise(x / 1.8f + 40, y / 1.8f) * 0.35f;
            return _tile = t;
        }

        // ── The painter ──

        static readonly Color Pine = PuttrArcade.Make.Hex(0x26401c), Gorse = PuttrArcade.Make.Hex(0x3a5420), Bloom = PuttrArcade.Make.Hex(0xb5ad3a), Flag = PuttrArcade.Make.Hex(0xff3b30);

        /// <summary>Paint the whole course into a w x h still, turned so it fills the frame; done(still, or null with no shapes).</summary>
        public static IEnumerator Paint(TracksCourse c, int W, int H, Action<Still> done, float top = 0f, float bottom = 1f)
        {
            float sliceAt = Time.realtimeSinceStartup, startedAt = sliceAt;   // not Stopwatch: it runs fast in the Mac player on Apple silicon
            int sliceFrame = Time.frameCount;
            bool Slice() { float now = Time.realtimeSinceStartup; if (Time.frameCount != sliceFrame) { sliceFrame = Time.frameCount; sliceAt = now; } return now - sliceAt >= 0.006f; }

            // Frame on the course itself: the holes (their lines, tees and pins) and the greens and fairways that belong to
            // them, or the turf when there are no holes. A fairway or green far from every hole (a neighbouring course in
            // the same map) is left out, so it does not widen the frame.
            var pts = new List<Vector2>();
            foreach (TracksCourse.Hole h in c.Holes) { pts.AddRange(h.Line); pts.Add(h.Tee); pts.Add(h.Pin); }
            bool holes = pts.Count > 0;
            if (holes)
            {
                foreach (string k in new[] { "fairway", "green" })
                    foreach (Vector2[] poly in c.F(k))
                    {
                        if (poly.Length == 0) continue;
                        Vector2 mid = Vector2.zero;
                        foreach (Vector2 p in poly) mid += p;
                        mid /= poly.Length;
                        bool near = false;
                        foreach (TracksCourse.Hole h in c.Holes) if (h.Line != null && h.Line.Length > 1 && TracksCourse.Closest(h, mid).d < 60f) { near = true; break; }
                        if (near) pts.AddRange(poly);
                    }
            }
            else foreach (string k in new[] { "fairway", "green" }) foreach (Vector2[] poly in c.F(k)) pts.AddRange(poly);
            if (pts.Count < 2) foreach (Vector2[] poly in c.F("boundary")) pts.AddRange(poly);
            if (pts.Count < 2) { done(null); yield break; }
            // A small margin in pixels (room for the hole numbers by the outermost tees), not a band of yards of grass.
            float pad = Mathf.Round(Mathf.Min(W, H * (bottom - top)) * 0.045f);
            float availW = Mathf.Max(8f, W - pad * 2), availH = Mathf.Max(8f, H * (bottom - top) - pad * 2);
            // The turn: the course lying along the card (its thinnest way up), unless another turn fills the frame far
            // better; then hole 1 on the left, so every card reads the same way.
            float bestS = 0, bestA = 0, flatS = 0, flatA = 0, flatH = float.MaxValue; Vector2 bestC = Vector2.zero, flatC = Vector2.zero, bestE = Vector2.one, flatE = Vector2.one;
            for (int deg = 0; deg < 180; deg += 5)
            {
                float a = deg * Mathf.Deg2Rad, cs = Mathf.Cos(a), sn = Mathf.Sin(a);
                float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
                foreach (Vector2 p in pts) { float rx = p.x * cs - p.y * sn, ry = p.x * sn + p.y * cs; x0 = Mathf.Min(x0, rx); x1 = Mathf.Max(x1, rx); y0 = Mathf.Min(y0, ry); y1 = Mathf.Max(y1, ry); }
                Vector2 ext = new Vector2(Mathf.Max(1f, x1 - x0), Mathf.Max(1f, y1 - y0));
                float s = Mathf.Min(availW / ext.x, availH / ext.y);
                if (s > bestS) { bestS = s; bestA = a; bestC = new Vector2((x0 + x1) / 2, (y0 + y1) / 2); bestE = ext; }
                if (ext.y / ext.x < flatH) { flatH = ext.y / ext.x; flatS = s; flatA = a; flatC = new Vector2((x0 + x1) / 2, (y0 + y1) / 2); flatE = ext; }
            }
            if (bestS < flatS * 1.25f) { bestS = flatS; bestA = flatA; bestC = flatC; bestE = flatE; }
            if (holes)
            {
                // Half a turn more when hole 1 would start on the right.
                Vector2 t1 = c.Holes[0].Tee;
                float t1x = t1.x * Mathf.Cos(bestA) - t1.y * Mathf.Sin(bestA);
                if (t1x > bestC.x) { bestA += Mathf.PI; bestC = -bestC; }
            }
            // Zoomed in to fill the card: a course whose shape is not the card's would sit in a wide band of grass when it
            // only fits, so it is enlarged toward filling the card the other way, by up to MaxZoom (the far ends of its
            // longer side may go just off the card; the hole numbers stay on it).
            const float MaxZoom = 1.3f;
            float cover = Mathf.Max(availW / bestE.x, availH / bestE.y);
            float scale = Mathf.Min(cover, bestS * MaxZoom), cosA = Mathf.Cos(bestA), sinA = Mathf.Sin(bestA);
            Vector2 cen = bestC;
            // The course sits in its band (top..bottom, from the top of the picture): the middle of the band is this far up from the middle.
            float lift = (0.5f - (top + bottom) / 2f) * H;
            // Course yards to pixels (row 0 at the bottom, as the texture is) and back.
            Vector2 ToPx(Vector2 p) { float rx = p.x * cosA - p.y * sinA, ry = p.x * sinA + p.y * cosA; return new Vector2(W / 2f + (rx - cen.x) * scale, H / 2f + lift + (ry - cen.y) * scale); }
            Vector2 ToYd(float px, float py) { float rx = cen.x + (px - W / 2f) / scale, ry = cen.y + (py - H / 2f - lift) / scale; return new Vector2(rx * cosA + ry * sinA, -rx * sinA + ry * cosA); }

            // Lies by pixel, in the 3D world's paint order.
            var ids = new byte[W * H];
            foreach (var p in TracksWorld.Paint)
            {
                foreach (Vector2[] poly in c.F(p.key))
                {
                    if (poly.Length < 3) continue;
                    var q = new Vector2[poly.Length];
                    float qx0 = float.MaxValue, qx1 = float.MinValue, qy0 = float.MaxValue, qy1 = float.MinValue;
                    for (int i = 0; i < poly.Length; i++) { q[i] = ToPx(poly[i]); qx0 = Mathf.Min(qx0, q[i].x); qx1 = Mathf.Max(qx1, q[i].x); qy0 = Mathf.Min(qy0, q[i].y); qy1 = Mathf.Max(qy1, q[i].y); }
                    if (qx1 < 0 || qy1 < 0 || qx0 > W || qy0 > H) continue;
                    TracksWorld.Fill(ids, W, H, 0, 0, 1, q, p.id);
                }
                if (Slice()) yield return null;
            }

            // Ground height by lie, as the 3D world shapes it, then softened: it only lights the picture.
            var hByLie = new float[10]; var aByLie = new float[10]; hByLie[0] = 0.4f; aByLie[0] = 2.3f;
            var cols = new Color[10]; cols[0] = TracksWorld.Base;
            foreach (var p in TracksWorld.Paint) { hByLie[p.id] = p.h; aByLie[p.id] = p.amp; cols[p.id] = p.col; }
            // The land's swell on a coarse grid (every 4 px), the lie's own height at every pixel.
            const int G = 4;
            int gw = W / G + 2, gh = H / G + 2;
            var swell = new float[gw * gh];
            for (int j = 0; j < gh; j++)
            {
                for (int i = 0; i < gw; i++)
                {
                    Vector2 yd = ToYd(i * G, j * G);
                    byte id = ids[Math.Min(H - 1, j * G) * W + Math.Min(W - 1, i * G)];
                    swell[j * gw + i] = (Fbm(yd.x / 70, yd.y / 70) - 0.5f) * 7f + (Fbm(yd.x / 14 + 9, yd.y / 14) - 0.5f) * aByLie[id] * 2f;
                }
                if (Slice()) yield return null;
            }
            var ht = new float[W * H];
            for (int r = 0; r < H; r++)
            {
                int j = r / G; float fy = (r - j * G) / (float)G;
                for (int x = 0; x < W; x++)
                {
                    int i = x / G; float fx = (x - i * G) / (float)G;
                    int o = j * gw + i;
                    float upper = swell[o] + (swell[o + 1] - swell[o]) * fx, lower = swell[o + gw] + (swell[o + gw + 1] - swell[o + gw]) * fx;
                    ht[r * W + x] = upper + (lower - upper) * fy + hByLie[ids[r * W + x]];
                }
                if (Slice()) yield return null;
            }
            var tmp = new float[W * H];
            for (int pass = 0; pass < 2; pass++)
            {
                for (int r = 0; r < H; r++)
                {
                    int row = r * W;
                    tmp[row] = ht[row]; tmp[row + W - 1] = ht[row + W - 1];
                    for (int x = 1; x < W - 1; x++) tmp[row + x] = (ht[row + x] * 2 + ht[row + x - 1] + ht[row + x + 1]) * 0.25f;
                    if (Slice()) yield return null;
                }
                for (int r = 0; r < H; r++)
                {
                    int up = (r > 0 ? r - 1 : 0) * W, dn = (r < H - 1 ? r + 1 : r) * W, row = r * W;
                    for (int x = 0; x < W; x++) ht[row + x] = (tmp[row + x] * 2 + tmp[up + x] + tmp[dn + x]) * 0.25f;
                    if (Slice()) yield return null;
                }
            }

            // Colour: the lie's own, mown stripes, speckled rough, rippled water, lit from the top left.
            float[] tile = Tile();
            var px = new Color32[W * H];
            float stripe = Mathf.Max(3f, 9f * scale), greenStripe = Mathf.Max(2f, 3f * scale), slope = scale / 2f * 0.9f;
            for (int r = 0; r < H; r++)
            {
                int up = (r < H - 1 ? r + 1 : r) * W, dn = (r > 0 ? r - 1 : 0) * W, row = r * W;
                for (int x = 0; x < W; x++)
                {
                    int o = row + x, id = ids[o];
                    float k = 0, t = tile[(x & 255) + ((r & 255) << 8)];
                    if (id == 5 || id == 6) k = ((int)((x + r * 0.55f) / stripe) & 1) == 0 ? 0.035f : -0.03f;
                    else if (id == 7) k = ((int)((x - r * 0.55f + 4096) / greenStripe) & 1) == 0 ? 0.03f : -0.02f;
                    else if (id == 0 || id == 4 || id == 2 || id == 3) k = (IHash(x, r) - 0.5f) * 0.1f + (t - 0.5f) * 0.16f;
                    else if (id == 1 || id == 8) k = (IHash(x, r) - 0.5f) * 0.05f;
                    else if (id == 9) k = (tile[((x >> 1) & 255) + (((r * 3) & 255) << 8)] - 0.5f) * 0.14f;
                    float shade = 1f;
                    if (id != 9)
                    {
                        float gx = ht[row + (x < W - 1 ? x + 1 : x)] - ht[row + (x > 0 ? x - 1 : x)], gz = ht[up + x] - ht[dn + x];
                        shade = 1f + (gz - gx) * slope;
                        shade = shade < 0.62f ? 0.62f : shade > 1.32f ? 1.32f : shade;
                    }
                    Color col = cols[id];
                    float cr = (col.r + k) * shade, cg = (col.g + k) * shade, cb = (col.b + k * 0.6f) * shade;
                    px[o] = new Color32((byte)(cr < 0 ? 0 : cr > 1 ? 255 : cr * 255), (byte)(cg < 0 ? 0 : cg > 1 ? 255 : cg * 255), (byte)(cb < 0 ? 0 : cb > 1 ? 255 : cb * 255), 255);
                }
                if (Slice()) yield return null;
            }

            // Pines in the woods and gorse on the scrub, each with its shadow down and to the right.
            float step = Mathf.Max(2.5f, 4.5f * scale);
            for (float gy = 0; gy < H; gy += step)
            {
                for (float gxp = 0; gxp < W; gxp += step)
                {
                    float jx = gxp + (IHashF(gxp, gy + 7) - 0.5f) * step, jy = gy + (IHashF(gy, gxp + 3) - 0.5f) * step;
                    int ix = Mathf.Clamp(Mathf.RoundToInt(jx), 0, W - 1), iy = Mathf.Clamp(Mathf.RoundToInt(jy), 0, H - 1);
                    byte id = ids[iy * W + ix];
                    if ((id != 2 && id != 3) || IHashF(jx * 0.37f, jy * 0.91f) > 0.72f) continue;
                    float hv = IHashF(jx, jy);
                    float rad = Mathf.Max(1.2f, (id == 3 ? 2.6f + hv * 2f : 1.3f + hv * 1.3f) * scale);
                    Disc(px, W, H, jx + rad * 0.55f, jy - rad * 0.55f, rad, new Color(0.05f, 0.08f, 0.03f), 0.45f, false);
                    Disc(px, W, H, jx, jy, rad, id == 3 ? Pine : Gorse, 1f, true);
                    if (id == 2 && hv > 0.7f) Disc(px, W, H, jx - rad * 0.2f, jy + rad * 0.2f, rad * 0.5f, Bloom, 0.85f, false);
                }
                if (Slice()) yield return null;
            }

            // Every hole: its line dashed from the tee, the tee, the flag, and where its number goes.
            var still = new Still();
            foreach (TracksCourse.Hole h in c.Holes)
            {
                float run = 0;
                for (int i = 1; i < h.Line.Length; i++)
                {
                    Vector2 a = ToPx(h.Line[i - 1]), b = ToPx(h.Line[i]);
                    float len = Vector2.Distance(a, b);
                    for (float t = 0; t <= len; t += 0.6f, run += 0.6f)
                        if (run % 9f < 5.5f) Disc(px, W, H, Mathf.Lerp(a.x, b.x, t / len), Mathf.Lerp(a.y, b.y, t / len), 1.05f, Color.white, 0.75f, false);
                }
                Vector2 tee = ToPx(h.Tee), pin = ToPx(h.Pin);
                Disc(px, W, H, tee.x, tee.y, 2.4f, Color.white, 0.95f, false);
                Disc(px, W, H, pin.x + 1.2f, pin.y - 1.2f, 3.6f, Color.black, 0.35f, false);
                Disc(px, W, H, pin.x, pin.y, 3.6f, Color.white, 1f, false);
                Disc(px, W, H, pin.x, pin.y, 2.4f, Flag, 1f, false);
                // The number sits behind the tee, away from the hole's line.
                Vector2 dir = (ToPx(h.Line[1]) - tee).normalized;
                Vector2 at = tee - dir * 13f;
                still.Holes.Add((h.N > 0 ? h.N : still.Holes.Count + 1, new Vector2(Mathf.Clamp(at.x / W, 0.03f, 0.97f), Mathf.Clamp(1f - at.y / H, 0.05f, 0.95f))));
                if (Slice()) yield return null;
            }

            // A soft dark edge so the card's corners and words sit well on it.
            for (int r = 0; r < H; r++)
            {
                float vy = (r / (float)(H - 1)) * 2 - 1;
                for (int x = 0; x < W; x++)
                {
                    float vx = (x / (float)(W - 1)) * 2 - 1, v = 1f - 0.28f * Mathf.Clamp01((vx * vx + vy * vy) * 0.5f);
                    int o = r * W + x; Color32 q = px[o];
                    px[o] = new Color32((byte)(q.r * v), (byte)(q.g * v), (byte)(q.b * v), 255);
                }
            }

            if (Dbg) Perf.Note("TracksArt painted " + c.Slug + " in " + Mathf.RoundToInt((Time.realtimeSinceStartup - startedAt) * 1000) + " ms");
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp, name = "Tracks still " + c.Slug };
            tex.SetPixels32(px);
            tex.Apply(false, true);   // no copy kept on the CPU: the still lives on the GPU (and as a JPEG on disk)
            still.Tex = tex;
            still.Pixels = px;
            done(still);
        }

        /// <summary>An anti-aliased disc blended into the picture; lit, it is brighter to the top left like a crown in the sun.</summary>
        static void Disc(Color32[] px, int W, int H, float cx, float cy, float r, Color col, float alpha, bool lit)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - r - 1)), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + r + 1));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - r - 1)), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + r + 1));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x - cx, dy = y - cy, d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(r + 0.5f - d) * alpha;
                    if (a <= 0) continue;
                    Color c = col;
                    if (lit) { float l = 1f + (-dx + dy) / Mathf.Max(1f, r) * 0.28f; c = new Color(c.r * l, c.g * l, c.b * l); }
                    int o = y * W + x; Color32 q = px[o];
                    px[o] = new Color32((byte)Mathf.Clamp(q.r + (c.r * 255f - q.r) * a, 0, 255), (byte)Mathf.Clamp(q.g + (c.g * 255f - q.g) * a, 0, 255), (byte)Mathf.Clamp(q.b + (c.b * 255f - q.b) * a, 0, 255), 255);
                }
        }
    }
}

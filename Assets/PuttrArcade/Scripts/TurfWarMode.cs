// Turf War: two teams, one white floor, and a lot of paint.
//
// The putt is the throw. A ball of paint leaves the bucket at the near edge
// and lands as a splat of the team's color, covering whatever was there.
//
// The floor IS the mat, seen from above: the paint lands where the ball finished.
//   across  the mat's 24 inches, edge to edge: its left edge is the floor's left edge.
//   along   from the front of the camera's view (the near edge, SOFT) to 30 in past the
//           hole (the far edge, FIRM). The hole's spot is a third of the way up.
//   short   it lands where the ball got to, the top of its roll up the ramp: short and
//           left is near and far left.
//   reached it lands where the ball would have finished past the hole: the line at the
//           hole carried on along its heading, for the rollout its speed gives (v*v/128,
//           the same "in past" the putt card shows).
//   holed   the splat is three times the area.
//
// The paint is a small picture kept in memory (one texel is one inch of
// floor) with a second map of who owns each texel. A splat is drawn into
// both, and the score is a count of the second.
//
// Teams alternate and partners alternate within a team: 8 putts a side.
// With one Player the orange team is Puttr AI, which throws on its own.
using System.Collections;
using System.Collections.Generic;
using Puttr;
using UnityEngine;

namespace PuttrArcade
{
    public sealed class TurfWarMode : StageMode
    {
        public override string Name => "Turf War";
        public override string VoicePrefix => "turfwar";
        public override int Putter => Mathf.Max(0, Thrower(Team, PuttIndex)); // whose difficulty applies to this putt
        public override string Blurb => "Two teams, one white floor.\nYour putt throws the paint.\nMost floor covered wins.";
        public override string Rule => "The floor is the mat: the paint lands where your ball finished. Most floor wins.";
        public override int Score(int player) => _pct[TeamOf(player)];
        public override string ScoreWord(int n) => n == 1 ? "percent" : "percent";
        public override string ScoreUnit => "%";
        public override GameAudio.Scene Sound => GameAudio.Scene.Table;
        // puttr-app-2: a paint studio of its own (the room tone until it is recorded); it ends on the big splat.
        public override string BedSlot => "turfwar_studio_loop";
        public override string EndSlot => "turfwar_big_splat";
        public override string Icon => "Target";
        public override Color Accent => Orange;
        public override string[] Tags => new[] { "Teams", "8 putts each" };
        public override int Difficulty => 1;
        public override int MaxPlayers => 4;
        public override string SoloOpponent => "Puttr";
        public override bool ShowsAim => false; // anywhere on the floor can be the right putt
        public override string[] HowTo => new[]
        {
            "Teams take turns throwing paint, 8 putts a side. The floor is the mat from above: the paint lands where your ball finished. Short and left lands near and on the left; firm lands far.",
            "Paint covers paint: splat over the other team to take their floor. Hole it and the splat is three times the size.",
            "A putt that stops short lands where it stopped; one that runs past lands where it would have finished. Most floor covered at the end wins.",
        };

        const int Putts = 8;
        // The paint map. One texel is one unit of floor, so the floor is W wide and H deep.
        const int W = 320, H = 200;
        const float Ground = -6f;          // the studio floor; the painted floor is a slab standing on it, its top at 0
        const float BucketZ = -34f, BucketTop = 20f;
        const float Inset = 10f;           // how close to an edge the middle of a splat can land
        const float LineReach = Arcade.MatHalfInches, FarPastIn = 30f;
        // How far in front of the hole the camera sees (the near edge of the floor), true inches.
        static readonly float FrontIn = PuttrAnalysis.ToInchesIntegrated(Hole.Typical.X, 479f, Hole.Typical).y;
        const float Radius = 22f, BigRadius = 38f; // 38 is 22 times the square root of 3: three times the area
        const float FlightTime = 0.6f, GrowTime = 0.25f;

        static readonly Color Blue = Make.Hex(0x3D68D8), Orange = Make.Hex(0xF29F05), Floor = Make.Hex(0xE9E6DC);
        // The same two colors, lightened to be read on a dark card.
        static readonly Color BlueText = Make.Hex(0x8FB0FF), OrangeText = Make.Hex(0xFFC266);

        // Raised, and about 40 degrees down, so the far edge of the floor is at the top.
        public override Vector3 CameraPosition => At(0f, 308f, -288f);
        public override Vector3 CameraLook => At(0f, 0f, 80f);
        public override float CameraFov => 36f;
        public override float ShadowDistance => 1000f;

        Texture2D _tex;
        Color32[] _px, _saved, _clean;
        byte[] _owner, _savedOwner;
        float[] _field;
        readonly List<Vector3> _discs = new List<Vector3>(); // one splat: x, y from its middle, and radius
        float _extent;

        Transform _ball, _ring;
        Material _ballPaint, _bucketPaint;
        System.Random _rng = new System.Random(23);

        int _players, _turn;
        readonly int[] _pct = new int[2];

        int Team => _turn % 2;
        int PuttIndex => _turn / 2;

        // ── The set ─────────────────────────────────────────────────────────

        public override void Build()
        {
            // A soft key light from the near left, so rims and cans throw a
            // shadow across the floor, and a cool one from behind to fill.
            Sun(Root, new Vector3(48f, 30f, 0f), new Color(1f, 0.97f, 0.93f), 1.05f); // any brighter and the white floor burns out
            Sun(Root, new Vector3(40f, -150f, 0f), new Color(0.75f, 0.84f, 1f), 0.45f).shadows = LightShadows.None;

            // The studio: dark concrete, a long way in every direction.
            Props.Concrete(out Texture2D slab, out Texture2D pits);
            Material concrete = Make.Lit(new Color(0.17f, 0.18f, 0.2f), 0.35f, 0f, slab, pits, 0.6f);
            concrete.SetTextureScale("_BaseMap", new Vector2(4000f / 220f, 4000f / 220f));
            Shape(PrimitiveType.Cube, "Studio floor", Root, new Vector3(0f, Ground - 1f, H * 0.5f), new Vector3(4000f, 2f, 4000f), concrete);

            // The floor to be painted: a pale slab, with the paint map on top of it.
            Shape(PrimitiveType.Cube, "Slab", Root, new Vector3(0f, (Ground - 0.5f) * 0.5f, H * 0.5f), new Vector3(W, -Ground - 0.5f, H), Make.Lit(Floor, 0.3f));
            _tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            _px = new Color32[W * H];
            _saved = new Color32[W * H];
            _clean = new Color32[W * H];
            _owner = new byte[W * H];
            _savedOwner = new byte[W * H];
            _field = new float[W * H];
            BuildClean();
            var mesh = new Mesh
            {
                name = "Painted floor",
                vertices = new[] { new Vector3(-W * 0.5f, 0f, 0f), new Vector3(W * 0.5f, 0f, 0f), new Vector3(-W * 0.5f, 0f, H), new Vector3(W * 0.5f, 0f, H) },
                uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) },
                normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up },
                triangles = new[] { 0, 2, 1, 1, 2, 3 },
            };
            mesh.RecalculateBounds();
            GameObject top = Make.MeshObject("Painted floor", Root, mesh, Make.Lit(Color.white, 0.42f, 0f, _tex));
            top.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // A low rim all the way round.
            Material rim = Make.Lit(Make.Hex(0x2B2F33), 0.4f);
            const float rimW = 6f, rimTop = 4f;
            float rimH = rimTop - Ground, rimY = (rimTop + Ground) * 0.5f;
            Shape(PrimitiveType.Cube, "Rim", Root, new Vector3(-W * 0.5f - rimW * 0.5f, rimY, H * 0.5f), new Vector3(rimW, rimH, H + rimW * 2f), rim);
            Shape(PrimitiveType.Cube, "Rim", Root, new Vector3(W * 0.5f + rimW * 0.5f, rimY, H * 0.5f), new Vector3(rimW, rimH, H + rimW * 2f), rim);
            Shape(PrimitiveType.Cube, "Rim", Root, new Vector3(0f, rimY, -rimW * 0.5f), new Vector3(W, rimH, rimW), rim);
            Shape(PrimitiveType.Cube, "Rim", Root, new Vector3(0f, rimY, H + rimW * 0.5f), new Vector3(W, rimH, rimW), rim);

            // What the lines on the floor mean, lying on the studio floor beside them.
            var ink = new Color(1f, 1f, 1f, 0.75f);
            string[] across = { "FAR LEFT", "LEFT", "RIGHT", "FAR RIGHT" };
            for (int i = 0; i < 4; i++)
                Lay(Words(Root, across[i], new Vector3((i - 1.5f) * W * 0.25f, Ground + 0.2f, -rimW - 10f), 6.5f, ink));
            string[] along = { "SOFT", "MEDIUM", "FIRM" };
            for (int i = 0; i < 3; i++)
                Lay(Words(Root, along[i], new Vector3(-W * 0.5f - rimW - 7f, Ground + 0.2f, (i + 0.5f) * H / 3f), 6.5f, ink, TextAnchor.MiddleRight));


            // The bucket the paint is thrown from. What is in it is the color of the team to putt.
            Shape(PrimitiveType.Cylinder, "Bucket", Root, new Vector3(0f, Ground + (BucketTop - Ground) * 0.5f, BucketZ), new Vector3(26f, (BucketTop - Ground) * 0.5f, 26f), Make.Lit(new Color(0.8f, 0.8f, 0.78f), 0.5f));
            Shape(PrimitiveType.Cylinder, "Bucket band", Root, new Vector3(0f, BucketTop - 2.5f, BucketZ), new Vector3(27.4f, 1.5f, 27.4f), Make.Lit(new Color(0.55f, 0.56f, 0.57f), 0.6f, 0.8f));
            _bucketPaint = Make.Lit(Blue, 0.85f);
            Make.MeshObject("Bucket paint", Root, Make.Disc(11.5f), _bucketPaint).transform.localPosition = new Vector3(0f, BucketTop + 0.15f, BucketZ);

            // Cans and spills off to the sides.
            Material steel = Make.Lit(new Color(0.62f, 0.63f, 0.65f), 0.7f, 0.9f);
            Can(197f, 28f, Orange, steel);
            Can(219f, 60f, Blue, steel);
            Can(-236f, 128f, Blue, steel);
            Can(-222f, 160f, Orange, steel);
            Spill(192f, 92f, Orange, 3);
            Spill(-216f, 96f, Blue, 8);
            Spill(236f, 150f, Blue, 14);

            // The ball of paint, and a ring round the splat it just made.
            _ballPaint = Make.Lit(Blue, 0.85f);
            _ball = Shape(PrimitiveType.Sphere, "Paint ball", Root, new Vector3(0f, BucketTop, BucketZ), Vector3.one * 11f, _ballPaint).transform;
            _ball.gameObject.SetActive(false);
            _ring = Make.MeshObject("Latest", Root, Make.Ring(0.965f, 1f, 96), Make.Glow(new Color(1f, 1f, 1f, 0.85f), false)).transform;
            _ring.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _ring.gameObject.SetActive(false);

            ClearFloor();
        }

        // Words face the camera as made. Laid flat, they read from the near side.
        static void Lay(TextMesh words) => words.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        void Can(float x, float z, Color paint, Material steel)
        {
            const float r = 10f, h = 24f;
            Shape(PrimitiveType.Cylinder, "Can", Root, new Vector3(x, Ground + h * 0.5f, z), new Vector3(r * 2f, h * 0.5f, r * 2f), steel);
            Shape(PrimitiveType.Cylinder, "Can label", Root, new Vector3(x, Ground + h * 0.48f, z), new Vector3(r * 2f + 0.5f, h * 0.3f, r * 2f + 0.5f), Make.Lit(paint, 0.45f));
            Make.MeshObject("Can paint", Root, Make.Disc(r - 1.2f), Make.Lit(paint, 0.85f)).transform.localPosition = new Vector3(x, Ground + h + 0.15f, z);
        }

        // Paint that missed: a puddle on the studio floor, and the drops round it.
        void Spill(float x, float z, Color paint, int seed)
        {
            var rng = new System.Random(seed);
            Material wet = Make.Lit(paint, 0.8f);
            float main = 9f + (float)rng.NextDouble() * 5f;
            void Drop(float dx, float dz, float r)
            {
                GameObject drop = Make.MeshObject("Spill", Root, Make.Disc(r, 32), wet);
                drop.transform.localPosition = new Vector3(x + dx, Ground + 0.12f, z + dz);
                drop.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            Drop(0f, 0f, main);
            for (int i = 0; i < 7; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float far = main * (0.8f + (float)rng.NextDouble() * 1.4f);
                Drop(Mathf.Cos(a) * far, Mathf.Sin(a) * far, main * (0.12f + (float)rng.NextDouble() * 0.3f));
            }
        }

        // The bare floor: pale, with faint lines at the quarters across and the thirds along.
        void BuildClean()
        {
            Color32 bare = Floor;
            Color32 line = Color.Lerp(Floor, Color.black, 0.13f);
            for (int y = 0; y < H; y++)
            {
                bool row = Mathf.Abs(y - H / 3) < 1 || Mathf.Abs(y - 2 * H / 3) < 1;
                for (int x = 0; x < W; x++)
                {
                    bool column = x % (W / 4) == 0 && x > 0;
                    _clean[y * W + x] = row || column ? line : bare;
                }
            }
        }

        void ClearFloor()
        {
            System.Array.Copy(_clean, _px, _px.Length);
            System.Array.Clear(_owner, 0, _owner.Length);
            _pct[0] = _pct[1] = 0;
            Upload();
        }

        void Upload()
        {
            _tex.SetPixels32(_px);
            _tex.Apply(false);
        }

        // ── Paint ───────────────────────────────────────────────────────────

        float Rand(float low, float high) => low + (float)_rng.NextDouble() * (high - low);

        // Make up one splat: a main disc, lumps on its edge, loose drops, and
        // a few thin streaks thrown on the way the paint was travelling.
        void MakeBlob(float radius, Vector2 travel)
        {
            _discs.Clear();
            _discs.Add(new Vector3(0f, 0f, radius));
            int satellites = _rng.Next(8, 15);
            for (int i = 0; i < satellites; i++)
            {
                float a = Rand(0f, Mathf.PI * 2f);
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                float onward = 0.5f + 0.5f * Vector2.Dot(dir, travel);
                float far, r;
                if (i % 3 != 2) { far = radius * Rand(0.6f, 0.95f); r = radius * Rand(0.26f, 0.46f); }
                else { far = radius * Rand(1.3f, 1.7f + 0.6f * onward); r = radius * Rand(0.07f, 0.16f); }
                _discs.Add(new Vector3(dir.x * far, dir.y * far, r));
            }
            int streaks = _rng.Next(3, 6);
            float heading = Mathf.Atan2(travel.y, travel.x);
            for (int s = 0; s < streaks; s++)
            {
                float a = heading + Rand(-0.75f, 0.75f);
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                float from = radius * 0.85f, length = radius * Rand(0.8f, 1.7f);
                int steps = Mathf.CeilToInt(length / 1.2f);
                for (int k = 0; k <= steps; k++)
                {
                    float u = k / (float)steps;
                    float r = Mathf.Max(1.1f, Mathf.Lerp(radius * 0.1f, radius * 0.035f, u));
                    _discs.Add(new Vector3(dir.x * (from + length * u), dir.y * (from + length * u), r));
                }
                float drop = radius * Rand(0.07f, 0.12f);
                _discs.Add(new Vector3(dir.x * (from + length + drop), dir.y * (from + length + drop), drop));
            }
            _extent = 0f;
            foreach (Vector3 d in _discs) _extent = Mathf.Max(_extent, new Vector2(d.x, d.y).magnitude + d.z);
        }

        // Before a splat: keep the floor as it is, so the splat can be drawn again and again as it grows.
        void Keep()
        {
            System.Array.Copy(_px, _saved, _px.Length);
            System.Array.Copy(_owner, _savedOwner, _owner.Length);
        }

        // Draw the splat at this size (0 to 1) over the kept floor.
        void Stamp(float cx, float cy, float scale, int team)
        {
            Color32 paint = team == 0 ? (Color32)Blue : (Color32)Orange;
            byte id = (byte)(team + 1);
            float reach = _extent + 3f;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - reach)), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + reach));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - reach)), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + reach));
            if (x0 > x1 || y0 > y1) return;

            // How far inside the paint each texel is: more than 0 is painted.
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    _field[y * W + x] = -1f;
            foreach (Vector3 d in _discs)
            {
                float r = d.z * scale;
                if (r < 0.3f) continue;
                float px = cx + d.x * scale, py = cy + d.y * scale;
                int ax = Mathf.Max(x0, Mathf.FloorToInt(px - r - 1f)), bx = Mathf.Min(x1, Mathf.CeilToInt(px + r + 1f));
                int ay = Mathf.Max(y0, Mathf.FloorToInt(py - r - 1f)), by = Mathf.Min(y1, Mathf.CeilToInt(py + r + 1f));
                for (int y = ay; y <= by; y++)
                {
                    float dy = y + 0.5f - py;
                    for (int x = ax; x <= bx; x++)
                    {
                        float dx = x + 0.5f - px;
                        float inside = r - Mathf.Sqrt(dx * dx + dy * dy);
                        int i = y * W + x;
                        if (inside > _field[i]) _field[i] = inside;
                    }
                }
            }

            // Wet paint: a darker rim, darkest on the side away from the
            // light, and a soft shine just inside the edge that faces it.
            const float lightX = -0.5f, lightY = -0.866f; // toward the key light, in texels
            for (int y = y0; y <= y1; y++)
            {
                int up = Mathf.Min(y1, y + 1), down = Mathf.Max(y0, y - 1);
                for (int x = x0; x <= x1; x++)
                {
                    int i = y * W + x;
                    float inside = _field[i];
                    if (inside <= 0f) { _px[i] = _saved[i]; _owner[i] = _savedOwner[i]; continue; }

                    float gx = _field[y * W + Mathf.Min(x1, x + 1)] - _field[y * W + Mathf.Max(x0, x - 1)];
                    float gy = _field[up * W + x] - _field[down * W + x];
                    float length = Mathf.Sqrt(gx * gx + gy * gy);
                    // The slope runs into the paint, so the edge faces the other way.
                    float lit = length > 0.0001f ? -(gx * lightX + gy * lightY) / length : 0f;

                    float edge = 1f - Mathf.Clamp01(inside / 3.5f);
                    float shade = 1f - 0.15f * edge - 0.15f * edge * Mathf.Clamp01(-lit);
                    float shine = Mathf.Clamp01(lit) * Mathf.Clamp01(inside / 1.2f) * Mathf.Clamp01(1f - (inside - 1.2f) / 3.5f) * 0.4f;
                    float r = paint.r * shade, g = paint.g * shade, b = paint.b * shade;
                    r += (255f - r) * shine; g += (255f - g) * shine; b += (255f - b) * shine;

                    // The last texel fades into what is under it.
                    float cover = Mathf.Clamp01(inside);
                    Color32 under = _saved[i];
                    _px[i] = new Color32(
                        (byte)Mathf.Clamp(under.r + (r - under.r) * cover, 0f, 255f),
                        (byte)Mathf.Clamp(under.g + (g - under.g) * cover, 0f, 255f),
                        (byte)Mathf.Clamp(under.b + (b - under.b) * cover, 0f, 255f), 255);
                    _owner[i] = inside > 0.5f ? id : _savedOwner[i];
                }
            }
            Upload();
        }

        // How much of the floor each color has, in percent.
        void Count()
        {
            int blue = 0, orange = 0;
            for (int i = 0; i < _owner.Length; i++)
            {
                if (_owner[i] == 1) blue++;
                else if (_owner[i] == 2) orange++;
            }
            _pct[0] = Mathf.RoundToInt(blue * 100f / _owner.Length);
            _pct[1] = Mathf.RoundToInt(orange * 100f / _owner.Length);
        }

        // The way the paint was going when it landed, across the floor.
        static Vector2 Travel(float tx, float ty)
        {
            var way = new Vector2(tx - W * 0.5f, ty - BucketZ);
            return way.sqrMagnitude > 0.01f ? way.normalized : Vector2.up;
        }

        // A ball of paint from the bucket to (tx, ty) on the map, and the splat it makes.
        IEnumerator Throw(int team, float tx, float ty, float radius)
        {
            SetPaint(team);
            var from = new Vector3(0f, BucketTop + 4f, BucketZ);
            var to = new Vector3(tx - W * 0.5f, 2f, ty);
            float peak = 46f + Vector3.Distance(from, to) * 0.22f;
            _ring.gameObject.SetActive(false);
            _ball.gameObject.SetActive(true);
            if (!Arcade.Audio.TryLibrary("turfwar_throw", 0.75f, 1f + Random.Range(-0.05f, 0.05f))) Arcade.Audio.Whoosh();   // puttr-app-2: paint, not On Fire's whoosh
            bool shot = false;
            for (float t = 0; t < FlightTime; t += Time.deltaTime)
            {
                float u = t / FlightTime;
                Vector3 p = Vector3.Lerp(from, to, u);
                p.y += peak * 4f * u * (1f - u);
                _ball.localPosition = p;
                if (!shot && u > 0.6f) { shot = true; Arcade.Shot("action"); }
                yield return null;
            }
            _ball.gameObject.SetActive(false);

            MakeBlob(radius, Travel(tx, ty));
            Keep();
            if (!Arcade.Audio.TryLibrary("turfwar_splat", radius > Radius ? 1f : 0.8f, 1f + Random.Range(-0.06f, 0.06f))) Arcade.Audio.Thump(radius > Radius ? 60f : 42f);   // puttr-app-2
            for (float t = 0; t < GrowTime; t += Time.deltaTime)
            {
                float u = 1f - t / GrowTime;
                Stamp(tx, ty, Mathf.Max(0.05f, 1f - u * u * u), team);
                yield return null;
            }
            Stamp(tx, ty, 1f, team);
            Count();

            _ring.localPosition = new Vector3(to.x, 0.2f, to.z);
            _ring.localScale = Vector3.one * radius * 1.45f;
            _ring.gameObject.SetActive(true);
        }

        // Left short: the paint hops in the bucket and stays there.
        IEnumerator Slop()
        {
            var from = new Vector3(0f, BucketTop + 2f, BucketZ);
            _ring.gameObject.SetActive(false);
            _ball.gameObject.SetActive(true);
            for (float t = 0; t < 0.5f; t += Time.deltaTime)
            {
                float u = t / 0.5f;
                _ball.localPosition = from + Vector3.up * 26f * 4f * u * (1f - u);
                yield return null;
            }
            _ball.gameObject.SetActive(false);
        }

        void SetPaint(int team)
        {
            Color color = team == 0 ? Blue : Orange;
            _ballPaint.SetColor("_BaseColor", color);
            _bucketPaint.SetColor("_BaseColor", color);
        }

        // ── The teams ───────────────────────────────────────────────────────

        // Players 1 and 2 are blue, 3 and 4 orange; with two it is one against the other.
        int TeamOf(int player) => _players <= 2 ? Mathf.Min(player, 1) : player < 2 ? 0 : 1;

        // Who throws for this team on its nth putt. -1 is Puttr AI.
        int Thrower(int team, int putt)
        {
            if (team == 0) return _players >= 3 ? putt % 2 : 0;
            if (_players <= 1) return -1;
            if (_players == 2) return 1;
            return _players == 3 ? 2 : 2 + putt % 2;
        }

        string TeamName(int team) => team == 0 ? "Blue" : _players <= 1 ? "Puttr" : "Orange";
        static Color TeamColor(int team) => team == 0 ? BlueText : OrangeText;

        // ── The game ────────────────────────────────────────────────────────

        public override void Begin(int players)
        {
            _players = Mathf.Clamp(players, 1, MaxPlayers);
            _turn = 0;
            _rng = new System.Random(23 + System.Environment.TickCount % 1000);
            Arcade.Hud.BuildBoard(new List<string> { TeamName(0), TeamName(1) }, Putts, "FLOOR %", true, "PUTTS");
            ClearFloor();
            _ring.gameObject.SetActive(false);
            Park();
            ShowBoard();
        }

        // Behind the home screen: a floor with a game already going on it.
        public override void Preview()
        {
            Begin(1);
            _rng = new System.Random(4);
            float[] spots = { 70f, 150f, 232f, 148f, 150f, 86f, 262f, 62f, 96f, 52f, 190f, 168f, 40f, 110f };
            for (int i = 0; i < spots.Length / 2; i++)
            {
                float tx = spots[i * 2], ty = spots[i * 2 + 1];
                MakeBlob(i == 2 ? BigRadius : Radius, Travel(tx, ty));
                Keep();
                Stamp(tx, ty, 1f, i % 2);
            }
            Count();
            Arcade.Hud.SetTotal(0, _pct[0]);
            Arcade.Hud.SetTotal(1, _pct[1]);
        }

        public override IEnumerator Prepare()
        {
            Park();
            yield break;
        }

        void Park()
        {
            _ball.gameObject.SetActive(false);
            SetPaint(Over ? 0 : Team);
        }

        void ShowBoard()
        {
            Hud hud = Arcade.Hud;
            hud.SetBoardTitle(Over ? "ALL THE PAINT IS THROWN" : "PUTT " + (PuttIndex + 1) + " OF " + Putts);
            hud.SetTotal(0, _pct[0]);
            hud.SetTotal(1, _pct[1]);
            hud.SetTurn(Over ? -1 : Team, Over ? -1 : PuttIndex);
        }

        public override string Prompt
        {
            get
            {
                int team = Team, player = Thrower(team, PuttIndex);
                string who = _players > 1 && player >= 0 ? Arcade.PlayerName(player) + "   ·   " + TeamName(team) + "   ·   " : "";
                string other = team == 0 ? "orange" : "blue";
                string aim = _pct[1 - team] > 0 ? "Aim where there is " + other : "Aim for open floor";
                return who + "Putt " + (PuttIndex + 1) + " of " + Putts + "   ·   " + aim;
            }
        }

        string Standing => TeamName(0) + " " + _pct[0] + "%   ·   " + TeamName(1) + " " + _pct[1] + "%";

        public override IEnumerator Play(PuttResult putt)
        {
            int team = Team, index = PuttIndex;
            bool left = !Finish(Arcade.TruePutt(putt), out _, out _), big = putt.Made;
            Hud hud = Arcade.Hud;

            if (left)
            {
                // Never reached the hole: no paint this turn.
                SetPaint(team);
                yield return Slop();
                hud.Banner("IN THE BUCKET", "The camera lost the ball: no paint thrown", Make.Red, 2f);
                if (!Arcade.Audio.TryLibrary("turfwar_dribble", 0.75f)) Arcade.Audio.Miss();   // puttr-app-2
                ShowPutt(Arcade.TruePutt(putt), "No paint", Make.Red);
                hud.SetPip(team, index, Hud.Pip.Bad);
            }
            else
            {
                // Where the ball finished on the mat is where the paint lands. A holed putt lands the
                // same way, only bigger. The real putt, not the one Easy draws in toward the hole.
                Finish(Arcade.TruePutt(putt), out float fx, out float fd);
                float tx = FloorX(fx), ty = FloorY(fd);
                yield return Throw(team, tx, ty, big ? BigRadius : Radius);
                if (big)
                {
                    hud.Banner("BIG SPLAT", "Holed: three times the paint   ·   " + Standing, TeamColor(team), 2.2f);
                    if (Arcade.Audio.TryLibrary("turfwar_big_splat", 0.95f)) Arcade.Audio.Excite(1f); else Arcade.Audio.Strike();   // puttr-app-2
                    Arcade.Shake(0.45f);
                    Arcade.Celebrate(70, At(tx - W * 0.5f, 6f, ty));
                }
                else
                {
                    hud.Banner(TeamName(team).ToUpperInvariant() + "  " + _pct[team] + "%", Standing, TeamColor(team), 1.8f);
                }
                ShowPutt(Arcade.TruePutt(putt), big ? "Big splat" : "Splat", TeamColor(team));
                hud.SetPip(team, index, Hud.Pip.Good, big ? "x3" : "");
            }
            hud.SetTotal(0, _pct[0]);
            hud.SetTotal(1, _pct[1]);
            Arcade.Shot("result");
            yield return new WaitForSecondsRealtime(0.9f);
            _turn++;
            ShowBoard();

            // One Player: Puttr AI takes the orange turn by itself.
            if (_players <= 1 && !Over)
            {
                yield return new WaitForSecondsRealtime(0.5f);
                team = Team;
                index = PuttIndex;
                yield return Throw(team, Rand(Inset, W - Inset), Rand(Inset, H - Inset), Radius);
                hud.Banner("PUTTR  " + _pct[team] + "%", Standing, TeamColor(team), 1.6f);
                hud.SetPip(team, index, Hud.Pip.Good);
                hud.SetTotal(0, _pct[0]);
                hud.SetTotal(1, _pct[1]);
                yield return new WaitForSecondsRealtime(0.8f);
                _turn++;
                ShowBoard();
            }
        }

        public override bool Over => _turn >= Putts * 2;

        /// <summary>Where the putt finished on the mat: x inches right of the hole's centre,
        /// d inches past it (negative is short). False when there is no path to go on.</summary>
        static bool Finish(PuttResult putt, out float x, out float d)
        {
            x = d = 0f;
            if (putt == null) return false;
            PuttLaunch launch = putt.Launch;
            if (launch.Reached)
            {
                float v = launch.HandoffSpeedInPerSec > 0f ? launch.HandoffSpeedInPerSec : launch.SpeedInPerSec;
                d = PaceWords.Past(v);
                x = launch.LineAtHoleInches + Mathf.Tan(Mathf.Clamp(launch.HeadingDeg, -40f, 40f) * Mathf.Deg2Rad) * d;
                return true;
            }
            // Short: the farthest it got up the ramp. (After that it rolls back down toward the
            // Player, so the last place the camera saw it is not where the putt ended.)
            if (putt.Path == null || putt.Path.Count == 0) return false;
            float best = float.MaxValue;
            foreach (BallPoint p in putt.Path)
            {
                Vector2 at = PuttrAnalysis.ToInchesIntegrated(p, putt.Hole);
                if (at.y < best) { best = at.y; x = at.x; }
            }
            d = -best;
            return true;
        }

        static float FloorX(float inchesRight) => Mathf.Lerp(Inset, W - Inset, Mathf.InverseLerp(-LineReach, LineReach, inchesRight));
        static float FloorY(float inchesPast) => Mathf.Lerp(Inset, H - Inset, Mathf.InverseLerp(-FrontIn, FarPastIn, inchesPast));

        public override Hud.Line[] Results(out string headline)
        {
            int blue = _pct[0], orange = _pct[1];
            string other = _players <= 1 ? "Puttr" : "Orange";
            headline = blue == orange ? "A draw" : blue > orange ? "Blue wins" : other + " wins";
            return new[]
            {
                new Hud.Line { Name = "Blue", Value = blue + "% of the floor", Dot = Make.Hex(0x3D68D8), Lit = blue > orange, Tag = blue > orange ? "WINNER" : "" },
                new Hud.Line { Name = other, Value = orange + "% of the floor", Dot = Make.Hex(0xF29F05), Lit = orange > blue, Tag = orange > blue ? "WINNER" : "" },
            };
        }

        public override void Final(out string title, out string body)
        {
            int blue = _pct[0], orange = _pct[1];
            if (_players <= 1)
            {
                title = blue + "  P E R C E N T";
                body = "Blue " + blue + "%, Puttr " + orange + "%.\n";
                body += blue > orange ? "More floor than Puttr.\n" : blue < orange ? "Puttr took more floor.\n" : "Level with Puttr.\n";
                return;
            }
            title = blue == orange ? "A   D R A W" : blue > orange ? "B L U E   W I N S" : "O R A N G E   W I N S";
            body = "";
            for (int team = 0; team < 2; team++)
            {
                string who = "";
                for (int i = 0; i < _players; i++)
                    if (TeamOf(i) == team) who += (who.Length > 0 ? " and " : "") + Arcade.PlayerName(i);
                bool won = _pct[team] > _pct[1 - team];
                body += TeamName(team) + "     " + _pct[team] + "%     " + who + (won ? "     winner" : "") + "\n";
            }
        }

        public override void End()
        {
            _ball.gameObject.SetActive(false);
            _ring.gameObject.SetActive(false);
        }
    }
}

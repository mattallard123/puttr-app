// The host's arena (Event Mode, Venue Mode): the Puttr as the hero of an NBA-style bowl, dressed in the host's
// brand. It is the host's home screen (HostPickerScreen) and the brand kit editor's preview.
//
//   The Puttr        the Improve tab's model (Arena.BuildMat): the photographed carpet, the printed circles,
//                    numbers and mark, the V2 box with its light bar; the carpet tinted to the product shots' green
//   The court        maple hardwood, white sidelines, an apron in the brand's colour, the brand's logo on the floor
//   The bowl         LED ad boards round the court (host, partner, Puttr last), a lower bowl of seated people in
//                    3D (built here, varied clothes, skin, hair and poses), a ribbon board, a painted upper deck
//   Overhead         a centre-hung scoreboard with the logo on its faces, roof lights, haze, sweeping spotlights
//   Gloss            soft streaks on the floor under every lit board: the polished-floor look of a broadcast
//
// Scene inches: the hole at the origin, the Player's end of the mat at z -139, the floor at y 0. Everything it
// reads about the brand comes from PuttrArcade.Brand, as the games do. The crowd's meshes are made once for each
// brand colour and kept (the home screen is rebuilt each time it comes back from a game).
using System;
using System.Collections.Generic;
using PuttrArcade;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuttrApp
{
    public static class BrandStage
    {
        public const int BoardCount = 6;

        // The court and the bowl.
        // The space inside the walls: 10 ft across and 20 ft long round the Puttr (2 ft by 12 ft, the box at the far
        // end), the hole at the origin.
        const float CourtX = 60f, CourtBack = 50f, CourtFront = -190f;
        const float BoardH = 32f;
        const int Rows = 9;
        const float RowDeep = 32f, RowRise = 15f, BowlStart = 8f;           // the lower bowl, from the boards back: a packed house up to the wall
        static float BowlDepth => BowlStart + Rows * RowDeep;
        static float BowlTop => Rows * RowRise + 8f;

        /// <summary>The Puttr's camera when nothing moves it (the brand kit preview): low behind the mat, the box and
        /// the crowd beyond it, the scoreboard overhead.</summary>
        public static readonly Vector3 Eye = new Vector3(6f, 168f, -236f), Target = new Vector3(0f, 0f, -52f);

        /// <summary>The lounge's camera (Venue Mode): lower and further back than the arena's, looking across the room at
        /// the back wall (the logo, the TV, the bar), the Puttr in front of it.</summary>
        public static readonly Vector3 LoungeEye = new Vector3(0f, 175f, -245f), LoungeTarget = new Vector3(-62f, 14f, -50f);   // swung left so the left TV clears the title
        /// <summary>The bar, for the lounge camera's pan (Matt: "pan the camera all the way the other way so you can see the bar and then back").</summary>
        public static readonly Vector3 LoungeBarEye = new Vector3(30f, 165f, -235f), LoungeBarTarget = new Vector3(185f, 40f, -25f);

        /// <summary>Where the lounge camera is at a time: a slow, steady sway from the Puttr across to the bar and back,
        /// PanSeconds each way, easing at each end and never stopping (Matt: "back and forth and much slower").</summary>
        public static void LoungePan(float time, out Vector3 eye, out Vector3 target)
        {
            const float PanSeconds = 60f;
            float k = 0.5f - 0.5f * Mathf.Cos(time * Mathf.PI / PanSeconds);
            eye = Vector3.Lerp(LoungeEye, LoungeBarEye, k) + new Vector3(0f, Mathf.Sin(time * 0.05f) * 4f, 0f);
            target = Vector3.Lerp(LoungeTarget, LoungeBarTarget, k);
        }

        public static Vector3 EyeForMode => BrandKit.Mode == BrandKit.Venue ? LoungeEye : Eye;
        public static Vector3 TargetForMode => BrandKit.Mode == BrandKit.Venue ? LoungeTarget : Target;

        /// <summary>The host's room for its mode: Venue Mode is a cocktail lounge (LoungeStage), Event Mode the arena.</summary>
        public static void ForMode(Live3D.Stage stage)
        {
            if (BrandKit.Mode == BrandKit.Venue) LoungeStage.Build(stage);
            else Build(stage);
        }

        public static void Build(Live3D.Stage stage)
        {
            Color primary = Brand.Primary;
            Arena.BuildMat(stage, out Material ring, false, new Color(0.62f, 1.08f, 0.78f));
            stage.Background = new Color32(2, 3, 4, 255);
            // The mat's own lights are made for the Improve tab's studio: here they are only a fill, and the court is lit
            // from the roof (KeyLights), so the Puttr is the brightest thing and the stands fall away into the dark.
            foreach (Light l in stage.Lights) l.intensity *= 0.32f;
            ring.SetColor("_Color", new Color(0f, 0f, 0f, 0f));   // the hole is just a hole: no ring round it

            Court(stage, primary);
            Boards(stage, primary);
            Crowd(stage, primary);
            Ribbon(stage, primary);
            Upper(stage, primary);
            Scoreboard(stage, primary);
            Roof(stage, primary);
            Transform[] flashes = Flashes(stage);
            Action<float> sweep = Spotlights(stage, primary);
            KeyLights(stage);

            // A ball on the mat, on its way to the hole.
            GameObject ball = stage.Primitive(PrimitiveType.Sphere, "Ball", stage.Lit(Color.white, 0.8f));
            ball.transform.localPosition = Arena.Spot(4, 2, LaneSpec.BallRadius);
            ball.transform.localScale = Vector3.one * LaneSpec.BallRadius * 2f;
            BoxLight(stage);

            var rng = new System.Random(11);
            float next = 0f;
            stage.Tick = dt =>
            {
                next -= dt;
                foreach (Transform f in flashes) if (f.localScale.x > 0.01f) f.localScale = Vector3.MoveTowards(f.localScale, Vector3.zero, dt * 60f);
                if (next <= 0f && flashes.Length > 0)
                {
                    next = 0.08f + (float)rng.NextDouble() * 0.35f;
                    flashes[rng.Next(flashes.Length)].localScale = Vector3.one * (3.5f + (float)rng.NextDouble() * 3f);
                }
                sweep(stage.Time);
                Sway(stage.Time);
                return true;
            };

            stage.Camera.fieldOfView = 48f;
            stage.Camera.farClipPlane = 6000f;
            stage.Look(Eye, Target);
            sweep(0f);
        }

        // ── The court ────────────────────────────────────────────────────────

        static void Court(Live3D.Stage stage, Color primary)
        {
            float w = CourtX * 2f, d = CourtBack - CourtFront;
            // A black court, polished: the lights and the boards show in it.
            Material maple = stage.Lit(new Color(0.018f, 0.019f, 0.022f), 0.9f, 0.2f);
            GameObject floor = stage.Primitive(PrimitiveType.Quad, "Court", maple);
            floor.transform.localPosition = new Vector3(0, 0f, (CourtBack + CourtFront) / 2f);
            floor.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            floor.transform.localScale = new Vector3(w, d, 1f);
            // Under everything beyond the court (the bowl's floor, seen between the rows): dark.
            stage.Box("Base", new Vector3(0, -1.2f, 0), new Vector3(4000f, 1f, 4000f), stage.Lit(new Color(0.01f, 0.011f, 0.013f), 0.2f));
            // Plain black: no lines, no logos on the floor.
        }

        // ── LED boards round the court ───────────────────────────────────────

        static void Boards(Live3D.Stage stage, Color primary)
        {
            // Every board is black (an LED board switched to a black ground): the logos carry the colour.
            Material panel = stage.Emissive(new Color(0.008f, 0.008f, 0.01f), new Color(0.004f, 0.004f, 0.005f), 0.7f);
            // Puttr's own board: black, the logo's white letters and green mark on it.
            Material puttr = stage.Emissive(new Color(0.008f, 0.008f, 0.01f), new Color(0.004f, 0.004f, 0.005f), 0.7f);
            Material hostPanel = panel;
            Material strip = stage.Emissive(primary, primary * 1.6f);
            Material cap = stage.Lit(new Color(0.05f, 0.052f, 0.056f), 0.7f, 0.5f);

            // The boards run round the walls as one row (the left wall from the Player's end, the back, the right wall
            // back toward the Player), so the host's and partners' logos take turns and Puttr's is the one board at the end.
            int Count(float length) => Mathf.Max(2, Mathf.RoundToInt(length / 62f));
            float sideLength = CourtBack - CourtFront, backLength = CourtX * 2f;
            int total = Count(sideLength) * 2 + Count(backLength), next = 0;
            void Run(Vector3 from, Vector3 to, float turn)
            {
                float length = Vector3.Distance(from, to);
                int n = Count(length);
                for (int i = 0; i < n; i++)
                {
                    Vector3 at = Vector3.Lerp(from, to, (i + 0.5f) / n);
                    int k = next++;
                    Brand.Slot slot = Brand.SlotAt(k, total);
                    Texture2D logo = Brand.Panel(k, total);
                    Board(stage, at + Vector3.up * BoardH / 2f, turn, length / n - 2f, slot == Brand.Slot.Puttr ? puttr : slot == Brand.Slot.Host ? hostPanel : panel, slot == Brand.Slot.Host ? strip : null, logo);
                }
                Transform capBar = stage.Box("Board cap", (from + to) / 2f + Vector3.up * (BoardH + 0.7f), new Vector3(length, 1.4f, 3f), cap).transform;
                capBar.localRotation = Quaternion.Euler(0, turn, 0);
                Gloss(stage, from, to, turn, primary);
            }
            Run(new Vector3(-CourtX, 0, CourtFront), new Vector3(-CourtX, 0, CourtBack), -90f);
            Run(new Vector3(-CourtX, 0, CourtBack), new Vector3(CourtX, 0, CourtBack), 0f);
            Run(new Vector3(CourtX, 0, CourtBack), new Vector3(CourtX, 0, CourtFront), 90f);
        }

        // A board: a deep panel leaning back a little (as LED boards do), a lit lip along the top, the face set in a
        // frame, and the logo raised off the face with depth (Raised).
        static void Board(Live3D.Stage stage, Vector3 at, float turn, float width, Material ground, Material strip, Texture2D logo)
        {
            Transform g = stage.Group("Board");
            g.localPosition = at;
            g.localRotation = Quaternion.Euler(0, turn, 0) * Quaternion.Euler(9f, 0, 0);
            Material frame = Frame(stage);
            stage.Mesh("Body", Make.RoundedBox(new Vector3(width, BoardH, 7f), 1.2f), frame, g).transform.localPosition = new Vector3(0, 0, 3.5f);
            stage.Box("Face", new Vector3(0, 0.6f, -0.05f), new Vector3(width - 3f, BoardH - 4.2f, 0.4f), ground, g);
            stage.Box("Lip", new Vector3(0, BoardH / 2f - 0.7f, -0.3f), new Vector3(width - 1f, 1.2f, 1f), stage.Emissive(new Color(0.9f, 0.92f, 0.95f) * 0.4f, new Color(0.9f, 0.92f, 0.95f) * 0.5f), g);
            if (strip != null) stage.Box("Strip", new Vector3(0, -BoardH / 2f + 2.2f, -0.35f), new Vector3(width - 3f, 1.6f, 0.4f), strip, g);
            if (logo == null) return;
            Vector2 size = Brand.Fit(logo, width * 0.7f, BoardH * 0.5f);
            Raised(stage, g, logo, new Vector3(0, 1.2f, -0.3f), size, 1.4f);
        }

        static Material Frame(Live3D.Stage stage) => stage.Lit(new Color(0.045f, 0.047f, 0.052f), 0.75f, 0.6f);

        /// <summary>A logo standing off a face with depth: its face bright, and behind it the same shape again and
        /// again, darker, back to the face, so from the side it reads as letters cut from a block (front faces -Z).</summary>
        static void Raised(Live3D.Stage stage, Transform parent, Texture2D logo, Vector3 at, Vector2 size, float depth)
        {
            const int layers = 7;
            Material side = stage.Glow(new Color(0.16f, 0.17f, 0.19f, 1f), false, logo);
            for (int i = layers; i >= 1; i--)
            {
                GameObject layer = stage.Primitive(PrimitiveType.Quad, "Logo depth", side, parent);
                layer.transform.localPosition = at + new Vector3(0, 0, -depth * (i - 1) / layers);
                layer.transform.localScale = new Vector3(size.x, size.y, 1f);
            }
            GameObject face = stage.Primitive(PrimitiveType.Quad, "Logo", stage.Glow(new Color(1.2f, 1.2f, 1.2f, 1f), false, logo), parent);
            face.transform.localPosition = at + new Vector3(0, 0, -depth);
            face.transform.localScale = new Vector3(size.x, size.y, 1f);
        }

        // The polished floor: a soft streak of a light's colour on the floor in front of it, as a reflection.
        static Texture2D _streak;
        static Texture2D Streak()
        {
            if (_streak != null) return _streak;
            const int w = 64, h = 128;
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = Mathf.Abs(x / (w - 1f) - 0.5f) * 2f, v = y / (h - 1f);   // v 1 at the board
                    float a = Mathf.Pow(v, 2.2f) * Mathf.Pow(Mathf.Clamp01(1f - u), 0.6f);
                    px[y * w + x] = new Color(1f, 1f, 1f, a);
                }
            _streak = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            _streak.SetPixels(px);
            _streak.Apply(false, true);
            return _streak;
        }

        static void Gloss(Live3D.Stage stage, Vector3 from, Vector3 to, float turn, Color tint)
        {
            float length = Vector3.Distance(from, to);
            Material m = stage.Glow(new Color(tint.r * 0.5f + 0.25f, tint.g * 0.5f + 0.25f, tint.b * 0.5f + 0.25f, 0.13f), true, Streak());
            GameObject q = stage.Primitive(PrimitiveType.Quad, "Gloss", m);
            Vector3 inward = Quaternion.Euler(0, turn, 0) * Vector3.back;
            q.transform.localPosition = (from + to) / 2f + inward * 40f + Vector3.up * 0.08f;
            q.transform.localRotation = Quaternion.Euler(0, turn, 0) * Quaternion.Euler(90f, 0, 0);
            q.transform.localScale = new Vector3(length, 80f, 1f);
        }

        // ── The crowd ────────────────────────────────────────────────────────

        static readonly List<Transform> _sway = new List<Transform>();
        static readonly List<float> _swayPhase = new List<float>();

        static void Sway(float t)
        {
            for (int i = 0; i < _sway.Count; i++)
            {
                if (_sway[i] == null) continue;
                float p = _swayPhase[i];
                Vector3 at = _sway[i].localPosition;
                at.y = Mathf.Sin(t * (1.3f + p * 0.4f) + p * 6f) * 0.35f;
                _sway[i].localPosition = at;
            }
        }

        /// <summary>The lower bowl: risers and seats behind the boards on three sides, the courtside row at floor
        /// level, and a seated person in most seats (People.cs). Each side is a few meshes (one per two rows) so the
        /// rows can breathe a little out of step.</summary>
        static void Crowd(Live3D.Stage stage, Color primary)
        {
            _sway.Clear();
            _swayPhase.Clear();
            Material risers = stage.Lit(new Color(0.03f, 0.031f, 0.034f), 0.25f);
            Material seatMat = stage.Lit(primary * 0.22f + new Color(0.02f, 0.02f, 0.022f), 0.35f);
            Material people = People.Material(primary);

            var steps = new List<(Vector3, Vector3, Quaternion)>();
            var seats = new List<(Vector3, Vector3, Quaternion)>();
            for (int side = 0; side < 3; side++)
            {
                // Along a stand: the back runs across x; the sides run along z.
                // The side stands start 40 in up from the Player's end of the space: nobody sits beside the camera.
                float length = side == 0 ? CourtX * 2f + BowlDepth * 2f : CourtBack - CourtFront + BowlDepth - 40f;
                Quaternion turn = Quaternion.Euler(0, side == 0 ? 0f : side == 1 ? -90f : 90f, 0);
                Vector3 Place(float along, float back, float y) => side == 0
                    ? new Vector3(along, y, CourtBack + back)
                    : new Vector3((side == 1 ? -1f : 1f) * (CourtX + back), y, (CourtBack + CourtFront) / 2f + BowlDepth / 2f + 20f + along);

                int seed = 100 + side * 31;
                for (int r = 0; r < Rows; r++)
                {
                    float back = BowlStart + r * RowDeep, floorY = (r + 1) * RowRise;
                    Vector3 mid = Place(0, back + RowDeep / 2f, floorY / 2f);
                    steps.Add((mid, side == 0 ? new Vector3(length, floorY, RowDeep) : new Vector3(RowDeep, floorY, length), Quaternion.identity));
                    Vector3 seatRow = Place(0, back + RowDeep - 6f, floorY + 9f);
                    seats.Add((seatRow, side == 0 ? new Vector3(length, 18f, 3f) : new Vector3(3f, 18f, length), Quaternion.identity));
                }
                for (int r = 0; r < Rows; r += 2)
                {
                    var rows = new People.Builder();
                    for (int k = r; k < Mathf.Min(Rows, r + 2); k++)
                    {
                        float back = BowlStart + k * RowDeep + RowDeep * 0.42f, floorY = (k + 1) * RowRise;
                        int seatsInRow = Mathf.FloorToInt(length / 22f);
                        for (int c = 0; c < seatsInRow; c++)
                        {
                            int who = seed * 977 + k * 131 + c;
                            if (People.Hash(who) % 100 < 8) continue;   // an empty seat
                            float along = (c + 0.5f) * 22f - length / 2f;
                            rows.Seated(Place(along, back, floorY), turn, who);
                        }
                    }
                    Emit(stage, rows, people, side, r);
                }
            }
            stage.Boxes("Risers", steps, risers);
            stage.Boxes("Seat backs", seats, seatMat);
        }

        static void Emit(Live3D.Stage stage, People.Builder b, Material m, int side, int row)
        {
            foreach (Mesh mesh in b.Meshes())
            {
                var go = new GameObject("People");
                go.transform.SetParent(stage.Root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = m;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                stage.Own(mesh);
                _sway.Add(go.transform);
                _swayPhase.Add((side * 7 + row * 3) % 11 / 11f);
            }
        }

        // The ribbon board along the front of the upper deck: an LED band of the brand's colour with logos on it.
        static void Ribbon(Live3D.Stage stage, Color primary)
        {
            float y = BowlTop + 30f, back = BowlDepth + 8f;
            Material band = stage.Emissive(new Color(0.02f, 0.022f, 0.025f), new Color(0.025f, 0.027f, 0.03f), 0.6f);
            Material edge = stage.Emissive(primary, primary * 1.5f);
            void Run(Vector3 from, Vector3 to, float turn)
            {
                float length = Vector3.Distance(from, to);
                Transform g = stage.Group("Ribbon");
                g.localPosition = (from + to) / 2f + Vector3.up * y;
                g.localRotation = Quaternion.Euler(0, turn, 0);
                stage.Box("Ribbon", Vector3.zero, new Vector3(length, 24f, 2f), band, g);
                stage.Box("Ribbon edge", new Vector3(0, -12.6f, -0.4f), new Vector3(length, 1.4f, 1f), edge, g);
                stage.Box("Ribbon edge", new Vector3(0, 12.6f, -0.4f), new Vector3(length, 1.4f, 1f), edge, g);
                int n = Mathf.Max(2, Mathf.RoundToInt(length / 150f));
                for (int i = 0; i < n; i++)
                {
                    Texture2D logo = Brand.Panel(i % BoardCount, BoardCount);
                    if (logo == null) continue;
                    Vector2 size = Brand.Fit(logo, 110f, 16f);
                    Raised(stage, g, logo, new Vector3(-length / 2f + length * (i + 0.5f) / n, 0, -1.1f), size, 1.6f);
                }
            }
            Run(new Vector3(-CourtX - back, 0, CourtBack + back), new Vector3(CourtX + back, 0, CourtBack + back), 0f);
            Run(new Vector3(-CourtX - back, 0, CourtFront), new Vector3(-CourtX - back, 0, CourtBack + back), -90f);
            Run(new Vector3(CourtX + back, 0, CourtFront), new Vector3(CourtX + back, 0, CourtBack + back), 90f);
        }

        // The upper deck: the painted crowd, rising steeply into the dark behind the ribbon.
        static void Upper(Live3D.Stage stage, Color primary)
        {
            Texture2D people = PaintedCrowd(primary);
            const float rise = 0.85f, deep = 520f;
            float tilt = Mathf.Atan(rise) * Mathf.Rad2Deg, slope = deep / Mathf.Cos(tilt * Mathf.Deg2Rad);
            float baseY = BowlTop + 44f, back = BowlDepth + 14f, midY = baseY + deep * rise / 2f;
            void Bank(Vector3 at, float turn, float width)
            {
                Material m = stage.Glow(new Color(0.34f, 0.35f, 0.4f, 1f), false, people);
                m.SetTextureScale("_MainTex", new Vector2(width / 300f, 2.2f));
                GameObject q = stage.Primitive(PrimitiveType.Quad, "Upper deck", m);
                q.transform.localPosition = at;
                q.transform.localRotation = Quaternion.Euler(0, turn, 0) * Quaternion.Euler(90f - tilt, 0, 0);
                q.transform.localScale = new Vector3(width, slope, 1f);
            }
            float wide = (CourtX + back) * 2f + deep * 2f, longways = CourtBack - CourtFront + back + deep;
            Bank(new Vector3(0, midY, CourtBack + back + deep / 2f), 0f, wide);
            Bank(new Vector3(-CourtX - back - deep / 2f, midY, (CourtBack + CourtFront) / 2f + (back + deep) / 2f), -90f, longways);
            Bank(new Vector3(CourtX + back + deep / 2f, midY, (CourtBack + CourtFront) / 2f + (back + deep) / 2f), 90f, longways);
            // The wall under the ribbon, between the lower bowl and the upper deck.
            Material wall = stage.Lit(new Color(0.012f, 0.013f, 0.015f), 0.3f);
            stage.Box("Fascia", new Vector3(0, BowlTop + 14f, CourtBack + BowlDepth + 4f), new Vector3(wide, 28f, 4f), wall);
        }

        // ── Overhead ─────────────────────────────────────────────────────────

        // The centre-hung scoreboard: four screens round a dark box, an LED ring in the brand's colour above and below,
        // the logo on every face. It hangs over the far end of the court, where a camera behind the mat sees it.
        static void Scoreboard(Live3D.Stage stage, Color primary)
        {
            const float w = 120f, h = 66f, y = 176f, z = CourtBack + 330f;
            Transform g = stage.Group("Scoreboard");
            g.localPosition = new Vector3(0, y, z);
            g.localRotation = Quaternion.Euler(0, 35f, 0);   // turned, so two faces show
            Material body = stage.Lit(new Color(0.02f, 0.021f, 0.024f), 0.6f, 0.5f);
            Material screen = stage.Emissive(new Color(0.02f, 0.022f, 0.026f), new Color(0.035f, 0.038f, 0.045f), 0.9f);
            Material ringLit = stage.Emissive(primary * 0.3f, primary * 0.45f);
            stage.Box("Box", Vector3.zero, new Vector3(w + 8f, h + 8f, w + 8f), body, g);
            stage.Box("Ring", new Vector3(0, h / 2f + 7f, 0), new Vector3(w + 12f, 5f, w + 12f), ringLit, g);
            stage.Box("Ring", new Vector3(0, -h / 2f - 7f, 0), new Vector3(w + 12f, 5f, w + 12f), ringLit, g);
            stage.Box("Crown", new Vector3(0, h / 2f + 26f, 0), new Vector3(w * 0.7f, 24f, w * 0.7f), body, g);
            Texture2D logo = Brand.Logo;
            for (int face = 0; face < 4; face++)
            {
                Transform f = stage.Group("Face", g);
                f.localRotation = Quaternion.Euler(0, face * 90f, 0);
                float front = -(w / 2f + 4.4f);
                stage.Box("Screen", new Vector3(0, 0, front + 1.5f), new Vector3(w - 6f, h - 6f, 0.6f), screen, f);
                // The bezel round the screen, standing proud of it.
                Material bezel = Frame(stage);
                stage.Box("Bezel", new Vector3(0, h / 2f - 1.5f, front), new Vector3(w, 3f, 3f), bezel, f);
                stage.Box("Bezel", new Vector3(0, -h / 2f + 1.5f, front), new Vector3(w, 3f, 3f), bezel, f);
                stage.Box("Bezel", new Vector3(-w / 2f + 1.5f, 0, front), new Vector3(3f, h, 3f), bezel, f);
                stage.Box("Bezel", new Vector3(w / 2f - 1.5f, 0, front), new Vector3(3f, h, 3f), bezel, f);
                if (logo == null) continue;
                Vector2 size = Brand.Fit(logo, w * 0.74f, h * 0.52f);
                Raised(stage, f, logo, new Vector3(0, 0, front + 1.1f), size, 3.2f);
            }
            foreach (float x in new[] { -50f, 50f }) foreach (float zz in new[] { -50f, 50f })
                stage.Box("Cable", new Vector3(x, h / 2f + 300f, zz), new Vector3(0.8f, 560f, 0.8f), body, g);
            
        }

        // The roof: banks of white lights over the court and a haze in the air under them.
        static void Roof(Live3D.Stage stage, Color primary)
        {
            Material lamp = stage.Glow(new Color(0.95f, 0.97f, 1f, 0.95f), true, Make.SoftDot());
            Material frame = stage.Lit(new Color(0.018f, 0.019f, 0.022f), 0.4f, 0.5f);
            var dots = new List<(Vector3, float)>();
            foreach (float x in new[] { -330f, -110f, 110f, 330f })
                foreach (float z in new[] { -120f, 160f, 440f })
                {
                    stage.Box("Lamp bank", new Vector3(x, 560f, z), new Vector3(70f, 10f, 30f), frame);
                    for (int k = -2; k <= 2; k++) dots.Add((new Vector3(x + k * 12f, 554f, z), 26f));
                }
            foreach (var (at, size) in dots)
            {
                GameObject dot = stage.Primitive(PrimitiveType.Quad, "Lamp", lamp);
                dot.transform.localPosition = at;
                dot.transform.localRotation = Quaternion.Euler(80f, 0, 0);
                dot.transform.localScale = Vector3.one * size;
            }
            // Haze: big soft light in the air over the court, the arena's atmosphere.
            Material haze = stage.Glow(new Color(0.75f, 0.82f, 0.95f, 0.035f), true, Make.SoftDot());
            foreach (Vector3 at in new[] { new Vector3(-150f, 260f, 120f), new Vector3(170f, 300f, 220f), new Vector3(0f, 200f, 330f), new Vector3(-60f, 360f, 420f) })
            {
                GameObject h = stage.Primitive(PrimitiveType.Quad, "Haze", haze);
                h.transform.localPosition = at;
                h.transform.localScale = Vector3.one * 560f;
            }
        }

        // The light inside the Puttr's box, as on the product: a white strip under the lid, toward the top, shining
        // straight down on the hole, and the pool of light it makes on the felt round the hole.
        internal static void BoxLight(Live3D.Stage stage)
        {
            float lift = 0.5f - LaneSpec.MatY;                  // the mat model's lift (Arena.BuildMat: Top - MatY)
            float under = lift + 13.45f, hole = lift + LaneSpec.SurfaceY(0f);
            stage.Box("Box light", new Vector3(0, under, -1.2f), new Vector3(18f, 0.35f, 2.2f), stage.Emissive(Color.white, Color.white * 6f));
            var at = new GameObject("Box light");
            at.transform.SetParent(stage.Root, false);
            at.transform.localPosition = new Vector3(0, under - 0.3f, -1.2f);
            at.transform.localRotation = Quaternion.LookRotation(new Vector3(0, hole, 0) - at.transform.localPosition);
            Light l = at.AddComponent<Light>();
            l.type = LightType.Spot;
            l.color = new Color(0.96f, 0.98f, 1f);
            l.range = 60f;
            l.spotAngle = 120f;
            l.innerSpotAngle = 60f;
            l.intensity = 900f;
            l.shadows = LightShadows.None;
            l.cullingMask = 1 << Live3D.Layer;
            l.enabled = false;
            stage.Lights.Add(l);
            // The lit felt round the hole (a light alone falls off too fast to show as the photos do).
            const float poolW = 26f;
            GameObject pool = stage.Primitive(PrimitiveType.Quad, "Box light pool", stage.Glow(new Color(0.85f, 0.95f, 0.9f, 0.32f), true, PoolAroundHole(LaneSpec.HoleRadius / poolW)));
            pool.transform.localPosition = new Vector3(0, hole + 0.08f, 0f);
            pool.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            pool.transform.localScale = new Vector3(poolW, poolW, 1f);
        }

        // A soft pool of light with the hole left dark: clear inside the hole's edge (r, a share of the width).
        static Texture2D _pool;
        static Texture2D PoolAroundHole(float r)
        {
            if (_pool != null) return _pool;
            const int n = 128;
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = x / (n - 1f) - 0.5f, dy = y / (n - 1f) - 0.5f, d = Mathf.Sqrt(dx * dx + dy * dy);
                    float glow = Mathf.Pow(Mathf.Clamp01(1f - d * 2f), 1.6f);
                    float edge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(r * 1.02f, r * 1.25f, d));
                    px[y * n + x] = new Color(1f, 1f, 1f, glow * edge);
                }
            _pool = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            _pool.SetPixels(px);
            _pool.Apply(false, true);
            return _pool;
        }

        // Two strong white lights straight down on the Puttr: the hero is the brightest thing in the building.
        static void KeyLights(Live3D.Stage stage)
        {
            foreach (Vector3 from in new[] { new Vector3(-70f, 330f, -120f), new Vector3(70f, 330f, -10f) })
            {
                var at = new GameObject("Key light");
                at.transform.SetParent(stage.Root, false);
                at.transform.localPosition = from;
                at.transform.localRotation = Quaternion.LookRotation(new Vector3(0, 0, -60f) - from);
                Light l = at.AddComponent<Light>();
                l.type = LightType.Spot;
                l.color = new Color(1f, 0.97f, 0.92f);
                l.range = 700f;
                l.spotAngle = 64f;
                l.innerSpotAngle = 20f;
                l.intensity = 150000f;
                l.shadows = LightShadows.None;
                l.cullingMask = 1 << Live3D.Layer;
                l.enabled = false;
                stage.Lights.Add(l);
            }
        }

        // Camera flashes in the crowd (returned at no size: Build's tick sets them off).
        static Transform[] Flashes(Live3D.Stage stage)
        {
            Material flash = stage.Glow(new Color(1f, 1f, 1f, 0.95f), true, Make.SoftDot());
            var flashes = new List<Transform>();
            var rng = new System.Random(3);
            for (int i = 0; i < 40; i++)
            {
                float up = (float)rng.NextDouble();
                float back = BowlStart + up * (BowlDepth - BowlStart), y = 20f + up * BowlTop;
                GameObject f = stage.Primitive(PrimitiveType.Quad, "Flash", flash);
                int where = i % 3;
                if (where == 0) f.transform.localPosition = new Vector3(((float)rng.NextDouble() * 2f - 1f) * (CourtX + 200f), y, CourtBack + back);
                else
                {
                    f.transform.localPosition = new Vector3((where == 1 ? -1f : 1f) * (CourtX + back), y, CourtFront + (float)rng.NextDouble() * (CourtBack - CourtFront));
                    f.transform.localRotation = Quaternion.Euler(0, where == 1 ? -70f : 70f, 0);
                }
                f.transform.localScale = Vector3.zero;
                flashes.Add(f.transform);
            }
            return flashes.ToArray();
        }

        // Spotlights in the roof sweeping the floor (two white, two in the brand's colour); every nine seconds they
        // swing in together on the Puttr. Each is a drawn beam that thins out before the floor, a pool, and a real
        // spot light that lights what it crosses. Returns the mover: call it with the time.
        const float SpotPower = 380000f;

        static Action<float> Spotlights(Live3D.Stage stage, Color primary)
        {
            const int count = 4;
            var profile = new List<Vector2>();
            const int rings = 9;
            for (int i = 0; i < rings; i++) { float y = i / (float)(rings - 1); profile.Add(new Vector2(Mathf.Lerp(0.012f, 1f, y), y)); }
            Mesh cone = Make.Lathe(profile, 28);
            Vector3[] verts = cone.vertices;
            var tint = new Color[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                float y = verts[i].y;
                tint[i] = new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.06f, y)) * Mathf.Pow(Mathf.Clamp01(1f - y), 1.1f));
            }
            cone.colors = tint;
            stage.Own(cone);
            Texture2D soft = Make.SoftDot();
            var from = new Vector3[count];
            var beams = new Transform[count];
            var pools = new Transform[count];
            var spots = new Light[count];
            for (int i = 0; i < count; i++)
            {
                bool brand = i % 2 == 1;
                Color c = brand ? primary : new Color(0.92f, 0.96f, 1f);
                from[i] = new Vector3((i < 2 ? -1f : 1f) * 170f, 420f, i % 2 == 0 ? -120f : 140f);
                var beam = new GameObject("Spot beam");
                beam.transform.SetParent(stage.Root, false);
                beam.AddComponent<MeshFilter>().sharedMesh = cone;
                MeshRenderer br = beam.AddComponent<MeshRenderer>();
                br.sharedMaterial = stage.Glow(new Color(c.r, c.g, c.b, brand ? 0.15f : 0.1f), true);
                br.shadowCastingMode = ShadowCastingMode.Off;
                br.receiveShadows = false;
                beams[i] = beam.transform;
                GameObject pool = stage.Primitive(PrimitiveType.Quad, "Spot pool", stage.Glow(new Color(c.r, c.g, c.b, brand ? 0.3f : 0.2f), true, soft));
                pool.transform.localRotation = Quaternion.Euler(90f, 0, 0);
                pools[i] = pool.transform;
                var lightAt = new GameObject("Spot light");
                lightAt.transform.SetParent(stage.Root, false);
                lightAt.transform.localPosition = from[i];
                Light spot = lightAt.AddComponent<Light>();
                spot.type = LightType.Spot;
                spot.color = c;
                spot.range = 900f;
                spot.spotAngle = 9f;
                spot.innerSpotAngle = 5f;
                spot.intensity = SpotPower;
                spot.shadows = LightShadows.None;
                spot.cullingMask = 1 << Live3D.Layer;
                spot.enabled = false;
                stage.Lights.Add(spot);
                spots[i] = spot;
            }
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-app2SpotCheck") >= 0)
                for (int i = 0; i < count; i++) { beams[i].gameObject.SetActive(false); pools[i].gameObject.SetActive(false); }
            var home = new Vector3(0, 0.6f, -60f);
            return t =>
            {
                float round = Mathf.Repeat(t, 9f);
                float together = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(5.6f, 6.6f, round)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(8.2f, 9f, round)));
                for (int i = 0; i < count; i++)
                {
                    float a = t * (0.42f + 0.1f * i) + i * 1.7f, b = t * (0.33f + 0.07f * i) + i * 0.9f;
                    var roam = new Vector3(Mathf.Sin(a) * (CourtX - 30f), 0.6f, (CourtBack + CourtFront) / 2f + 60f + Mathf.Sin(b) * (CourtBack - CourtFront) * 0.38f);
                    Vector3 to = Vector3.Lerp(roam, home + new Vector3((i - 1.5f) * 9f, 0, (i % 2 == 0 ? -1f : 1f) * 26f), together);
                    Vector3 dir = to - from[i];
                    float len = dir.magnitude, wide = Mathf.Lerp(28f, 22f, together);
                    beams[i].localPosition = from[i];
                    beams[i].localRotation = Quaternion.FromToRotation(Vector3.up, dir / len);
                    beams[i].localScale = new Vector3(wide * 0.5f, len, wide * 0.5f);
                    pools[i].localPosition = to;
                    pools[i].localScale = new Vector3(wide * 1.7f, wide * 1.7f, 1f);
                    spots[i].transform.localRotation = Quaternion.LookRotation(dir / len);
                    spots[i].spotAngle = Mathf.Atan2(wide * 0.62f, len) * Mathf.Rad2Deg * 2f;
                    spots[i].innerSpotAngle = spots[i].spotAngle * 0.55f;
                }
            };
        }

        // The upper deck's people, painted once for each brand colour: rows of heads and shoulders, a few in the
        // brand's colour, darker toward the roof. It tiles along a bank.
        static readonly Dictionary<Color, Texture2D> Painted = new Dictionary<Color, Texture2D>();

        static Texture2D PaintedCrowd(Color primary)
        {
            if (Painted.TryGetValue(primary, out Texture2D made) && made != null) return made;
            const int w = 1024, h = 1024, rows = 34, seats = 56;
            var px = new Color[w * h];
            var rng = new System.Random(7);
            Color[] shirts = { new Color(0.75f, 0.76f, 0.78f), new Color(0.16f, 0.2f, 0.3f), new Color(0.5f, 0.12f, 0.12f), new Color(0.82f, 0.7f, 0.3f), new Color(0.1f, 0.1f, 0.12f), new Color(0.3f, 0.42f, 0.56f), new Color(0.9f, 0.9f, 0.9f) };
            Color[] skins = { new Color(0.86f, 0.67f, 0.54f), new Color(0.6f, 0.42f, 0.3f), new Color(0.36f, 0.24f, 0.17f), new Color(0.94f, 0.78f, 0.66f) };
            float rowH = h / (float)rows, seatW = w / (float)seats;
            for (int r = 0; r < rows; r++)
            {
                float lit = Mathf.Lerp(0.8f, 0.1f, Mathf.Pow(r / (float)(rows - 1), 0.7f));
                int y0 = Mathf.RoundToInt(r * rowH), y1 = Mathf.RoundToInt((r + 1) * rowH);
                for (int y = y0; y < y1; y++) for (int x = 0; x < w; x++) px[y * w + x] = new Color(0.01f, 0.011f, 0.014f) * lit + new Color(0.003f, 0.003f, 0.004f);
                float shift = (r % 2) * 0.5f;
                for (int c = 0; c < seats; c++)
                {
                    if (rng.NextDouble() < 0.08) continue;
                    Color shirt = rng.NextDouble() < 0.1 ? primary : shirts[rng.Next(shirts.Length)];
                    Color skin = skins[rng.Next(skins.Length)];
                    float cx = (c + 0.5f + shift + ((float)rng.NextDouble() - 0.5f) * 0.22f) * seatW;
                    float tall = 0.9f + (float)rng.NextDouble() * 0.2f;
                    float shoulder = seatW * 0.3f, headR = seatW * 0.17f * tall;
                    float chestTop = y0 + rowH * 0.5f * tall, headY = chestTop + headR * 1.25f;
                    for (int y = y0; y < y1; y++)
                        for (int xi = (int)(cx - seatW * 0.5f); xi <= (int)(cx + seatW * 0.5f); xi++)
                        {
                            int x = ((xi % w) + w) % w;
                            float dx = xi - cx, dy = y - headY;
                            float soft = 0.75f + 0.25f * Mathf.Clamp01(1f - Mathf.Abs(dx) / shoulder);
                            if (dx * dx + dy * dy <= headR * headR) px[y * w + x] = skin * lit * 0.85f * soft;
                            else if (y <= chestTop)
                            {
                                float up = Mathf.InverseLerp(y0, chestTop, y);
                                float half = shoulder * (up < 0.7f ? 1f : Mathf.Lerp(1f, 0.45f, (up - 0.7f) / 0.3f));
                                if (Mathf.Abs(dx) <= half) px[y * w + x] = shirt * lit * 0.6f * soft;
                            }
                        }
                }
            }
            for (int i = 0; i < px.Length; i++) px[i].a = 1f;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
            tex.SetPixels(px);
            tex.Apply(true);
            Painted[primary] = tex;
            return tex;
        }
    }

    /// <summary>
    /// Seated people for the arena's lower bowl, built as meshes: hips and thighs on the seat, a torso, shoulders,
    /// two arms in one of a few poses (hands in the lap, clapping, one arm up, elbows on knees, a phone held up), a
    /// neck, a head and hair (short, long, a cap or none). Every person has their own clothes, skin and hair, picked
    /// from a palette texture (each vertex's UV is its colour's cell), so a whole stand is one lit mesh.
    /// </summary>
    public static class People
    {
        // The palette: 16 x 16 cells, 4 pixels each.
        const int Cells = 16, CellPx = 4;
        static readonly Dictionary<Color, Texture2D> _palettes = new Dictionary<Color, Texture2D>();
        static readonly Dictionary<Color, Material> _materials = new Dictionary<Color, Material>();

        static readonly Color[] Shirts =
        {
            new Color(0.8f, 0.8f, 0.78f), new Color(0.06f, 0.065f, 0.075f), new Color(0.11f, 0.14f, 0.24f), new Color(0.36f, 0.08f, 0.09f),
            new Color(0.56f, 0.5f, 0.38f), new Color(0.24f, 0.33f, 0.45f), new Color(0.18f, 0.26f, 0.18f), new Color(0.42f, 0.42f, 0.44f),
            new Color(0.5f, 0.27f, 0.14f), new Color(0.26f, 0.17f, 0.3f), new Color(0.9f, 0.9f, 0.88f), new Color(0.19f, 0.19f, 0.21f),
        };
        static readonly Color[] Skins =
        {
            new Color(0.93f, 0.76f, 0.64f), new Color(0.84f, 0.64f, 0.5f), new Color(0.68f, 0.48f, 0.34f),
            new Color(0.5f, 0.34f, 0.23f), new Color(0.34f, 0.22f, 0.15f), new Color(0.76f, 0.58f, 0.44f),
        };
        static readonly Color[] Hairs =
        {
            new Color(0.05f, 0.04f, 0.035f), new Color(0.16f, 0.1f, 0.06f), new Color(0.32f, 0.2f, 0.1f),
            new Color(0.62f, 0.5f, 0.3f), new Color(0.5f, 0.5f, 0.5f), new Color(0.08f, 0.07f, 0.06f),
        };
        static readonly Color[] Pants =
        {
            new Color(0.1f, 0.13f, 0.22f), new Color(0.07f, 0.07f, 0.08f), new Color(0.32f, 0.3f, 0.26f), new Color(0.2f, 0.27f, 0.38f),
        };
        // Cells: 0..11 shirts, 12..14 the brand's colour (three shades), 16..21 skins, 24..29 hair, 32..35 trousers, 40 phone.
        const int BrandCell = 12, SkinCell = 16, HairCell = 24, PantsCell = 32, PhoneCell = 40;

        static Texture2D Palette(Color primary)
        {
            if (_palettes.TryGetValue(primary, out Texture2D made) && made != null) return made;
            int size = Cells * CellPx;
            var px = new Color[size * size];
            void Fill(int cell, Color c)
            {
                int cx = cell % Cells, cy = cell / Cells;
                for (int y = 0; y < CellPx; y++) for (int x = 0; x < CellPx; x++) px[(cy * CellPx + y) * size + cx * CellPx + x] = c;
            }
            for (int i = 0; i < Shirts.Length; i++) Fill(i, Shirts[i]);
            Fill(BrandCell, primary); Fill(BrandCell + 1, primary * 0.75f); Fill(BrandCell + 2, Color.Lerp(primary, Color.white, 0.3f));
            for (int i = 0; i < Skins.Length; i++) Fill(SkinCell + i, Skins[i]);
            for (int i = 0; i < Hairs.Length; i++) Fill(HairCell + i, Hairs[i]);
            for (int i = 0; i < Pants.Length; i++) Fill(PantsCell + i, Pants[i]);
            Fill(PhoneCell, new Color(0.9f, 0.95f, 1f));
            for (int i = 0; i < px.Length; i++) px[i].a = 1f;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Crowd palette" };
            tex.SetPixels(px);
            tex.Apply(false);
            _palettes[primary] = tex;
            return tex;
        }

        /// <summary>The crowd's material (kept with its palette for each brand colour; the stage does not own it).</summary>
        public static Material Material(Color primary)
        {
            if (_materials.TryGetValue(primary, out Material m) && m != null) return m;
            m = Make.Lit(new Color(0.62f, 0.6f, 0.58f), 0.18f, 0f, Palette(primary));
            m.SetTexture("_BaseMap", Palette(primary));
            _materials[primary] = m;
            return m;
        }

        static Vector2 Uv(int cell)
        {
            float size = Cells * CellPx;
            int cx = cell % Cells, cy = cell / Cells;
            return new Vector2((cx * CellPx + CellPx * 0.5f) / size, (cy * CellPx + CellPx * 0.5f) / size);
        }

        public static int Hash(int x)
        {
            unchecked
            {
                uint h = (uint)x * 2654435761u;
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return (int)(h & 0x7fffffff);
            }
        }

        // ── Shapes (made once) ──

        sealed class Shape { public Vector3[] V, N; public int[] T; }
        static Shape _box, _ball, _limb, _torso, _cap;

        static Shape From(Mesh m) { var s = new Shape { V = m.vertices, N = m.normals, T = m.triangles }; UnityEngine.Object.Destroy(m); return s; }

        // A unit box: 24 corners, flat faces (the rounded box is 384: too many for hundreds of people).
        static Shape Box
        {
            get
            {
                if (_box != null) return _box;
                var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
                Vector3[] axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
                foreach (Vector3 f in axes)
                {
                    Vector3 u = Mathf.Abs(f.y) > 0.5f ? Vector3.right : Vector3.up, w = Vector3.Cross(f, u);
                    int start = v.Count;
                    v.Add((f - u - w) * 0.5f); v.Add((f - u + w) * 0.5f); v.Add((f + u + w) * 0.5f); v.Add((f + u - w) * 0.5f);
                    for (int i = 0; i < 4; i++) n.Add(f);
                    t.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
                }
                return _box = new Shape { V = v.ToArray(), N = n.ToArray(), T = t.ToArray() };
            }
        }
        static Shape Ball
        {
            get
            {
                if (_ball != null) return _ball;
                var p = new List<Vector2>();
                for (int i = 0; i <= 6; i++) { float a = Mathf.PI * i / 6f; p.Add(new Vector2(Mathf.Max(0.001f, Mathf.Sin(a) * 0.5f), -Mathf.Cos(a) * 0.5f)); }
                return _ball = From(Make.Lathe(p, 10));
            }
        }
        // A limb: a tapered tube 1 long down -Y from the joint, rounded at both ends.
        static Shape Limb
        {
            get
            {
                if (_limb != null) return _limb;
                var p = new List<Vector2> { new Vector2(0.001f, -1.08f), new Vector2(0.36f, -1.02f), new Vector2(0.42f, -0.9f), new Vector2(0.5f, -0.1f), new Vector2(0.42f, 0.04f), new Vector2(0.001f, 0.1f) };
                return _limb = From(Make.Lathe(p, 8));
            }
        }
        // A torso: wider at the shoulders than the waist, round in section (scaled flatter front to back).
        static Shape Torso
        {
            get
            {
                if (_torso != null) return _torso;
                var p = new List<Vector2> { new Vector2(0.001f, 0f), new Vector2(0.4f, 0.02f), new Vector2(0.42f, 0.25f), new Vector2(0.45f, 0.62f), new Vector2(0.5f, 0.85f), new Vector2(0.38f, 0.98f), new Vector2(0.12f, 1.03f), new Vector2(0.001f, 1.04f) };
                return _torso = From(Make.Lathe(p, 12));
            }
        }
        // A cap of hair (or a hat): the top of a ball.
        static Shape Cap
        {
            get
            {
                if (_cap != null) return _cap;
                var p = new List<Vector2>();
                for (int i = 0; i <= 4; i++) { float a = Mathf.Lerp(0.42f, 1f, i / 4f) * Mathf.PI * 0.5f; p.Add(new Vector2(Mathf.Max(0.001f, Mathf.Cos(a) * 0.5f), Mathf.Sin(a) * 0.5f)); }
                return _cap = From(Make.Lathe(p, 10));
            }
        }

        /// <summary>Gathers people into meshes of at most 60,000 vertices.</summary>
        public sealed class Builder
        {
            readonly List<Vector3> _v = new List<Vector3>();
            readonly List<Vector3> _n = new List<Vector3>();
            readonly List<Vector2> _uv = new List<Vector2>();
            readonly List<int> _t = new List<int>();
            readonly List<Mesh> _done = new List<Mesh>();

            void Add(Shape s, Matrix4x4 m, int cell)
            {
                if (_v.Count + s.V.Length > 60000) Flush();
                int start = _v.Count;
                Vector2 uv = Uv(cell);
                Matrix4x4 normalM = m.inverse.transpose;
                for (int i = 0; i < s.V.Length; i++)
                {
                    _v.Add(m.MultiplyPoint3x4(s.V[i]));
                    _n.Add(normalM.MultiplyVector(s.N[i]).normalized);
                    _uv.Add(uv);
                }
                for (int i = 0; i < s.T.Length; i++) _t.Add(start + s.T[i]);
            }

            void Flush()
            {
                if (_v.Count == 0) return;
                var mesh = new Mesh { name = "People", indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(_v); mesh.SetNormals(_n); mesh.SetUVs(0, _uv); mesh.SetTriangles(_t, 0);
                mesh.RecalculateBounds();
                _done.Add(mesh);
                _v.Clear(); _n.Clear(); _uv.Clear(); _t.Clear();
            }

            public List<Mesh> Meshes() { Flush(); var all = new List<Mesh>(_done); _done.Clear(); return all; }

            /// <summary>A seated person: feet at `at` (the floor of their row), facing the court (turn's +Z is away from it).</summary>
            public void Seated(Vector3 at, Quaternion turn, int who)
            {
                int h = Hash(who);
                int R(int n) { h = Hash(h + 17); return h % n; }
                float F() { h = Hash(h + 29); return (h % 10000) / 10000f; }

                bool brand = R(100) < 9;
                int shirt = brand ? BrandCell + R(3) : R(Shirts.Length);
                int skin = SkinCell + R(Skins.Length), hair = HairCell + R(Hairs.Length), pants = PantsCell + R(Pants.Length);
                int pose = R(100);   // <60 lap, <82 clap, <86 arm up, <97 lean, else phone
                int style = R(100);  // <40 short, <62 long, <80 cap, else bald/very short
                float size = 0.92f + F() * 0.12f, wide = 0.9f + F() * 0.16f;   // adults, a little either side of average
                float yaw = (F() - 0.5f) * 26f, lean = pose >= 86 && pose < 97 ? 18f : (F() - 0.5f) * 8f;

                // The person's own frame: origin at the feet, facing -Z (the court).
                Matrix4x4 root = Matrix4x4.TRS(at, turn * Quaternion.Euler(0, yaw, 0), Vector3.one * size);
                Matrix4x4 M(Vector3 p, Quaternion r, Vector3 s) => root * Matrix4x4.TRS(p, r, s);

                const float seatY = 17f;
                // Thighs on the seat, toward the court, and the shins down to the floor (mostly hidden by the row in front).
                Add(Box, M(new Vector3(0, seatY + 2.5f, -6.5f), Quaternion.identity, new Vector3(13f * wide, 5.5f, 17f)), pants);
                Add(Box, M(new Vector3(0, seatY / 2f, -13.5f), Quaternion.identity, new Vector3(12f * wide, seatY, 5f)), pants);

                // The torso, from the seat, leaning.
                Quaternion lean0 = Quaternion.Euler(-lean, 0, 0);
                Vector3 hips = new Vector3(0, seatY + 4f, 1.5f);
                Add(Torso, M(hips, lean0, new Vector3(14.5f * wide, 20f, 9f)), shirt);
                Vector3 shoulders = hips + lean0 * new Vector3(0, 18f, 0);
                // Neck and head.
                Vector3 neck = shoulders + lean0 * new Vector3(0, 1.8f, 0);
                Add(Limb, M(neck + lean0 * new Vector3(0, 2.6f, 0), lean0, new Vector3(3.8f, 3f, 3.8f)), skin);
                Quaternion headTurn = lean0 * Quaternion.Euler((F() - 0.5f) * 14f, (F() - 0.5f) * 30f, 0);
                Vector3 head = neck + lean0 * new Vector3(0, 5.6f, 0);
                Add(Ball, M(head, headTurn, new Vector3(6.1f, 8.8f, 7.4f)), skin);
                if (style < 40) Add(Cap, M(head + headTurn * new Vector3(0, 0.5f, 0.4f), headTurn, new Vector3(6.6f, 9.2f, 8f)), hair);
                else if (style < 62)
                {
                    Add(Cap, M(head + headTurn * new Vector3(0, 0.4f, 0.5f), headTurn, new Vector3(6.9f, 9.4f, 8.2f)), hair);
                    Add(Box, M(head + headTurn * new Vector3(0, -3.8f, 2.6f), headTurn, new Vector3(6.2f, 8f, 2.2f)), hair);   // hair down the back
                }
                else if (style < 80)
                {
                    Add(Cap, M(head + headTurn * new Vector3(0, 1f, 0.3f), headTurn, new Vector3(7f, 8f, 8.2f)), shirt == BrandCell ? BrandCell + 1 : BrandCell);
                    Add(Box, M(head + headTurn * new Vector3(0, 2f, -4.2f), headTurn * Quaternion.Euler(-8f, 0, 0), new Vector3(5.8f, 0.5f, 3.4f)), shirt == BrandCell ? BrandCell + 1 : BrandCell);   // the peak
                }

                // The arms.
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 shoulder = shoulders + lean0 * new Vector3(side * 7.2f * wide, -1.2f, 0);
                    Quaternion upper, fore;
                    bool raised = (pose >= 82 && pose < 86 && side == 1) || (pose >= 97 && side == 1);
                    if (raised)
                    {
                        upper = Quaternion.Euler(-150f, 0, side * -12f);
                        fore = upper * Quaternion.Euler(-15f, 0, 0);
                    }
                    else if (pose >= 60 && pose < 82)   // clapping: hands together in front of the chest
                    {
                        upper = lean0 * Quaternion.Euler(-35f, 0, side * 10f);
                        fore = lean0 * Quaternion.Euler(-95f, side * 28f, 0);
                    }
                    else if (pose >= 86 && pose < 97)   // elbows on the knees
                    {
                        upper = lean0 * Quaternion.Euler(-20f, 0, side * 4f);
                        fore = lean0 * Quaternion.Euler(-80f, side * 14f, 0);
                    }
                    else   // hands in the lap
                    {
                        upper = lean0 * Quaternion.Euler(-12f, 0, side * 8f);
                        fore = lean0 * Quaternion.Euler(-72f, side * 10f, 0);
                    }
                    bool shortSleeve = R(2) == 0;
                    Add(Limb, M(shoulder, upper, new Vector3(3.6f, 11.5f, 3.6f)), shirt);
                    Vector3 elbow = shoulder + upper * new Vector3(0, -11.5f, 0);
                    Add(Limb, M(elbow, fore, new Vector3(2.8f, 10f, 2.8f)), shortSleeve ? skin : shirt);
                    Vector3 hand = elbow + fore * new Vector3(0, -10.4f, 0);
                    Add(Ball, M(hand, fore, new Vector3(2.4f, 3.4f, 1.6f)), skin);
                    if (raised && pose >= 97) Add(Box, M(hand + fore * new Vector3(0, -2.4f, 0), fore, new Vector3(2.8f, 5f, 0.5f)), PhoneCell);   // a phone held up
                }
            }
        }
    }
}

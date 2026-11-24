// Putt Star's arena: the stage (black deck, drape, ground-support truss, LED wall, flown PA, subs, wedges, drum
// riser, backline), the barricade, the floor and the stands. Everything that stands still is merged into one mesh
// per material (StarBatch), so the whole set is about fifteen draw calls. Stage coordinates: x across, y up,
// z away from the crowd; the deck front is z = 0 and its top is Deck.
using UnityEngine;
using UnityEngine.Rendering;

namespace PuttrArcade
{
    sealed class StarStage
    {
        public const float Deck = 14f;                    // height of the deck
        public const float RiserTop = Deck + 7.3f;        // drum riser
        public const float TrussY = Deck + 112f;          // the top of the rig
        public const float WallZ = 90.3f, WallW = 168f, WallH = 92f, WallY = Deck + 52f;
        public const float MeterW = 132f, MeterH = 8f, MeterY = Deck + 40f, ShoutY = Deck + 66f;

        // Where the lamps are (used by the mode to place the beams).
        public static readonly float[] FanX = { -72f, -56f, -40f, -24f, -8f, 8f, 24f, 40f, 56f, 72f };
        public static readonly float[] FrontX = { -70f, -42f, -14f, 14f, 42f, 70f };
        public static readonly float[] BackX = { -75f, -45f, -15f, 15f, 45f, 75f };
        public static readonly float[] PyroX = { -80f, -48f, -16f, 16f, 48f, 80f };
        public const float FanZ = 84f, FanY = Deck + 2.5f, FrontZ = 4f, FrontY = TrussY - 7f, BackZ = 92f, BackY = TrussY - 7f;
        public static readonly Vector3[] Blinders =
        {
            new Vector3(-91f, TrussY - 6f, 0.5f), new Vector3(-85f, TrussY - 6f, 0.5f), new Vector3(-30f, TrussY - 6f, 0.5f), new Vector3(-24f, TrussY - 6f, 0.5f),
            new Vector3(24f, TrussY - 6f, 0.5f), new Vector3(30f, TrussY - 6f, 0.5f), new Vector3(85f, TrussY - 6f, 0.5f), new Vector3(91f, TrussY - 6f, 0.5f),
        };

        public Material Strip;                 // the LED strips on the deck lip and the riser (colour set by the show)
        public Material Wall, MeterTrack, MeterFill, Grid, HazeBack, HazeFront;
        public Transform MeterBar;
        public TextMesh Shout;
        public Texture2D Atlas;

        public static StarStage Build(Transform root, StarCrowd crowd)
        {
            var s = new StarStage();
            var b = new StarBatch();
            const float S = Deck;

            // ── Materials ──
            StarKit.Deck(out Texture2D deckTex, out Texture2D deckNormal);
            Material deck = Named(Make.Lit(Color.white, 0.48f, 0f, deckTex, deckNormal, 0.5f), "Deck");
            Material floor = Named(Make.Lit(new Color(0.16f, 0.16f, 0.17f), 0.3f, 0f, StarKit.Concrete()), "Concrete");
            StarKit.Drape(out Texture2D drapeTex, out Texture2D drapeNormal);
            Material drape = Named(Make.Lit(new Color(0.06f, 0.06f, 0.07f), 0.12f, 0f, drapeTex, drapeNormal, 1f), "Drape");
            Material truss = Named(Make.Lit(new Color(0.66f, 0.67f, 0.7f), 0.66f, 0.92f), "Truss");
            Material black = Named(Make.Lit(new Color(0.032f, 0.032f, 0.036f), 0.42f, 0.15f), "Black");
            StarKit.Grille(out Texture2D grilleTex, out Texture2D grilleNormal);
            Material grille = Named(Make.Lit(new Color(0.22f, 0.22f, 0.24f), 0.5f, 0.55f, grilleTex, grilleNormal, 0.8f), "Grille");
            StarKit.Tolex(out Texture2D tolexTex, out Texture2D tolexNormal);
            Material tolex = Named(Make.Lit(new Color(0.11f, 0.11f, 0.11f), 0.5f, 0f, tolexTex, tolexNormal, 0.8f), "Tolex");
            Material weave = Named(Make.Lit(new Color(0.55f, 0.52f, 0.45f), 0.2f, 0f, StarKit.Weave()), "Weave");
            Material chrome = Named(Make.Lit(new Color(0.86f, 0.87f, 0.9f), 0.92f, 1f), "Chrome");
            Material brass = Named(Make.Lit(new Color(0.86f, 0.63f, 0.3f), 0.82f, 1f), "Brass");
            Material wood = Named(Make.Lit(new Color(0.5f, 0.3f, 0.18f), 0.6f, 0f, Make.Wood(19)), "Wood");
            Material head = Named(Make.Lit(new Color(0.9f, 0.88f, 0.83f), 0.3f), "Heads");
            Material steel = Named(Make.Lit(new Color(0.28f, 0.29f, 0.31f), 0.5f, 0.75f), "Steel");
            s.Strip = Named(Make.Emissive(Color.black, new Color(1f, 0.3f, 0.8f) * 2f), "Strip");
            Material exit = Named(Make.Emissive(Color.black, new Color(0.2f, 1f, 0.35f) * 1.6f), "Exit");
            Material aisle = Named(Make.Emissive(Color.black, new Color(1f, 0.7f, 0.4f) * 1.2f), "Aisle");

            // ── Arena ──
            b.Box(floor, new Vector3(0, -1f, -150f), new Vector3(1400f, 2f, 900f), 1f / 90f, false);
            b.Box(drape, new Vector3(0, 130f, 101f), new Vector3(620f, 260f, 1f), 1f / 60f, false);
            for (int side = -1; side <= 1; side += 2)
            {
                // The stands: concrete tiers rising away from the floor, full of people.
                for (int k = 0; k < 10; k++)
                {
                    float x = side * (170f + k * 13f), top = 5f + k * 7.5f;
                    b.Box(floor, new Vector3(x, top / 2f, -40f), new Vector3(13f, top, 230f), 1f / 90f, false);
                    for (float z = -150f; z < 74f; z += 9f + (k % 3))
                    {
                        float jitter = Mathf.Sin(z * 1.7f + k * 3.1f) * 2.5f;
                        if (((int)(z / 9f) + k) % 6 == 0) continue; // an empty seat or an aisle
                        Vector3 feet = new Vector3(x - side * 2f + jitter * 0.3f, top, z + jitter);
                        if (((int)(z * 7f) + k * 13) % 7 == 0) crowd.AddWithPhone(feet, 0.92f);
                        else crowd.Add(feet, 0.92f, false);
                    }
                    if (k % 3 == 1) for (float z = -140f; z < 70f; z += 46f) b.Box(aisle, new Vector3(x - side * 6.3f, top - 0.6f, z), new Vector3(0.3f, 0.5f, 1.4f), 0f, false);
                }
                b.Box(exit, new Vector3(side * 236f, 110f, 60f), new Vector3(8f, 3f, 0.6f), 0f, false);
                b.Box(exit, new Vector3(side * 236f, 110f, -120f), new Vector3(8f, 3f, 0.6f), 0f, false);
            }

            // ── The floor crowd ──
            var rng = new System.Random(17);
            float R() => (float)rng.NextDouble();
            for (float z = -15f; z > -97f; z -= 5f)
            {
                float rowShift = R() * 6f;
                float half = 150f + (-z) * 0.2f;
                for (float x = -half + rowShift; x < half; x += 6.3f + R() * 1.4f)
                {
                    var feet = new Vector3(x + (R() - 0.5f) * 2f, 0f, z + (R() - 0.5f) * 2.4f);
                    float scale = 0.9f + R() * 0.2f;
                    if (R() < 0.12f) crowd.AddWithPhone(feet, scale);
                    else crowd.Add(feet, scale, false);
                }
            }

            // ── The barricade ──
            b.Box(steel, new Vector3(0, 0.3f, -10f), new Vector3(300f, 0.6f, 7f), 0f, true);
            b.Box(steel, new Vector3(0, 6f, -13f), new Vector3(300f, 11f, 0.5f), 0f, true);
            b.Tube(steel, new Vector3(-150f, 11.8f, -13f), new Vector3(150f, 11.8f, -13f), 0.6f, true, StarKit.RoundTube);
            for (float x = -150f; x <= 150f; x += 20f) b.Box(steel, new Vector3(x, 6f, -12.2f), new Vector3(0.8f, 12f, 1.6f));

            // ── The stage ──
            b.Box(deck, new Vector3(0, S / 2f, 46f), new Vector3(196f, S, 92f), 1f / 64f);
            b.Box(drape, new Vector3(0, S / 2f, -0.35f), new Vector3(197f, S, 0.7f), 1f / 40f, false);
            b.Box(black, new Vector3(0, S - 0.2f, -0.2f), new Vector3(197f, 0.6f, 1.2f));
            var strips = new StarBatch();
            strips.Box(s.Strip, new Vector3(0, S - 1.2f, -0.82f), new Vector3(196f, 0.5f, 0.3f), 0f, false);
            strips.Box(s.Strip, new Vector3(0, RiserTop - 0.9f, 51.85f), new Vector3(56f, 0.5f, 0.3f), 0f, false);

            // Ground-support towers, the grid on top, and the lamps hung from it.
            const float tx = 102f, ts = 7f;
            foreach (float x in new[] { -tx, tx })
                foreach (float z in new[] { FrontZ, BackZ })
                {
                    StarKit.Truss(b, truss, new Vector3(x, S, z), new Vector3(x, TrussY + ts * 0.5f, z), ts, Vector3.forward);
                    b.Box(black, new Vector3(x, S + 0.5f, z), new Vector3(13f, 1f, 13f));
                    b.Box(truss, new Vector3(x, TrussY, z), new Vector3(ts + 1.2f, ts + 1.2f, ts + 1.2f));
                }
            StarKit.Truss(b, truss, new Vector3(-tx, TrussY, FrontZ), new Vector3(tx, TrussY, FrontZ), ts, Vector3.up);
            StarKit.Truss(b, truss, new Vector3(-tx, TrussY, BackZ), new Vector3(tx, TrussY, BackZ), ts, Vector3.up);
            StarKit.Truss(b, truss, new Vector3(-tx, TrussY, 48f), new Vector3(tx, TrussY, 48f), ts, Vector3.up);
            foreach (float x in new[] { -tx, tx }) StarKit.Truss(b, truss, new Vector3(x, TrussY, FrontZ), new Vector3(x, TrussY, BackZ), ts, Vector3.up);
            // Chain hoists up into the dark.
            foreach (float x in new[] { -tx, -34f, 34f, tx })
                foreach (float z in new[] { FrontZ, BackZ })
                    b.Tube(black, new Vector3(x, TrussY + 4f, z), new Vector3(x, TrussY + 120f, z), 0.25f, false);
            foreach (float x in FrontX) Fixture(b, black, new Vector3(x, FrontY, FrontZ), true);
            foreach (float x in BackX) Fixture(b, black, new Vector3(x, BackY, BackZ), true);
            foreach (float x in new[] { -tx, tx }) Fixture(b, black, new Vector3(x, TrussY - 7f, FrontZ - 5f), true);
            foreach (float x in FanX) Fixture(b, black, new Vector3(x, FanY, FanZ), false);
            for (int i = 0; i < Blinders.Length; i += 2)
                b.Box(black, (Blinders[i] + Blinders[i + 1]) * 0.5f + new Vector3(0, 0, 1.6f), new Vector3(13f, 5.5f, 2.6f));

            // The LED wall: black panels; the picture is laid on by the mode.
            b.Box(black, new Vector3(0, WallY, WallZ + 1.2f), new Vector3(WallW + 4f, WallH + 4f, 2f));

            // Flown PA: a curved line array each side, and subs on the floor.
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 at = new Vector3(side * 120f, TrussY - 4f, -4f);
                b.Box(black, at + new Vector3(0, 1.2f, 0), new Vector3(23f, 1.4f, 15f));
                b.Tube(black, at + new Vector3(-8f, 2f, 0), at + new Vector3(-8f, 130f, 0), 0.3f, false);
                b.Tube(black, at + new Vector3(8f, 2f, 0), at + new Vector3(8f, 130f, 0), 0.3f, false);
                float tilt = 0f;
                for (int i = 0; i < 10; i++)
                {
                    tilt += i * 0.75f;
                    Quaternion q = Quaternion.Euler(-tilt, 0, 0);
                    Vector3 c = at + q * new Vector3(0, -3f, 0);
                    b.Add(StarKit.Rounded(new Vector3(21f, 5.2f, 13f), 0.5f), Matrix4x4.TRS(c, q, Vector3.one), black);
                    b.Box(grille, c + q * new Vector3(0, 0, -6.55f), new Vector3(19.6f, 4.4f, 0.2f), q, 0.06f);
                    at = c + q * new Vector3(0, -2.75f, 0);
                }
                for (int k = 0; k < 2; k++)
                {
                    Vector3 c = new Vector3(side * 118f, 5.5f + k * 11f, -3f);
                    b.Add(StarKit.Rounded(new Vector3(22f, 10.6f, 16f), 0.6f), Matrix4x4.Translate(c), black);
                    b.Box(grille, c + new Vector3(0, 0, -8.05f), new Vector3(20.4f, 9.2f, 0.2f), 0.06f);
                }
            }

            // Wedge monitors along the front, angled up at the band.
            foreach (float x in new[] { -40f, -7f, 7f, 40f })
            {
                Quaternion q = Quaternion.Euler(38f, x < -20f ? 12f : x > 20f ? -12f : 0f, 0);
                Vector3 c = new Vector3(x, S + 2.2f, 4.5f);
                b.Add(StarKit.Rounded(new Vector3(12f, 6f, 8.5f), 0.6f), Matrix4x4.TRS(c, q, Vector3.one), black);
                b.Box(grille, c + q * new Vector3(0, 3.02f, 0.3f), new Vector3(10.6f, 0.2f, 7f), q, 0.06f);
            }
            // Set lists taped down, and cable runs.
            b.Box(head, new Vector3(-12f, S + 0.05f, 4f), new Vector3(2.6f, 0.1f, 3.6f), Quaternion.Euler(0, 14f, 0), 0f, false);
            b.Box(head, new Vector3(-46f, S + 0.05f, 9f), new Vector3(2.6f, 0.1f, 3.6f), Quaternion.Euler(0, -9f, 0), 0f, false);
            Cable(b, black, new[] { new Vector3(-7f, S + 0.3f, 6f), new Vector3(-10f, S + 0.3f, 16f), new Vector3(-6f, S + 0.3f, 34f), new Vector3(-14f, S + 0.3f, 52f) });
            Cable(b, black, new[] { new Vector3(-40f, S + 0.3f, 7f), new Vector3(-44f, S + 0.3f, 22f), new Vector3(-52f, S + 0.3f, 40f), new Vector3(-50f, S + 0.3f, 74f) });
            Cable(b, black, new[] { new Vector3(40f, S + 0.3f, 7f), new Vector3(46f, S + 0.3f, 24f), new Vector3(50f, S + 0.3f, 46f), new Vector3(48f, S + 0.3f, 74f) });
            Cable(b, black, new[] { new Vector3(5.5f, S + 0.3f, 7f), new Vector3(12f, S + 0.3f, 14f), new Vector3(16f, S + 0.3f, 40f), new Vector3(14f, S + 0.3f, 52f) });

            // The mic stand (the singer's mic is in his hand).
            Vector3 stand = new Vector3(6.5f, S, 9f);
            b.Drum(black, stand, Vector3.up, 2.2f, 0.45f);
            b.Tube(chrome, stand, stand + new Vector3(0, 16f, 0), 0.22f, true, StarKit.RoundTube);
            b.Drum(black, stand + new Vector3(0, 15.6f, 0), Vector3.up, 0.42f, 1.1f);
            b.Tube(chrome, stand + new Vector3(0, 16f, 0), stand + new Vector3(-2.2f, 19.6f, -1.5f), 0.16f, true, StarKit.RoundTube);
            b.Box(black, stand + new Vector3(-2.3f, 19.8f, -1.6f), new Vector3(0.6f, 0.9f, 0.6f));

            // The drum riser and the kit on it.
            b.Box(drape, new Vector3(0, S + 3.5f, 66f), new Vector3(56f, 7f, 28f), 1f / 40f);
            b.Box(wood, new Vector3(0, RiserTop - 0.15f, 66f), new Vector3(56f, 0.3f, 28f), 1f / 40f);
            Kit(b, wood, head, chrome, brass, black, RiserTop);

            // The backline: guitar stacks stage left, bass rig stage right.
            foreach (float x in new[] { -46f, -62f }) Stack(b, tolex, weave, brass, black, new Vector3(x, S, 80f), 2, 13f);
            Stack(b, tolex, weave, brass, black, new Vector3(48f, S, 80f), 1, 24f);
            Stack(b, tolex, weave, brass, black, new Vector3(64f, S, 80f), 2, 13f);

            // Pyro and CO2 heads along the lip.
            foreach (float x in PyroX) b.Box(black, new Vector3(x, S + 0.9f, 2.6f), new Vector3(3.4f, 1.8f, 3.4f));

            b.Build(root, "Set");
            strips.Build(root, "Strips");

            // The Puttr mark on the kick drum's front head.
            var mark = Brand.Mark;
            if (mark != null)
            {
                Material logo = Make.Glow(new Color(0.9f, 0.9f, 0.9f, 0.95f), false, mark);
                Layer(root, "Kick logo", new Vector3(0, RiserTop + 5.6f, 56.75f), Brand.Fit(mark, 7.4f, 7.4f), logo, 3000);
            }

            // ── The LED wall's picture ──
            s.Atlas = StarKit.LightAtlas();
            float wz = WallZ;
            s.Wall = Make.Glow(new Color(1f, 0.3f, 0.8f), true, StarKit.Visual());
            s.Wall.name = "Wall";
            Layer(root, "Wall picture", new Vector3(0, WallY, wz), new Vector2(WallW, WallH), s.Wall, 3000);
            Texture2D meter = StarKit.Meter();
            s.MeterTrack = Make.Glow(new Color(1f, 1f, 1f, 0.16f), true, meter);
            Layer(root, "Meter track", new Vector3(0, MeterY, wz - 0.1f), new Vector2(MeterW, MeterH), s.MeterTrack, 3000);
            s.MeterFill = Make.Glow(Color.white * 1.6f, true, meter);
            s.MeterBar = Layer(root, "Meter", new Vector3(0, MeterY, wz - 0.2f), new Vector2(MeterW, MeterH), s.MeterFill, 3000).transform;
            s.Grid = Make.Glow(Color.white, false, StarKit.LedGrid());
            s.Grid.mainTextureScale = new Vector2(WallW / 0.75f, WallH / 0.75f);
            Layer(root, "Wall pixels", new Vector3(0, WallY, wz - 0.4f), new Vector2(WallW, WallH), s.Grid, 3001);
            s.Shout = Words(root, "", new Vector3(0, ShoutY, wz - 0.3f), 13f, Color.white);

            // Haze: soft smoke hanging in the light, behind the band and over the front.
            Texture2D haze = StarKit.Haze();
            s.HazeBack = Make.Glow(new Color(1f, 1f, 1f, 0.07f), true, haze);
            s.HazeBack.mainTextureScale = new Vector2(2.5f, 1.2f);
            Layer(root, "Haze back", new Vector3(0, Deck + 62f, 72f), new Vector2(300f, 150f), s.HazeBack, 3011);
            s.HazeFront = Make.Glow(new Color(1f, 1f, 1f, 0.05f), true, haze);
            s.HazeFront.mainTextureScale = new Vector2(3f, 1f);
            Layer(root, "Haze front", new Vector3(0, Deck + 50f, 2f), new Vector2(340f, 130f), s.HazeFront, 3011);
            return s;
        }

        static Material Named(Material m, string name) { m.name = name; return m; }

        static GameObject Layer(Transform root, string name, Vector3 at, Vector2 size, Material m, int queue)
        {
            m.renderQueue = queue;
            GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            Make.Kill(q.GetComponent<Collider>());
            q.transform.SetParent(root, false);
            q.transform.localPosition = at;
            q.transform.localScale = new Vector3(size.x, size.y, 1f);
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return q;
        }

        static Font _font;
        static TextMesh Words(Transform parent, string text, Vector3 at, float height, Color color)
        {
            if (_font == null) _font = Resources.Load<Font>("PuttrArcade/Fonts/Montserrat-Bold");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var go = new GameObject("Words");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            var words = go.AddComponent<TextMesh>();
            words.font = _font;
            words.fontSize = 128;
            words.characterSize = height * 10f / 128f * 1.4f;
            words.anchor = TextAnchor.MiddleCenter;
            words.alignment = TextAlignment.Center;
            words.color = color;
            words.text = text;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = _font.material;
            r.sortingOrder = 10;
            return words;
        }

        /// <summary>A moving head (hung under the truss, or standing on the deck): yoke and head, lens at at.</summary>
        static void Fixture(StarBatch b, Material black, Vector3 at, bool hung)
        {
            float d = hung ? 1f : -1f;
            b.Box(black, at + new Vector3(0, d * 4.6f, 0), new Vector3(5f, 1.6f, 4f));
            b.Box(black, at + new Vector3(-2.3f, d * 2.4f, 0), new Vector3(0.6f, 3.6f, 1.6f));
            b.Box(black, at + new Vector3(2.3f, d * 2.4f, 0), new Vector3(0.6f, 3.6f, 1.6f));
            b.Box(black, at + new Vector3(0, d * 1.4f, 0), new Vector3(3.6f, 3.4f, 3.6f));
        }

        static void Cable(StarBatch b, Material black, Vector3[] path)
        {
            for (int i = 0; i < path.Length - 1; i++)
            {
                Vector3 p0 = path[Mathf.Max(0, i - 1)], p1 = path[i], p2 = path[i + 1], p3 = path[Mathf.Min(path.Length - 1, i + 2)];
                Vector3 prev = p1;
                for (int k = 1; k <= 4; k++)
                {
                    float t = k / 4f;
                    Vector3 p = 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t);
                    b.Tube(black, prev, p, 0.28f, false);
                    prev = p;
                }
            }
        }

        static void Stack(StarBatch b, Material tolex, Material weave, Material brass, Material black, Vector3 foot, int cabs, float cabH)
        {
            const float w = 15f, d = 9.5f;
            float y = foot.y;
            for (int i = 0; i < cabs; i++)
            {
                Vector3 c = new Vector3(foot.x, y + cabH / 2f, foot.z);
                b.Add(StarKit.Rounded(new Vector3(w, cabH, d), 0.7f), Matrix4x4.Translate(c), tolex);
                b.Box(weave, c + new Vector3(0, 0, -d / 2f - 0.05f), new Vector3(w - 1.8f, cabH - 1.8f, 0.2f));
                b.Box(brass, c + new Vector3(0, -cabH / 2f + 1.2f, -d / 2f - 0.12f), new Vector3(2.2f, 0.7f, 0.1f));
                y += cabH;
            }
            Vector3 h = new Vector3(foot.x, y + 2.9f, foot.z + 0.4f);
            b.Add(StarKit.Rounded(new Vector3(w, 5.8f, d - 1f), 0.6f), Matrix4x4.Translate(h), tolex);
            b.Box(brass, h + new Vector3(0, -0.5f, -(d - 1f) / 2f - 0.05f), new Vector3(w - 2f, 2.2f, 0.15f));
            for (int k = 0; k < 6; k++) b.Drum(black, h + new Vector3(-5f + k * 2f, -0.5f, -(d - 1f) / 2f - 0.15f), Vector3.back, 0.35f, 0.5f);
        }

        static void Kit(StarBatch b, Material wood, Material head, Material chrome, Material brass, Material black, float T)
        {
            // Kick drum, front head to the crowd.
            Vector3 kick = new Vector3(0, T + 5.6f, 57f);
            b.Drum(wood, kick, Vector3.forward, 5.6f, 7f);
            b.Drum(black, kick + new Vector3(0, 0, -0.08f), Vector3.forward, 5.45f, 0.1f);
            b.Tube(chrome, kick + new Vector3(0, 0, -0.3f), kick + new Vector3(0, 0, 0.5f), 5.75f, true, StarKit.RoundTube);
            b.Tube(chrome, kick + new Vector3(0, 0, 6.6f), kick + new Vector3(0, 0, 7.3f), 5.75f, true, StarKit.RoundTube);
            for (int s = -1; s <= 1; s += 2) b.Tube(chrome, kick + new Vector3(s * 4.2f, -3f, 1.5f), new Vector3(s * 6.5f, T, 56.5f), 0.18f, true, StarKit.RoundTube); // spurs
            // Rack toms, tilted to the drummer.
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 axis = new Vector3(s * 0.18f, 1f, 0.5f).normalized;
                Vector3 foot = new Vector3(s * 4.4f, T + 11.3f, 59.5f);
                b.Drum(wood, foot, axis, 3.3f, 3.8f);
                b.Drum(head, foot + axis * 3.8f, axis, 3.2f, 0.1f);
                b.Tube(chrome, foot + axis * 3.6f, foot + axis * 4.0f, 3.38f, true, StarKit.RoundTube);
            }
            // Floor tom and snare.
            Vector3 floorTom = new Vector3(12f, T + 4.5f, 64f);
            b.Drum(wood, floorTom, Vector3.up, 4.4f, 6.3f);
            b.Drum(head, floorTom + Vector3.up * 6.3f, Vector3.up, 4.3f, 0.1f);
            for (int k = 0; k < 3; k++) { float a = k * 2.1f; b.Tube(chrome, floorTom + new Vector3(Mathf.Cos(a) * 4.5f, 4f, Mathf.Sin(a) * 4.5f), new Vector3(12f + Mathf.Cos(a) * 5.4f, T, 64f + Mathf.Sin(a) * 5.4f), 0.16f, true, StarKit.RoundTube); }
            Vector3 snare = new Vector3(-7.5f, T + 8.8f, 63f);
            b.Drum(chrome, snare, Vector3.up, 3.5f, 2.3f);
            b.Drum(head, snare + Vector3.up * 2.3f, Vector3.up, 3.4f, 0.1f);
            b.Tube(chrome, snare, new Vector3(-7.5f, T, 63f), 0.2f, true, StarKit.RoundTube);
            // Cymbals on their stands.
            Cymbal(b, brass, chrome, new Vector3(-13.5f, T, 64f), 13.5f, 3.6f, 0f, true);
            Cymbal(b, brass, chrome, new Vector3(-12f, T, 57f), 20f, 4.9f, 14f, false);
            Cymbal(b, brass, chrome, new Vector3(11f, T, 56.5f), 21f, 5.1f, -14f, false);
            Cymbal(b, brass, chrome, new Vector3(16f, T, 62f), 16.5f, 5.8f, -10f, false);
            // The throne.
            b.Drum(black, new Vector3(0, T, 70f), Vector3.up, 0.5f, 6.6f);
            b.Drum(black, new Vector3(0, T + 6.6f, 70f), Vector3.up, 2.4f, 1.2f);
        }

        static void Cymbal(StarBatch b, Material brass, Material chrome, Vector3 foot, float height, float radius, float tilt, bool hats)
        {
            Vector3 top = foot + Vector3.up * height;
            b.Tube(chrome, foot, top, 0.18f, true, StarKit.RoundTube);
            for (int k = 0; k < 3; k++) { float a = k * 2.1f + 0.4f; b.Tube(chrome, foot + Vector3.up * 2.5f, foot + new Vector3(Mathf.Cos(a) * 3.4f, 0, Mathf.Sin(a) * 3.4f), 0.14f, true, StarKit.RoundTube); }
            Vector3 axis = Quaternion.Euler(-12f, 0, tilt) * Vector3.up;
            b.Add(StarKit.CylinderMesh, Matrix4x4.TRS(top, Quaternion.FromToRotation(Vector3.up, axis), new Vector3(radius, 0.12f, radius)), brass);
            b.Add(StarKit.Ball, Matrix4x4.TRS(top + axis * 0.1f, Quaternion.FromToRotation(Vector3.up, axis), new Vector3(1.4f, 0.5f, 1.4f)), brass);
            if (hats) b.Add(StarKit.CylinderMesh, Matrix4x4.TRS(top - Vector3.up * 0.5f, Quaternion.identity, new Vector3(radius, 0.12f, radius)), brass);
        }
    }
}

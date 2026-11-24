// Venue Mode's room (Matt, 2026-11-21): in place of the arena, the Puttr set up in a high-end cocktail lounge. A
// walnut floor and a dark rug under the Puttr, slatted walnut walls with a warm cove light, the host's logo lit on the
// back wall, a marble-topped bar on the right with backlit shelves of bottles and brass stools, a lounge on the left
// (velvet sofas, cognac leather club chairs, a marble coffee table), brass pendants and floor lamps, tall plants.
// Low, warm light: the Puttr is the brightest thing in the room. Same scene inches as the arena (BrandStage): the hole
// at the origin, the Player's end of the mat at z -139, the floor at y 0, so the home screen's camera move is shared.
using System.Collections.Generic;
using PuttrArcade;
using UnityEngine;

namespace PuttrApp
{
    public static class LoungeStage
    {
        const float Back = 30f, Side = 290f, Ceiling = 300f, Front = -620f;   // the back wall right behind the Puttr's box

        public static void Build(Live3D.Stage stage)
        {
            Color primary = Brand.Primary;
            int before = stage.Root.childCount;
            Arena.BuildMat(stage, out Material ring, false, new Color(0.62f, 1.08f, 0.78f));
            stage.Background = new Color32(6, 4, 3, 255);
            foreach (Light l in stage.Lights) l.intensity *= 0.4f;
            ring.SetColor("_Color", new Color(0f, 0f, 0f, 0f));
            var puttr = new List<Transform>();
            for (int i = before; i < stage.Root.childCount; i++) puttr.Add(stage.Root.GetChild(i));

            Room(stage, primary);
            Tv(stage);
            LogoTv(stage, primary);
            Bar(stage);
            Lounge(stage);
            Lamps(stage);
            Plants(stage);
            Lights(stage);

            GameObject ball = stage.Primitive(PrimitiveType.Sphere, "Ball", stage.Lit(Color.white, 0.8f));
            ball.transform.localPosition = Arena.Spot(4, 2, LaneSpec.BallRadius);
            ball.transform.localScale = Vector3.one * LaneSpec.BallRadius * 2f;
            int boxFrom = stage.Root.childCount;
            BrandStage.BoxLight(stage);
            for (int i = boxFrom; i < stage.Root.childCount; i++) puttr.Add(stage.Root.GetChild(i));
            puttr.Add(ball.transform);
            ShowPiece(stage, puttr);

            stage.Tick = dt => false;
            stage.Camera.fieldOfView = 48f;
            stage.Camera.farClipPlane = 4000f;
            stage.Look(BrandStage.LoungeEye, BrandStage.LoungeTarget);
        }

        // The Puttr is the show piece (Matt: "the puttr is too small, the bar stool looks the same width"): the mat, its
        // box, the ball and the box light are scaled up together about the hole, so the mat stays on the rug and the box
        // against the wall. Spot and point lights reach and shine to match.
        const float PuttrScale = 1.4f;

        static void ShowPiece(Live3D.Stage stage, List<Transform> parts)
        {
            Transform set = stage.Group("Puttr set");
            set.localScale = Vector3.one * PuttrScale;
            foreach (Transform t in parts)
            {
                t.SetParent(set, false);
                foreach (Light l in t.GetComponentsInChildren<Light>(true))
                    if (l.type != LightType.Directional) { l.range *= PuttrScale; l.intensity *= PuttrScale * PuttrScale; }
            }
        }

        static Material Textured(Live3D.Stage stage, Color colour, float smooth, Texture2D tex, Vector2 tile)
        {
            Material m = stage.Own(Make.Lit(colour, smooth, 0f, tex));
            m.SetTextureScale("_BaseMap", tile);
            return m;
        }

        static GameObject Cyl(Live3D.Stage stage, string name, Vector3 at, float radius, float height, Material m)
        {
            GameObject go = stage.Primitive(PrimitiveType.Cylinder, name, m);
            go.transform.localPosition = at;
            go.transform.localScale = new Vector3(radius * 2f, height / 2f, radius * 2f);
            return go;
        }

        static GameObject Ball(Live3D.Stage stage, string name, Vector3 at, Vector3 size, Material m)
        {
            GameObject go = stage.Primitive(PrimitiveType.Sphere, name, m);
            go.transform.localPosition = at;
            go.transform.localScale = size;
            return go;
        }

        // ── The room ─────────────────────────────────────────────────────────

        static void Room(Live3D.Stage stage, Color primary)
        {
            // Walnut floor, a deep charcoal rug under the Puttr with a brass inlay round it.
            Material floor = Textured(stage, new Color(0.2f, 0.12f, 0.07f), 0.5f, Make.Wood(5), new Vector2(6f, 10f));
            GameObject f = stage.Primitive(PrimitiveType.Quad, "Floor", floor);
            f.transform.localPosition = new Vector3(0, 0f, (Back + Front) / 2f);
            f.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            f.transform.localScale = new Vector3(Side * 2f, Back - Front, 1f);
            Material brass = stage.Lit(new Color(0.78f, 0.56f, 0.28f), 0.85f, 0.95f);
            stage.Box("Rug inlay", new Vector3(0, 0.08f, -100f), new Vector3(134f, 0.1f, 264f), brass);
            stage.Box("Rug", new Vector3(0, 0.15f, -100f), new Vector3(130f, 0.2f, 260f), stage.Lit(new Color(0.035f, 0.04f, 0.05f), 0.08f));

            // Walls: dark plaster behind vertical walnut slats, a warm cove light along the top and a glow at the foot.
            Material plaster = stage.Lit(new Color(0.05f, 0.035f, 0.03f), 0.2f);
            Material slat = Textured(stage, new Color(0.26f, 0.15f, 0.08f), 0.5f, Make.Wood(9), new Vector2(0.3f, 2f));
            stage.Box("Back wall", new Vector3(0, Ceiling / 2f, Back + 4f), new Vector3(Side * 2f, Ceiling, 4f), plaster);
            stage.Box("Left wall", new Vector3(-Side - 4f, Ceiling / 2f, (Back + Front) / 2f), new Vector3(4f, Ceiling, Back - Front), plaster);
            stage.Box("Right wall", new Vector3(Side + 4f, Ceiling / 2f, (Back + Front) / 2f), new Vector3(4f, Ceiling, Back - Front), plaster);
            stage.Box("Ceiling", new Vector3(0, Ceiling + 2f, (Back + Front) / 2f), new Vector3(Side * 2f + 10f, 4f, Back - Front), stage.Lit(new Color(0.02f, 0.016f, 0.014f), 0.3f));
            var slats = new List<(Vector3, Vector3, Quaternion)>();
            for (float x = -Side + 6f; x < Side; x += 11f) slats.Add((new Vector3(x, Ceiling / 2f, Back), new Vector3(5f, Ceiling, 3f), Quaternion.identity));
            for (float z = Front + 6f; z < Back; z += 11f)
            {
                slats.Add((new Vector3(-Side, Ceiling / 2f, z), new Vector3(3f, Ceiling, 5f), Quaternion.identity));
                slats.Add((new Vector3(Side, Ceiling / 2f, z), new Vector3(3f, Ceiling, 5f), Quaternion.identity));
            }
            stage.Boxes("Wall slats", slats, slat);
            Material cove = stage.Emissive(new Color(1f, 0.72f, 0.4f), new Color(1f, 0.62f, 0.3f) * 2.2f);
            stage.Box("Cove", new Vector3(0, Ceiling - 6f, Back - 3f), new Vector3(Side * 2f, 1.2f, 1.2f), cove);
            stage.Box("Cove left", new Vector3(-Side + 3f, Ceiling - 6f, (Back + Front) / 2f), new Vector3(1.2f, 1.2f, Back - Front), cove);
            stage.Box("Cove right", new Vector3(Side - 3f, Ceiling - 6f, (Back + Front) / 2f), new Vector3(1.2f, 1.2f, Back - Front), cove);
            Material foot = stage.Emissive(new Color(1f, 0.7f, 0.4f), new Color(1f, 0.6f, 0.3f) * 1.2f);
            stage.Box("Foot glow", new Vector3(0, 2f, Back - 3f), new Vector3(Side * 2f, 0.8f, 0.8f), foot);

            // The host's logo is on the second TV (LogoTv), not on the wall (Matt, 2026-11-21).
        }

        // ── The TV on the back wall: a game of On Fire under way (Matt, 2026-11-21) ──

        // The back wall's three sets (Matt, 2026-11-21): the Puttr logo on the main 75-inch set centred right above the
        // Puttr's box, the host's logo on a 55-inch set either side of it.
        static void Tv(Live3D.Stage stage)
        {
            Texture2D puttr = Resources.Load<Texture2D>("PuttrArcade/LogoHeader");
            LogoScreen(stage, "TV", 0f, 66f, puttr, new Color(0.45f, 1f, 0.55f, 0.12f));
        }

        static void LogoTv(Live3D.Stage stage, Color primary)
        {
            Color spill = new Color(primary.r, primary.g, primary.b, 0.08f);
            LogoScreen(stage, "Logo TV left", -76f, 48f, Brand.Logo, spill);
            LogoScreen(stage, "Logo TV right", 76f, 48f, Brand.Logo, spill);
        }

        // One wall-hung set showing a logo: a thin black bezel on a bracket, a dark screen a hair in front, the logo lit
        // on it, and the set's soft light on the wall round it. inches: across.
        static void LogoScreen(Live3D.Stage stage, string name, float x, float inches, Texture2D logo, Color spillColour)
        {
            const float W = 112f, H = 63f;   // drawn at this size, then scaled to the set
            Transform tv = stage.Group(name);
            tv.localPosition = new Vector3(x, 64f, Back - 4f);
            tv.localScale = Vector3.one * (inches / W);
            stage.Box("Bracket", new Vector3(0, 0, 3f), new Vector3(30f, 20f, 3f), stage.Lit(new Color(0.02f, 0.02f, 0.02f), 0.4f), tv);
            stage.Box("Bezel", Vector3.zero, new Vector3(W + 2.4f, H + 2.4f, 2.2f), stage.Lit(new Color(0.015f, 0.015f, 0.017f), 0.9f), tv);
            GameObject screen = stage.Primitive(PrimitiveType.Quad, "Screen", stage.Emissive(new Color(0.03f, 0.03f, 0.035f), new Color(0.03f, 0.03f, 0.035f)), tv);
            screen.transform.localPosition = new Vector3(0, 0, -1.15f);
            screen.transform.localScale = new Vector3(W, H, 1f);
            if (logo != null)
            {
                Vector2 size = Brand.Fit(logo, W * 0.72f, H * 0.5f);
                GameObject mark = stage.Primitive(PrimitiveType.Quad, "Logo", stage.Glow(new Color(1f, 1f, 1f, 1f), false, logo), tv);
                mark.transform.localPosition = new Vector3(0, 3f, -1.3f);
                mark.transform.localScale = new Vector3(size.x, size.y, 1f);
            }
            GameObject spill = stage.Primitive(PrimitiveType.Quad, "TV spill", stage.Glow(spillColour, true, Make.SoftDot()), tv);
            spill.transform.localPosition = new Vector3(0, 0, 3.6f);   // just in front of the slats behind it, never in their plane
            spill.transform.localScale = new Vector3(W * 1.7f, H * 1.9f, 1f);
        }

        // ── The bar, back right ──────────────────────────────────────────────

        static void Bar(Live3D.Stage stage)
        {
            // A short bar on the right, well clear of the Puttr: dark walnut, a black marble top, a brass rail.
            float x0 = 105f, x1 = Side - 20f, z = Back - 62f;
            Material walnut = Textured(stage, new Color(0.13f, 0.075f, 0.045f), 0.55f, Make.Wood(3), new Vector2(3f, 1f));
            Material marble = stage.Lit(new Color(0.05f, 0.05f, 0.055f), 0.93f);
            Material brass = stage.Lit(new Color(0.7f, 0.5f, 0.26f), 0.8f, 0.95f);
            float mid = (x0 + x1) / 2f, len = x1 - x0;
            stage.Box("Bar front", new Vector3(mid, 20f, z), new Vector3(len, 40f, 22f), walnut);
            stage.Box("Bar top", new Vector3(mid, 41.5f, z - 2f), new Vector3(len + 6f, 2.5f, 28f), marble);
            stage.Box("Foot rail", new Vector3(mid, 7f, z - 15f), new Vector3(len, 1.4f, 1.4f), brass);
            for (int i = 0; i < 3; i++)
            {
                float x = x0 + 25f + i * (len - 50f) / 2f;
                Cyl(stage, "Stool post", new Vector3(x, 15f, z - 30f), 1f, 30f, brass);
                Cyl(stage, "Stool foot ring", new Vector3(x, 10f, z - 30f), 5f, 0.6f, brass);
                Cyl(stage, "Stool foot", new Vector3(x, 0.6f, z - 30f), 6f, 1.2f, stage.Lit(new Color(0.04f, 0.04f, 0.045f), 0.7f, 0.6f));
                Cyl(stage, "Stool seat", new Vector3(x, 31f, z - 30f), 6.5f, 2.6f, stage.Lit(new Color(0.08f, 0.05f, 0.035f), 0.45f));
            }

            // The back bar against the wall: a low walnut cabinet, a smoked mirror above it, two glass shelves each with a
            // thin warm LED under it, and bottles shaped as bottles (body, shoulder, neck, cap, a paper label). The
            // mirror and shelves sit in front of the wall slats, never in their plane (that fought and flickered).
            float wallFace = Back - 2f;
            stage.Box("Back bar cabinet", new Vector3(mid, 20f, wallFace - 12f), new Vector3(len, 40f, 22f), walnut);
            stage.Box("Back bar top", new Vector3(mid, 41f, wallFace - 12f), new Vector3(len + 2f, 2f, 24f), marble);
            stage.Box("Mirror", new Vector3(mid, 92f, wallFace - 0.6f), new Vector3(len, 96f, 0.6f), stage.Lit(new Color(0.06f, 0.055f, 0.05f), 0.97f, 0.9f));
            stage.Box("Mirror frame", new Vector3(mid, 141f, wallFace - 1f), new Vector3(len + 4f, 3f, 1.4f), brass);
            Material glassShelf = stage.Lit(new Color(0.55f, 0.6f, 0.58f), 0.97f, 0.2f);
            Material led = stage.Emissive(new Color(1f, 0.75f, 0.45f), new Color(1f, 0.62f, 0.3f) * 1.1f);
            var bottleGlass = new[]
            {
                stage.Lit(new Color(0.2f, 0.09f, 0.02f), 0.96f, 0.15f),   // amber: whisky, rum
                stage.Lit(new Color(0.03f, 0.12f, 0.06f), 0.96f, 0.15f),  // green: gin
                stage.Lit(new Color(0.42f, 0.44f, 0.45f), 0.97f, 0.2f),   // clear: vodka, tequila
                stage.Lit(new Color(0.02f, 0.03f, 0.08f), 0.96f, 0.15f),  // blue
                stage.Lit(new Color(0.02f, 0.02f, 0.02f), 0.96f, 0.15f),  // black
            };
            var labels = new[] { stage.Lit(new Color(0.85f, 0.8f, 0.68f), 0.25f), stage.Lit(new Color(0.08f, 0.08f, 0.08f), 0.3f), stage.Lit(new Color(0.55f, 0.12f, 0.08f), 0.3f), stage.Lit(new Color(0.75f, 0.62f, 0.35f), 0.5f, 0.6f) };
            Material cap = stage.Lit(new Color(0.06f, 0.05f, 0.04f), 0.6f, 0.4f);
            var rng = new System.Random(4);
            foreach (float y in new[] { 42f, 76f, 110f })
            {
                if (y > 50f)
                {
                    stage.Box("Shelf", new Vector3(mid, y, wallFace - 6f), new Vector3(len - 4f, 0.8f, 10f), glassShelf);
                    stage.Box("Shelf LED", new Vector3(mid, y - 0.7f, wallFace - 9.5f), new Vector3(len - 6f, 0.4f, 0.4f), led);
                }
                float depth = y > 50f ? wallFace - 6f : wallFace - 16f;
                for (float x = x0 + 10f; x < x1 - 8f; x += 10f + (float)rng.NextDouble() * 4f)
                    Bottle(stage, new Vector3(x, y + (y > 50f ? 0.4f : 1f), depth), bottleGlass[rng.Next(bottleGlass.Length)], labels[rng.Next(labels.Length)], cap, (float)rng.NextDouble());
            }
            // Bar tools on the counter: a brass shaker, two glasses.
            Cyl(stage, "Shaker", new Vector3(x0 + 30f, 47f, z - 4f), 1.8f, 9f, brass);
            Cyl(stage, "Glass", new Vector3(x0 + 40f, 45f, z - 6f), 1.6f, 4f, stage.Lit(new Color(0.5f, 0.52f, 0.5f), 0.97f, 0.2f));
            Cyl(stage, "Glass", new Vector3(x0 + 46f, 45f, z - 3f), 1.6f, 4f, stage.Lit(new Color(0.5f, 0.52f, 0.5f), 0.97f, 0.2f));
        }

        // A bottle standing on y: a body, a rounded shoulder, a neck and a cap, a paper label round the body.
        static void Bottle(Live3D.Stage stage, Vector3 at, Material glass, Material label, Material cap, float kind)
        {
            bool tall = kind > 0.55f, squat = kind < 0.2f;
            float r = squat ? 2.4f : tall ? 1.6f : 1.9f, body = squat ? 6f : tall ? 10f : 8f, neck = tall ? 4.5f : 3f;
            Cyl(stage, "Bottle", at + new Vector3(0, body / 2f, 0), r, body, glass);
            Ball(stage, "Shoulder", at + new Vector3(0, body, 0), new Vector3(r * 2f, r * 1.2f, r * 2f), glass);
            Cyl(stage, "Neck", at + new Vector3(0, body + r * 0.4f + neck / 2f, 0), r * 0.38f, neck, glass);
            Cyl(stage, "Cap", at + new Vector3(0, body + r * 0.4f + neck + 0.6f, 0), r * 0.45f, 1.2f, cap);
            Cyl(stage, "Label", at + new Vector3(0, body * 0.45f, 0), r * 1.03f, body * 0.45f, label);
        }

        // ── The lounge, left ─────────────────────────────────────────────────

        static void Lounge(Live3D.Stage stage)
        {
            Material velvet = stage.Lit(new Color(0.03f, 0.2f, 0.16f), 0.25f);          // deep emerald velvet
            Material leather = stage.Lit(new Color(0.42f, 0.18f, 0.07f), 0.6f);         // cognac leather
            Material brass = stage.Lit(new Color(0.78f, 0.56f, 0.28f), 0.85f, 0.95f);
            Material marble = stage.Lit(new Color(0.06f, 0.06f, 0.065f), 0.95f);        // black marble

            // Two sofas in an L, facing the Puttr.
            Sofa(stage, "Sofa", new Vector3(-215f, 0, -170f), -90f, 110f, velvet, brass);
            Sofa(stage, "Sofa", new Vector3(-150f, 0, -95f), 0f, 96f, velvet, brass);
            // A low coffee table between them: black marble on brass.
            stage.Box("Coffee table", new Vector3(-150f, 15f, -160f), new Vector3(52f, 2.2f, 34f), marble);
            foreach (Vector2 c in new[] { new Vector2(-172f, -174f), new Vector2(-128f, -174f), new Vector2(-172f, -146f), new Vector2(-128f, -146f) })
                Cyl(stage, "Table leg", new Vector3(c.x, 7f, c.y), 0.8f, 14f, brass);
            // Glasses on it: two coupes, a candle.
            foreach (float x in new[] { -160f, -142f })
            {
                Cyl(stage, "Glass stem", new Vector3(x, 19f, -158f), 0.3f, 6f, brass);
                Cyl(stage, "Glass", new Vector3(x, 22.5f, -158f), 2.6f, 1.2f, stage.Lit(new Color(0.9f, 0.75f, 0.4f), 0.95f));
            }
            stage.Box("Book", new Vector3(-136f, 17.2f, -168f), new Vector3(12f, 1.6f, 9f), stage.Lit(new Color(0.75f, 0.72f, 0.66f), 0.3f));
            stage.Box("Book", new Vector3(-136f, 18.6f, -168f), new Vector3(10f, 1.2f, 8f), stage.Lit(new Color(0.1f, 0.18f, 0.16f), 0.3f)).transform.localRotation = Quaternion.Euler(0, 12f, 0);
            Cyl(stage, "Candle", new Vector3(-150f, 18.5f, -168f), 1.6f, 5f, stage.Emissive(new Color(1f, 0.9f, 0.7f), new Color(1f, 0.7f, 0.35f) * 1.4f));

            // Leather club chairs: two on the left by the window side, two on the right in front of the bar.
            Chair(stage, new Vector3(-200f, 0, -300f), -135f, leather);
            Chair(stage, new Vector3(-120f, 0, -310f), -160f, leather);
            Chair(stage, new Vector3(195f, 0, -150f), -100f, leather);
            Chair(stage, new Vector3(200f, 0, -250f), -80f, leather);
            stage.Box("Side table", new Vector3(205f, 13f, -200f), new Vector3(20f, 2f, 20f), marble);
            Cyl(stage, "Side table post", new Vector3(205f, 6f, -200f), 1f, 12f, brass);

            // Art on the left wall, each lit from above.
            for (int i = 0; i < 2; i++)
            {
                float z = -260f + i * 150f;
                stage.Box("Frame", new Vector3(-Side + 2f, 140f, z), new Vector3(2f, 70f, 96f), brass);
                stage.Box("Canvas", new Vector3(-Side + 3.2f, 140f, z), new Vector3(1f, 64f, 90f), stage.Lit(i == 0 ? new Color(0.12f, 0.22f, 0.2f) : new Color(0.35f, 0.18f, 0.1f), 0.3f));
                stage.Box("Picture light", new Vector3(-Side + 7f, 178f, z), new Vector3(4f, 1.6f, 40f), stage.Emissive(new Color(1f, 0.8f, 0.5f), new Color(1f, 0.7f, 0.4f) * 1.6f));
            }
        }

        static void Sofa(Live3D.Stage stage, string name, Vector3 at, float turn, float length, Material velvet, Material brass)
        {
            Transform t = stage.Group(name);
            t.localPosition = at;
            t.localRotation = Quaternion.Euler(0, turn, 0);
            // Facing +z in its own frame: seat, back, arms, cushions, brass feet.
            stage.Box("Base", new Vector3(0, 9f, 0), new Vector3(length, 10f, 36f), velvet, t);
            stage.Box("Back", new Vector3(0, 22f, 15f), new Vector3(length, 28f, 8f), velvet, t);
            stage.Box("Arm", new Vector3(-length / 2f + 4f, 16f, 0), new Vector3(8f, 18f, 36f), velvet, t);
            stage.Box("Arm", new Vector3(length / 2f - 4f, 16f, 0), new Vector3(8f, 18f, 36f), velvet, t);
            int n = Mathf.Max(2, Mathf.RoundToInt((length - 16f) / 32f));
            float w = (length - 16f) / n;
            for (int i = 0; i < n; i++) stage.Box("Cushion", new Vector3(-length / 2f + 8f + w * (i + 0.5f), 16.5f, -2f), new Vector3(w - 1.5f, 5f, 28f), velvet, t);
            foreach (float x in new[] { -length / 2f + 5f, length / 2f - 5f })
                foreach (float z in new[] { -14f, 14f })
                    stage.Box("Foot", new Vector3(x, 2f, z), new Vector3(2f, 4f, 2f), brass, t);
            // Rounded tops on the back and arms (a velvet roll, not a hard edge), and throw pillows in the corners.
            GameObject roll = stage.Primitive(PrimitiveType.Capsule, "Back roll", velvet, t);
            roll.transform.localPosition = new Vector3(0, 36f, 15f);
            roll.transform.localRotation = Quaternion.Euler(0, 0, 90f);
            roll.transform.localScale = new Vector3(8f, length / 2f, 8f);
            foreach (float x in new[] { -length / 2f + 4f, length / 2f - 4f })
            {
                GameObject arm = stage.Primitive(PrimitiveType.Capsule, "Arm roll", velvet, t);
                arm.transform.localPosition = new Vector3(x, 25f, 0);
                arm.transform.localRotation = Quaternion.Euler(90f, 0, 0);
                arm.transform.localScale = new Vector3(8f, 18f, 8f);
            }
            Material pillow = stage.Lit(new Color(0.55f, 0.42f, 0.25f), 0.35f);
            Material pillow2 = stage.Lit(new Color(0.12f, 0.12f, 0.13f), 0.3f);
            Ball(stage, "Pillow", Vector3.zero, Vector3.one, pillow).transform.SetParent(t, false);
            Transform p1 = t.GetChild(t.childCount - 1);
            p1.localPosition = new Vector3(-length / 2f + 16f, 25f, 8f); p1.localRotation = Quaternion.Euler(-18f, 20f, 0); p1.localScale = new Vector3(16f, 15f, 5f);
            Ball(stage, "Pillow", Vector3.zero, Vector3.one, pillow2).transform.SetParent(t, false);
            Transform p2 = t.GetChild(t.childCount - 1);
            p2.localPosition = new Vector3(length / 2f - 16f, 25f, 8f); p2.localRotation = Quaternion.Euler(-18f, -15f, 0); p2.localScale = new Vector3(16f, 15f, 5f);
        }

        static void Chair(Live3D.Stage stage, Vector3 at, float turn, Material leather)
        {
            Transform t = stage.Group("Club chair");
            t.localPosition = at;
            t.localRotation = Quaternion.Euler(0, turn, 0);
            stage.Box("Seat", new Vector3(0, 11f, 0), new Vector3(30f, 12f, 30f), leather, t);
            stage.Box("Cushion", new Vector3(0, 18.5f, -2f), new Vector3(22f, 4f, 24f), leather, t);
            stage.Box("Back", new Vector3(0, 24f, 12f), new Vector3(30f, 26f, 7f), leather, t);
            stage.Box("Arm", new Vector3(-13f, 20f, 0), new Vector3(6f, 14f, 30f), leather, t);
            stage.Box("Arm", new Vector3(13f, 20f, 0), new Vector3(6f, 14f, 30f), leather, t);
            stage.Box("Plinth", new Vector3(0, 2.5f, 0), new Vector3(28f, 5f, 28f), stage.Lit(new Color(0.05f, 0.03f, 0.02f), 0.5f), t);
        }

        // ── Light fittings ───────────────────────────────────────────────────

        static readonly List<Vector3> Pendants = new List<Vector3>();

        static void Lamps(Live3D.Stage stage)
        {
            Pendants.Clear();
            Material brass = stage.Lit(new Color(0.78f, 0.56f, 0.28f), 0.85f, 0.95f);
            Material cord = stage.Lit(new Color(0.02f, 0.02f, 0.02f), 0.3f);
            Material globe = stage.Emissive(new Color(0.9f, 0.62f, 0.32f), new Color(1f, 0.55f, 0.22f) * 0.9f, 0.9f);   // smoked amber glass
            // Globes over the bar and over the lounge, hanging at different heights.
            var spots = new List<Vector3>();
            for (int i = 0; i < 3; i++) spots.Add(new Vector3(135f + i * 55f, 125f + (i % 2) * 12f, Back - 66f));
            spots.Add(new Vector3(-150f, 115f, -155f)); spots.Add(new Vector3(-125f, 132f, -135f)); spots.Add(new Vector3(-170f, 128f, -180f));
            foreach (Vector3 p in spots)
            {
                Cyl(stage, "Cord", new Vector3(p.x, (p.y + Ceiling) / 2f, p.z), 0.3f, Ceiling - p.y, cord);
                Cyl(stage, "Cap", new Vector3(p.x, p.y + 6f, p.z), 2.2f, 2f, brass);
                Ball(stage, "Globe", p, Vector3.one * 11f, globe);
                Pendants.Add(p);
            }
            // Brass floor lamps with linen shades by the sofas.
            Material shade = stage.Emissive(new Color(0.75f, 0.62f, 0.45f), new Color(1f, 0.62f, 0.3f) * 0.55f);
            foreach (Vector3 p in new[] { new Vector3(-250f, 0, -100f), new Vector3(-255f, 0, -270f) })
            {
                Cyl(stage, "Lamp base", p + new Vector3(0, 0.8f, 0), 8f, 1.6f, brass);
                Cyl(stage, "Lamp pole", p + new Vector3(0, 32f, 0), 0.7f, 62f, brass);
                Cyl(stage, "Lamp shade", p + new Vector3(0, 66f, 0), 9f, 13f, shade);
                Pendants.Add(p + new Vector3(0, 64f, 0));
            }
        }

        static void Plants(Live3D.Stage stage)
        {
            Material pot = stage.Lit(new Color(0.06f, 0.06f, 0.065f), 0.7f);
            Material leaf = stage.Lit(new Color(0.06f, 0.2f, 0.08f), 0.35f);
            Material leaf2 = stage.Lit(new Color(0.09f, 0.26f, 0.1f), 0.35f);
            var rng = new System.Random(9);
            foreach (Vector3 p in new[] { new Vector3(-Side + 30f, 0, Back - 30f), new Vector3(Side - 30f, 0, -330f), new Vector3(-Side + 30f, 0, -420f) })
            {
                Cyl(stage, "Planter", p + new Vector3(0, 13f, 0), 12f, 26f, pot);
                Cyl(stage, "Trunk", p + new Vector3(0, 45f, 0), 1.2f, 50f, stage.Lit(new Color(0.15f, 0.1f, 0.06f), 0.3f));
                for (int k = 0; k < 9; k++)
                {
                    Vector3 o = new Vector3(((float)rng.NextDouble() - 0.5f) * 34f, 60f + (float)rng.NextDouble() * 40f, ((float)rng.NextDouble() - 0.5f) * 34f);
                    float s = 14f + (float)rng.NextDouble() * 10f;
                    Ball(stage, "Leaves", p + o, new Vector3(s, s * 0.8f, s), k % 2 == 0 ? leaf : leaf2);
                }
            }
        }

        // Warm, low light: a soft key on the Puttr, pools from the pendants and lamps, the cove washing the walls.
        static void Lights(Live3D.Stage stage)
        {
            Light Add(LightType type, Vector3 at, Vector3 look, Color c, float intensity, float range, float angle = 60f)
            {
                var go = new GameObject("Lounge light");
                go.transform.SetParent(stage.Root, false);
                go.transform.localPosition = at;
                if (type == LightType.Spot) go.transform.localRotation = Quaternion.LookRotation(look - at);
                Light l = go.AddComponent<Light>();
                l.type = type;
                l.color = c;
                l.intensity = intensity;
                l.range = range;
                if (type == LightType.Spot) { l.spotAngle = angle; l.innerSpotAngle = angle * 0.4f; }
                l.shadows = LightShadows.None;
                l.cullingMask = 1 << Live3D.Layer;
                l.enabled = false;
                stage.Lights.Add(l);
                return l;
            }
            Color warm = new Color(1f, 0.78f, 0.52f);
            Add(LightType.Spot, new Vector3(0, 280f, -90f), new Vector3(0, 0, -60f), new Color(1f, 0.92f, 0.82f), 70000f, 600f, 46f);
            // Few real lights: with one per lamp the engine picked a different handful for each object as the camera moved,
            // and things flickered. The pendants and shades glow; these two warm the bar and the lounge.
            Add(LightType.Point, new Vector3(190f, 150f, Back - 50f), Vector3.zero, new Color(1f, 0.7f, 0.42f), 22000f, 260f);   // the back bar's glow
            Add(LightType.Point, new Vector3(-170f, 200f, -170f), Vector3.zero, warm, 26000f, 360f);
            stage.Light(new Color(1f, 0.85f, 0.7f), 0.12f, new Vector3(50f, 20f, 0f));
        }
    }
}

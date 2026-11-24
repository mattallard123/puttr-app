// The set, in two halves.
//
// In front: a Puttr, alone in the dark. Dark green carpet curving up into the
// ramp, the hole, the black console around it, and the one light there is:
// the console's own, under its hood, straight above the hole.
//
// Behind: what the back of the console opens onto, which is the game.
//
// There is no room. If environments are added later, BuildRoom is where one
// would go.
using UnityEngine;
using UnityEngine.Rendering;

namespace PuttrArcade
{
    public sealed partial class World
    {
        public Transform Root;
        /// <summary>The ring of light around the hole. It flashes when a ball arrives.</summary>
        public Material HoleRing;
        public Light HoodLight;

        const float WallZ = LaneSpec.RampEndZ + 0.8f;
        const float BoxInner = LaneSpec.MatHalfWidth + 0.3f;
        const float BoxOuter = BoxInner + 0.8f;
        const float BoxFrontZ = -13f;
        const float BoxTop = 14f;

        public static void ApplyPhysicsSettings()
        {
            // One unit is one inch, so the physics constants are in inches too.
            Physics.simulationMode = SimulationMode.FixedUpdate;
            Physics.gravity = new Vector3(0, -LaneSpec.Gravity, 0);
            Physics.defaultSolverIterations = 12;
            Physics.defaultSolverVelocityIterations = 4;
            Physics.bounceThreshold = 8f;
            Physics.sleepThreshold = 4f;
            Physics.defaultContactOffset = 0.02f;
            Physics.defaultMaxDepenetrationVelocity = 120f;
            Time.fixedDeltaTime = 1f / 240f;
            // After a long frame the physics catches up by at most this much (24 steps), not a third of a second (80):
            // on a slow device the catching up was itself what made the next frame long.
            Time.maximumDeltaTime = 0.1f;
        }

        public void Build(Transform parent)
        {
            Root = new GameObject("World").transform;
            Root.SetParent(parent, false);
            BuildRoom();
            BuildHall();
            BuildLights();
        }

        // ── The room ────────────────────────────────────────────────────────

        void BuildRoom()
        {
            float floorY = LaneSpec.MatY - 0.3f;
            float w = LaneSpec.MatHalfWidth;

            // The carpet: one ribbon from behind the camera, up the curve and
            // the ramp, to the far end of the mat. It is its own collider.
            // The texture is the real carpet, cut from a product photograph.
            const float carpetTile = 1f / 12.8f;
            Material carpet;
            Texture2D carpetColor;
            if (Make.Photo("CarpetPhoto", 1.4f, out carpetColor, out Texture2D carpetNormal))
                carpet = Make.Lit(new Color(1.15f, 1.3f, 0.92f), 0.1f, 0f, carpetColor, carpetNormal, 0.8f);
            else
            {
                Make.Carpet(out carpetColor, out carpetNormal);
                carpet = Make.Lit(new Color(0.04f, 0.13f, 0.1f), 0.12f, 0f, carpetColor, carpetNormal, 0.9f);
            }

            // The collider is one plain ribbon. What you see is the same
            // ribbon with a square left out around the hole, and a patch with
            // the hole cut in it.
            const float patch = 3f;
            float cos = Mathf.Cos(LaneSpec.RampAngleDeg * Mathf.Deg2Rad);
            float pz = patch * cos;
            System.Func<float, float> Y = LaneSpec.SurfaceY, dY = LaneSpec.SurfaceSlope;
            Mesh solid = Make.Ribbon(LaneSpec.MatStartZ, LaneSpec.RampEndZ, -w, w, Y, dY, 0.5f, carpetTile);
            var matCollider = new GameObject("Carpet collider").AddComponent<MeshCollider>();
            matCollider.transform.SetParent(Root, false);
            matCollider.sharedMesh = solid;
            matCollider.sharedMaterial = Make.Physics(0.6f, 0.05f);

            Make.MeshObject("Carpet", Root, Make.Ribbon(LaneSpec.MatStartZ, -pz, -w, w, Y, dY, 0.5f, carpetTile), carpet);
            Make.MeshObject("Carpet", Root, Make.Ribbon(pz, LaneSpec.RampEndZ, -w, w, Y, dY, 0.5f, carpetTile), carpet);
            Make.MeshObject("Carpet", Root, Make.Ribbon(-pz, pz, -w, -patch, Y, dY, 0.5f, carpetTile), carpet);
            Make.MeshObject("Carpet", Root, Make.Ribbon(-pz, pz, patch, w, Y, dY, 0.5f, carpetTile), carpet);

            // Its bound edge, and the ball return that runs down the right side:
            // a black channel between two black rails, as on the real one.
            Material binding = Make.Lit(new Color(0.012f, 0.03f, 0.024f), 0.2f);
            for (int side = -1; side <= 1; side += 2)
            {
                Mesh edge = Make.Ribbon(LaneSpec.MatStartZ, LaneSpec.RampEndZ, side * w - 0.18f, side * w + 0.18f, z => LaneSpec.SurfaceY(z) + 0.02f, LaneSpec.SurfaceSlope, 1f, 0.1f);
                Make.MeshObject("Carpet edge", Root, edge, binding);
            }
            Material rail = Make.Lit(new Color(0.012f, 0.012f, 0.014f), 0.45f, 0.2f);
            float returnLen = ShellFrontZ - 0.06f - LaneSpec.MatStartZ, returnMid = LaneSpec.MatStartZ + returnLen * 0.5f;
            Make.Box("Ball return", Root, new Vector3(w + 1.9f, floorY + 0.12f, returnMid), new Vector3(1.9f, 0.24f, returnLen), Make.Lit(new Color(0.035f, 0.037f, 0.04f), 0.35f, 0.2f), null, false);
            Make.Box("Return rail", Root, new Vector3(w + 0.85f, floorY + 0.3f, returnMid), new Vector3(0.22f, 0.6f, returnLen), rail, null, false);
            Make.Box("Return rail", Root, new Vector3(w + 2.95f, floorY + 0.3f, returnMid), new Vector3(0.22f, 0.6f, returnLen), rail, null, false);

            // The hole: a real cup, cut square to the ramp, with a ring of
            // light at its rim for when a ball arrives.
            Quaternion tilt = Quaternion.FromToRotation(Vector3.up, LaneSpec.SurfaceNormal(0f));
            Vector3 up = LaneSpec.SurfaceNormal(0f);
            Make.MeshObject("Carpet around hole", Root, Make.HolePatch(patch, LaneSpec.HoleRadius, carpetTile, Vector3.zero, cos), carpet).transform.SetPositionAndRotation(Vector3.zero, tilt);
            Make.MeshObject("Cup", Root, Make.Cup(LaneSpec.HoleRadius, 4f), Make.Lit(new Color(0.012f, 0.02f, 0.017f), 0.25f)).transform.SetPositionAndRotation(Vector3.zero, tilt);
            GameObject pile = Make.MeshObject("Cut pile", Root, Make.Cup(LaneSpec.HoleRadius + 0.004f, 0.22f, false), carpet);
            pile.transform.SetPositionAndRotation(up * 0.002f, tilt);
            HoleRing = Make.Glow(Color.black);
            GameObject glow = Make.MeshObject("Hole light", Root, Make.Ring(LaneSpec.HoleRadius - 0.02f, LaneSpec.HoleRadius + 0.2f), HoleRing);
            glow.transform.SetPositionAndRotation(up * 0.025f, tilt);

            // The console: matte black, open at the back. What you see is built
            // in WorldConsole.cs from the product shots; these are the walls the
            // ball meets, exactly where they have always been.
            PhysicsMaterial wallPhysics = Make.Physics(0.3f, 0.4f);
            float boxLen = WallZ - BoxFrontZ, boxMid = BoxFrontZ + boxLen * 0.5f;
            float sideH = BoxTop - floorY;
            for (int side = -1; side <= 1; side += 2)
            {
                var wall = new GameObject("Console side collider").AddComponent<BoxCollider>();
                wall.transform.SetParent(Root, false);
                wall.transform.localPosition = new Vector3(side * (BoxInner + BoxOuter) * 0.5f, floorY + sideH * 0.5f, boxMid);
                wall.size = new Vector3(BoxOuter - BoxInner, sideH, boxLen);
                wall.sharedMaterial = wallPhysics;
            }
            BuildConsole(floorY);
            // Under the end of the mat, facing the lane. Its collider is as it
            // was; what you see stops inside the console's walls, or its ends
            // would sit in the same plane as the shell's sides and flicker.
            Material riserPaint = Make.Lit(new Color(0.03f, 0.03f, 0.033f), 0.38f, 0.15f);
            float riserH = LaneSpec.RampEndY - (LaneSpec.LaneHeadY - 12f);
            Vector3 riserAt = new Vector3(0, LaneSpec.RampEndY - 0.06f - riserH * 0.5f, LaneSpec.RampEndZ - 0.45f);
            GameObject riser = Make.Box("Riser", Root, riserAt, new Vector3(BoxOuter * 2, riserH, 0.8f), riserPaint, wallPhysics);
            riser.GetComponent<MeshRenderer>().enabled = false;
            // Below the console's floor it would stand under the box like a plinth, so it stops there.
            float riserTop = LaneSpec.RampEndY - 0.06f;
            Make.Box("Riser face", Root, new Vector3(0, (riserTop + floorY) * 0.5f, riserAt.z), new Vector3((BoxInner - 0.06f) * 2f, riserTop - floorY, 0.8f), riserPaint, null, false);

            // The wall behind it, with the console set into it, and the floor.
            // Nothing behind or beside the console but black. These panels are
            // unlit, and hide the hall except through the console itself.
            // Their front faces stand a hair proud of WallZ: the hall's walls and
            // floor start at WallZ, and two faces in one plane flicker (the line
            // beside the box as the camera swings round it).
            Material black = Make.Glow(new Color(0f, 0f, 0f, 1f), false);
            const float blackZ = WallZ + 0.45f;
            const float wide = 160f, tall = 90f;
            float low = floorY - 30f;
            Make.Box("Black left", Root, new Vector3(-(BoxOuter + wide) * 0.5f, (tall + low) * 0.5f, blackZ), new Vector3(wide - BoxOuter, tall - low, 1f), black, null, false);
            Make.Box("Black right", Root, new Vector3((BoxOuter + wide) * 0.5f, (tall + low) * 0.5f, blackZ), new Vector3(wide - BoxOuter, tall - low, 1f), black, null, false);
            Make.Box("Black above", Root, new Vector3(0, (tall + BoxTop) * 0.5f, blackZ), new Vector3(BoxOuter * 2, tall - BoxTop, 1f), black, null, false);
            Make.Box("Black below", Root, new Vector3(0, (floorY + low) * 0.5f - 0.2f, blackZ), new Vector3(BoxOuter * 2, floorY - low, 1f), black, null, false);

            // The console's shell is on a layer of its own, so one faint light
            // can pick out its edges without touching the carpet.
            foreach (Transform child in Root)
                if (child.name.StartsWith("Console")) child.gameObject.layer = ShellLayer;
        }

        // ── The hall behind the console ─────────────────────────────────────

        /// <summary>Where each game builds its set: dark walls, and a floor far below.</summary>
        public Transform Hall;
        public const float HallEndZ = 46f;

        void BuildHall()
        {
            Hall = new GameObject("Hall").transform;
            Hall.SetParent(Root, false);
            Material hall = Make.Lit(new Color(0.02f, 0.021f, 0.024f), 0.3f);
            PhysicsMaterial soft = Make.Physics(0.6f, 0.05f);
            float w = LaneSpec.MatHalfWidth;
            float len = HallEndZ - WallZ, mid = WallZ + len * 0.5f;
            for (int side = -1; side <= 1; side += 2)
                Make.Box("Hall wall", Hall, new Vector3(side * (w + 6f), LaneSpec.LaneHeadY + 6f, mid), new Vector3(1f, 44f, len), hall, soft);
            Make.Box("Hall floor", Hall, new Vector3(0, LaneSpec.LaneHeadY - 12f, mid), new Vector3(w * 2 + 14f, 1f, len), hall, soft);
            Make.Box("Hall end", Hall, new Vector3(0, LaneSpec.LaneHeadY + 6f, HallEndZ + 0.5f), new Vector3(w * 2 + 14f, 44f, 1f), hall, soft);
        }

        /// <summary>
        /// A stretch of lane running from local z = 0 to length: boards, a
        /// gutter each side with a line of light, and the decks outside them.
        /// </summary>
        public static void LaneSection(Transform parent, float length, float thick, Material wood, Material gutter, Material dark, Material edgeLight, PhysicsMaterial lanePhysics, PhysicsMaterial wallPhysics, bool castShadows)
        {
            float lane = LaneSpec.LaneHalfWidth, w = LaneSpec.MatHalfWidth, outer = lane + LaneSpec.GutterWidth;
            float mid = length * 0.5f;
            GameObject boards = Make.Box("Lane", parent, new Vector3(0, -thick * 0.5f, mid), new Vector3(lane * 2, thick, length), wood, lanePhysics);
            if (!castShadows) boards.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            if (thick < 0.5f)
            {
                // A thin sheet to look at, but a solid inch of collider under it.
                var box = boards.GetComponent<BoxCollider>();
                float deep = 1.2f / thick;
                box.size = new Vector3(1f, deep, 1f);
                box.center = new Vector3(0f, 0.5f - deep * 0.5f, 0f);
            }
            for (int side = -1; side <= 1; side += 2)
            {
                Make.Box("Gutter", parent, new Vector3(side * (lane + LaneSpec.GutterWidth * 0.5f), -LaneSpec.GutterDepth - 0.25f, mid), new Vector3(LaneSpec.GutterWidth, 0.5f, length), gutter, lanePhysics);
                float deckW = w - outer + 6f;
                Make.Box("Side deck", parent, new Vector3(side * (outer + deckW * 0.5f), -0.4f, mid), new Vector3(deckW, 2.4f, length), dark, wallPhysics);
                Make.Box("Edge light", parent, new Vector3(side * (outer + 0.07f), 0.81f, mid), new Vector3(0.1f, 0.025f, length), edgeLight, null, false);
            }
        }

        // ── Light ───────────────────────────────────────────────────────────

        const int ShellLayer = 1; // Unity's built-in "TransparentFX" layer: unused here

        void BuildLights()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.012f, 0.014f, 0.016f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = Make.ReflectionSky();
            RenderSettings.reflectionIntensity = 0.55f;
            RenderSettings.fog = false;

            // The only light on the carpet: the console's own, under the hood,
            // straight above the hole. It is brightest at the hole and falls
            // away down the mat into the dark.
            HoodLight = NewLight("Hood light", LightType.Spot, new Color(0.82f, 0.95f, 1f), 900f);
            HoodLight.transform.position = new Vector3(0, BoxTop - 1.1f, 0.5f);
            HoodLight.transform.rotation = Quaternion.Euler(90f, 0, 0);
            HoodLight.spotAngle = 160f;
            HoodLight.innerSpotAngle = 110f;
            HoodLight.range = 70f;
            HoodLight.cullingMask = ~(1 << ShellLayer) | (1 << ShellLayer);

            // A whisper of light on the shell alone, so the console has edges.
            Light rim = NewLight("Shell rim", LightType.Directional, new Color(0.75f, 0.85f, 1f), 0.4f);
            rim.transform.rotation = Quaternion.Euler(40f, 25f, 0f);
            rim.shadows = LightShadows.None;
            rim.cullingMask = 1 << ShellLayer;
        }

        Light NewLight(string name, LightType type, Color color, float intensity) => NewLight(Root, name, type, color, intensity);

        public static Light NewLight(Transform parent, string name, LightType type, Color color, float intensity)
        {
            var light = new GameObject(name).AddComponent<Light>();
            light.transform.SetParent(parent, false);
            light.type = type;
            light.color = color;
            light.intensity = intensity;
            light.shadows = LightShadows.Soft;
            PlainLights.Add(light);
            return light;
        }
    }
}

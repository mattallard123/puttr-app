// The hole-entry games' shared close-up (Matt, 2026-11-23): any game that scores where the ball enters the hole
// (Center Cut, Reducer, Doors, Around the Clock) is drawn on the mat's real 4.25 inch hole, from Center Cut's own
// viewpoint: looked down on from behind the ball, the cup filling the view. A scene builds its gates, doors or
// clock round the rim and takes its camera from here, so every one of them frames the hole the same way.
// cupR is the scene's cup radius in its own units (2.125 inches x the scene's scale).
using UnityEngine;
using UnityEngine.Rendering;

namespace PuttrArcade
{
    public static class HoleView
    {
        const float CenterCutCupR = 10.625f;   // Center Cut's cup radius in its units (2.125 in x 5): its camera is given in these

        public static bool Portrait => Screen.height > Screen.width;
        public static Vector3 From(float cupR) { float k = cupR / CenterCutCupR; return new Vector3(0f, (Portrait ? 92f : 54f) * k, (Portrait ? -66f : -38f) * k); }
        public static Vector3 Look(float cupR) { float k = cupR / CenterCutCupR; return new Vector3(0f, 0f, (Portrait ? 14f : 4f) * k); }
        public static float Fov => Portrait ? 42f : 38f;

        /// <summary>Upright: how far down the game's picture the app's score bug ends, as a fraction of the picture's
        /// height (0 sideways, where the bug is not over the picture).</summary>
        public static float BugBottom()
        {
            if (!Portrait) return 0f;
            return Hud.StatusBelowPx > 1f ? Mathf.Clamp(Hud.StatusBelowPx / Mathf.Max(1f, Screen.height * FreePuttMode.UprightShare), 0f, 0.6f) : 0.37f;
        }

        /// <summary>
        /// The mat round the hole and the cup itself (white liner at the top, black below), as on the Puttr mat. The mat is
        /// 24 inches wide, runs back to the Player and stops 7 inches past the hole. Nothing a scene adds may cross the
        /// cup's column below y = 0, or it shows in the hole.
        /// </summary>
        public static void Build(Transform root, float cupR, Color turf)
        {
            float k = cupR / 2.125f, half = 12f * k, far = 7f * k;
            Make.Carpet(out Texture2D pile, out Texture2D pileNormal);
            Material mat = Make.Lit(turf, 0.12f, 0f, pile, pileNormal, 0.9f);
            float uv = 1f / (10f * k);
            Make.MeshObject("Mat by the cup", root, Make.HolePatch(far, cupR, uv, Vector3.zero, 1f, 96), mat);
            Make.MeshObject("Mat left", root, Flat(-half, -far, -far, far, uv), mat);
            Make.MeshObject("Mat right", root, Flat(far, half, -far, far, uv), mat);
            Make.MeshObject("Mat near", root, Flat(-half, half, -200f * k, -far, uv), mat);
            Make.MeshObject("Liner", root, Make.Cup(cupR, 1.1f * k, false, 72), Make.Lit(new Color(0.92f, 0.93f, 0.9f), 0.45f));
            GameObject inner = Make.MeshObject("Cup", root, Make.Cup(cupR, 2.9f * k, true, 72), Make.Lit(new Color(0.015f, 0.015f, 0.017f), 0.15f));
            inner.transform.localPosition = new Vector3(0f, -1.1f * k, 0f);
        }

        /// <summary>A floor under the mat with the cup's column cut out of it (y is below the mat).</summary>
        public static GameObject Floor(Transform root, float cupR, float y, Material material)
        {
            GameObject floor = Make.MeshObject("Floor", root, Make.HolePatch(3000f, cupR + 0.1f * cupR, 0f, Vector3.zero, 1f, 48), material);
            floor.transform.localPosition = new Vector3(0f, y, 0f);
            floor.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            return floor;
        }

        static Mesh Flat(float x0, float x1, float z0, float z1, float uv)
        {
            var mesh = new Mesh { name = "Carpet" };
            mesh.vertices = new[] { new Vector3(x0, 0, z0), new Vector3(x0, 0, z1), new Vector3(x1, 0, z1), new Vector3(x1, 0, z0) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.uv = new[] { new Vector2(x0 * uv, z0 * uv), new Vector2(x0 * uv, z1 * uv), new Vector2(x1 * uv, z1 * uv), new Vector2(x1 * uv, z0 * uv) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}

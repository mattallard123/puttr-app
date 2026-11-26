// The ideal putt's picture (AimView) away from the arcade: the console's ramp and cup alone, with the hood's light,
// for a game that has a world of its own (Tracks). The arcade's AimView looks at the ramp the arcade itself built;
// this builds only what that camera sees, in the same place (the hole at the origin), so the same picture comes out.
// It must not be alive while the arcade is in play: they would stand in the same spot. Close it when the game ends.
using UnityEngine;
using UnityEngine.Rendering;

namespace PuttrArcade
{
    public sealed class AimStage
    {
        /// <summary>The ramp's own layer, so the host game can keep its sun off it (the picture is lit by the hood alone,
        /// as in the arcade's dark room).</summary>
        public const int Layer = 6;
        readonly GameObject _root;
        public readonly AimView View;

        public AimStage()
        {
            _root = new GameObject("Aim stage");
            Object.DontDestroyOnLoad(_root);
            Transform root = _root.transform;

            // The carpet and the cup, as World.BuildRoom lays them (what is seen only: nothing here is hit by a ball).
            float w = LaneSpec.MatHalfWidth;
            const float carpetTile = 1f / 12.8f, patch = 3f;
            Material carpet;
            if (Make.Photo("CarpetPhoto", 1.4f, out Texture2D colour, out Texture2D normal))
                carpet = Make.Lit(new Color(1.15f, 1.3f, 0.92f), 0.1f, 0f, colour, normal, 0.8f);
            else
            {
                Make.Carpet(out colour, out normal);
                carpet = Make.Lit(new Color(0.04f, 0.13f, 0.1f), 0.12f, 0f, colour, normal, 0.9f);
            }
            float cos = Mathf.Cos(LaneSpec.RampAngleDeg * Mathf.Deg2Rad), pz = patch * cos;
            System.Func<float, float> Y = LaneSpec.SurfaceY, dY = LaneSpec.SurfaceSlope;
            Make.MeshObject("Carpet", root, Make.Ribbon(LaneSpec.MatStartZ, -pz, -w, w, Y, dY, 0.5f, carpetTile), carpet);
            Make.MeshObject("Carpet", root, Make.Ribbon(pz, LaneSpec.RampEndZ, -w, w, Y, dY, 0.5f, carpetTile), carpet);
            Make.MeshObject("Carpet", root, Make.Ribbon(-pz, pz, -w, -patch, Y, dY, 0.5f, carpetTile), carpet);
            Make.MeshObject("Carpet", root, Make.Ribbon(-pz, pz, patch, w, Y, dY, 0.5f, carpetTile), carpet);
            Quaternion tilt = Quaternion.FromToRotation(Vector3.up, LaneSpec.SurfaceNormal(0f));
            Vector3 up = LaneSpec.SurfaceNormal(0f);
            Make.MeshObject("Carpet around hole", root, Make.HolePatch(patch, LaneSpec.HoleRadius, carpetTile, Vector3.zero, cos), carpet).transform.SetPositionAndRotation(Vector3.zero, tilt);
            Make.MeshObject("Cup", root, Make.Cup(LaneSpec.HoleRadius, 4f), Make.Lit(new Color(0.012f, 0.02f, 0.017f), 0.25f)).transform.SetPositionAndRotation(Vector3.zero, tilt);
            Make.MeshObject("Cut pile", root, Make.Cup(LaneSpec.HoleRadius + 0.004f, 0.22f, false), carpet).transform.SetPositionAndRotation(up * 0.002f, tilt);

            // The hood's light, straight above the hole (World.BuildLights). Not on an old chip: there every light is
            // kept by PlainLights as one flat sun for the whole picture, and the host game's own sun lights this too.
            if (!Arcade.Plain)
            {
                var hood = new GameObject("Hood light").AddComponent<Light>();
                hood.transform.SetParent(root, false);
                hood.type = LightType.Spot;
                hood.color = new Color(0.82f, 0.95f, 1f);
                hood.intensity = 900f;
                hood.shadows = LightShadows.None;
                hood.transform.position = new Vector3(0f, 12.9f, 0.5f);
                hood.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                hood.spotAngle = 160f;
                hood.innerSpotAngle = 110f;
                hood.range = 70f;
            }
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            View = new AimView(root);
            RenderPipelineManager.beginCameraRendering += Before;
            RenderPipelineManager.endCameraRendering += After;
        }

        // The arcade's room is dark (World.BuildLights: a flat, near-black ambient); the host game's is not. This
        // camera alone draws in the arcade's, so the carpet is lit by the hood and nothing else.
        AmbientMode _modeWas;
        Color _lightWas;
        bool _swapped;

        void Before(ScriptableRenderContext context, Camera camera)
        {
            if (camera != View.Camera) return;
            _modeWas = RenderSettings.ambientMode; _lightWas = RenderSettings.ambientLight;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.012f, 0.014f, 0.016f);
            _swapped = true;
        }

        void After(ScriptableRenderContext context, Camera camera)
        {
            if (!_swapped || camera != View.Camera) return;
            _swapped = false;
            RenderSettings.ambientMode = _modeWas; RenderSettings.ambientLight = _lightWas;
        }

        public void Close()
        {
            RenderPipelineManager.beginCameraRendering -= Before;
            RenderPipelineManager.endCameraRendering -= After;
            View.Release();
            if (_root != null) Object.Destroy(_root);
        }
    }
}

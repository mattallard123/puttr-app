// Puttr > Set Up Project: makes the few assets that cannot be made at run
// time (two base materials and the scene), and checks the settings the game
// depends on. Safe to run again at any time.
//
// From the command line:
//   Unity -batchmode -quit -projectPath . -executeMethod PuttrArcade.EditorTools.ProjectSetup.Run
using System.IO;
using Puttr;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PuttrArcade.EditorTools
{
    public static class ProjectSetup
    {
        const string ResourceDir = "Assets/PuttrArcade/Resources/PuttrArcade";
        const string ScenePath = "Assets/Scenes/PuttrArcade.unity";

        [MenuItem("Puttr/Set Up Project")]
        public static void Run()
        {
            Directory.CreateDirectory(ResourceDir);
            Directory.CreateDirectory("Assets/Scenes");

            // Materials made at run time are copies of these, so that the
            // shader variants they need are included in a build.
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            SaveMaterial(new Material(lit), "Lit");
            Material emissive = SaveMaterial(new Material(lit), "LitEmissive");
            // URP re-derives a Lit material's keywords from its properties when
            // the asset is imported, so set what emission is derived from.
            emissive.SetColor("_EmissionColor", Color.white);
            emissive.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            emissive.EnableKeyword("_EMISSION");
            EditorUtility.SetDirty(emissive);

            // Normal mapped, and see-through: the keywords come from these properties.
            Material normal = SaveMaterial(new Material(lit), "LitNormal");
            normal.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ResourceDir + "/LogoMark.png"));
            normal.EnableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(normal);

            Material clear = SaveMaterial(new Material(lit), "LitTransparent");
            clear.SetFloat("_Surface", 1f);
            clear.SetFloat("_Blend", 0f);
            clear.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            clear.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            clear.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            clear.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            clear.SetFloat("_ZWrite", 0f);
            clear.SetOverrideTag("RenderType", "Transparent");
            clear.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            clear.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            clear.SetShaderPassEnabled("DepthOnly", false);
            clear.SetShaderPassEnabled("ShadowCaster", false);
            EditorUtility.SetDirty(clear);

            // The console speaks plain http on the local network.
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            PlayerSettings.runInBackground = true;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var game = new GameObject("Puttr Arcade");
            game.AddComponent<PuttrConsole>();
            game.AddComponent<Arcade>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            AssetDatabase.SaveAssets();
            Debug.Log("Puttr Arcade: keywords normal=" + normal.IsKeywordEnabled("_NORMALMAP") + " transparent=" + clear.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") + " queue=" + clear.renderQueue);
            if (!emissive.IsKeywordEnabled("_EMISSION")) Debug.LogWarning("Puttr Arcade: LitEmissive lost its _EMISSION keyword. Glowing surfaces will be dark in a build.");
            Debug.Log("Puttr Arcade: project set up. Open " + ScenePath + " and press Play.");
        }

        static Material SaveMaterial(Material material, string name)
        {
            string path = ResourceDir + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>Build a macOS app into Builds/.</summary>
        [MenuItem("Puttr/Build macOS App")]
        public static void BuildMac()
        {
            // Built while the editor is set to another platform, the app comes out with no renderer.
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneOSX)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/PuttrArcade.app",
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log("Puttr Arcade build: " + report.summary.result + ", " + report.summary.totalErrors + " errors");
            if (Application.isBatchMode && report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}

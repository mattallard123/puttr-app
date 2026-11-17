// Project setup and builds for Puttr 2, run from the menu or in batch mode (Tools/app-build.sh).
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp.EditorTools
{
    public static class AppSetup
    {
        const string ResourceDir = "Assets/PuttrApp/Resources/PuttrApp";
        const string ScenePath = "Assets/Scenes/PuttrApp.unity";

        /// <summary>Makes the panel settings the UI is drawn with and the empty scene the app starts in.</summary>
        [MenuItem("Puttr 2/Set Up Project")]
        public static void Run()
        {
            AssetDatabase.Refresh();
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ResourceDir + "/Theme.tss");
            if (theme == null) Debug.LogError("Puttr 2: Theme.tss did not import. Text will not draw.");
            string panelPath = ResourceDir + "/Panel.asset";
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(panelPath);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panel, panelPath);
            }
            panel.themeStyleSheet = theme;
            // Laid out for a phone 390 units wide; App.cs flips the match to height on a wide screen.
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(390, 844);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0f;
            panel.forceGammaRendering = false;
            EditorUtility.SetDirty(panel);

            // The app builds itself in code (App.Boot), so the scene is empty.
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            // The console speaks plain http on the local network.
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            PlayerSettings.runInBackground = true;
            AssetDatabase.SaveAssets();
            Debug.Log("Puttr 2: project set up. Theme " + (theme != null) + ", panel " + panelPath + ", scene " + ScenePath);
        }

        [MenuItem("Puttr 2/Build macOS App")]
        public static void BuildMac() => Build(BuildTarget.StandaloneOSX, "Builds/Puttr.app");

        [MenuItem("Puttr 2/Build for iOS")]
        public static void BuildIos() => Build(BuildTarget.iOS, "Builds/iOS");

        // The Simulator needs its own build: one made for a real iPhone will not install there.
        // Same steps as the arcade's Packaging.BuildIosSimulator, Burst switched off for this build.
        [MenuItem("Puttr 2/Build for iOS Simulator")]
        public static void BuildIosSimulator()
        {
            iOSSdkVersion was = PlayerSettings.iOS.sdkVersion;
            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.SimulatorSDK;
            AppleMobileArchitectureSimulator arch = PlayerSettings.iOS.simulatorSdkArchitecture;
            PlayerSettings.iOS.simulatorSdkArchitecture = AppleMobileArchitectureSimulator.Universal;
            const string burst = "ProjectSettings/BurstAotSettings_iOS.json";
            string before = File.Exists(burst) ? File.ReadAllText(burst) : null;
            File.WriteAllText(burst, "{\"MonoBehaviour\":{\"Version\":4,\"EnableBurstCompilation\":false,\"EnableOptimisations\":true,\"EnableSafetyChecks\":false,\"EnableDebugInAllBuilds\":false,\"DebugDataKind\":0,\"EnableArmv9SecurityFeatures\":false,\"CpuMinTargetX32\":0,\"CpuMaxTargetX32\":0,\"CpuMinTargetX64\":0,\"CpuMaxTargetX64\":0,\"OptimizeFor\":0}}");
            try { Build(BuildTarget.iOS, "Builds/iOS-Simulator"); }
            finally
            {
                PlayerSettings.iOS.sdkVersion = was;
                PlayerSettings.iOS.simulatorSdkArchitecture = arch;
                if (before != null) File.WriteAllText(burst, before); else File.Delete(burst);
            }
        }

        [MenuItem("Puttr 2/Build for Android")]
        public static void BuildAndroid()
        {
            // Signed with puttr-app's own debug key (React Native's public debug keystore): its SHA-1 is the one
            // registered for co.puttr.android in Firebase, so Google sign-in works in a test build. A Play build
            // is signed with the upload key instead; this never ships.
            const string key = "../puttr-app/android/app/debug.keystore";
            if (File.Exists(key))
            {
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = Path.GetFullPath(key);
                PlayerSettings.Android.keystorePass = "android";
                PlayerSettings.Android.keyaliasName = "androiddebugkey";
                PlayerSettings.Android.keyaliasPass = "android";
            }
            Build(BuildTarget.Android, "Builds/Android/Puttr.apk");
        }

        static void Build(BuildTarget target, string path)
        {
            if (EditorUserBuildSettings.activeBuildTarget != target)
                Debug.LogWarning("Puttr 2: the editor is set to " + EditorUserBuildSettings.activeBuildTarget + ", not " + target + ". Start batch mode with -buildTarget.");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = path, target = target, options = BuildOptions.None });
            Debug.Log("Puttr 2 " + target + " build: " + report.summary.result + ", " + report.summary.totalErrors + " errors, " + (report.summary.totalSize / (1024 * 1024)) + " MB");
            if (Application.isBatchMode && report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}

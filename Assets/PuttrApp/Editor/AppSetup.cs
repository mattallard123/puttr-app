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
        public static void BuildIos()
        {
            // Always the device SDK, whatever the project was left on: a simulator build that failed part way once
            // left it on the simulator's, and the next Builds/iOS could not be run on an iPhone.
            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
            Build(BuildTarget.iOS, "Builds/iOS");
        }

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
            // The settings go back before a failed build ends batch mode (exiting inside the build skipped this).
            bool ok = false;
            try { ok = Build(BuildTarget.iOS, "Builds/iOS-Simulator", false); }
            finally
            {
                PlayerSettings.iOS.sdkVersion = was == iOSSdkVersion.SimulatorSDK ? iOSSdkVersion.DeviceSDK : was;
                PlayerSettings.iOS.simulatorSdkArchitecture = arch;
                if (before != null) File.WriteAllText(burst, before); else File.Delete(burst);
            }
            if (!ok && Application.isBatchMode) EditorApplication.Exit(1);
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

        /// <summary>
        /// The Google Play build: an app bundle (Builds/AndroidPlay/Puttr.aab) signed with puttr-app's upload key, the
        /// key the live co.puttr.android is uploaded with. The key's file and passwords are read from puttr-app's
        /// android/gradle.properties (MYAPP_UPLOAD_*) at build time and never written into this project: the settings
        /// go back to the debug key and an .apk afterwards, so the test build (BuildAndroid) is unchanged.
        /// One package for phones, tablets and TVs: Play sends it to TVs only once the Android TV form factor is
        /// added in Play Console, so a phone release does not reach TVs.
        /// </summary>
        [MenuItem("Puttr 2/Build for Google Play")]
        public static void BuildAndroidPlay()
        {
            const string gradle = "../puttr-app/android/gradle.properties";
            var props = new System.Collections.Generic.Dictionary<string, string>();
            if (File.Exists(gradle))
                foreach (string line in File.ReadAllLines(gradle))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0 && !line.TrimStart().StartsWith("#")) props[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
            props.TryGetValue("MYAPP_UPLOAD_STORE_FILE", out string store);
            string storePath = store == null ? null : Path.GetFullPath(Path.Combine("../puttr-app/android/app", store));
            if (storePath == null || !File.Exists(storePath) || !props.ContainsKey("MYAPP_UPLOAD_STORE_PASSWORD") || !props.ContainsKey("MYAPP_UPLOAD_KEY_ALIAS") || !props.ContainsKey("MYAPP_UPLOAD_KEY_PASSWORD"))
            {
                Debug.LogError("Puttr 2 Google Play build: the upload key was not found (puttr-app/android/gradle.properties MYAPP_UPLOAD_*).");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }
            bool bundle = EditorUserBuildSettings.buildAppBundle;
            var symbols = EditorUserBuildSettings.androidCreateSymbols;
            string name = PlayerSettings.Android.keystoreName, alias = PlayerSettings.Android.keyaliasName;
            try
            {
                EditorUserBuildSettings.buildAppBundle = true;
                // Native debug symbols (function names and lines, no code) zipped next to the bundle, for Play
                // Console's crash and ANR reports: upload it under the release's App bundle explorer.
                EditorUserBuildSettings.androidCreateSymbols = AndroidCreateSymbols.Public;
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = storePath;
                PlayerSettings.Android.keystorePass = props["MYAPP_UPLOAD_STORE_PASSWORD"];
                PlayerSettings.Android.keyaliasName = props["MYAPP_UPLOAD_KEY_ALIAS"];
                PlayerSettings.Android.keyaliasPass = props["MYAPP_UPLOAD_KEY_PASSWORD"];
                Build(BuildTarget.Android, "Builds/AndroidPlay/Puttr.aab", false);
            }
            finally
            {
                // Back to the test build's settings: no upload key or password is left in ProjectSettings.
                EditorUserBuildSettings.buildAppBundle = bundle;
                EditorUserBuildSettings.androidCreateSymbols = symbols;
                PlayerSettings.Android.keystoreName = name;
                PlayerSettings.Android.keyaliasName = alias;
                PlayerSettings.Android.keystorePass = "android";
                PlayerSettings.Android.keyaliasPass = "android";
                AssetDatabase.SaveAssets();
            }
        }

        /// <summary>For the other build files here (MacBuilds): builds, and leaves a failure to the caller.</summary>
        internal static bool BuildTo(BuildTarget target, string path) => Build(target, path, false);

        static bool Build(BuildTarget target, string path, bool exitOnFailure = true)
        {
            if (EditorUserBuildSettings.activeBuildTarget != target)
                Debug.LogWarning("Puttr 2: the editor is set to " + EditorUserBuildSettings.activeBuildTarget + ", not " + target + ". Start batch mode with -buildTarget.");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = path, target = target, options = BuildOptions.None });
            Debug.Log("Puttr 2 " + target + " build: " + report.summary.result + ", " + report.summary.totalErrors + " errors, " + (report.summary.totalSize / (1024 * 1024)) + " MB");
            bool ok = report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;
            if (Application.isBatchMode && !ok && exitOnFailure) EditorApplication.Exit(1);
            return ok;
        }
    }
}

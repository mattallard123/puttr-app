// The TV builds (Tools/app-build.sh tvos | tvossim | webtv; Android TV is the Android build itself).
//
//   Apple TV       Builds/tvOS, an Xcode project: co.puttr.ios (tvOS was added to the iPhone app's App Store
//                  record), the iPhone's version and build, scheme PuttrCo. No Firebase SDK, sign-in with
//                  Apple or Google, camera, push or payments there: the iPhone post-processors are iOS only,
//                  the plugins in Plugins/iOS and Plugins/Apple are iPhone only, and Auth uses Firebase's REST API.
//   Apple TV Sim   Builds/tvOS-Simulator, the same for the Apple TV simulator (Burst off, as the iPhone one).
//   Android TV     the Android build: TV compatible (leanback launcher, touchscreen not required), the banner.
//   Samsung        Builds/WebTV, the WebGL build in the PuttrTV page (Assets/WebGLTemplates/PuttrTV), which
//                  app-build.sh packages with Tools/tizen/config.xml as a Tizen web app.
//
// Icons: Assets/PuttrApp/TvIcons, made by docs/appicon/tv.py from the app icon's art.
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PuttrApp.EditorTools
{
    public static class TvBuilds
    {
        const string ScenePath = "Assets/Scenes/PuttrApp.unity";
        const string Icons = "Assets/PuttrApp/TvIcons";
        const string AppleId = "co.puttr.ios";

        [MenuItem("Puttr 2/Build for Apple TV")]
        public static void BuildTvos()
        {
            FileUtil.DeleteFileOrDirectory("Library/BurstCache"); // after an iPhone build Burst hands back that platform's library
            PlayerSettings.tvOS.sdkVersion = tvOSSdkVersion.Device;
            Build(BuildTarget.tvOS, "Builds/tvOS");
        }

        // A device project will not run on the simulator: its own build, Burst off as for the iPhone simulator.
        [MenuItem("Puttr 2/Build for Apple TV Simulator")]
        public static void BuildTvosSimulator()
        {
            tvOSSdkVersion was = PlayerSettings.tvOS.sdkVersion;
            AppleMobileArchitectureSimulator arch = PlayerSettings.tvOS.simulatorSdkArchitecture;
            PlayerSettings.tvOS.sdkVersion = tvOSSdkVersion.Simulator;
            PlayerSettings.tvOS.simulatorSdkArchitecture = AppleMobileArchitectureSimulator.Universal;
            const string burst = "ProjectSettings/BurstAotSettings_tvOS.json";
            string before = File.Exists(burst) ? File.ReadAllText(burst) : null;
            File.WriteAllText(burst, "{\"MonoBehaviour\":{\"Version\":4,\"EnableBurstCompilation\":false,\"EnableOptimisations\":true,\"EnableSafetyChecks\":false,\"EnableDebugInAllBuilds\":false,\"DebugDataKind\":0,\"EnableArmv9SecurityFeatures\":false,\"CpuMinTargetX32\":0,\"CpuMaxTargetX32\":0,\"CpuMinTargetX64\":0,\"CpuMaxTargetX64\":0,\"OptimizeFor\":0}}");
            FileUtil.DeleteFileOrDirectory("Library/BurstCache");
            try { Build(BuildTarget.tvOS, "Builds/tvOS-Simulator"); }
            finally
            {
                PlayerSettings.tvOS.sdkVersion = was;
                PlayerSettings.tvOS.simulatorSdkArchitecture = arch;
                if (before != null) File.WriteAllText(burst, before); else File.Delete(burst);
            }
        }

        [MenuItem("Puttr 2/Build for Samsung TV (WebGL)")]
        public static void BuildWebTv()
        {
            // The page is the PuttrTV template. Samsung's Tizen runs a web app from its package, with no
            // Content-Encoding headers, so nothing is compressed (the .wgt is a zip anyway).
            PlayerSettings.WebGL.template = "PROJECT:PuttrTV";
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.dataCaching = false;          // the files are on the TV already
            PlayerSettings.WebGL.showDiagnostics = false;
            PlayerSettings.WebGL.threadsSupport = false;       // Tizen 6.5 is Chromium 85: no SharedArrayBuffer for apps
            PlayerSettings.WebGL.nameFilesAsHashes = false;
            // A TV's graphics chip is a phone's: it cannot read the desktop texture format (DXT), so every texture was
            // unpacked to four bytes a pixel as it loaded. ETC2 is what a WebGL 2 browser on that chip reads as it is.
            EditorUserBuildSettings.webGLBuildSubtarget = WebGLTextureSubtarget.ETC2;
            // The code built for size, with link-time optimisation: the TV compiles all of it before the first frame.
            // (This was "shorter build time", and only by what the editor last had set: it is said here now.)
            EditorUserBuildSettings.SetPlatformSettings("WebGL", "CodeOptimization", "DiskSizeLTO");
            // Memory to start with: from 32 MB the heap grew a fifth at a time, a dozen times, through the start.
            PlayerSettings.WebGL.initialMemorySize = 256;
            FileUtil.DeleteFileOrDirectory("Builds/WebTV");
            Build(BuildTarget.WebGL, "Builds/WebTV");
        }

        static void Build(BuildTarget target, string path)
        {
            if (EditorUserBuildSettings.activeBuildTarget != target)
                Debug.LogWarning("Puttr 2: the editor is set to " + EditorUserBuildSettings.activeBuildTarget + ", not " + target + ". Start batch mode with -buildTarget.");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = path, target = target, options = BuildOptions.None });
            Debug.Log("Puttr 2 " + target + " build: " + report.summary.result + ", " + report.summary.totalErrors + " errors, " + (report.summary.totalSize / (1024 * 1024)) + " MB");
            if (Application.isBatchMode && report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        /// <summary>Before any tvOS or Android build: the TV settings and icons, however the build was started.</summary>
        class Prepare : IPreprocessBuildWithReport
        {
            public int callbackOrder => -10;

            public void OnPreprocessBuild(BuildReport report)
            {
                if (report.summary.platform == BuildTarget.tvOS) Tvos();
                if (report.summary.platform == BuildTarget.Android) Android();
            }
        }

        static void Tvos()
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.tvOS, AppleId);
            PlayerSettings.tvOS.buildNumber = PlayerSettings.iOS.buildNumber;   // version: PlayerSettings.bundleVersion, shared
            int filled = 0;
            foreach (PlatformIconKind kind in PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.tvOS))
            {
                PlatformIcon[] icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.tvOS, kind);
                foreach (PlatformIcon icon in icons)
                {
                    string size = icon.width + "x" + icon.height;
                    if (icon.maxLayerCount >= 2)
                    {
                        // Layered: the first is the front.
                        Texture2D front = Icon("Front-" + size), back = Icon("Back-" + size);
                        if (front != null && back != null) { icon.SetTextures(front, back); filled++; }
                    }
                    else
                    {
                        Texture2D shelf = Icon("Shelf-" + size);
                        if (shelf != null) { icon.SetTextures(shelf); filled++; }
                    }
                }
                PlayerSettings.SetPlatformIcons(NamedBuildTarget.tvOS, kind, icons);
            }
            Debug.Log("Puttr 2 tvOS: " + AppleId + " " + PlayerSettings.bundleVersion + " (" + PlayerSettings.tvOS.buildNumber + "), " + filled + " icon slots from " + Icons);
        }

        static void Android()
        {
            // One package for phones, tablets and TVs: Unity adds the LEANBACK_LAUNCHER category, leanback and
            // touchscreen as not required, and android:banner from the banner below.
            PlayerSettings.Android.androidTVCompatibility = true;
            Texture2D banner = Icon("Banner-320x180");
            if (banner == null) return;
            var settings = new SerializedObject(Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings"));
            SerializedProperty enable = settings.FindProperty("androidEnableBanner");
            if (enable != null) enable.boolValue = true;
            SerializedProperty banners = settings.FindProperty("m_AndroidBanners");
            if (banners != null && banners.arraySize > 0) banners.GetArrayElementAtIndex(0).FindPropertyRelative("banner").objectReferenceValue = banner;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        static Texture2D Icon(string name)
        {
            string path = Icons + "/" + name + ".png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) { Debug.LogWarning("Puttr 2: no " + path + " (run python3 docs/appicon/tv.py)"); return null; }
            // Icons as they are: no power-of-two squeeze, no mipmaps, no compression.
            if (importer.npotScale != TextureImporterNPOTScale.None || importer.mipmapEnabled || importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureType = TextureImporterType.Default;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.alphaIsTransparency = name.StartsWith("Front");
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}

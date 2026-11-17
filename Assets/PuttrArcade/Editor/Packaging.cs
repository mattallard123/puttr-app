// Packaging for devices.
//
//   Puttr > Package for Mac    builds the app and zips it, ready to copy to another Mac
//   Puttr > Build for iOS      writes an Xcode project to Builds/iOS (needs Unity's iOS Build Support)
//   Puttr > Build for iOS Simulator   the same, for Xcode's Simulator, in Builds/iOS-Simulator
//   Puttr > Build for Apple TV writes one to Builds/tvOS (needs tvOS Build Support)
//   Puttr > Build for Android  writes Builds/Android/PuttrArcade.apk, for tablets and TVs alike
//
// Both also run from the command line with -executeMethod.
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace PuttrArcade.EditorTools
{
    public static class Packaging
    {
        const string Scene = "Assets/Scenes/PuttrArcade.unity";

        [MenuItem("Puttr/Package for Mac")]
        public static void PackageMac()
        {
            ProjectSetup.BuildMac();
            const string app = "Builds/PuttrArcade.app", zip = "Builds/PuttrArcade-mac.zip";
            if (!Directory.Exists(app)) return;
            if (File.Exists(zip)) File.Delete(zip);
            // ditto keeps the signature and the bundle's structure; plain zip does not.
            Run("/usr/bin/ditto", "-c -k --sequesterRsrc --keepParent \"" + app + "\" \"" + zip + "\"");
            Debug.Log("Puttr Arcade packaged: " + Path.GetFullPath(zip));
            if (!Application.isBatchMode) EditorUtility.RevealInFinder(zip);
        }

        [MenuItem("Puttr/Build for iOS")]
        public static void BuildIos() => BuildApple(BuildTargetGroup.iOS, BuildTarget.iOS, "iOS", "Builds/iOS");

        // The Simulator needs its own build: one made for a real iPhone will not install there.
        [MenuItem("Puttr/Build for iOS Simulator")]
        public static void BuildIosSimulator()
        {
            iOSSdkVersion was = PlayerSettings.iOS.sdkVersion;
            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.SimulatorSDK;
            // Simulators on an Apple silicon Mac are arm64, and on an Intel Mac x86_64: build both.
            AppleMobileArchitectureSimulator arch = PlayerSettings.iOS.simulatorSdkArchitecture;
            PlayerSettings.iOS.simulatorSdkArchitecture = AppleMobileArchitectureSimulator.Universal;
            // Burst cannot compile for the Simulator here: its library comes out
            // without the entry points the app links against. Nothing in the
            // arcade needs Burst, so it is switched off for this one build.
            const string burst = "ProjectSettings/BurstAotSettings_iOS.json";
            string before = File.Exists(burst) ? File.ReadAllText(burst) : null;
            File.WriteAllText(burst, "{\"MonoBehaviour\":{\"Version\":4,\"EnableBurstCompilation\":false,\"EnableOptimisations\":true,\"EnableSafetyChecks\":false,\"EnableDebugInAllBuilds\":false,\"DebugDataKind\":0,\"EnableArmv9SecurityFeatures\":false,\"CpuMinTargetX32\":0,\"CpuMaxTargetX32\":0,\"CpuMinTargetX64\":0,\"CpuMaxTargetX64\":0,\"OptimizeFor\":0}}");
            try { BuildApple(BuildTargetGroup.iOS, BuildTarget.iOS, "iOS", "Builds/iOS-Simulator"); }
            finally
            {
                PlayerSettings.iOS.sdkVersion = was;
                PlayerSettings.iOS.simulatorSdkArchitecture = arch;
                if (before != null) File.WriteAllText(burst, before); else File.Delete(burst);
            }
        }

        [MenuItem("Puttr/Build for Apple TV Simulator")]
        public static void BuildTvosSimulator()
        {
            tvOSSdkVersion was = PlayerSettings.tvOS.sdkVersion;
            PlayerSettings.tvOS.sdkVersion = tvOSSdkVersion.Simulator;
            AppleMobileArchitectureSimulator arch = PlayerSettings.tvOS.simulatorSdkArchitecture;
            PlayerSettings.tvOS.simulatorSdkArchitecture = AppleMobileArchitectureSimulator.Universal;
            const string burst = "ProjectSettings/BurstAotSettings_tvOS.json";
            string before = File.Exists(burst) ? File.ReadAllText(burst) : null;
            File.WriteAllText(burst, "{\"MonoBehaviour\":{\"Version\":4,\"EnableBurstCompilation\":false,\"EnableOptimisations\":true,\"EnableSafetyChecks\":false,\"EnableDebugInAllBuilds\":false,\"DebugDataKind\":0,\"EnableArmv9SecurityFeatures\":false,\"CpuMinTargetX32\":0,\"CpuMaxTargetX32\":0,\"CpuMinTargetX64\":0,\"CpuMaxTargetX64\":0,\"OptimizeFor\":0}}");
            try { BuildApple(BuildTargetGroup.tvOS, BuildTarget.tvOS, "tvOS", "Builds/tvOS-Simulator"); }
            finally
            {
                PlayerSettings.tvOS.sdkVersion = was;
                PlayerSettings.tvOS.simulatorSdkArchitecture = arch;
                if (before != null) File.WriteAllText(burst, before); else File.Delete(burst);
            }
        }

        [MenuItem("Puttr/Build for Apple TV")]
        public static void BuildTvos() => BuildApple(BuildTargetGroup.tvOS, BuildTarget.tvOS, "tvOS", "Builds/tvOS");

        // One package for Android tablets and TVs (Android TV, Google TV, Fire TV): the Player
        // Settings mark it as TV compatible, and the app asks the device which it is.
        // An .apk installs straight onto a device (adb install). Google Play wants an .aab,
        // signed with the upload key: BuildAndroidBundle, once a keystore is set in Player Settings.
        [MenuItem("Puttr/Build for Android")]
        public static void BuildAndroid() => BuildForAndroid(false);

        [MenuItem("Puttr/Build for Android (Play bundle)")]
        public static void BuildAndroidBundle() => BuildForAndroid(true);

        static void BuildForAndroid(bool bundle)
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
            {
                const string how = "Unity's Android Build Support is not installed. In Unity Hub: Installs > this editor > Add modules > Android Build Support (with the SDK, NDK and OpenJDK).";
                Debug.LogError(how);
                if (!Application.isBatchMode) EditorUtility.DisplayDialog("Build for Android", how, "OK");
                else EditorApplication.Exit(1);
                return;
            }
            Directory.CreateDirectory("Builds/Android");
            string file = "Builds/Android/PuttrArcade." + (bundle ? "aab" : "apk");
            EditorUserBuildSettings.buildAppBundle = bundle;
            var options = new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = file,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log("Puttr Arcade Android build: " + report.summary.result + ", " + report.summary.totalErrors + " errors");
            if (report.summary.result == BuildResult.Succeeded && !Application.isBatchMode) EditorUtility.RevealInFinder(file);
            if (Application.isBatchMode && report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        static void BuildApple(BuildTargetGroup group, BuildTarget target, string name, string folder)
        {
            if (!BuildPipeline.IsBuildTargetSupported(group, target))
            {
                string how = "Unity's " + name + " Build Support is not installed. In Unity Hub: Installs > this editor > Add modules > " + name + " Build Support.";
                Debug.LogError(how);
                if (!Application.isBatchMode) EditorUtility.DisplayDialog("Build for " + name, how, "OK");
                else EditorApplication.Exit(1);
                return;
            }
            // Built straight after the other Apple platform, Burst hands back
            // that platform's library and Xcode then fails to link. Start clean.
            FileUtil.DeleteFileOrDirectory("Library/BurstCache");
            FileUtil.DeleteFileOrDirectory(folder);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = folder,
                target = target,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log("Puttr Arcade " + name + " build: " + report.summary.result + ", " + report.summary.totalErrors + " errors");
            if (report.summary.result == BuildResult.Succeeded && !Application.isBatchMode) EditorUtility.RevealInFinder(folder + "/Unity-iPhone.xcodeproj");
            if (Application.isBatchMode && report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        static void Run(string tool, string arguments)
        {
            using (Process process = Process.Start(new ProcessStartInfo(tool, arguments) { UseShellExecute = false }))
                process.WaitForExit();
        }
    }
}

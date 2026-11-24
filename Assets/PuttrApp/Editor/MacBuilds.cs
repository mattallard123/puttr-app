// The Mac App Store build of Puttr 2.
//
//   Puttr 2 > Build for Mac App Store   Builds/macOS, an Xcode project: co.puttr.ios (macOS was added to the iPhone
//                                       app's App Store record, so the Mac app is the same app, one purchase), Intel
//                                       and Apple silicon. Open it, scheme PuttrCo, Product > Archive, as the iPhone one.
//
// Builds/Puttr.app (Puttr 2 > Build macOS App) stays what it was: a plain app to run on this Mac, co.puttr.app2,
// signed for nobody. The store's settings are put on for this build only and taken off again after it.
#if UNITY_STANDALONE_OSX
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace PuttrApp.EditorTools
{
    public static class MacBuilds
    {
        public const string StoreId = "co.puttr.ios", Folder = "Builds/macOS", Scheme = "PuttrCo";
        const string StoreKey = "PuttrApp.MacStoreBuild";

        [MenuItem("Puttr 2/Build for Mac App Store")]
        public static void BuildMacStore()
        {
            string id = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Standalone);
            string build = PlayerSettings.macOS.buildNumber;
            bool xcode = UnityEditor.OSXStandalone.UserBuildSettings.createXcodeProject;
            var arch = UnityEditor.OSXStandalone.UserBuildSettings.architecture;
            bool ok = false;
            try
            {
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, StoreId);
                PlayerSettings.macOS.buildNumber = PlayerSettings.iOS.buildNumber;   // version: PlayerSettings.bundleVersion, shared
                UnityEditor.OSXStandalone.UserBuildSettings.createXcodeProject = true;
                UnityEditor.OSXStandalone.UserBuildSettings.architecture = OSArchitecture.x64ARM64;
                SessionState.SetBool(StoreKey, true);
                if (Directory.Exists(Folder)) Directory.Delete(Folder, true);
                ok = AppSetup.BuildTo(BuildTarget.StandaloneOSX, Folder);
            }
            finally
            {
                SessionState.EraseBool(StoreKey);
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, id);
                PlayerSettings.macOS.buildNumber = build;
                UnityEditor.OSXStandalone.UserBuildSettings.createXcodeProject = xcode;
                UnityEditor.OSXStandalone.UserBuildSettings.architecture = arch;
            }
            if (!ok && Application.isBatchMode) EditorApplication.Exit(1);
        }

        // What the store needs that Unity's project does not have: the sandbox, signing by the team, every library
        // and plug-in inside the app signed with it, and the same Version / Build fields as the iPhone project.
        [PostProcessBuild(900)]
        public static void After(BuildTarget target, string path)
        {
            if (target != BuildTarget.StandaloneOSX || !SessionState.GetBool(StoreKey, false)) return;
            string name = PlayerSettings.productName;
            string projectPath = Path.Combine(path, Path.GetFileName(path.TrimEnd('/')) + ".xcodeproj/project.pbxproj");
            string plistPath = Path.Combine(path, name + "/Info.plist");

            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            PlistElementDict root = plist.root;
            root.SetString("LSApplicationCategoryType", "public.app-category.sports");
            root.SetBoolean("ITSAppUsesNonExemptEncryption", false);   // export compliance, as the iPhone app
            root.SetString("NSLocalNetworkUsageDescription", "Puttr finds your Puttr on your WiFi network to receive your putts.");
            root.SetString("NSBluetoothAlwaysUsageDescription", "Puttr connects to your Puttr over Bluetooth to receive your putts.");
            root.SetString("CFBundleDisplayName", name);
            root.SetString("CFBundleShortVersionString", "$(MARKETING_VERSION)");
            root.SetString("CFBundleVersion", "$(CURRENT_PROJECT_VERSION)");
            if (root["NSCameraUsageDescription"] != null) root.values.Remove("NSCameraUsageDescription");   // the Mac app has no camera screen
            // The console speaks plain http, on the local network only (Unity writes NSAllowsArbitraryLoads).
            PlistElementDict ats = root.CreateDict("NSAppTransportSecurity");
            ats.SetBoolean("NSAllowsLocalNetworking", true);
            plist.WriteToFile(plistPath);

            // The sandbox (the store refuses an app without it), the network (the API, the Puttr on the wifi, Google's
            // sign-in page), Bluetooth.
            string entitlements = name + "/" + name + ".entitlements";
            var rights = new PlistDocument();
            rights.root.SetBoolean("com.apple.security.app-sandbox", true);
            rights.root.SetBoolean("com.apple.security.network.client", true);
            rights.root.SetBoolean("com.apple.security.device.bluetooth", true);
            // Sign in with Apple (SocialSignIn, PuttrSignIn.bundle): the capability co.puttr.ios already has for the iPhone.
            rights.root.CreateArray("com.apple.developer.applesignin").AddString("Default");
            rights.WriteToFile(Path.Combine(path, entitlements));

            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            string guid = project.TargetGuidByName(name);
            project.AddFile(entitlements, entitlements);
            project.SetBuildProperty(guid, "CODE_SIGN_ENTITLEMENTS", entitlements);
            project.SetBuildProperty(guid, "CODE_SIGN_STYLE", "Automatic");
            project.SetBuildProperty(guid, "DEVELOPMENT_TEAM", PlayerSettings.iOS.appleDeveloperTeamID);
            project.SetBuildProperty(guid, "PRODUCT_BUNDLE_IDENTIFIER", StoreId);
            project.SetBuildProperty(guid, "MARKETING_VERSION", PlayerSettings.bundleVersion);
            project.SetBuildProperty(guid, "CURRENT_PROJECT_VERSION", PlayerSettings.macOS.buildNumber);
            project.SetBuildProperty(guid, "ENABLE_HARDENED_RUNTIME", "NO");   // Mono makes code as it runs; the store asks for the sandbox, not this
            project.WriteToFile(projectPath);

            // Unity copies its libraries and plug-ins into the app unsigned: the store wants each signed as the app is.
            string text = File.ReadAllText(projectPath);
            text = System.Text.RegularExpressions.Regex.Replace(text,
                @"(/\* [^*]+ in (?:Embed Libraries|Embed Frameworks|CopyPlugIns) \*/ = \{isa = PBXBuildFile; fileRef = [0-9A-F]+ /\* [^*]+ \*/; )\};",
                "$1settings = {ATTRIBUTES = (CodeSignOnCopy, ); }; };");
            File.WriteAllText(projectPath, text);

            // The Organizer names an archive after its scheme: PuttrCo, as the iPhone and Apple TV ones.
            string schemes = Path.Combine(Path.GetDirectoryName(projectPath), "xcshareddata/xcschemes");
            string from = Path.Combine(schemes, name + ".xcscheme"), to = Path.Combine(schemes, Scheme + ".xcscheme");
            if (File.Exists(from)) { if (File.Exists(to)) File.Delete(to); File.Move(from, to); }
            Debug.Log("Puttr 2 macOS: " + StoreId + " " + PlayerSettings.bundleVersion + " (" + PlayerSettings.macOS.buildNumber + "), sandboxed, team " + PlayerSettings.iOS.appleDeveloperTeamID);
        }
    }
}
#endif

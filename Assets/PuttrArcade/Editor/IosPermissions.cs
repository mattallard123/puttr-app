// What an iPhone needs to be told before the app may look for a console.
//
// Bluetooth's permission text comes from Player Settings. The local network
// one has no setting, so it is written into Info.plist after the build:
// without it iOS silently blocks every request to the console on WiFi.
#if UNITY_IOS || UNITY_TVOS
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace PuttrArcade.EditorTools
{
    public static class IosPermissions
    {
const string KeptKey = "PuttrArcade.KeptXcode";

        /// What the Xcode project already in a folder says, read before Unity writes over it.
        struct Kept
        {
            public string Version, Build, Name;

            public static Kept From(string folder)
            {
                var kept = new Kept();
                try
                {
                    string file = PBXProject.GetPBXProjectPath(folder);
                    if (!File.Exists(file)) return kept;
                    var project = new PBXProject();
                    project.ReadFromFile(file);
                    string main = project.GetUnityMainTargetGuid();
                    kept.Version = project.GetBuildPropertyForAnyConfig(main, "MARKETING_VERSION");
                    kept.Build = project.GetBuildPropertyForAnyConfig(main, "CURRENT_PROJECT_VERSION");
                    kept.Name = project.GetBuildPropertyForAnyConfig(main, "INFOPLIST_KEY_CFBundleDisplayName");
                }
                catch (System.Exception) { }
                return kept;
            }

            public string Save() => (Version ?? "") + "\n" + (Build ?? "") + "\n" + (Name ?? "");

            public static Kept Load(string text)
            {
                string[] parts = (text ?? "").Split('\n');
                return parts.Length < 3 ? new Kept() : new Kept { Version = parts[0], Build = parts[1], Name = parts[2] };
            }
        }

        class Remember : IPreprocessBuildWithReport
        {
            public int callbackOrder => 0;

            public void OnPreprocessBuild(BuildReport report)
            {
                BuildTarget target = report.summary.platform;
                if (target != BuildTarget.iOS && target != BuildTarget.tvOS) return;
                SessionState.SetString(KeptKey, Kept.From(report.summary.outputPath).Save());
            }
        }

        /// The later of two dotted numbers ("2" and "10", "1.0.0" and "1.1"). Anything unreadable loses.
        static string Higher(string a, string b)
        {
            if (!System.Version.TryParse(Dotted(b), out System.Version right)) return a;
            if (!System.Version.TryParse(Dotted(a), out System.Version left)) return b;
            return right > left ? b : a;
        }

        static string Dotted(string number) => string.IsNullOrEmpty(number) ? "" : number.Contains(".") ? number : number + ".0";

                [PostProcessBuild]
        public static void OnBuilt(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS && target != BuildTarget.tvOS) return;
            string plistPath = Path.Combine(path, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            PlistElementDict root = plist.root;
            root.SetString("NSLocalNetworkUsageDescription", "Puttr finds your Puttr on your WiFi network to receive your putts.");
            root.SetString("NSBluetoothAlwaysUsageDescription", "Puttr connects to your Puttr over Bluetooth to receive your putts.");
            // The console speaks plain http, on the local network only.
            PlistElementDict ats = root["NSAppTransportSecurity"] != null ? root["NSAppTransportSecurity"].AsDict() : root.CreateDict("NSAppTransportSecurity");
            ats.SetBoolean("NSAllowsLocalNetworking", true);
            // Export compliance: no encryption beyond what the system provides.
            root.SetBoolean("ITSAppUsesNonExemptEncryption", false);
            // Version, Build and Display Name follow the fields in Xcode (target > General), so
            // they can be raised there before an archive. Unity writes fixed text here, which
            // made those fields do nothing.
            root.SetString("CFBundleShortVersionString", "$(MARKETING_VERSION)");
            root.SetString("CFBundleVersion", "$(CURRENT_PROJECT_VERSION)");
            root.SetString("CFBundleDisplayName", "$(INFOPLIST_KEY_CFBundleDisplayName)");
            plist.WriteToFile(plistPath);

            string projectPath = PBXProject.GetPBXProjectPath(path);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            project.AddFrameworkToProject(project.GetUnityFrameworkTargetGuid(), "CoreBluetooth.framework", false);
            // Start those fields at what Unity has, unless the project this build replaces had
            // been raised further in Xcode: a rebuild must never take a build number backwards.
            string build = target == BuildTarget.tvOS ? PlayerSettings.tvOS.buildNumber : PlayerSettings.iOS.buildNumber;
            string version = PlayerSettings.bundleVersion, name = PlayerSettings.productName;
            Kept before = Kept.Load(SessionState.GetString(KeptKey, ""));
            string shelf = System.Environment.GetEnvironmentVariable("PUTTR_XCODE_KEEP");
            Kept there = string.IsNullOrEmpty(shelf) ? new Kept() : Kept.From(Path.Combine(shelf, Path.GetFileName(path.TrimEnd('/'))));
            foreach (Kept kept in new[] { before, there })
            {
                build = Higher(build, kept.Build);
                version = Higher(version, kept.Version);
                if (!string.IsNullOrEmpty(kept.Name)) name = kept.Name;
            }
            SessionState.EraseString(KeptKey);
            foreach (string guid in new[] { project.GetUnityMainTargetGuid(), project.GetUnityFrameworkTargetGuid() })
            {
                project.SetBuildProperty(guid, "CURRENT_PROJECT_VERSION", build);
                project.SetBuildProperty(guid, "MARKETING_VERSION", version);
            }
            project.SetBuildProperty(project.GetUnityMainTargetGuid(), "INFOPLIST_KEY_CFBundleDisplayName", name);
            UnityEngine.Debug.Log("Puttr Arcade " + target + ": version " + version + ", build " + build + ", name " + name);
            project.WriteToFile(projectPath);

            // Xcode names an archive after its scheme, and Unity's is always
            // "Unity-iPhone". Rename the scheme so the Organizer says PuttrCo (puttr-app-2: was Puttr Arcade).
            // (The target keeps its name: Unity's own tools look for it.)
            string schemes = Path.Combine(path, "Unity-iPhone.xcodeproj/xcshareddata/xcschemes");
            string from = Path.Combine(schemes, "Unity-iPhone.xcscheme"), to = Path.Combine(schemes, "PuttrCo.xcscheme");
            if (File.Exists(from))
            {
                if (File.Exists(to)) File.Delete(to);
                File.Move(from, to);
            }
        }
    }
}
#endif

// What the Xcode project needs for Sign in with Apple: the capability (an entitlements file)
// and the AuthenticationServices framework the plugin in Assets/Plugins/iOS calls.
#if UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace PuttrApp.EditorTools
{
    public static class IosSignIn
    {
        [PostProcessBuild(300)]
        public static void OnBuilt(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;
            string projectPath = PBXProject.GetPBXProjectPath(path);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            project.AddFrameworkToProject(project.GetUnityFrameworkTargetGuid(), "AuthenticationServices.framework", false);
            project.WriteToFile(projectPath);

            var capabilities = new ProjectCapabilityManager(projectPath, "Unity-iPhone/puttr.entitlements", null, project.GetUnityMainTargetGuid());
            capabilities.AddSignInWithApple();
            // Push (aps-environment development, as puttr-app's entitlements: Xcode makes it production on export),
            // and woken by a push in the background.
            capabilities.AddPushNotifications(true);
            capabilities.AddBackgroundModes(BackgroundModesOptions.RemoteNotifications | BackgroundModesOptions.BackgroundFetch);
            // Universal links: the same domains puttr-app's entitlements list (AASA on api.puttr.co and app.puttr.co; trk.puttr.co by Klaviyo).
            // Apple Pay in the lesson payment sheet: puttr-app's merchant id.
            capabilities.AddApplePay(new[] { "merchant.co.puttr.puttrapp" });
            capabilities.AddAssociatedDomains(new[] { "applinks:api.puttr.co", "applinks:app.puttr.co", "applinks:trk.puttr.co" });
            capabilities.WriteToFile();

            // puttr:// links, and what the photo library prompt says (puttr-app's Info.plist).
            string plistPath = System.IO.Path.Combine(path, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            PlistElementArray types = plist.root["CFBundleURLTypes"] as PlistElementArray ?? plist.root.CreateArray("CFBundleURLTypes");
            PlistElementDict puttr = types.AddDict();
            puttr.SetString("CFBundleURLName", "co.puttr.ios");
            puttr.CreateArray("CFBundleURLSchemes").AddString("puttr");
            plist.root.SetString("NSPhotoLibraryUsageDescription", "Puttr lets you choose a picture from your library as your profile photo.");
            plist.WriteToFile(plistPath);
            UnityEngine.Debug.Log("Puttr 2 iOS: Sign in with Apple, push, universal links and puttr:// added");
        }
    }
}
#endif

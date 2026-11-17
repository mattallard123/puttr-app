// Adds the Firebase iOS SDK to the Xcode project Unity writes, as a Swift package (no CocoaPods):
// the same version puttr-app ships, so both read the same signed-in Player from the Keychain.
// Also copies in GoogleService-Info.plist, which Firebase reads its project from.
#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace PuttrApp.EditorTools
{
    public static class IosFirebase
    {
        const string Version = "10.29.0"; // puttr-app/ios/Podfile.lock: keep them in step
        const string Plist = "Assets/PuttrApp/Editor/Firebase/GoogleService-Info.plist";

        [PostProcessBuild(250)]
        public static void OnBuilt(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;
            string projectPath = PBXProject.GetPBXProjectPath(path);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            string app = project.GetUnityMainTargetGuid(), framework = project.GetUnityFrameworkTargetGuid();

            string package = project.AddRemotePackageReferenceAtVersion("https://github.com/firebase/firebase-ios-sdk", Version);
            project.AddRemotePackageFrameworkToProject(framework, "FirebaseAuth", package, false);
            // Push: the FCM token (Plugins/iOS/PuttrPush.m).
            project.AddRemotePackageFrameworkToProject(framework, "FirebaseMessaging", package, false);
            // Paying for lessons (Plugins/iOS/PuttrPay.swift): Stripe's PaymentSheet, as puttr-app (stripe-ios 26.3.0).
            string stripe = project.AddRemotePackageReferenceAtVersion("https://github.com/stripe/stripe-ios-spm", "26.3.0");
            project.AddRemotePackageFrameworkToProject(framework, "StripePaymentSheet", stripe, false);
            // Puttr Club subscriptions: IAPHUB's SDK is CocoaPods-only, so its Swift is in Plugins/iOS/Iaphub (4.6.7);
            // it needs StoreKit and Swift 5.
            project.SetBuildProperty(framework, "SWIFT_VERSION", "5.0");
            project.AddFrameworkToProject(framework, "StoreKit.framework", false);
            project.AddFrameworkToProject(framework, "UserNotifications.framework", false);
            project.AddFrameworkToProject(framework, "PhotosUI.framework", false);
            project.AddFrameworkToProject(framework, "ReplayKit.framework", false);
            // The plugin is Objective-C and imports Firebase as modules; Firebase's categories need -ObjC to be linked in.
            project.SetBuildProperty(framework, "CLANG_ENABLE_MODULES", "YES");
            project.AddBuildProperty(framework, "OTHER_LDFLAGS", "-ObjC");

            File.Copy(Plist, Path.Combine(path, "GoogleService-Info.plist"), true);
            project.AddFileToBuild(app, project.AddFile("GoogleService-Info.plist", "GoogleService-Info.plist"));
            project.WriteToFile(projectPath);
            UnityEngine.Debug.Log("Puttr 2 iOS: Firebase " + Version + " added as a Swift package");
        }
    }
}
#endif

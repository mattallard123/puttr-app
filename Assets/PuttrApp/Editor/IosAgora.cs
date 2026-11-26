// Adds the Agora RTC iOS SDK to the Xcode project as a Swift package (no CocoaPods), and the
// camera and microphone permission texts the app must carry to use them: puttr-app's own words.
#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace PuttrApp.EditorTools
{
    public static class IosAgora
    {
        const string Version = "4.5.3"; // puttr-app ships react-native-agora 4.5.4 over the 4.5.3 engine

        [PostProcessBuild(260)]
        public static void OnBuilt(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;
            string projectPath = PBXProject.GetPBXProjectPath(path);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            string package = project.AddRemotePackageReferenceAtVersion("https://github.com/AgoraIO/AgoraRtcEngine_iOS", Version);
            // The plugin is compiled into UnityFramework; Agora's own frameworks are dynamic, and it is
            // the app that carries (embeds) them, so both are given the product.
            project.AddRemotePackageFrameworkToProject(project.GetUnityFrameworkTargetGuid(), "RtcBasic", package, false);
            project.AddRemotePackageFrameworkToProject(project.GetUnityMainTargetGuid(), "RtcBasic", package, false);
            project.WriteToFile(projectPath);

            string plistPath = Path.Combine(path, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            plist.root.SetString("NSCameraUsageDescription", "Puttr films your putting for 3D sessions, recorded rounds and live matches, and takes your profile photo.");
            plist.root.SetString("NSMicrophoneUsageDescription", "Puttr uses the microphone so your opponent hears you in a live match, and to record a Coach's voice in a lesson.");
            plist.WriteToFile(plistPath);
            UnityEngine.Debug.Log("Puttr 2 iOS: Agora " + Version + " added as a Swift package");
        }
    }
}
#endif

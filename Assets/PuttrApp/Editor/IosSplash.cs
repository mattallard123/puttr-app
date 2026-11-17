// The Puttr splash: the logo (mark and name) on the app's ink, half the screen's short side wide.
// It is the native launch screen, and Loader.cs draws the very same picture at the same size when
// Unity starts, so the only thing that changes is the progress bar appearing under it.
//
// The storyboards get names of their own (PuttrSplash-*). An app keeps the launch picture iOS
// cached for it, and this one installs over the React Native app, whose launch screen is black:
// a storyboard iOS has not seen before is drawn fresh.
#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace PuttrApp.EditorTools
{
    public static class IosSplash
    {
        const string Logo = "Assets/PuttrArcade/Resources/PuttrArcade/LogoHeader.png";
        const string Image = "PuttrSplash.png";
        const int LogoWide = 1943, LogoHigh = 479; // keep in step with Loader.SplashAspect
        const string Share = "0.5";                // of the screen's short side: Loader.SplashShare

        [PostProcessBuild(210)] // after the arcade's LaunchScreens (200), whose storyboards this starts from
        public static void OnBuilt(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;
            string projectPath = PBXProject.GetPBXProjectPath(path);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            string app = project.GetUnityMainTargetGuid();

            File.Copy(Logo, Path.Combine(path, Image), true);
            project.AddFileToBuild(app, project.AddFile(Image, Image));

            string plistPath = Path.Combine(path, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            foreach (string device in new[] { "iPhone", "iPad" })
            {
                string old = Path.Combine(path, "LaunchScreen-" + device + ".storyboard");
                if (!File.Exists(old)) continue;
                // The arcade's storyboard centres a square mark at 16% of the short side: the same layout, for a wide logo at half.
                string board = File.ReadAllText(old)
                    .Replace("LaunchScreen-" + device + ".png", Image)
                    .Replace("multiplier=\"0.16\"", "multiplier=\"" + Share + "\"")
                    .Replace("multiplier=\"1\" id=\"pl-sq\"", "multiplier=\"" + (LogoWide / (float)LogoHigh).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + "\" id=\"pl-sq\"")
                    .Replace("width=\"512\" height=\"512\"", "width=\"" + LogoWide + "\" height=\"" + LogoHigh + "\"");
                File.WriteAllText(old, board); // Unity shows this one again while the engine starts
                string name = "PuttrSplash-" + device;
                File.WriteAllText(Path.Combine(path, name + ".storyboard"), board);
                project.AddFileToBuild(app, project.AddFile(name + ".storyboard", name + ".storyboard"));
                if (device == "iPad") plist.root.SetString("UILaunchStoryboardName~ipad", name);
                else
                {
                    plist.root.SetString("UILaunchStoryboardName", name);
                    plist.root.SetString("UILaunchStoryboardName~iphone", name);
                    plist.root.SetString("UILaunchStoryboardName~ipod", name);
                }
            }
            plist.WriteToFile(plistPath);
            project.WriteToFile(projectPath);
            UnityEngine.Debug.Log("Puttr 2 iOS: Puttr splash set as the launch screen");
        }
    }
}
#endif

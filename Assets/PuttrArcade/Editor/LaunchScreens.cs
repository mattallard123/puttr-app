// The native launch screens match the Puttr spinner (Scripts/Loader.cs): the Puttr mark in Puttr green,
// centred, 16% of the screen's short side, on the arcade's ink. iOS/iPadOS gets its own storyboard
// (iPads come in several shapes, so the mark is laid out, not baked into a picture); tvOS keeps
// Unity's storyboard and gets 16:9 pictures of the same thing. When the player starts, the very
// same mark simply begins to turn. Images: Editor/Launch (LaunchMark.png, AppleTV-*.png).
#if UNITY_IOS || UNITY_TVOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;

namespace PuttrArcade.EditorTools
{
    public static class LaunchScreens
    {
        const string Art = "Assets/PuttrArcade/Editor/Launch";
        // Make.Ink and Make.Green as sRGB 0-1, the colours Loader.cs draws with.
        const string Ink = "red=\"0.035\" green=\"0.045\" blue=\"0.05\"";

        [PostProcessBuild(200)]
        public static void OnBuilt(BuildTarget target, string path)
        {
            if (target == BuildTarget.iOS) Ios(path);
            if (target == BuildTarget.tvOS) Tv(path);
        }

        static void Ios(string path)
        {
            // Unity's storyboards show LaunchScreen-*.png full screen; each now shows the mark, laid out.
            foreach (string name in new[] { "LaunchScreen-iPad", "LaunchScreen-iPhone" })
            {
                string board = Path.Combine(path, name + ".storyboard");
                if (!File.Exists(board)) continue;
                File.Copy(Path.Combine(Art, "LaunchMark.png"), Path.Combine(path, name + ".png"), true);
                File.WriteAllText(board, Storyboard(name + ".png"));
            }
        }

        static void Tv(string path)
        {
            string set = Path.Combine(path, "Unity-iPhone/Images.xcassets/SplashScreen.imageset");
            if (!Directory.Exists(set)) return;
            File.Copy(Path.Combine(Art, "AppleTV-1920x1080.png"), Path.Combine(set, "Default-AppleTV.png"), true);
            File.Copy(Path.Combine(Art, "AppleTV-3840x2160.png"), Path.Combine(set, "Default-AppleTV@2x.png"), true);
        }

        static string Storyboard(string image) => $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<document type=""com.apple.InterfaceBuilder3.CocoaTouch.Storyboard.XIB"" version=""3.0"" toolsVersion=""15702"" targetRuntime=""iOS.CocoaTouch"" propertyAccessControl=""none"" useAutolayout=""YES"" launchScreen=""YES"" useTraitCollections=""YES"" colorMatched=""YES"" initialViewController=""Puttr-Launch-VC"">
    <device id=""ipad9_7"" orientation=""landscape"" layout=""fullscreen"" appearance=""dark""/>
    <dependencies>
        <deployment identifier=""iOS""/>
        <plugIn identifier=""com.apple.InterfaceBuilder.IBCocoaTouchPlugin"" version=""15704""/>
        <capability name=""documents saved in the Xcode 8 format"" minToolsVersion=""8.0""/>
    </dependencies>
    <scenes>
        <scene sceneID=""Puttr-Launch-Scene"">
            <objects>
                <viewController id=""Puttr-Launch-VC"" sceneMemberID=""viewController"">
                    <view key=""view"" userInteractionEnabled=""NO"" contentMode=""scaleToFill"" id=""Puttr-Launch-Root"">
                        <rect key=""frame"" x=""0.0"" y=""0.0"" width=""1024"" height=""768""/>
                        <autoresizingMask key=""autoresizingMask"" widthSizable=""YES"" heightSizable=""YES""/>
                        <subviews>
                            <imageView userInteractionEnabled=""NO"" contentMode=""scaleAspectFit"" image=""{image}"" translatesAutoresizingMaskIntoConstraints=""NO"" id=""Puttr-Launch-Mark"">
                                <rect key=""frame"" x=""450.5"" y=""322.5"" width=""123"" height=""123""/>
                            </imageView>
                        </subviews>
                        <color key=""backgroundColor"" {Ink} alpha=""1"" colorSpace=""custom"" customColorSpace=""sRGB""/>
                        <constraints>
                            <constraint firstItem=""Puttr-Launch-Mark"" firstAttribute=""centerX"" secondItem=""Puttr-Launch-Root"" secondAttribute=""centerX"" id=""pl-cx""/>
                            <constraint firstItem=""Puttr-Launch-Mark"" firstAttribute=""centerY"" secondItem=""Puttr-Launch-Root"" secondAttribute=""centerY"" id=""pl-cy""/>
                            <constraint firstItem=""Puttr-Launch-Mark"" firstAttribute=""width"" secondItem=""Puttr-Launch-Mark"" secondAttribute=""height"" multiplier=""1"" id=""pl-sq""/>
                            <constraint firstItem=""Puttr-Launch-Mark"" firstAttribute=""width"" relation=""lessThanOrEqual"" secondItem=""Puttr-Launch-Root"" secondAttribute=""width"" multiplier=""0.16"" id=""pl-maxw""/>
                            <constraint firstItem=""Puttr-Launch-Mark"" firstAttribute=""width"" relation=""lessThanOrEqual"" secondItem=""Puttr-Launch-Root"" secondAttribute=""height"" multiplier=""0.16"" id=""pl-maxh""/>
                            <constraint firstItem=""Puttr-Launch-Mark"" firstAttribute=""width"" secondItem=""Puttr-Launch-Root"" secondAttribute=""width"" multiplier=""0.16"" priority=""750"" id=""pl-w""/>
                            <constraint firstItem=""Puttr-Launch-Mark"" firstAttribute=""width"" secondItem=""Puttr-Launch-Root"" secondAttribute=""height"" multiplier=""0.16"" priority=""750"" id=""pl-h""/>
                        </constraints>
                    </view>
                </viewController>
                <placeholder placeholderIdentifier=""IBFirstResponder"" id=""Puttr-Launch-FR"" userLabel=""First Responder"" sceneMemberID=""firstResponder""/>
            </objects>
        </scene>
    </scenes>
    <resources>
        <image name=""{image}"" width=""512"" height=""512""/>
    </resources>
</document>
";
    }
}
#endif

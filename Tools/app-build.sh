#!/bin/bash
# Builds and tests Puttr 2 in batch mode. Close the Unity Editor on this project first.
#
#   Tools/app-build.sh setup      panel settings + scene (run once, and after changing Theme.tss)
#   Tools/app-build.sh test       EditMode tests
#   Tools/app-build.sh mac        Builds/Puttr.app
#   Tools/app-build.sh macstore   Builds/macOS (Mac App Store Xcode project, scheme PuttrCo: co.puttr.ios)
#   Tools/app-build.sh ios        Builds/iOS (Xcode project: open, pick your iPhone, Run)
#   Tools/app-build.sh iossim     Builds/iOS-Simulator (Xcode project for the iPhone Simulator only)
#   Tools/app-build.sh android    Builds/Android/Puttr.apk (also the Google TV / Android TV app; test build, debug key)
#   Tools/app-build.sh androidplay Builds/AndroidPlay/Puttr.aab (Google Play: app bundle, puttr-app's upload key)
#   Tools/app-build.sh tvos       Builds/tvOS (Apple TV Xcode project, scheme PuttrCo)
#   Tools/app-build.sh tvossim    Builds/tvOS-Simulator (Xcode project for the Apple TV simulator only)
#   Tools/app-build.sh tracksart  import the Tracks scans again (after Tools/tracks-art/bake.sh)
#   Tools/app-build.sh webtv      Builds/WebTV (WebGL) + Builds/WebTV-Tizen/Puttr.wgt (Samsung TV app; signing profile $TIZEN_PROFILE, default PuttrTV)
P="$(cd "$(dirname "$0")/.." && pwd)"
U=/Applications/Unity/Hub/Editor/6000.6.8f1/Unity.app/Contents/MacOS/Unity
mkdir -p "$P/Logs"
unity() { # target, method (AppSetup.X, or Class.X), log name
  local method="$2"; [[ "$method" == *.* ]] || method="AppSetup.$method"
  "$U" -batchmode -quit -projectPath "$P" -buildTarget "$1" -executeMethod "PuttrApp.EditorTools.$method" -logFile "$P/Logs/$3.log"
  local code=$?
  grep "error CS" "$P/Logs/$3.log" | sort -u | head -30
  grep "^Puttr 2" "$P/Logs/$3.log" | sort -u
  return $code
}
case "$1" in
  setup)   unity StandaloneOSX Run setup ;;
  mac)     unity StandaloneOSX BuildMac mac ;;
  tracksart) unity StandaloneOSX TracksArtImport.Reimport tracksart ;;
  macstore) unity StandaloneOSX MacBuilds.BuildMacStore macstore ;;
  ios)     unity iOS BuildIos ios ;;
  iossim)  unity iOS BuildIosSimulator iossim ;;
  android) unity Android BuildAndroid android ;;
  androidplay) unity Android BuildAndroidPlay androidplay ;;
  tvos)    unity tvOS TvBuilds.BuildTvos tvos ;;
  tvossim) unity tvOS TvBuilds.BuildTvosSimulator tvossim ;;
  webtv)
    unity WebGL TvBuilds.BuildWebTv webtv || exit $?
    # The Tizen web app: config.xml + icon + the WebGL site, signed and zipped by the Tizen Studio CLI.
    T="$P/Builds/WebTV-Tizen"
    rm -rf "$T"; mkdir -p "$T"
    cp -R "$P/Builds/WebTV/." "$T/"
    cp "$P/Tools/tizen/config.xml" "$P/Tools/tizen/icon.png" "$P/Tools/tizen/.tproject" "$T/"
    TIZEN="${TIZEN_STUDIO:-$HOME/tizen-studio}/tools/ide/bin/tizen"
    if [ -x "$TIZEN" ]; then
      "$TIZEN" package -t wgt -s "${TIZEN_PROFILE:-PuttrTV}" -- "$T" | tail -3
      ls -lh "$T"/*.wgt 2>/dev/null
    else
      echo "Tizen CLI not found at $TIZEN: $T is staged but not packaged"
    fi ;;
  test)
    "$U" -batchmode -projectPath "$P" -buildTarget StandaloneOSX -runTests -testPlatform EditMode -testResults "$P/Logs/tests.xml" -logFile "$P/Logs/test.log"
    code=$?
    grep "error CS" "$P/Logs/test.log" | sort -u | head -30
    grep -o '<test-run [^>]*' "$P/Logs/tests.xml" | grep -o 'total="[0-9]*"\|passed="[0-9]*"\|failed="[0-9]*"' | tr '\n' ' '; echo
    grep -o '<test-case [^>]*result="Failed"[^>]*' "$P/Logs/tests.xml" | grep -o 'fullname="[^"]*"'
    exit $code ;;
  *) sed -n 2,15p "$0"; exit 1 ;;
esac

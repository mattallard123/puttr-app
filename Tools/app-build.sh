#!/bin/bash
# Builds and tests Puttr 2 in batch mode. Close the Unity Editor on this project first.
#
#   Tools/app-build.sh setup      panel settings + scene (run once, and after changing Theme.tss)
#   Tools/app-build.sh test       EditMode tests
#   Tools/app-build.sh mac        Builds/Puttr.app
#   Tools/app-build.sh ios        Builds/iOS (Xcode project: open, pick your iPhone, Run)
#   Tools/app-build.sh iossim     Builds/iOS-Simulator (Xcode project for the iPhone Simulator only)
#   Tools/app-build.sh android    Builds/Android/Puttr.apk
P="$(cd "$(dirname "$0")/.." && pwd)"
U=/Applications/Unity/Hub/Editor/6000.6.8f1/Unity.app/Contents/MacOS/Unity
mkdir -p "$P/Logs"
unity() { # target, method, log name
  "$U" -batchmode -quit -projectPath "$P" -buildTarget "$1" -executeMethod "PuttrApp.EditorTools.AppSetup.$2" -logFile "$P/Logs/$3.log"
  local code=$?
  grep "error CS" "$P/Logs/$3.log" | sort -u | head -30
  grep "^Puttr 2" "$P/Logs/$3.log" | sort -u
  return $code
}
case "$1" in
  setup)   unity StandaloneOSX Run setup ;;
  mac)     unity StandaloneOSX BuildMac mac ;;
  ios)     unity iOS BuildIos ios ;;
  iossim)  unity iOS BuildIosSimulator iossim ;;
  android) unity Android BuildAndroid android ;;
  test)
    "$U" -batchmode -projectPath "$P" -buildTarget StandaloneOSX -runTests -testPlatform EditMode -testResults "$P/Logs/tests.xml" -logFile "$P/Logs/test.log"
    code=$?
    grep "error CS" "$P/Logs/test.log" | sort -u | head -30
    grep -o '<test-run [^>]*' "$P/Logs/tests.xml" | grep -o 'total="[0-9]*"\|passed="[0-9]*"\|failed="[0-9]*"' | tr '\n' ' '; echo
    grep -o '<test-case [^>]*result="Failed"[^>]*' "$P/Logs/tests.xml" | grep -o 'fullname="[^"]*"'
    exit $code ;;
  *) sed -n 2,10p "$0"; exit 1 ;;
esac

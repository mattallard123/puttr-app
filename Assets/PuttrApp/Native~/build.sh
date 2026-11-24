#!/bin/bash
# Builds the Mac app's own plug-ins (Intel and Apple silicon) into Assets/Plugins/macOS.
#   PuttrSignIn.bundle   Sign in with Apple, and the web sign-in sheet Google uses (PuttrSignInMac.m)
set -e
cd "$(dirname "$0")"
OUT=../../Plugins/macOS/PuttrSignIn.bundle
rm -rf "$OUT"
mkdir -p "$OUT/Contents/MacOS"
clang -bundle -fobjc-arc -O2 -Wall -arch arm64 -arch x86_64 -mmacosx-version-min=12.0 \
  -framework AppKit -framework AuthenticationServices -o "$OUT/Contents/MacOS/PuttrSignIn" PuttrSignInMac.m
cat > "$OUT/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleExecutable</key><string>PuttrSignIn</string>
<key>CFBundleIdentifier</key><string>co.puttr.signin</string>
<key>CFBundleName</key><string>PuttrSignIn</string>
<key>CFBundlePackageType</key><string>BNDL</string>
<key>CFBundleVersion</key><string>1</string>
<key>CFBundleShortVersionString</key><string>1.0</string>
</dict></plist>
PLIST
codesign --force -s - "$OUT"
echo "Built $OUT"

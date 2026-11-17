#!/bin/bash
# Builds the macOS Bluetooth plugin (Intel and Apple silicon) into Assets/Plugins/macOS.
set -e
cd "$(dirname "$0")"
OUT=../../Plugins/macOS/PuttrBle.bundle
rm -rf "$OUT"
mkdir -p "$OUT/Contents/MacOS"
clang -bundle -fobjc-arc -O2 -arch arm64 -arch x86_64 -mmacosx-version-min=11.0 \
  -framework Foundation -framework CoreBluetooth -o "$OUT/Contents/MacOS/PuttrBle" PuttrBle.m
cat > "$OUT/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleExecutable</key><string>PuttrBle</string>
<key>CFBundleIdentifier</key><string>co.puttr.ble</string>
<key>CFBundleName</key><string>PuttrBle</string>
<key>CFBundlePackageType</key><string>BNDL</string>
<key>CFBundleVersion</key><string>1</string>
<key>CFBundleShortVersionString</key><string>1.0</string>
</dict></plist>
PLIST
codesign --force -s - "$OUT"
echo "Built $OUT"
# iOS and tvOS compile the same source into the app. One copy, in a folder
# Unity does not treat as belonging to either, so both pick it up once.
mkdir -p ../../Plugins/Apple && cp PuttrBle.m ../../Plugins/Apple/PuttrBle.m

#!/bin/bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
ARCH=$(uname -m)
RID="osx-arm64"
[ "$ARCH" = "x86_64" ] && RID="osx-x64"
echo "Publishing $RID…"
dotnet publish src/MacSpaceCleaner/MacSpaceCleaner.csproj -c Release -r "$RID" --self-contained true \
  -o "dist/$RID" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
APP="$ROOT/dist/MacSpaceCleaner.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "dist/$RID/"* "$APP/Contents/MacOS/"
chmod +x "$APP/Contents/MacOS/MacSpaceCleaner"
cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>MacSpaceCleaner</string>
  <key>CFBundleDisplayName</key><string>MacSpaceCleaner</string>
  <key>CFBundleIdentifier</key><string>com.adrrojek.macspacecleaner</string>
  <key>CFBundleVersion</key><string>1.0.0</string>
  <key>CFBundleShortVersionString</key><string>1.0.0</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleExecutable</key><string>MacSpaceCleaner</string>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST
echo "Built: $APP"
echo "Double-click MacSpaceCleaner.command or open the .app"
open "$APP" || true

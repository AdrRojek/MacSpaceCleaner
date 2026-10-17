#!/bin/bash
cd "$(dirname "$0")"
APP="$(dirname "$0")/dist/MacSpaceCleaner.app"
BIN="$(dirname "$0")/dist/osx-arm64/MacSpaceCleaner"

if [ -d "$APP" ]; then
  open "$APP"
  exit 0
fi

if [ -x "$BIN" ]; then
  exec "$BIN"
fi

echo "App not built yet. Building once (needs .NET 9)…"
export NUGET_PACKAGES="${NUGET_PACKAGES:-$HOME/.nuget/packages}"
export TMPDIR="${TMPDIR:-/tmp}"
ARCH=$(uname -m)
RID="osx-arm64"
[ "$ARCH" = "x86_64" ] && RID="osx-x64"
dotnet publish src/MacSpaceCleaner/MacSpaceCleaner.csproj -c Release -r "$RID" --self-contained true \
  -o "dist/$RID" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
mkdir -p dist/MacSpaceCleaner.app/Contents/MacOS dist/MacSpaceCleaner.app/Contents/Resources
cp -R "dist/$RID/"* dist/MacSpaceCleaner.app/Contents/MacOS/
chmod +x dist/MacSpaceCleaner.app/Contents/MacOS/MacSpaceCleaner
cp "$(dirname "$0")/dist/MacSpaceCleaner.app/Contents/Info.plist" dist/MacSpaceCleaner.app/Contents/Info.plist 2>/dev/null || true
if [ ! -f dist/MacSpaceCleaner.app/Contents/Info.plist ]; then
  cat > dist/MacSpaceCleaner.app/Contents/Info.plist <<'PLIST'
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
fi
open dist/MacSpaceCleaner.app

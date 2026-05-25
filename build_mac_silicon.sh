#!/bin/bash
set -e
APP_NAME="Bulletbox"
IDENTIFIER="com.movies.bulletbox"
EXECUTABLE_NAME="Bulletbox"

# Get the root directory
ROOT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )/.." && pwd )"
RUNTIME="osx-arm64"
PUBLISH_DIR="bin/Release/net10.0/$RUNTIME/publish"

# 1. Compile
echo "🚀 Compiling for macOS Silicon ($RUNTIME - net10.0)..."
rm -rf bin obj
dotnet publish -r "$RUNTIME" -c Release -f net10.0 -p:PublishSingleFile=true -p:SelfContained=true -p:AssemblyName=$APP_NAME -o "$PUBLISH_DIR"

# 2. Build Structure
rm -rf "$APP_NAME.app"
mkdir -p "$APP_NAME.app/Contents/MacOS"
mkdir -p "$APP_NAME.app/Contents/Resources"

# 3. Copy everything
cp -r "$PUBLISH_DIR/"* "$APP_NAME.app/Contents/MacOS/"

# Fix the executable name mismatch
if [ -f "$APP_NAME.app/Contents/MacOS/$EXECUTABLE_NAME" ] && [ "$EXECUTABLE_NAME" != "$APP_NAME" ]; then
    mv "$APP_NAME.app/Contents/MacOS/$EXECUTABLE_NAME" "$APP_NAME.app/Contents/MacOS/$APP_NAME"
fi
chmod +x "$APP_NAME.app/Contents/MacOS/$APP_NAME"

# 4. CRITICAL FIX: Ad-hoc sign the executable and native .dylib files
# Without this, Apple Silicon will refuse to execute the binary and crash immediately.
echo "🔐 Ad-hoc signing binaries for Apple Silicon..."
find "$APP_NAME.app/Contents/MacOS" -type f \( -name "$APP_NAME" -o -name "*.dylib" \) -exec codesign --force --deep --sign - {} \;

# 5. Copy Assets
cp -r "$ROOT_DIR/fonts" "$APP_NAME.app/Contents/Resources/" 2>/dev/null || cp -r fonts "$APP_NAME.app/Contents/Resources/" 2>/dev/null || :
cp -r "$ROOT_DIR/img" "$APP_NAME.app/Contents/Resources/" 2>/dev/null || cp -r img "$APP_NAME.app/Contents/Resources/" 2>/dev/null || :

if [ -d "$ROOT_DIR/resources" ]; then
    cp -r "$ROOT_DIR/resources" "$APP_NAME.app/Contents/Resources/"
elif [ -d "resources" ]; then
    cp -r resources "$APP_NAME.app/Contents/Resources/"
else
    echo "⚠️ Warning: resources folder not found!"
fi

cp "$ROOT_DIR/icons.icns" "$APP_NAME.app/Contents/Resources/" 2>/dev/null || cp icons.icns "$APP_NAME.app/Contents/Resources/" 2>/dev/null || :

# 6. Create Info.plist
cat > "$APP_NAME.app/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleExecutable</key>
    <string>Bulletbox</string>
    <key>CFBundleIdentifier</key>
    <string>$IDENTIFIER</string>
    <key>CFBundleName</key>
    <string>Bulletbox</string>
    <key>CFBundleIconFile</key>
    <string>icons.icns</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>
EOF

touch "$APP_NAME.app"
echo "📦 Creating Silicon Disk Image (.dmg)..."
VOL_NAME="Bulletbox Silicon"
# Force unmount existing volume if it got stuck
hdiutil detach -force "/Volumes/$VOL_NAME" 2>/dev/null || :
rm -rf "dmg_root_silicon"
mkdir -p "dmg_root_silicon"

cp -R "$APP_NAME.app" "dmg_root_silicon/"

# Stripping Mac metadata (.DS_Store files, etc.) so it's a completely clean package
xattr -rc "dmg_root_silicon"
find "dmg_root_silicon" -name ".DS_Store" -depth -exec rm {} \; 2>/dev/null || :

ln -s /Applications "dmg_root_silicon/Applications"

# Staging method to bypass "No space left on device" bug
hdiutil create -size 500m -fs HFS+ -volname "$VOL_NAME" -ov "staging_silicon.dmg"
DEVICE=$(hdiutil attach -nobrowse "staging_silicon.dmg" | grep '/Volumes' | awk '{print $1}')
cp -R "dmg_root_silicon/"* "/Volumes/$VOL_NAME/"
hdiutil detach "$DEVICE"

hdiutil convert "staging_silicon.dmg" -format UDZO -o "${APP_NAME}_Silicon.dmg" -ov
rm "staging_silicon.dmg"
rm -rf "dmg_root_silicon"

echo "✅ Done! ${APP_NAME}_Silicon.dmg created."
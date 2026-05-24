#!/bin/bash
set -e
APP_NAME="Bulletbox"
IDENTIFIER="com.movies.bulletbox"
EXECUTABLE_NAME="Bulletbox"

# Get the root directory (one level up from BulletboxClient)
ROOT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )/.." && pwd )"
PUBLISH_DIR="bin/Release/net10.0/osx-x64/publish"

# 1. Compile
echo "🚀 Compiling for macOS Intel (net10.0)..."
rm -rf bin obj
dotnet publish -r osx-x64 -c Release -f net10.0 -p:PublishSingleFile=true -p:SelfContained=true -p:AssemblyName=$APP_NAME -o "$PUBLISH_DIR"

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

# 4. Create Info.plist (This is required for the icon to work)
cat > "$APP_NAME.app/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleExecutable</key>
    <string>$APP_NAME</string>
    <key>CFBundleIdentifier</key>
    <string>$IDENTIFIER</string>
    <key>CFBundleName</key>
    <string>$APP_NAME</string>
    <key>CFBundleIconFile</key>
    <string>icons.icns</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>
EOF

# 5. Refresh the bundle to force macOS to update the icon
touch "$APP_NAME.app"

# 6. Create DMG
echo "📦 Creating Disk Image (.dmg)..."
# Force unmount existing volume if it got stuck
hdiutil detach -force "/Volumes/$APP_NAME" 2>/dev/null || :
rm -rf "dmg_root"
mkdir -p "dmg_root"

cp -R "$APP_NAME.app" "dmg_root/"
xattr -rc "dmg_root"
ln -s /Applications "dmg_root/Applications"

# Create a temporary disk image with a fixed size (500MB is safe for Bulletbox) 
# to avoid the auto-calculation "No space left on device" bug.
hdiutil create -size 500m -fs HFS+ -volname "$APP_NAME" -ov "staging.dmg"

# Mount it, copy files manually, then detach
DEVICE=$(hdiutil attach -nobrowse "staging.dmg" | grep '/Volumes' | awk '{print $1}')
cp -R "dmg_root/"* "/Volumes/$APP_NAME/"
hdiutil detach "$DEVICE"

# Convert the staging DMG into the final compressed distribution DMG
hdiutil convert "staging.dmg" -format UDZO -o "$APP_NAME.dmg" -ov
rm "staging.dmg"
rm -rf "dmg_root"

echo "✅ Done! $APP_NAME.dmg is ready for distribution."
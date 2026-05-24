#!/bin/bash
set -e
APP_NAME="Bulletbox_Windows"

# Get the root directory
ROOT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )/.." && pwd )"
RUNTIME="win-x64"
PUBLISH_DIR="bin/Release/net10.0/$RUNTIME/publish"
BUILD_DIR="build_win"

# 1. Compile
echo "🚀 Compiling for Windows ($RUNTIME - net10.0)..."
rm -rf bin obj
dotnet publish -r "$RUNTIME" -c Release -f net10.0 -p:PublishSingleFile=true -p:SelfContained=true -p:AssemblyName=Bulletbox -o "$PUBLISH_DIR"

# 2. Package
rm -rf "$BUILD_DIR"
mkdir -p "$BUILD_DIR"
cp "$PUBLISH_DIR/Bulletbox.exe" "$BUILD_DIR/Bulletbox.exe"
cp -r "$ROOT_DIR/resources" "$BUILD_DIR/" 2>/dev/null || cp -r resources "$BUILD_DIR/" 2>/dev/null || :
cp -r "$ROOT_DIR/fonts" "$BUILD_DIR/" 2>/dev/null || cp -r fonts "$BUILD_DIR/" 2>/dev/null || :
cp -r "$ROOT_DIR/img" "$BUILD_DIR/" 2>/dev/null || cp -r img "$BUILD_DIR/" 2>/dev/null || :

echo "📦 Zipping for Windows..."
zip -r "$APP_NAME.zip" "$BUILD_DIR"

# Cleanup
rm -rf "$BUILD_DIR"

echo "✅ Done! $APP_NAME.zip is ready."
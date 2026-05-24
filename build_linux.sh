#!/bin/bash
set -e
APP_NAME="Bulletbox_Linux"

# Get the root directory
ROOT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )/.." && pwd )"
RUNTIME="linux-x64"
PUBLISH_DIR="bin/Release/net10.0/$RUNTIME/publish"
BUILD_DIR="build_linux"

# 1. Compile
echo "🚀 Compiling for Linux ($RUNTIME - net10.0)..."
rm -rf bin obj
dotnet publish -r "$RUNTIME" -c Release -f net10.0 -p:PublishSingleFile=true -p:SelfContained=true -p:AssemblyName=Bulletbox -o "$PUBLISH_DIR"

# 2. Package
rm -rf "$BUILD_DIR"
mkdir -p "$BUILD_DIR"
cp "$PUBLISH_DIR/Bulletbox" "$BUILD_DIR/Bulletbox"
chmod +x "$BUILD_DIR/Bulletbox"
cp -r "$ROOT_DIR/resources" "$BUILD_DIR/" 2>/dev/null || cp -r resources "$BUILD_DIR/" 2>/dev/null || :
cp -r "$ROOT_DIR/fonts" "$BUILD_DIR/" 2>/dev/null || cp -r fonts "$BUILD_DIR/" 2>/dev/null || :
cp -r "$ROOT_DIR/img" "$BUILD_DIR/" 2>/dev/null || cp -r img "$BUILD_DIR/" 2>/dev/null || :

echo "📦 Compressing for Linux..."
tar -czf "$APP_NAME.tar.gz" "$BUILD_DIR"

# Cleanup
rm -rf "$BUILD_DIR"

echo "✅ Done! $APP_NAME.tar.gz is ready."
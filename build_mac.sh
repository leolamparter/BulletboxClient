#!/bin/bash
APP_NAME="Bulletbox"
PUBLISH_DIR="bin/Release/net10.0/osx-x64/publish"

# 1. Compile
dotnet publish -r osx-x64 -c Release /p:PublishSingleFile=true /p:SelfContained=true

# 2. Build Structure
mkdir -p "$APP_NAME.app/Contents/MacOS"
mkdir -p "$APP_NAME.app/Contents/Resources"

# 3. Copy everything
cp -r $PUBLISH_DIR/* "$APP_NAME.app/Contents/MacOS/"
cp -r fonts "$APP_NAME.app/Contents/MacOS/"
cp -r img "$APP_NAME.app/Contents/MacOS/"
cp icons.icns "$APP_NAME.app/Contents/Resources/"

echo "Done! $APP_NAME.app is ready."
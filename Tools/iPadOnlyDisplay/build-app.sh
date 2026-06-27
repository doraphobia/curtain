#!/bin/zsh

set -euo pipefail

SCRIPT_DIR="${0:A:h}"
APP_NAME="iPadOnlyDisplay"
APP_DIR="$SCRIPT_DIR/dist/$APP_NAME.app"
BUILD_DIR="$SCRIPT_DIR/.build/local"
MODULE_CACHE="$SCRIPT_DIR/.build/ModuleCache"
ARCHITECTURE="$(uname -m)"

cd "$SCRIPT_DIR"
mkdir -p "$BUILD_DIR" "$MODULE_CACHE"

xcrun swiftc \
    -parse-as-library \
    -O \
    -target "$ARCHITECTURE-apple-macosx13.0" \
    -module-cache-path "$MODULE_CACHE" \
    -framework AppKit \
    -framework Carbon \
    -framework CoreGraphics \
    "$SCRIPT_DIR/Sources/iPadOnlyDisplay/iPadOnlyDisplayApp.swift" \
    -o "$BUILD_DIR/$APP_NAME"

mkdir -p "$APP_DIR/Contents/MacOS"
cp "$BUILD_DIR/$APP_NAME" "$APP_DIR/Contents/MacOS/$APP_NAME"
cp "$SCRIPT_DIR/Resources/Info.plist" "$APP_DIR/Contents/Info.plist"

xattr -cr "$APP_DIR"
codesign --force --deep --sign - "$APP_DIR"

echo "$APP_DIR"

#!/usr/bin/env bash

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BUILD_ARCH="${BUILD_ARCH:-$(uname -m)}"
ARCH_LABEL="${ARCH_LABEL:-$BUILD_ARCH}"
DIST_DIR="$ROOT_DIR/dist"
APP_DIR="$DIST_DIR/ZhuoDazi-macOS-$ARCH_LABEL.app"
CONTENTS_DIR="$APP_DIR/Contents"

swift build --package-path "$ROOT_DIR" --configuration release --arch "$BUILD_ARCH"
BIN_DIR="$(swift build --package-path "$ROOT_DIR" --configuration release --arch "$BUILD_ARCH" --show-bin-path)"

rm -rf "$APP_DIR"
mkdir -p "$CONTENTS_DIR/MacOS" "$CONTENTS_DIR/Resources"
cp "$BIN_DIR/ZhuoDaziMac" "$CONTENTS_DIR/MacOS/ZhuoDazi"
cp "$ROOT_DIR/Info.plist" "$CONTENTS_DIR/Info.plist"
cp -R "$ROOT_DIR/Resources/." "$CONTENTS_DIR/Resources/"
chmod +x "$CONTENTS_DIR/MacOS/ZhuoDazi"

codesign --force --deep --sign - --timestamp=none "$APP_DIR"
codesign --verify --deep --strict "$APP_DIR"

ARCHIVE_PATH="$DIST_DIR/ZhuoDazi-macOS-$ARCH_LABEL.zip"
rm -f "$ARCHIVE_PATH"
ditto -c -k --sequesterRsrc --keepParent "$APP_DIR" "$ARCHIVE_PATH"

echo "Application: $APP_DIR"
echo "Archive: $ARCHIVE_PATH"

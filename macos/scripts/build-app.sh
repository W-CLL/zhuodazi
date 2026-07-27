#!/usr/bin/env bash

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BUILD_ARCH="${BUILD_ARCH:-$(uname -m)}"
ARCH_LABEL="${ARCH_LABEL:-$BUILD_ARCH}"
APP_VERSION="${APP_VERSION:-$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$ROOT_DIR/Info.plist")}"
if [[ ! "$APP_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "Version must use MAJOR.MINOR.PATCH format, received '$APP_VERSION'." >&2
    exit 1
fi
DIST_DIR="$ROOT_DIR/dist"
APP_DIR="$DIST_DIR/ZhuoDazi-macOS-$APP_VERSION-$ARCH_LABEL.app"
CONTENTS_DIR="$APP_DIR/Contents"

swift build --package-path "$ROOT_DIR" --configuration release --arch "$BUILD_ARCH"
BIN_DIR="$(swift build --package-path "$ROOT_DIR" --configuration release --arch "$BUILD_ARCH" --show-bin-path)"

rm -rf "$APP_DIR"
mkdir -p "$CONTENTS_DIR/MacOS" "$CONTENTS_DIR/Resources"
cp "$BIN_DIR/ZhuoDaziMac" "$CONTENTS_DIR/MacOS/ZhuoDazi"
cp "$ROOT_DIR/Info.plist" "$CONTENTS_DIR/Info.plist"
cp -R "$ROOT_DIR/Resources/." "$CONTENTS_DIR/Resources/"
ICON_WORK_DIR="$(mktemp -d "$DIST_DIR/app-icon.XXXXXX")"
ICONSET_DIR="$ICON_WORK_DIR/AppIcon.iconset"
mkdir -p "$ICONSET_DIR"
trap 'rm -rf "$ICON_WORK_DIR"' EXIT
for size in 16 32 128 256 512; do
    sips -z "$size" "$size" "$ROOT_DIR/../windows/assets/app-icon.png" --out "$ICONSET_DIR/icon_${size}x${size}.png" >/dev/null
    sips -z "$((size * 2))" "$((size * 2))" "$ROOT_DIR/../windows/assets/app-icon.png" --out "$ICONSET_DIR/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET_DIR" -o "$CONTENTS_DIR/Resources/AppIcon.icns"
mkdir -p "$CONTENTS_DIR/Resources/Pets/yuexinmiao"
cp "$ROOT_DIR/../windows/assets/pet-libraries/yuexinmiao/"*.gif \
    "$CONTENTS_DIR/Resources/Pets/yuexinmiao/"
chmod +x "$CONTENTS_DIR/MacOS/ZhuoDazi"

codesign --force --deep --sign - --timestamp=none "$APP_DIR"
codesign --verify --deep --strict "$APP_DIR"

ARCHIVE_PATH="$DIST_DIR/ZhuoDazi-macOS-$APP_VERSION-$ARCH_LABEL.zip"
rm -f "$ARCHIVE_PATH"
ditto -c -k --sequesterRsrc --keepParent "$APP_DIR" "$ARCHIVE_PATH"

echo "Application: $APP_DIR"
echo "Archive: $ARCHIVE_PATH"

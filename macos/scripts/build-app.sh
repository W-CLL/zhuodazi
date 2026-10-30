#!/usr/bin/env bash

set -euo pipefail

if [[ "$(uname -s)" != "Darwin" ]]; then
    echo "This packaging script requires macOS." >&2
    exit 1
fi

for required_tool in xcrun swift sips iconutil codesign ditto; do
    if ! command -v "$required_tool" >/dev/null 2>&1; then
        echo "Required macOS build tool is missing: $required_tool" >&2
        exit 1
    fi
done
if ! xcrun --find swift >/dev/null 2>&1; then
    echo "Xcode Command Line Tools cannot locate Swift." >&2
    exit 1
fi
if [[ ! -x /usr/libexec/PlistBuddy ]]; then
    echo "Required macOS build tool is missing: /usr/libexec/PlistBuddy" >&2
    exit 1
fi

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BUILD_ARCH="${BUILD_ARCH:-$(uname -m)}"
ARCH_LABEL="${ARCH_LABEL:-$BUILD_ARCH}"
case "$BUILD_ARCH" in
    arm64|x86_64) ;;
    *)
        echo "BUILD_ARCH must be arm64 or x86_64, received '$BUILD_ARCH'." >&2
        exit 1
        ;;
esac
if [[ ! "$ARCH_LABEL" =~ ^[A-Za-z0-9][A-Za-z0-9._-]{0,31}$ ]] || [[ "$ARCH_LABEL" == "." || "$ARCH_LABEL" == ".." ]]; then
    echo "ARCH_LABEL must be one safe path segment, received '$ARCH_LABEL'." >&2
    exit 1
fi
APP_VERSION="${APP_VERSION:-$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$ROOT_DIR/Info.plist")}"
if [[ ! "$APP_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "Version must use MAJOR.MINOR.PATCH format, received '$APP_VERSION'." >&2
    exit 1
fi
DIST_DIR="$ROOT_DIR/dist"
APP_DIR="$DIST_DIR/ZhuoDazi-macOS-$APP_VERSION-$ARCH_LABEL.app"
CONTENTS_DIR="$APP_DIR/Contents"
case "$APP_DIR" in
    "$DIST_DIR"/ZhuoDazi-macOS-*.app) ;;
    *)
        echo "Refusing unsafe application output path: $APP_DIR" >&2
        exit 1
        ;;
esac

swift build --package-path "$ROOT_DIR" --configuration release --arch "$BUILD_ARCH"
BIN_DIR="$(swift build --package-path "$ROOT_DIR" --configuration release --arch "$BUILD_ARCH" --show-bin-path)"

rm -rf "$APP_DIR"
mkdir -p "$CONTENTS_DIR/MacOS" "$CONTENTS_DIR/Helpers" "$CONTENTS_DIR/Resources"
cp "$BIN_DIR/ZhuoDaziMac" "$CONTENTS_DIR/MacOS/ZhuoDazi"
cp "$BIN_DIR/ZhuoDaziUninstallHelper" "$CONTENTS_DIR/Helpers/ZhuoDaziUninstallHelper"
cp "$ROOT_DIR/Info.plist" "$CONTENTS_DIR/Info.plist"
# Keep legacy Pets files as development fixtures, outside the shipped app.
for resource in "$ROOT_DIR/Resources/"*; do
    [[ -e "$resource" ]] || continue
    [[ "$(basename "$resource")" == "Pets" ]] && continue
    cp -R "$resource" "$CONTENTS_DIR/Resources/"
done
cp "$ROOT_DIR/../windows/assets/contact-author-wechat.png" \
    "$CONTENTS_DIR/Resources/contact-author-wechat.png"
ICON_WORK_DIR="$(mktemp -d "$DIST_DIR/app-icon.XXXXXX")"
ICONSET_DIR="$ICON_WORK_DIR/AppIcon.iconset"
mkdir -p "$ICONSET_DIR"
trap 'rm -rf "$ICON_WORK_DIR"' EXIT
for size in 16 32 128 256 512; do
    sips -z "$size" "$size" "$ROOT_DIR/../windows/assets/app-icon.png" --out "$ICONSET_DIR/icon_${size}x${size}.png" >/dev/null
    sips -z "$((size * 2))" "$((size * 2))" "$ROOT_DIR/../windows/assets/app-icon.png" --out "$ICONSET_DIR/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET_DIR" -o "$CONTENTS_DIR/Resources/AppIcon.icns"
mkdir -p "$CONTENTS_DIR/Resources/Pets/default"
cp "$ROOT_DIR/../assets/default-pets/"*.gif \
    "$CONTENTS_DIR/Resources/Pets/default/"
chmod +x "$CONTENTS_DIR/MacOS/ZhuoDazi"
chmod +x "$CONTENTS_DIR/Helpers/ZhuoDaziUninstallHelper"

codesign --force --sign - --timestamp=none "$CONTENTS_DIR/Helpers/ZhuoDaziUninstallHelper"
codesign --force --deep --sign - --timestamp=none "$APP_DIR"
codesign --verify --deep --strict "$APP_DIR"

ARCHIVE_PATH="$DIST_DIR/ZhuoDazi-macOS-$APP_VERSION-$ARCH_LABEL.zip"
rm -f "$ARCHIVE_PATH"
ditto -c -k --sequesterRsrc --keepParent "$APP_DIR" "$ARCHIVE_PATH"

echo "Application: $APP_DIR"
echo "Archive: $ARCHIVE_PATH"

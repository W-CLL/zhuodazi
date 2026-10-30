#!/usr/bin/env bash

set -euo pipefail

if [[ "$(uname -s)" != "Darwin" ]]; then
    printf '%s\n' 'Package verification requires macOS.' >&2
    exit 1
fi

mac_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
package_arch="${BUILD_ARCH:-$(uname -m)}"
package_label="${ARCH_LABEL:-$package_arch}"
case "$package_arch" in
    arm64|x86_64) ;;
    *) printf '%s\n' 'BUILD_ARCH must be arm64 or x86_64.' >&2; exit 1 ;;
esac
if [[ ! "$package_label" =~ ^[A-Za-z0-9][A-Za-z0-9._-]{0,31}$ ]]; then
    printf '%s\n' 'ARCH_LABEL must be one safe path segment.' >&2
    exit 1
fi
package_version="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$mac_root/Info.plist")"
if [[ ! "$package_version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    printf '%s\n' 'The application version is invalid.' >&2
    exit 1
fi
package_name="ZhuoDazi-macOS-$package_version-$package_label.app"
package_app="$mac_root/dist/$package_name"
package_archive="$mac_root/dist/${package_name%.app}.zip"

verify_bundle() {
    local bundle_path="$1"
    plutil -lint "$bundle_path/Contents/Info.plist"
    test "$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$bundle_path/Contents/Info.plist")" = 'com.zhuodazi.desktop-pet'
    test "$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$bundle_path/Contents/Info.plist")" = "$package_version"
    for executable in 'MacOS/ZhuoDazi' 'Helpers/ZhuoDaziUninstallHelper'; do
        local binary="$bundle_path/Contents/$executable"
        test -f "$binary"
        test -x "$binary"
        test ! -L "$binary"
        test "$(xcrun lipo -archs "$binary")" = "$package_arch"
        file "$binary"
        codesign --verify --strict "$binary"
    done
    codesign --verify --deep --strict "$bundle_path"
    python3 "$mac_root/../scripts/check-default-pets.py" --root "$bundle_path/Contents/Resources/Pets/default"
    test ! -d "$bundle_path/Contents/Resources/Pets/yuexinmiao"
}

verify_bundle "$package_app"
test -s "$package_archive"
package_check_tmp="$(mktemp -d "${TMPDIR:-/tmp}/zhuodazi-package-check.XXXXXX")"
trap 'rm -rf -- "$package_check_tmp"' EXIT
ditto -x -k "$package_archive" "$package_check_tmp"
verify_bundle "$package_check_tmp/$package_name"
cmp "$package_app/Contents/MacOS/ZhuoDazi" "$package_check_tmp/$package_name/Contents/MacOS/ZhuoDazi"
cmp "$package_app/Contents/Helpers/ZhuoDaziUninstallHelper" "$package_check_tmp/$package_name/Contents/Helpers/ZhuoDaziUninstallHelper"

printf 'Verified macOS %s package and uninstall helper: %s\n' "$package_arch" "$package_archive"

from pathlib import Path
import plistlib
import re
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]


def read_windows_version():
    project = ET.parse(ROOT / "windows" / "ZhuoDazi" / "ZhuoDazi.csproj").getroot()
    version = project.findtext("./PropertyGroup/Version", "").strip()
    file_version = project.findtext("./PropertyGroup/FileVersion", "").strip()
    if file_version != f"{version}.0":
        raise SystemExit(f"Windows FileVersion mismatch: {file_version!r} != {version}.0")
    return version


def read_macos_version():
    with (ROOT / "macos" / "Info.plist").open("rb") as stream:
        metadata = plistlib.load(stream)
    short = str(metadata.get("CFBundleShortVersionString", "")).strip()
    build = str(metadata.get("CFBundleVersion", "")).strip()
    if build != short:
        raise SystemExit(f"macOS CFBundleVersion mismatch: {build!r} != {short!r}")
    return short


def main():
    windows = read_windows_version()
    macos = read_macos_version()
    if not re.fullmatch(r"\d+\.\d+\.\d+", windows + ""):
        raise SystemExit(f"Invalid Windows version: {windows!r}")
    if not re.fullmatch(r"\d+\.\d+\.\d+", macos + ""):
        raise SystemExit(f"Invalid macOS version: {macos!r}")

    download = (ROOT / "website" / "themes" / "deskpet" / "layout" / "download.ejs").read_text(encoding="utf-8")
    index = (ROOT / "website" / "themes" / "deskpet" / "layout" / "index.ejs").read_text(encoding="utf-8")
    expected = {
        "Windows": windows,
        "macOS": macos,
    }
    for label, version in expected.items():
        if version not in download or version not in index:
            raise SystemExit(f"Website does not contain current {label} version {version}")
    print(f"release consistency ok: Windows {windows}, macOS {macos}")


if __name__ == "__main__":
    main()

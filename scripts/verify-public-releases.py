import argparse
import json
from urllib.parse import urljoin
from urllib.request import Request, urlopen


TARGETS = (
    ("windows", "x64"),
    ("macos", "arm64"),
    ("macos", "x86_64"),
)


def read_json(url):
    request = Request(url, headers={"User-Agent": "ZhuoDazi-release-verifier/1"})
    with urlopen(request, timeout=15) as response:
        if response.status != 200:
            raise RuntimeError(f"{url} returned HTTP {response.status}")
        return json.load(response)


def verify_download(url):
    request = Request(url, headers={
        "Range": "bytes=0-0",
        "User-Agent": "ZhuoDazi-release-verifier/1",
    })
    with urlopen(request, timeout=30) as response:
        if response.status not in (200, 206):
            raise RuntimeError(f"{url} returned HTTP {response.status}")
        response.read(1)


def main():
    parser = argparse.ArgumentParser(description="Verify public DeskPet release metadata and downloads.")
    parser.add_argument("--base-url", default="https://in.desktoppet.online")
    parser.add_argument("--windows-version", required=True)
    parser.add_argument("--macos-version", required=True)
    args = parser.parse_args()

    base_url = args.base_url.rstrip("/") + "/"
    payload = read_json(urljoin(base_url, "api/public/downloads"))
    releases = {
        (item.get("platform"), item.get("architecture")): item
        for item in payload.get("downloads", [])
    }
    for platform, architecture in TARGETS:
        expected = args.windows_version if platform == "windows" else args.macos_version
        release = releases.get((platform, architecture))
        if not release:
            raise SystemExit(f"Missing public release for {platform}/{architecture}")
        if release.get("version") != expected:
            raise SystemExit(
                f"Version mismatch for {platform}/{architecture}: "
                f"{release.get('version')!r} != {expected!r}"
            )
        verify_download(urljoin(base_url, f"downloads/latest/{platform}/{architecture}"))
        print(f"ok: {platform}/{architecture} {expected}")


if __name__ == "__main__":
    main()

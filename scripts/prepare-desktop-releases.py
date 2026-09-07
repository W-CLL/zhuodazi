import json
import os
from pathlib import Path
import subprocess


ROOT = Path(__file__).resolve().parents[1]
NOTES = "更新/修复了一些功能"


def read_release(tag):
    result = subprocess.run(
        ["gh", "release", "view", tag, "--json", "isDraft,assets,tagName"],
        cwd=ROOT, capture_output=True, text=True, encoding="utf-8",
    )
    if result.returncode:
        if result.stderr.strip() == "release not found":
            return None
        raise RuntimeError(f"Unable to inspect {tag}: {result.stderr.strip()}")
    return json.loads(result.stdout)


def expected_assets(platform, version):
    if platform == "windows":
        return {
            f"ZhuoDazi-Desktop-Pet-{version}.exe",
            f"ZhuoDazi-Desktop-Pet-{version}-portable.zip",
        }
    return {
        f"ZhuoDazi-macOS-{version}-arm64.zip",
        f"ZhuoDazi-macOS-{version}-x86_64.zip",
    }


def changed_files(platform, tag, target):
    paths = ["windows"] if platform == "windows" else ["macos", "windows/assets"]
    result = subprocess.run(
        ["git", "diff", "--name-only", tag, target, "--", *paths],
        cwd=ROOT, capture_output=True, text=True, encoding="utf-8", check=True,
    )
    return result.stdout.strip()


def plan_release(platform, version, target):
    tag = f"v{version}" if platform == "windows" else f"macos-v{version}"
    release = read_release(tag)
    assets = expected_assets(platform, version)
    if platform == "macos" and release is None:
        legacy_tag = f"v{version}"
        legacy = read_release(legacy_tag)
        if legacy is not None and not legacy["isDraft"]:
            legacy_assets = {asset["name"] for asset in legacy["assets"]}
            if assets <= legacy_assets:
                tag, release = legacy_tag, legacy
    published = release is not None and not release["isDraft"]
    if published:
        missing = assets - {asset["name"] for asset in release["assets"]}
        if missing:
            raise RuntimeError(f"Published release {tag} is missing packages: {sorted(missing)}")
        changes = changed_files(platform, tag, target)
        if changes:
            raise RuntimeError(
                f"Release {tag} is already published and {platform} files changed; "
                f"bump the {platform} version before rebuilding.\n{changes}"
            )
    return {
        "platform": platform,
        "version": version,
        "tag": tag,
        "published": published,
        "exists": release is not None,
    }


def prepare_release(plan, target):
    if plan["published"]:
        print(f"{plan['tag']} already contains the same {plan['platform']} files; skipping packages.")
        return
    command = ["gh", "release", "edit" if plan["exists"] else "create", plan["tag"]]
    if not plan["exists"]:
        command.extend(["--draft", "--latest=false"])
    name = "Windows" if plan["platform"] == "windows" else "macOS"
    command.extend([
        "--target", target,
        "--title", f"桌搭子 {name} {plan['version']}",
        "--notes", NOTES,
    ])
    subprocess.run(command, cwd=ROOT, check=True)


def main():
    target = os.environ["TARGET_SHA"]
    plans = [
        plan_release(platform, os.environ[f"{platform.upper()}_VERSION"], target)
        for platform in ("windows", "macos")
    ]
    for plan in plans:
        prepare_release(plan, target)
    with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
        for plan in plans:
            output.write(f"{plan['platform']}_published={str(plan['published']).lower()}\n")
            output.write(f"{plan['platform']}_tag={plan['tag']}\n")


if __name__ == "__main__":
    main()

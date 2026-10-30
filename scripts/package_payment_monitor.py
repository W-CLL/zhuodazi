"""Create a private, independent Windows copy. Never alters original wxlocal."""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import shutil
import sqlite3
import subprocess
import sys


def private_directory(path):
    path.mkdir(parents=True, exist_ok=True)
    if os.name == "nt":
        identity = os.environ["USERDOMAIN"] + "\\" + os.environ["USERNAME"]
        subprocess.run(["icacls", str(path), "/inheritance:r", "/grant:r",
                        identity + ":(OI)(CI)F", "*S-1-5-18:(OI)(CI)F"],
                       check=True, stdout=subprocess.DEVNULL)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--config", type=Path, required=True)
    parser.add_argument("--destination", type=Path, required=True)
    parser.add_argument("--state-db", type=Path)
    args = parser.parse_args()
    source, destination = args.source.resolve(), args.destination.resolve()
    if destination.exists() or source in destination.parents or destination == source:
        parser.error("destination must be a new independent directory")
    config = json.loads(args.config.read_text(encoding="utf-8-sig"))
    if config.get("mode", "local") != "local" or not config["baseUrl"].startswith("http://127.0.0.1:"):
        parser.error("packaging only migrates an existing local profile")
    runtime_config = dict(line.split(" = ", 1) for line in (source / ".venv/pyvenv.cfg").read_text().splitlines() if " = " in line)
    python_root = Path(runtime_config["home"])
    if not (python_root / "python.exe").is_file():
        parser.error("existing Python runtime not found")
    destination.mkdir(parents=True)
    private_directory(destination / "config")
    private_directory(destination / "state")
    ignore = shutil.ignore_patterns("__pycache__", "*.pyc", ".git", "site-packages")
    shutil.copytree(python_root, destination / "runtime/python", ignore=ignore)
    dependencies = destination / "runtime/python/Lib/site-packages"
    dependencies.mkdir(parents=True, exist_ok=True)
    available = source / ".venv/Lib/site-packages"
    packages = {"apsw", "Crypto", "psutil", "zstandard"}
    for item in available.iterdir():
        if item.name in packages or (item.name.endswith(".dist-info") and item.name.split("-")[0] in {"apsw", "pycryptodome", "psutil", "zstandard"}):
            shutil.copytree(item, dependencies / item.name, ignore=shutil.ignore_patterns("__pycache__", "*.pyc"))
    sdk = destination / "sdk/wxlocal"
    shutil.copytree(source / "wxlocal", sdk / "wxlocal", ignore=shutil.ignore_patterns("__pycache__", "*.pyc"))
    shutil.copytree(source / "licenses", sdk / "licenses")
    for name in ("pyproject.toml", "README.md", "LICENSE"):
        if (source / name).is_file():
            shutil.copy2(source / name, sdk / name)
    scripts = destination / "scripts"
    scripts.mkdir()
    for name in ("local_market_payments.py", "payment_console.py"):
        shutil.copy2(Path(__file__).with_name(name), scripts / name)
    shutil.copy2(Path(__file__).with_name("start-payments.bat"), destination / "start-payments.bat")
    shutil.copy2(Path(__file__).parent.parent / "docs/payment-monitor-standalone.md", destination / "README.md")
    shutil.copy2(config["keysFile"], destination / "config/wechat-keys.json")
    config.update(wxlocalRoot=str(sdk), keysFile=str(destination / "config/wechat-keys.json"), mode="local")
    (destination / "config/local.json").write_text(json.dumps(config, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    example = dict(config, baseUrl="https://in.desktoppet.online", mode="production", bridgeToken="REPLACE_WITH_SEPARATE_PRODUCTION_TOKEN")
    (destination / "config/production.example.json").write_text(json.dumps(example, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    local = destination / "state/local"
    local.mkdir()
    if args.state_db:
        # SQLite backup includes WAL safely. Original files and queues remain intact.
        with sqlite3.connect(args.state_db.resolve().as_uri() + "?mode=ro", uri=True) as old:
            with sqlite3.connect(local / "bridge.sqlite3") as new:
                old.backup(new)
    print("Standalone monitor created. Private configuration copied; original source unchanged.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

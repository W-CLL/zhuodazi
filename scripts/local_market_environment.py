"""Prepare isolated local payment-test configuration without exposing credentials.

Uses existing wxlocal keys. Account discovery reads process mapping names only;
this helper never scans process memory for keys or queries message contents.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import secrets
import sys


def read_json(path: Path) -> dict:
    value = json.loads(path.read_text(encoding="utf-8-sig"))
    if not isinstance(value, dict):
        raise ValueError("Configuration must be a JSON object.")
    return value


def save_json(path: Path, value: dict) -> None:
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    temporary.replace(path)


def resolve_account(explicit: str | None) -> Path:
    if explicit:
        result = Path(explicit).resolve(strict=True)
        if not (result / "db_storage").is_dir():
            raise ValueError("The selected account has no database directory.")
        return result
    import psutil
    from wxlocal.core.process_accounts import inspect_binding

    accounts: dict[str, Path] = {}
    for process in psutil.process_iter(["pid", "name", "cmdline"]):
        if (process.info.get("name") or "").casefold() not in {"weixin.exe", "wechat.exe"}:
            continue
        if any(str(arg).startswith("--type") for arg in (process.info.get("cmdline") or [])):
            continue
        try:
            binding = inspect_binding(process.pid)
        except RuntimeError:
            continue
        accounts[str(binding.account_dir).casefold()] = binding.account_dir
    if len(accounts) != 1:
        raise ValueError("Cannot identify exactly one running WeChat account. Supply --account-dir explicitly.")
    return next(iter(accounts.values()))


def choose_keys(root: Path, account: Path, explicit: str | None) -> Path:
    from wxlocal import KeyStore
    from wxlocal.core.shards import discover_message_databases, discover_official_databases

    candidates = [Path(explicit)] if explicit else [root / "all_keys_2.json", root / "all_keys.json"]
    official = discover_official_databases(account)
    transfers = discover_message_databases(account)
    if not official:
        raise ValueError("The selected account has no local official-message database.")
    if not transfers:
        raise ValueError("The selected account has no local transfer-message database.")
    for candidate in candidates:
        if not candidate.is_file():
            continue
        keys = KeyStore.load(candidate)
        if all(keys.key_for(database) is not None for database in (*official, *transfers)):
            return candidate.resolve()
    raise ValueError("Existing key files do not cover both payment-notice and transfer-message databases.")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--wxlocal-root", required=True)
    parser.add_argument("--state-dir", required=True)
    parser.add_argument("--port", type=int, default=18765)
    parser.add_argument("--account-dir")
    parser.add_argument("--keys-file")
    parser.add_argument("--qr-image")
    args = parser.parse_args()
    if not 1024 <= args.port <= 65535:
        raise ValueError("Use a local unprivileged TCP port.")
    root = Path(args.wxlocal_root).resolve(strict=True)
    if not (root / "wxlocal" / "__init__.py").is_file():
        raise ValueError("wxlocalRoot must be the project root containing the wxlocal package.")
    sys.path.insert(0, str(root))
    from wxlocal.core.accounts import account_id_from_dir

    account = resolve_account(args.account_dir)
    account_id = account_id_from_dir(account)
    keys_file = choose_keys(root, account, args.keys_file)
    state = Path(args.state_dir).resolve()
    state.mkdir(parents=True, exist_ok=True)
    server_path = state / "server.json"
    previous = read_json(server_path) if server_path.exists() else {}
    if previous and previous.get("collectionAccountId") != account_id:
        raise ValueError("This test directory belongs to another collection account; use a new state directory.")
    base_url = f"http://127.0.0.1:{args.port}"
    tokens = {name: previous.get(name) or secrets.token_urlsafe(32)
              for name in ("clientToken", "bridgeToken", "adminToken")}
    server = {
        "dataDir": str(state / "server"), "collectionAccountId": account_id,
        "baseUrl": base_url, "port": args.port, "stopFile": str(state / "stop.signal"), **tokens,
    }
    client_path = state / "client.json"
    previous_client = read_json(client_path) if client_path.exists() else {}
    qr_path = args.qr_image or previous_client.get("qrImagePath") or ""
    if qr_path:
        qr = Path(qr_path).resolve(strict=True)
        if qr.suffix.casefold() not in {".png", ".jpg", ".jpeg"}:
            raise ValueError("The collection QR image must be a local PNG/JPEG file.")
        qr_path = str(qr)
    client = {
        "baseUrl": base_url, "clientToken": tokens["clientToken"],
        "adminToken": tokens["adminToken"], "dataDirectory": str(state / "client"),
        "qrImagePath": qr_path,
    }
    bridge = {
        "wxlocalRoot": str(root), "accountDir": str(account), "keysFile": str(keys_file),
        "baseUrl": base_url, "bridgeToken": tokens["bridgeToken"],
    }
    for folder in (state / "server", state / "client"):
        folder.mkdir(exist_ok=True)
    save_json(server_path, server)
    save_json(client_path, client)
    save_json(state / "bridge.json", bridge)
    print(json.dumps({"ok": True, "baseUrl": base_url, "stateDirectory": str(state),
                      "accountBound": True, "existingKeysOnly": True, "qrImageConfigured": bool(qr_path)}))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (ValueError, OSError, ImportError, KeyError) as error:
        # Never echo raw configuration, key values, source paths or SDK traces.
        print(f"Local environment preparation failed ({type(error).__name__}).", file=sys.stderr)
        if isinstance(error, ValueError):
            print(str(error), file=sys.stderr)
        raise SystemExit(1)

"""Explicitly configured wxlocal payment observation bridge for ZhuoDazi.

The bridge reads only the structured ``PaymentReader.notices`` and
``PaymentReader.transfers`` views.  It never starts a ``WeChatClient``, reads
ordinary chat text, scans process memory, or sends WeChat messages.  Every
forwarded event is an observation rather than independently verified settlement,
so every payload carries ``verified: false``.
"""

from __future__ import annotations

import argparse
from dataclasses import dataclass, field
from datetime import datetime, timezone
from decimal import Decimal, InvalidOperation
import hashlib
import importlib
import json
import os
from pathlib import Path
import re
import sqlite3
import sys
import tempfile
import time
from typing import Any, Callable, Iterable
from urllib.error import HTTPError, URLError
from urllib.parse import urlsplit
from urllib.request import (
    HTTPRedirectHandler,
    ProxyHandler,
    Request,
    build_opener,
)
import uuid


API_PATH = "/api/local-market/payment-events"
MARKET_API_PATH = "/api/pet-market/payment-events"
MARKET_HEARTBEAT_PATH = "/api/pet-market/payment-heartbeat"
PRODUCTION_HOST = "in.desktoppet.online"
CONFIG_FIELDS = {"wxlocalRoot", "accountDir", "keysFile", "baseUrl", "bridgeToken"}
REFERENCE_PATTERN = re.compile(
    r"(?:^|[;；])[ \t]*备注[ \t]*[：:][ \t]*"
    r"([0-9]{3}[A-Za-z]{2})(?=$|[\s,，;；.。!！?？)）\]】|/])",
    re.MULTILINE,
)
PAYER_REFERENCE_LINE_PATTERN = re.compile(
    r"^[ \t]*付款方备注[ \t]*[：:]?[ \t]*([0-9]{3}[A-Za-z]{2})[ \t]*(?=\r?$)",
    re.MULTILINE,
)
PERSONAL_COLLECTION_TITLE_PATTERN = re.compile(
    r"个人收款码到账[¥￥]((?:0|[1-9][0-9]*)(?:\.[0-9]{1,2})?)"
)
PERSONAL_COLLECTION_SUCCESS_PATTERN = re.compile(
    r"(?:\A|\r?\n)(?:备注)?收款成功，已存入零钱。"
    r"(?:点击可查看详情)?[ \t]*(?=\r?\n|\Z)"
)
PERSONAL_COLLECTION_NEGATIVE_MARKERS = (
    "失败", "待到账", "未到账", "等待到账", "预计到账", "待确认", "处理中",
    "取消", "撤销", "退款", "退还", "异常", "未成功", "未入账", "未完成", "冻结",
)
WECHAT_PAY_OFFICIAL_ID = "gh_3dfda90e39d6"
HEX_KEY_PATTERN = re.compile(r"[0-9a-fA-F]{64}")
OFFICIAL_DB_PATTERN = re.compile(r"biz_message_[0-9]+\.db", re.IGNORECASE)
MESSAGE_DB_PATTERN = re.compile(r"message_[0-9]+\.db", re.IGNORECASE)
SAFE_IDENTIFIER_PATTERN = re.compile(r"[A-Za-z0-9_-]{1,200}")
SAFE_ENUM_PATTERN = re.compile(r"[a-z][a-z0-9_]{0,63}")
BARE_REFERENCE_PATTERN = re.compile(r"[0-9]{3}[A-Za-z]{2}")
POLL_SOURCES = frozenset(("notice", "transfer"))
BUSY_RETRY_DELAYS = (0.6, 1.2)
EXCLUDED_CATEGORIES = {
    "automatic_payment",
    "daily_summary",
    "monthly_summary",
    "pay_later",
    "payment",
    "refund",
    "toll",
    "transfer_refund",
}


class ConfigError(ValueError):
    """Configuration is invalid.  Messages are internal and never print secrets."""


class NoticeError(ValueError):
    """A notice cannot safely be turned into a bridge record."""


class _NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, request, file_pointer, code, message, headers, new_url):
        return None


@dataclass(frozen=True)
class BridgeConfig:
    wxlocal_root: Path
    account_dir: Path = field(repr=False)
    keys_file: Path = field(repr=False)
    base_url: str
    bridge_token: str = field(repr=False)
    event_path: str = API_PATH
    mode: str = "local"


@dataclass(frozen=True)
class RuntimePaths:
    state_db: Path
    status_json: Path
    stop_file: Path


@dataclass(frozen=True)
class IngestRecord:
    event_key: str
    source_created_at: int
    observed_at: str
    payload: dict[str, Any] | None


@dataclass(frozen=True)
class IngestResult:
    baseline_established: bool
    new_candidates: int
    new_seen: int


@dataclass(frozen=True)
class SourcePollResult:
    source: str
    success: bool
    item_count: int
    captured_count: int
    baseline_established: bool
    saturated: bool
    attempts: int
    error_code: str = ""


@dataclass(frozen=True)
class PollResult:
    notice: SourcePollResult
    transfer: SourcePollResult

    @property
    def success(self) -> bool:
        return self.notice.success and self.transfer.success

    @property
    def notice_count(self) -> int:
        return self.notice.item_count

    @property
    def transfer_count(self) -> int:
        return self.transfer.item_count

    @property
    def captured_count(self) -> int:
        return self.notice.captured_count + self.transfer.captured_count

    @property
    def baseline_established(self) -> bool:
        return self.notice.baseline_established or self.transfer.baseline_established

    @property
    def saturated(self) -> bool:
        return self.notice.saturated or self.transfer.saturated

    @property
    def error_code(self) -> str:
        return self.notice.error_code or self.transfer.error_code


@dataclass(frozen=True)
class DeliveryResult:
    delivered_count: int
    failed_count: int
    error_code: str = ""


def _required_string(data: dict[str, Any], key: str) -> str:
    value = data.get(key)
    if not isinstance(value, str) or not value.strip():
        raise ConfigError(f"invalid_{key}")
    return value.strip()


def _absolute_path(value: str, key: str, *, directory: bool) -> Path:
    path = Path(value).expanduser()
    if not path.is_absolute():
        raise ConfigError(f"{key}_must_be_absolute")
    path = path.resolve()
    if directory and not path.is_dir():
        raise ConfigError(f"{key}_directory_missing")
    if not directory and not path.is_file():
        raise ConfigError(f"{key}_file_missing")
    return path


def normalize_base_url(value: str, mode: str = "local") -> str:
    try:
        parsed = urlsplit(value)
        port = parsed.port
    except ValueError as exc:
        raise ConfigError("invalid_base_url") from exc
    if parsed.username is not None or parsed.password is not None:
        raise ConfigError("base_url_userinfo_forbidden")
    if parsed.path not in ("", "/") or parsed.query or parsed.fragment:
        raise ConfigError("base_url_must_not_have_path_query_or_fragment")
    if mode == "local":
        if parsed.scheme != "http" or parsed.hostname != "127.0.0.1":
            raise ConfigError("base_url_must_be_literal_ipv4_loopback")
        if port is None or not 1 <= port <= 65535:
            raise ConfigError("base_url_requires_port")
        return f"http://127.0.0.1:{port}"
    if mode == "production":
        if parsed.scheme != "https" or parsed.hostname != PRODUCTION_HOST or port not in (None, 443):
            raise ConfigError("production_requires_official_https_endpoint")
        return f"https://{PRODUCTION_HOST}"
    raise ConfigError("invalid_endpoint_mode")


def _is_within(child: Path, parent: Path) -> bool:
    child_text = os.path.normcase(str(child.resolve(strict=False)))
    parent_text = os.path.normcase(str(parent.resolve(strict=False)))
    try:
        return os.path.commonpath((child_text, parent_text)) == parent_text
    except ValueError:
        return False


def _key_path_identity(value: str | Path) -> str:
    # Match wxlocal KeyStore path lookup without importing or opening wxlocal.
    return str(value).replace("\\", "/").lower()


def _existing_payment_shards(account_dir: Path) -> tuple[list[Path], list[Path]]:
    directory = account_dir / "db_storage" / "message"
    try:
        children = [path for path in directory.iterdir() if path.is_file()]
    except OSError as exc:
        raise ConfigError("account_message_directory_unavailable") from exc
    official = sorted(
        (path for path in children if OFFICIAL_DB_PATTERN.fullmatch(path.name)),
        key=lambda path: path.name.casefold(),
    )
    messages = sorted(
        (path for path in children if MESSAGE_DB_PATTERN.fullmatch(path.name)),
        key=lambda path: path.name.casefold(),
    )
    if not official:
        raise ConfigError("account_official_source_missing")
    if not messages:
        raise ConfigError("account_transfer_source_missing")
    return official, messages


def _validate_keys_file(keys_file: Path, account_dir: Path) -> None:
    if keys_file.stat().st_size > 32 * 1024 * 1024:
        raise ConfigError("keys_file_too_large")
    try:
        with keys_file.open("r", encoding="utf-8-sig") as stream:
            data = json.load(stream)
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise ConfigError("invalid_keys_file") from exc
    try:
        if not isinstance(data, dict) or not data:
            raise ConfigError("invalid_keys_mapping")
        keyed_account_paths: set[str] = set()
        for raw_path, raw_key in data.items():
            if not isinstance(raw_path, str) or not raw_path:
                raise ConfigError("invalid_key_path")
            if not isinstance(raw_key, str) or HEX_KEY_PATTERN.fullmatch(raw_key) is None:
                raise ConfigError("invalid_key_value")
            candidate = Path(raw_path).expanduser()
            if candidate.is_absolute() and _is_within(candidate, account_dir):
                keyed_account_paths.add(_key_path_identity(raw_path))
        official, messages = _existing_payment_shards(account_dir)
        covered_official = {
            path for path in official if _key_path_identity(path) in keyed_account_paths
        }
        covered_messages = {
            path for path in messages if _key_path_identity(path) in keyed_account_paths
        }
        if not covered_official:
            raise ConfigError("keys_missing_official_account_binding")
        if not covered_messages:
            raise ConfigError("keys_missing_transfer_account_binding")
        if len(covered_official) != len(official) or len(covered_messages) != len(messages):
            raise ConfigError("keys_missing_existing_source_shard")
    finally:
        # Avoid retaining the parsed secret mapping longer than validation needs.
        data = None


def load_config(path: Path) -> BridgeConfig:
    path = path.expanduser().resolve()
    if not path.is_file() or path.stat().st_size > 256 * 1024:
        raise ConfigError("config_file_missing_or_too_large")
    try:
        with path.open("r", encoding="utf-8-sig") as stream:
            data = json.load(stream)
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise ConfigError("invalid_config_json") from exc
    if (not isinstance(data, dict) or not CONFIG_FIELDS.issubset(data)
            or set(data) - CONFIG_FIELDS - {"eventPath", "mode"}):
        raise ConfigError("config_fields_mismatch")
    wxlocal_root = _absolute_path(_required_string(data, "wxlocalRoot"), "wxlocalRoot", directory=True)
    account_dir = _absolute_path(_required_string(data, "accountDir"), "accountDir", directory=True)
    keys_file = _absolute_path(_required_string(data, "keysFile"), "keysFile", directory=False)
    mode = data.get("mode", "local")
    base_url = normalize_base_url(_required_string(data, "baseUrl"), mode)
    token = _required_string(data, "bridgeToken")
    event_path = data.get("eventPath", API_PATH)
    if event_path not in (API_PATH, MARKET_API_PATH):
        raise ConfigError("invalid_event_path")
    if mode == "production" and event_path != MARKET_API_PATH:
        raise ConfigError("production_requires_market_api")
    if len(token) < 24 or len(token) > 256 or any(ord(char) < 33 or ord(char) > 126 for char in token):
        raise ConfigError("invalid_bridge_token")
    _validate_keys_file(keys_file, account_dir)
    return BridgeConfig(wxlocal_root, account_dir, keys_file, base_url, token, event_path, mode)


def _wxlocal_import_location(root: Path) -> tuple[Path, Path]:
    nested = root / "wxlocal" / "__init__.py"
    direct = root / "__init__.py"
    if nested.is_file():
        return root, nested
    if direct.is_file() and root.name.casefold() == "wxlocal":
        return root.parent, direct
    raise ConfigError("wxlocal_package_missing")


def load_payment_reader(root: Path):
    import_root, expected_init = _wxlocal_import_location(root)
    import_text = str(import_root)
    if import_text not in sys.path:
        sys.path.insert(0, import_text)
    importlib.invalidate_caches()
    existing = sys.modules.get("wxlocal")
    if existing is not None:
        existing_file = Path(getattr(existing, "__file__", "")).resolve()
        if os.path.normcase(str(existing_file)) != os.path.normcase(str(expected_init.resolve())):
            raise ConfigError("different_wxlocal_package_already_loaded")
    try:
        module = importlib.import_module("wxlocal")
    except ImportError as exc:
        raise ConfigError("wxlocal_payment_reader_unavailable") from exc
    module_file = Path(getattr(module, "__file__", "")).resolve()
    if os.path.normcase(str(module_file)) != os.path.normcase(str(expected_init.resolve())):
        raise ConfigError("different_wxlocal_package_already_loaded")
    try:
        reader = getattr(module, "PaymentReader")
    except AttributeError as exc:
        raise ConfigError("wxlocal_payment_reader_unavailable") from exc
    return reader


def resolve_runtime_paths(config_path: Path, args: argparse.Namespace, config: BridgeConfig) -> RuntimePaths:
    base = config_path.expanduser().resolve()
    state = Path(args.state_db).expanduser().resolve() if args.state_db else base.with_suffix(".sqlite3")
    status = Path(args.status_json).expanduser().resolve() if args.status_json else base.with_suffix(".status.json")
    stop = Path(args.stop_file).expanduser().resolve() if args.stop_file else base.with_suffix(".stop")
    paths = (state, status, stop)
    if len({os.path.normcase(str(item)) for item in paths}) != len(paths):
        raise ConfigError("runtime_paths_must_be_distinct")
    protected = {os.path.normcase(str(base)), os.path.normcase(str(config.keys_file))}
    if any(os.path.normcase(str(item)) in protected for item in paths):
        raise ConfigError("runtime_paths_must_not_replace_config_or_keys")
    if any(_is_within(item, config.account_dir) for item in paths):
        raise ConfigError("runtime_paths_must_be_outside_account_directory")
    for item in (state, status):
        if item.exists() and item.is_dir():
            raise ConfigError("runtime_file_is_directory")
    return RuntimePaths(state, status, stop)


def source_binding(account_dir: Path) -> str:
    normalized = os.path.normcase(str(account_dir.resolve())).encode("utf-8")
    return hashlib.sha256(b"zhuodazi-local-market-source-v1\0" + normalized).hexdigest()


class SourceLock:
    """One observer per account on this computer, including copied tools."""

    def __init__(self, binding: str):
        if re.fullmatch(r"[0-9a-f]{64}", binding) is None:
            raise ConfigError("invalid_source_binding")
        directory = Path(tempfile.gettempdir()) / "zhuodazi-payment-locks"
        directory.mkdir(exist_ok=True)
        self.stream = (directory / (binding + ".lock")).open("a+b")
        try:
            self.stream.seek(0, os.SEEK_END)
            if self.stream.tell() == 0:
                self.stream.write(b"0")
                self.stream.flush()
            self.stream.seek(0)
            if os.name == "nt":
                import msvcrt
                msvcrt.locking(self.stream.fileno(), msvcrt.LK_NBLCK, 1)
            else:
                import fcntl
                fcntl.flock(self.stream.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        except OSError as exc:
            self.stream.close()
            raise ConfigError("another_listener_is_running") from exc

    def close(self):
        self.stream.close()


def utc_iso(epoch_seconds: float | int | None = None) -> str:
    value = time.time() if epoch_seconds is None else float(epoch_seconds)
    return datetime.fromtimestamp(value, timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z")


def source_time_iso(value: Any) -> str | None:
    if isinstance(value, bool):
        return None
    try:
        number = int(value)
        if number <= 0:
            return None
        return utc_iso(number)
    except (TypeError, ValueError, OverflowError, OSError):
        return None


def extract_explicit_reference(*texts: Any) -> str:
    matches: set[str] = set()
    for text in texts:
        if isinstance(text, str):
            matches.update(match.group(1).upper() for match in REFERENCE_PATTERN.finditer(text))
            matches.update(
                match.group(1).upper() for match in PAYER_REFERENCE_LINE_PATTERN.finditer(text)
            )
    return next(iter(matches)) if len(matches) == 1 else ""


def amount_to_fen(value: Any) -> int | None:
    if value is None:
        return None
    try:
        amount = value if isinstance(value, Decimal) else Decimal(str(value))
        fen = amount * Decimal(100)
    except (InvalidOperation, TypeError, ValueError):
        return None
    if not amount.is_finite() or amount < 0 or fen != fen.to_integral_value():
        return None
    result = int(fen)
    return result if result <= 9_007_199_254_740_991 else None


def _safe_enum(value: Any, fallback: str) -> str:
    return value if isinstance(value, str) and SAFE_ENUM_PATTERN.fullmatch(value) else fallback


def personal_collection_semantics(
        notice: Any, *, category: str, direction: str, status: str,
        amount_fen: int | None) -> tuple[str, str, str, bool]:
    """Recognize one confirmed official receipt template, or reject its lookalikes.

    The SDK amount remains authoritative.  The title amount is parsed only to
    prove that the SDK value came from the strict title rather than the daily
    aggregate amount that can also occur in ``detail``.
    """
    title = getattr(notice, "title", "")
    detail = getattr(notice, "detail", "")
    if not isinstance(title, str) or "个人收款码到账" not in title:
        return category, direction, status, False
    if not isinstance(detail, str):
        return category, direction, status, True
    event = getattr(notice, "event", None)
    title_match = PERSONAL_COLLECTION_TITLE_PATTERN.fullmatch(title)
    official_source = (
        getattr(event, "conversation", "") == WECHAT_PAY_OFFICIAL_ID
        and getattr(event, "is_official_message", False) is True
    )
    unknown_semantics = (
        category == "other" and direction == "unknown" and status == "unknown"
    )
    known_receipt_semantics = (
        category == "receipt" and direction == "incoming" and status == "completed"
    )
    safe_detail = (
        PERSONAL_COLLECTION_SUCCESS_PATTERN.search(detail) is not None
        and not any(marker in detail for marker in PERSONAL_COLLECTION_NEGATIVE_MARKERS)
    )
    if title_match is None:
        return category, direction, status, True
    title_amount_fen = amount_to_fen(Decimal(title_match.group(1)))
    exact_amount = amount_fen is not None and amount_fen > 0 and title_amount_fen == amount_fen
    if official_source and safe_detail and exact_amount:
        if unknown_semantics:
            return "receipt", "incoming", "completed", False
        if known_receipt_semantics:
            return category, direction, status, False
    return category, direction, status, True


def is_receipt_candidate(*, category: str, direction: str, status: str,
                         amount_fen: int | None, reference: str) -> bool:
    if direction == "outgoing" or category in EXCLUDED_CATEGORIES or status == "summary":
        return False
    if category == "receipt":
        return direction in ("incoming", "unknown")
    if direction == "incoming":
        return True
    # A new SDK-unknown template stays reviewable only when it has a concrete
    # amount or an explicitly labelled short reference.  Generic notices are ignored.
    return category == "other" and direction == "unknown" and (
        amount_fen is not None or bool(reference)
    )


def notice_to_ingest_record(notice: Any, observed_at: str) -> IngestRecord:
    event = getattr(notice, "event", None)
    event_key = getattr(notice, "event_key", "")
    if not isinstance(event_key, str) or not event_key or len(event_key) > 512:
        raise NoticeError("invalid_event_key")
    if any(ord(char) < 32 for char in event_key):
        raise NoticeError("invalid_event_key")
    created_raw = getattr(event, "create_time", 0)
    try:
        source_created = int(created_raw) if not isinstance(created_raw, bool) else 0
    except (TypeError, ValueError, OverflowError):
        source_created = 0
    category = _safe_enum(getattr(notice, "category", ""), "other")
    direction = _safe_enum(getattr(notice, "direction", ""), "unknown")
    status = _safe_enum(getattr(notice, "status", ""), "unknown")
    amount_fen = amount_to_fen(getattr(notice, "amount", None))
    category, direction, status, reject_lookalike = personal_collection_semantics(
        notice,
        category=category,
        direction=direction,
        status=status,
        amount_fen=amount_fen,
    )
    reference = extract_explicit_reference(
        getattr(notice, "title", ""), getattr(notice, "detail", "")
    )
    candidate = not reject_lookalike and is_receipt_candidate(
        category=category,
        direction=direction,
        status=status,
        amount_fen=amount_fen,
        reference=reference,
    )
    if not candidate:
        return IngestRecord(event_key, source_created, observed_at, None)
    account_id = getattr(event, "account_id", "")
    if not isinstance(account_id, str) or not account_id or len(account_id) > 200:
        raise NoticeError("missing_account_id")
    if any(ord(char) < 32 for char in account_id):
        raise NoticeError("invalid_account_id")
    transaction_id = getattr(notice, "transaction_id", "")
    if not isinstance(transaction_id, str) or SAFE_IDENTIFIER_PATTERN.fullmatch(transaction_id) is None:
        transaction_id = ""
    currency = getattr(notice, "currency", "")
    currency = "CNY" if amount_fen is not None and currency == "CNY" else ""
    payload = {
        "eventKey": event_key,
        "accountId": account_id,
        "category": category,
        "direction": direction,
        "status": status,
        "amountFen": amount_fen,
        "currency": currency,
        "reference": reference,
        "transactionId": transaction_id,
        "occurredAt": source_time_iso(source_created),
        "observedAt": observed_at,
        "verified": False,
    }
    return IngestRecord(event_key, source_created, observed_at, payload)


def _safe_identifier(value: Any) -> str:
    return value if isinstance(value, str) and SAFE_IDENTIFIER_PATTERN.fullmatch(value) else ""


def _source_created_at(event: Any) -> int:
    value = getattr(event, "create_time", 0)
    if isinstance(value, bool):
        return 0
    try:
        return int(value)
    except (TypeError, ValueError, OverflowError):
        return 0


def _is_private_event(event: Any) -> bool:
    explicit = getattr(event, "is_private", None)
    if isinstance(explicit, bool):
        return explicit
    conversation = getattr(event, "conversation", "")
    return (
        isinstance(conversation, str)
        and bool(conversation)
        and "@chatroom" not in conversation.casefold()
    )


def transfer_to_ingest_record(record: Any, observed_at: str) -> IngestRecord:
    """Convert one structured private transfer card into a minimal observation.

    A qualifying payload means only that an incoming transfer card addressed to
    this account was observed with a strict reference.  It does not assert that
    the transfer was accepted, settled, or otherwise completed.
    """
    event = getattr(record, "event", None)
    raw_event_key = getattr(record, "event_key", "")
    if (
        not isinstance(raw_event_key, str)
        or not raw_event_key
        or len(raw_event_key) > 512
        or any(ord(char) < 32 for char in raw_event_key)
    ):
        raise NoticeError("invalid_event_key")
    event_key = f"transfer:{raw_event_key}"
    source_created = _source_created_at(event)

    account_id = getattr(event, "account_id", "")
    payer = getattr(record, "payer", "")
    receiver = getattr(record, "receiver", "")
    amount_fen = amount_to_fen(getattr(record, "amount", None))
    memo = getattr(record, "memo", "")
    reference = ""
    if isinstance(memo, str):
        trimmed_memo = memo.strip()
        if BARE_REFERENCE_PATTERN.fullmatch(trimmed_memo):
            reference = trimmed_memo.upper()
        else:
            reference = extract_explicit_reference(memo)

    transaction_id = _safe_identifier(getattr(record, "transaction_id", ""))
    transfer_id = _safe_identifier(getattr(record, "transfer_id", ""))
    outgoing_transaction_id = transaction_id or (
        f"transfer_id:{transfer_id}" if transfer_id else ""
    )
    safe_account = (
        isinstance(account_id, str)
        and bool(account_id)
        and len(account_id) <= 200
        and not any(ord(char) < 32 for char in account_id)
    )
    safe_payer = (
        isinstance(payer, str)
        and bool(payer)
        and len(payer) <= 200
        and not any(ord(char) < 32 for char in payer)
    )
    # Some genuine initial transfer cards leave payer_username empty.  The
    # explicit receiver still establishes the financial direction; in this
    # one case, require a direct private chat sender as corroborating context.
    # Never use sender direction alone or infer receipt from pay_subtype.
    sender = getattr(event, "sender", "")
    direct_sender_when_payer_missing = (
        payer == ""
        and getattr(event, "source_kind", "") == "chat"
        and getattr(event, "msg_type", None) == 49
        and isinstance(sender, str)
        and bool(sender)
        and len(sender) <= 200
        and not any(ord(char) < 32 for char in sender)
        and sender != account_id
        and sender == getattr(event, "conversation", "")
    )
    candidate = (
        getattr(record, "kind", "") == "transfer"
        and _is_private_event(event)
        and getattr(record, "direction", "") == "incoming"
        and safe_account
        and isinstance(receiver, str)
        and receiver == account_id
        and ((safe_payer and payer != receiver) or direct_sender_when_payer_missing)
        and amount_fen is not None
        and amount_fen > 0
        and bool(reference)
        and bool(outgoing_transaction_id)
    )
    if not candidate:
        return IngestRecord(event_key, source_created, observed_at, None)

    payload = {
        "eventKey": event_key,
        "accountId": account_id,
        "category": "personal_transfer",
        "direction": "incoming",
        "status": "observed",
        "amountFen": amount_fen,
        "currency": "CNY",
        "reference": reference,
        "transactionId": outgoing_transaction_id,
        "occurredAt": source_time_iso(source_created),
        "observedAt": observed_at,
        "verified": False,
    }
    return IngestRecord(event_key, source_created, observed_at, payload)


class StateStore:
    """Durable source cursor and minimal at-least-once delivery outbox."""

    def __init__(self, path: Path, binding: str):
        path.parent.mkdir(parents=True, exist_ok=True)
        self.path = path
        self.database = sqlite3.connect(path, timeout=5)
        self.database.row_factory = sqlite3.Row
        self.database.execute("PRAGMA journal_mode=WAL")
        self.database.execute("PRAGMA synchronous=FULL")
        self.database.executescript(
            """
            CREATE TABLE IF NOT EXISTS bridge_state (
              key TEXT PRIMARY KEY,
              value TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS seen_events (
              event_key TEXT PRIMARY KEY,
              source_created_at INTEGER NOT NULL,
              first_observed_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS payment_outbox (
              event_key TEXT PRIMARY KEY,
              account_id TEXT NOT NULL,
              category TEXT NOT NULL,
              direction TEXT NOT NULL,
              status TEXT NOT NULL,
              amount_fen INTEGER,
              currency TEXT NOT NULL,
              reference TEXT NOT NULL,
              transaction_id TEXT NOT NULL,
              occurred_at TEXT,
              observed_at TEXT NOT NULL,
              verified INTEGER NOT NULL CHECK (verified = 0),
              delivery_state TEXT NOT NULL CHECK (delivery_state IN ('pending','inflight','delivered')),
              attempts INTEGER NOT NULL DEFAULT 0,
              next_attempt_at REAL NOT NULL,
              claim_token TEXT,
              claim_until REAL,
              last_error_code TEXT NOT NULL DEFAULT '',
              delivered_at TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_payment_outbox_due
              ON payment_outbox(delivery_state, next_attempt_at);
            """
        )
        if "server_outcome" not in {row[1] for row in self.database.execute("PRAGMA table_info(payment_outbox)")}:
            self.database.execute("ALTER TABLE payment_outbox ADD COLUMN server_outcome TEXT NOT NULL DEFAULT 'unknown'")
            self.database.commit()
        try:
            self._bind_source(binding)
        except Exception:
            self.database.close()
            raise

    def _begin(self) -> None:
        self.database.execute("BEGIN IMMEDIATE")

    def bind_destination(self, base_url: str, event_path: str, mode: str = "local") -> None:
        if event_path not in (API_PATH, MARKET_API_PATH):
            raise ConfigError("invalid_event_path")
        destination = normalize_base_url(base_url, mode) + event_path
        self._begin()
        try:
            existing = self._state("delivery_destination")
            if existing is not None and existing != destination:
                raise ConfigError("state_database_bound_to_different_destination")
            if existing is None:
                has_history = self.database.execute("SELECT 1 FROM seen_events LIMIT 1").fetchone()
                if event_path != API_PATH and has_history:
                    raise ConfigError("new_market_requires_separate_state_database")
                self._set_state("delivery_destination", destination)
            self.database.commit()
        except Exception:
            self.database.rollback()
            raise

    def _state(self, key: str) -> str | None:
        row = self.database.execute("SELECT value FROM bridge_state WHERE key = ?", (key,)).fetchone()
        return None if row is None else str(row["value"])

    def _set_state(self, key: str, value: str) -> None:
        self.database.execute(
            "INSERT INTO bridge_state(key,value) VALUES (?,?) "
            "ON CONFLICT(key) DO UPDATE SET value=excluded.value",
            (key, value),
        )

    def _bind_source(self, binding: str) -> None:
        self._begin()
        try:
            existing = self._state("source_binding")
            if existing is not None and existing != binding:
                raise ConfigError("state_database_bound_to_different_account")
            if existing is None:
                self._set_state("source_binding", binding)
            # A pre-transfer bridge used one global baseline.  Preserve it only
            # as the notice baseline so the first transfer read still suppresses
            # historical transfer cards.
            if (
                self._state("baseline_ready") == "1"
                and self._state("baseline_ready_notice") is None
            ):
                self._set_state("baseline_ready_notice", "1")
            self.database.commit()
        except Exception:
            self.database.rollback()
            raise

    @staticmethod
    def _baseline_key(source: str) -> str:
        if source not in POLL_SOURCES:
            raise ValueError("invalid_poll_source")
        return f"baseline_ready_{source}"

    def baseline_ready(self, source: str = "notice") -> bool:
        return self._state(self._baseline_key(source)) == "1"

    def overlaps_seen(self, records: list[IngestRecord]) -> bool:
        for start in range(0, len(records), 400):
            keys = [record.event_key for record in records[start:start + 400]]
            if keys and self.database.execute(
                "SELECT 1 FROM seen_events WHERE event_key IN (" + ",".join("?" for _ in keys) + ") LIMIT 1",
                keys,
            ).fetchone() is not None:
                return True
        return False

    def ingest(self, records: Iterable[IngestRecord], source: str = "notice") -> IngestResult:
        rows = list(records)
        baseline_key = self._baseline_key(source)
        self._begin()
        try:
            baseline = self._state(baseline_key) == "1"
            established = not baseline
            new_candidates = 0
            new_seen = 0
            for record in rows:
                if self.database.execute(
                    "SELECT 1 FROM seen_events WHERE event_key = ?", (record.event_key,)
                ).fetchone() is not None:
                    continue
                if baseline and record.payload is not None:
                    payload = record.payload
                    self.database.execute(
                        """
                        INSERT OR IGNORE INTO payment_outbox (
                          event_key,account_id,category,direction,status,amount_fen,currency,
                          reference,transaction_id,occurred_at,observed_at,verified,
                          delivery_state,attempts,next_attempt_at
                        ) VALUES (?,?,?,?,?,?,?,?,?,?,?,?, 'pending',0,0)
                        """,
                        (
                            payload["eventKey"], payload["accountId"], payload["category"],
                            payload["direction"], payload["status"], payload["amountFen"],
                            payload["currency"], payload["reference"], payload["transactionId"],
                            payload["occurredAt"], payload["observedAt"], 0,
                        ),
                    )
                    new_candidates += self.database.execute("SELECT changes()").fetchone()[0]
                self.database.execute(
                    "INSERT INTO seen_events(event_key,source_created_at,first_observed_at) VALUES (?,?,?)",
                    (record.event_key, record.source_created_at, record.observed_at),
                )
                new_seen += 1
            if established:
                self._set_state(baseline_key, "1")
            self.database.commit()
            return IngestResult(established, new_candidates, new_seen)
        except Exception:
            self.database.rollback()
            raise

    @staticmethod
    def _payload(row: sqlite3.Row) -> dict[str, Any]:
        return {
            "eventKey": row["event_key"],
            "accountId": row["account_id"],
            "category": row["category"],
            "direction": row["direction"],
            "status": row["status"],
            "amountFen": row["amount_fen"],
            "currency": row["currency"],
            "reference": row["reference"],
            "transactionId": row["transaction_id"],
            "occurredAt": row["occurred_at"],
            "observedAt": row["observed_at"],
            "verified": False,
        }

    def claim_due(self, now_epoch: float, lease_seconds: float = 30.0):
        self._begin()
        try:
            row = self.database.execute(
                """
                SELECT * FROM payment_outbox
                WHERE (delivery_state = 'pending' AND next_attempt_at <= ?)
                   OR (delivery_state = 'inflight' AND claim_until <= ?)
                ORDER BY observed_at, event_key
                LIMIT 1
                """,
                (now_epoch, now_epoch),
            ).fetchone()
            if row is None:
                self.database.commit()
                return None
            token = uuid.uuid4().hex
            updated = self.database.execute(
                """
                UPDATE payment_outbox
                SET delivery_state='inflight', claim_token=?, claim_until=?
                WHERE event_key=? AND delivery_state=?
                """,
                (token, now_epoch + lease_seconds, row["event_key"], row["delivery_state"]),
            ).rowcount
            if updated != 1:
                self.database.rollback()
                return None
            self.database.commit()
            return row["event_key"], token, int(row["attempts"]), self._payload(row)
        except Exception:
            self.database.rollback()
            raise

    def mark_delivered(self, event_key: str, claim_token: str, delivered_at: str,
                       outcome: str = "unknown") -> None:
        outcome = outcome if outcome in ("approved", "candidate_review") else "unknown"
        with self.database:
            updated = self.database.execute(
                """
                UPDATE payment_outbox
                SET delivery_state='delivered', delivered_at=?, claim_token=NULL,
                    claim_until=NULL, last_error_code='', server_outcome=?
                WHERE event_key=? AND delivery_state='inflight' AND claim_token=?
                """,
                (delivered_at, outcome, event_key, claim_token),
            ).rowcount
        if updated != 1:
            raise RuntimeError("outbox_claim_lost")

    def mark_failed(self, event_key: str, claim_token: str, attempts: int,
                    now_epoch: float, error_code: str) -> None:
        next_attempts = attempts + 1
        delay = min(300.0, float(2 ** min(next_attempts, 8)))
        with self.database:
            updated = self.database.execute(
                """
                UPDATE payment_outbox
                SET delivery_state='pending', attempts=?, next_attempt_at=?,
                    claim_token=NULL, claim_until=NULL, last_error_code=?
                WHERE event_key=? AND delivery_state='inflight' AND claim_token=?
                """,
                (next_attempts, now_epoch + delay, error_code, event_key, claim_token),
            ).rowcount
        if updated != 1:
            raise RuntimeError("outbox_claim_lost")

    def counts(self) -> dict[str, int]:
        row = self.database.execute(
            """
            SELECT COUNT(*) AS candidates,
              SUM(CASE WHEN delivery_state != 'delivered' THEN 1 ELSE 0 END) AS pending,
              SUM(CASE WHEN delivery_state = 'delivered' THEN 1 ELSE 0 END) AS delivered,
              SUM(CASE WHEN server_outcome = 'approved' THEN 1 ELSE 0 END) AS approved,
              SUM(CASE WHEN server_outcome = 'candidate_review' THEN 1 ELSE 0 END) AS review,
              SUM(CASE WHEN delivery_state != 'delivered' AND last_error_code IN ('http_400','http_401','http_403','http_404','http_409','http_422') THEN 1 ELSE 0 END) AS attention,
              COALESCE(SUM(attempts),0) AS retries
            FROM payment_outbox
            """
        ).fetchone()
        seen = self.database.execute("SELECT COUNT(*) FROM seen_events").fetchone()[0]
        return {
            "candidateCount": int(row["candidates"] or 0),
            "pendingCount": int(row["pending"] or 0),
            "deliveredCount": int(row["delivered"] or 0),
            "retryCount": int(row["retries"] or 0),
            "seenCount": int(seen),
            "approvedCount": int(row["approved"] or 0),
            "reviewCount": int(row["review"] or 0),
            "attentionCount": int(row["attention"] or 0),
        }

    def close(self) -> None:
        self.database.close()


class LocalEventSender:
    """POST to validated loopback or explicitly opted-in official HTTPS."""

    def __init__(self, base_url: str, token: str, timeout: float = 3.0, *, event_path: str = API_PATH,
                 mode: str = "local"):
        if event_path not in (API_PATH, MARKET_API_PATH):
            raise ConfigError("invalid_event_path")
        if mode == "production" and event_path != MARKET_API_PATH:
            raise ConfigError("production_requires_market_api")
        self.endpoint = normalize_base_url(base_url, mode) + event_path
        self.heartbeat_endpoint = (
            normalize_base_url(base_url, mode) + MARKET_HEARTBEAT_PATH
            if event_path == MARKET_API_PATH else None
        )
        self.token = token
        self.timeout = timeout
        self.last_outcome = "unknown"
        # Environment proxies and redirects could move a request off-machine.
        self.opener = build_opener(ProxyHandler({}), _NoRedirect())

    def send(self, payload: dict[str, Any]) -> tuple[bool, str]:
        self.last_outcome = "unknown"
        return self._post(self.endpoint, payload, payment_event=True)

    def send_heartbeat(self, account_id: str, store: StateStore,
                       poll: PollResult, *, running: bool = True) -> tuple[bool | None, str]:
        # The old isolated payment experiment has no heartbeat API. Do not
        # change its protocol or replay any of its persisted observations.
        if self.heartbeat_endpoint is None:
            return True, ""
        # A concurrent WeChat WAL write invalidates an otherwise consistent
        # read. Keep the last real heartbeat lease instead of flapping offline
        # on this transient condition. Crucially, do NOT renew that lease:
        # sustained contention expires it at the server's normal 60s deadline.
        # Hard failures, saturation and shutdown still report immediately.
        failed_sources = [source for source in (poll.notice, poll.transfer) if not source.success]
        if (running and not poll.saturated and failed_sources
                and all(source.error_code == "source_busy" for source in failed_sources)):
            return None, "source_busy_deferred"
        return self._post(self.heartbeat_endpoint, {
            "accountId": account_id,
            "noticeReady": running and store.baseline_ready("notice"),
            "transferReady": running and store.baseline_ready("transfer"),
            "noticePollSuccess": running and poll.notice.success,
            "transferPollSuccess": running and poll.transfer.success,
            "pollLimitSaturated": poll.saturated,
        })

    def _post(self, endpoint: str, payload: dict[str, Any], *, payment_event: bool = False) -> tuple[bool, str]:
        body = json.dumps(payload, ensure_ascii=True, separators=(",", ":")).encode("utf-8")
        request = Request(
            endpoint,
            data=body,
            method="POST",
            headers={
                "Accept": "application/json",
                "User-Agent": "ZhuoDazi-PaymentMonitor/3.5.3",
                "Authorization": f"Bearer {self.token}",
                "Content-Type": "application/json",
            },
        )
        try:
            with self.opener.open(request, timeout=self.timeout) as response:
                status = int(response.status)
                if payment_event and 200 <= status < 300:
                    try:
                        raw = response.read(65537)
                        result = json.loads(raw) if len(raw) <= 65536 else None
                        event = result.get("paymentEvent") if isinstance(result, dict) else None
                        outcome = event.get("reviewStatus") if isinstance(event, dict) else None
                        if outcome in ("approved", "candidate_review"):
                            self.last_outcome = outcome
                    except (ValueError, UnicodeError):
                        pass  # Delivery succeeded; an unknown outcome never means paid.
                return (True, "") if 200 <= status < 300 else (False, f"http_{status}")
        except HTTPError as exc:
            try:
                code = int(exc.code)
            finally:
                exc.close()
            return False, f"http_{code}"
        except (URLError, TimeoutError, OSError):
            return False, "network_error"


def _is_busy_error(error: BaseException) -> bool:
    return any(base.__name__ == "BusyError" for base in type(error).__mro__)


class PaymentBridge:
    def __init__(self, reader: Any, store: StateStore, sender: Any, *,
                 limit: int = 200, clock: Callable[[], float] = time.time,
                 sleeper: Callable[[float], None] = time.sleep):
        self.reader = reader
        self.store = store
        self.sender = sender
        self.limit = limit
        self.clock = clock
        self.sleeper = sleeper

    def _read_source(self, source: str, limit: int | None = None) -> tuple[list[Any] | None, int, str]:
        method_name = "notices" if source == "notice" else "transfers"
        attempts = 0
        while attempts < 3:
            attempts += 1
            try:
                method = getattr(self.reader, method_name)
                return list(method(limit=limit or self.limit)), attempts, ""
            except Exception as exc:
                busy = _is_busy_error(exc)
                if not busy or attempts >= 3:
                    return None, attempts, "source_busy" if busy else "source_read_failed"
                self.sleeper(BUSY_RETRY_DELAYS[attempts - 1])
        return None, attempts, "source_read_failed"

    def _poll_source(self, source: str) -> SourcePollResult:
        limit, attempts = self.limit, 0
        converter = notice_to_ingest_record if source == "notice" else transfer_to_ingest_record
        while True:
            items, read_attempts, read_error = self._read_source(source, limit)
            attempts += read_attempts
            if items is None:
                return SourcePollResult(source, False, 0, 0, False, False, attempts, read_error)
            # SDK results are newest first. Timestamp after each completed read.
            observed_at = utc_iso(self.clock())
            records: list[IngestRecord] = []
            error_code = ""
            for item in reversed(items):
                try:
                    records.append(converter(item, observed_at))
                except NoticeError as exc:
                    error_code = str(exc) if str(exc) in {
                        "missing_account_id", "invalid_account_id", "invalid_event_key"
                    } else f"invalid_{source}"
            # A full recent-history window is normal for a long-running account.
            # Grow it only when reconnecting without overlap with our cursor.
            # Never advance the cursor across an unresolved gap.
            if (len(items) < limit or not self.store.baseline_ready(source)
                    or self.store.overlaps_seen(records)):
                break
            if limit >= 5000:
                return SourcePollResult(source, False, len(items), 0, False, True,
                                        attempts, "source_window_gap")
            limit = min(5000, limit * 2)
        try:
            ingested = self.store.ingest(records, source=source)
        except Exception:
            return SourcePollResult(
                source, False, len(items), 0, False, False,
                attempts, "state_write_failed",
            )
        success = not error_code
        return SourcePollResult(
            source,
            success,
            len(items),
            ingested.new_candidates,
            ingested.baseline_established,
            False,
            attempts,
            error_code,
        )

    def poll_once(self) -> PollResult:
        notice = self._poll_source("notice")
        transfer = self._poll_source("transfer")
        return PollResult(notice, transfer)

    def deliver_due(self, maximum: int = 100, *, should_stop: Callable[[], bool] = lambda: False,
                    time_budget: float = 4.0) -> DeliveryResult:
        delivered = 0
        failed = 0
        last_error = ""
        deadline = time.monotonic() + time_budget
        for _ in range(maximum):
            # Bound each batch so a slow backlog cannot starve heartbeat/polling
            # or keep a closed monitor sending for several minutes.
            if should_stop() or time.monotonic() >= deadline:
                break
            now_epoch = self.clock()
            claimed = self.store.claim_due(now_epoch)
            if claimed is None:
                break
            event_key, token, attempts, payload = claimed
            try:
                ok, error_code = self.sender.send(payload)
            except Exception:
                ok, error_code = False, "sender_error"
            if ok:
                self.store.mark_delivered(event_key, token, utc_iso(self.clock()),
                                          getattr(self.sender, "last_outcome", "unknown"))
                delivered += 1
            else:
                safe_error = error_code if re.fullmatch(r"(?:http_[0-9]{3}|network_error|sender_error)", error_code or "") else "sender_error"
                self.store.mark_failed(event_key, token, attempts, now_epoch, safe_error)
                failed += 1
                last_error = safe_error
        return DeliveryResult(delivered, failed, last_error)


class StatusFile:
    def __init__(self, path: Path, store: StateStore):
        self.path = path
        self.store = store
        self.last_poll_at: str | None = None
        self.last_successful_poll_at: str | None = None
        self.last_poll_success = False
        self.notice_last_poll_success = False
        self.transfer_last_poll_success = False
        self.notice_last_count = 0
        self.transfer_last_count = 0
        self.notice_last_captured_count = 0
        self.transfer_last_captured_count = 0
        self.notice_last_error_code = ""
        self.transfer_last_error_code = ""
        self.notice_saturated = False
        self.transfer_saturated = False
        self.last_captured_count = 0
        self.last_delivered_count = 0
        self.last_failed_delivery_count = 0
        self.last_error_code = ""
        self.saturated = False
        self.last_heartbeat_success: bool | None = None
        self.last_heartbeat_at: str | None = None
        self.last_heartbeat_error_code = ""

    def record(self, poll: PollResult, delivery: DeliveryResult) -> None:
        self.last_poll_at = utc_iso()
        self.last_poll_success = poll.success
        if poll.success:
            self.last_successful_poll_at = self.last_poll_at
        self.notice_last_poll_success = poll.notice.success
        self.transfer_last_poll_success = poll.transfer.success
        self.notice_last_count = poll.notice.item_count
        self.transfer_last_count = poll.transfer.item_count
        self.notice_last_captured_count = poll.notice.captured_count
        self.transfer_last_captured_count = poll.transfer.captured_count
        self.notice_last_error_code = poll.notice.error_code
        self.transfer_last_error_code = poll.transfer.error_code
        self.notice_saturated = poll.notice.saturated
        self.transfer_saturated = poll.transfer.saturated
        self.last_captured_count = poll.captured_count
        self.last_delivered_count = delivery.delivered_count
        self.last_failed_delivery_count = delivery.failed_count
        self.saturated = poll.saturated
        self.last_error_code = poll.error_code or delivery.error_code

    def write(self, *, running: bool, stopped: bool = False) -> None:
        counts = self.store.counts()
        if stopped:
            health = "stopped"
        elif self.last_error_code or self.saturated:
            health = "degraded"
        elif self.last_poll_success:
            health = "ok"
        else:
            health = "starting"
        document = {
            "version": 1,
            "health": health,
            "running": running,
            "baselineReady": (
                self.store.baseline_ready("notice")
                and self.store.baseline_ready("transfer")
            ),
            "noticeBaselineReady": self.store.baseline_ready("notice"),
            "transferBaselineReady": self.store.baseline_ready("transfer"),
            "lastPollSuccess": self.last_poll_success,
            "noticeLastPollSuccess": self.notice_last_poll_success,
            "transferLastPollSuccess": self.transfer_last_poll_success,
            "lastPollAt": self.last_poll_at,
            "lastSuccessfulPollAt": self.last_successful_poll_at,
            "lastNoticeCount": self.notice_last_count,
            "noticeLastCount": self.notice_last_count,
            "transferLastCount": self.transfer_last_count,
            "lastCapturedCount": self.last_captured_count,
            "noticeLastCapturedCount": self.notice_last_captured_count,
            "transferLastCapturedCount": self.transfer_last_captured_count,
            "lastDeliveredCount": self.last_delivered_count,
            "lastFailedDeliveryCount": self.last_failed_delivery_count,
            "pollLimitSaturated": self.saturated,
            "noticePollLimitSaturated": self.notice_saturated,
            "transferPollLimitSaturated": self.transfer_saturated,
            "lastErrorCode": self.last_error_code,
            "noticeLastErrorCode": self.notice_last_error_code,
            "transferLastErrorCode": self.transfer_last_error_code,
            "lastHeartbeatSuccess": self.last_heartbeat_success,
            "lastHeartbeatAt": self.last_heartbeat_at,
            "lastHeartbeatErrorCode": self.last_heartbeat_error_code,
            **counts,
        }
        self.path.parent.mkdir(parents=True, exist_ok=True)
        temporary = self.path.with_name(f".{self.path.name}.{uuid.uuid4().hex}.tmp")
        try:
            temporary.write_text(
                json.dumps(document, ensure_ascii=True, separators=(",", ":")) + "\n",
                encoding="utf-8",
            )
            os.replace(temporary, self.path)
        finally:
            if temporary.exists():
                temporary.unlink()


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description=(
            "Poll wxlocal payment notices and personal transfers, then forward "
            "minimal observations to the explicitly configured ZhuoDazi API."
        )
    )
    parser.add_argument("--config", required=True, help="Absolute path to the bridge JSON configuration.")
    parser.add_argument("--check-config", action="store_true",
                        help="Validate configuration without opening WeChat source databases.")
    parser.add_argument("--once", action="store_true", help="Run one poll/delivery pass and exit.")
    parser.add_argument("--stop-file", help="Stop when this local file exists.")
    parser.add_argument("--state-db", help="Local SQLite cursor/outbox path.")
    parser.add_argument("--status-json", help="Local count-only health JSON path.")
    parser.add_argument("--poll-seconds", type=float, default=2.0)
    parser.add_argument("--limit", type=int, default=200)
    return parser


def _wait_for_stop(stop_file: Path, seconds: float) -> bool:
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        if stop_file.exists():
            return True
        time.sleep(min(0.25, max(0.0, deadline - time.monotonic())))
    return stop_file.exists()


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    source_lock = None
    try:
        if not 0.5 <= args.poll_seconds <= 3600:
            raise ConfigError("invalid_poll_seconds")
        if not 1 <= args.limit <= 5000:
            raise ConfigError("invalid_limit")
        config_path = Path(args.config).expanduser().resolve()
        config = load_config(config_path)
        reader_type = load_payment_reader(config.wxlocal_root)
        if args.check_config:
            print("configuration valid; source databases were not opened")
            return 0
        runtime = resolve_runtime_paths(config_path, args, config)
        source_lock = SourceLock(source_binding(config.account_dir))
        store = StateStore(runtime.state_db, source_binding(config.account_dir))
        try:
            store.bind_destination(config.base_url, config.event_path, config.mode)
        except Exception:
            store.close()
            raise
    except ConfigError as exc:
        if source_lock is not None:
            source_lock.close()
        print("another listener is running" if str(exc) == "another_listener_is_running" else "configuration invalid", file=sys.stderr)
        return 2
    except (OSError, sqlite3.Error):
        if source_lock is not None:
            source_lock.close()
        print("local state unavailable", file=sys.stderr)
        return 3

    status = StatusFile(runtime.status_json, store)
    sender = None
    poll = None
    heartbeat_account_id = ""
    try:
        if runtime.stop_file.exists():
            status.write(running=False, stopped=True)
            return 0
        # Construction is inert; source databases open only during poll methods.
        reader = reader_type(config.account_dir, config.keys_file)
        sender = LocalEventSender(config.base_url, config.bridge_token, event_path=config.event_path, mode=config.mode)
        if config.event_path == MARKET_API_PATH:
            heartbeat_account_id = importlib.import_module(
                "wxlocal.core.accounts"
            ).account_id_from_dir(config.account_dir)
        bridge = PaymentBridge(reader, store, sender, limit=args.limit)
        status.write(running=True)
        exit_code = 0
        while True:
            poll = bridge.poll_once()
            heartbeat_ok, heartbeat_error = sender.send_heartbeat(heartbeat_account_id, store, poll)
            if config.event_path == MARKET_API_PATH and heartbeat_ok is not None:
                status.last_heartbeat_success = heartbeat_ok
                status.last_heartbeat_at = utc_iso()
                status.last_heartbeat_error_code = heartbeat_error
            delivery = bridge.deliver_due(should_stop=runtime.stop_file.exists)
            status.record(poll, delivery)
            if heartbeat_ok is False:
                status.last_error_code = heartbeat_error
            status.write(running=not args.once)
            if not poll.success or delivery.failed_count or heartbeat_ok is False:
                exit_code = 4
            if args.once or runtime.stop_file.exists():
                break
            if _wait_for_stop(runtime.stop_file, args.poll_seconds):
                break
        status.write(running=False, stopped=not args.once)
        return exit_code
    except KeyboardInterrupt:
        status.write(running=False, stopped=True)
        return 130
    except (OSError, sqlite3.Error, RuntimeError):
        try:
            status.last_error_code = "local_runtime_error"
            status.write(running=False)
        except Exception:
            pass
        print("local bridge runtime error", file=sys.stderr)
        return 5
    finally:
        if sender is not None and poll is not None and config.event_path == MARKET_API_PATH:
            # Mark the listener unavailable immediately on a controlled stop.
            # A killed process instead ages out via the server's 60s lease.
            sender.send_heartbeat(heartbeat_account_id, store, poll, running=False)
        store.close()
        source_lock.close()


if __name__ == "__main__":
    raise SystemExit(main())

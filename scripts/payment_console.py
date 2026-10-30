"""Visible, restartable supervisor for the standalone Windows payment reader."""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import time
from types import SimpleNamespace

sys.path.insert(0, str(Path(__file__).resolve().parent))
from local_market_payments import ConfigError, SourceLock, load_config, normalize_base_url


def age_seconds(value):
    try:
        return max(0, int((datetime.now(timezone.utc) - datetime.fromisoformat(value.replace("Z", "+00:00"))).total_seconds()))
    except (ValueError, AttributeError, TypeError):
        return None


def dashboard(config, status, alive, restarts, exit_code=None, *, read_only=False):
    age = age_seconds(status.get("lastPollAt"))
    fresh = alive and status.get("running") and age is not None and age < 30
    heartbeat_age = age_seconds(status.get("lastHeartbeatAt"))
    connected = fresh and status.get("lastHeartbeatSuccess") and heartbeat_age is not None and heartbeat_age < 60
    def source(name):
        if not fresh:
            return "等待监听 / 状态已过期"
        return "正常" if status.get(name + "LastPollSuccess") else "读取异常（请检查微信登录和密钥）"
    def count(name):
        value = status.get(name, 0)
        return value if isinstance(value, int) and value >= 0 else 0
    error = status.get("lastErrorCode", "") or status.get("lastHeartbeatErrorCode", "")
    # Only internal enumerated codes enter the visible dashboard.
    import re
    if not isinstance(error, str) or not re.fullmatch(r"[a-z0-9_]{0,64}", error):
        error = "status_unavailable"
    mode = "本地调试" if config.mode == "local" else "正式 HTTPS"
    process_line = (f"监听状态：{'最近运行记录正常' if fresh else '未运行或状态已过期'}（只读观察）"
                    if read_only else f"监听进程：{'运行中' if alive else '已停止'}    自动重启：{restarts} 次")
    return "\n".join([
        "桌搭子 · 微信收款监听", "=" * 62,
        f"环境：{mode}    后台：{config.base_url}",
        process_line,
        f"微信收款通知：{source('notice')}",
        f"好友转账消息：{source('transfer')}",
        f"历史基线：{'已建立' if status.get('baselineReady') else '准备中'}",
        f"后台心跳：{'已连接' if connected else '未就绪 / 连接中断'}",
        f"最近读取：{str(age) + ' 秒前' if age is not None else '等待首次读取'}",
        f"待上报：{count('pendingCount')}    已上报：{count('deliveredCount')}    重试累计：{count('retryCount')}",
        f"订单已确认：{count('approvedCount')}    待后台核对：{count('reviewCount')}",
        f"需处理的上报：{count('attentionCount')}    最近错误：{error or '无'}",
        "", "已上报不等于订单已确认；迁移前的历史结果以后台订单为准。",
        ("后台监听独立运行，本窗口只读取状态。电脑和微信仍需保持在线。" if read_only
         else "保持电脑和微信在线，并保持本窗口运行。断网后待上报事件会重试。"),
        "超过订单有效期才送达的付款需要后台核对，不会直接放行。",
        ("关闭本窗口或按 Ctrl+C 只退出查看，不会停止后台监听。" if read_only
         else "按 Ctrl+C 安全停止；关闭窗口也会停止监听。"),
        (f"进程退出码：{exit_code}，请修正配置后重新启动。" if exit_code in (2, 3) else ""),
    ])


def read_status(path):
    try:
        if path.stat().st_size > 65536:
            return {}
        value = json.loads(path.read_text(encoding="utf-8"))
        return value if isinstance(value, dict) else {}
    except (OSError, ValueError):
        return {}


def watch_status(root, profile):
    # A viewer must work even when keys/dependencies are unavailable. Read only
    # the safe endpoint fields; never validate keys, acquire a lock or own a worker.
    stored_config = read_status(root / "config" / (profile + ".json"))
    endpoint = "配置不可用（仅显示本地状态记录）"
    try:
        if stored_config.get("mode", "local") == profile:
            endpoint = normalize_base_url(stored_config.get("baseUrl", ""), profile)
    except (ConfigError, TypeError, ValueError, AttributeError):
        pass
    config = SimpleNamespace(mode=profile, base_url=endpoint)
    status_path = root / "state" / profile / "bridge.status.json"
    try:
        while True:
            status = read_status(status_path)
            if sys.stdout.isatty():
                os.system("cls" if os.name == "nt" else "clear")
            print(dashboard(config, status, True, 0, read_only=True), flush=True)
            time.sleep(2)
    except KeyboardInterrupt:
        print("\n状态查看已关闭；后台监听未停止。", flush=True)
    return 0


def main(argv=None):
    parser = argparse.ArgumentParser(description="桌搭子收款监听状态窗口")
    parser.add_argument("--profile", choices=("local", "production"), default="local")
    actions = parser.add_mutually_exclusive_group()
    actions.add_argument("--check", action="store_true", help="仅校验配置和依赖，不读取微信、不联网")
    actions.add_argument("--status", action="store_true", help="只读持续查看状态；关闭窗口不会停止后台监听")
    args = parser.parse_args(argv)
    root = Path(__file__).resolve().parent.parent
    if args.status:
        return watch_status(root, args.profile)
    config_path = root / "config" / (args.profile + ".json")
    try:
        config = load_config(config_path)
        if config.mode != args.profile:
            raise ConfigError("profile_mode_mismatch")
    except (ConfigError, OSError):
        print("配置不可用。请检查 config 中对应环境的配置；未启动监听。")
        return 2
    bridge = root / "scripts" / "local_market_payments.py"
    command = [sys.executable, "-I", str(bridge), "--config", str(config_path)]
    if args.check:
        return subprocess.call(command + ["--check-config"])
    state = root / "state" / args.profile
    state.mkdir(parents=True, exist_ok=True)
    stop = state / "listener.stop"
    status_path = state / "bridge.status.json"
    try:
        lock = SourceLock(hashlib.sha256((str(root).casefold() + args.profile).encode()).hexdigest())
    except ConfigError:
        print("此环境的监听已经运行。查看正式连接状态请打开 payment-status.bat。")
        return 2
    stop.unlink(missing_ok=True)
    command += ["--state-db", str(state / "bridge.sqlite3"), "--status-json", str(status_path), "--stop-file", str(stop)]
    process = None
    restarts = 0
    next_start = 0.0
    terminal_error = None
    close_handler = None
    def request_stop():
        try:
            stop.write_text("stop\n", encoding="ascii")
        except OSError:
            pass
    if os.name == "nt":
        import ctypes
        @ctypes.WINFUNCTYPE(ctypes.c_bool, ctypes.c_uint)
        def close_handler(event):
            if event in (2, 5, 6):  # Console close, logoff, shutdown.
                request_stop()
                if process is not None:
                    try:
                        process.wait(timeout=3.5)
                    except subprocess.TimeoutExpired:
                        # Windows permits only a short console-close handler.
                        # An owned hidden worker must not outlive its dashboard.
                        process.terminate()
                        try:
                            process.wait(timeout=0.4)
                        except subprocess.TimeoutExpired:
                            pass
                return True
            return False  # Ctrl+C is handled by Python below.
        ctypes.windll.kernel32.SetConsoleCtrlHandler(close_handler, True)
    try:
        with (state / "listener.log").open("ab") as log:
            while not stop.exists():
                if process is not None and process.poll() is not None:
                    code = process.returncode
                    process = None
                    if code in (2, 3):
                        terminal_error = code
                    else:
                        restarts += 1
                        next_start = time.monotonic() + min(30, 2 ** min(restarts, 5))
                if process is None and terminal_error is None and time.monotonic() >= next_start:
                    process = subprocess.Popen(command, cwd=root, stdout=log, stderr=log,
                                               creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
                status = read_status(status_path)
                # Fixed command; no external values are interpolated into a shell.
                if sys.stdout.isatty():
                    os.system("cls" if os.name == "nt" else "clear")
                print(dashboard(config, status, process is not None and process.poll() is None,
                                restarts, terminal_error), flush=True)
                if terminal_error is not None:
                    return terminal_error
                time.sleep(2)
    except KeyboardInterrupt:
        print("\n正在保存监听进度并停止……", flush=True)
    finally:
        request_stop()
        if process is not None and process.poll() is None:
            try:
                process.wait(timeout=15)
            except subprocess.TimeoutExpired:
                process.terminate()
                process.wait(timeout=5)
        if close_handler is not None:
            ctypes.windll.kernel32.SetConsoleCtrlHandler(close_handler, False)
        lock.close()
    print("监听已停止；重新打开 BAT 可继续。")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

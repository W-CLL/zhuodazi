from datetime import datetime, timezone, timedelta
import importlib.util
import io
import json
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location("payment_console", Path(__file__).resolve().parents[1] / "payment_console.py")
console = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = console
SPEC.loader.exec_module(console)


class PaymentConsoleTests(unittest.TestCase):
    def setUp(self):
        self.config = SimpleNamespace(mode="local", base_url="http://127.0.0.1:18767")
        now = datetime.now(timezone.utc).isoformat()
        self.status = dict(running=True, lastPollAt=now, lastHeartbeatAt=now,
                           lastHeartbeatSuccess=True, noticeLastPollSuccess=True,
                           transferLastPollSuccess=True, baselineReady=True,
                           deliveredCount=8, approvedCount=3, reviewCount=2)

    def test_stale_or_stopped_process_never_shows_connected(self):
        fresh = console.dashboard(self.config, self.status, True, 0)
        self.assertIn("后台心跳：已连接", fresh)
        self.status["lastPollAt"] = (datetime.now(timezone.utc) - timedelta(minutes=2)).isoformat()
        self.assertIn("后台心跳：未就绪", console.dashboard(self.config, self.status, True, 0))
        self.assertIn("监听进程：已停止", console.dashboard(self.config, self.status, False, 0))

    def test_delivery_is_not_presented_as_approval(self):
        output = console.dashboard(self.config, self.status, True, 0)
        self.assertIn("已上报：8", output)
        self.assertIn("订单已确认：3", output)
        self.assertIn("待后台核对：2", output)

    def test_status_cannot_leak_payload_or_arbitrary_errors(self):
        self.status.update(reference="123AB", bridgeToken="private-token", accountId="private-account",
                           rawMessage="private-content", lastErrorCode="private-token\nprivate-content")
        output = console.dashboard(self.config, self.status, True, 0)
        for sensitive in ("123AB", "private-token", "private-account", "private-content"):
            self.assertNotIn(sensitive, output)
        self.assertIn("status_unavailable", output)

    def test_status_mode_reads_existing_record_without_owning_or_stopping_worker(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            state = root / "state" / "production"
            state.mkdir(parents=True)
            config = root / "config"
            config.mkdir()
            # No key or SDK configuration: status viewing must not depend on either.
            (config / "production.json").write_text(json.dumps({
                "mode": "production", "baseUrl": "https://in.desktoppet.online",
                "bridgeToken": "private-status-token"
            }), encoding="utf-8")
            status_file = state / "bridge.status.json"
            status_file.write_text(json.dumps(self.status), encoding="utf-8")
            before = status_file.read_bytes()
            output = io.StringIO()
            with patch.object(console, "__file__", str(root / "scripts" / "payment_console.py")), \
                    patch.object(console, "load_config", side_effect=AssertionError("status must not load keys")), \
                    patch.object(console, "SourceLock", side_effect=AssertionError("status must not acquire locks")), \
                    patch.object(console.subprocess, "Popen", side_effect=AssertionError("status must not start worker")), \
                    patch.object(console.subprocess, "call", side_effect=AssertionError("status must not run checks")), \
                    patch.object(console.time, "sleep", side_effect=[None, KeyboardInterrupt]), \
                    patch.object(console.sys, "stdout", output):
                self.assertEqual(console.main(["--profile", "production", "--status"]), 0)
            self.assertEqual(output.getvalue().count("后台心跳：已连接"), 2)
            self.assertIn("关闭本窗口或按 Ctrl+C 只退出查看，不会停止后台监听。", output.getvalue())
            self.assertNotIn("关闭窗口也会停止监听", output.getvalue())
            self.assertNotIn("private-status-token", output.getvalue())
            self.assertEqual(status_file.read_bytes(), before)
            self.assertFalse((state / "listener.stop").exists())
            self.assertEqual(sorted(item.name for item in state.iterdir()), ["bridge.status.json"])

    def test_status_mode_with_missing_files_never_creates_state_or_claims_connected(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            output = io.StringIO()
            with patch.object(console, "__file__", str(root / "scripts" / "payment_console.py")), \
                    patch.object(console, "SourceLock", side_effect=AssertionError("viewer is read-only")), \
                    patch.object(console.subprocess, "Popen", side_effect=AssertionError("viewer is read-only")), \
                    patch.object(console.time, "sleep", side_effect=KeyboardInterrupt), \
                    patch.object(console.sys, "stdout", output):
                self.assertEqual(console.main(["--status"]), 0)
            self.assertIn("未运行或状态已过期", output.getvalue())
            self.assertNotIn("后台心跳：已连接", output.getvalue())
            self.assertEqual(list(root.iterdir()), [])

    def test_status_and_check_are_mutually_exclusive_before_loading_configuration(self):
        with patch.object(console, "load_config", side_effect=AssertionError("no config expected")), \
                patch.object(console.sys, "stderr", io.StringIO()):
            with self.assertRaises(SystemExit) as raised:
                console.main(["--status", "--check"])
            self.assertEqual(raised.exception.code, 2)

    def test_read_only_view_does_not_mistake_old_status_for_a_live_process(self):
        self.status["lastPollAt"] = (datetime.now(timezone.utc) - timedelta(minutes=2)).isoformat()
        output = console.dashboard(self.config, self.status, True, 0, read_only=True)
        self.assertIn("未运行或状态已过期", output)
        self.assertIn("后台心跳：未就绪", output)
        self.assertNotIn("自动重启：0", output)


if __name__ == "__main__":
    unittest.main()

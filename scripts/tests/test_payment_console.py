from datetime import datetime, timezone, timedelta
import importlib.util
from pathlib import Path
import sys
from types import SimpleNamespace
import unittest

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


if __name__ == "__main__":
    unittest.main()

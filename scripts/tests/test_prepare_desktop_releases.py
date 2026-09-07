import importlib.util
from pathlib import Path
import subprocess
import unittest
from unittest.mock import patch


SPEC = importlib.util.spec_from_file_location(
    "desktop_releases", Path(__file__).resolve().parents[1] / "prepare-desktop-releases.py"
)
releases = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(releases)


def published_release(platform, version):
    return {
        "isDraft": False,
        "assets": [{"name": name} for name in releases.expected_assets(platform, version)],
    }


class DesktopReleaseTests(unittest.TestCase):
    def test_windows_stays_published_while_macos_gets_its_own_release(self):
        windows = published_release("windows", "3.2.8")
        with patch.object(releases, "read_release", side_effect=[windows, None, None]):
            with patch.object(releases, "changed_files", return_value="") as changes:
                windows_plan = releases.plan_release("windows", "3.2.8", "target")
                macos_plan = releases.plan_release("macos", "3.2.9", "target")
        self.assertTrue(windows_plan["published"])
        self.assertEqual(windows_plan["tag"], "v3.2.8")
        self.assertFalse(macos_plan["published"])
        self.assertEqual(macos_plan["tag"], "macos-v3.2.9")
        changes.assert_called_once_with("windows", "v3.2.8", "target")

    def test_legacy_unified_macos_release_is_not_rebuilt(self):
        legacy = published_release("macos", "3.2.8")
        with patch.object(releases, "read_release", side_effect=[None, legacy]):
            with patch.object(releases, "changed_files", return_value=""):
                plan = releases.plan_release("macos", "3.2.8", "target")
        self.assertTrue(plan["published"])
        self.assertEqual(plan["tag"], "v3.2.8")

    def test_changed_published_platform_requires_a_version_bump(self):
        for platform in ("windows", "macos"):
            with self.subTest(platform=platform):
                with patch.object(releases, "read_release", return_value=published_release(platform, "3.2.8")):
                    with patch.object(releases, "changed_files", return_value=f"{platform}/changed"):
                        with self.assertRaisesRegex(RuntimeError, "bump the"):
                            releases.plan_release(platform, "3.2.8", "target")

    def test_published_release_with_missing_packages_is_rejected(self):
        with patch.object(releases, "read_release", return_value={"isDraft": False, "assets": []}):
            with self.assertRaisesRegex(RuntimeError, "missing packages"):
                releases.plan_release("windows", "3.2.8", "target")

    def test_macos_does_not_reuse_a_windows_only_release(self):
        windows = published_release("windows", "3.2.9")
        with patch.object(releases, "read_release", side_effect=[None, windows]):
            plan = releases.plan_release("macos", "3.2.9", "target")
        self.assertFalse(plan["exists"])
        self.assertEqual(plan["tag"], "macos-v3.2.9")

    def test_draft_is_reused(self):
        with patch.object(releases, "read_release", return_value={"isDraft": True, "assets": []}):
            plan = releases.plan_release("macos", "3.2.9", "target")
        with patch.object(releases.subprocess, "run") as run:
            releases.prepare_release(plan, "target")
        command = run.call_args.args[0]
        self.assertEqual(command[:4], ["gh", "release", "edit", "macos-v3.2.9"])
        self.assertNotIn("--draft", command)

    def test_published_release_is_never_modified(self):
        with patch.object(releases.subprocess, "run") as run:
            releases.prepare_release({"published": True, "tag": "v3.2.8", "platform": "windows"}, "target")
        run.assert_not_called()

    def test_new_release_uses_exact_notes_target_and_draft(self):
        plan = {"published": False, "exists": False, "tag": "macos-v3.2.9", "platform": "macos", "version": "3.2.9"}
        with patch.object(releases.subprocess, "run") as run:
            releases.prepare_release(plan, "target")
        command = run.call_args.args[0]
        self.assertIn("--draft", command)
        self.assertIn("--latest=false", command)
        self.assertEqual(command[command.index("--notes") + 1], "更新/修复了一些功能")
        self.assertEqual(command[command.index("--target") + 1], "target")

    def test_read_release_only_treats_not_found_as_absent(self):
        missing = subprocess.CompletedProcess([], 1, "", "release not found\n")
        with patch.object(releases.subprocess, "run", return_value=missing):
            self.assertIsNone(releases.read_release("missing"))
        for message in ("HTTP 403: rate limit exceeded", "authentication failed", "connection failed"):
            with self.subTest(message=message):
                failure = subprocess.CompletedProcess([], 1, "", message)
                with patch.object(releases.subprocess, "run", return_value=failure):
                    with self.assertRaisesRegex(RuntimeError, "Unable to inspect"):
                        releases.read_release("unavailable")

    def test_macos_checks_shared_windows_assets(self):
        result = subprocess.CompletedProcess([], 0, "windows/assets/app-icon.png\n", "")
        with patch.object(releases.subprocess, "run", return_value=result) as run:
            self.assertEqual(releases.changed_files("macos", "tag", "target"), "windows/assets/app-icon.png")
        self.assertEqual(run.call_args.args[0][-2:], ["macos", "windows/assets"])


if __name__ == "__main__":
    unittest.main()

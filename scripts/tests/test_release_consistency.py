import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location(
    "release_consistency", Path(__file__).resolve().parents[1] / "check-release-consistency.py"
)
consistency = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(consistency)


class AndroidReleaseConsistencyTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        self.addCleanup(self.folder.cleanup)
        self.root = Path(self.folder.name)
        for directory in ("android/app", "mobile_ui", "docs/releases"):
            (self.root / directory).mkdir(parents=True)
        self.gradle = self.root / "android/app/build.gradle.kts"
        self.flutter = self.root / "mobile_ui/pubspec.yaml"
        self.notes = self.root / "docs/releases/android-1.4.0.md"
        self.gradle.write_text('versionName = "1.4.0"\nversionCode = 18\n', encoding="utf-8")
        self.flutter.write_text("version: 1.4.0+18\n", encoding="utf-8")
        self.notes.write_text("新增可继续的使用引导。\n", encoding="utf-8")
        root_patch = patch.object(consistency, "ROOT", self.root)
        root_patch.start()
        self.addCleanup(root_patch.stop)

    def test_android_uses_its_own_version_without_reading_desktop_metadata(self):
        self.assertEqual(consistency.read_android_version(), ("1.4.0", 18))

    def test_flutter_display_version_mismatch_fails_before_packaging(self):
        self.flutter.write_text("version: 3.3.0+18\n", encoding="utf-8")
        with self.assertRaisesRegex(SystemExit, "Flutter version"):
            consistency.read_android_version()

    def test_flutter_install_number_mismatch_fails_before_packaging(self):
        self.flutter.write_text("version: 1.4.0+17\n", encoding="utf-8")
        with self.assertRaisesRegex(SystemExit, "Flutter version"):
            consistency.read_android_version()

    def test_release_requires_nonempty_notes_for_its_actual_version(self):
        self.notes.unlink()
        with self.assertRaisesRegex(SystemExit, "release notes"):
            consistency.read_android_version()
        self.notes.write_text("  \n", encoding="utf-8")
        with self.assertRaisesRegex(SystemExit, "release notes"):
            consistency.read_android_version()

    def test_invalid_install_number_is_rejected(self):
        self.gradle.write_text('versionName = "1.4.0"\nversionCode = 0\n', encoding="utf-8")
        with self.assertRaisesRegex(SystemExit, "positive versionCode"):
            consistency.read_android_version()


if __name__ == "__main__":
    unittest.main()

from __future__ import annotations

import base64
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

SPEC = importlib.util.spec_from_file_location(
    "build_pet_market_pack", Path(__file__).resolve().parents[1] / "build_pet_market_pack.py")
builder = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(builder)
GIF = base64.b64decode("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7")


class PetMarketPackTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.first = self.root / "idle.gif"
        self.second = self.root / "wave.gif"
        self.first.write_bytes(GIF)
        self.second.write_bytes(GIF)
        self.output = self.root / "bundle.zip"

    def build(self, **overrides):
        arguments = dict(product_id="starter-cat", version="1.0.0", title="测试桌宠", output=self.output)
        arguments.update(overrides)
        return builder.build_pack([self.second, self.first], **arguments)

    def test_package_contains_matching_version_manifest_and_file_hashes(self):
        result = self.build()
        self.assertEqual(result["assetCount"], 2)
        with zipfile.ZipFile(self.output) as archive:
            manifest = json.loads(archive.read("pet-market.json"))
            self.assertEqual(manifest["productId"], "starter-cat")
            self.assertEqual(manifest["version"], "1.0.0")
            self.assertEqual(len(archive.infolist()), 3)
            for file in manifest["files"]:
                data = archive.read(file["path"])
                self.assertEqual(len(data), file["sizeBytes"])
                self.assertEqual(hashlib.sha256(data).hexdigest(), file["sha256"])

    def test_existing_output_is_preserved(self):
        self.output.write_bytes(b"previous archive")
        with self.assertRaises(FileExistsError):
            self.build()
        self.assertEqual(self.output.read_bytes(), b"previous archive")

    def test_invalid_gif_does_not_leave_an_archive(self):
        self.second.write_bytes(b"not a gif file")
        with self.assertRaises(ValueError):
            self.build()
        self.assertFalse(self.output.exists())

    def test_windows_unsafe_paths_and_invalid_ids_are_rejected(self):
        for path in ("../idle.gif", "/idle.gif", "gifs/CON.gif", "gifs/x.gif:exe", "gifs/.hidden.gif", "gifs\\idle.gif"):
            with self.subTest(path=path), self.assertRaises(ValueError):
                builder.validate_path(path)
        for product_id in ("../cat", "CAT", "a/b", ""):
            with self.subTest(product_id=product_id), self.assertRaises(ValueError):
                self.build(product_id=product_id)


if __name__ == "__main__":
    unittest.main()

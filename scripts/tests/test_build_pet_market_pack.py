from __future__ import annotations

import base64
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock
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

    def test_more_than_eighty_gifs_build_into_a_complete_installable_archive(self):
        sources = []
        for index in range(81):
            source = self.root / f"pose-{index:03d}.gif"
            source.write_bytes(GIF)
            sources.append(source)
        result = builder.build_pack(sources, product_id="many-gifs", version="1.0.0",
                                    title="多动作桌宠", output=self.output)
        self.assertEqual(result["assetCount"], 81)
        with zipfile.ZipFile(self.output) as archive:
            manifest = json.loads(archive.read("pet-market.json"))
            self.assertEqual(len(manifest["files"]), 81)
            self.assertEqual(len(archive.infolist()), 82)
            self.assertEqual({item["path"] for item in manifest["files"]},
                             {f"gifs/{source.name}" for source in sources})
            self.assertTrue(all(entry.compress_type == zipfile.ZIP_STORED for entry in archive.infolist()))
            for item in manifest["files"]:
                self.assertEqual(archive.read(item["path"]), GIF)

    def test_large_valid_gif_is_stored_without_exceeding_client_compression_ratio(self):
        # Valid comment sub-blocks enlarge a real GIF while retaining a tiny image canvas.
        comment = b"\x21\xfe" + (b"\xff" + b"a" * 255) * 32769 + b"\0"
        large_gif = GIF[:-1] + comment + GIF[-1:]
        self.assertGreater(len(large_gif), 8 * 1024 * 1024)
        self.first.write_bytes(large_gif)
        result = self.build()
        self.assertEqual(result["assetCount"], 2)
        with zipfile.ZipFile(self.output) as archive:
            manifest = json.loads(archive.read("pet-market.json"))
            entry = archive.getinfo("gifs/idle.gif")
            self.assertEqual(entry.compress_type, zipfile.ZIP_STORED)
            self.assertEqual(entry.file_size, entry.compress_size)
            self.assertEqual(archive.read(entry), large_gif)
            item = next(item for item in manifest["files"] if item["path"] == entry.filename)
            self.assertEqual(item["sizeBytes"], len(large_gif))
            self.assertEqual(item["sha256"], hashlib.sha256(large_gif).hexdigest())
        self.assertEqual(result["sha256"], hashlib.sha256(self.output.read_bytes()).hexdigest())

    def test_package_budget_covers_resources_manifest_and_zip_directory(self):
        self.build()
        with zipfile.ZipFile(self.output) as archive:
            expanded_size = sum(item.file_size for item in archive.infolist())
        package_size = self.output.stat().st_size
        for budget in (len(GIF) * 2 - 1, expanded_size - 1, package_size - 1):
            target = self.root / f"over-budget-{budget}.zip"
            with self.subTest(budget=budget), mock.patch.object(builder, "MAX_TOTAL_BYTES", budget):
                with self.assertRaises(ValueError):
                    self.build(output=target)
                self.assertFalse(target.exists(), "Rejected uploads must not leave a partial archive.")
        with mock.patch.object(builder, "MAX_TOTAL_BYTES", package_size):
            accepted = self.build(output=self.root / "at-budget.zip")
        self.assertEqual(accepted["sizeBytes"], package_size)
        self.assertEqual(self.first.read_bytes(), GIF)
        self.assertEqual(self.second.read_bytes(), GIF)

    def test_manifest_byte_budget_is_enforced_before_creating_output(self):
        self.build()
        with zipfile.ZipFile(self.output) as archive:
            manifest_size = len(archive.read("pet-market.json"))
        rejected = self.root / "large-manifest.zip"
        with mock.patch.object(builder, "MAX_MANIFEST_BYTES", manifest_size - 1):
            with self.assertRaisesRegex(ValueError, "清单"):
                self.build(output=rejected)
        self.assertFalse(rejected.exists())
        with mock.patch.object(builder, "MAX_MANIFEST_BYTES", manifest_size):
            self.assertEqual(self.build(output=rejected)["assetCount"], 2)

    def test_gif_dimensions_allow_4096_and_reject_larger_canvases(self):
        for side in (4096, 4097):
            image = bytearray(GIF)
            image[6:10] = side.to_bytes(2, "little") * 2
            self.first.write_bytes(image)
            target = self.root / f"canvas-{side}.zip"
            if side == 4096:
                self.assertEqual(self.build(output=target)["assetCount"], 2)
            else:
                with self.assertRaisesRegex(ValueError, "4096"):
                    self.build(output=target)
                self.assertFalse(target.exists())

    def test_single_file_and_zip32_entry_limits_are_retained(self):
        with mock.patch.object(builder, "MAX_GIF_BYTES", len(GIF) - 1):
            with self.assertRaises(ValueError):
                self.build()
        with self.assertRaises(ValueError):
            builder.build_pack([self.first] * 65534, product_id="many-gifs", version="1.0.0",
                               title="过多素材", output=self.output)
        self.assertFalse(self.output.exists())

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

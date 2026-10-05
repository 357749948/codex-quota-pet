"""Offline fixtures are original rectangles/color ramps, never bundled artwork."""
import hashlib
import io
import json
from pathlib import Path
import struct
import sys
import tempfile
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools"))
import asar
import generate_templates as generator
import numpy as np
from PIL import Image


def archive_bytes(content=b"synthetic local asset", extra=None):
    asset = dict(size=len(content), offset="0")
    if extra:
        asset.update(extra)
    header = dict(files={"webview": {"files": {"assets": {"files": {
        "null-signal-spritesheet-synthetic.webp": asset}}}}})
    raw = json.dumps(header, separators=(",", ":")).encode()
    padded = (len(raw) + 3) & ~3
    header_size = padded + 8
    return struct.pack("<4I", 4, header_size, header_size - 4, len(raw)) + raw + b"\0" * (padded - len(raw)) + content


class ArchiveTests(unittest.TestCase):
    def read(self, data):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "app.asar"
            path.write_bytes(data)
            return asar.read_sprite(path)

    def test_reads_only_requested_entry(self):
        name, content = self.read(archive_bytes())
        self.assertEqual(content, b"synthetic local asset")
        self.assertEqual(name, "null-signal-spritesheet-synthetic.webp")

    def test_truncated_and_out_of_bounds(self):
        for value in (b"", b"\0" * 16, archive_bytes()[:-1], archive_bytes(extra={"offset": "-1"})):
            with self.subTest(size=len(value)), self.assertRaises((ValueError, KeyError)):
                self.read(value)

    def test_link_and_unpacked_rejected(self):
        for extra in ({"link": "other.webp"}, {"unpacked": True}):
            with self.subTest(extra=extra), self.assertRaises(ValueError):
                self.read(archive_bytes(extra=extra))

    def test_archive_discovery(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            resources = root / "app" / "resources"
            resources.mkdir(parents=True)
            target = resources / "app.asar"
            target.write_bytes(archive_bytes())
            self.assertEqual(asar.find_archive(root), target)
            self.assertEqual(asar.find_archive(target), target)
            with self.assertRaises(ValueError):
                asar.find_archive(resources)


class GeneratorTests(unittest.TestCase):
    @staticmethod
    def tile():
        tile = np.zeros((generator.H, generator.W, 4), dtype=np.uint8)
        tile[12:197, 20:172] = [60, 63, 68, 255]
        # An original rectangular test monitor, not a robot or copied sprite.
        tile[75:137, 54:138] = [17, 12, 17, 255]
        tile[96:117, 74:118] = [185, 35, 32, 255]
        return tile

    def test_runs_roundtrip(self):
        mask = np.zeros((generator.H, generator.W), dtype=np.uint8)
        mask[20:24, 30:70] = 1
        mask[25, 31:33] = 1
        restored = np.zeros_like(mask)
        for y, x, width in generator.runs(mask):
            restored[y, x:x + width] = 1
        np.testing.assert_array_equal(mask, restored)

    def test_synthetic_screen_and_samples(self):
        mask, red, rect, _, background = generator.screen_mask(self.tile())
        x, y, width, height = rect
        self.assertGreaterEqual(width, 28)
        self.assertGreaterEqual(height, 14)
        self.assertTrue(np.all(mask[y:y + height, x:x + width]))
        self.assertTrue(np.all(mask[red]))
        self.assertEqual(background, [17, 12, 17])
        points = generator.sample_points(self.tile(), mask)
        self.assertGreaterEqual(len(points), 40)
        self.assertLessEqual(len(points), 250)
        self.assertTrue(all(not mask[p[1], p[0]] for p in points))

    def test_dimensions_and_unknown_fingerprint(self):
        with self.assertRaises(ValueError):
            generator.build_frames(np.zeros((10, 10, 4), dtype=np.uint8))
        with self.assertRaisesRegex(ValueError, "fingerprint"):
            generator.make_document(b"Not an official or compatible asset")

    def test_synthetic_full_sheet(self):
        image = np.zeros((2288, 1536, 4), dtype=np.uint8)
        image[:generator.H, :generator.W] = self.tile()
        frames = generator.build_frames(image)
        self.assertEqual(len(frames), 1)
        self.assertTrue(frames[0]["supported"])
        self.assertEqual(frames[0]["index"], 0)

    def test_atomic_local_cache(self):
        with tempfile.TemporaryDirectory() as temporary:
            document = dict(spriteSha256=hashlib.sha256(b"synthetic").hexdigest(), frames=[])
            with mock.patch.dict("os.environ", {"LOCALAPPDATA": temporary}):
                result = generator.write_cache(document)
                self.assertEqual(result.parent, Path(temporary) / "CodexQuotaPetData" / "cache")
                self.assertEqual(json.loads(result.read_text()), document)
                self.assertEqual(list(result.parent.glob("*.tmp")), [])
                generator.write_cache(document)


if __name__ == "__main__":
    unittest.main()

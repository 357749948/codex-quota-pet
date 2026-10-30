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
    return archive_with_assets({"null-signal-spritesheet-synthetic.webp": asset}, content)


def archive_with_assets(assets, content):
    header = dict(files={"webview": {"files": {"assets": {"files": assets}}}})
    raw = json.dumps(header, separators=(",", ":")).encode()
    padded = (len(raw) + 3) & ~3
    header_size = padded + 8
    return struct.pack("<4I", 4, header_size, header_size - 4, len(raw)) + raw + b"\0" * (padded - len(raw)) + content


def archive_entries(entries):
    assets, payload = {}, bytearray()
    for name, content, extra in entries:
        item = dict(size=len(content), offset=str(len(payload)))
        item.update(extra)
        assets[name] = item
        payload.extend(content)
    return archive_with_assets(assets, bytes(payload))


def synthetic_webp(color):
    output = io.BytesIO()
    Image.new("RGB", (3, 2), color).save(output, format="WEBP", lossless=True)
    return output.getvalue()


class ArchiveTests(unittest.TestCase):
    def read(self, data, expected_hash=None):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "app.asar"
            path.write_bytes(data)
            return asar.read_sprite(path, expected_hash=expected_hash)

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

    def test_hashed_name_uses_supported_content_not_filename(self):
        content = synthetic_webp("#28517a")
        other = synthetic_webp("#8f6231")
        data = archive_entries([
            ("111111111111.webp", other, {}),
            ("222222222222.webp", content, {}),
            ("333333333333.webp", other, {}),
        ])
        expected = hashlib.sha256(content).hexdigest()
        self.assertEqual(self.read(data, expected), ("222222222222.webp", content))
        with self.assertRaisesRegex(ValueError, "fingerprint"):
            self.read(data, hashlib.sha256(b"unsupported synthetic sprite").hexdigest())

    def test_named_entry_retains_precedence_and_unique_requirement(self):
        content = synthetic_webp("#28517a")
        expected = hashlib.sha256(content).hexdigest()
        named = "null-signal-spritesheet-synthetic.webp"
        data = archive_entries([(named, b"named but not compatible", {}), ("123abc.webp", content, {})])
        self.assertEqual(self.read(data, expected), (named, b"named but not compatible"))
        # A caller must still perform the existing fingerprint validation;
        # fallback cannot silently substitute a different image for a named one.
        data = archive_entries([(named, content, {}), ("null-signal-spritesheet-other.webp", content, {})])
        with self.assertRaisesRegex(ValueError, "exactly one"):
            self.read(data, expected)

    def test_duplicate_fingerprint_rejected(self):
        content = synthetic_webp("#28517a")
        data = archive_entries([("111.webp", content, {}), ("222.webp", content, {})])
        with self.assertRaisesRegex(ValueError, "Ambiguous"):
            self.read(data, hashlib.sha256(content).hexdigest())

    def test_hashed_scan_skips_nonregular_entries(self):
        content = synthetic_webp("#28517a")
        data = archive_entries([
            ("link.webp", content, {"link": "222.webp"}),
            ("external.webp", content, {"unpacked": True}),
            ("directory.webp", content, {"files": {}}),
            ("ignored.png", content, {}),
            ("222.webp", content, {}),
        ])
        self.assertEqual(self.read(data, hashlib.sha256(content).hexdigest()), ("222.webp", content))

    def test_hashed_scan_validates_ranges_before_reading(self):
        content = synthetic_webp("#28517a")
        expected = hashlib.sha256(content).hexdigest()
        invalid = ({"offset": "-1"}, {"offset": "1.5"}, {"offset": 0},
                   {"offset": "999999"}, {"size": 0}, {"size": True},
                   {"size": asar.MAX_ASSET + 1})
        for extra in invalid:
            data = archive_entries([("111.webp", content, {}), ("222.webp", content, extra)])
            with self.subTest(extra=extra), mock.patch.object(asar, "_read_entry") as read:
                with self.assertRaises(ValueError):
                    self.read(data, expected)
                read.assert_not_called()
        with self.assertRaisesRegex(ValueError, "bounds"):
            self.read(archive_entries([("111.webp", content, {})])[:-1], expected)

    def test_hashed_scan_limits_before_payload_reads(self):
        expected = hashlib.sha256(b"x").hexdigest()
        assets = {"%04d.webp" % index: dict(size=1, offset="0") for index in range(asar.MAX_SCAN_ASSETS + 1)}
        # ASAR entries may share a byte range. Bounds still limit work even when
        # the actual archive stays small and a matching entry appears first.
        cases = [archive_with_assets(assets, b"x")]
        assets = {"%02d.webp" % index: dict(size=asar.MAX_ASSET, offset="0")
                  for index in range(asar.MAX_SCAN_BYTES // asar.MAX_ASSET + 1)}
        cases.append(archive_with_assets(assets, b"\0" * asar.MAX_ASSET))
        for data in cases:
            with self.subTest(archive_size=len(data)), mock.patch.object(asar, "_read_entry") as read:
                with self.assertRaisesRegex(ValueError, "scan limit"):
                    self.read(data, expected)
                read.assert_not_called()

    def test_archive_discovery(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            resources = root / "app" / "resources"
            resources.mkdir(parents=True)
            target = resources / "app.asar"
            target.write_bytes(archive_bytes())
            # Windows temporary paths may use 8.3 names while resolve() returns
            # their long spelling. Assert file identity, not path spelling.
            self.assertTrue(asar.find_archive(root).samefile(target))
            self.assertTrue(asar.find_archive(target).samefile(target))
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

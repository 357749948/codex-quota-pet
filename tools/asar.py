"""Bounded, read-only access to packed ASAR files; never extracts files to disk."""
import hashlib
import json
from pathlib import Path
import struct

MAX_HEADER = 16 * 1024 * 1024
MAX_ASSET = 8 * 1024 * 1024
MAX_SCAN_ASSETS = 4096
MAX_SCAN_BYTES = 128 * 1024 * 1024


def _packed_range(item, header_size, archive_size):
    if not isinstance(item, dict) or item.get("unpacked") or "link" in item or "files" in item:
        raise ValueError("Sprite must be a packed regular ASAR entry")
    size = item.get("size")
    offset = item.get("offset")
    # ASAR offsets are decimal strings. Reject signs, fractions and coercions.
    if (type(size) is not int or not 1 <= size <= MAX_ASSET or
            not isinstance(offset, str) or not offset.isascii() or not offset.isdecimal()):
        raise ValueError("Invalid sprite range")
    absolute = 8 + header_size + int(offset)
    if absolute + size > archive_size:
        raise ValueError("Sprite exceeds ASAR bounds")
    return absolute, size


def _read_entry(stream, absolute, size):
    stream.seek(absolute)
    content = stream.read(size)
    if len(content) != size:
        raise ValueError("Truncated sprite")
    return content


def read_sprite(archive, expected_hash=None):
    archive = Path(archive)
    with archive.open("rb") as stream:
        prefix = stream.read(16)
        if len(prefix) != 16:
            raise ValueError("Truncated ASAR header")
        size_payload, header_size, pickle_size, json_size = struct.unpack("<4I", prefix)
        if (size_payload != 4 or not 8 <= header_size <= MAX_HEADER or
                pickle_size + 4 != header_size or not 1 <= json_size <= header_size - 8):
            raise ValueError("Invalid ASAR header")
        raw = stream.read(json_size)
        if len(raw) != json_size:
            raise ValueError("Truncated ASAR JSON")
        header = json.loads(raw.decode("utf-8"))
        assets = header["files"]["webview"]["files"]["assets"]["files"]
        matches = [(name, item) for name, item in assets.items()
                   if name.startswith("null-signal-spritesheet-") and name.endswith(".webp")]
        archive_size = archive.stat().st_size
        if len(matches) > 1:
            raise ValueError("Expected exactly one Null Signal sprite")
        if matches:
            name, item = matches[0]
            return name, _read_entry(stream, *_packed_range(item, header_size, archive_size))
        if expected_hash is None:
            raise ValueError("Expected exactly one Null Signal sprite")
        if (not isinstance(expected_hash, str) or len(expected_hash) != 64 or
                any(character not in "0123456789abcdef" for character in expected_hash)):
            raise ValueError("Invalid expected sprite fingerprint")

        # Recent packages use content-hashed names. Search only this bounded
        # image directory, and accept only the already-supported content hash.
        candidates = []
        total_bytes = 0
        for name, item in assets.items():
            if not name.endswith(".webp"):
                continue
            if isinstance(item, dict) and (item.get("unpacked") or "link" in item or "files" in item):
                continue
            absolute, size = _packed_range(item, header_size, archive_size)
            candidates.append((name, absolute, size))
            total_bytes += size
            if len(candidates) > MAX_SCAN_ASSETS or total_bytes > MAX_SCAN_BYTES:
                raise ValueError("Sprite discovery scan limit exceeded")

        matched = None
        for name, absolute, size in candidates:
            content = _read_entry(stream, absolute, size)
            if hashlib.sha256(content).hexdigest() != expected_hash:
                continue
            if matched is not None:
                raise ValueError("Ambiguous compatible Null Signal sprite")
            matched = name, content
        if matched is None:
            raise ValueError("No compatible Null Signal sprite fingerprint found")
        return matched


def find_archive(location):
    """Accept app.asar, Codex.exe, or the package/app directory, without recursion."""
    path = Path(location).expanduser().resolve(strict=True)
    if path.is_file():
        if path.name.lower() == "app.asar":
            return path
        path = path.parent
    candidates = [path / "resources" / "app.asar", path / "app" / "resources" / "app.asar"]
    found = [candidate for candidate in candidates if candidate.is_file()]
    if len(found) != 1:
        raise ValueError("Could not find one desktop resources/app.asar under the supplied path")
    return found[0]

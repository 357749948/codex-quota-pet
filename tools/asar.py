"""Bounded, read-only access to packed ASAR files; never extracts files to disk."""
import json
from pathlib import Path
import struct

MAX_HEADER = 16 * 1024 * 1024
MAX_ASSET = 8 * 1024 * 1024


def read_sprite(archive):
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
        if len(matches) != 1:
            raise ValueError("Expected exactly one Null Signal sprite")
        name, item = matches[0]
        if item.get("unpacked") or "link" in item:
            raise ValueError("Sprite must be a packed regular ASAR entry")
        size = item["size"]
        offset = int(item["offset"])
        if type(size) is not int or not 1 <= size <= MAX_ASSET or offset < 0:
            raise ValueError("Invalid sprite range")
        absolute = 8 + header_size + offset
        if absolute + size > archive.stat().st_size:
            raise ValueError("Sprite exceeds ASAR bounds")
        stream.seek(absolute)
        content = stream.read(size)
        if len(content) != size:
            raise ValueError("Truncated sprite")
        return name, content


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

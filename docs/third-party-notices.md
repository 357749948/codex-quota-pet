# Dependencies and material provenance

The repository's own code, written documentation, original SVG diagram, and synthetic test-generation code use the [MIT license](../LICENSE). Third-party UI and pet artwork visible in documentation screenshots are excluded from that license. No third-party source, binary, font, extracted Codex sprite image, or generated recognition table is bundled in the repository.

## Locally installed development dependencies

The template preparation script installs the pinned versions recorded in [tools/requirements.txt](../tools/requirements.txt). These packages are separate works with their own licenses:

| Component | Purpose | Upstream license information |
| --- | --- | --- |
| Python 3.13 | Template-generation runtime | [PSF license and bundled component notices](https://docs.python.org/3.13/license.html) |
| Pillow | Image decoding | [MIT-CMU license](https://raw.githubusercontent.com/python-pillow/Pillow/main/LICENSE) |
| NumPy | Array operations | [BSD 3-Clause license](https://raw.githubusercontent.com/numpy/numpy/main/LICENSE.txt) |
| opencv-python-headless | Image analysis without GUI dependencies | [Python packaging MIT license](https://raw.githubusercontent.com/opencv/opencv-python/4.x/LICENSE.txt), [OpenCV Apache 2.0 license](https://raw.githubusercontent.com/opencv/opencv/4.x/LICENSE), and [bundled third-party notices](https://raw.githubusercontent.com/opencv/opencv-python/4.x/LICENSE-3RD-PARTY.txt) |

Binary wheels can contain additional libraries. Their installed license files and notices apply; the table is not a replacement for those files. This source release does not redistribute Python environments or wheels. Any future binary/dependency bundle requires a separate inventory and inclusion of its applicable notices.

## Windows and Codex

The application references .NET Framework/WPF and Windows APIs supplied by the user's system. It uses the installed Consolas font without bundling font files. Microsoft components remain subject to their own terms.

Codex is separately installed software. OpenAI names are used to identify compatibility, not to imply endorsement. The repository includes two cropped desktop-validation screenshots under `docs/screenshots`: the quota overlay on Codex's Null Signal pet and a close-up of the reset-time tip. They contain third-party pet artwork and a small wallpaper background, but no unrelated application windows, messages, or account identifiers. They are documentation examples, not runtime assets, extracted sprites, or recognition templates.

The screenshots document the application's behavior. Their inclusion does not place the third-party material under MIT or grant rights to reuse that material. This project makes no claim of a license from OpenAI to redistribute its artwork.

The local preparation tool reads a supported sprite from the user's installed Codex and generates a local cache. Asset hashes are compatibility identifiers, not artwork. The project has not established permission to redistribute Codex's built-in art or the resulting derived recognition data. Keeping those files local does not create or transfer third-party rights; the project's MIT license does not cover them.

Use original synthetic fixtures for tests and CI. Keep extracted sprites and derived recognition data out of commits, issue attachments, CI artifacts, and releases. Proposed additions to the curated documentation screenshots require separate review of their provenance, third-party material, and private content; existing screenshots are not permission to publish other Codex assets.

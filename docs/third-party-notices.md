# Dependencies and material provenance

The repository's own code, documentation, original SVG diagram, and synthetic test-generation code use the [MIT license](../LICENSE). No third-party source, binary, font, Codex sprite image, or generated recognition table is bundled in the source release.

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

Codex is separately installed software. OpenAI names are used to identify compatibility, not to imply endorsement. The source repository includes no OpenAI logos, pet artwork, personal desktop screenshots, or recognition templates extracted from that artwork.

The local preparation tool reads a supported sprite from the user's installed Codex and generates a local cache. Asset hashes are compatibility identifiers, not artwork. The project has not established permission to redistribute Codex's built-in art or the resulting derived recognition data. Keeping those files local does not create or transfer third-party rights; the project's MIT license does not cover them.

Contributors should publish original synthetic fixtures and code only. Do not add original or derived Codex image data to commits, issue attachments, CI artifacts, or releases without separately establishing the relevant rights.

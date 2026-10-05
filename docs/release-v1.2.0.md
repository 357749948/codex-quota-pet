# v1.2.0 — first public source release

Codex Quota Pet displays the remaining quota as large digits on the Null Signal pet's face screen. It follows the pet, polls quota while visible, and restores the original image when a frame cannot be recognized safely. Its passive window is designed to let mouse input through without taking keyboard focus.

This release publishes the Windows x64 C#/.NET Framework source, reproducible build entry points, offline synthetic tests, and Chinese/English documentation under the MIT license. It is intended for developers who can build locally; no ready-to-run executable is attached.

Recognition templates are generated from the user's own supported Codex installation and stay on that computer. The repository and source archives contain no Codex pet artwork, derived recognition templates, account data, or personal desktop screenshots.

Start with the [README](https://github.com/357749948/codex-quota-pet/blob/v1.2.0/README.md). Python 3.13 x64 is needed for local template preparation; it is not needed by the running overlay. See [compatibility](https://github.com/357749948/codex-quota-pet/blob/v1.2.0/docs/compatibility.md) for supported assets, actual validation results, and remaining desktop-test limits.

This is an unofficial project, not affiliated with or endorsed by OpenAI.

# Contributing

Bug reports and small, focused pull requests are welcome. The current target is Windows x64, .NET Framework 4.8, and the Null Signal pet. Please discuss new platforms, pets, or background services before implementing them.

## Development

1. Follow the [README](README.en.md) to build and prepare local templates.
2. Run `powershell -NoProfile -ExecutionPolicy Bypass -File .\test.ps1` for offline tests.
3. Add a regression test when changing quota parsing, process ownership, template validation, or drawing boundaries.
4. State what you tested. Separate automated tests from actual desktop observations, and mark unavailable devices/scaling configurations as untested.

The production app uses C# 5 and the Framework compiler, without runtime NuGet packages. Python is a development/template-generation dependency. Keep the existing click-through behavior, missing-data semantics, stale-data handling, and current-user installation boundary.

## Material that belongs in a pull request

Use synthetic account responses and original generated test images. Do not commit extracted Codex artwork, local template JSON, compiled applications, login files, machine-specific reports, or real account diagnostics. The curated README screenshots under `docs/screenshots` are documentation examples, not test fixtures; additions need separate review for provenance, third-party material, and private content. See [material notices](docs/third-party-notices.md). `.gitignore` is a convenience, not a publication review: inspect the complete staged diff.

Changes to supported sprite compatibility must be checked locally against the user's installed Codex. Publish code, compatibility fingerprints, and test outcomes; keep source artwork and derived recognition data local. Do not broaden a compatibility fingerprint without checking all animation frames and safe text regions.

## Releases

`VERSION` is the version source. Build metadata and protocol identification are derived from it; a release tag is `v` followed by that value. The v1.2.0 release distributes source only. Release preparation requires green Windows CI, a fresh-checkout build, and an honest compatibility record. Do not attach `dist`, local templates, or personal desktop evidence to the public release.

Contributions to the project's own code and documentation are accepted under the [MIT license](LICENSE). Do not include third-party material unless its provenance and applicable permission have been established.

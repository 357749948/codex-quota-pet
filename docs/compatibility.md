# Compatibility and validation

## Supported target

- Windows x64 with .NET Framework 4.8 and an interactive unlocked desktop.
- Codex desktop's **Null Signal** pet with the specific sprite fingerprint accepted by the generator.
- An independently available native Codex CLI signed in with a ChatGPT account.
- Chinese tray/UI text and Beijing-time reset display.

The initial compatibility baseline is Codex desktop package **26.928.4866.0**. The supported sprite's SHA-256 is `a816f7488c187ffe8b7f5d58319deb6cfa591f98c219ef06cbaf10d1f9f330db`. Template preparation checks the sprite fingerprint, not just the application version or pet name. A later package containing an identical supported asset may work; a changed asset fails closed until the generator and recognition behavior have been reviewed.

Only one unambiguous visible pet is supported. Extreme perspective/glitch frames intentionally restore the original image. Other pets, macOS/Linux, ARM64, and headless/service sessions are outside the initial target.

## v1.2.0 verification record

Validation was performed on Windows 11 build 26300 x64, with Codex desktop 26.928.4866.0 and 200% display scaling. The build passed in Windows PowerShell 5.1. Template-generation checks used Python 3.13.12. A passing build does not count as a desktop test; synthetic geometry tests do not establish mixed-monitor compatibility.

| Check | Status |
| --- | --- |
| Clean-checkout build and setup | Passed in Windows PowerShell 5.1: fresh clone, isolated dependency installation, local template generation, complete test suite, opt-in live quota read and desktop probe |
| Offline quota parsing, error, retry, process ownership and rendering scenarios | Passed: production self-tests and the offline C# suite, including the UTF-8-console transport regression |
| Synthetic template generation and input validation | Passed: 9 Python tests |
| C# template loader | Passed: C# 5 compilation and loading from the actual running Codex installation |
| Synthetic matcher tests | Passed in the offline suite |
| Large `0%`, `63%`, `100%` and `--` rendering bounds | Passed: visible glyphs remain inside the safe text rectangle |
| Local supported-asset template generation | Passed: all 74 frames, including 46 supported masks, exactly match the predecessor's local recognition data |
| Local full-animation matcher validation | Passed: 330 cases across 96/160/192-pixel sizes, overlay feedback, frame transitions and negative backgrounds; original materials remain local |
| Real CLI account quota read | Passed: remaining quota, window duration and reset time matched the same account's desktop usage tool; values are not published |
| Live v1.2.0 overlay at 200% scaling | Passed: pet node found, template ready and overlay visible |
| Readability, pet clicks, dragging, hiding and showing | Passed in user testing; a 180-second local trace observed movement and two hide/reappear cycles, and the user reported no issues |
| Duplicate launch | Passed: second launch exited successfully without another overlay |
| Keyboard-focus retention | Passive-window properties passed offline checks; not separately confirmed by an interactive typing test |
| Lock/unlock, Codex restart and sleep/resume | Not separately verified |
| Isolated installation, startup choice, upgrade and uninstall | Passed: new install defaults, retained startup selection, legacy migration, installed PowerShell and batch uninstall, unrelated-file preservation and reinstall |
| Mixed-DPI cross-monitor movement and other scaling | Not yet physically verified |

The live smoke test ran the new v1.2.0 candidate. It did not replace the existing installed program; the previous installed version was restored afterward. Desktop observations establish the listed interactions only, not unperformed lock/restart/sleep or cross-monitor scenarios. A real Windows sign-out/sign-in was not performed; startup shortcut registration was verified in the isolated installer tests.

## Local validation procedure

1. In a clean checkout, follow the README in order: build, generate templates, run offline tests.
2. Run `test.ps1 -Live` only when prepared to read the CLI's actual account. Compare percentage, quota window and reset time with the same account's Codex usage display; do not publish account values.
3. With Null Signal visible, use `test.ps1 -DesktopProbe`. A found node confirms discovery only. Check the rendered digits and animation visually, then drag, hide and show the pet.
4. Check that clicks reach the pet and the current application retains typing focus. Start the app again and confirm only one overlay exists.
5. Lock/unlock, restart Codex, and test sleep/resume. Observe hidden/recovered state. Test actual monitors and record their scaling, rather than inferring cross-monitor behavior from calculations.
6. Run installer tests in an isolated test location. Test startup registration separately from a real Windows sign-in. Preserve an existing working installation during release preparation.

Do not attach original sprite files, templates, or private screenshots to the validation record. Report sanitized pass/fail outcomes and relevant software versions. If a check cannot be performed, keep it explicitly unverified.

# Compatibility and validation

## Supported target

- Windows x64 with .NET Framework 4.8 and an interactive unlocked desktop.
- Codex desktop's **Null Signal** pet with the specific sprite fingerprint accepted by the generator.
- An independently available native Codex CLI signed in with a ChatGPT account.
- Chinese tray/UI text and Beijing-time reset display.

The initial compatibility baseline is Codex desktop package **26.928.4866.0**. The supported sprite's SHA-256 is `a816f7488c187ffe8b7f5d58319deb6cfa591f98c219ef06cbaf10d1f9f330db`. Template preparation checks the sprite fingerprint, not just the application version or pet name. A later package containing an identical supported asset may work; a changed asset fails closed until the generator and recognition behavior have been reviewed.

Only one unambiguous visible pet is supported. Extreme perspective/glitch frames intentionally restore the original image. Other pets, macOS/Linux, ARM64, and headless/service sessions are outside the initial target.

## v1.3.1 polling interval

Normal quota polling while the pet is visible now runs every 60 seconds. Manual refresh, appearance, resume, reset-time checks and quota events retain their existing behavior. Failure backoff and the 90-second stale / 5-minute offline thresholds are unchanged. The complete offline suite passed, and the local 1.3.1 upgrade was verified with live quota, one running instance and the existing login-startup preference preserved.

## v1.3.0 verification record

Version 1.3.0 on `main` adds reset-time hover tips. It is not a new GitHub Release; the published v1.2.0 archive remains unchanged. Validation below is tracked separately from the historical v1.2.0 results; unperformed desktop scenarios remain explicitly unverified.

| Check | Status |
| --- | --- |
| Existing offline suite and build | Passed in Windows PowerShell 5.1, including transport, installer and 9 synthetic Python tests |
| Single/dual quota, missing/elapsed reset times, Beijing-time date rollover and stale/offline/login states | Passed in the hover logic suite |
| Hover delay, leave/button dismissal and animation frame changes | Passed: 272 hover/reset assertions overall, including jumping digits, recognized side poses, bounded recognition gaps and actual pointer leave |
| Rotated hit regions, scaling, negative display coordinates and placement without pet overlap | Passed with synthetic 100%/150%/200% cases; both HWNDs also passed actual passive-style and message checks |
| Real reset-time comparison and visible tip at 200% scaling | Passed: current account returned one period, and its displayed date/time matched the in-memory live response; two-period presentation was verified with synthetic data |
| Animation stability, pet clicks and dragging with the tip | Passed: stationary-pointer samples retained the visible tip across animation changes; the user retested the jumping-pet fix and confirmed stable display and normal operation |
| Hiding/showing with the tip | Immediate dismissal/reset is covered offline; not separately confirmed in the final interactive retest |
| Keyboard-focus retention during hover | Passed: foreground HWND remained unchanged when the live tip appeared; actual passive HWND styles and messages also passed. A separate typing exercise was not recorded |
| Content-fitted tip width | Passed: the fixed minimum width was removed after visual feedback; the final live tip was inspected with equal side padding and unchanged text size |
| Upgrade with preserved startup choice and duplicate launch | Passed: backed up the existing install, upgraded to 1.3.0, verified live quota/overlay, preserved enabled login startup, and confirmed duplicate launch exits without a second instance |
| GitHub Windows automated checks | The same offline suite runs on Windows for `main`; see the [workflow result for the current commit](https://github.com/357749948/codex-quota-pet/actions/workflows/ci.yml) |
| Lock/unlock, Codex restart and sleep/resume | Not yet physically verified for v1.3.0 |
| Mixed-DPI cross-monitor movement and other scaling | Not yet physically verified |
| Real Windows sign-out/sign-in | Not yet performed; shortcut registration alone is not a sign-in test |

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
3. With Null Signal visible, use `test.ps1 -DesktopProbe`. A found node confirms discovery only. Check the rendered digits and animation visually. Hover over the quota for about 300 milliseconds, compare all returned reset times, and confirm the tip stays outside the pet and inside the work area. Move away and press mouse buttons to verify dismissal; check it recovers on a new hover.
4. Drag, hide and show the pet. Check that animation remains stable while the tip is visible, clicks reach the pet, and the current application retains typing focus. Start the app again and confirm only one overlay exists.
5. Lock/unlock, restart Codex, and test sleep/resume. Observe hidden/recovered state. Test actual monitors and record their scaling, rather than inferring cross-monitor behavior from calculations.
6. Run installer tests in an isolated test location. Test startup registration separately from a real Windows sign-in. Back up an existing working installation before an approved upgrade, retain its startup choice, and restore it if the upgrade fails.

Do not attach original sprite files, templates, or private screenshots to the validation record. Report sanitized pass/fail outcomes and relevant software versions. If a check cannot be performed, keep it explicitly unverified.

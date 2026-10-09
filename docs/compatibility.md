# Compatibility and validation

## Supported target

- Windows x64 with .NET Framework 4.8 and an interactive unlocked desktop.
- Codex desktop's **Null Signal** pet with the specific sprite fingerprint accepted by the generator.
- An independently available native Codex CLI signed in with a ChatGPT account.
- Chinese tray/UI text and Beijing-time reset display.

The initial compatibility baseline is Codex desktop package **26.928.4866.0**. The supported sprite's SHA-256 is `a816f7488c187ffe8b7f5d58319deb6cfa591f98c219ef06cbaf10d1f9f330db`. Template preparation checks the sprite fingerprint, not just the application version or pet name. A later package containing an identical supported asset may work; a changed asset fails closed until the generator and recognition behavior have been reviewed.

Only one unambiguous visible pet is supported. Extreme perspective/glitch frames intentionally restore the original image. Other pets, macOS/Linux, ARM64, and headless/service sessions are outside the initial target.

## v1.4.0 reset-credit details

Version 1.4.0 adds available reset-credit counts and individual expiration times below the quota reset times. It reads `rateLimitResetCredits.availableCount` and `credits` from the existing local `account/rateLimits/read` response, including credit details in the normal 60-second query. Older CLI versions may omit this optional field; the tip then shows “暂不可用” while quota remains usable. The program has no credit-redemption action. This source update does not create a new tag or GitHub Release; v1.2.0 remains the published archive.

| Check | Status |
| --- | --- |
| Local app-server response shape | Verified with Codex CLI `0.162.0-alpha.2`: the existing read returned an available count and credit details; raw responses, identifiers and account-specific values are not published |
| Offline parsing, expiry formatting, partial details and freshness | Passed: C# service tests, 304 hover assertions, mock protocol recovery/legacy responses, 3-scale WPF layout, 80-row oversized-content rejection, isolated installer tests and 9 synthetic Python tests |
| Periodic reads under quota events and one-time expiration refresh | Passed: quota events preserve credit timestamps and full-read deadlines; expiration bypasses retry backoff once, without decrementing the count or looping on unchanged expired replies. A live credit update interval of 61.29 seconds was observed, including request time |
| Actual count/expiration comparison and complete tip at 200% scaling | Passed on 2026-10-09: count and every expiration matched the live response, all text was visible, and the complete tip stayed within the work area and outside the pet |
| Hover stability, clicks, dragging, hiding/showing and keyboard focus | Passed: stationary-pointer animation samples retained the tip, passive HWND checks and foreground retention passed; the user confirmed clear, stable display and normal dragging, hiding/showing and typing |
| Backed-up upgrade, startup-choice retention and duplicate launch | Passed: verified the 1.3.1 backup, installed the tested 1.4.0 binary, observed live quota and credits, retained enabled login startup, and confirmed one running instance after duplicate launch |
| GitHub Windows automated checks | The workflow builds and runs the same offline suite for each `main` update; see the [current workflow results](https://github.com/357749948/codex-quota-pet/actions/workflows/ci.yml) |
| Lock/unlock, restart, sleep/resume, real sign-in and mixed-DPI cross-monitor movement | Not yet physically verified for v1.4.0 |

Known expiration times are sorted first, non-expiring credits last; missing or invalid times are labeled unavailable. The server's available count is authoritative even when it returns only part of the detail list. Credits become stale independently of quota events (90 seconds stale, 5 minutes offline), and old details are not displayed in those states. A known expiration triggers one new read, not a local count decrement. The expanded tip must fit entirely outside the pet and inside a monitor's work area, or it remains hidden.

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
2. Run `test.ps1 -Live` only when prepared to read the CLI's actual account. Compare percentage, quota window and reset time with the same account's Codex usage display; compare credit count and each expiration with the local read response. Do not publish account values.
3. With Null Signal visible, use `test.ps1 -DesktopProbe`. A found node confirms discovery only. Check the rendered digits and animation visually. Hover over the quota for about 300 milliseconds, compare all returned reset times and available credit details, and confirm the complete tip stays outside the pet and inside the work area. Verify that insufficient space hides the tip rather than clipping details. Move away and press mouse buttons to verify dismissal; check it recovers on a new hover.
4. Drag, hide and show the pet. Check that animation remains stable while the tip is visible, clicks reach the pet, and the current application retains typing focus. Start the app again and confirm only one overlay exists.
5. Lock/unlock, restart Codex, and test sleep/resume. Observe hidden/recovered state. Test actual monitors and record their scaling, rather than inferring cross-monitor behavior from calculations.
6. Run installer tests in an isolated test location. Test startup registration separately from a real Windows sign-in. Back up an existing working installation before an approved upgrade, retain its startup choice, and restore it if the upgrade fails.

Do not attach original sprite files, templates, or private screenshots to the validation record. Report sanitized pass/fail outcomes and relevant software versions. If a check cannot be performed, keep it explicitly unverified.

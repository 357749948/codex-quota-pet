# Data handling / 数据处理

## Account and network

The overlay starts its own `codex --no-daemon app-server --stdio` child process and uses account/rate-limit protocol calls. It uses the existing CLI login; the project does not ask for passwords or tokens, read login files directly, or modify Codex configuration. The CLI account can differ from the desktop app account.

The child process performs the authenticated service requests needed to read quota. The overlay does not operate a server or send telemetry. It only shuts down the child process it created, not other Codex sessions.

Template preparation installs pinned Python packages into a repository-local virtual environment using pip. That installation contacts the configured package index. Normal quota reads contact the services used by Codex itself.

## Screen region

Windows UI Automation locates the pet image. While the desktop is unlocked and the pet is visible, the application captures that pet bounding rectangle in memory to match its animation and screen position. This region can include background pixels within the rectangle. It is not full-desktop capture.

The capture is used locally and is not saved or uploaded. Locking the desktop stops capture and hides the overlay. Ambiguous locations or unsupported animation frames hide the overlay.

## Local files

| Location | Contents |
| --- | --- |
| `%LOCALAPPDATA%\CodexQuotaPet` | Installed application and installation ownership marker |
| `%LOCALAPPDATA%\CodexQuotaPetData` | `state.json` and an error file when necessary |
| `%LOCALAPPDATA%\CodexQuotaPetData\cache` | Locally generated recognition templates, including asset fingerprint, masks, and sampled colors |
| Repository `.venv` | Template-generation Python environment |
| Repository `dist`, `.build`, and test output directories | Local build and test artifacts |
| Current-user Startup folder | `CodexQuotaPet.lnk`, only when login startup is enabled |

`state.json` contains quota values and status, update/reset times, process and session identifiers, pet/window coordinates, and recognition status. It is not a login credential file, but these operational details can still be private. Exceptions can also contain local paths. Review and redact diagnostics before sharing; do not attach whole files by default.

The source-only release contains none of these local files. Uninstall preserves the separate `CodexQuotaPetData` directory and unknown user files, and does not alter Codex login or configuration. The retained data directory can be removed manually after the overlay exits if no longer needed. See the uninstall script for the exact installed-file allowlist; uninstall does not erase local development artifacts.

## Sharing reports

Prefer a short error message with project, Windows, Codex, and display-scaling versions. Do not upload login files, access tokens, local templates, original pet artwork, or screenshots containing private desktop content. Synthetic examples belong in public tests.

---

额度由本机 Codex CLI 子进程读取，使用 CLI 现有登录，可能与桌面账户不同。本项目没有自己的后台或遥测；不会直接读取登录文件。宠物矩形区域的图像仅在内存中用于识别，不保存或上传，锁屏时停止读取。该矩形可能包含部分桌面背景。模板和诊断保存在本机，诊断含额度、时间和窗口位置，公开前应检查脱敏。

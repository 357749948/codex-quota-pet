# Data handling / 数据处理

## Account and network

The overlay starts its own `codex --no-daemon app-server --stdio` child process and uses account/rate-limit protocol calls. It uses the existing CLI login; the project does not ask for passwords or tokens, read login files directly, or modify Codex configuration. The CLI account can differ from the desktop app account.

The child process performs the authenticated service requests needed to read quota. The overlay does not operate a server or send telemetry. It only shuts down the child process it created, not other Codex sessions.

The hover tip displays quota reset times, available reset-credit counts and individual credit expiration times from the existing in-memory snapshot. Credit details come from the same local `account/rateLimits/read` response as quota, with details requested during normal 60-second polling. There is no added HTTP client or dependency and no redemption operation. Hovering does not trigger additional account or network requests. Quota events do not renew the credit data's separate freshness timestamp; account changes and expired login clear the previous credit data.

Template preparation installs pinned Python packages into a repository-local virtual environment using pip. That installation contacts the configured package index. Normal quota reads contact the services used by Codex itself.

## Screen region

Windows UI Automation locates the pet image. While the desktop is unlocked and the pet is visible, the application captures that pet bounding rectangle in memory to match its animation and screen position. This region can include background pixels within the rectangle. It is not full-desktop capture.

The capture is used locally and is not saved or uploaded. Locking the desktop stops capture and hides the overlay. Ambiguous locations or unsupported animation frames hide the overlay.

The README's cropped example screenshots were prepared separately for documentation. They are not produced or uploaded by the application's capture loop. Their displayed quota and reset times reflect the capture-time state only.

To detect hover without intercepting the pet's mouse input, the app reads the cursor position and mouse-button state every 100 milliseconds while running. These values are used locally for hit testing and dismissal; it does not record or upload mouse-movement history. The tip hides when the desktop is locked, the pet is unavailable, or cursor reading fails.

## Local files

| Location | Contents |
| --- | --- |
| `%LOCALAPPDATA%\CodexQuotaPet` | Installed application and installation ownership marker |
| `%LOCALAPPDATA%\CodexQuotaPetData` | `state.json` and an error file when necessary |
| `%LOCALAPPDATA%\CodexQuotaPetData\cache` | Locally generated recognition templates, including asset fingerprint, masks, and sampled colors |
| Repository `.venv` | Template-generation Python environment |
| Repository `dist`, `.build`, and test output directories | Local build and test artifacts |
| Current-user Startup folder | `CodexQuotaPet.lnk`, only when login startup is enabled |

`state.json` contains quota values and status, update/reset times, sanitized reset-credit counts and expiration fields with their freshness, process and session identifiers, pet/window coordinates, recognition status, and tooltip visibility, window handle, styles and bounds. Credit identifiers, titles and raw detail objects are not retained in diagnostics. It does not contain cursor history. It is not a login credential file, but these operational details can still be private. Exceptions can also contain local paths. Review and redact diagnostics before sharing; do not attach whole files by default.

The source-only release contains none of these local files. Uninstall preserves the separate `CodexQuotaPetData` directory and unknown user files, and does not alter Codex login or configuration. The retained data directory can be removed manually after the overlay exits if no longer needed. See the uninstall script for the exact installed-file allowlist; uninstall does not erase local development artifacts.

## Sharing reports

Prefer a short error message with project, Windows, Codex, and display-scaling versions. Do not upload login files, access tokens, local templates, original pet artwork, or screenshots containing private desktop content. Synthetic examples belong in public tests.

---

额度及重置次数、到期明细由本机 Codex CLI 子进程通过同一次 `account/rateLimits/read` 读取，使用 CLI 现有登录，可能与桌面账户不同。本项目没有自己的后台或遥测，不会直接读取登录文件，不增加 HTTP 客户端或外部依赖，也不提供消耗重置次数的操作。悬停提示复用已有数据，不增加查询；每分钟完整查询包含重置明细，普通额度事件不会把旧明细标记为刚更新，账户切换或登录失效时清除旧明细。

宠物矩形区域的图像仅在内存中用于识别，不保存或上传，锁屏时停止读取。该矩形可能包含部分桌面背景。程序每 100 毫秒读取鼠标位置和按键状态用于本机判断，不记录或上传鼠标移动历史。模板和诊断保存在本机，诊断含额度、重置次数、到期时间及窗口位置，不包含重置明细的标识、标题或原始对象；公开前应检查脱敏。

README 中的裁剪效果截图为单独准备的文档素材，并非程序自动保存或上传；图中的额度和重置时间只代表拍摄时的状态。

# Codex Quota Pet

[English](README.en.md)

在 Codex 的 **Null Signal** 宠物脸部屏幕中显示剩余额度，用清晰的大字百分比覆盖原来的文字。显示层跟随宠物，鼠标可穿透，窗口按不激活、不获取键盘焦点的方式创建；难以可靠识别的动画帧会恢复原画面。实际验证范围见[兼容性记录](docs/compatibility.md)。

这是非 OpenAI 官方项目，与 OpenAI 无隶属或背书关系。**v1.2.0 仅发布源码**，需要自行构建。仓库不包含 Codex 宠物素材、识别模板或可执行文件。

![原创功能示意图；63% 为示例，并非实际账户数据](docs/overview.svg)

## 环境要求

- Windows x64、Windows PowerShell 5.1、.NET Framework 4.8，使用系统自带的 x64 Framework C# 编译器。无需 .NET SDK。
- 本机已安装 Codex 桌面应用，并显示 **Null Signal** 宠物；支持范围见[兼容性记录](docs/compatibility.md)。其他宠物暂不支持。
- 可用的原生 `codex.exe`，已使用 ChatGPT 账户登录。读取的是 **CLI 当前登录账户**，它可能与桌面应用账户不同，请自行核对。
- Python **3.13.x x64**，仅用于首次生成本机识别模板和开发测试。日常运行不需要 Python。
- 首次准备模板需要网络下载固定版本的 Python 依赖。额度查询需要 Codex 能连接服务。

## 从源码运行

克隆仓库或解压 GitHub 的源码归档，在仓库根目录打开 PowerShell。以下命令的 `ExecutionPolicy Bypass` 只作用于该次 PowerShell 进程，不修改系统策略。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\prepare-templates.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\test.ps1
Start-Process .\dist\CodexQuotaPet.exe
```

构建结果位于 `dist`。准备脚本在仓库的 `.venv` 中安装固定依赖，从本机 Codex 的 `app.asar` 只读加载素材，在当前用户本地缓存中生成模板，不修改 Codex，也不将素材原图写入仓库。

若自动发现失败，可显式选择安装位置及 Python：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\prepare-templates.ps1 -CodexPath 'C:\path\to\Codex\app.asar' -PythonPath 'C:\path\to\python.exe'
```

`-CodexPath` 接受桌面安装目录、`Codex.exe` 或 `app.asar`。`-PythonPath` 必须指向 Python 3.13 x64；默认先查找 `py -3.13`，再查找 `python`。已有版本完全匹配的虚拟环境时，可添加 `-SkipDependencyInstall` 跳过安装依赖。

程序优先读取环境变量 `CODEX_QUOTA_PET_CODEX_PATH` 指定的原生 `codex.exe`，其次检查 PATH，最后检查已知安装位置。此变量指定的是**命令行程序**，不是桌面 `Codex.exe`。可在启动前为当前终端设置：

```powershell
$env:CODEX_QUOTA_PET_CODEX_PATH = 'C:\path\to\codex.exe'
Start-Process .\dist\CodexQuotaPet.exe
```

需要登录启动也使用该路径时，将它设置为 Windows 当前用户的环境变量。

## 安装、启动与卸载

从仓库根目录运行：

```powershell
# 安装到当前用户目录并运行；新安装默认不添加登录启动
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Install.ps1

# 需要登录启动时显式开启
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Install.ps1 -AutoStart

# 卸载本程序
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Uninstall.ps1
```

安装默认读取 `dist`，可用 `-SourceDirectory` 指定其他构建目录。`-NoLaunch` 只安装不运行。升级保留已有登录启动选择；`-AutoStart` 可将其开启。使用便携版本时，先从托盘退出，再安装。重复启动不会产生多个显示层。

安装位置是 `%LOCALAPPDATA%\CodexQuotaPet`，无需管理员权限。诊断和模板独立保存在 `%LOCALAPPDATA%\CodexQuotaPetData`。卸载只处理本程序拥有的安装文件和启动项，保留本地数据目录及不认识的用户文件，不修改 Codex 登录或配置。安装检测到不属于本程序的同名目录、链接或快捷方式时会停止并提示。

## 显示与刷新规则

- 屏幕显示一个百分比，优先显示周额度；所有返回的额度窗口、状态和重置时间可在系统托盘菜单查看。中文界面，时间统一为北京时间。
- 剩余比例为 `100 - usedPercent`，限制在 0–100。额度充足时为青绿色，≤30% 为琥珀色，≤10% 为红色。
- 首次显示立即查询，之后每 30 秒查询；收到额度事件时更新。宠物隐藏时暂停周期查询，重新出现、唤醒或点击托盘“立即刷新”时再次查询。服务器统计可能有延迟。
- 缺失数据不当作零。超过 90 秒未成功更新显示 `--`，托盘提示“待更新”；达到 5 分钟提示“离线”。登录失效时提示重新登录。
- 请求 15 秒超时，失败逐步延长重试，最长间隔 5 分钟。达到重置时间会重新查询，不自行把额度改成 100%。
- 宠物不可见、屏幕锁定、动画角度不支持或识别不可靠时隐藏覆盖层。锁屏期间停止宠物区域读取。

托盘图标可能位于 Windows 的隐藏图标区域。右键可刷新、查看详情或退出。

## 模板与更新

识别模板仅在本机生成，缓存位于 `%LOCALAPPDATA%\CodexQuotaPetData\cache`。模板包含素材指纹、生成器版本和格式版本。当前只支持已验证的 Null Signal 素材指纹；**相同宠物名称并不意味着任意 Codex 版本都兼容**。

Codex 更新后若提示模板缺失或不匹配，重新执行 `prepare-templates.ps1`。显示层会自动重新读取缓存，也可以退出后重启。如果生成器提示素材未支持，需要等待兼容性更新；不要强行修改指纹或继续使用旧模板。

本项目只对自己编写的代码和文档提供 [MIT 许可](LICENSE)，不为 Codex 原素材或本机派生数据授予再分发许可。请不要上传本机模板、素材或带有原画的测试附件。详见[依赖与素材说明](docs/third-party-notices.md)。

## 测试与问题反馈

`test.ps1` 默认离线运行合成图像、模拟协议和程序逻辑测试，不读取真实账户。需显式选择的本机检查：

```powershell
# 读取 CLI 当前账户的真实额度
powershell -NoProfile -ExecutionPolicy Bypass -File .\test.ps1 -Live

# 检查当前交互桌面中的宠物节点
powershell -NoProfile -ExecutionPolicy Bypass -File .\test.ps1 -DesktopProbe
```

这些检查不能替代实际拖动、隐藏、锁屏恢复和不同缩放的交互验收。已验证项和限制见[兼容性记录](docs/compatibility.md)。

程序只在内存中读取宠物所在屏幕区域，不保存或上传截图。额度查询由本机 Codex 子进程完成，没有本项目运营的后台或遥测服务。本地诊断仍含额度、时间和窗口位置，分享前应检查脱敏。详见[数据处理说明](docs/privacy.md)。

报告问题时提供项目版本、Windows/Codex 版本、缩放比例及简短错误信息；不要提交登录文件、令牌、模板或私人桌面截图。开发与贡献见 [CONTRIBUTING](CONTRIBUTING.md)，安全问题见 [SECURITY](SECURITY.md)。

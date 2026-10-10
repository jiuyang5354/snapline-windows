# Snapline Windows 安装版

Snapline 是 Tendedero 的非官方 Windows 移植版。原项目作者为 Alejandro Buján（https://github.com/alejandrobujan/tendedero），Windows 版由 jiuyang5354 发布。使用独立名称和图标，不代表原作者背书。完整署名与许可见同目录的 NOTICE.md、LICENSE 和 UPSTREAM-LICENSE.txt。

## 安装和桌面图标

下载 `Snapline-Windows-Setup-v1.3.1.exe`，双击运行中文安装向导。“创建桌面快捷方式”默认勾选，可以取消。开始菜单入口始终创建；桌面图标使用程序自己的 Snapline 图标。

默认安装到 `%LOCALAPPDATA%\Programs\Snapline`，只安装给当前 Windows 用户，不需要管理员权限。需要 Windows 10 / 11 和 .NET Framework 4.8 或更高版本；不自动下载或修改系统运行时。

安装完成后可双击桌面图标。完成页的“运行 Snapline”默认不勾选。开机启动仍由程序托盘菜单控制，默认关闭，安装包不主动开启。

**覆盖升级：** 已通过 EXE 安装的用户直接运行新版安装包，默认沿用登记的原安装目录，包括自定义路径。开始替换前，安装器正常退出该位置运行的旧版；无法退出时提示重试或取消。随后覆盖程序文件，更新同一份应用登记，不必先卸载，也不需要重启 Windows。

原目录覆盖时，已有桌面和开始菜单快捷方式保持原样，包括手动填写的 `--data-dir` 参数；缺少快捷方式时重新创建。取消桌面选项仍可删除安装器以前创建的桌面图标。开机启动项保持原值，截图与设置不覆盖或删除。其他目录的便携副本不会被关闭或替换。

便携版没有安装登记，迁移前请先从托盘退出旧版并选择安装位置。默认图片和设置仍在 `%LOCALAPPDATA%\Snapline`。使用自定义 `--data-dir` 时，应给新建快捷方式附加相同参数；便携版启用过开机启动时，在安装版设置中重新勾选，以更新程序路径。

程序及安装包目前未作代码签名，Windows 可能提示未知发布者。程序与同版本便携 ZIP 使用相同的已验证 EXE，包含新版截图栏和统一设置窗口，仍为预发布。

## 卸载

在 Windows 设置的应用列表中卸载 Snapline，或从开始菜单选择“卸载 Snapline”。先从托盘退出运行中的程序。

卸载移除安装程序文件、开始菜单入口和安装包创建的桌面快捷方式。原有截图和设置保留；只有指向本次安装路径的 Snapline 开机启动项会被移除。

## 构建和验证

安装包使用 NSIS 3.13：从 https://nsis.sourceforge.io/Download 下载官方 ZIP，解压到 `.tools/`，或向脚本传入 `-NsisPath`。NSIS 编译器不随安装包或源码包分发。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\windows\package-installer.ps1 -Test
```

先将当前版本便携 ZIP 放在 `windows/`，其 SHA-256 由根目录 `SHA256SUMS.txt` 校验。使用 `-Test` 还需将公开的 `Snapline-Windows-v1.3.0.zip` 放在同一目录，作为真实旧版升级样本，测试会校验其固定 SHA-256。脚本直接封装当前程序，输出 EXE、安装器源码 ZIP 与 `SHA256SUMS-Setup.txt` 到 `windows/dist/installer/`。便携 ZIP、其摘要和现有更新文件保持原样。安装器源码 ZIP 包含安装脚本与测试，不包含程序二进制或 NSIS 编译器；程序源码另见 Windows 源码包。

验证使用同一脚本编译的 QA 安装器，将安装位置、桌面和开始菜单重定向到 `qa/output/installer/`，登记使用私有的 `HKCU\Software\Snapline-InstallerTests` 测试项，验证后清理。除安装与卸载外，还启动真实 v1.3.0 的两个独立数据目录实例，验证自动正常退出、原路径替换、快捷方式参数与启动项保留；另一目录的运行副本应保持不变。不修改真实桌面、应用列表或启动项。真实 Windows 应用列表显示、桌面重定向和点击启动体验仍需用户安装验收。

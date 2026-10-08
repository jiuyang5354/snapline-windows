# Snapline · 截图晾衣绳

Tendedero 的非官方 Windows 移植版。保留屏幕顶端晾衣绳、金属夹子、半透明图片边框与轻微摆动的交互，用 C# / WPF 和 Win32 原生接口重新实现。

## 运行

支持 Windows 10 / 11，需 .NET Framework 4.8 或更高版本（多数系统已经自带）。解压整个便携包，双击 `Snapline.exe`。无须安装 Python、Node.js 或 Swift，也不需要管理员权限。

当前 v1.0.0 为预发布，程序未作代码签名，Windows 可能提示未知发布者。源码和编译脚本已公开；实际验证范围见随包 `QA.md`。

程序驻留系统托盘；图标可能位于任务栏右侧的“隐藏图标”菜单。点击托盘图标或按 `Ctrl + Alt + T` 展开，再次点击或按快捷键收起。重复启动同一程序会唤起现有实例。

如果 `Ctrl + Alt + T` 已被其他软件占用，自动改用 `Ctrl + Alt + Shift + T`，并在托盘菜单和启动提示中显示。两个组合键都被占用时，仍可用托盘图标与顶部悬停操作。

## 使用

| 操作 | 结果 |
| --- | --- |
| `Win + Shift + S` / `PrintScreen` 截图 | 剪贴板中的新图片自动挂入 |
| 鼠标在屏幕顶端停留约 0.28 秒 | 在该屏幕展开 |
| 鼠标离开顶部区域 | 约 0.42 秒后收起；新截图短暂展示 |
| `Ctrl + Alt + T` | 显示 / 隐藏，主动打开后等待鼠标访问 |
| 单击截图 | 复制图片，并提供 PNG、位图和文件格式；等待系统双击判定结束 |
| 双击截图 | 使用 Windows 默认图片应用预览 |
| 按住截图 0.45 秒 | 用 Windows 画图编辑，保存后刷新缩略图 |
| 拖入聊天、编辑器等应用 | 目标应用接收图片或文件副本，截图留在绳上 |
| 拖入资源管理器文件夹 | 目标决定复制或移动；可按住 Shift 请求移动，原文件被移走后会从绳上取下 |
| 右键截图 | 复制、预览、画图编辑、另存为、在文件夹中显示、取下 |
| 点击截图叉号 | 程序收件夹内的图片进入回收站；外部图片只取下，保留原文件 |
| 托盘“全部取下” | 清空展示列表，保留文件 |

也可通过托盘“挂入图片…”加入已有图片，或“选择截图文件夹…”监听其他截图软件的新文件。默认监听 Windows 已登记的 Screenshots 文件夹；文件夹尚未创建时，程序等待它出现。支持 PNG、JPEG、BMP、GIF、TIFF；GIF 展示静态缩略图。

“收集剪贴板图片”会收集截图以及从其他应用复制的图片，无法保证只识别截图。可在托盘菜单关闭此项，只监听文件夹。文字剪贴板不会保存。

## 文件与行为

- 默认将收集的剪贴板图片保存至 `%LOCALAPPDATA%\Snapline\Inbox`。状态保存在同目录的 `settings.json`。
- 程序不发送网络请求，不提供账户或遥测。图片仍可能通过你使用的聊天软件、Windows 剪贴板同步等功能离开本机，这些由对应程序管理。
- 最多恢复最近 12 张；屏幕较窄时展示其中最近几张。超出数量的旧图片保留在收件夹，可通过托盘打开并按需清理。
- 同一图片同时从剪贴板和文件夹出现时去重。程序自己复制图片不会反复新增。
- 不接管 Windows 截图快捷键，不修改截图工具的自动保存设置。截图工具仍可能保存自己的副本；可在截图工具设置里自行关闭自动保存。
- 检测到前台窗口的客户区覆盖整个显示器时收起，退出全屏后仍可从顶部或快捷键唤起。普通最大化窗口不会被当作全屏。
- 遵循 Windows 的界面动画设置。透明外框是 WPF 半透明材质，没有使用 macOS 的原生模糊材质。
- 无开机启动注册项。要退出程序，在托盘右键选择“退出”。

## 编译与验证

源码位于 `src/`，可用系统 .NET Framework 的 C# 编译器直接编译。使用 Windows PowerShell 或 PowerShell 7：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test
```

如果系统没有 `Microsoft.NET\Framework\v4.0.30319\csc.exe`，安装 .NET Framework 4.8 Developer Pack 后再编译。测试使用 `qa/output/` 内的独立收件夹，测试过程临时使用系统剪贴板并恢复可读取的原格式。不要在同时进行复制操作时运行测试。

也可指定独立数据目录用于便携运行或检查：

```powershell
.\bin\Snapline.exe --data-dir D:\SnaplineData
```

## 来源与许可

原项目：[alejandrobujan/tendedero](https://github.com/alejandrobujan/tendedero)，作者 Alejandro Buján。适配基于上游提交 `3c866d94d25ee9d1e008aecd9a78a4e9b2bf06a4`。

Windows 版发布账号：[jiuyang5354](https://github.com/jiuyang5354)。源码仓库：[jiuyang5354/snapline-windows](https://github.com/jiuyang5354/snapline-windows)。

原项目代码为 MIT 许可，原名称、图标及 `docs/` 图片不在授权范围内。本版本使用新名称 Snapline 和新绘制图标；Windows 便携包不包含原项目的图标或宣传图片，不代表原作者背书。上游许可完整保留在 `UPSTREAM-LICENSE.txt`，来源说明见 `NOTICE.md`。Windows 实现同样按 MIT 许可提供，便携包和源码包均包含 `LICENSE`。

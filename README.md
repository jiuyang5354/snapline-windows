# Snapline · 截图晾衣绳 Windows 版

Snapline 是 [Tendedero](https://github.com/alejandrobujan/tendedero) 的**非官方 Windows 移植版**。原项目作者为 **[Alejandro Buján](https://github.com/alejandrobujan)**；本仓库由 [jiuyang5354](https://github.com/jiuyang5354) 发布 Windows 实现。它把截图挂在屏幕顶部，方便复制、预览、编辑和拖入其他应用。

An unofficial Windows implementation of Tendedero by Alejandro Buján, using C# / WPF and Win32. This project uses its own name and icon and is not endorsed by the upstream author.

**当前版本为 v1.0.0 预发布。** 发布前自动检查 40 / 40 通过；不同物理显示器的缩放、真实鼠标手势、具体软件的拖放接收和全屏应用体验仍需人工检查。请阅读 [验证记录](windows/QA.md)。

![Snapline Windows 窗口预览](docs/preview.png)

上图是实际 WPF 窗口内容放在展示背景上的预览；卡片使用程序生成的测试图片。

## 下载和运行

- [下载 Windows 便携包](https://github.com/jiuyang5354/snapline-windows/releases/download/v1.0.0/Snapline-Windows-v1.0.0.zip)
- [下载可编译源码包](https://github.com/jiuyang5354/snapline-windows/releases/download/v1.0.0/Snapline-Windows-Source-v1.0.0.zip)
- [发布说明与 SHA-256 校验文件](https://github.com/jiuyang5354/snapline-windows/releases/tag/v1.0.0)

目标系统为 **Windows 10 / 11，需 .NET Framework 4.8 或更高版本**。解压整个便携包，双击其中的 `Snapline.exe`。程序不需要管理员权限；运行后驻留系统托盘，图标也可能在“隐藏图标”菜单中。

当前程序未作代码签名，Windows 可能提示未知发布者。源码和编译脚本已公开，可以自行编译。

## 使用方法

1. 使用 `Win + Shift + S` 截图，或复制一张图片。
2. 在屏幕顶部悬停，点击托盘图标，或按 `Ctrl + Alt + T` 展开。
3. 单击图片复制，双击用默认图片应用预览，按住约 0.45 秒用画图编辑，也可以拖入其他应用。
4. 从托盘右键菜单选择“退出”关闭程序。

如果 `Ctrl + Alt + T` 已被占用，程序会尝试 `Ctrl + Alt + Shift + T`；两个组合键都被占用时，仍可通过托盘和顶部悬停操作。完整操作说明见 [中文使用指南](windows/README.zh-CN.md)。

## 使用注意事项

- **默认收集剪贴板中的图片，不仅是截图。** 从聊天、浏览器或其他应用复制的图片也会被保存。可在托盘关闭“收集剪贴板图片”，改为只监听指定文件夹；文字剪贴板不会保存。
- 收集的图片默认保存在 `%LOCALAPPDATA%\Snapline\Inbox`，设置在 `%LOCALAPPDATA%\Snapline\settings.json`。最多恢复最近 12 张，超出展示数量的旧文件仍会保留，需要自行按需清理。
- 点击图片叉号会把程序收件夹内的图片送入回收站；外部图片只从展示列表取下，保留原文件。“全部取下”保留文件。
- 拖放时由目标应用决定接收图片或文件，以及复制或移动。使用移动操作后，原文件可能离开原文件夹。
- 程序本身不发送网络请求，也没有账号或遥测。Windows 剪贴板同步及接收图片的其他应用仍按各自设置处理数据。
- 程序不接管系统截图快捷键、不修改截图工具的自动保存设置，也不自动设置开机启动。
- 透明外框使用 WPF 半透明效果，与 macOS 原生模糊材质有差异。预发布验证范围以 [QA.md](windows/QA.md) 为准。

## 编译

使用 Windows PowerShell 或 PowerShell 7，在仓库根目录运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\windows\build.ps1
```

生成的程序位于 `windows/bin/Snapline.exe`。编译使用系统 .NET Framework C# 编译器，无第三方运行时依赖。如果系统缺少编译器，安装 .NET Framework 4.8 Developer Pack。

运行现有验证和打包脚本：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\windows\build.ps1 -Test
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\windows\package.ps1
```

测试会临时使用系统剪贴板，结束时恢复能读取的原格式；测试期间请暂停复制操作。测试图片与状态使用 `windows/qa/output/` 中的独立目录。

## 原作者与许可

- 原项目：[alejandrobujan/tendedero](https://github.com/alejandrobujan/tendedero)。
- 原作者：**Alejandro Buján**。
- 参考的上游提交：[`3c866d94d25ee9d1e008aecd9a78a4e9b2bf06a4`](https://github.com/alejandrobujan/tendedero/commit/3c866d94d25ee9d1e008aecd9a78a4e9b2bf06a4)。
- Windows 实现：C# 5、WPF、.NET Framework 4.8 与 Win32；本仓库只发布 Windows 实现及其文档。

上游代码采用 MIT 许可，但原项目名称、原图标及 `docs/` 图片不在其授权范围内。按上游要求，本版本使用 **Snapline** 名称和新绘制的图标，发布内容不包含原图标或宣传图片，也不代表原作者背书。

原作者版权与完整上游许可保留在 [UPSTREAM-LICENSE.txt](UPSTREAM-LICENSE.txt)。Windows 实现按 [MIT 许可](LICENSE) 提供，详细来源说明见 [NOTICE.md](NOTICE.md)。

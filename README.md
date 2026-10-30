<p align="center">
  <img src="docs/snapline-icon.png" width="96" height="96" alt="Snapline 图标">
</p>

<h1 align="center">Snapline</h1>

<p align="center"><strong>把截图挂到屏幕顶部，把临时文件放到手边。</strong></p>
<p align="center">截图晾衣绳 · 文件暂存架 · 本地图片与 PDF 工具</p>
<p align="center">A Windows desktop utility for screenshots, temporary files and local image / PDF processing.</p>

<p align="center">
  <a href="https://github.com/jiuyang5354/snapline-windows/releases/tag/v1.5.1"><img src="https://img.shields.io/badge/version-v1.5.1%20Beta%202-e8a44d" alt="v1.5.1 Beta 2"></a>
  <a href="#下载"><img src="https://img.shields.io/badge/Windows-10%20%2F%2011-4793d7" alt="Windows 10 / 11"></a>
  <a href="#下载"><img src="https://img.shields.io/badge/.NET%20Framework-4.8%2B-846cc4" alt=".NET Framework 4.8+"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-63ad93" alt="MIT license"></a>
</p>

<p align="center"><a href="https://github.com/jiuyang5354/snapline-windows/releases/download/v1.5.1/Snapline-Windows-Setup-v1.5.1.exe">下载 EXE</a> · <a href="#快速开始">快速开始</a> · <a href="#能做什么">能做什么</a> · <a href="#常见问题">常见问题</a> · <a href="docs/VERSIONS.md">历史版本</a> · <a href="https://github.com/jiuyang5354/snapline-windows/issues">反馈建议</a></p>
<p align="center"><strong>简体中文</strong> | <a href="README.en.md">English</a></p>

> **English summary**
>
> Snapline is a Windows desktop utility that keeps screenshots on a clothesline at the top of the screen and temporary files on a floating shelf. It also offers local image conversion, PDF tools, batch renaming and ZIP packaging. Files are processed on your computer without uploading their contents.
>
> **Status: v1.5.1 Beta 2, a public prerelease.** Requires Windows 10/11 and .NET Framework 4.8 or later; WebP tools require 64-bit Windows. See the [full English README](README.en.md) for setup, usage and limitations.

## Beta 说明

- 当前为预发布测试版，仍在收集不同电脑与软件环境的兼容性反馈。已知限制与验证范围见下文。
- 更新前建议备份截图、设置与暂存记录；[历史版本下载](docs/VERSIONS.md)继续保留，方便回退。
- 本项目是 **[Alejandro Buján](https://github.com/alejandrobujan) 的 [Tendedero](https://github.com/alejandrobujan/tendedero) 的非官方 Windows 实现**，由 [jiuyang5354](https://github.com/jiuyang5354) 发布与维护。使用独立名称和图标，不代表原作者官方版本或背书。

## 下载

当前版本：**v1.5.1 Beta 2 · 预发布测试版**。适用于 **Windows 10 / 11，需要 .NET Framework 4.8 或更高版本**；WebP 工具需要 64 位 Windows。

| 下载内容 | 适合谁 |
| --- | --- |
| **[EXE 安装包](https://github.com/jiuyang5354/snapline-windows/releases/download/v1.5.1/Snapline-Windows-Setup-v1.5.1.exe)** | 普通用户。默认创建桌面快捷方式，支持覆盖旧版安装。 |
| **[便携版 ZIP](https://github.com/jiuyang5354/snapline-windows/releases/download/v1.5.1/Snapline-Windows-v1.5.1.zip)** | 希望自行选择程序目录的用户。退出旧版后完整解压，再运行 `Snapline.exe`。 |
| [程序源码](https://github.com/jiuyang5354/snapline-windows/releases/download/v1.5.1/Snapline-Windows-Source-v1.5.1.zip) · [安装器源码](https://github.com/jiuyang5354/snapline-windows/releases/download/v1.5.1/Snapline-Windows-Installer-Source-v1.5.1.zip) | 开发、审阅或自行构建。 |
| [安装包校验文件](https://github.com/jiuyang5354/snapline-windows/releases/download/v1.5.1/SHA256SUMS-Setup.txt) · [便携包与源码校验文件](https://github.com/jiuyang5354/snapline-windows/releases/download/v1.5.1/SHA256SUMS.txt) | 核对 SHA-256。 |

[本版更新公告](https://github.com/jiuyang5354/snapline-windows/releases/tag/v1.5.1) · **[历史版本下载](docs/VERSIONS.md)** · [问题与建议](https://github.com/jiuyang5354/snapline-windows/issues)

安装器默认安装到 `%LOCALAPPDATA%\Programs\Snapline`，也可以选择其他目录。覆盖升级保留截图、设置、暂存记录和已有快捷方式；开机启动默认关闭。程序未作代码签名，Windows 可能提示未知发布者。完整安装说明见 [安装指南](windows/INSTALLER.zh-CN.md)。

## 能做什么

### 截图晾衣绳

截图和复制的图片挂在屏幕顶部，沿用透明空白、玻璃容器、金属夹子与轻微物理反馈。单击复制、双击预览、长按约 0.45 秒用画图编辑，也可以拖入支持图片或文件的其他应用。

- **每 9 张一组**：新截图进入最新组，旧组保留，可用滚轮和箭头翻看，重启后恢复。
- **静默收集**：设置 → 常规 → 关闭“收集新截图时自动展开”。继续保存截图，只在需要时主动展开。
- **自定义快捷键**：分别设置显示 / 隐藏、区域截图和清空截图栏的按键。
- **悬停操作**：鼠标指向截图时，按 `C` 复制、`A` 编辑、`D / Delete` 取下。
- **区域截图**：框选区域保存，也可选择包含鼠标指针；`Esc` 取消。

![截图晾衣绳示意](docs/preview.png)

截图栏在生成的展示背景上呈现；图片为测试样例。容器之间保持透明，截图内容不经过玻璃滤镜。

### 文件暂存架

主屏右侧的浮动卡叠用于暂存文件和文件夹，支持多文件叠、展开网格、拖入拖出，以及文字、链接和图片剪藏。

- 空架默认藏到屏幕外，鼠标在右侧边缘停留后出现；有文件时显示卡叠。
- 悬停或点击展开，可随时中断展开、收起的过程。
- 文件与文件夹只记录原路径；移出暂存架或清空列表保留原件。
- 在展开面板中选择文件，进入重命名、转格式、压缩、ZIP 或 PDF 工具。

### 离线文件工具

五种工具共用原生工作台，支持浅色 / 深色，并携带当前文件列表切换。转换结果可加入暂存架，重命名后同步截图历史与暂存路径。

| 工具 | 当前功能 |
| --- | --- |
| 批量重命名 | 原名称与连续编号模板、执行前预览、冲突检查；确认后修改原文件名。 |
| 图片转格式 | PNG、JPEG、BMP、TIFF、静态 WebP；WebP 支持有损 / 无损输出。 |
| 图片压缩 | JPEG 质量与图片长边限制，PNG 可保留透明；处理结果另存。 |
| ZIP 打包 | 选择文件或文件夹，打成 ZIP，结果另存。 |
| 图片转 PDF | 单图转 PDF、多图合并 PDF。 |
| PDF 页面工具 | 合并、逐页拆分、提取 / 排序、删除、旋转、页码、文字水印、设置打开密码、填写已知密码后移除保护。 |

处理在本地进行，不上传文档。转换与打包另存结果、区分同名文件，后台处理可取消。压缩结果不保证比原文件小；动画和多帧图片暂不处理。

**范围说明：** 图片 PDF 不包含 OCR；Office 文档转换、PDF 压缩、OCR、数字签名尚未提供，交互表单与带数字签名的 PDF 暂不处理。常用 PDF 任务最多 512 页，合并最多 200 个文件、输入合计 128 MB。后续范围见 [PDF 工具计划](windows/PDF-TOOLS-PLAN.zh-CN.md)。

### 设置与日常操作

提供浅色、深色和跟随系统；可暂停自动收集、复制最近一张、修改截图收件夹、设置开机启动、检查更新，以及重看更新公告。

![v1.5.1 常规设置界面](docs/screenshots/settings-v1.5.1-light.png)

上图来自 v1.5.1 的隔离测试实例。设置使用实色背景，截图容器保留原有玻璃效果；开关状态仅为测试示例。

## 快速开始

1. 安装或完整解压后运行 `Snapline.exe`，程序驻留系统托盘。
2. 使用 `Win + Shift + S` 截图、复制一张图片，或使用 Snapline 的区域截图。
3. 鼠标停在屏幕顶部，点击托盘图标，或按 `Ctrl + Alt + T` 展开晾衣绳。
4. 把临时文件拖到右侧暂存架；在展开面板里选择文件并打开处理工具。
5. 在设置里调整快捷键、外观、收集方式和保存位置，点击“保存更改”。

| 默认操作 | 快捷键 |
| --- | --- |
| 显示 / 隐藏截图栏 | `Ctrl + Alt + T` |
| 区域截图 | `Ctrl + Alt + S` |
| 清空所有截图分组，保留原文件 | `Ctrl + Alt + Backspace` |

快捷键可在“设置 → 快捷键”修改。若默认显示键被占用，程序会尝试备用组合，并在托盘中显示实际使用的按键。托盘图标也可能位于任务栏的“隐藏图标”菜单中。

## 文件保存、拖放与隐私

- 截图默认保存在 `%LOCALAPPDATA%\Snapline\Inbox`，设置保存在 `%LOCALAPPDATA%\Snapline\settings.json`。可在“设置 → 存储”更改收件夹，旧图片不会自动搬迁。
- **取下一张与清空全部不同**：叉号或 `D / Delete` 会将当前 / 默认收件夹内的图片送入回收站；外部图片和旧自定义收件夹中的图片只移除卡片。清空全部截图分组保留原文件。
- 截图成功拖入接收应用后仍留在绳上；拖到无人接收区域松开时，默认只取下卡片并保留原文件，`Esc` 取消保留。此行为可在设置中关闭。
- 目标应用需要支持接收图片 / 文件。Snapline 与目标应用的权限级别应一致；管理员权限运行的聊天软件可能无法接收普通权限程序的拖放。
- 收集并不限于系统截图，也包括符合格式规则的剪贴板图片。可关闭“收集剪贴板图片”，或暂停所有自动收集。
- 已知文字、表格、矢量对象及 Office / WPS 对象预览会跳过。v1.5.1 额外检查 WPS 剪贴板拥有者，因此 **从 WPS 复制的普通图片也会跳过**，需要时可使用区域截图。反馈者的 WPS 现场仍需复测，见 [Issue #2](https://github.com/jiuyang5354/snapline-windows/issues/2)。
- 截图、暂存和文件处理不上传内容。更新检查访问 GitHub 的公开更新文件，下载访问 GitHub 及其资产 CDN；不发送截图、剪贴板内容或本地路径。

## 更新与旧版本

自动检查更新默认开启，**包含预发布版本**：启动约 15 秒后尝试检查，之后每 24 小时最多检查一次；也可从托盘手动检查。下载会核对大小与 SHA-256，安装版下载 EXE 后由你确认覆盖安装，便携版下载 ZIP 后需先退出再完整解压。

**旧版本的 Release、安装包、便携包、源码、校验文件及标签继续保留。** [历史版本索引](docs/VERSIONS.md) 列出各版可用下载；[更新记录](CHANGELOG.md) 保留功能变化。回退前请先备份截图、设置和暂存记录。

改版前的仓库介绍也保存在 Git 历史中：[查看旧版 README](https://github.com/jiuyang5354/snapline-windows/blob/d39864addb2f2d050e441b73e36cadace6b3a8ad/README.md)。

## 验证与反馈

v1.5.1 已完成静默收集、剪贴板格式与来源、受限内存、设置与动效、工具集成，以及安装 / 覆盖升级 / 卸载检查。自动检查不代表所有用户环境均已验证；WPS 反馈现场、其他设备、多屏和不同缩放仍需实际反馈。记录见 [QA.md](windows/QA.md)。

遇到问题请在 [Issues](https://github.com/jiuyang5354/snapline-windows/issues) 提供软件版本、Windows 版本、触发步骤和错误原文。有启动问题时可附默认数据目录里的 `diagnostics.log`；剪贴板问题可附 `clipboard-error.log`。无需上传私人文档、设计源文件或完整设置文件。

## 常见问题

### 应该下载 EXE 还是 ZIP？

普通用户建议下载上方的 **EXE 安装包**，按提示安装即可，默认创建桌面快捷方式。ZIP 是便携版，需要完整解压后运行 `Snapline.exe`。两种方式的具体区别见[下载](#下载)。

### 安装后怎么没有看到窗口？

Snapline 驻留系统托盘。把鼠标停在屏幕顶部、点击托盘图标，或按默认快捷键 `Ctrl + Alt + T` 展开截图栏。托盘图标可能位于“隐藏图标”菜单中。

### 为什么拖不进聊天软件？

目标应用需要支持图片或文件拖放，两个程序的权限级别也应一致。可先从资源管理器拖入同一输入区做对照；若同样不能接收，先检查目标软件与权限设置。

### 文件会上传吗？取下卡片会删除文件吗？

截图、暂存和文件处理在本地进行；更新检查与下载会连接 GitHub。暂存架移除条目保留原文件；截图的“取下一张”和“清空全部”处理不同，操作前请查看[文件保存、拖放与隐私](#文件保存拖放与隐私)。

## 自行构建

程序使用 **C# / WPF / Win32 与 .NET Framework 4.8**。在仓库根目录运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\windows\build.ps1
```

生成文件位于 `windows/bin/`，运行时须保留配置与整个 `Converters` 目录。构建脚本按锁定版本和 SHA-256 获取转换组件；发行包已附带组件，运行工具时不另行下载。需要相应的 .NET Framework 开发组件，制作安装包另需 NSIS 3.13。详见 [构建与安装说明](windows/INSTALLER.zh-CN.md)。

另提供 [GitHub Packages / NuGet 包](https://github.com/users/jiuyang5354/packages/nuget/package/jiuyang5354.Snapline.Windows)，当前为 `1.5.1-preview`；下载需要 GitHub 软件包认证。普通用户建议使用上方 EXE 或 ZIP，开发者可查看 [软件包说明](windows/PACKAGE-README.md)。

## 原作者与许可

- **Tendedero 原作者：** [Alejandro Buján](https://github.com/alejandrobujan)，[原项目](https://github.com/alejandrobujan/tendedero)，参考提交 `3c866d94d25ee9d1e008aecd9a78a4e9b2bf06a4`。
- **Windows 版发布与维护：** [jiuyang5354](https://github.com/jiuyang5354)。本仓库只发布 Windows 实现，不是原作者官方 Windows 版本。
- Windows 实现采用 [MIT 许可](LICENSE)，保留 [上游许可](UPSTREAM-LICENSE.txt)。上游名称、图标与其 `docs/` 图片不在该 MIT 授权范围内，本项目使用独立名称、图标和生成的演示素材。
- 文件暂存架来源、PDFsharp、WebP、QingIcon 以及设计参考说明见 [NOTICE.md](NOTICE.md)；随包保留各组件自己的许可和声明。

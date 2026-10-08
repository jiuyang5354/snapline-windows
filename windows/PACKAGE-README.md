# Snapline Windows 软件包

Snapline 是 [Tendedero](https://github.com/alejandrobujan/tendedero) 的非官方 Windows 实现。原项目作者为 **[Alejandro Buján](https://github.com/alejandrobujan)**，Windows 版由 [jiuyang5354](https://github.com/jiuyang5354) 发布。本版本使用独立名称和图标，不代表原作者背书。

本 NuGet 包包含已发布的 Windows 便携程序，文件位于 `tools/Snapline/`。需要 **Windows 10 / 11 和 .NET Framework 4.8 或更高版本**。

## 下载和运行

普通用户可直接从 [Releases](https://github.com/jiuyang5354/snapline-windows/releases) 下载 `Snapline-Windows-v版本号.zip`，解压整个文件夹并运行 `Snapline.exe`。

使用 GitHub Packages 时，需要先按 [GitHub 官方 NuGet 认证说明](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry) 配置账号及具有 `read:packages` 权限的令牌；公开的 NuGet 包也需要认证。不要把令牌提交到仓库或发给他人。

```powershell
nuget install jiuyang5354.Snapline.Windows -Prerelease -OutputDirectory .\packages -Source https://nuget.pkg.github.com/jiuyang5354/index.json
```

安装后，把 `packages/jiuyang5354.Snapline.Windows.版本号/tools/Snapline/` **整个文件夹**复制到自己的程序目录，再双击 `Snapline.exe`。也可把已下载的 `.nupkg` 当作 ZIP 解压，保留 `tools/Snapline/` 内全部文件。程序运行后驻留系统托盘。

NuGet 负责分发文件；升级已运行的程序时，仍需从托盘退出旧版、换用新版完整文件夹。预发布 Release 对应的 NuGet 版本带 `-preview` 后缀。

## 使用与注意事项

- 默认快捷键为 `Ctrl + Alt + T`；托盘 →“设置快捷键…”可自定义显示 / 隐藏快捷键。
- 默认收集剪贴板图片。托盘可关闭收集、暂停全部自动收集，或快速复制最近一张；开机启动默认关闭。
- 图片和设置保存在 `%LOCALAPPDATA%\Snapline`。不上传图片、剪贴板内容或本地路径。
- 自动更新提供检查、提醒及校验下载；安装更新需手动退出旧版并解压运行。更新检查和下载访问本仓库的 GitHub 文件。
- 程序尚未代码签名，Windows 可能提示未知发布者。预发布的验证范围及人工检查项见包内 `tools/Snapline/QA.md`，完整操作见 `README.zh-CN.md`。

## 许可与来源

Windows 实现按 MIT 许可发布；包内保留 `LICENSE`、`UPSTREAM-LICENSE.txt` 和 `NOTICE.md`。上游名称、图标及上游 `docs/` 图片不在其 MIT 授权范围内，本软件包不包含这些素材。

软件包的仓库元数据记录对应 Release 的源码提交。包内程序取自该 Release 的便携 ZIP，封装前核对 SHA-256；发布流程下载软件包后再次核对全部便携文件。

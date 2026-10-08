# Snapline v1.1.0 · 自定义快捷键 Windows 预发布

Tendedero 的非官方 Windows 移植版。原项目作者为 **[Alejandro Buján](https://github.com/alejandrobujan)**，原仓库为 [alejandrobujan/tendedero](https://github.com/alejandrobujan/tendedero)，参考提交为 `3c866d94d25ee9d1e008aecd9a78a4e9b2bf06a4`。

## 下载

- `Snapline-Windows-v1.1.0.zip`：便携程序、配置、中文指南、验证记录和许可说明。解压整个文件夹后运行 `Snapline.exe`。
- `Snapline-Windows-Source-v1.1.0.zip`：Windows 源码、编译/打包脚本、测试源码、文档和许可说明。
- `SHA256SUMS.txt`：两个下载包的 SHA-256 校验值。

目标系统：Windows 10 / 11 + .NET Framework 4.8 或更高版本。无须安装 Python、Node.js 或 Swift；无需管理员权限。当前二进制未作代码签名。

## 本次新增

右键托盘 → **设置快捷键…** → 按下单键或组合键 → **保存**。可自由选择显示 / 隐藏晾衣绳的快捷键，保存后立即生效，重启后继续使用。支持普通单键、功能键及 Ctrl / Alt / Shift / Win 修饰组合，并提供恢复默认与取消。

新组合注册成功后才释放旧绑定。按键冲突、系统保留键或设置无法保存时会提示；旧版本图片列表与收集偏好可继续使用。

## 原有功能

屏幕顶端悬停展开、托盘和快捷键操作；收集剪贴板图片和监听截图文件夹；复制、预览、画图编辑、拖放、右键另存为；最近图片与设置恢复；重复启动唤起现有实例。

默认快捷键为 `Ctrl + Alt + T`；被占用时尝试 `Ctrl + Alt + Shift + T`。具体操作见随包中文指南或 [仓库使用说明](https://github.com/jiuyang5354/snapline-windows#使用方法)。

## 注意事项与验证范围

- 默认会保存所有复制到剪贴板的图片，无法保证只识别截图；可从托盘关闭收集。保存位置为 `%LOCALAPPDATA%\Snapline\Inbox`。
- 展示最多恢复最近 12 张，超出展示数量的旧文件仍保留。点击叉号会把收件夹图片送入回收站；外部原文件只取下。“全部取下”保留文件。
- 拖放接收格式及复制/移动行为由目标应用决定。程序自身不发送网络请求；系统剪贴板同步和其他应用的数据处理由各自设置控制。
- 单键会影响其他应用中的同名按键；字母或数字建议使用组合键。修饰键须搭配其他键；F12 为 Windows 保留键，其他已占用或无法注册的组合会在设置窗口提示。
- 本机自动检查 **58 / 58 通过**，新增检查覆盖实时生效、设置保存、冲突保护、录键窗口、取消、恢复默认与重启。多显示器不同缩放、真实鼠标完整手势、具体软件拖放、实际全屏应用及实体键盘完整组合仍需人工验证，因此此次标记为预发布。完整证据见 [QA.md](https://github.com/jiuyang5354/snapline-windows/blob/main/windows/QA.md)。

## 署名与许可

本版本使用 Snapline 名称和新绘制图标，不包含原项目的图标或宣传图片，不代表原作者背书。上游代码为 MIT 许可，原名称、原图标和上游 docs 图片不在该授权范围内。仓库及两个下载包均保留原作者版权、完整上游许可和 Windows MIT 许可；详见 [NOTICE.md](https://github.com/jiuyang5354/snapline-windows/blob/main/NOTICE.md)。

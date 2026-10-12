# 来源与版权说明

Snapline 是 Tendedero 的非官方 Windows 移植版。

- 原项目作者：Alejandro Buján（https://github.com/alejandrobujan）。
- 原项目：https://github.com/alejandrobujan/tendedero。
- 参考提交：`3c866d94d25ee9d1e008aecd9a78a4e9b2bf06a4`。
- 原版权声明：Copyright (c) 2026 Alejandro Buján。
- Windows 版发布账号：https://github.com/jiuyang5354。
- Windows 版源码仓库：https://github.com/jiuyang5354/snapline-windows。

Windows 实现使用 C# / WPF 和公开 Win32 接口重新实现截图晾衣绳交互。原项目代码采用 MIT 许可；原项目名称、原图标（包括其生成脚本所绘制的图标）及上游 docs 目录图片不在该授权范围内。完整原文随仓库、源码包与便携包保留于 UPSTREAM-LICENSE.txt。

本版本使用 Snapline 名称和新绘制图标，不发布原图标或上游宣传图片，不代表原作者背书。仓库预览使用程序生成的测试图片。Windows 实现按 LICENSE 中的 MIT 许可提供。

v1.3.2 截图容器的液态玻璃材质为原生 C# 实现，参考用户提供的 liquid-glass skill 中背景分层、法线与 Snell 折射、方向高光和轻微色散的方法。发布包不包含该 skill 文件或网页 JavaScript 实现。

v1.4.4 设置窗口继续参考用户提供的 liquid-glass 和 liquid-metal skill：背景独立采样、毛玻璃扩散、边缘折射与高光，以及液态金属的层流条带、谱段采样、点击扰动、独立亮边和泛光分层。材质以原生 C# / WPF 重写，不包含 skill 文件、网页 JavaScript 或原 WebGL 引擎。参考素材的 MIT 声明为 Copyright (c) 2026 jiuyang5354，与本仓库 LICENSE 中的 Windows 实现声明一并保留。

v1.4.5 在同一套原生材质上调整透明度与泛光：降低毛玻璃白色覆盖以保留背景层次，提高液态金属外侧泛光增益，并把柔化与泛光合并进同一条浮点管线，避免裁剪丢失亮边。仍不包含 skill 文件、网页 JavaScript 或原 WebGL 引擎。

v1.4.6 设置玻璃通过原生 WPF 像素着色器采样缓存背景和 Snell 光学贴图，参考 https://glass.outpacestudios.com/ 的边缘折射与方向高光说明及用户提供的 liquid-glass skill。金属计算改在后台线程完成；仍不包含原网站页面代码或原 WebGL 引擎。

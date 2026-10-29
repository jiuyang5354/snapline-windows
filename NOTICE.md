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

## 拖拽暂存架来源

暂存架移植自 https://github.com/jiuyang5354/Dropline- ，参考提交 `51f2d7c5c4a988bdc391af3ec2636b3badb4d451`。已调整为 Snapline 内部组件，共用程序生命周期、设置和托盘。Dropline 的交互灵感来自 Yoink（Eternal Storms Software）；双方无关联，未使用 Yoink 代码或素材。以下保留 Dropline 原 MIT 声明：

```text
MIT License

Copyright (c) 2026 jiuyang5354

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## PDF and image conversion components

The first local PDF toolbox uses PDFsharp-GDI 6.2.4 by empira Software GmbH under MIT, plus its required Microsoft .NET libraries under their bundled licenses. WebP uses the official Google libwebp 1.6.0 Windows x64 cwebp/dwebp utilities. Their copyright notices and license texts are retained in Converters/licenses. Package origins, exact versions and SHA-256 hashes are recorded in windows/conversion-dependencies.lock.json; runtime conversion does not download libraries or upload documents.

The iLovePDF website is a feature-category reference only. No iLovePDF application code, branding or processing service is included. The new native file-list interaction was informed by 21st metadata references, without importing their component code.

## Native tools interface and icons

The native tools layout and selection motion reference Sortable File List by sean0205 and Stretch Tabs by dhxwhy on 21st.dev. Their component sources were consulted during design; the application uses its own WPF controls and animations, without bundling React components or a browser runtime.

QingIcon SVG paths were retrieved through QingIcon MCP and adapted to WPF Geometry with theme colors and adjusted stroke widths. Copyright 2026 Asorn; Apache License 2.0. The upstream license and NOTICE are included as Converters/licenses/QingIcon-LICENSE.txt and QingIcon-NOTICE.txt. Existing Tendedero, Dropline, PDFsharp, WebP and Microsoft component notices are retained.

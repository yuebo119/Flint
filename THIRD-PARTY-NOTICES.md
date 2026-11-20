# 第三方组件许可声明

本文件登记 Flint 运行时依赖的第三方组件及其许可，供下游使用者与再分发者核对。
Flint 本体以 MIT 许可发布（见 `LICENSE`）。本文档对应 0.4.0（2026-11-21）。

## 运行时直接依赖

| 组件 | 版本 | 许可 | 说明 |
| --- | --- | --- | --- |
| System.CommandLine | 3.0.0 | MIT | CLI 解析 |
| Scriban | 7.6.1 | BSD-2-Clause | Hugo 兼容模板引擎 |
| Fluid.Core | 3.0.0 | MIT | Liquid 主力模板引擎 |
| Markdig | 1.6.0 | BSD-2-Clause | Markdown 解析 |
| SixLabors.ImageSharp | 3.1.12 | **Six Labors Split License 1.0**（见下） | 图片解码与变换 |
| Tommy | 3.1.2 | MIT | TOML 解析 |
| VYaml | 1.6.1 | MIT | YAML 解析 |
| System.Diagnostics.PerformanceCounter | 11.0.0 | MIT | DevTools 性能采样（仅开发工具） |

## 传递依赖

| 组件 | 版本 | 许可 | 来源 |
| --- | --- | --- | --- |
| Parlot | 2.3.1 | BSD-3-Clause | Fluid 的解析器 |
| VYaml.Annotations | 1.6.1 | MIT | VYaml |

## Six Labors.ImageSharp 特别说明

ImageSharp 自 3.0 起采用 **Six Labors Split License 1.0**（非 OSI 认证许可）：
满足其资格条款的使用者可按 Apache License 2.0 使用；不满足者需商业许可。
完整条款见 NuGet 包内 `LICENSE` 或 https://sixlabors.com/pricing/。

- **Flint 的立场**：以开源项目方式使用（符合 Split License 的开源资格条款），
  同时锁定 3.1.12 版本（3.2.0 起 Release 构建强制许可校验，见
  `Directory.Packages.props` 中该包的说明）。
- **下游提示**：若你以商业方式再分发 Flint 的二进制产物，请自行核对
  Split License 条款是否覆盖你的使用场景。
- **归属**：ImageSharp 版权归 Six Labors 所有。

## 安全公告状态

ImageSharp 3.1.12 存在 6 条已由 3.2.0/4.1.2 修复的安全公告
（4 High + 2 Moderate，集中在 TIFF 解码/编码、ICC CLUT、HistogramEqualization）。
Flint 的处理：

- 解码格式白名单（PNG/JPEG/GIF/WebP/BMP），TIFF 相关公告不可达
  （见 `src/Flint.Core/Assets/ImageProcessor.cs` 的格式校验）
- ICC/直方图路径未在 Flint 的处理链中触达
- 未升级的原因：3.2.0+ 的许可校验与 Flint 的开源分发不兼容（既定裁决）

`NuGetAudit` 已开启（0.4.0 起），未来新公告会在构建期以警告形式出现，
不阻断构建。具体公告编号的显式压制条目待构建机可访问 nuget.org
漏洞数据源时补登记。

## 外部二进制依赖（不随 Flint 分发）

以下工具由使用者自行安装，Flint 只负责调用：

| 工具 | 用途 | 许可 |
| --- | --- | --- |
| Dart Sass（sass CLI） | SCSS 编译 | MIT |
| Lightning CSS | CSS 压缩 | MPL-2.0 |
| esbuild | JS 打包压缩 | MIT |
| git | 模块管理与部署 | GPL-2.0 |

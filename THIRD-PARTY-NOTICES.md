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

ImageSharp 自 3.0 起采用 **Six Labors Split License 1.0**（2022-06，非 OSI 认证许可）。
该许可把作品"分许可"：符合资格条款者按 **Apache License, Version 2.0** 使用，
其余场景需购买 Six Labors Commercial Use License（https://sixlabors.com/pricing/）。
完整条款见 NuGet 包内 `LICENSE`。

### Flint 依据的资格条款

Split License 1.0 第 2 条规定四类可适用 Apache 2.0 的情形。Flint 依据的是第一条：

> "You are consuming the Work in for use in software licensed under an Open Source
> or Source Available license."

Flint 本体以 MIT 许可发布（见 `LICENSE`），属于 Open Source license，因此
**Flint 对 ImageSharp 的使用依据 Apache License, Version 2.0**。
这一声明是许可条款的硬性要求（"Once granted, You must reference the granted
license only in all documentation"）。

### 下游使用者的适用条款

下载并使用 Flint 发布产物（AOT 单文件或框架依赖包）的使用者，对 ImageSharp 而言
属于 Split License 定义的 **Transitive Package Dependency**（"installed indirectly
by a third party dependency unrelated to Six Labors"），适用第 2 条第二款，
同样依据 Apache License 2.0。

需要自行核对的例外：若你把 Flint 作为**直接依赖**使用（例如 Fork 源码后在企业内部
构建自有产品）且属于年总营收 1M USD 以上的营利实体，则需按第三条自行确认资格，
必要时向 Six Labors 购买商业许可。

### 专利

Split License 1.0 正文不含独立专利条款，但它把授权指向 Apache License 2.0，
而 Apache 2.0 第 3 条包含明确的专利授权（贡献者向使用者授予实施其贡献所需的专利
许可，并含专利诉讼时的授权终止条款）。因此走 Apache 2.0 路径时，专利风险由
Apache 2.0 第 3 条覆盖。

### 再分发义务

依据 Apache License 2.0 再分发 Flint 产物时，需保留版权声明、许可副本，并在
分发物中提供本文件（或等效的第三方许可声明）。Apache License 2.0 全文已随仓库
提供于 `LICENSES/Apache-2.0.txt`（取自 https://www.apache.org/licenses/LICENSE-2.0.txt，
SHA-256 `cfc7749b96f63bd31c3c42b5c471bf756814053e847c10f3eb003417bc523d30`）。

Flint 的 AOT 单文件把 ImageSharp 以 Object form 编入，属 Apache 2.0 允许的
分发形态（"Object" form 明确包含 compiled object code）。

### 风险触发点

出现以下任一情形时，本声明失效，需重新评估并可能购买商业许可：

1. Flint 改为非开源（或非 Source Available）许可发布
2. 把 Flint 作为直接依赖并入闭源商业产品，且实体年营收达到 1M USD 以上

### 版本选择说明

Flint 锁定 3.1.12，因为 3.2.0 起的 Release 构建会强制校验商业许可 key
（3.1.12 是最后一个不校验的版本）。这一锁定同时使 Flint 停留在含 6 条安全公告的
版本上，缓解措施见下节。

### 归属

ImageSharp 版权归 Six Labors 所有（"Copyright (c) Six Labors"）。

> 本文件是工程侧许可合规说明，不构成法律意见。正式商用或再分发前，
> 请按自身情形核对 Split License 原文或咨询法律顾问。

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

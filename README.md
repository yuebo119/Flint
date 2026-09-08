# Flint 静态站点生成器

<p align="left">
  <strong>🔥 .NET 10 打造的高性能静态站点生成器 · NativeAOT 单文件 · 无需运行时</strong>
</p>

---

## ⚡ 性能对比（2026-09-09 实测）

> 测试环境：Windows 10 x64 · 32 核 · 同机同语料同模板 · 冷构建 3 次中位数
> 对照引擎：Hugo v0.165.0 Extended 官方二进制 · Flint Release + NativeAOT

### 三语料端到端构建

> 页数 = 实际构建产出（html 页面，非语料文件数）；内存 = 构建进程峰值
> （USS，进程独占内存，psutil 采样）。每格双引擎 3 次中位数。

| 语料 | 语料 md | **产出页面（Hugo / Flint）** | 内容形态 | Hugo | **Flint AOT** | 比值 | 峰值内存 Hugo / Flint |
|------|--------:|------|---------|------:|--------------:|:----:|------|
| 万页合成 | 10,000 | 10,001 / 10,004 ✓ | 同构 lorem | 5540ms | **4245ms** | 0.77x | 392MB / **326MB** |
| Hugo 官方基准站点 | 662 | 2,239 / 718 ⚠ | 真实异构（5 站点） | 1257ms | **379ms** | ⚠ | — |
| MDN Web Docs | 14,621 | 14,623 / 14,576 ✓ | 技术文档 | 8847ms | **6374ms** | 0.72x | — |

> **⚠ Hugo 官方基准站点行**：产出页数不对称（Hugo 2239 页含全目录
> section list + 双语言展开；Flint 718 页）——两引擎对该语料的页面映射
> 规则不同，**总时间与页均时间均不可直接对比**，该行仅作量级参考。
> 另两行产物对称已审计 ✓。

### 内存峰值（对称语料万页合成，psutil 采样 2 轮）

| 引擎 | 峰值 RSS | 峰值 USS（独占） |
| ---- | -------- | ---------------- |
| Hugo v0.165.0 | 408-416 MB | 383-392 MB |
| **Flint AOT** | **344-369 MB** | **326-352 MB** |

**Flint 内存低约 15%**（Server GC + AOT 无 JIT 基础设施开销）。

### 主题复杂度阶梯（复杂度增长不会反转优势）

| 层级 | 主题负担 | Hugo | **Flint** | 比值 |
|------|---------|------:|----------:|:----:|
| L1 基础 | 单页渲染 | 634ms | **546ms** | 0.86x |
| L2 中等 | + 侧边栏 O(N) 全站循环 | 1087ms | **972ms** | 0.89x |
| L3 重度 | + 双 O(N) 循环 + 嵌套 partial + partialCached | 1863ms | **1592ms** | 0.85x |

复杂度每升一级两引擎等比例变慢（斜率平行）——**Flint 全层级保持 11-15% 领先**。

### 进程内基准（Release，11/11 全绿）

| 指标 | 数值 |
|------|------|
| Markdown 解析 | 181,000 文件/秒 |
| 模板渲染 | 44,700 页/秒 |
| 增量构建加速 | **6.9x**（增量 43ms） |
| 配置加载 | 0.20ms |
| 可扩展性 | O(n) 线性 |

> 口径说明：CLI 对比为 NativeAOT 原生二进制端到端（含进程启动）；
> 进程内套件为 Release JIT。完整方法论与产物对称性审计见
> [benchmarks/REPORT.md](benchmarks/REPORT.md)，复现命令见
> [测试与基准](#-测试与基准)。

---

## ✨ 特性

- **🚀 极速构建**：万页 3~4 秒，增量 43ms，1000+ 页/秒起步
- **📦 单文件部署**：NativeAOT 原生编译 ~18MB，无运行时依赖
- **🔄 Hugo 语义兼容**：目录结构、Front Matter（YAML/TOML/JSON）、permalink、taxonomy、渲染钩子、partialCached
- **📝 现代内容管线**：CommonMark + GFM、语法高亮、数学公式、渲染钩子（链接/图片/标题）
- **⚡ 开发体验**：Kestrel 热重载、增量构建、多格式配置、环境变量覆盖
- **🎨 Scriban 模板**：完整脚本语言（条件/循环/函数/继承），AI 辅助转写友好
- **🛡️ 生产加固**：Server GC、路径逃逸防护、缓存原子写、AOT 全链路验证

---

## 🛠️ 技术栈

| 技术 | 说明 |
|------|------|
| **.NET 10** | 最新 LTS |
| **NativeAOT** | 原生编译，单文件发布 |
| **Scriban 7.4** | 模板引擎（快速路径 + LoopLimit 对齐 Hugo） |
| **Markdig 1.3** | CommonMark + GFM 解析 |
| **Kestrel** | 开发服务器（热重载） |
| **ImageSharp 3.1** | 图片处理（缩放/格式转换/响应式） |
| **Dart Sass / esbuild** | Sass 编译与 JS 打包（可选组件） |
| **Server GC** | 多核并行回收（批处理吞吐） |

---

## 🚀 快速开始

### 安装

```bash
git clone https://github.com/yuebo119/flint.git
cd flint/Flint

# Windows（NativeAOT 单文件）
dotnet publish src/Flint.Cli -c Release -r win-x64 -o ./publish

# Linux
dotnet publish src/Flint.Cli -c Release -r linux-x64 -o ./publish

# macOS (Apple Silicon)
dotnet publish src/Flint.Cli -c Release -r osx-arm64 -o ./publish
```

### 创建站点

```bash
flint new site my-blog
cd my-blog
flint new content posts/hello-world.md
flint serve        # http://localhost:1313，热重载
```

生成的目录结构：

```
my-blog/
├── archetypes/          # 内容模板
│   └── default.md
├── content/             # Markdown 内容
├── layouts/             # 模板文件
│   └── _default/
│       ├── baseof.html  # 基础模板
│       ├── list.html    # 列表模板
│       └── single.html  # 单页模板
├── static/              # 静态文件
└── flint.toml           # 站点配置
```

### 构建发布

```bash
flint build --minify   # 输出到 public/，可直接部署任意静态托管
```

### 部署

`public/` 目录可部署到：**GitHub Pages**（推送到 gh-pages 分支）、
**Netlify / Vercel / Cloudflare Pages**（连接仓库设置构建命令）、
**任意 Web 服务器**（直接上传）。或使用 `flint deploy <target>`
（s3://、gh-pages、netlify、vercel，依赖对应 CLI）。

---

## 📖 命令参考

| 命令 | 说明 |
|------|------|
| `flint new site <name>` | 创建新站点 |
| `flint new content <path>` | 创建内容（`--kind` 指定模板） |
| `flint new theme <name>` | 创建主题骨架 |
| `flint build` | 构建站点（`-s` 源目录 `-o` 输出 `--minify` `--clean`） |
| `flint serve` | 开发服务器（`-p` 端口 `-l` 热重载开关） |
| `flint new content <path> --missing-layout skip` | 见构建选项 |
| `flint mod get <url>` | 模块管理（init/get/update/list/remove） |
| `flint deploy <target>` | 部署（s3://、gh-pages、netlify、vercel） |

### 构建选项

| 选项 | 简写 | 默认值 | 说明 |
|------|------|--------|------|
| `--source` | `-s` | `.` | 源目录 |
| `--output` | `-o` | `public` | 输出目录 |
| `--minify` | `-m` | false | 压缩 HTML/CSS/JS |
| `--drafts` | `-D` | false | 包含草稿内容 |
| `--future` | `-F` | false | 包含未来日期的内容 |
| `--missing-layout` | - | `error` | 缺失模板处理：`error` / `skip` |
| `--clean` | - | false | 构建前清理输出目录 |
| `--verbose` | `-v` | false | 详细输出 |

### 服务器选项

| 选项 | 简写 | 默认值 | 说明 |
|------|------|--------|------|
| `--port` | `-p` | `1313` | 服务器端口 |
| `--host` | - | `localhost` | 绑定主机地址 |
| `--open` | - | true | 自动打开浏览器 |
| `--livereload` | `-l` | true | 启用热重载 |
| `--drafts` | `-D` | true | 包含草稿内容 |

---

## 📐 最佳实践

### 大站点模板性能

侧边栏/导航等全站循环，用 **partialCached** 缓存（输出不依赖页面时）：

```scriban
{{ partialcached "partials/sidebar" "site-wide" }}
```

依赖页面内容的 partial 用 `include`——缓存与正确性的分界线。

### 缺失模板的构建策略

页面声明了自定义 `layout:` 而模板不存在时，默认构建失败（fail-fast）。
大规模站点迁移期可用宽容模式跳过：

```bash
flint build --missing-layout skip
```

### 增量友好的内容组织

- 内容按 section 分目录（content/posts/、content/docs/）
- permalink 未配置时按目录结构生成——跨 section 同名页面不冲突
- `:git` 日期源需 `enableGitInfo = true`

### 环境变量覆盖（CI/多环境）

```bash
export FLINT_BASEURL="https://staging.example.com/"
flint build
```

### AOT 发布（生产推荐）

```bash
dotnet publish src/Flint.Cli -c Release -r win-x64 -p:PublishAot=true
```

---

## ⚙️ 配置

支持 TOML / YAML / JSON，Hugo 配置高度兼容：

```toml
baseURL = "https://example.com/"
title = "我的博客"
languageCode = "zh-cn"
timeZone = "Asia/Shanghai"          # 无偏移日期按此时区解释
enableGitInfo = true                # 启用 date: ":git"

[params]
  author = "作者名"

[menu]
  [[menu.main]]
    name = "文章"
    url = "/posts/"
    weight = 2

[taxonomies]
  tag = "tags"
  category = "categories"
```

环境变量覆盖（`FLINT_` 前缀，`__` 访问嵌套）：

```bash
export FLINT_BASEURL="https://staging.example.com/"
export FLINT_PARAMS_AUTHOR="新作者"
```

---

## 📝 内容编写

Front Matter 支持 YAML / TOML / JSON 三格式：

```yaml
---
title: "文章标题"
date: 2026-09-09T10:00:00+08:00
lastmod: ":git"              # 取 git 最后提交时间
tags: ["Go", "Hugo"]
draft: false
---

文章内容...
```

Markdown 支持 CommonMark + GFM：代码高亮、表格、任务列表、自动链接、
删除线、数学公式。

#### 日期特殊源（对齐 Hugo）

| 源 | 含义 | 前提 |
|---|------|------|
| `:git` | 文件最后一次提交的修改时间 | `enableGitInfo = true` |
| `:filemodtime` | 文件修改时间 | 无 |
| `:filename` | 文件名 `YYYY-MM-DD-` 前缀 | 未显式设置 date 时自动生效 |

#### 渲染钩子（对齐 Hugo render hooks）

`layouts/_markup/` 下的钩子模板可定制链接/图片/标题的 HTML 输出：

| 模板 | 拦截对象 | 可用变量 |
|---|---|---|
| `render-link.html` | 链接（含自动链接） | `destination`、`title`、`text`、`plain_text` |
| `render-image.html` | 图片（优先于 render-link） | 同上 |
| `render-heading.html` | 标题 | `level`、`id`（自动锚点）、`text`、`plain_text` |

```html
<!-- layouts/_markup/render-link.html -->
<a class="ext" href="{{ destination }}" rel="noopener">{{ plain_text }}</a>
```

无对应模板时走默认渲染，零开销。

---

## 🎨 模板语法（Scriban）

```html
{{ page.title }}                    <!-- 页面字段 -->
{{ site.params.author }}            <!-- 站点配置 -->
{{ for post in site.regular_pages }}{{ end }}   <!-- 循环 -->
{{ if page.draft }}{{ end }}        <!-- 条件 -->
{{ include "partials/header" }}     <!-- partial -->
{{ partialcached "footer" "v1" }}   <!-- 缓存 partial -->

<!-- 模板继承 -->
{{ extends "_default/baseof.html" }}
{{ block "main" }}...{{ end }}
```

渲染钩子（对齐 Hugo render hooks）：

```html
<!-- layouts/_markup/render-link.html -->
<a class="ext" href="{{ destination }}" rel="noopener">{{ plain_text }}</a>
```

### HTML 转义契约（安全须知）

Flint **不自动转义**模板输出：Scriban 渲染配置为不启用 HTML 自动转义，
内容字段（`page.content`、`page.title` 等）与 Markdown 渲染产物直接写入
页面。这与 Hugo 的"默认按上下文转义 + safe 类型豁免"模型相反，安全责任
分配如下：

- Markdown 正文由 Markdig 管线产出（原始 HTML 由 `markup.goldmark.renderer.unsafe` 控制）；
- front matter 字段与用户配置值**原样输出**——不可信内容需模板作者自行 `html.escape`；
- `safe*` 系列函数（safeHTML/safeCSS/safeHTMLAttr 等）是恒等标记（对齐
  Hugo 语义），不做转义也不做豁免。

---

## 🧪 测试与基准

```bash
dotnet test                                              # 全量测试
dotnet run --project tests/Flint.PerformanceTests -c Release   # 性能套件
python scripts/ssg-bench.py --pages 10000                # 万页 Hugo 对比
python scripts/complexity-bench.py                       # 主题复杂度阶梯
powershell -File scripts/perf-gate.ps1                   # 性能回归门禁
```

📊 **完整性能报告**：[benchmarks/REPORT.md](benchmarks/REPORT.md)
（三语料矩阵、复杂度阶梯、公平性声明、产物审计、可复现命令）

---

## 📚 文档

- [使用指南](docs/USAGE.md)
- [API 文档](docs/API.md)
- [性能优化](docs/PERFORMANCE-OPTIMIZATION.md)

---

## 🏗️ 项目结构

```
Flint/
├── src/
│   ├── Flint.Cli/           # 命令行工具
│   └── Flint.Core/          # 核心库
│       ├── Abstractions/    # 接口定义
│       ├── Assets/          # 资源处理
│       ├── Configuration/   # 配置加载
│       ├── Content/         # 内容解析
│       ├── Models/          # 数据模型
│       ├── Server/          # 开发服务器
│       ├── Site/            # 站点构建
│       └── Templates/       # 模板渲染
├── tests/
│   ├── Flint.Core.Tests/
│   ├── Flint.IntegrationTests/
│   └── Flint.PerformanceTests/
└── benchmarks/              # 性能基准（语料/脚本/报告/门禁）
```

---

## 📄 许可证

MIT License

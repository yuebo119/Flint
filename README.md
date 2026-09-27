# Flint

<p align="center">
  <strong>🔥 高性能静态站点生成器 · C# / .NET 10 · NativeAOT 单文件</strong>
</p>

<p align="center">
  <a href="README.en.md">English</a> | 中文
</p>

<p align="center">
  <img alt="license" src="https://img.shields.io/badge/license-MIT-blue">
  <img alt=".NET" src="https://img.shields.io/badge/.NET-10-purple">
  <img alt="NativeAOT" src="https://img.shields.io/badge/deploy-NativeAOT%20single--file-green">
  <img alt="themes" src="https://img.shields.io/badge/themes-21%20verified-orange">
</p>

Flint 是一个用 C# / .NET 10 实现的静态站点生成器：万页站点 3.5 秒构建，
增量构建 75ms，交付物是一个原生单文件——并内置 Hugo 主题的
AST 级自动迁移工具链。

| 万页构建 | 增量构建 | 交付物 | 真实语料内存 |
|:---:|:---:|:---:|:---:|
| **3.5s**（Hugo 的 0.81x） | **75ms** | **28MB** 单文件 | 与 Hugo 持平（-2%） |

---

## 目录

- [亮点](#-亮点)
- [快速开始](#-快速开始)
- [案例站](#-案例站)
- [使用](#-使用)
- [配置与命令](#️-配置与命令)
- [最佳实践](#-最佳实践)
- [性能](#-性能)
- [与主流 SSG 对比](#-与主流-ssg-对比)
- [常见问题](#-常见问题)
- [路线图](#️-路线图)
- [开发](#️-开发)
- [贡献](#-贡献)
- [交流](#-交流)
- [许可证](#-许可证)

---

## ✨ 亮点

**构建吞吐与可预测性** —— 万页 3.5 秒（Hugo 的 0.81 倍），增量 75ms，
三次冷构建离散 ±1%，负载漂移敏感度低于对照（×1.4 vs ×1.6）。增量由
依赖图驱动：改一篇文章只重建受影响的页面。

**零依赖原生部署** —— .NET 10 NativeAOT 单文件应用：无运行时、无
node_modules、无 JIT 预热，交付物是一个 exe。

**Hugo 兼容层** —— lookup order / 渲染钩子 / 短码 API / 25 命名空间
函数全对齐；ThemeMigrator 以 AST 级转换 Go template，无法判定的语义
亮 `TODO-HUGO` 不静默错译，21 个真实主题已验证——迁移从"重写"降为
"一条命令 + 少量人工裁决"。

**进程内资产管线** —— Sass 进程内编译（DartSassHost 原生库）；TS
类型剥离自研（无法识别的语法原样保留，浏览器报错优于静默错位）；
图片处理结果落盘缓存。带 TS/SCSS 的重度主题零 npm 依赖构建。

**工程化质量保障** —— `perf gate` 回归门禁（三轮中位数棘轮，劣化在
合并前拦截）；FsCheck 属性测试，集成套件 1647/1647 全绿；输出路径
逃逸防护、缓存条目原子写。

引擎侧：三相全并行管线（解析/渲染/写入 + Server GC）、内容签名变换
缓存（一次执行全站复用）、Win32 单段写（写入中位 -42%）、Scriban
语义桥（宽容比较 / 集合归一，真实主题零改动渲染）。

---

## 🚀 快速开始

### 环境要求

- .NET SDK 10.0.401 或更高（仅构建 Flint 本体时需要；产出的 exe 无任何运行时依赖）
- Git

### 安装

```bash
git clone https://github.com/yuebo119/Flint.git
cd Flint

# Windows（NativeAOT 单文件 ~28MB；缺 -p:PublishAot=true 会产出 ~89MB 非 AOT 单文件）
dotnet publish src/Flint.Cli -c Release -r win-x64 -p:PublishAot=true -o ./publish

# Linux / macOS (Apple Silicon)
dotnet publish src/Flint.Cli -c Release -r linux-x64 -o ./publish
dotnet publish src/Flint.Cli -c Release -r osx-arm64 -o ./publish
```

把 `flint` 加入 PATH（否则 shell 找不到命令）：

```bash
# Windows（PowerShell，持久化到用户 PATH）
[Environment]::SetEnvironmentVariable("Path", $env:Path + ";$PWD\publish", "User")

# Linux / macOS
export PATH="$PWD/publish:$PATH"        # 当前会话
sudo ln -s "$PWD/publish/flint" /usr/local/bin/flint   # 或软链一次到位
```

验证：`flint version`。

### 五分钟建站

```bash
flint new site my-blog
cd my-blog
flint new content posts/hello-world.md
flint serve        # http://localhost:1313，热重载
flint build --minify   # 输出到 public/，可直接部署
```

### 部署

`public/` 部署到 GitHub Pages / Netlify / Vercel / Cloudflare Pages 或任意
Web 服务器；或 `flint deploy <target>`（s3://、gh-pages、netlify、vercel，
依赖对应 CLI）。

---

## 🎨 案例站

仓库自带 21 个 Hugo 迁移主题的演示站（统一中文语料 100 篇长文）与一个
主题画廊（预览图卡片网格，点击直达各演示站）：

```bash
# 前置：构建引擎与迁移器（DevTools 按固定路径查找）
dotnet build src/Flint.Cli -c Release -r win-x64
dotnet build src/Flint.ThemeMigrator

dotnet run --project src/Flint.DevTools -- demo build --serve   # 全部主题 + 启动服务
dotnet run --project src/Flint.DevTools -- demo build narrow    # 单主题
dotnet run --project src/Flint.DevTools -- demo gallery --serve # 画廊（8400 端口）
dotnet run --project src/Flint.DevTools -- demo stop            # 停止服务
```

画廊 `http://127.0.0.1:8400/`，各主题 8401-8421。注意事项（Sass 定位、
fixit 构建时长、目录锁）见 [demo-sites/README.md](demo-sites/README.md)。

---

## 📖 使用

> 模板与配置语义与 Hugo 高度兼容，Hugo 站点与主题可低成本迁移；
> 下文以 Flint 视角描述，与 Hugo 有实质差异处单独标注。

### 写内容：Markdown + 元数据

一篇文章就是一个 `.md` 文件：正文 Markdown，开头一段元数据
（Front Matter）声明标题、日期、标签——YAML / TOML / JSON 任选：

```yaml
---
title: "文章标题"
date: 2026-09-09T10:00:00+08:00
lastmod: ":git"              # 取 git 最后提交时间
tags: ["Go", "Hugo"]
draft: false
---

正文（CommonMark + GFM：代码高亮、表格、任务列表、数学公式…）…
```

日期可自动推断：`:git`（需 `enableGitInfo = true`）、`:filemodtime`、
`:filename`（文件名 `YYYY-MM-DD-` 前缀，未显式设 date 时生效）。

**渲染钩子**：改链接/图片/标题的默认 HTML，在 `layouts/_markup/` 放
同名模板（`render-link.html` / `render-image.html` / `render-heading.html`，
变量 `destination`/`text`/`level`/`id` 等）；不放则走默认渲染，零开销。

### 写模板：Scriban

模板决定页面长什么样，花括号里取数据：

```html
{{ page.title }}                                <!-- 页面字段 -->
{{ site.params.author }}                        <!-- 站点配置 -->
{{ for post in site.regular_pages }}{{ end }}   <!-- 循环 -->
{{ if page.draft }}{{ end }}                    <!-- 条件 -->
{{ include "partials/header" }}                 <!-- 引入 partial -->
{{ partialcached "footer" "v1" }}               <!-- 缓存 partial（输出与页面无关时）-->
```

骨架复用用 capture + include（Scriban 无 extends/block）：

```html
{{ capture content }}
<article>{{ page.content }}</article>
{{ end }}
{{ include "baseof.html" content: content }}    <!-- baseof 里 {{ content }} 接收此块 -->
```

### 安全须知：HTML 转义契约

Flint **不自动转义**模板输出——`page.title`、`page.content` 原样写入页面。
自己写的站点这通常正合适；内容来自**不可信来源**（访客投稿、外部导入）
时，模板里显式 `{{ transform.HTMLEscape page.title }}`。`safe*` 函数是
恒等标记，不提供转义保护；Markdown 正文的原始 HTML由
`markup.goldmark.renderer.unsafe` 控制。

### 用主题：站点永远优先

主题是一整套模板 + 样式 + 示例内容，`flint mod get <repo>` 装到 `themes/`。
**核心规则：同名时你的文件永远赢，主题只补缺。**

页面用哪个模板由**页面特征**决定——`layouts/posts/single.html` 只对
posts 生效，`layouts/blog/` 按 front matter `type` 路由：

| 页面 kind | 候选顺序（靠前优先） |
|---|---|
| 普通页 | `{type}/{layout}` → `{type}/single` → `{section}/{layout}` → `{section}/single` → `{layout}` → `single` → `all` |
| 首页 | `index` → `home` → `list` → `all` |
| section | `{section}/section` → `{section}/list` → `section/section` → `section/list` → `list` → `all` |
| taxonomy | `{taxonomy}/terms` → `{taxonomy}/taxonomy` → `{taxonomy}/list` → `terms` → `taxonomy` → `list` |
| term | `{taxonomy}/term` → `{taxonomy}/taxonomy` → … → `term` → `taxonomy` → `list` |

主题目录的构建行为：`layouts/`（含短码、渲染钩子）按序回退站点覆盖；
`static/`、`assets/` 合并进输出（`static/css/a.css` → `public/css/a.css`）；
`content/` 站点优先主题补缺；`config/_default/params.*` 与 `theme.toml
[params]` 作默认值、站点深覆盖；`archetypes/`、`data/`、`i18n/` 同规则
回退。主题自带 `404.html`/`robots.txt`/`rss.xml`/`sitemap.xml` 时以模板
渲染替代内置生成。

模板能力速览：`{{ render "view" }}` 内容视图、`{{ partial "func/x" }}`
返回值、`{{ includeCached "x" }}` 缓存、`{{ page.file.path }}` /
`{{ page.resources }}` / `{{ i18n "key" }}`、分类页 `page.data.*`。完整
语义面——kind 谓词、`.Scratch`/`.Store`、Pages 方法族（`ByDate`/
`GroupBy`/`Related`…）、25 个命名空间对象（`strings.*`/
`collections.*`/`resources.*`…）、`opengraph` 等内置 partial 兜底。

多主题叠加：`theme = "t1,t2"`（前面优先），全链路站点 > t1 > t2；
i18n 用 `i18n/<lang>.toml`，`{{ i18n "key" }}` 取翻译，缺键返回空串。

> 更完整的用法细节见 [docs/USAGE.md](docs/USAGE.md)；API 面见
> [docs/API.md](docs/API.md)；性能优化记录见
> [docs/PERFORMANCE-OPTIMIZATION.md](docs/PERFORMANCE-OPTIMIZATION.md)；
> 主题兼容方案见 [docs/THEME-COMPAT-PLAN.md](docs/THEME-COMPAT-PLAN.md)。

---

## ⚙️ 配置与命令

配置支持 TOML / YAML / JSON，兼容 Hugo 配置形态：

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

环境变量覆盖（`FLINT_` 前缀，`__` 访问嵌套）：`FLINT_BASEURL`、
`FLINT_PARAMS_AUTHOR`。

### 命令参考

| 命令 | 说明 |
|------|------|
| `flint new site <name>` | 创建新站点 |
| `flint new content <path>` | 创建内容（`--kind` 指定模板） |
| `flint new theme <name>` | 创建主题骨架 |
| `flint build` | 构建站点 |
| `flint serve` | 启动开发服务器 |
| `flint mod <sub>` | 模块管理（init/get/update/list/remove） |
| `flint deploy <target>` | 部署（s3://、gh-pages、netlify、vercel） |
| `flint version` | 显示版本信息 |

**构建选项**：`--source/-s`（默认 `.`）、`--output/-o`（`public`）、
`--minify/-m`、`--drafts/-D`、`--future/-F`、`--clean`、`--verbose/-v`、
`--missing-layout`（`error`/`skip`，默认 error）。

**服务器选项**：`--port/-p`（1313）、`--host`（localhost）、`--open`（默认
true）、`--livereload/-l`（默认 true）、`--drafts/-D`（默认 true）、
`--source/-s`（`.`）。

---

## 📐 最佳实践

把 Flint 的速度、缓存与工具链榨到底——每条对着引擎的一个真实优势。

1. **用 partialCached 吃掉全站循环**——侧边栏/导航等输出不依赖页面的
   循环一行缓存全站复用（`{{ partialcached "partials/sidebar" "site-wide" }}`）；
   依赖页面内容的用 `include`。复杂度阶梯实测：全站级 O(N) 循环是模板
   开销的主要来源
2. **站在增量构建这边**——内容按 section 分目录；`:git` 配
   `enableGitInfo = true`；显式设 `timeZone` 防跨机器排序漂移；`--clean`
   只在需要全量重建时用
3. **生产三件套**——`dotnet publish -p:PublishAot=true`（原生单文件应用）
   + `flint build --minify` + 资源指纹化：
   ```scriban
   {{ $css = resources.Get "css/main.css" | resources.Minify | resources.Fingerprint }}
   <link rel="stylesheet" href="{{ $css.RelPermalink }}" integrity="{{ $css.Data.Integrity }}">
   ```
   Sass 编译结果构建内按内容签名缓存，多页引用同一份 SCSS 只编译一次
4. **主题迁移让 ThemeMigrator 干重活**——① `dotnet run --project
   src/Flint.ThemeMigrator -- <hugo-dir> <flint-dir>` AST 级转换，搜
   `TODO-HUGO` 逐个人工裁决；② `flint build --missing-layout skip`
   宽容模式先跑通再收敛（清除 Hugo 0.145 已移除的旧键如 `_build`）；
   ③ `theme matrix <主题>` + `audit assets/elements` 对比双侧产物；
   ④ `demo build <主题> --serve` 统一语料预览。Flint 内置 12 个短码，
   主题私有短码随主题迁移即生效
5. **不可信内容显式转义**——见上文转义契约
6. **多环境用环境变量**——一份配置走遍 dev/staging/prod，CI 注入切换
7. **把 perf gate 拉进 CI**——基线存于 `scripts/perf-baseline.json`，
   优化后重录收紧水位；Flint 的 ±1% 离散度让阈值可以设得很紧

---

## 📊 性能

> 同机同语料同窗实测（2026-09-25 安静窗口，冷构建 3 次中位数）·
> Windows 10 x64 · 32 核 · .NET SDK 10.0.401 · 对照 Hugo v0.165.0
> Extended 官方二进制 vs Flint（Release + NativeAOT 单文件）。
> 完整方法论与公平性声明见 [benchmarks/REPORT.md](benchmarks/REPORT.md)，
> 复现命令见[开发](#️-开发)一节。

### 端到端构建

| 语料 | 页数 | Hugo | **Flint AOT** | 比值 |
|------|------|-----:|--------------:|:----:|
| 万页合成 | 10,007 | 4336ms | **3530ms** | **0.81x** |
| MDN Web Docs | 14,576 | 8515ms | **7086ms** | **0.83x** |

规模越大优势越大，且赢得稳定：万页三次离散 ±1%（3505-3540ms）；同机
从安静窗口进入负载窗口，Hugo 漂移 ×1.6、Flint ×1.4。

### 主题复杂度阶梯（1000 页 × L1/L2/L3）

| 层级 | 主题内容 | Hugo | **Flint** | 比值 |
| ---- | -------- | ---- | ----- | ---- |
| L1 基础 | 单页渲染 | 649ms | **488ms** | **0.75x** |
| L2 中等 | + 侧边栏 O(N) 全站循环 | 830ms | **746ms** | **0.90x** |
| L3 重度 | + 双 O(N) 循环 + 嵌套 partial + partialCached | 1266ms | **1112ms** | **0.88x** |

### 内存峰值（构建进程，RSS / USS · MB）

| 语料 | Hugo | **Flint** | USS 差异 |
|------|------|-----------|---------|
| 万页合成 | 440 / 415 | **530 / 514** | +24% |
| MDN 14,621 页 | 1531 / 1505 | **1494 / 1476** | **-2%（更省）** |

### 进程内套件（Release，11/11 全绿）

Markdown 解析 238k 文件/秒 · 模板渲染 69k 页/秒 · 增量构建 75ms（完整
构建 214ms，加速 2.82x）· 配置加载 0.20ms · 并发加速比 1.12x ·
病态检测随集成套件 1647/1647 全绿。

---

## 🆚 与主流 SSG 对比

| 维度 | **Flint** | Hugo | Astro | Eleventy | Jekyll | Hexo |
|---|---|---|---|---|---|---|
| 实现与运行时 | **C# / .NET 10，AOT 单文件应用** | Go 单二进制 | Node + 依赖树 | Node + 依赖树 | Ruby + gems | Node + 依赖树 |
| 模板系统 | **Scriban（完整脚本语言）** | Go template（刻意受限） | UI 组件（React/Vue/Svelte…） | 多引擎（Liquid/Nunjucks/EJS…） | Liquid | EJS / Pug 等 |
| 构建性能（万页） | **3.5s（同机实测）** | 4.3s（同机实测） | 未实测 | 未实测 | 未实测 | 未实测 |
| 增量构建 | **75ms** | 支持 | 支持 | 支持 | 有限 | 支持 |
| 资产管线 | **进程内 Sass + TS 类型剥离 + minify + 指纹** | Hugo Pipes（Extended） | Vite 工具链 | 插件 | 插件 | 插件 |
| 图片处理 | **resize/fit/fill/crop，响应式** | 内置 | sharp 生态 | 插件 | 插件 | 插件 |
| 内容模型 | **三格式 Front Matter + taxonomy + 短码** | 同类 | Content Collections（typed） | data cascade | Front Matter + taxonomy | Front Matter |
| 多语言 | **i18n 文件 + 主题回退** | 完整 i18n | i18n 路由 | 社区方案 | 插件为主 | 多语言 |
| 主题系统 | **模块化 + 版本锁定 + 多主题叠加** | Hugo Modules | npm / 复制安装 | 无正式机制 | gem 主题 | npm 主题 |
| 迁移工具 | **ThemeMigrator（AST 级主题转换）** | 文章导入（jekyll） | — | — | — | — |
| 质量保障 | **性能门禁 + 属性测试** | — | — | — | — | — |
| 控制台编码 | **强制 UTF-8** | Windows 下 GBK | UTF-8 | UTF-8 | UTF-8 | UTF-8 |

> 除 Flint vs Hugo 为同机同语料同窗实测外，其余引擎未在本仓库基准中
> 实测，表中为公开结构性特征。Flint vs Hugo 的八个实测差异点全记录见
> [docs/FLINT-VS-HUGO.md](docs/FLINT-VS-HUGO.md)，主题兼容矩阵见
> [docs/HUGO-COMPAT-MATRIX.md](docs/HUGO-COMPAT-MATRIX.md)。

---

## ❓ 常见问题

**Windows 控制台输出乱码？**
Flint 全链路强制 UTF-8 输出；Windows 控制台默认 GBK 代码页，执行
`chcp 65001` 切换到 UTF-8 即可正常显示。

**`flint serve` 或演示站端口被占用？**
`dotnet run --project src/Flint.DevTools -- demo stop` 清理演示站服务；
开发服务器用 `--port` 换端口，或 `netstat -ano | grep :1313` 找到 PID
后结束进程（Windows 下服务进程工作目录在 `public/` 内会锁目录，需先
停服务再重建）。

**SCSS 主题构建后样式没编译？**
模板里的 `toCSS` 路径调用外部 Dart Sass CLI，按 `FLINT_SASS` 环境变量 →
Flint.exe 旁的 `dart-sass*/` → PATH 上的 `sass` → 仓库 `tools/dart-sass/`
顺序定位；装好 sass 或设 `FLINT_SASS` 指向可执行文件。

**fixit 主题构建很慢是正常的吗？**
正常——它在 100 篇统一语料 + 全分类/标签分页下约 5.5 分钟（631 页），
单站超时上限已放宽到 600s；其余主题均在秒级到分钟级。

---

## 🗺️ 路线图

- Hugo 兼容层持续收敛：缺口清单与优先级见
  [docs/HUGO-GAP-TASKS.md](docs/HUGO-GAP-TASKS.md)，
  主题兼容矩阵见 [docs/HUGO-COMPAT-MATRIX.md](docs/HUGO-COMPAT-MATRIX.md)
- 主题覆盖从当前 21 个验证主题继续扩展（候选池见 `theme-migrator/candidates/`）
- 短码独立生态建设（当前靠主题迁移携带主题私有短码）

---

## 🛠️ 开发

### 技术栈

| 技术 | 说明 |
|------|------|
| **.NET 10** | 最新 LTS，NativeAOT 原生编译，单文件发布 |
| **Scriban 7.5** | 模板引擎（大列表无迭代上限） |
| **Markdig 1.4** | CommonMark + GFM 解析 |
| **DartSassHost** | 进程内 Sass 编译（三平台原生库，无需外部 CLI） |
| **NUglify** | HTML/CSS/JS 压缩（`--minify`） |
| **ImageSharp 3.1** | 图片处理（缩放/格式转换/响应式） |
| **Tomlyn / YamlDotNet** | TOML 配置与 YAML Front Matter 解析 |
| **System.CommandLine** | CLI 命令行框架 |
| **Kestrel** | 开发服务器（热重载） |
| **Server GC** | 多核并行回收（批处理吞吐） |
| **xunit.v3 / FsCheck / BenchmarkDotNet** | 测试与基准（测试侧依赖，不入产品） |

### 测试与基准

```bash
dotnet run --project tests/Flint.Core.Tests -c Release        # Core 全量测试
dotnet run --project tests/Flint.IntegrationTests -c Release  # 集成测试
dotnet run --project tests/Flint.PerformanceTests -c Release  # 性能套件

dotnet run --project src/Flint.DevTools -- bench ssg          # 万页 Flint vs Hugo
dotnet run --project src/Flint.DevTools -- bench complexity   # 复杂度阶梯
dotnet run --project src/Flint.DevTools -- bench memory       # 峰值内存采样
dotnet run --project src/Flint.DevTools -- perf run           # 性能套件 + HTML 报告
dotnet run --project src/Flint.DevTools -- perf gate          # 性能回归门禁
```

### 项目结构

```
Flint/
├── src/
│   ├── Flint.Cli/               # CLI（产品入口，AOT 发布面）
│   ├── Flint.Core/              # 核心库（Abstractions/Assets/Configuration/
│   │                            #   Content/Models/IO/Modules/Server/Site/Templates）
│   ├── Flint.AiGate/            # AI 开发工作流门禁工具
│   ├── Flint.DevTools/          # 开发运维工具集（bench/audit/corpus/release/perf/theme/demo）
│   └── Flint.ThemeMigrator/     # Hugo 主题迁移（Go template → Scriban）
├── tests/                       # Core / Integration / Performance 三套件
├── demo-sites/                  # 案例站（21 主题 + 画廊 + 统一语料）
├── theme-migrator/              # 主题语料（themes/ 克隆源、candidates/）
└── benchmarks/                  # 性能基准（语料/报告/门禁）
```

---

## 🤝 贡献

欢迎 issue 与 PR。提交前请跑通相关测试套件；涉及构建性能的改动请附带
`perf gate` 前后对比（三轮中位数）。Hugo 主题兼容性缺陷请附主题名与
`demo build` 复现步骤。

---

## 💬 交流

<p align="center">
  <img src="docs/images/qq-group.jpg" alt="Flint 交流群（QQ）" width="260">
</p>

<p align="center">扫码加入 QQ 交流群——使用问题、主题迁移、性能反馈都欢迎。</p>

---

## 📄 许可证

MIT License

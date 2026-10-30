<h1 align="center">Flint</h1>

<p align="center">
  <strong>🔥 高性能静态站点生成器 · C# / .NET 10+ · NativeAOT 单文件</strong>
</p>

<p align="center">
  <a href="README.en.md">English</a> | 中文
</p>

<p align="center">
  <a href="https://github.com/yuebo119/Flint/releases/latest"><img alt="release" src="https://img.shields.io/github/v/release/yuebo119/Flint"></a>
  <img alt="NativeAOT" src="https://img.shields.io/badge/deploy-NativeAOT%20single--file-green">
  <a href="#-快速开始"><img alt="platform" src="https://img.shields.io/badge/platform-windows%20%7C%20linux%20%7C%20macOS-blue"></a>
  <a href="LICENSE"><img alt="license" src="https://img.shields.io/badge/license-MIT-blue"></a>
</p>

---

Flint 是一个用 C# / .NET 10+ 实现的静态站点生成器。跨平台，NativeAOT 单文件交付。

---

**万页冷构建矩阵（14 引擎，2026-10-30，全引擎最新版）**

| 排名 | 引擎（版本）                 | 中位       | vs 最快   | 实现语言           |
| ----:| ---------------------------- | ----------:| ---------:| ------------------ |
| 1    | **Flint Fluid（0.3.0 AOT）** | **2717ms** | **1.00x** | C#（.NET 10）      |
| 2    | Flint Scriban（0.3.0 AOT）   | 3069ms     | 1.13x     | C#（.NET 10）      |
| 3    | Zola 0.23.6                  | 4405ms     | 1.62x     | Rust               |
| 4    | Hugo 0.169.0（extended）     | 5691ms     | 2.09x     | Go                 |
| 5    | Eleventy 3.1.7               | 10558ms    | 3.89x     | JavaScript（Node） |
| 6    | Jekyll 4.4.1                 | 17245ms    | 6.35x     | Ruby               |
| 7    | Pelican 4.12.0               | 18012ms    | 6.63x     | Python             |
| 8    | Hexo 8.1.2                   | 34566ms    | 12.72x    | JavaScript（Node） |
| 9    | VitePress 1.6.4              | 60687ms    | 22.34x    | TypeScript（Node） |
| 10   | Gatsby 5.16.1                | 62651ms    | 23.06x    | JavaScript（Node） |
| 11   | Astro 7.4.4                  | 68563ms    | 25.24x    | TypeScript（Node） |
| 12   | VuePress 2.0.0-rc.31         | 94209ms    | 34.67x    | TypeScript（Node） |
| 13   | Statiq 1.0.0（JIT，.NET 10） | 231107ms   | 85.06x    | C#（.NET 10）      |
| 14   | MkDocs 1.6.1                 | 252975ms   | 93.11x    | Python             |

统计范围：同内容语料，每臂 10,000 页，冷构建 3 轮中位（含进程启动），
Windows 10 x64 单窗口，按最快臂归一化，跨机器只比排名。

校验：产物对称（完整正文，无空壳），排名跨两窗口三轮复现。明细见
[benchmarks/REPORT.md](benchmarks/REPORT.md)。

---

## 目录

- [特性](#-特性)
- [快速开始](#-快速开始)
- [使用](#-使用)
- [配置与命令](#️-配置与命令)
- [性能](#-性能)
- [与主流 SSG 对比](#-与主流-ssg-对比)
- [案例站](#-案例站)
- [常见问题](#-常见问题)
- [开发](#️-开发)
- [贡献](#-贡献)
- [交流](#-交流)
- [许可证](#-许可证)

---

## ✨ 特性

**双模板引擎** —— Fluid（Liquid 语法）是新建站点主力引擎，Scriban 承接
Hugo 主题兼容；`engine = auto` 按模板扩展名自动路由，同一站点两种模板
并存，纯 Liquid 站点不加载 Scriban。

**构建性能** —— 万页冷构建 2.7 秒（Hugo 的 0.48 倍，14 款 SSG 同窗矩阵
最快），增量构建 64ms（500 页站、单文件变更、进程内套件）、由依赖图驱动
只重建受影响页面；`--cache` 持久内容缓存让重复构建总耗时再降 59%，产物
逐字节一致。

**Hugo 主题直接迁移** —— lookup order / 渲染钩子 / 短码 API / 命名空间
函数全对齐；ThemeMigrator 以 AST 级转换 Go template，无法判定的语义亮
`TODO-HUGO` 不静默错译，21 个真实主题已验证。

**无运行时单文件交付** —— .NET 10 NativeAOT：无运行时、无 node_modules、
无 JIT 预热，交付物是一个 exe；资产管线覆盖 CSS 压缩、轻量 TS 类型剥离、
图片处理与指纹化，CSS 压缩调用外部 lightningcss，缺省回退内置简化压缩。

**毫秒级冷启动** —— NativeAOT 无 JIT 预热与运行时装配，`flint version`
启动到输出中位 106ms（2026-10-28 实测，win-x64 单文件，10 次中位，区间
89-117ms）。

**Sass/SCSS 编译** —— 外部 Dart Sass CLI（`FLINT_SASS` 或 PATH 上的
`sass`）：支持 IncludePaths、压缩输出与内联 source map；构建内按内容
签名缓存，多页引用同一份 SCSS 只编译一次；Windows 中文输入走 UTF-8
临时文件，不因系统代码页乱码。

---

## 🚀 快速开始

### 环境要求

- .NET SDK 10.0.401 或更高（仅构建 Flint 本体时需要；产出的 exe 无任何运行时依赖）
- Git

### 安装

```bash
git clone https://github.com/yuebo119/Flint.git
cd Flint

# Windows（NativeAOT 单文件 ~41MB；缺 -p:PublishAot=true 会产出 ~89MB 非 AOT 单文件）
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

### 写模板：Fluid（Liquid，新站点默认）

`Flint new site` 默认生成 `.liquid` 脚手架。Liquid 语法，成员大小写不敏感
且支持 snake_case，默认不转义：

```html
{{ page.title }}                                <!-- 页面字段 -->
{{ site.params.author }}                        <!-- 站点配置 -->
{% for post in site.regular_pages %}{% endfor %} <!-- 循环 -->
{% if page.draft %}{% endif %}                   <!-- 条件 -->
{% include 'partials/header' %}                 <!-- 引入 partial（共享作用域）-->
{% render 'partials/card' %}                    <!-- 隔离作用域渲染 -->
```

骨架复用经 include 组合（Liquid 无 extends/block 概念，partial 即积木）。
完整写法（脚手架清单、函数清单、已知差异）见
[`docs/LIQUID-GUIDE.md`](docs/LIQUID-GUIDE.md)。

### 写模板：Scriban（Hugo 主题兼容）

Hugo 主题（`.html`）与迁移产物走 Scriban。同一站点里 `.liquid` 与 `.html`
可以并存（同根内 `.liquid` 优先）；配置 `engine: scriban` 切全站 Scriban。

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

`mod` 的身份是**目录名**（`themes/` 下的目录名，也就是配置里 `theme = "..."` 要写的值），
它不一定等于主题自述名：`flint mod get panr/hugo-theme-terminal` 装到
`themes/hugo-theme-terminal/`（自述名是 Terminal）。`mod list` 会同时打印自述名、
锁定的实际安装版本，以及两者不同时的目录名；`mod remove` 自述名与目录名都接受。

页面用哪个模板由**页面特征**决定——`layouts/posts/single.html` 只对
posts 生效，`layouts/blog/` 按 front matter `type` 路由：

| 页面 kind | 候选顺序（靠前优先）                                                                                                                                                                                                                           |
| --------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 普通页    | `{type}/{layout}` → `{type}/page` → `{type}/single` → `{section}/{layout}` → `{section}/page` → `{section}/single` → `{layout}` → `page` → `single` → `all`                                                                                    |
| 首页      | `{type}/{layout}` → `{type}/home` → `{type}/index` → `{type}/list` → `{layout}` → `home` → `index` → `list` → `all`                                                                                                                            |
| section   | `{type}/{layout}` → `{type}/section` → `{type}/list` → `{section}/{layout}` → `{section}/section` → `{section}/list` → `section/{layout}` → `section/section` → `section/list` → `{layout}` → `section` → `list` → `all`                       |
| taxonomy  | `{taxonomy}/{layout}` → `{taxonomy}/terms` → `{taxonomy}/taxonomy` → `{taxonomy}/list` → `taxonomy/{layout}` → `taxonomy/terms` → `taxonomy/taxonomy` → `taxonomy/list` → `_default/terms` → `_default/taxonomy` → `taxonomy` → `list` → `all` |
| term      | `{taxonomy}/{layout}` → `{taxonomy}/term` → `{taxonomy}/list` → `term/{layout}` → `term/term` → `term/list` → `taxonomy/{layout}` → `taxonomy/term` → `_default/taxonomy` → `_default/term` → `term` → `list` → `all`                          |

三条约定：① 裸名（无斜杠）自动展开为 `_default/{name}` 在前、`{name}`
在后两级；② 每级先查站点 `layouts/` 再查主题（同名站点赢）；③ `{type}`
级在未显式声明时按 section 名兜底，`page` 先于 `single`、`home` 先于
`index`——序均为 Hugo v0.166 逐级淘汰实测，完整链见
`src/Flint.Core/Templates/PageTemplateCandidates.cs`。

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
`GroupBy`/`Related`…）、20 个命名空间对象（`strings.*`/
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

| 命令                       | 说明                                     |
| -------------------------- | ---------------------------------------- |
| `flint new site <name>`    | 创建新站点                               |
| `flint new content <path>` | 创建内容（`--kind` 指定模板）            |
| `flint new theme <name>`   | 创建主题骨架                             |
| `flint build`              | 构建站点                                 |
| `flint serve`              | 启动开发服务器                           |
| `flint mod <sub>`          | 模块管理（init/get/update/list/remove）  |
| `flint deploy <target>`    | 部署（s3://、gh-pages、netlify、vercel） |

**gh-pages 部署的覆盖语义**：默认"能快进就推"，克隆失败时先用 `ls-remote` 区分
远端分支是否存在——存在（网络/权限问题）即中止，避免用孤儿分支覆盖远端历史。
确实需要重写远端历史时加 `--force` 显式声明。
| `flint version` | 显示版本信息 |

**构建选项**：`--source/-s`（默认 `.`）、`--output/-o`（默认取站点配置
`publishDir`，未配置则 `public`）、
`--minify/-m`、`--drafts/-D`、`--future/-F`、`--clean`、`--cache`
（持久内容缓存，重复构建跳过未变化文件的读盘）、`--verbose/-v`、
`--missing-layout`（`error`/`skip`，默认 error）。

**服务器选项**：`--port/-p`（1313）、`--host`（localhost）、`--open`（默认
true）、`--livereload/-l`（默认 true）、`--drafts/-D`（默认 true）、
`--cache`（与 build 同址同语义）、`--source/-s`（`.`）。

---

## 📊 性能

> 同机同窗实测（冷构建多轮中位）· Windows 10 x64 · Hugo
> vs Flint NativeAOT。跨窗口绝对值漂移大，只取同窗
> 比值。方法论见 [benchmarks/REPORT.md](benchmarks/REPORT.md)，复现
> 命令见[开发](#️-开发)一节。

### 端到端构建

| 语料                          | 页数   | Hugo   | **Flint Scriban**   | **Flint Fluid**     |
| ----------------------------- | ------ | ------:| -------------------:| -------------------:|
| 万页合成（SsgBench）          | 10,000 | 5179ms | **2909ms（0.56x）** | **2620ms（0.51x）** |
| MDN Web Docs（Hugo 迁移语料） | 14,621 | 8153ms | **4292ms（0.53x）** | —                   |

万页比值跨三晚复现于 0.50-0.56x 区间；MDN 真实语料 0.53x。Fluid 臂为
万页合成语料的同内容 `.liquid` 孪生站，产物与 Scriban 臂逐字节一致
（10,005 文件哈希全同）；MDN 为 Hugo 迁移语料（`.html` 模板），
无 Fluid 臂。

### 主题复杂度阶梯（1000 页 × L1/L2/L3）

| 层级    | 主题内容                                      | Hugo   | **Flint** | 比值      |
| ------- | --------------------------------------------- | ------ | --------- | --------- |
| L1 基础 | 单页渲染                                      | 698ms  | **437ms** | **0.63x** |
| L2 中等 | + 侧边栏 O(N) 全站循环                        | 983ms  | **593ms** | **0.60x** |
| L3 重度 | + 双 O(N) 循环 + 嵌套 partial + partialCached | 1265ms | **865ms** | **0.68x** |

### 内存峰值（构建进程，RSS / USS · MB）

| 语料          | Hugo        | **Flint**     | USS 差异         |
| ------------- | ----------- | ------------- | ---------------- |
| 万页合成      | 399 / 392   | **338 / 321** | **-18%（更省）** |
| MDN 14,621 页 | 1427 / 1402 | **893 / 872** | **-38%（更省）** |

### 进程内套件（Release，12/12 全绿）

Markdown 解析 229k 文件/秒 · 模板渲染 Scriban 54k / Fluid 175k 页/秒
（对拍比值 0.31x）· 增量构建 64ms（加速 3.4x）· 万页站内吞吐 5,244 页/秒
（0.19ms/页）· 配置加载 0.31ms · 并发加速比 2.13x · 病态检测随集成套件
1667 项（nightly 全量运行）。

### v0.3.0 性能变化（相对 v0.2.1 基线）

- Fluid 主力引擎上线：渲染吞吐为 Scriban 的 **3.2** 倍（进程内对拍 175k vs
  54k 页/秒，2026-10-27）。
- 持久内容缓存 `--cache`：14,622 页站重复构建总耗时 **-59%**，产物逐字节
  一致（2026-10-22 本地实测）。
- 增量构建按词条裁剪分类页：改 front matter 标签的增量构建 **16.5s →
  3.8s**（2026-10-27）；热重建跳过空操作目录创建，总时长再降约 7.7%。
- 同期 vs Hugo：万页比值 **0.81x → 0.56x**（Scriban 臂同口径；Fluid 臂
  0.51x），两窗各自同窗交错测量。

优化清单与量具见 `docs/PERFORMANCE-OPTIMIZATION.md` 与
`.ai/perf-ledger.md`。

---

## 🆚 与主流 SSG 对比

| 维度             | **Flint**                                                | Hugo                    | Astro                        | Eleventy                       | Jekyll                  | Hexo          |
| ---------------- | -------------------------------------------------------- | ----------------------- | ---------------------------- | ------------------------------ | ----------------------- | ------------- |
| 实现与运行时     | **C# / .NET 10+，AOT 单文件应用**                        | Go 单二进制             | Node + 依赖树                | Node + 依赖树                  | Ruby + gems             | Node + 依赖树 |
| 模板系统         | **Fluid/Liquid（新站点主力）+ Scriban（Hugo 主题兼容）** | Go template（刻意受限） | UI 组件（React/Vue/Svelte…） | 多引擎（Liquid/Nunjucks/EJS…） | Liquid                  | EJS / Pug 等  |
| 构建性能（万页） | **1.00x（最快，2.7s）**                                  | 2.09x                   | 25.24x                       | 3.89x                          | 6.35x                   | 12.72x        |
| 增量构建         | **64ms（500 页）**                                       | 支持                    | 支持                         | 支持                           | 有限                    | 支持          |
| 资产管线         | **Sass + TS 剥离 + minify + 指纹**                       | Hugo Pipes（Extended）  | Vite 工具链                  | 插件                           | 插件                    | 插件          |
| 图片处理         | **resize/fit/fill/crop，响应式**                         | 内置                    | sharp 生态                   | 插件                           | 插件                    | 插件          |
| 内容模型         | **三格式 Front Matter + taxonomy + 短码**                | 同类                    | Content Collections（typed） | data cascade                   | Front Matter + taxonomy | Front Matter  |
| 多语言           | **i18n 文件 + 主题回退**                                 | 完整 i18n               | i18n 路由                    | 社区方案                       | 插件为主                | 多语言        |
| 主题系统         | **模块化 + 版本锁定 + 多主题叠加**                       | Hugo Modules            | npm / 复制安装               | 无正式机制                     | gem 主题                | npm 主题      |
| 迁移工具         | **ThemeMigrator（AST 级主题转换）**                      | 文章导入（jekyll）      | —                            | —                              | —                       | —             |
| 质量保障         | **性能门禁 + 属性测试**                                  | —                       | —                            | —                              | —                       | —             |
| 控制台编码       | **强制 UTF-8**                                           | Windows 下 GBK          | UTF-8                        | UTF-8                          | UTF-8                   | UTF-8         |

> 构建性能行为 2026-10-30 十四引擎同窗矩阵实测（全部引擎当日最新稳定
> 版，比值按最快臂归一化，完整矩阵见文首表格；Docusaurus 因规模超线性
> 崩溃未入榜）；其余维度为公开结构性特征。Flint vs Hugo 的八个实测差异
> 点全记录见 [docs/FLINT-VS-HUGO.md](docs/FLINT-VS-HUGO.md)，主题兼容
> 矩阵见 [docs/HUGO-COMPAT-MATRIX.md](docs/HUGO-COMPAT-MATRIX.md)。

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
PATH 上的 `sass` → 仓库 `tools/dart-sass/` 顺序定位；产品不再内置
Dart Sass，装好 sass 或设 `FLINT_SASS` 指向可执行文件。

**fixit 主题构建很慢是正常的吗？**
冷构建正常——它在 100 篇统一语料 + 全分类/标签分页下约 5.5 分钟
（631 页），单站超时上限已放宽到 600s；其余主题均在秒级到分钟级。
增量构建不受此限：改 front matter 标签的热重建约 3.8 秒。

**重复构建还能更快吗？**
`build --cache` 开启持久内容缓存：未变化文件免读盘、未变化产物免写盘，
14.6k 页站重复构建总耗时再降 59%，产物逐字节一致；`--clean` 全量重建
时自动绕过缓存。

**该用 Fluid 还是 Scriban？**
新站点默认 Fluid（Liquid 语法，渲染吞吐约为 Scriban 的 3 倍）；
`engine = "auto"` 按模板扩展名自动路由（`.liquid` → Fluid，Hugo 迁移的
`.html` → Scriban），两种模板可同站并存。迁入 Hugo 主题走 Scriban
兼容层，ThemeMigrator 的产物即为此形态。

---

## 🛠️ 开发

### 技术栈

| 技术                                                 | 说明                                                                      |
| ---------------------------------------------------- | ------------------------------------------------------------------------- |
| **.NET 10+**                                         | LTS 基线及更新线，NativeAOT 原生编译，单文件发布                          |
| **Fluid 3.0.0-beta.8 + Scriban 7.5.3**               | 双模板引擎：Fluid（Liquid）主力（新站点默认），Scriban 作 Hugo 主题兼容层 |
| **Markdig 1.5.0**                                    | CommonMark + GFM 解析                                                     |
| **Dart Sass CLI**                                    | 外部 Sass 编译（`FLINT_SASS`/PATH 提供 `sass`）                           |
| **Lightning CSS**                                    | CSS 压缩（外部 CLI，缺失回退内置简单压缩）                                |
| **ImageSharp 3.1.12**                                | 图片处理（缩放/格式转换/响应式；许可锁定版）                              |
| **Tommy 3.1.2 / VYaml 1.5.0**                        | TOML 配置与 YAML Front Matter 解析                                        |
| **System.CommandLine 3.0.0-rc.3**                    | CLI 命令行框架                                                            |
| **Kestrel**                                          | 开发服务器（热重载）                                                      |
| **Server GC**                                        | 多核并行回收（批处理吞吐）                                                |
| **xunit.v3 4.0.2 / FsCheck 3.6.0 / BenchmarkDotNet** | 测试与基准（测试侧依赖，不入产品）                                        |

### 测试与基准

```bash
# 测试（五套件，根因定位与回归防护）
dotnet run --project tests/Flint.Core.Tests -c Release          # 核心（单测+属性测试）
dotnet run --project tests/Flint.IntegrationTests -c Release    # 集成（CLI 端到端）
dotnet run --project tests/Flint.ThemeMigrator.Tests -c Release # 主题迁移器
dotnet run --project tests/Flint.Tools.Tests -c Release         # DevTools/工具链
dotnet run --project tests/Flint.PerformanceTests -c Release    # 性能套件（进程内）

# 基准（Flint vs Hugo 对照；十四引擎矩阵协议与结果见 benchmarks/REPORT.md）
dotnet run --project src/Flint.DevTools -- bench ssg          # 万页冷构建
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
├── tests/                       # Core / Integration / ThemeMigrator / Tools / Performance 五套件
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

本项目基于 [MIT License](LICENSE) 开源。

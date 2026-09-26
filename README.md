# Flint 静态站点生成器

<p align="left">
  <strong>🔥 .NET 10 打造的高性能静态站点生成器 · NativeAOT 原生编译</strong>
</p>

---

## ✨ 特性

- **🚀 极速构建**：万页站点 3.5 秒、MDN 14.6k 页 7.1 秒（同窗单机自测：万页合成语料与 Hugo 打平，MDN 真实语料快约 24%，建议复测），增量 75ms，Markdown 解析 238k 文件/秒
- **🧠 内存可控**：万页构建内存峰值约 490MB，14.6k 页技术文档语料约 1420MB（单机自测数据，随语料与构建阶段波动）
- **📦 单文件部署**：~28MB AOT 原生 exe，无运行时依赖，Server GC 多核并行回收
- **🔄 全功能站点语义**：目录结构 permalink、Front Matter（YAML/TOML/JSON）、taxonomy、渲染钩子、partialCached、`:git` 日期源、环境变量覆盖
- **📝 现代内容管线**：CommonMark + GFM、语法高亮、数学公式、渲染钩子（链接/图片/标题）、短码
- **⚡ 开发体验**：Kestrel 热重载、增量构建、多格式配置、`--missing-layout` 宽容模式
- **🎨 Scriban 模板**：完整脚本语言（条件/循环/函数/继承），大列表迭代无千项上限
- **🛡️ 生产加固**：路径逃逸防护、缓存原子写、AOT 全链路验证、性能回归门禁（`dotnet run --project src/Flint.DevTools -- perf gate`）

---

## 🛠️ 技术栈

| 技术 | 说明 |
|------|------|
| **.NET 10** | 最新 LTS |
| **NativeAOT** | 原生编译，单文件发布 |
| **Scriban 7.5** | 模板引擎（LoopLimit 对齐 Hugo 无限制语义） |
| **Markdig 1.4** | CommonMark + GFM 解析 |
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

# Windows（NativeAOT 单文件，~28MB；缺 -p:PublishAot=true 会产出 ~89MB 非 AOT 单文件）
dotnet publish src/Flint.Cli -c Release -r win-x64 -p:PublishAot=true -o ./publish

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

## 📖 使用指南

### 内容编写

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

### 模板语法（Scriban）

```html
{{ page.title }}                    <!-- 页面字段 -->
{{ site.params.author }}            <!-- 站点配置 -->
{{ for post in site.regular_pages }}{{ end }}   <!-- 循环 -->
{{ if page.draft }}{{ end }}        <!-- 条件 -->
{{ include "partials/header" }}     <!-- partial -->
{{ partialcached "footer" "v1" }}   <!-- 缓存 partial：输出不依赖页面时用 -->

<!-- 模板继承：Scriban 无 extends/block，用 capture + 命名参数 include 组合 -->
{{ capture content }}
<article>{{ page.content }}</article>
{{ end }}
{{ include "baseof.html" content: content }}
```

> `layouts/_default/baseof.html` 里用 `{{ content }}` 占位接收上面捕获的块。

### HTML 转义契约（安全须知）

Flint **不自动转义**模板输出：Scriban 渲染配置为不启用 HTML 自动转义，
内容字段（`page.content`、`page.title` 等）与 Markdown 渲染产物直接写入
页面。这与 Hugo 的"默认按上下文转义 + safe 类型豁免"模型相反，安全责任
分配如下：

- Markdown 正文由 Markdig 管线产出（原始 HTML 由 `markup.goldmark.renderer.unsafe` 控制）；
- front matter 字段与用户配置值**原样输出**——不可信内容需模板作者自行 `html.escape`；
- `safe*` 系列函数（safeHTML/safeCSS/safeHTMLAttr 等）是恒等标记（对齐
  Hugo 语义），不做转义也不做豁免。

### 主题布局

`theme` 配置的主题（`flint mod get <repo>` 下载到 `themes/`）自动参与构建的
各层级回退，同名时站点覆盖主题：

- **模板**：站点 `layouts/` 优先，主题按序回退，include/partial 同规则；
  `partials/` 与 `_partials/`、`shortcodes/` 与 `_shortcodes/` 双形态支持（兼容 Hugo v0.146+ 新目录约定）
- **模板查找（页面感知）**：布局模板按**页面特征**逐级查找，对齐 Hugo lookup order——

  | 页面 kind | 候选顺序（靠前优先） |
  |---|---|
  | 普通页 | `{type}/{layout}` → `{type}/single` → `{section}/{layout}` → `{section}/single` → `{layout}` → `single` → `all` |
  | 首页 | `index` → `home` → `list` → `all`（`home.html` 为 v0.146+ 标准名） |
  | section | `{section}/section` → `{section}/list` → `section/section` → `section/list` → `list` → `all` |
  | taxonomy | `{taxonomy}/terms` → `{taxonomy}/taxonomy` → `{taxonomy}/list` → `terms` → `taxonomy` → `list` |
  | term | `{taxonomy}/term` → `{taxonomy}/taxonomy` → … → `term` → `taxonomy` → `list` |

  同一候选级内站点优先于主题，同根内根形态（`layouts/x.html`）优先于
  `_default` 形态（`layouts/_default/x.html`）；输出格式变体（`home.rss.html`）
  按 `.{format}` 后缀解析。这让 `layouts/posts/single.html` **只对 posts 下的
  页面生效**，`layouts/blog/` 按 front matter `type` 路由。
- **资源**：主题 `static/` 与 `assets/` 合并进输出，与站点同名资源时站点覆盖；
  `static/` 内容映射到输出根（`static/css/a.css` → `public/css/a.css`，对齐 Hugo）
- **内容**：主题 `content/` 合并（站点优先、主题补缺），主题示例内容可直接构建
- **参数**：主题 `config/_default/params.{toml,yaml,json}`（Hugo 标准形态）与
  `theme.toml` 的 `[params]` 段作为默认值，站点 `[params]` 深覆盖（嵌套表递归）
- **archetypes**：`new content` 模板查找站点优先、主题回退
- **短代码**：主题 `layouts/{_,}shortcodes/*.html` 注册（站点覆盖同名），
  上下文提供 Hugo 短码 API（`get`/`is_named_params`/`params`/`inner`）
- **render hooks**：主题 `_markup/render-*.html` 参与回退（站点优先）
- **数据**：主题 `data/` 合并进 `site.data`（站点覆盖同名键）
- **i18n**：主题 `i18n/` 参与回退（站点优先）

主题自带 `404.html` / `robots.txt` / `rss.xml` / `sitemap.xml` 模板时，
构建器以模板渲染替代内置生成。

模板可用 `{{ render "view" }}` 渲染内容视图（对齐 Hugo `.Render`，按页面
`.Path` 逐级查找 `<section>/<view>`，可传显式接收者 `{{ render "summary" post }}`）、
`{{ partial "func/x" }}` 取 partial 返回值（标量类型还原）、`{{ includeCached "x" }}`
缓存 partial、`{{ page.file.path }}` 访问 `.File.*` 方法族、
`{{ page.resources }}` 访问 bundle 资源、`{{ i18n "key" }}` 取翻译；
分类页提供 Hugo `.Data` 对象：`page.data.singular`/`plural`（单复数名）、
`page.data.terms`（含 `.Alphabetical`/`.ByCount` 视图）、`page.data.pages`
（taxonomy 列表页为词条对象，含 `.title`/`.rel_permalink`/`.pages`；term 页为内容页集合）；
内置函数含 Hugo 兼容的 `where`/`sortBy`/`after`/`in`/
`dict`/`merge`/`urlize`/`markdownify`/`plainify`/`emojify`/`humanize` 等。

### 命名空间函数（Hugo 0.146+ 形态）

Hugo 0.146 起官方文档改用命名空间形式。Flint 提供 **19 个命名空间对象**
（别名指向同一实现，非重复实现）：

| 命名空间 | 覆盖内容 |
|---|---|
| `strings.*` | ToUpper/ToLower/Trim/TrimPrefix/TrimSuffix/HasPrefix/HasSuffix/Split/Substr/Replace/ReplaceRE/Truncate/Chomp/CountWords/FindRE/FindRESubmatch/Diff/ReplacePairs… |
| `collections.*` | Where/Sort/Delimit/Dict/Slice/First/Last/In/Index/Merge/Union/Uniq/Reverse/Shuffle/Seq/KeyVals/SymDiff/Complement/Group/Apply… |
| `compare.*` | Default/Conditional/Eq/Ne/Gt/Ge/Lt/Le |
| `math.*` | Add/Sub/Mul/Div/Mod/ModBool/Max/Min/Pow/Sqrt/Round/Ceil/Floor/Log/Sum/Product/Rand/三角函数/ToDegrees/ToRadians/Pi/Counter/MaxInt64 |
| `cast.*` | ToInt/ToFloat/ToString |
| `crypto.*` / `hash.*` | MD5/SHA1/SHA256/HMAC/Hash、FNV32a/XxHash |
| `encoding.*` | Base64Encode/Decode、HexEncode/Decode、Jsonify |
| `transform.*` | Markdownify/Plainify/Emojify/HTMLEscape/HTMLUnescape/XMLEscape/Highlight/Unmarshal/Remarshal/HTMLToMarkdown… |
| `urls.*` / `path.*` | AbsURL/RelURL/Anchorize/URLize/Ref/RelRef/Parse/PathEscape、Base/Dir/Ext/Join/Split/Clean |
| `inflect.*` / `safe.*` | Humanize/Pluralize/Singularize、HTML/CSS/JS/JSStr/URL/HTMLAttr |
| `fmt.*` | Errorf/Warnf/Erroridf/Warnidf/Print/Printf/Println |
| `reflect.*` | IsMap/IsSlice/IsPage/IsResource/IsSite/IsImageResource* |
| `os.*` / `lang.*` / `debug.*` / `templates.*` | Getenv/ReadFile/FileExists/Stat、Translate/FormatNumber*、Dump/Timer、Exists/Current/Inner/Defer |
| **`hugo.*`** | Version/Environment/IsProduction/IsDevelopment/IsExtended/IsMultilingual/WorkingDir/Generator/Data/Store |
| **`resources.*`** | Get/GetMatch/Match/ByType/FromString/Concat/Minify/Fingerprint/Copy/Publish/ExecuteAsTemplate + Resize/Fit/Fill/Crop/Process |
| **`css.*` / `js.*` / `images.*`** | Build/Sass/PostCSS/TailwindCSS/Quoted/Unquoted、Build/Babel/Batch、Config |

**资源管线**：`resources.*` 从站点 `assets/`（优先）与主题 `assets/` 读取；
`Concat`/`FromString`/`Fingerprint` 的产物自动写入输出目录（`/assets/...`），
模板引用的链接不会 404。`Fingerprint` 提供 `Data.Integrity`（sha256 base64）。

### 页面对象扩展（Hugo 语义）

除基础字段外，页面还提供：

- **kind 谓词**：`is_home`/`is_page`/`is_section`/`is_node`/`is_branch`/`kind`
- **元数据**：`link_title`/`truncated`/`path`/`bundle_type`/`keywords`/
  `publish_date`/`expiry_date`/`aliases`/`plain_words`/`fuzzy_word_count`/`len`
- **`.Params` 兼容**：front matter 顶层字段（title/date/tags…）并入 `Params`，
  故 `.Params.Title` / `.Params.title` 均可用（Ananke 等主题依赖此行为）
- **`.Scratch` / `.Store`**：页面级暂存，`set`/`get`/`add`/`delete`/
  `setinmap`/`deleteinmap`/`getsortedmapvalues`/`values`
- **集合方法族**（Pages）：`ByDate`/`ByTitle`/`ByWeight`/`ByLength`/`ByLastmod`/
  `ByParam`/`Related`/`Reverse`/`Limit`/`GroupBy`/`GroupByDate`/`IndexOf`/`Next`/`Prev`
- **`.Related`**：按 Hugo 默认关键词算法打分（keywords 100 / date 100 / tags 80 /
  categories 80），得分 >0 入选并按分降序

### 内置 partial（Hugo embedded）

主题未提供但 Hugo 内置的 partial 由 Flint 兜底：`opengraph`、`schema`、
`twitter_cards`（最小等价元信息）、`google_analytics`/`disqus`（空实现）、
`pagination`。主题可放同名 partial 覆盖。

分页：列表页（home/section）按站点 `paginate`/`paginatePath` 切片，逐页产出
`{列表页}page/{N}/`（如 `posts/page/2/`）。每页绑定 `page.paginator` 与全局
`paginator`（`pages`/`page_number`/`total_pages`/`pager_size`/`has_prev`/
`has_next`/`url`/`first`/`last`/`prev`/`next`/`pagers`），`{{ include "pagination" }}`
命中内置分页导航模板（Hugo embedded default 格式等价）；站点或主题同名
`pagination.html` 可覆盖。

模块安装支持三种来源：`owner/repo`（GitHub Releases）、`owner/repo@分支或标签`
及 git URL（git clone）、本地目录路径（需含 theme.toml）；安装版本写入
`Flint.lock` 锁定。

多主题叠加：`theme = "t1,t2"`（前面的优先）——模板、资源、参数、archetypes
全链路按序回退，站点覆盖 t1、t1 覆盖 t2。

i18n：`i18n/<lang>.toml`（站点覆盖主题），模板 `{{ i18n "key" }}` 取翻译，
缺键返回空串（对齐 Hugo）；兼容 Hugo 的 `[key] other = "..."` 文件形态。

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

## 📖 命令参考

| 命令 | 说明 |
|------|------|
| `flint new site <name>` | 创建新站点 |
| `flint new content <path>` | 创建内容（`--kind` 指定模板） |
| `flint new theme <name>` | 创建主题骨架 |
| `flint build` | 构建站点 |
| `flint serve` | 启动开发服务器 |
| `flint mod <sub>` | 模块管理（init/get/update/list/remove） |
| `flint deploy <target>` | 部署站点（s3://、gh-pages、netlify、vercel） |
| `flint version` | 显示版本信息 |

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
export FLINT_PARAMS_AUTHOR="新作者"
```

### AOT 发布（生产推荐）

```bash
dotnet publish src/Flint.Cli -c Release -r win-x64 -p:PublishAot=true
```

---

## 📊 性能

> 测试环境：2026-09-25 安静窗口实测（机器负载 16%；杀软常驻不可关）· Windows 10 x64 · 32 核 · .NET SDK 10.0.401 / runtime 10.0.12 · 同机同语料同模板 · 冷构建 3 次中位数
> 对照：Hugo v0.165.0 Extended 官方二进制 vs Flint（构建点 `13d4b1e`，Release + NativeAOT `-p:PublishAot=true`，28MB 单文件）
> 完整方法论、产物对称性审计与公平性声明见 **[benchmarks/REPORT.md](benchmarks/REPORT.md)**（2026-09-08/09 历史三轮基线）；跨轮次比值解读见下文"与历史基线的关系"

### 端到端构建（双语料 × 双引擎 × 3 次中位数）

| 语料 | 页数（构建产出） | 内容形态 | Hugo | **Flint AOT** | 比值 |
|------|------|---------|------:|--------------:|:----:|
| 万页合成 | 10,007 | 同构 lorem | 4336ms | **3530ms** | **0.81x** |
| MDN Web Docs | 14,576 | 技术文档（HTML/代码密集） | 8515ms | **7086ms** | **0.83x** |

双语料均快于 Hugo（省时 17~19%）。Flint 万页三次离散 ±1%（3505-3540ms）；
MDN 双引擎首轮含冷缓存（Hugo 10774 / Flint 7752），中位数已剔除。

### 内存峰值（构建进程，psutil 采样，RSS / USS · MB）

| 语料 | Hugo RSS/USS | **Flint RSS/USS** | USS 差异 |
|------|-------------|-------------------|---------|
| 万页合成 | 440 / 415 | **530 / 514** | +24% |
| MDN 14,621 页 | 1531 / 1505 | **1494 / 1476** | **-2%（更省）** |

### 主题复杂度阶梯（1000 页 × L1/L2/L3 × 双语法等价实现）

| 层级 | 主题内容 | Hugo | **Flint** | 比值 |
| ---- | -------- | ---- | ----- | ---- |
| L1 基础 | 单页渲染 | 649ms | **488ms** | **0.75x** |
| L2 中等 | + 侧边栏 O(N) 全站循环 | 830ms | **746ms** | **0.90x** |
| L3 重度 | + 双 O(N) 循环 + 嵌套 partial + partialCached | 1266ms | **1112ms** | **0.88x** |

两引擎随复杂度近似线性变慢、斜率几乎相同（Hugo +309ms/级、Flint +312ms/级），
三层级 Flint 全部领先（省时 10~25%）。partialCached 双引擎均生效。
（分层峰值 RSS 为 09-24 轮值 102/271/407MB vs Hugo 88/134/168MB，安静轮未复采）

### 进程内套件（Release，11/11 全绿）

| 指标 | 数值 |
|------|------|
| Markdown 解析 | 4.2ms/千文件（238k 文件/秒） |
| 模板渲染 | 14.5ms/千页（69k 页/秒） |
| 增量构建 | 75ms（完整构建 214ms，加速 2.82x） |
| 配置加载 | 0.20ms |
| 并发构建加速比 | 1.12x（单线程 761ms → 多线程 681ms） |
| 病态检测 | 随集成套件全绿（1647/1647，2026-09-24）；发布轮 Core 1125 全绿 + 集成 Performance 47/47 |

### 与历史基线（REPORT.md）的关系

四点解读（2026-09-25 安静窗口轮）：

- **安静窗口比值即真实比值**：万页 0.81x、MDN 0.83x、复杂度 0.75~0.90x——
  双语料、全层级均快于 Hugo。09-24 负载日曾记录万页 0.73x、MDN 1.03x（落后），
  差异来自两引擎对负载的敏感度不同（同日漂移倍率：Hugo 6927/4336≈×1.6，
  Flint 5081/3530≈×1.4），跨轮比较以安静窗口为准。
- **写入相优化仍是最大单一矿脉**：段级缓存 + Win32 单段原语使写入相中位 -42%
  （4417 → 2576ms），万页绝对值由此进入 3.5 秒档（项目历史最佳）。
- **绝对值不可跨环境直比**：同一 Hugo 冻结二进制同日从 4530~5540ms 漂移到
  6927~7339ms（+30%，机器负载 25~48%）。门禁基线已于 09-25 环境换代重录
  （`scripts/perf-baseline.json`，复跑 9/9 PASS）；相对 09-08 epoch 的 3 项"稳定
  劣化"（增量 +55% / 复杂主题 +43.6% / 100 页 +31.7%）已经端点实验归因为
  **环境换代、无代码回归**——基线锚点 `e2cd026` 老代码在今日环境跑出
  124/569/135ms（环境税 ×2.5/+79%/+91%），而代码自 09-08 反而 -39%/-20%/-31%；
  详情记于该文件 `known_regressions`，保留为环境 epoch 记录。
- **未决项（不影响上述比值）**：AOT 产物的扫描/渲染相慢于同源码 JIT 对照
  （同窗交错实测：扫描 8x、渲染 +45%）。已排除 ILC 代际（10.0.4 复刻同慢）、
  运行时代际、文件系统、ServerGC、ILC Speed、脏 obj、SDK 11 共 8 项；
  剩余嫌疑为防护的进程级策略（腾讯电脑管家 RTP 标记 NOT_STOPPABLE，
  用户确认不可关闭，反向验证无法执行）——挂起待环境变化。

---

## 🧪 测试与基准

```bash
dotnet run --project tests/Flint.Core.Tests -c Release        # Core 全量测试（xunit v3 原生 runner）
dotnet run --project tests/Flint.IntegrationTests -c Release  # 集成测试
dotnet run --project tests/Flint.PerformanceTests -c Release   # 性能套件
python scripts/ssg-bench.py --pages 10000                # 万页 Hugo 对比
python scripts/complexity-bench.py           
powershell -File scripts/perf-gate.ps1                   # 性能回归门禁
```

📊 **完整性能报告**：[benchmarks/REPORT.md](benchmarks/REPORT.md)
（2026-09-08/09 历史轮次：语料矩阵、复杂度阶梯、内存峰值、公平性声明、产物审计、可复现命令）

---

## 📚 文档

- [使用指南](docs/USAGE.md)
- [API 文档](docs/API.md)
- [性能优化](docs/PERFORMANCE-OPTIMIZATION.md)
- [Hugo 主题兼容方案](docs/THEME-COMPAT-PLAN.md)

### Hugo 主题迁移

`src/Flint.ThemeMigrator <hugo-theme-dir> <flint-theme-dir>` 自动转换（C#，AST 级）
Go template 主题为 Scriban（变量映射/控制流/partial/range/with/日期格式），
无法静态确定的 Hugo 特有语义输出 `TODO-HUGO` 标记不静默错译。

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
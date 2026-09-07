# Flint 静态站点生成器

<p align="left">
  <strong>🔥 一款完全使用AI打造的 .NET 10 高性能静态站点生成器</strong>
</p>

<p  align="left">
单文件二进制 ~18MB | 无需运行时 | 自测吞吐量见下方表格（单机数据，建议用 <code>dotnet run --project tests/Flint.PerformanceTests -c Release</code> 在目标机器复测）
</p>

---

## 🛠️ 技术栈

| 技术                   | 说明                                  |
| -------------------- | ----------------------------------- |
| **.NET 10**          | 最新 LTS 版本，提供卓越性能和现代化 API            |
| **Native AOT**       | 原生编译，启动即运行，无需 JIT 预热                |
| **Scriban**          | 高性能模板引擎，语法简洁，AOT 友好                 |
| **Markdig**          | 快速 Markdown 解析器，支持 CommonMark + GFM |
| **Kestrel**          | ASP.NET Core 内置 Web 服务器，支持热重载       |
| **System.Text.Json** | 高性能 JSON 序列化，零分配优化                  |
| **Tomlyn**           | TOML 配置解析器，Hugo 配置兼容                |
| **YamlDotNet**       | YAML Front Matter 解析                |
| **LibSass**          | SCSS/Sass 编译支持                      |
| **NUglify**          | HTML/CSS/JS 压缩优化                    |

### 核心优化技术

- **Span\<T\> / Memory\<T\>** - 零分配字符串处理
- **ArrayPool\<T\>** - 内存池化，减少 GC 压力
- **ValueTask** - 异步操作优化
- **并行处理** - 多核 CPU 充分利用
- **增量构建** - 智能缓存，仅重建变更内容
- **懒加载** - 按需解析，减少内存占用

---

## ✨ 特性

- **🚀 极速构建**: 1000+ 页/秒，复杂主题 1285 页/秒
- **📦 小巧体积**: 单文件二进制 ~18MB，无需安装运行时
- **🔄 Hugo 兼容**: 支持 Hugo 目录结构、Front Matter 格式
- **📝 现代化内容**: Markdown (CommonMark + GFM)、代码高亮、数学公式
- **⚡ 开发友好**: 内置 Kestrel 开发服务器、热重载、增量构建 (5.1x 加速)
- **🎨 灵活模板**: Scriban 模板引擎，支持模板继承和 Partial

---

## 📈 性能指标

以下为开发机（单机、单次运行）的自测数据，不同硬件/内容下会有差异：

| 测试项目           | 构建速度     | 每页耗时   |
| -------------- | -------- | ------ |
| 小型站点 (100页)    | 828 页/秒  | 1.21ms |
| 中型站点 (500页)    | 951 页/秒  | 1.05ms |
| 大型站点 (1000页)   | 1082 页/秒 | 0.92ms |
| 超大型站点 (10000页) | 1105 页/秒 | 0.90ms |
| 复杂主题 (1000页)   | 1285 页/秒 | 0.78ms |

| 其他指标         | 数值           |
| ------------ | ------------ |
| 增量构建加速       | 5.1x         |
| Markdown 解析  | 245,791 文件/秒 |
| 模板渲染         | 55,331 页/秒   |
| 可扩展性         | O(n) 线性      |

> ⚠️ 关于与 Hugo 的对比：本项目仓库不包含同机可复现的 Hugo 对比基准，
> 此前文档中 "比 Hugo 快 2.5-5.0x" 的说法基于社区流传的 Hugo 性能数据估算，不应作为选型依据。

📊 **[查看完整性能测试报告](performance-report.html)**（生成物，可用上述命令重新生成）

---

## 🚀 快速开始

### 安装

```bash
# 克隆仓库
git clone https://github.com/yuebo119/flint.git
cd flint/Flint

# 构建 Native AOT 版本 (Windows)
dotnet publish src/Flint.Cli -c Release -r win-x64 -o ./publish

# Linux
dotnet publish src/Flint.Cli -c Release -r linux-x64 -o ./publish

# macOS (Apple Silicon)
dotnet publish src/Flint.Cli -c Release -r osx-arm64 -o ./publish
```

### 添加到 PATH

```powershell
# Windows PowerShell
$env:Path += ";$PWD\publish"

# 或永久添加 (管理员权限)
[Environment]::SetEnvironmentVariable("Path", $env:Path + ";$PWD\publish", "User")
```

```bash
# Linux/macOS
sudo cp publish/flint /usr/local/bin/
```

### 验证安装

```bash
flint version
```

---

## � 使用方法

### 1. 创建新站点

```bash
flint new site my-blog
cd my-blog
```

这会创建以下目录结构：

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

### 2. 创建内容

```bash
# 创建博客文章
flint new content posts/hello-world.md

# 创建页面
flint new content about.md

# 使用指定模板
flint new content posts/tutorial.md --kind tutorial
```

创建的文件会自动包含 Front Matter：

```yaml
---
title: "Hello World"
date: 2026-02-06T20:00:00+08:00
draft: true
---

在这里写你的内容...
```

### 3. 启动开发服务器

```bash
flint serve
```

- 默认地址: `http://localhost:1313`
- 自动打开浏览器
- 热重载：修改文件后自动刷新
- 包含草稿内容

```bash
# 自定义端口
flint serve --port 8080

# 不自动打开浏览器
flint serve --open false

# 详细输出
flint serve --verbose
```

### 4. 构建站点

```bash
# 基本构建
flint build

# 包含草稿
flint build --drafts

# 压缩输出 (HTML/CSS/JS)
flint build --minify

# 清理后构建
flint build --clean

# 组合使用
flint build --clean --minify --drafts
```

输出到 `public/` 目录，可直接部署到任意静态托管服务。

### 5. 部署

将 `public/` 目录部署到：

- **GitHub Pages**: 推送到 `gh-pages` 分支
- **Netlify**: 连接仓库，设置构建命令 `flint build`
- **Vercel**: 连接仓库，设置输出目录 `public`
- **Cloudflare Pages**: 连接仓库，设置构建命令
- **任意 Web 服务器**: 直接上传 `public/` 目录

---

## 📖 命令参考

| 命令                         | 说明      | 示例                                 |
| -------------------------- | ------- | ---------------------------------- |
| `flint new site <name>`    | 创建新站点   | `flint new site my-blog`           |
| `flint new content <path>` | 创建新内容   | `flint new content posts/hello.md` |
| `flint new theme <name>`   | 创建新主题   | `flint new theme my-theme`         |
| `flint build`              | 构建站点    | `flint build --minify`             |
| `flint serve`              | 启动开发服务器 | `flint serve --port 8080`          |
| `flint deploy <target>`    | 部署站点    | `flint deploy gh-pages --dry-run`  |
| `flint mod <sub>`          | 模块管理    | `flint mod get github.com/o/r`     |
| `flint version`            | 显示版本信息  | `flint version`                    |

> `deploy` 支持 `s3://<bucket>`、`gh-pages`、`netlify`、`vercel` 四种目标（依赖对应 CLI 已安装），
> `--dry-run` 模拟执行不实际上传；gh-pages 部署使用 `--force` 推送，请注意目标分支数据。
> `mod` 子命令：`init`（初始化模块配置）/ `get <url>`（安装）/ `update`（更新）/ `list`（列表）/ `remove`（移除）。

### 构建选项

| 选项          | 简写   | 默认值      | 说明             |
| ----------- | ---- | -------- | -------------- |
| `--source`  | `-s` | `.`      | 源目录            |
| `--output`  | `-o` | `public` | 输出目录           |
| `--minify`  | `-m` | false    | 压缩 HTML/CSS/JS |
| `--drafts`  | `-D` | false    | 包含草稿内容         |
| `--future`  | `-F` | false    | 包含未来日期的内容      |
| `--clean`   | -    | false    | 构建前清理输出目录      |
| `--verbose` | `-v` | false    | 详细输出           |

### 服务器选项

| 选项             | 简写   | 默认值    | 说明      |
| -------------- | ---- | ------ | ------- |
| `--port`       | `-p` | `1313` | 服务器端口   |
| `--host`       | -    | `localhost` | 绑定主机地址 |
| `--open`       | -    | true   | 自动打开浏览器（`-o` 统一保留给 build `--output`） |
| `--livereload` | `-l` | true   | 启用热重载   |
| `--drafts`     | `-D` | true   | 包含草稿内容  |

---

## ⚙️ 配置文件

支持 TOML、YAML、JSON 三种格式。

```toml
# flint.toml
baseURL = "https://example.com/"
title = "我的博客"
languageCode = "zh-cn"
theme = "my-theme"

# 站点时区（IANA 名称）：Front Matter 中无偏移的日期按此时区解释；
# 带显式偏移（如 +08:00）的日期不受影响。缺省时使用本机时区
timeZone = "Asia/Shanghai"

# 启用 Git 信息后，页面可用 `date: ":git"` 特殊日期源取最后提交时间
# enableGitInfo = true

[params]
  author = "作者名"
  description = "站点描述"
  keywords = ["博客", "技术"]

[menu]
  [[menu.main]]
    name = "首页"
    url = "/"
    weight = 1
  [[menu.main]]
    name = "文章"
    url = "/posts/"
    weight = 2
  [[menu.main]]
    name = "关于"
    url = "/about/"
    weight = 3

[taxonomies]
  tag = "tags"
  category = "categories"
```

### 环境变量覆盖

```bash
export FLINT_BASEURL="https://staging.example.com/"
export FLINT_PARAMS_AUTHOR="新作者"
```

---

## 📝 内容编写

### Front Matter

支持 YAML (---), TOML (+++), JSON ({}) 三种格式：

```yaml
---
title: "文章标题"
date: 2026-02-06T10:00:00+08:00
draft: false
tags: ["Go", "Hugo"]
categories: ["技术"]
author: "作者名"
description: "文章摘要"
---

文章内容...
```

#### 日期特殊源（对齐 Hugo）

`date`/`lastmod` 可使用字符串特殊源，解析期兑现为实际时间：

| 源 | 含义 | 前提 |
|---|------|------|
| `:git` | 文件最后一次提交的修改时间 | `enableGitInfo = true`；非 git 仓库时静默缺省 |
| `:filemodtime` | 文件修改时间 | 无 |
| `:filename` | 文件名 `YYYY-MM-DD-` 前缀 | 未显式设置 date/slug 时自动生效 |

```yaml
---
title: "文章标题"
lastmod: ":git"        # 每次构建取 git 最后提交时间
date: ":filemodtime"
---
```

无偏移的日期值按站点 `timeZone` 解释；带显式偏移的日期保持原样。

#### 渲染钩子（对齐 Hugo render hooks）

`layouts/_markup/` 下的钩子模板可定制链接/图片/标题的 HTML 输出：

| 模板 | 拦截对象 | 可用变量 |
|---|---|---|
| `render-link.html` | 链接（含自动链接） | `destination`、`title`、`text`（子内容 HTML）、`plain_text` |
| `render-image.html` | 图片（优先于 render-link） | 同上 |
| `render-heading.html` | 标题 | `level`、`id`（自动锚点）、`text`、`plain_text` |

```html
<!-- layouts/_markup/render-link.html -->
<a class="ext" href="{{ destination }}" rel="noopener">{{ plain_text }}</a>
```

无对应模板时走默认渲染，零开销。注意：钩子与全管道自动切换暂不并存（钩子站点使用默认管道）。

### Markdown 语法

支持 CommonMark + GFM 扩展：

- 标题、段落、列表
- 代码块（带语法高亮）
- 表格、任务列表
- 自动链接、删除线
- 数学公式 (`$...$` 和 `$$...$$`)

---

## 🎨 模板语法

Flint 使用 Scriban 模板引擎：

```html
<!-- 变量访问 -->
{{ page.title }}
{{ site.params.author }}

<!-- 条件判断 -->
{{ if page.draft }}
  <span class="badge">草稿</span>
{{ end }}

<!-- 循环遍历 -->
{{ for post in site.regular_pages }}
  <article>
    <h2><a href="{{ post.permalink }}">{{ post.title }}</a></h2>
  </article>
{{ end }}

<!-- 模板继承 -->
{{ extends "_default/baseof.html" }}
{{ block "main" }}...{{ end }}

<!-- Partial 引用 -->
{{ include "partials/header.html" }}

<!-- 结果缓存 partial（对齐 Hugo partialCached）：输出只依赖 name+variants，
     依赖页面内容的 partial 请用 include；partial 内经 variants[0] 访问点参数 -->
{{ partialcached "partials/expensive-list" "variant-key" }}
```

### 主题布局

`theme` 配置的主题（`flint mod get <repo>` 下载到 `themes/`）其 `layouts/`
自动作为模板回退目录：站点 `layouts/` 优先，主题按序回退（同名文件站点
覆盖主题），include/partial 同规则。

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

## 🧪 测试

```bash
# 运行所有测试
dotnet test

# 运行单元测试
dotnet test tests/Flint.Core.Tests

# 运行集成测试
dotnet test tests/Flint.IntegrationTests

# 运行性能测试
dotnet run --project tests/Flint.PerformanceTests -c Release
```

性能测试会生成 HTML 报告：**[performance-report.html](performance-report.html)**

---

## 📚 文档

- **[使用指南](docs/USAGE.md)** - 详细使用说明
- **[API 文档](docs/API.md)** - 核心库 API 参考
- **[性能优化](docs/PERFORMANCE-OPTIMIZATION.md)** - 性能优化方案
- **[性能报告](performance-report.html)** - 最新性能测试结果

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
│   ├── Flint.Core.Tests/        # 单元测试
│   ├── Flint.IntegrationTests/  # 集成测试
│   └── Flint.PerformanceTests/  # 性能测试
└── docs/                    # 文档
```

---

## 📄 许可证

MIT License

---

<p align="center">
  <sub>使用 ❤️ 和 .NET 10 构建</sub>
</p>

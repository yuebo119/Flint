# Flint 使用指南

## 目录

1. [安装](#安装)
2. [创建站点](#创建站点)
3. [目录结构](#目录结构)
4. [配置文件](#配置文件)
5. [内容编写](#内容编写)
6. [模板语法](#模板语法)
7. [命令参考](#命令参考)
8. [开发服务器](#开发服务器)
9. [构建部署](#构建部署)

---

## 安装

### 从源码构建

```bash
# 克隆仓库
git clone https://github.com/your-org/Flint.git
cd Flint/Flint

# 构建 Native AOT 版本 (Windows)
dotnet publish src/Flint.Cli -c Release -r win-x64 -o ./publish

# Linux
dotnet publish src/Flint.Cli -c Release -r linux-x64 -o ./publish

# macOS (Apple Silicon)
dotnet publish src/Flint.Cli -c Release -r osx-arm64 -o ./publish
```

### 添加到 PATH

```bash
# Windows PowerShell (管理员)
$env:Path += ";$PWD\publish"

# Linux/macOS
sudo cp publish/Flint /usr/local/bin/
```

### 验证安装

```bash
Flint version
```

输出示例:
```
Flint v0.1.0
运行时: .NET 10.0.0
操作系统: Microsoft Windows 10.0.22631
架构: X64
```

---

## 创建站点

### 创建新站点

```bash
Flint new site my-blog
cd my-blog
```

这会创建以下目录结构：

```
my-blog/
├── archetypes/
│   └── default.md
├── content/
├── layouts/
│   └── _default/
│       ├── baseof.html
│       ├── list.html
│       └── single.html
├── static/
└── Flint.toml
```

### 创建新内容

```bash
# 创建文章
Flint new content posts/hello-world.md

# 使用指定模板
Flint new content posts/tutorial.md --kind tutorial
```

---

## 目录结构

```
my-site/
├── archetypes/          # 内容模板 (archetype)
│   └── default.md       # 默认模板
├── assets/              # 需要处理的资源
│   ├── scss/            # SCSS/Sass 文件
│   └── js/              # JavaScript 文件
├── content/             # Markdown 内容
│   ├── posts/           # 博客文章
│   ├── pages/           # 独立页面
│   └── _index.md        # 首页内容
├── data/                # 数据文件 (YAML/TOML/JSON)
├── layouts/             # 模板文件
│   ├── _default/        # 默认模板
│   │   ├── baseof.html  # 基础模板
│   │   ├── list.html    # 列表模板
│   │   └── single.html  # 单页模板
│   └── partials/        # 部分模板
├── static/              # 静态文件 (直接复制)
├── themes/              # 主题目录
└── Flint.toml          # 站点配置
```

### 目录说明

| 目录 | 说明 |
|------|------|
| `archetypes/` | 内容模板，用于 `Flint new content` 命令 |
| `content/` | Markdown 内容文件 |
| `layouts/` | HTML 模板文件 |
| `static/` | 静态资源，直接复制到输出目录 |
| `data/` | 数据文件，可在模板中访问 |
| `themes/` | 主题目录 |

---

## 配置文件

支持 TOML、YAML、JSON 三种格式，自动检测 `Flint.toml`、`Flint.yaml`、`Flint.json`。

### 基本配置

```toml
# Flint.toml
baseURL = "https://example.com/"
title = "我的博客"
languageCode = "zh-cn"
theme = "my-theme"
```

### 站点参数

```toml
[params]
  author = "作者名"
  description = "站点描述"
  keywords = ["博客", "技术"]
```

### 菜单配置

```toml
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
```

### 分类系统

```toml
[taxonomies]
  tag = "tags"
  category = "categories"
```

### 输出格式

```toml
[outputs]
  home = ["HTML", "RSS", "JSON"]
  section = ["HTML", "RSS"]
  page = ["HTML"]
```

### 环境变量覆盖

配置项可通过环境变量覆盖，格式: `Flint_<KEY>`

```bash
export Flint_BASEURL="https://staging.example.com/"
export Flint_PARAMS_AUTHOR="新作者"
```

---

## 内容编写

### Front Matter

支持 YAML (---), TOML (+++), JSON ({}) 三种格式。

```yaml
---
title: "文章标题"
date: 2026-02-03T10:00:00+08:00
draft: false
tags: ["Go", "Hugo"]
categories: ["技术"]
author: "作者名"
description: "文章摘要"
weight: 10
---

文章内容...
```

### 常用字段

| 字段 | 类型 | 说明 |
|------|------|------|
| `title` | string | 标题 |
| `date` | datetime | 发布日期 |
| `draft` | bool | 是否为草稿 |
| `tags` | []string | 标签列表 |
| `categories` | []string | 分类列表 |
| `author` | string | 作者 |
| `description` | string | 摘要/描述 |
| `weight` | int | 排序权重 |
| `slug` | string | URL 别名 |
| `layout` | string | 指定模板 |

### Markdown 语法

Flint 支持 CommonMark + GFM 扩展：

- 标题、段落、列表
- 代码块（带语法高亮）
- 表格
- 任务列表
- 自动链接
- 删除线

---

## 模板语法

Flint 使用 Scriban 模板引擎。

### 变量访问

```html
<!-- 页面变量 -->
{{ page.title }}
{{ page.date }}
{{ page.content }}

<!-- 站点变量 -->
{{ site.title }}
{{ site.base_url }}
{{ site.params.author }}
```

### 条件判断

```html
{{ if page.draft }}
  <span class="badge">草稿</span>
{{ end }}

{{ if page.tags && page.tags.size > 0 }}
  <div class="tags">
    {{ for tag in page.tags }}
      <a href="/tags/{{ tag | string.downcase }}/">{{ tag }}</a>
    {{ end }}
  </div>
{{ end }}
```

### 循环遍历

```html
{{ for post in site.regular_pages }}
  <article>
    <h2><a href="{{ post.permalink }}">{{ post.title }}</a></h2>
    <time>{{ post.date | date.to_string "%Y-%m-%d" }}</time>
  </article>
{{ end }}
```

### 模板继承

```html
<!-- layouts/_default/baseof.html -->
<!DOCTYPE html>
<html>
<head>
  <title>{{ block "title" }}{{ site.title }}{{ end }}</title>
</head>
<body>
  {{ include "partials/header.html" }}
  <main>
    {{ block "main" }}{{ end }}
  </main>
  {{ include "partials/footer.html" }}
</body>
</html>
```

```html
<!-- layouts/_default/single.html -->
{{ extends "_default/baseof.html" }}

{{ block "title" }}{{ page.title }} | {{ site.title }}{{ end }}

{{ block "main" }}
  <article>
    <h1>{{ page.title }}</h1>
    {{ page.content }}
  </article>
{{ end }}
```

### Partial 模板

```html
<!-- 简单引用 -->
{{ include "partials/header.html" }}

<!-- 传递参数 -->
{{ include "partials/card.html" item: post }}
```

---

## 命令参考

### Flint new - 创建新项目

```bash
Flint new site <name>              # 创建新站点
Flint new content <path>           # 创建新内容
Flint new content <path> --kind <archetype>  # 使用指定模板
```

### Flint build - 构建站点

```bash
Flint build [选项]
```

| 选项 | 简写 | 默认值 | 说明 |
|------|------|--------|------|
| `--source` | `-s` | `.` | 源目录 |
| `--output` | `-o` | `public` | 输出目录 |
| `--minify` | `-m` | false | 压缩 HTML/CSS/JS |
| `--drafts` | `-D` | false | 包含草稿内容 |
| `--future` | `-F` | false | 包含未来日期的内容 |
| `--clean` | - | false | 构建前清理输出目录 |
| `--verbose` | `-v` | false | 详细输出 |

### Flint serve - 开发服务器

```bash
Flint serve [选项]
```

| 选项 | 简写 | 默认值 | 说明 |
|------|------|--------|------|
| `--source` | `-s` | `.` | 源目录 |
| `--port` | `-p` | `1313` | 服务器端口 |
| `--open` | `-o` | true | 自动打开浏览器 |
| `--livereload` | `-l` | true | 启用热重载 |
| `--drafts` | `-D` | true | 包含草稿内容 |
| `--verbose` | `-v` | false | 详细输出 |

### Flint version - 版本信息

```bash
Flint version
```

---

## 开发服务器

### 启动服务器

```bash
Flint serve
```

默认在 `http://localhost:1313` 启动，自动打开浏览器。

### 热重载

修改内容或模板文件后，浏览器会自动刷新。

### 自定义端口

```bash
Flint serve --port 8080
```

---

## 构建部署

### 构建生产版本

```bash
Flint build
```

输出到 `public/` 目录。

### 构建选项

```bash
# 压缩输出
Flint build --minify

# 包含草稿
Flint build --drafts

# 清理后构建
Flint build --clean

# 组合使用
Flint build --clean --minify
```

### 部署

将 `public/` 目录部署到任意静态托管服务：

- GitHub Pages
- Netlify
- Vercel
- Cloudflare Pages
- 任意 Web 服务器

---

*文档更新日期：2026-02-06*

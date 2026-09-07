# Flint Static Site Generator

<p align="center">
  <strong>🔥 A High-Performance Static Site Generator Built Entirely with AI using .NET 10</strong>
</p>

<p align="center">
  <strong>2.5-5.0x</strong> faster than Hugo complex themes | Single binary ~18MB | No runtime required
</p>

---

## 🛠️ Tech Stack

| Technology | Description |
|------------|-------------|
| **.NET 10** | Latest LTS version with excellent performance and modern APIs |
| **Native AOT** | Ahead-of-time compilation, instant startup, no JIT warmup |
| **Scriban** | High-performance template engine, clean syntax, AOT-friendly |
| **Markdig** | Fast Markdown parser supporting CommonMark + GFM |
| **Kestrel** | ASP.NET Core built-in web server with hot reload support |
| **System.Text.Json** | High-performance JSON serialization with zero-allocation optimization |
| **Tomlyn** | TOML config parser, Hugo configuration compatible |
| **YamlDotNet** | YAML Front Matter parsing |
| **LibSass** | SCSS/Sass compilation support |
| **NUglify** | HTML/CSS/JS minification |

### Core Optimization Techniques

- **Span\<T\> / Memory\<T\>** - Zero-allocation string processing
- **ArrayPool\<T\>** - Memory pooling to reduce GC pressure
- **ValueTask** - Async operation optimization
- **Parallel Processing** - Full utilization of multi-core CPUs
- **Incremental Build** - Smart caching, rebuild only changed content
- **Lazy Loading** - On-demand parsing to reduce memory footprint

---

## ✨ Features

- **🚀 Blazing Fast**: 1000+ pages/sec, complex themes 1285 pages/sec
- **📦 Compact Size**: Single binary ~18MB, no runtime installation required
- **🔄 Hugo Compatible**: Supports Hugo directory structure and Front Matter formats
- **📝 Modern Content**: Markdown (CommonMark + GFM), syntax highlighting, math formulas
- **⚡ Developer Friendly**: Built-in Kestrel dev server, hot reload, incremental build (5.1x speedup)
- **🎨 Flexible Templates**: Scriban template engine with inheritance and partials

---

## 📈 Performance Benchmarks

| Test Case | Build Speed | Time per Page |
|-----------|-------------|---------------|
| Small Site (100 pages) | 828 pages/sec | 1.21ms |
| Medium Site (500 pages) | 951 pages/sec | 1.05ms |
| Large Site (1000 pages) | 1082 pages/sec | 0.92ms |
| Extra Large Site (10000 pages) | 1105 pages/sec | 0.90ms |
| Complex Theme (1000 pages) | 1285 pages/sec | 0.78ms |

| Other Metrics | Value |
|---------------|-------|
| Incremental Build Speedup | 5.1x |
| Markdown Parsing | 245,791 files/sec |
| Template Rendering | 55,331 pages/sec |
| Scalability | O(n) Linear |
| vs Hugo Complex Themes | **2.5-5.0x** |

📊 **[View Full Performance Report](performance-report.html)**

---

## 🚀 Quick Start

### Installation

```bash
# Clone the repository
git clone https://github.com/your-org/flint.git
cd flint/Flint

# Build Native AOT version (Windows)
dotnet publish src/Flint.Cli -c Release -r win-x64 -o ./publish

# Linux
dotnet publish src/Flint.Cli -c Release -r linux-x64 -o ./publish

# macOS (Apple Silicon)
dotnet publish src/Flint.Cli -c Release -r osx-arm64 -o ./publish
```

### Add to PATH

```powershell
# Windows PowerShell
$env:Path += ";$PWD\publish"

# Or add permanently (requires admin)
[Environment]::SetEnvironmentVariable("Path", $env:Path + ";$PWD\publish", "User")
```

```bash
# Linux/macOS
sudo cp publish/flint /usr/local/bin/
```

### Verify Installation

```bash
flint version
```

---

## 📖 Usage

### 1. Create a New Site

```bash
flint new site my-blog
cd my-blog
```

This creates the following directory structure:

```
my-blog/
├── archetypes/          # Content templates
│   └── default.md
├── content/             # Markdown content
├── layouts/             # Template files
│   └── _default/
│       ├── baseof.html  # Base template
│       ├── list.html    # List template
│       └── single.html  # Single page template
├── static/              # Static files
└── flint.toml           # Site configuration
```

### 2. Create Content

```bash
# Create a blog post
flint new content posts/hello-world.md

# Create a page
flint new content about.md

# Use a specific archetype
flint new content posts/tutorial.md --kind tutorial
```

Created files automatically include Front Matter:

```yaml
---
title: "Hello World"
date: 2026-02-06T20:00:00+08:00
draft: true
---

Write your content here...
```

### 3. Start Development Server

```bash
flint serve
```

- Default address: `http://localhost:1313`
- Auto-opens browser
- Hot reload: auto-refresh on file changes
- Includes draft content

```bash
# Custom port
flint serve --port 8080

# Don't auto-open browser
flint serve --open false

# Verbose output
flint serve --verbose
```

### 4. Build Site

```bash
# Basic build
flint build

# Include drafts
flint build --drafts

# Minify output (HTML/CSS/JS)
flint build --minify

# Clean before build
flint build --clean

# Combined options
flint build --clean --minify --drafts
```

Output goes to `public/` directory, ready for deployment to any static hosting service.

### 5. Deploy

Deploy the `public/` directory to:

- **GitHub Pages**: Push to `gh-pages` branch
- **Netlify**: Connect repo, set build command to `flint build`
- **Vercel**: Connect repo, set output directory to `public`
- **Cloudflare Pages**: Connect repo, set build command
- **Any Web Server**: Upload `public/` directory directly

---

## 📖 Command Reference

| Command | Description | Example |
|---------|-------------|---------|
| `flint new site <name>` | Create new site | `flint new site my-blog` |
| `flint new content <path>` | Create new content | `flint new content posts/hello.md` |
| `flint new theme <name>` | Create new theme | `flint new theme my-theme` |
| `flint build` | Build site | `flint build --minify` |
| `flint serve` | Start dev server | `flint serve --port 8080` |
| `flint version` | Show version info | `flint version` |

### Build Options

| Option | Short | Default | Description |
|--------|-------|---------|-------------|
| `--source` | `-s` | `.` | Source directory |
| `--output` | `-o` | `public` | Output directory |
| `--minify` | `-m` | false | Minify HTML/CSS/JS |
| `--drafts` | `-D` | false | Include draft content |
| `--future` | `-F` | false | Include future-dated content |
| `--clean` | - | false | Clean output before build |
| `--verbose` | `-v` | false | Verbose output |

### Server Options

| Option | Short | Default | Description |
|--------|-------|---------|-------------|
| `--port` | `-p` | `1313` | Server port |
| `--open` | `-o` | true | Auto-open browser |
| `--livereload` | `-l` | true | Enable hot reload |
| `--drafts` | `-D` | true | Include draft content |

---

## ⚙️ Configuration

Supports TOML, YAML, and JSON formats.

```toml
# flint.toml
baseURL = "https://example.com/"
title = "My Blog"
languageCode = "en-us"
theme = "my-theme"

[params]
  author = "Author Name"
  description = "Site description"
  keywords = ["blog", "tech"]

[menu]
  [[menu.main]]
    name = "Home"
    url = "/"
    weight = 1
  [[menu.main]]
    name = "Posts"
    url = "/posts/"
    weight = 2
  [[menu.main]]
    name = "About"
    url = "/about/"
    weight = 3

[taxonomies]
  tag = "tags"
  category = "categories"
```

### Environment Variable Overrides

```bash
export FLINT_BASEURL="https://staging.example.com/"
export FLINT_PARAMS_AUTHOR="New Author"
```

---

## 📝 Content Writing

### Front Matter

Supports YAML (---), TOML (+++), and JSON ({}) formats:

```yaml
---
title: "Article Title"
date: 2026-02-06T10:00:00+08:00
draft: false
tags: ["Go", "Hugo"]
categories: ["Tech"]
author: "Author Name"
description: "Article summary"
---

Article content...
```

#### Special Date Sources (Hugo-compatible)

`date`/`lastmod` accept string special sources, resolved to actual times at parse time:

| Source | Meaning | Prerequisite |
|---|---------|--------------|
| `:git` | Last git commit time of the file | `enableGitInfo = true`; silently absent outside a git repo |
| `:filemodtime` | File modification time | None |
| `:filename` | `YYYY-MM-DD-` filename prefix | Applies automatically when date/slug are not set |

Dates without an explicit offset are interpreted in the site `timeZone`; dates with an explicit offset are kept as written.

### Markdown Syntax

Supports CommonMark + GFM extensions:

- Headings, paragraphs, lists
- Code blocks (with syntax highlighting)
- Tables, task lists
- Auto-links, strikethrough
- Math formulas (`$...$` and `$$...$$`)

---

## 🎨 Template Syntax

Flint uses the Scriban template engine:

```html
<!-- Variable access -->
{{ page.title }}
{{ site.params.author }}

<!-- Conditionals -->
{{ if page.draft }}
  <span class="badge">Draft</span>
{{ end }}

<!-- Loops -->
{{ for post in site.regular_pages }}
  <article>
    <h2><a href="{{ post.permalink }}">{{ post.title }}</a></h2>
  </article>
{{ end }}

<!-- Template inheritance -->
{{ extends "_default/baseof.html" }}
{{ block "main" }}...{{ end }}

<!-- Partial includes -->
{{ include "partials/header.html" }}
```

---

## 🧪 Testing

```bash
# Run all tests
dotnet test

# Run unit tests
dotnet test tests/Flint.Core.Tests

# Run integration tests
dotnet test tests/Flint.IntegrationTests

# Run performance tests
dotnet run --project tests/Flint.PerformanceTests -c Release
```

Performance tests generate an HTML report: **[performance-report.html](performance-report.html)**

---

## 📚 Documentation

- **[Usage Guide](docs/USAGE.md)** - Detailed usage instructions
- **[API Documentation](docs/API.md)** - Core library API reference
- **[Performance Optimization](docs/PERFORMANCE-OPTIMIZATION.md)** - Performance optimization guide
- **[Performance Report](performance-report.html)** - Latest performance test results

---

## 🏗️ Project Structure

```
Flint/
├── src/
│   ├── Flint.Cli/           # Command-line tool
│   └── Flint.Core/          # Core library
│       ├── Abstractions/    # Interface definitions
│       ├── Assets/          # Asset processing
│       ├── Configuration/   # Configuration loading
│       ├── Content/         # Content parsing
│       ├── Models/          # Data models
│       ├── Server/          # Development server
│       ├── Site/            # Site building
│       └── Templates/       # Template rendering
├── tests/
│   ├── Flint.Core.Tests/        # Unit tests
│   ├── Flint.IntegrationTests/  # Integration tests
│   └── Flint.PerformanceTests/  # Performance tests
└── docs/                    # Documentation
```

---

## 📄 License

MIT License

---

<p align="center">
  <sub>Built with ❤️ and .NET 10</sub>
</p>

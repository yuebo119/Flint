# Flint

<p align="center">
  <strong>🔥 High-performance static site generator · C# / .NET 10 · NativeAOT single file</strong>
</p>

<p align="center">
  English | <a href="README.md">中文</a>
</p>

<p align="center">
  <img alt="license" src="https://img.shields.io/badge/license-MIT-blue">
  <img alt=".NET" src="https://img.shields.io/badge/.NET-10-purple">
  <img alt="NativeAOT" src="https://img.shields.io/badge/deploy-NativeAOT%20single--file-green">
  <img alt="themes" src="https://img.shields.io/badge/themes-21%20verified-orange">
</p>

Flint is a static site generator built in C# / .NET 10: 10k-page sites build
in 3.5 seconds, incrementals take 75ms, the deliverable is a native single
file — and it ships an AST-level automated migration toolchain for
Hugo themes.

| 10k-page build | Incremental | Deliverable | Real-corpus memory |
|:---:|:---:|:---:|:---:|
| **3.5s** (0.81x Hugo) | **75ms** | **28MB** single file | on par with Hugo (-2%) |

---

## Table of Contents

- [Highlights](#-highlights)
- [Quick Start](#-quick-start)
- [Demo Sites](#-demo-sites)
- [Usage](#-usage)
- [Configuration and Commands](#️-configuration-and-commands)
- [Best Practices](#-best-practices)
- [Performance](#-performance)
- [SSG Comparison](#-ssg-comparison)
- [FAQ](#-faq)
- [Roadmap](#️-roadmap)
- [Development](#️-development)
- [Contributing](#-contributing)
- [License](#-license)

---

## ✨ Highlights

**Build throughput and predictability** — 10k pages in 3.5s (0.81x Hugo),
75ms incrementals, ±1% dispersion over three cold builds, lower load-drift
sensitivity than the reference (×1.4 vs ×1.6). Incrementals are
dependency-graph driven: editing one article rebuilds only the affected
pages.

**Zero-dependency native deployment** — a .NET 10 NativeAOT single-file
app: no runtime, no node_modules, no JIT warmup. The deliverable is one exe.

**The Hugo compatibility layer** — lookup order / render hooks / shortcode
API / 25 namespace functions all aligned; ThemeMigrator converts Go
templates at the AST level, marking indeterminable semantics with
`TODO-HUGO` instead of silently mistranslating — 21 real themes verified.
Migration drops from "rewrite" to "one command plus a few adjudications".

**In-process asset pipeline** — Sass compiles in-process (DartSassHost
native libs); TypeScript type stripping is homegrown (unrecognized syntax
passes through untouched — a browser error beats silent corruption); image
processing results cached on disk. Heavy TS/SCSS themes build with zero npm
dependencies.

**Engineering-grade quality assurance** — the `perf gate` regression gate
(three-run median ratchet, degradation intercepted before merge); FsCheck
property testing, integration suite 1647/1647 green; output path-escape
protection and atomic cache-entry writes.

Under the hood: a three-phase parallel pipeline (parse/render/write +
Server GC), content-signature transform caching (executed once, reused
site-wide), Win32 single-segment writes (write-phase median -42%), and a
Scriban semantic bridge (lenient comparison / collection normalization —
real themes render unchanged).

---

## 🚀 Quick Start

### Prerequisites

- .NET SDK 10.0.401 or later (only for building Flint itself; the published
  exe has zero runtime dependencies)
- Git

### Installation

```bash
git clone https://github.com/yuebo119/Flint.git
cd Flint

# Windows (NativeAOT single file ~28MB; omitting -p:PublishAot=true yields ~89MB non-AOT)
dotnet publish src/Flint.Cli -c Release -r win-x64 -p:PublishAot=true -o ./publish

# Linux / macOS (Apple Silicon)
dotnet publish src/Flint.Cli -c Release -r linux-x64 -o ./publish
dotnet publish src/Flint.Cli -c Release -r osx-arm64 -o ./publish
```

Put `flint` on your PATH (otherwise the shell can't find it):

```bash
# Windows (PowerShell, persisted to the user PATH)
[Environment]::SetEnvironmentVariable("Path", $env:Path + ";$PWD\publish", "User")

# Linux / macOS
export PATH="$PWD/publish:$PATH"        # current session
sudo ln -s "$PWD/publish/flint" /usr/local/bin/flint   # or a one-time symlink
```

Verify: `flint version`.

### A site in five minutes

```bash
flint new site my-blog
cd my-blog
flint new content posts/hello-world.md
flint serve        # http://localhost:1313, hot reload
flint build --minify   # Outputs to public/, deploy anywhere
```

### Deploy

Push `public/` to GitHub Pages / Netlify / Vercel / Cloudflare Pages or any
web server; or `flint deploy <target>` (s3://, gh-pages, netlify, vercel —
requires the corresponding CLI).

---

## 🎨 Demo Sites

The repo ships 21 Hugo-migrated theme demo sites (built from a shared
100-article Chinese corpus) plus a theme gallery (card grid of preview
screenshots, each linking to its demo site):

```bash
# Prerequisites: build the engine and migrator (DevTools looks them up at fixed paths)
dotnet build src/Flint.Cli -c Release -r win-x64
dotnet build src/Flint.ThemeMigrator

dotnet run --project src/Flint.DevTools -- demo build --serve   # all themes + start servers
dotnet run --project src/Flint.DevTools -- demo build narrow    # a single theme
dotnet run --project src/Flint.DevTools -- demo gallery --serve # the gallery (port 8400)
dotnet run --project src/Flint.DevTools -- demo stop            # stop servers
```

Gallery at `http://127.0.0.1:8400/`, themes at 8401-8421. Caveats (Sass
resolution, fixit build time, directory locks) in
[demo-sites/README.md](demo-sites/README.md).

---

## 📖 Usage

> Template and config semantics are highly Hugo-compatible, so Hugo sites
> and themes migrate at low cost; the sections below describe usage from
> Flint's own perspective, with real differences from Hugo called out
> individually.

### Writing Content: Markdown + Metadata

An article is just a `.md` file: Markdown body, preceded by a metadata block
(Front Matter) declaring title, date, tags — YAML / TOML / JSON, your pick:

```yaml
---
title: "Article Title"
date: 2026-09-09T10:00:00+08:00
lastmod: ":git"              # from the last git commit
tags: ["Go", "Hugo"]
draft: false
---

Body text (CommonMark + GFM: code highlighting, tables, task lists, math…)…
```

Dates can be inferred: `:git` (needs `enableGitInfo = true`),
`:filemodtime`, `:filename` (`YYYY-MM-DD-` prefix of the filename; applies
when date is unset).

**Render hooks**: to change the default HTML of links/images/headings, drop
a same-named template into `layouts/_markup/` (`render-link.html` /
`render-image.html` / `render-heading.html`; variables `destination`/`text`/
`level`/`id` etc.); without one, default rendering applies at zero cost.

### Writing Templates: Scriban

Templates decide what each page looks like; grab data inside double braces:

```html
{{ page.title }}                                <!-- page fields -->
{{ site.params.author }}                        <!-- site config -->
{{ for post in site.regular_pages }}{{ end }}   <!-- loops -->
{{ if page.draft }}{{ end }}                    <!-- conditionals -->
{{ include "partials/header" }}                 <!-- include a partial -->
{{ partialcached "footer" "v1" }}               <!-- cached partial (page-independent output) -->
```

Reuse skeletons with capture + include (Scriban has no extends/block):

```html
{{ capture content }}
<article>{{ page.content }}</article>
{{ end }}
{{ include "baseof.html" content: content }}    <!-- baseof receives it via {{ content }} -->
```

### Security Notes: The HTML Escaping Contract

Flint does **not** auto-escape template output — `page.title`,
`page.content` go into the page as-is. For a site you wrote yourself that's
usually exactly right; when content comes from **untrusted sources**
(visitor submissions, external imports), escape explicitly:
`{{ transform.HTMLEscape page.title }}`. The `safe*` functions are identity
markers, not protection; raw HTML inside Markdown bodies is controlled by
`markup.goldmark.renderer.unsafe`.

### Using Themes: Your Files Always Win

A theme is a full set of templates + styles + sample content, installed to
`themes/` with `flint mod get <repo>`. **The one rule: on name conflicts
your files always win; the theme only fills gaps.**

Which template a page uses is decided by **page characteristics** —
`layouts/posts/single.html` applies only to posts, `layouts/blog/` routes
by front matter `type`:

| Page kind | Candidate order (earlier wins) |
|---|---|
| Regular page | `{type}/{layout}` → `{type}/single` → `{section}/{layout}` → `{section}/single` → `{layout}` → `single` → `all` |
| Home | `index` → `home` → `list` → `all` |
| Section | `{section}/section` → `{section}/list` → `section/section` → `section/list` → `list` → `all` |
| Taxonomy | `{taxonomy}/terms` → `{taxonomy}/taxonomy` → `{taxonomy}/list` → `terms` → `taxonomy` → `list` |
| Term | `{taxonomy}/term` → `{taxonomy}/taxonomy` → … → `term` → `taxonomy` → `list` |

Theme directories at build time: `layouts/` (incl. shortcodes and render
hooks) falls back in order with the site overriding; `static/` and `assets/`
merge into the output (`static/css/a.css` → `public/css/a.css`); `content/`
site first, theme fills gaps; `config/_default/params.*` and `theme.toml
[params]` act as defaults with the site deep-merging over;
`archetypes/`, `data/`, `i18n/` follow the same fallback rule. A theme's
own `404.html`/`robots.txt`/`rss.xml`/`sitemap.xml` replaces the built-in
generators.

Template capability quick tour: `{{ render "view" }}` content views,
`{{ partial "func/x" }}` return values, `{{ includeCached "x" }}` caching,
`{{ page.file.path }}` / `{{ page.resources }}` / `{{ i18n "key" }}`,
taxonomy pages' `page.data.*`. The full semantic surface — kind predicates,
`.Scratch`/`.Store`, Pages methods (`ByDate`/`GroupBy`/`Related`…), 25
namespace objects (`strings.*`/`collections.*`/`resources.*`…), built-in
fallback partials (`opengraph`…).

Multi-theme stacking: `theme = "t1,t2"` (earlier wins), site > t1 > t2
across the whole chain; i18n via `i18n/<lang>.toml`, `{{ i18n "key" }}`
fetches translations, missing keys return an empty string.

> Fuller usage details in [docs/USAGE.md](docs/USAGE.md); the API surface in
> [docs/API.md](docs/API.md); performance optimization notes in
> [docs/PERFORMANCE-OPTIMIZATION.md](docs/PERFORMANCE-OPTIMIZATION.md);
> the theme compatibility plan in
> [docs/THEME-COMPAT-PLAN.md](docs/THEME-COMPAT-PLAN.md).

---

## ⚙️ Configuration and Commands

Config supports TOML / YAML / JSON, compatible with Hugo config forms:

```toml
baseURL = "https://example.com/"
title = "My Blog"
languageCode = "en-us"
timeZone = "Asia/Shanghai"          # offset-less dates interpret in this zone
enableGitInfo = true                # enables date: ":git"

[params]
  author = "Author Name"

[menu]
  [[menu.main]]
    name = "Posts"
    url = "/posts/"
    weight = 2

[taxonomies]
  tag = "tags"
  category = "categories"
```

Env-var overrides (`FLINT_` prefix, `__` for nesting): `FLINT_BASEURL`,
`FLINT_PARAMS_AUTHOR`.

### Command Reference

| Command | Description |
|------|------|
| `flint new site <name>` | Create a new site |
| `flint new content <path>` | Create content (`--kind` selects the archetype) |
| `flint new theme <name>` | Create a theme skeleton |
| `flint build` | Build the site |
| `flint serve` | Start the dev server |
| `flint mod <sub>` | Module management (init/get/update/list/remove) |
| `flint deploy <target>` | Deploy (s3://, gh-pages, netlify, vercel) |
| `flint version` | Show version info |

**Build options**: `--source/-s` (default `.`), `--output/-o` (`public`),
`--minify/-m`, `--drafts/-D`, `--future/-F`, `--clean`, `--verbose/-v`,
`--missing-layout` (`error`/`skip`, default error).

**Server options**: `--port/-p` (1313), `--host` (localhost), `--open`
(default true), `--livereload/-l` (default true), `--drafts/-D` (default
true), `--source/-s` (`.`).

---

## 📐 Best Practices

Squeeze every drop out of Flint's speed, caching, and tooling — each
practice maps to a real engine strength.

1. **Eat site-wide loops with partialCached** — sidebar/nav loops whose
   output doesn't depend on the page, one line cached site-wide
   (`{{ partialcached "partials/sidebar" "site-wide" }}`); page-dependent
   partials use `include`. Complexity-ladder measurements show site-wide
   O(N) loops are the dominant template cost
2. **Stay on incremental builds' good side** — organize content by section;
   pair `:git` with `enableGitInfo = true`; set `timeZone` explicitly to
   prevent cross-machine sort drift; use `--clean` only for true full
   rebuilds
3. **The production trio** — `dotnet publish -p:PublishAot=true` (native
   single-file app) + `flint build --minify` + asset fingerprinting:
   ```scriban
   {{ $css = resources.Get "css/main.css" | resources.Minify | resources.Fingerprint }}
   <link rel="stylesheet" href="{{ $css.RelPermalink }}" integrity="{{ $css.Data.Integrity }}">
   ```
   Sass results are content-signature cached within a build: reference one
   SCSS from many pages, it compiles once
4. **Let ThemeMigrator do the heavy lifting** — ① `dotnet run --project
   src/Flint.ThemeMigrator -- <hugo-dir> <flint-dir>` AST-level
   conversion, then search `TODO-HUGO` and adjudicate each; ②
   `flint build --missing-layout skip` to get running first (remove keys
   Hugo 0.145 dropped, e.g. `_build`); ③ `theme matrix <theme>` +
   `audit assets/elements` comparing dual-side artifacts; ④
   `demo build <theme> --serve` preview under the shared corpus. Flint
   ships 12 built-in shortcodes; theme-private ones migrate with the theme
5. **Escape untrusted content explicitly** — see the escaping contract above
6. **Multi-environment via env vars** — one config across dev/staging/prod;
   inject env vars in CI to switch
7. **Wire perf gate into CI** — baseline in `scripts/perf-baseline.json`;
   re-record after optimizations to tighten the bar. Flint's ±1% dispersion
   lets you set thresholds tight

---

## 📊 Performance

> Same-machine, same-corpus, same-window measurements (2026-09-25 quiet
> window, median of 3 cold builds) · Windows 10 x64 · 32 cores · .NET SDK
> 10.0.401 · Hugo v0.165.0 Extended official binary vs Flint
> (Release + NativeAOT single file). Full methodology and fairness
> notes in [benchmarks/REPORT.md](benchmarks/REPORT.md); reproduction
> commands under [Development](#️-development).

### End-to-End Builds

| Corpus | Pages | Hugo | **Flint AOT** | Ratio |
|------|------|-----:|--------------:|:----:|
| 10k synthetic | 10,007 | 4336ms | **3530ms** | **0.81x** |
| MDN Web Docs | 14,576 | 8515ms | **7086ms** | **0.83x** |

The bigger the scale, the wider the lead — and it's consistent: 10k-page
runs disperse within ±1% (3505–3540ms); quiet-to-loaded window drift is
×1.6 for Hugo vs ×1.4 for Flint.

### Theme Complexity Ladder (1000 pages × L1/L2/L3)

| Level | Theme content | Hugo | **Flint** | Ratio |
| ---- | -------- | ---- | ----- | ---- |
| L1 basic | single-page rendering | 649ms | **488ms** | **0.75x** |
| L2 medium | + sidebar O(N) site-wide loop | 830ms | **746ms** | **0.90x** |
| L3 heavy | + two O(N) loops + nested partials + partialCached | 1266ms | **1112ms** | **0.88x** |

### Peak Memory (build process, RSS / USS · MB)

| Corpus | Hugo | **Flint** | USS delta |
|------|------|-----------|---------|
| 10k synthetic | 440 / 415 | **530 / 514** | +24% |
| MDN 14,621 pages | 1531 / 1505 | **1494 / 1476** | **-2% (less)** |

### In-Process Suite (Release, 11/11 green)

Markdown parsing 238k files/sec · template rendering 69k pages/sec ·
incremental 75ms (full build 214ms, 2.82x speedup) · config load 0.20ms ·
concurrency speedup 1.12x · pathological detection green with the
integration suite (1647/1647).

---

## 🆚 SSG Comparison

| Dimension | **Flint** | Hugo | Astro | Eleventy | Jekyll | Hexo |
|---|---|---|---|---|---|---|
| Runtime | **C# / .NET 10, AOT single-file app** | Go single binary | Node + dep tree | Node + dep tree | Ruby + gems | Node + dep tree |
| Template system | **Scriban (full scripting language)** | Go template (deliberately restricted) | UI components (React/Vue/Svelte…) | Multi-engine (Liquid/Nunjucks/EJS…) | Liquid | EJS / Pug etc. |
| Build performance (10k pages) | **3.5s (same-machine measured)** | 4.3s (same-machine measured) | not measured | not measured | not measured | not measured |
| Incremental builds | **75ms** | supported | supported | supported | limited | supported |
| Asset pipeline | **in-process Sass + TS type stripping + minify + fingerprint** | Hugo Pipes (Extended) | Vite toolchain | plugins | plugins | plugins |
| Image processing | **resize/fit/fill/crop, responsive** | built-in | sharp ecosystem | plugins | plugins | plugins |
| Content model | **three-format Front Matter + taxonomies + shortcodes** | comparable | Content Collections (typed) | data cascade | Front Matter + taxonomies | Front Matter |
| Multilingual | **i18n files + theme fallback** | full i18n | i18n routing | community solutions | mostly plugins | multilingual |
| Theme system | **modular + version pinning + multi-theme stacking** | Hugo Modules | npm / copy install | no formal mechanism | gem themes | npm themes |
| Migration tooling | **ThemeMigrator (AST-level theme conversion)** | post import (jekyll) | — | — | — | — |
| Quality assurance | **perf gate + property testing** | — | — | — | — | — |
| Console encoding | **forced UTF-8** | GBK on Windows | UTF-8 | UTF-8 | UTF-8 | UTF-8 |

> Only Flint vs Hugo was measured on the same machine, corpus, and window;
> the other engines were not benchmarked in this repo — their columns state
> public structural facts. The eight measured Flint-vs-Hugo difference
> points are documented in
> [docs/FLINT-VS-HUGO.md](docs/FLINT-VS-HUGO.md); the theme compatibility
> matrix in [docs/HUGO-COMPAT-MATRIX.md](docs/HUGO-COMPAT-MATRIX.md).

---

## ❓ FAQ

**Garbled output in the Windows console?**
Flint forces UTF-8 end to end; the Windows console defaults to a GBK code
page — run `chcp 65001` to switch to UTF-8.

**`flint serve` or demo-site port already in use?**
`dotnet run --project src/Flint.DevTools -- demo stop` clears demo-site
servers; for the dev server use `--port`, or find the PID with
`netstat -ano | grep :1313` and end the process (on Windows a server whose
working directory sits inside `public/` locks the directory — stop it
before rebuilding).

**SCSS theme built without compiled styles?**
The template-side `toCSS` path invokes the external Dart Sass CLI, located
in this order: `FLINT_SASS` env var → `dart-sass*/` next to Flint.exe →
`sass` on PATH → repo `tools/dart-sass/`. Install sass or point `FLINT_SASS`
at the executable.

**Is the fixit theme supposed to build slowly?**
Yes — under the 100-article shared corpus with full taxonomy/tag pagination
it takes ~5.5 minutes (631 pages); the per-site timeout is already raised to
600s. All other themes build in seconds to low minutes.

---

## 🗺️ Roadmap

- Continue converging the Hugo compatibility layer: gap list and priorities
  in [docs/HUGO-GAP-TASKS.md](docs/HUGO-GAP-TASKS.md); theme compatibility
  matrix in [docs/HUGO-COMPAT-MATRIX.md](docs/HUGO-COMPAT-MATRIX.md)
- Grow theme coverage beyond the current 21 verified themes (candidate pool
  in `theme-migrator/candidates/`)
- Build out a standalone shortcode ecosystem (theme-private shortcodes
  currently ride along via theme migration)

---

## 🛠️ Development

### Tech Stack

| Technology | Role |
|------|------|
| **.NET 10** | Latest LTS; NativeAOT compilation, single-file publish |
| **Scriban 7.5** | Template engine (no iteration cap on large lists) |
| **Markdig 1.4** | CommonMark + GFM parsing |
| **DartSassHost** | In-process Sass compilation (native libs for three platforms; no external CLI) |
| **NUglify** | HTML/CSS/JS minification (`--minify`) |
| **ImageSharp 3.1** | Image processing (resize/format conversion/responsive) |
| **Tomlyn / YamlDotNet** | TOML config and YAML Front Matter parsing |
| **System.CommandLine** | CLI framework |
| **Kestrel** | Dev server (hot reload) |
| **Server GC** | Multi-core parallel collection (batch throughput) |
| **xunit.v3 / FsCheck / BenchmarkDotNet** | Testing and benchmarking (test-side only; not in the product) |

### Tests and Benchmarks

```bash
dotnet run --project tests/Flint.Core.Tests -c Release        # Full Core tests
dotnet run --project tests/Flint.IntegrationTests -c Release  # Integration tests
dotnet run --project tests/Flint.PerformanceTests -c Release  # Performance suite

dotnet run --project src/Flint.DevTools -- bench ssg          # 10k-page Flint vs Hugo
dotnet run --project src/Flint.DevTools -- bench complexity   # Complexity ladder
dotnet run --project src/Flint.DevTools -- bench memory       # Peak memory sampling
dotnet run --project src/Flint.DevTools -- perf run           # Perf suite + HTML report
dotnet run --project src/Flint.DevTools -- perf gate          # Regression gate
```

### Project Structure

```
Flint/
├── src/
│   ├── Flint.Cli/               # CLI (product entry, AOT publish surface)
│   ├── Flint.Core/              # Core library (Abstractions/Assets/Configuration/
│   │                            #   Content/Models/IO/Modules/Server/Site/Templates)
│   ├── Flint.AiGate/            # AI dev-workflow gate tooling
│   ├── Flint.DevTools/          # Dev-ops toolset (bench/audit/corpus/release/perf/theme/demo)
│   └── Flint.ThemeMigrator/     # Hugo theme migration (Go template → Scriban)
├── tests/                       # Core / Integration / Performance suites
├── demo-sites/                  # Demo sites (21 themes + gallery + shared corpus)
├── theme-migrator/              # Migration corpus (themes/ clones, candidates/)
└── benchmarks/                  # Performance benchmarks (corpora/reports/gate)
```

---

## 🤝 Contributing

Issues and PRs are welcome. Please run the relevant test suites before
submitting; build-performance changes should come with before/after
`perf gate` comparisons (three-run medians). Hugo-compatibility bugs should
include the theme name and `demo build` reproduction steps.

---

## 📄 License

MIT License

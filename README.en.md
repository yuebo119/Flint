<h1 align="center">Flint</h1>

<p align="center">
  <strong>🔥 High-performance static site generator · C# / .NET 10+ · NativeAOT single file</strong>
</p>

<p align="center">
  English | <a href="README.md">中文</a>
</p>

<p align="center">
  <a href="https://github.com/yuebo119/Flint/releases/latest"><img alt="release" src="https://img.shields.io/github/v/release/yuebo119/Flint"></a>
  <img alt="NativeAOT" src="https://img.shields.io/badge/deploy-NativeAOT%20single--file-green">
  <a href="#-quick-start"><img alt="platform" src="https://img.shields.io/badge/platform-windows%20%7C%20linux%20%7C%20macOS-blue"></a>
  <a href="LICENSE"><img alt="license" src="https://img.shields.io/badge/license-MIT-blue"></a>
</p>

---

Flint is a static site generator built with C# / .NET 10+. Cross-platform,
delivered as a NativeAOT single file.

---

**10k-page cold-build matrix (11 engines, 2026-10-31, full-protocol run)**

| Rank | Engine (version) | 10k cold-build median | vs fastest | Language |
| --: | --- | ---: | ---: | --- |
| 1 | **Flint Fluid (0.3.0 AOT)** | **2572ms** | **1.00x** | C# (.NET 10) |
| 2 | Flint Scriban (0.3.0 AOT) | 2901ms | 1.13x | C# (.NET 10) |
| 3 | Zola 0.23.6 | 4690ms | 1.82x | Rust |
| 4 | Hugo 0.169.0 (extended) | 5217ms | 2.03x | Go |
| 5 | Eleventy 3.1.7 | 11553ms | 4.49x | JavaScript (Node) |
| 6 | MkDocs 1.6.1 (pruned nav) | 13638ms | 5.30x | Python |
| 7 | Jekyll 4.4.1 | 18220ms | 7.08x | Ruby |
| 8 | Hexo 8.1.2 | 28196ms | 10.96x | JavaScript (Node) |
| 9 | VitePress 1.6.4 | 71747ms | 27.90x | TypeScript (Node) |
| 10 | Gatsby 5.16.1 | 75194ms | 29.24x | JavaScript (Node) |
| 11 | Astro 7.4.4 | 75651ms | 29.42x | TypeScript (Node) |

Scope: identical 10,000-page corpus per arm, cold builds, median of 3
rounds including process startup, single-window run on Windows 10 x64,
ratios normalized to the fastest arm; across machines only the ranking
holds.

Checks: pre-run gate (13% CPU), per-round load recorded, every arm passed
the "freshly generated" and body-sample checks. Per-round details in
[benchmarks/REPORT.md](benchmarks/REPORT.md).

Notes: MkDocs runs with a pruned nav (`nav` lists only the home page; all
10k pages are still rendered); VitePress enables `metaChunk` and disables
preload (output drops from 3.6GB to 62MB). The three long-tail engines
(Pelican, VuePress, Statiq) are not part of this round.

---

## Table of Contents

- [Features](#-features)
- [Quick Start](#-quick-start)
- [Usage](#-usage)
- [Configuration and Commands](#️-configuration-and-commands)
- [Performance](#-performance)
- [SSG Comparison](#-ssg-comparison)
- [Demo Sites](#-demo-sites)
- [FAQ](#-faq)
- [Development](#️-development)
- [Contributing](#-contributing)
- [License](#-license)

---

## ✨ Features

**Dual template engines** — Fluid (Liquid syntax) powers new sites; Scriban
carries Hugo theme compatibility. `engine = auto` routes by template
extension, both can coexist in one site, and pure-Liquid sites never load
Scriban.

**Build performance** — 10k-page cold builds in 2.7s (0.48x Hugo, fastest
of 14 SSGs in the same-window matrix); 64ms dependency-graph incrementals
(500-page site, single-file change, in-process suite) rebuilding only
affected pages; the `--cache` persistent content cache cuts repeat-build
total time another 59% with byte-identical output.

**Migrate Hugo themes directly** — lookup order / render hooks / shortcode
API / namespace functions aligned; ThemeMigrator converts Go templates at
the AST level, flagging indeterminable semantics with `TODO-HUGO` instead
of silently mistranslating; 21 real themes verified.

**No-runtime single-file delivery** — .NET 10 NativeAOT: no runtime,
no node_modules, no JIT warmup — the deliverable is one exe; the asset
pipeline covers CSS minification, lightweight TS type stripping, image
processing, and fingerprinting. CSS minification calls external
lightningcss, falling back to built-in simplified minification.

**Millisecond cold start** — NativeAOT means no JIT warmup and no runtime
bootstrap: `flint version` start to output at a 106ms median (measured
2026-10-28, win-x64 single file, median of 10 runs spanning 89-117ms).

**Sass/SCSS compilation** — external Dart Sass CLI (`FLINT_SASS` or `sass`
on PATH): IncludePaths, compressed output, and inline source maps;
per-build content-signature caching compiles each SCSS once no matter how
many pages reference it; Chinese input on Windows goes through UTF-8 temp
files, immune to system code-page mojibake.

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

# Windows (NativeAOT single file ~41MB; omitting -p:PublishAot=true yields ~89MB non-AOT)
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

### Writing Templates: Fluid (Liquid, default for new sites)

`Flint new site` generates `.liquid` scaffolding by default. Liquid syntax,
case-insensitive members with snake_case support, no output escaping by default:

```html
{{ page.title }}                                  <!-- page fields -->
{{ site.params.author }}                          <!-- site config -->
{% for post in site.regular_pages %}{% endfor %}  <!-- loops -->
{% if page.draft %}{% endif %}                    <!-- conditionals -->
{% include 'partials/header' %}                   <!-- include a partial (shared scope) -->
{% render 'partials/card' %}                      <!-- isolated-scope render -->
```

Full reference (scaffolding inventory, function list, known differences):
[`docs/LIQUID-GUIDE.md`](docs/LIQUID-GUIDE.md).

### Writing Templates: Scriban (Hugo theme compatibility)

Hugo themes (`.html`) and migrator output use Scriban. Both extensions can
coexist in one site (`.liquid` wins within the same root); `engine: scriban`
switches the whole site to Scriban.

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
| Regular page | `{type}/{layout}` → `{type}/page` → `{type}/single` → `{section}/{layout}` → `{section}/page` → `{section}/single` → `{layout}` → `page` → `single` → `all` |
| Home | `{type}/{layout}` → `{type}/home` → `{type}/index` → `{type}/list` → `{layout}` → `home` → `index` → `list` → `all` |
| Section | `{type}/{layout}` → `{type}/section` → `{type}/list` → `{section}/{layout}` → `{section}/section` → `{section}/list` → `section/{layout}` → `section/section` → `section/list` → `{layout}` → `section` → `list` → `all` |
| Taxonomy | `{taxonomy}/{layout}` → `{taxonomy}/terms` → `{taxonomy}/taxonomy` → `{taxonomy}/list` → `taxonomy/{layout}` → `taxonomy/terms` → `taxonomy/taxonomy` → `taxonomy/list` → `_default/terms` → `_default/taxonomy` → `taxonomy` → `list` → `all` |
| Term | `{taxonomy}/{layout}` → `{taxonomy}/term` → `{taxonomy}/list` → `term/{layout}` → `term/term` → `term/list` → `taxonomy/{layout}` → `taxonomy/term` → `_default/taxonomy` → `_default/term` → `term` → `list` → `all` |

Three conventions: bare names (no slash) expand to the `_default/{name}`
form first, then `{name}`; each level checks the site's `layouts/` before
the theme's (same name, site wins); `{type}` levels fall back to the
section name when unset, and `page` precedes `single` while `home`
precedes `index` — orderings verified by stepwise elimination against
Hugo v0.166; full chains in
`src/Flint.Core/Templates/PageTemplateCandidates.cs`.

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
`.Scratch`/`.Store`, Pages methods (`ByDate`/`GroupBy`/`Related`…), 20
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

## 📊 Performance

> Same-machine, same-window measurement (cold builds, multi-run medians) ·
> Windows 10 x64 · Hugo vs Flint NativeAOT. Absolute values drift across
> windows, so only same-window ratios are compared. Methodology in
> [benchmarks/REPORT.md](benchmarks/REPORT.md); reproduction commands under
> [Development](#️-development).

### End-to-End Builds

| Corpus | Pages | Hugo | **Flint Scriban** | **Flint Fluid** |
|------|------|-----:|--------------:|--------------:|
| 10k synthetic (SsgBench) | 10,000 | 5179ms | **2909ms (0.56x)** | **2620ms (0.51x)** |
| MDN Web Docs (Hugo-migrated corpus) | 14,621 | 8153ms | **4292ms (0.53x)** | — |

The 10k ratio reproduces at 0.50-0.56x across three nights; the real MDN
corpus sits at 0.53x. The Fluid arm runs a same-content `.liquid` twin of
the synthetic corpus whose output is byte-identical to the Scriban arm
(10,005 files, all hashes equal); MDN is a Hugo-migrated corpus
(`.html` templates) with no Fluid arm.

### Theme Complexity Ladder (1000 pages × L1/L2/L3)

| Level | Theme content | Hugo | **Flint** | Ratio |
| ---- | -------- | ---- | ----- | ---- |
| L1 basic | single-page rendering | 698ms | **437ms** | **0.63x** |
| L2 medium | + sidebar O(N) site-wide loop | 983ms | **593ms** | **0.60x** |
| L3 heavy | + two O(N) loops + nested partials + partialCached | 1265ms | **865ms** | **0.68x** |

### Peak Memory (build process, RSS / USS · MB)

| Corpus | Hugo | **Flint** | USS delta |
|------|------|-----------|---------|
| 10k synthetic | 399 / 392 | **338 / 321** | **-18% (less)** |
| MDN 14,621 pages | 1427 / 1402 | **893 / 872** | **-38% (less)** |

### In-Process Suite (Release, 12/12 green)

Markdown parsing 229k files/sec · template rendering Scriban 54k / Fluid
175k pages/sec (paired ratio 0.31x) · incremental 64ms (3.4x speedup) ·
10k-site in-process throughput 5,244 pages/sec (0.19ms/page) · config load
0.31ms · concurrency speedup 2.13x · pathological detection green with the
integration suite (1667 tests, run in full by nightly).

### v0.3.0 performance changes (vs the v0.2.1 baseline)

- Fluid became the primary engine: rendering throughput is 3.2x Scriban's
  (in-process pairing 175k vs 54k pages/sec, 2026-10-27).
- Persistent content cache `--cache`: repeat builds of a 14,622-page site
  **-59%** total time, byte-identical output (measured 2026-10-22, local).
- Incremental taxonomy-page trimming: tag-only edits rebuild 16.5s → 3.8s
  (2026-10-27); hot rebuilds skip no-op directory creation, another ~7.7%
  off total time.
- vs Hugo over the same period: the 10k ratio moved 0.81x → 0.56x (Scriban
  arm, like-for-like; the Fluid arm at 0.51x), each ratio from its own
  same-window interleaved run.

Optimization ledger and tooling: `docs/PERFORMANCE-OPTIMIZATION.md` and
`.ai/perf-ledger.md`.

---

## 🆚 SSG Comparison

| Dimension | **Flint** | Hugo | Astro | Eleventy | Jekyll | Hexo |
|---|---|---|---|---|---|---|
| Runtime | **C# / .NET 10+, AOT single-file app** | Go single binary | Node + dep tree | Node + dep tree | Ruby + gems | Node + dep tree |
| Template system | **Fluid/Liquid (primary for new sites) + Scriban (Hugo theme compatibility)** | Go template (deliberately restricted) | UI components (React/Vue/Svelte…) | Multi-engine (Liquid/Nunjucks/EJS…) | Liquid | EJS / Pug etc. |
| Build performance (10k pages) | **1.00x (fastest, 2.7s)** | 2.09x | 25.24x | 3.89x | 6.35x | 12.72x |
| Incremental builds | **64ms (500 pages)** | supported | supported | supported | limited | supported |
| Asset pipeline | **Sass + TS type stripping + minify + fingerprint** | Hugo Pipes (Extended) | Vite toolchain | plugins | plugins | plugins |
| Image processing | **resize/fit/fill/crop, responsive** | built-in | sharp ecosystem | plugins | plugins | plugins |
| Content model | **three-format Front Matter + taxonomies + shortcodes** | comparable | Content Collections (typed) | data cascade | Front Matter + taxonomies | Front Matter |
| Multilingual | **i18n files + theme fallback** | full i18n | i18n routing | community solutions | mostly plugins | multilingual |
| Theme system | **modular + version pinning + multi-theme stacking** | Hugo Modules | npm / copy install | no formal mechanism | gem themes | npm themes |
| Migration tooling | **ThemeMigrator (AST-level theme conversion)** | post import (jekyll) | — | — | — | — |
| Quality assurance | **perf gate + property testing** | — | — | — | — | — |
| Console encoding | **forced UTF-8** | GBK on Windows | UTF-8 | UTF-8 | UTF-8 | UTF-8 |

> Build-performance figures come from the eleven-engine same-window matrix
> measured 2026-10-30 with every engine at its latest stable release that day
> (same-machine 10k-page cold builds, ratios normalized to the fastest arm,
> full matrix at the top of this document; Docusaurus was excluded after
> superlinear collapse); the remaining columns state public structural facts.
> The eight measured Flint-vs-Hugo difference points are documented in
> [docs/FLINT-VS-HUGO.md](docs/FLINT-VS-HUGO.md); the theme compatibility
> matrix in [docs/HUGO-COMPAT-MATRIX.md](docs/HUGO-COMPAT-MATRIX.md).

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
in this order: `FLINT_SASS` env var → `sass` on PATH → repo
`tools/dart-sass/`. The product no longer bundles Dart Sass; install sass
or point `FLINT_SASS` at the executable.

**Is the fixit theme supposed to build slowly?**
Cold builds — yes: under the 100-article shared corpus with full
taxonomy/tag pagination it takes ~5.5 minutes (631 pages); the per-site
timeout is already raised to 600s. All other themes build in seconds to
low minutes. Incremental builds are unaffected: a front-matter tag edit
rebuilds in ~3.8 seconds.

**Can repeated builds go faster?**
`build --cache` enables the persistent content cache: unchanged files skip
re-reading, unchanged outputs skip writing — repeat builds of a 14.6k-page
site drop another 59% with byte-identical output; `--clean` full rebuilds
bypass the cache automatically.

**Fluid or Scriban?**
New sites default to Fluid (Liquid syntax, ~3x the render throughput of
Scriban); `engine = "auto"` routes by template extension (`.liquid` →
Fluid, Hugo-migrated `.html` → Scriban), and both can coexist in one
site. Migrating Hugo themes goes through the Scriban compatibility layer
— that is exactly what ThemeMigrator emits.

---

## 🛠️ Development

### Tech Stack

| Technology | Role |
|------|------|
| **.NET 10+** | LTS baseline and later; NativeAOT compilation, single-file publish |
| **Scriban 7.5.3 + Fluid 3.0.0-beta.8** | Dual template engines: Fluid (Liquid) primary (default for new sites), Scriban as the Hugo theme compatibility layer |
| **Markdig 1.5.0** | CommonMark + GFM parsing |
| **Dart Sass CLI** | External Sass compilation (`FLINT_SASS`/PATH provides `sass`) |
| **Lightning CSS** | CSS minification (external CLI, falls back to built-in simple minifier) |
| **ImageSharp 3.1.12** | Image processing (resize/format conversion/responsive; license-pinned) |
| **Tommy 3.1.2 / VYaml 1.5.0** | TOML config and YAML Front Matter parsing |
| **System.CommandLine 3.0.0-rc.3** | CLI framework |
| **Kestrel** | Dev server (hot reload) |
| **Server GC** | Multi-core parallel collection (batch throughput) |
| **xunit.v3 4.0.2 / FsCheck 3.6.0 / BenchmarkDotNet** | Testing and benchmarking (test-side only; not in the product) |

### Tests and Benchmarks

```bash
# Tests (five suites)
dotnet run --project tests/Flint.Core.Tests -c Release          # Core (unit + property)
dotnet run --project tests/Flint.IntegrationTests -c Release    # Integration (CLI e2e)
dotnet run --project tests/Flint.ThemeMigrator.Tests -c Release # Theme migrator
dotnet run --project tests/Flint.Tools.Tests -c Release         # DevTools / toolchain
dotnet run --project tests/Flint.PerformanceTests -c Release    # Performance suite (in-process)

# Benchmarks (Flint vs Hugo; the 11-engine matrix follows below)
dotnet run --project src/Flint.DevTools -- bench ssg          # 10k-page cold build
dotnet run --project src/Flint.DevTools -- bench complexity   # Complexity ladder
dotnet run --project src/Flint.DevTools -- bench memory       # Peak memory sampling
dotnet run --project src/Flint.DevTools -- perf run           # Perf suite + HTML report
dotnet run --project src/Flint.DevTools -- perf gate          # Regression gate

# 11-engine matrix (reproduces the matrix table; setup and caveats in
# benchmarks/ssg-matrix/README.md)
dotnet run --project benchmarks/ssg-matrix/harness -c Release           # full matrix (10k pages, 3 rounds)
dotnet run --project benchmarks/ssg-matrix/harness -c Release -- --list # readiness check
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
├── tests/                       # Core / Integration / ThemeMigrator / Tools / Performance suites
├── demo-sites/                  # Demo sites (21 themes + gallery + shared corpus)
├── theme-migrator/              # Migration corpus (themes/ clones, candidates/)
└── benchmarks/                  # Performance benchmarks (corpora/reports/gate + the 11-engine ssg-matrix)
```

---

## 🤝 Contributing

Issues and PRs are welcome. Please run the relevant test suites before
submitting; build-performance changes should come with before/after
`perf gate` comparisons (three-run medians). Hugo-compatibility bugs should
include the theme name and `demo build` reproduction steps.

---

## 📄 License

Licensed under the [MIT License](LICENSE).

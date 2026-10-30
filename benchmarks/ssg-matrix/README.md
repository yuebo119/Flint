# SSG 万页矩阵（14 引擎）

README 与 [REPORT.md](../REPORT.md) §12 的十四引擎矩阵就由这套资产复现：站点脚手架、
语料生成器、计时与内存采样、产物审计都在本目录，一条命令跑完。

## 测量口径

- 同一份语料：10,000 页 Markdown，正文逐字一致（仅 front matter 格式随引擎变化）
- 冷构建：每轮先删输出与各引擎持久缓存，再计时
- 外部计时：含进程启动，不含删除与依赖安装
- 预热 1 次（不计入），默认 3 轮取中位，轮序轮转
- 内存：20ms 轮询采样峰值（RSS = `WorkingSet64`，USS 代理 = `PrivateMemorySize64`）
- 产物审计：html 计数、零字节计数、正文特征抽样命中（防“数量对、内容空”）
- 比值按最快臂归一化；跨机器只对照排名结构，绝对值不可比

## 环境要求

- .NET 10 SDK（跑 harness 与两臂 Flint）
- 各引擎运行时按需（Node / Python / Ruby / Zola / Statiq），见下表
- Windows 10 x64 为实测平台；Linux/macOS 口径相同，但绝对时间不可与实测表直比

## 版本与安装

实测轮（2026-10-30）全部引擎为当日最新稳定版。下表是本仓库脚手架锁定的版本，
`npm install` / `pip install` 会按此安装。

| 臂 | 版本 | 安装 |
| --- | --- | --- |
| flint-scriban / flint-liquid | 0.3.0（HEAD） | `dotnet publish src/Flint.Cli -c Release -r win-x64 -p:PublishAot=true -o ./publish`，产物放 `benchmarks/tools/flint-aot/Flint.exe`，或 `--flint <path>` |
| hugo | 0.169.0 extended | 官方 Release 下载，放 `benchmarks/tools/hugo.exe`，或 `--hugo <path>` / PATH |
| zola | 0.23.6 | Release 二进制放 `benchmarks/tools/zola.exe`，或 `--zola <path>` / PATH |
| eleventy | 3.1.7 | `cd benchmarks/ssg-matrix/eleventy && npm install` |
| hexo | 8.1.2 | `cd benchmarks/ssg-matrix/hexo && npm install` |
| vitepress | 1.6.4 | `cd benchmarks/ssg-matrix/vitepress && npm install` |
| astro | 7.4.4 | `cd benchmarks/ssg-matrix/astro && npm install` |
| gatsby | 5.16.1 | `cd benchmarks/ssg-matrix/gatsby && npm install` |
| vuepress | 2.0.0-rc.31 | `cd benchmarks/ssg-matrix/vuepress && npm install` |
| jekyll | 4.4.1 | `gem install jekyll`（Windows 需要 Ruby+Devkit，见 [windows-ruby-jekyll-setup-traps](../../docs/) 同族笔记） |
| pelican | 4.12.0 | `pip install pelican` |
| mkdocs | 1.6.1 | `pip install mkdocs` |
| statiq | 1.0.0-beta.60（NuGet） | `cd benchmarks/ssg-matrix/statiq && dotnet build -c Release`，harness 首次运行会自动构建 |

版本可用 `dotnet run --project benchmarks/ssg-matrix/harness -c Release -- --list` 自查就绪状态。

## 一键装配

拉库后先跑一次装配，把二进制、依赖与 Flint AOT 产物准备好；已就绪的步骤自动跳过：

```bash
dotnet run --project benchmarks/ssg-matrix/harness -c Release -- --bootstrap

# 只装部分引擎（配合 --only 可只跑这部分）
dotnet run --project benchmarks/ssg-matrix/harness -c Release -- --bootstrap --only hugo,hexo
```

装配内容：

| 步骤 | 做什么 | 需要 |
| --- | --- | --- |
| hugo / zola | 从 GitHub Release 下载对应平台资产并解压到 `benchmarks/tools/` | 网络 |
| flint | `dotnet publish src/Flint.Cli -r <rid> -p:PublishAot=true` 到 `benchmarks/tools/flint-aot/` | .NET 10 SDK，1-3 分钟 |
| 六个 npm 引擎 | 各站目录 `npm install` | Node，网络 |
| pelican / mkdocs | `pip install`（已安装则跳过） | Python |
| jekyll | `gem install jekyll`（已安装则跳过） | Ruby |

装配完成后接 `--list` 可看就绪状态。任何一步失败只影响对应引擎，其余照常跑。

## 一键复现

```bash
# 仓库根执行。默认 10000 页、预热 1 + 3 轮
dotnet run --project benchmarks/ssg-matrix/harness -c Release

# 快速自检（200 页 1 轮，几分钟）
dotnet run --project benchmarks/ssg-matrix/harness -c Release -- --pages 200 --rounds 1 --warmup 0

# 只跑部分臂 / 指定工具路径
dotnet run --project benchmarks/ssg-matrix/harness -c Release -- \
  --only flint-liquid,hugo,zola --flint ./publish/Flint.exe --hugo "C:/tools/hugo.exe"
```

输出包含每臂中位时间、vs 最快比值、峰值 RSS、html 数、正文抽样命中与零字节数。
缺依赖的臂会标“缺: …”并跳过，不影响其余臂。

常用参数：`--pages`、`--rounds`、`--warmup`、`--only a,b`、`--list`、`--skip-gen`、
`--regen`、`--root`、`--clean`、`--clean-all`、`--bootstrap`、`--fast`、`--adaptive`、
`--slow-threshold`，以及 `--flint/--hugo/--zola/--node/--python` 显式指定可执行文件。

## 用时与提速

全矩阵默认跑一遍约 75 分钟（14 臂 × 预热 1 + 3 轮 = 56 次构建）。实测用时构成：
构建 77%、删除输出 20%、语料与进程杂项 3%。两个主要耗时段是 MkDocs（约 17 分钟）
与 Statiq（约 15 分钟），合计占 43%。计时器每次结束会打印这三项构成。

| 模式 | 构建次数 | 预计用时 | 用途 |
| --- | ---: | ---: | --- |
| 默认（预热 1 + 3 轮） | 56 | 75 分钟 | 对外发布数据 |
| `--fast` | 42 | 约 55 分钟 | 日常复测 |
| `--fast --rounds 2` | 28 | 约 37 分钟 | 快速回归 |
| `--fast --rounds 2 --pages 2000` | 28 | 约 8 分钟 | 改代码后的冒烟验证 |

`--fast` 的两条规则：

1. 关闭统一预热。快臂的首轮本身是冷构建，三轮取中位已能吸收首轮偏高。
2. 慢臂（首轮超过 `--slow-threshold`，默认 60 秒）的首轮改为预热并丢弃，仍取后两轮
   中位，避免慢臂因少了预热而系统性偏慢，也不把首轮的机器漂移记进结果。

刻意不做的优化：臂之间不并行。两个引擎同时构建会争抢 CPU 与磁盘，计时失去意义。
语料按页数线性缩放，缩小页数会放大各引擎启动开销的占比，只适合冒烟，不适合对外数字。

## 清理

生成的语料与产出很大（14 份语料约 11MB；MkDocs 万页产出可达 16GB），跑完可一次性清掉：

```bash
# 清理语料、产出与各引擎缓存，保留依赖（下次运行自动重建语料）
dotnet run --project benchmarks/ssg-matrix/harness -c Release -- --clean

# 连依赖一起清（node_modules 与 bin/obj 也删，下次需重新安装/构建）
dotnet run --project benchmarks/ssg-matrix/harness -c Release -- --clean-all
```

清理前会逐条打印路径与体积，删除目标限定在矩阵目录内（越界直接拒绝）。
仓库内本目录只有配置与模板，语料与产出从不入库。

## 复现注意事项

1. **Hugo 的 `-d` 是相对 `-s` 源目录解析，不是 CWD**。harness 已用绝对路径规避；
   手工复验时写成相对路径会把产物落到站点目录下，表现为“0 个 html”。
2. **Fluid 与 Scriban 是同一语料的两个孪生站**（`flint-liquid/`、`flint-scriban/`），
   `engine` 字段不同，产物应逐字节一致（除引擎无关的空白差异）。
3. **各引擎的持久缓存必须一起删**：Jekyll 的 `.jekyll-cache`、Gatsby 的 `.cache`、
   Hexo 的 `db.json`、VitePress 的 `.vitepress/cache`、VuePress 的 `.cache/.temp`、
   Statiq 的 `cache/temp`。漏删会把增量收益算进冷构建。
4. **MkDocs 默认主题把全站导航内联进每一页**：单页大小与站点页数线性相关（实测
   200 页 41.7KB / 203 个链接，1000 页 178.5KB / 1003 个链接），万页时单页约
   1.65MB、总写盘 16GB。这是无 `nav` 配置时的默认形态，不是配置错误，也是它排在
   末位的主因之一。
5. **VitePress 每页内联站点级路由哈希表**（`__VP_HASH_MAP__`）：1,000 页时占单页
   48KB 的 87%，万页时单页约 426KB、总产出 4.3GB，同样是 O(N²) 的默认设计。
   VitePress 的构建缓存在 `<站点>/.vitepress/cache`，配置必须放在站点根
   `.vitepress/config.js` 并用 `srcDir: 'docs'` 指向语料目录，放错位置会导致
   config 不生效且冷构建失效。
6. **Statiq 实测用的是源码 main 构建**（NuGet 最新为 1.0.0-beta.60）。默认包版本复现
   的绝对时间与实测表不完全对应；要完全一致，传
   `-p:StatiqWebProject=<Statiq 克隆>/src/extensions/Statiq.Web/Statiq.Web.csproj`。
7. **Docusaurus 不在矩阵内**：4,000 页时每页成本从 16ms 涨到 62ms（超线性），
   万页两种分页配置均失败（无产出 / V8 堆耗尽），排除有据。
8. 语料只生成一次，之后复用；`--regen` 强制重建，`--skip-gen` 完全跳过生成。
   生成的语料与各站输出都在 `.gitignore` 内，不入库。
9. **不要用包装器启动引擎**：Windows 上 `py -m pelican`、`cmd /c jekyll.bat`
   会让内存采样落在启动器而不是引擎进程（实测会得到 5-7MB 的假值）。harness 用
   `python -m`、`ruby -S jekyll` 直调；手工复验时同理。
10. `harness/` 关闭了分析器（`EnableNETAnalyzers=false`）：它继承仓库根
   `Directory.Build.props` 的 `TreatWarningsAsErrors` 与 `latest-all` 规则，
   否则 CA1303 之类的样式规则会让基准程序构建失败。
11. `eleventy/package-lock.json` 已入库以锁定传递依赖；其余 Node 站点首次
    `npm install` 后如需完全可复现，可自行提交 lock 文件。

## 原始日志

`results/` 保存 2026-10-30 两轮实测的原始输出（同日复现轮、全引擎最新版轮），
数字与 [REPORT.md](../REPORT.md) §12 对应，可用于核对。

## 目录结构

```
benchmarks/ssg-matrix/
├── harness/          计时器 + 语料生成器 + 产物审计（独立 csproj，关分析器）
├── flint-scriban/    Flint + Scriban 孪生站（Flint.toml + layouts/）
├── flint-liquid/     Flint + Fluid 孪生站
├── hugo/             hugo.toml + layouts/
├── zola/             config.toml + templates/ + content/blog/_index.md
├── astro/            astro.config.mjs + src/{pages,layouts}
├── eleventy/         eleventy.config.js + src/{index.njk,_includes}
├── hexo/             _config.yml + run-hexo.js + themes/minimal
├── jekyll/           _config.yml + _layouts + index.html
├── vitepress/        docs/{index.md,.vitepress}
├── vuepress/         docs/{index.md,.vuepress}
├── gatsby/           gatsby-config.js + gatsby-node.js + src/
├── mkdocs/           mkdocs.yml + docs/index.md
├── pelican/          pelicanconf.py
└── statiq/           SsgMatrix.Statiq.csproj + input/{index.md,_Layout.cshtml}
```

语料目录（各站的 `content/`、`docs/posts/`、`src/pages/posts/` 等）由 harness 生成后存在，
不提交。

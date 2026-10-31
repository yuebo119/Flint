# SSG 万页矩阵（11 引擎）

README 与 [REPORT.md](../REPORT.md) §12.6 起的十一引擎矩阵就由这套资产复现：站点脚手架、
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

- .NET 10 SDK（跑 harness 与两臂 Flint）；Windows 上构建 Flint AOT 还需
  VS C++ Build Tools（`Desktop development with C++` 工作负载），缺了只会
  跳过 Flint 两臂，其余照常
- 各引擎运行时按需（Node / Python / Ruby / Zola），见下表
- 网络需能访问 GitHub Releases / npm / PyPI / RubyGems。代理只配在
  git/npm 时，hugo/zola 下载会自动读 `git config http.proxy`；也可显式设
  `HTTPS_PROXY` 环境变量
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
| jekyll | 4.4.1 | `gem install jekyll`（Windows 需先装 Ruby + MSYS2/DevKit 工具链；gem 与 pacman 走代理） |
| mkdocs | 1.6.1 | `pip install mkdocs` |

版本可用 `dotnet run --project benchmarks/ssg-matrix/harness -c Release -- --list` 自查就绪状态。

## 一键装配

拉库后先跑一次装配，把二进制、依赖与 Flint AOT 产物准备好；已就绪的步骤自动跳过。
装配完成后命令会继续自动跑一遍默认全量矩阵（万页 × 预热 1 + 3 轮），
一条命令从零到报告，约 25 分钟：

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
| mkdocs | `pip install`（已安装则跳过） | Python |
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

## 测试协议（一次跑完检查、测量、校验、清理、报告）

```bash
dotnet run --project benchmarks/ssg-matrix/harness -c Release -- \
  --pages 10000 --preflight --report --clean-after
```

四个环节都由 harness 执行：

1. **运行前检查**（`--preflight`）：采样 CPU 占用、列出各引擎版本、统计语料文件数与
   指纹、核对所有选中臂的依赖。CPU 占用超过 `--max-load`（默认 15%）或任一臂缺依赖
   时拒绝运行；确需在嘈杂环境跑用 `--force`，但结果不可对外发布。
2. **运行中留痕**：每轮开始前再采样一次 CPU 占用并写入报告，运行被打扰时能从报告
   里看出来。
3. **产物校验**：每臂结束后统计 html 数、零字节文件数、正文抽样命中，以及输出目录中
   修改时间晚于本轮运行开始的文件数（`本次新生成`）。最后一项用来排除命令空跑、
   沿用上次残留产物的情形。任一臂不达标时报告判定为「复核」。
4. **收尾**：`--report` 把环境、版本、协议参数、结果、用时构成、校验说明写到
   `results/run-<时间戳>.md`；`--clean-after` 随后清掉语料与产出。

## 用时与提速

全矩阵默认跑一遍约 24 分钟（11 臂 × 预热 1 + 3 轮 = 44 次构建，2026-10-31 实测
总用时 1,432 秒，其中预热 335 秒、计时构建 886 秒、删除输出 159 秒）。耗时前两名
仍是 Astro 与 Gatsby（各约 4.5 分钟）。此前口径只统计 33 次计时构建（1,107 秒），
不含预热，导致「约 19 分钟」低于真实墙钟；现已改为总用时含全部构建。

各臂配置已按「最优性能 + 产物对称」逐臂审计（2026-10-31，A/B 全部一手实测，
万页 3 轮中位）：Astro 开 `build.concurrency: 8`（-18%）、Gatsby 设
`GATSBY_CPU_COUNT=4` 与关闭遥测（-22%）、MkDocs 关闭默认搜索插件（-16%）、
Hugo `disableKinds`（-9%）、Eleventy `--quiet`、VitePress 关 `vite.build.minify`。
明细与被否方案见 [REPORT.md](../REPORT.md) §12.8；改动都写在各站配置文件与
harness 内，拉库即得，无需手动设置。

| 模式 | 构建次数 | 预计用时 | 用途 |
| --- | ---: | ---: | --- |
| 默认（预热 1 + 3 轮） | 44 | 约 24 分钟 | 对外发布数据 |
| `--fast` | 33 | 约 14 分钟 | 日常复测 |
| `--fast --rounds 2` | 22 | 约 12 分钟 | 快速回归 |
| `--fast --rounds 2 --pages 2000` | 22 | 约 3 分钟 | 改代码后的冒烟验证 |

`--fast` 的两条规则：

1. 关闭统一预热。快臂的首轮本身是冷构建，三轮取中位已能吸收首轮偏高。
2. 慢臂（首轮超过 `--slow-threshold`，默认 60 秒）的首轮改为预热并丢弃，仍取后两轮
   中位，避免慢臂因少了预热而系统性偏慢，也不把首轮的机器漂移记进结果。

`--fast` 与后三行的预计用时按 2026-10-31 实测中位数推算（计时构建 + 删除 +
审计），属估算值；默认模式为全量实测。

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
   Hexo 的 `db.json`、VitePress 的 `.vitepress/cache`。漏删会把增量收益算进冷构建。
4. **MkDocs 已改为导航裁剪 + 关闭搜索插件**（`mkdocs.yml` 里 `nav` 只列首页、
   `plugins: []`）：1 万页仍全部渲染，只是不再把整棵文件树导航塞进每页侧栏，
   也不再建其余引擎默认都不建的搜索索引。默认形态是 252,975ms / 16.5GB，
   只裁导航 13,638ms / 61.5MB，两项都改后 12,128ms / 48.3MB。若改回不配 `nav`，
   单页会随页数线性膨胀（200 页 41.7KB / 203 链接，1000 页 178.5KB / 1003 链接）。
5. **VitePress 开启 `metaChunk` 并关闭 `shouldPreload`**：默认会把站点级路由哈希表
   `__VP_HASH_MAP__` 内联进每一页（1,000 页时占单页 87%，万页单页约 426KB、
   总产出 3.6GB）；开启后万页产出降到 62MB，耗时基本持平。它的构建缓存在
   `<站点>/.vitepress/cache`，配置必须放在站点根 `.vitepress/config.js` 并用
   `srcDir: 'docs'` 指向语料目录，放错位置会导致 config 不生效且冷构建失效。
6. **Docusaurus 不在矩阵内**：4,000 页时每页成本从 16ms 涨到 62ms（超线性），
   万页两种分页配置均失败（无产出 / V8 堆耗尽），排除有据。
7. 语料只生成一次，之后复用；`--regen` 强制重建，`--skip-gen` 完全跳过生成。
   生成的语料与各站输出都在 `.gitignore` 内，不入库。
8. **不要用包装器启动引擎**：Windows 上 `cmd /c jekyll.bat`
   会让内存采样落在启动器而不是引擎进程（实测会得到 5-7MB 的假值）。harness 用
   `python -m`、`ruby -S jekyll` 直调；手工复验时同理。
9. `harness/` 关闭了分析器（`EnableNETAnalyzers=false`）：它继承仓库根
   `Directory.Build.props` 的 `TreatWarningsAsErrors` 与 `latest-all` 规则，
   否则 CA1303 之类的样式规则会让基准程序构建失败。
10. `eleventy/package-lock.json` 已入库以锁定传递依赖；其余 Node 站点首次
    `npm install` 后如需完全可复现，可自行提交 lock 文件。
11. **Astro 的构建缓存在 `node_modules/.astro`**（`cacheDir` 默认值，不在站点目录下），
    harness 已把它与 `dist` 一起纳入每轮冷清与 `--clean`；漏删会把内容层缓存算进冷构建。
12. **Hexo 对机器状态敏感**：同日四个窗口观测 28.2s → 35.4s → 44.2s → 33.5s
    （3 轮中位，flint 锚臂同期持平甚至更快），已验证与删除策略无关
    （串行/并行删除下方向相反）。跨窗口比较该臂只看同窗比值，不引用绝对值。

## 原始日志

`results/` 只保留对外数字对应的原始记录：2026-10-30 两轮（`ssg14-*.log`）、
2026-10-31 十一臂基线（`run11.log` + `run-…-162947.md`）、最优配置审计轮
（`run-…-192507.md`）与同配置复现轮（`run-…-202016.md`），外加 12.5 审计的
eleventy 修复验证日志（`eleventy-remake.log`）。数字与 [REPORT.md](../REPORT.md)
§12 对应，可用于核对；中间过程日志不入库。

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
├── gatsby/           gatsby-config.js + gatsby-node.js + src/
└── mkdocs/           mkdocs.yml + docs/index.md
```

语料目录（各站的 `content/`、`docs/posts/`、`src/pages/posts/` 等）由 harness 生成后存在，
不提交。

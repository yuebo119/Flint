# Flint 性能测试综合报告

**测试日期**：2026-09-08　**平台**：Windows 10 x64 · .NET 10 · 32 核
**Flint 版本**：0.1.0（Release + NativeAOT，aef42a0 构建）　**对照**：Hugo v0.165.0 Extended 官方二进制

---

## 执行摘要

三语料 × 双引擎 × 3 次冷构建中位数：**Flint 在全部口径下持平或显著快于 Hugo**
（万页合成 0.74x、MDN 技术文档 0.65x）。进程内性能套件
11/11 全绿，增量加速比 5.68x。主题复杂度阶梯对照（L1/L2/L3）显示 Flint
全层级保持 12-15% 领先，扩展性曲线与 Hugo 平行——复杂度增长不会反转优势。

---

## 1. 吞吐矩阵（CLI 端到端冷构建，3 次中位数）

同轮同机同语料采集；每次测量前删除输出目录（冷构建口径）。

| 语料 | 页数 | 内容形态 | Hugo | Flint AOT | 比值 |
| ---- | ---- | -------- | ---- | --------- | ---- |
| 万页合成 | 10000 | 同构 lorem 三段 | 5290ms | **3912ms** | 0.74x |
| MDN Web Docs | 14621 | 技术文档（HTML/代码密集） | 9393ms | **6147ms** | 0.65x |

**产物对称性审计**：每轮对比均验证双引擎 html 产出数量一致
（±3 个 list 页形态差异）。曾发现 Hugo 侧仅渲染 362/14974 页的无效测量
并撤回（MDN 首轮，转换器 leaf bundle 嵌套问题）——审计流程见
docs/HUGO-GAP-TASKS.md。

## 2. 进程内性能套件（Release，11/11 全绿）

| 指标 | 数值 | 判定阈值 | 状态 |
| ---- | ---- | -------- | ---- |
| 小型站点 100 页 | 97.1ms | <15s | ✓ |
| 中型站点 500 页 | 422.3ms | <15s | ✓ |
| 大型站点 1000 页 | 826.2ms | <30s | ✓ |
| 超大型站点 10000 页 | 6650.6ms | <60s | ✓ |
| 复杂主题 1000 页 | 466.0ms | <30s | ✓ |
| 增量构建加速比 | 5.68x | ≥自适应阈值 | ✓ |
| Markdown 解析 | 4.97ms/千文件（201k 文件/秒） | ≥50k/秒 | ✓ |
| 模板渲染 | 19.2ms/千页（52k 页/秒） | ≥10k/秒 | ✓ |
| 配置加载 | 0.16ms | <5ms | ✓ |
| 并发构建加速比 | 1.08x | ≥0.95x | ✓ |
| 可扩展性 | O(n) 线性 | ≤2x | ✓ |

病态检测补充：47/47 通过（内存泄漏、并行扩展、边界条件）。

## 3. 主题复杂度阶梯（1000 页 × L1/L2/L3 × 双语法等价实现）

| 层级 | 主题内容 | Hugo | Flint | 比值 |
| ---- | -------- | ---- | ----- | ---- |
| L1 基础 | 单页渲染 | 634ms | 546ms | 0.86x |
| L2 中等 | + 侧边栏 O(N) 全站循环 | 1087ms | 972ms | 0.89x |
| L3 重度 | + 双 O(N) 循环 + 嵌套 partial + partialCached | 1863ms | 1592ms | 0.85x |

**斜率分析**：L1→L3 两引擎等比例增长 2.9 倍（斜率平行）——复杂度增长
不会反转 Flint 的优势。partialCached 双引擎均生效。产物结构经 diff
验证等价（nav×2/aside/article/footer/h1 计数一致）。

## 4. 规模扩展性规律

| 规模 | Flint/Hugo（真实内容） |
| ---- | ---------------------- |
| 10000 页（合成） | 0.92-0.95x |
| 14974 页（MDN+k8s） | 0.76x（修正后测量） |

规模越大 Flint 并行管线优势越明显（解析/渲染/写入全并行 + Server GC）。

## 5. 公平性声明

- **语料对称**：全部对比使用同一份 content 副本，过滤规则双引擎对称
- **模板等价**：双语法实现逻辑严格等价，产物结构经 diff 验证
- **产物审计**：每轮对比验证双引擎 html 产出数量（曾撤回一次 Hugo 侧
  未完成渲染的无效测量）
- **二进制对等**：Hugo 官方 release 预编译（Go 无 Debug 构建之分）；
  Flint Release+NativeAOT（生产标准）
- **同轮采集**：矩阵数据同一轮完成，控制机器状态漂移

## 6. 已知局限

1. **主题复杂度**：对比使用简化模板（复杂度阶梯已单独测，Flint 全层级
   0.85-0.89x 领先）；Docsy 级真实主题对 Hugo 的影响未对等测量
2. **图片管线**：测试站点无图片，ImageSharp 缩放/格式转换未进入对比
3. **单平台**：全部在 Windows 上测量；Hugo 生产部署 80% 在 Linux
4. **轮次**：每点 3 次中位数（检出 20% 劣化够用，精确数字需 15+ 迭代）
5. **增量编辑模式**：仅覆盖"改 1 深层页"，批量/删页/section 变更未测

## 7. 第二轮复测（同环境全清后重演）

全清理（缓存/输出/测试残留/bin-obj）后 AOT 重发布、同矩阵重测。
轮间稳定性确认（与第一轮对比，中位数 ms）：

| 口径 | 第一轮 | 第二轮 | 漂移 |
| ---- | ------ | ------ | ---- |
| 万页合成-Hugo | 5290 | 5540 | +4.7% |
| 万页合成-Flint | 3912 | 4245 | +8.5% |
| MDN-Hugo | 9393 | 8847 | -5.8% |
| MDN-Flint | 6147 | 6374 | +3.7% |

两轮全部口径漂移 ≤8.5%，比值稳定在 0.65-0.95x 区间，结论稳健。
进程内套件 11/11 全绿复确认（增量加速比 6.88x，历史最佳）。

## 8. 复测汇总（三轮全清重演，2026-09-08/09）

各口径三轮中位数汇总（Flint AOT vs Hugo，单位 ms）：

| 口径 | R1 | R2 | R3 | Hugo 参照 | 三轮趋势 |
| ---- | --- | --- | --- | --- | ------ |
| 万页合成 | 3912 | 4245 | 3433 | Hugo 5290/5540/4530 | 稳定 0.74-0.93x |
| MDN 14621 页 | 6147 | 6374 | 5431 | Hugo 9393/8847/8058 | 稳定 0.65-0.79x |

各轮全部口径 Flint 领先，轮间漂移 ≤13%（机器状态噪声），相对结论一致。
病态检测 48/48 全绿（R3），进程内套件 11/11（R3：小型 71ms/中型 320ms/
大型 708ms/超大型 6812ms/增量 51ms/模板渲染 20.2ms）。

**产出页数实测（R3，双引擎 html）**：万页 10006/10007、官方 2239/718
（口径差异见前）、MDN 14623/14576——与既有审计一致。

**内存峰值实测（psutil，3 轮中位数，MB）**：

| 语料 | Hugo RSS/USS | Flint RSS/USS | USS 差异 |
| ---- | ------------ | ------------- | -------- |
| 万页合成 | 409 / 383 | 367 / 326 | -10% |
| MDN 14621 页 | 1486 / 1416 | 921 / 886 | -38% |

采样：构建进程存活期每 20ms 轮询 memory_full_info，取峰值；
USS=进程独占内存（不含共享库）。脚本：scripts/memory-bench.py。

## 9. 第三轮明细（2026-09-09）

与第二轮同流程（全清→AOT 重发布→同矩阵）。**产出页数本轮实测确认**：

| 口径 | 语料 md | Hugo html | Flint html | Hugo | Flint | 比值 |
| ---- | ------: | --------: | ---------: | ----:| ----: | ---- |
| 万页合成 | 10,000 | 10,006 | 10,007 | 4589ms | 3688ms | 0.80x |
| MDN | 14,621 | 14,623 | 14,576 | 8145ms | 5389ms | 0.66x |

> 病态检测 48/48 全绿（含真实语料基线）。ssg-bench 口径复测：
> 0.70x（3565 vs 5093ms），与前两轮 0.92-0.95x 的差异为本轮 Hugo 偏慢
>（5540→5093 区间内），比值波动含机器状态噪声。

## 10. 可复现命令

> 原 Python 版 `scripts/ssg-bench.py` / `complexity-bench.py` /
> `corpus-convert.py` 已迁移为 DevTools 子命令（bench / corpus），下列为
> 现行命令。bench 对比走固定路径二进制 `benchmarks/tools/hugo.exe` 与
> `benchmarks/tools/flint-aot/Flint.exe`（目录不入库，需自行放置：Hugo
> 官方 Extended 版下载；Flint 用 `dotnet publish src/Flint.Cli -c Release
> -r win-x64 -p:PublishAot=true -o ./publish` 产出后复制）。

```bash
# 万页合成对比（自生成语料）
dotnet run --project src/Flint.DevTools -- bench ssg

# 复杂度阶梯对照
dotnet run --project src/Flint.DevTools -- bench complexity

# MDN 语料转换（需先克隆 mdn/content）
dotnet run --project src/Flint.DevTools -- corpus convert

# 性能套件（进程内）
dotnet run --project tests/Flint.PerformanceTests -c Release -- TestResults/report.html

# 性能回归门禁
dotnet run --project src/Flint.DevTools -- perf gate
```

报告快照：`benchmarks/reports/`（四轮演进）+ `TestResults/`（本轮明细）。

## 11. 2026-10-27/28 轮：v0.3.0 发布基线（三臂对照 + 十三引擎矩阵）

**测试日期**：2026-10-27/28　**Flint**：0.3.0（Release + NativeAOT 单文件
41,076,224 字节）　**对照**：Hugo v0.168.0 Extended。协议：冷构建（输出删除
在计时外）、预热 1 + 多轮中位、臂间 2s 间隔 + 轮转顺序（消除前臂落盘冲刷
串扰）、外部计时含进程启动；跨窗口绝对值漂移大（同一 Hugo 二进制三晚
4336→5503→4400ms），只取同窗交错比值。

### 11.1 端到端三臂（SsgBench 万页合成，10,000 页，5 轮轮转中位）

| 臂 | 数值 | 比值 |
| -- | ---- | ---- |
| Hugo v0.168.0 | 5179ms（5032-5202） | 1.00x |
| Flint Scriban（`.html`） | 2909ms（2832-2955） | **0.56x** |
| Flint Fluid（`.liquid` 孪生站） | 2620ms（2596-2702） | **0.51x** |

Fluid 臂为同内容 `.liquid` 孪生站（`benchmarks/corpus/ssg-bench/flint-liquid`，
`engine = "fluid"`），与 Scriban 臂产物逐字节一致（10,005 文件哈希全同）。

### 11.2 MDN 真实语料（14,621 页，Hugo 迁移 `.html`，4 轮轮转中位）

| 臂 | 数值 | 比值 |
| -- | ---- | ---- |
| Hugo | 8153ms（8057-8246） | 1.00x |
| Flint Scriban | 4292ms（4258-4327） | **0.53x** |

每页成本自洽校验：Flint 0.29ms/页与万页合成一致，Hugo 0.56ms/页吻合。

### 11.3 主题复杂度阶梯（1000 页 × 3 次中位）

| 层级 | Hugo | Flint | 比值 |
| ---- | ---- | ----- | ---- |
| L1 基础 | 698ms | 437ms | **0.63x** |
| L2 中等 | 983ms | 593ms | **0.60x** |
| L3 重度 | 1265ms | 865ms | **0.68x** |

### 11.4 内存峰值（构建进程，2 轮中位，MB）

| 语料 | Hugo RSS/USS | Flint RSS/USS | USS 差异 |
| ---- | ------------ | ------------- | -------- |
| 万页合成 | 399 / 392 | 338 / 321 | **-18%** |
| MDN 14,621 页 | 1427 / 1402 | 893 / 872 | **-38%** |

### 11.5 进程内套件（Release，12/12 全绿）

| 指标 | 数值 |
| ---- | ---- |
| 站点构建吞吐 | 100 页 3017 / 500 页 4131 / 1000 页 4487 / 10000 页 **5244 页/秒** |
| 复杂主题（1000 页） | 3527 页/秒 |
| 增量构建 | 64.13ms（加速 3.4x） |
| Markdown 解析 | 228,980 文件/秒 |
| 模板渲染 | Scriban 53,947 / **Fluid 175,047 页/秒**（对拍比值 0.308x） |
| 配置加载 | 0.307ms |
| 并发构建加速比 | 2.13x |
| 可扩展性 | O(n) 线性（比率 0.62x） |
| AOT 冷启动 | `flint version` 中位 **106ms**（10 次，89-117ms） |

### 11.6 十三引擎万页矩阵（2026-10-27，同窗，vsFastest 归一化）

锚 = Flint+Liquid 3478ms = 1.00x：Flint+Scriban 1.15x、Zola 1.15x、
Hugo 1.60x、Eleventy 2.94x、Jekyll 5.06x、Pelican 5.22x、Hexo 13.30x、
VitePress 18.16x、Astro 19.06x、Gatsby 21.17x、VuePress 32.72x、
MkDocs 84.55x；Statiq 91.8x（另窗以 Flint 锚归一化，全场 14 款最慢）；
Docusaurus 因规模超线性崩溃（100 页 7.4s → 4k 页 206s，万页无产出）
排除有据。

### 11.7 协议勘误（相对 9 月轮数字差异的来源）

1. **clean 旗标陷阱**：`--clean`/`--cleanDestinationDir` 的删除阶段计入
   时长会显著虚胀（MDN 曾测得 9644/15408ms 的失真值）；本轮删除一律在
   计时外。
2. **Hugo `-d` 相对 CWD**：相对路径相对当前目录而非 `--source`，须传
   绝对路径。
3. **臂间串扰**：前臂约一万文件的落盘冲刷会泄进下一臂计时窗（Fluid 臂
   曾现 2648-4356ms 假噪声），臂间 2s 间隔 + 轮转后各臂离散 <4%。
4. **跨窗口纪律**：绝对值不可跨窗比较，比值取同窗交错；9 月轮
   0.74-0.80x → 本轮 0.51-0.56x 的改善来自 Fluid 主力引擎化与 10 月
   优化轮，两轮各自同窗有效。

### 11.8 可复现命令

```bash
dotnet run --project src/Flint.DevTools -- bench ssg          # 万页 Hugo vs Flint(Scriban)
dotnet run --project src/Flint.DevTools -- bench complexity   # 复杂度阶梯
dotnet run --project src/Flint.DevTools -- bench memory       # 内存峰值（含 MDN）
dotnet run --project src/Flint.DevTools -- perf run           # 进程内套件 + HTML 报告
# Fluid 臂：benchmarks/corpus/ssg-bench/flint-liquid（同内容 .liquid 孪生站）
#   flint build -s benchmarks/corpus/ssg-bench/flint-liquid -o <输出>（绝对路径）
```

## 12. 2026-10-30 轮：十四引擎矩阵双跑（同日复现 + 全引擎最新版）

背景：应要求对全部同类 SSG 统一复测，并在版本审计后将全部引擎升至
当日最新版重跑。计时器为本机工作区 `tmp/ssg14.cs`（十四臂单进程交错，
Statiq 由 §11.6 的独立锚臂并入主矩阵，消除跨脚本锚归一）。协议与
§11.6 一致：万页同内容语料、删输出冷构建（含各引擎持久缓存）、预热
1 + 3 轮中位、轮序轮转、外部计时含进程启动。

### 12.1 同日复现轮（13:43–14:58，引擎版本与 10-27 轮相同）

vsFastest = Flint+Liquid 2973ms = 1.00x：Flint+Scriban 1.19x、Zola
1.64x、Hugo(v0.168.0) 2.08x、Eleventy 3.81x、Jekyll 6.20x、Pelican
7.36x、Hexo 10.55x、VitePress 23.52x、Gatsby 24.96x、Astro 26.07x、
VuePress 38.44x、Statiq 96.98x、MkDocs 108.50x。产物审计 14 臂
10001–10007 html 与前轮逐臂一致。

### 12.2 全引擎最新版轮（17:15–18:18）

版本审计结论：仅 2 款需升级——Hugo v0.168.0→**v0.169.0**、Astro
7.4.2→**7.4.4**；其余 12 款经 npm dist-tags / pip index / gem
--remote / GitHub releases 核实已是最新（Zola 0.23.6、Eleventy 3.1.7、
Jekyll 4.4.1、Pelican 4.12.0、VitePress 1.6.4 latest 稳定线、Hexo
8.1.2、Gatsby 5.16.1、MkDocs 1.6.1、VuePress 2.0.0-rc.31 = next 线
最新、Statiq 源码 = origin/main、Flint = 0.3.0 HEAD AOT）。升级臂单臂
验证产物对称（各 10001 html）后入矩阵。

| 排名 | 引擎（版本） | 中位 | vs 最快 |
| --: | --- | --: | --: |
| 1 | Flint Fluid（0.3.0 AOT） | 2717ms | 1.00x |
| 2 | Flint Scriban（0.3.0 AOT） | 3069ms | 1.13x |
| 3 | Zola 0.23.6 | 4405ms | 1.62x |
| 4 | Hugo 0.169.0（extended） | 5691ms | 2.09x |
| 5 | Eleventy 3.1.7 | 10558ms | 3.89x |
| 6 | Jekyll 4.4.1 | 17245ms | 6.35x |
| 7 | Pelican 4.12.0 | 18012ms | 6.63x |
| 8 | Hexo 8.1.2 | 34566ms | 12.72x |
| 9 | Gatsby 5.16.1 | 62651ms | 23.06x |
| 10 | Astro 7.4.4 | 68563ms | 25.24x |
| 11 | VitePress 1.6.4 | 81165ms | 29.87x |
| 12 | VuePress 2.0.0-rc.31 | 94209ms | 34.67x |
| 13 | Statiq 1.0.0（JIT，.NET 10） | 231107ms | 85.06x |
| 14 | MkDocs 1.6.1 | 252975ms | 93.11x |

### 12.3 结论

1. **排名复现（13/14）**：Flint+Fluid 三轮全部第一；尾部两名 MkDocs/Statiq
   （~90x 档）远超其余，垫底归属在窗口间互换属波动。VitePress 的名次为第 11，
   与原表第 9 的差异来自 12.4 记录的测量缺陷修正。
2. 升级无实质性能变化：Hugo 0.168→0.169 比值 2.08→2.09x；Astro
   7.4.2→7.4.4 绝对快 11.5% 但同期 Flint 锚快 8.6%，净差约 3% 属窗口
   漂移（[推断]）。
3. 跨窗口锚漂移再次实证：Flint+Liquid 三窗口 3478→2973→2717ms，绝对值
   只可同窗比较。
4. Statiq 预热曾出现一次 `[CRIT] Regex engine timed out`（单臂复测正常，
   判定负载相关偶发），计时器为该臂加了 Retries=1 保护，正式轮未触发。

### 12.4 勘误：VitePress 冷构建测量（2026-10-31）

排查各引擎输出体积时发现 VitePress 臂的两处脚手架缺陷，前两轮（10-27、10-30）
的 60687ms 偏乐观，已修正：

1. **缓存目录路径写错**：VitePress 的构建缓存实际在 `<站点>/.vitepress/cache`，
   而计时器清的是 `<站点>/docs/.vitepress/cache`，等于每轮都带着 bundler 缓存，
   不是冷构建。
2. **配置文件位置错误**：`config.js` 放在 `docs/.vitepress/` 下未被读取（产物
   `<title>` 显示默认品牌），站点实际按默认配置渲染；已移到站点根
   `.vitepress/config.js` 并显式 `srcDir: 'docs'`。

修正后按同一协议（预热 1 + 3 轮中位）复测两组：83,699ms 与 **81,165ms**
（单轮样本 73,621 / 81,165 / 84,304），取 81,165ms 入表，比值 29.87x，名次第 11。
其余 13 臂不受影响。VitePress 单页体积约 426KB 的来源是站点级路由哈希表
`__VP_HASH_MAP__` 内联（1,000 页时占单页 48KB 的 87%），属引擎默认设计。

### 12.5 全臂数据真实性审计（2026-10-31）

方法：每臂用 200 页快检（配置是否生效、首页是否列出全部文章、产物数与正文命中、
零字节数），再核对每臂的持久缓存目录是否被冷构建清理覆盖，最后对脚手架有改动
或曾存疑的臂做万页复测。

| 臂 | 配置生效证据 | 首页列表 | html | 缓存清理 | 结论 |
| --- | --- | ---: | ---: | --- | --- |
| flint-scriban | 页面标题 Page N | 200 | 201 | 无持久缓存 | 通过 |
| flint-liquid | 页面标题 Page N | 200 | 201 | 无（`--cache` 未启用） | 通过 |
| hugo | 页面标题 Page N | 200 | 201 | 无 | 通过 |
| zola | 页面标题 Page N | 200 | 203 | 无 | 通过 |
| astro | 页面标题 Page N | 200 | 201 | 仅 `.astro` 类型文件 | 通过 |
| eleventy | 页面标题 Page N | 200（修复后） | 201 | 无 | 修复后通过 |
| hexo | 页面标题 Page N | 200 | 201 | 未产出 db.json | 通过 |
| gatsby | 页面标题 Page N | 200 | 202 | `.cache` 39MB 已删 | 通过 |
| vitepress | `<title>Page N \| SSG Bench` | 200 | 202 | build 模式无预打包缓存 | 修复后通过 |
| vuepress | `<title>Home \| SSG Bench` | 静态首页（设计如此） | 202 | `.temp` 已删 | 通过 |
| pelican | `<title>SSG Bench` | 200（标题出现两次） | 207 | 无 | 通过 |
| mkdocs | site_name 命中 | 200（导航） | 202 | 无 | 通过 |
| jekyll | `site.docs` 集合生效 | 200 | 201 | `.jekyll-cache` 已删 | 通过 |
| statiq | 布局与标题生效 | 静态首页（设计如此） | 201 | `cache/` 8MB 已删 | 通过 |

审计中发现并修复的三处缺陷：

1. **VitePress 产物与缓存路径、config 位置**（详见 12.4）：已发布值偏乐观，
   复测更正为 81,165ms。
2. **Eleventy 首页为空**：语料不含 tag，而脚手架用 `collections.posts`，未定义
   集合时该集合为空。已在 `eleventy.config.js` 显式 `addCollection`，首页恢复
   200 条；万页复测 10,738ms，与表内 10,558ms 差 1.7%，不影响排名。
3. **两个脚手架文件从未入库**：`.gitignore` 曾整目录排除 `zola/content/` 与
   `mkdocs/docs/`，导致 `_index.md`、`index.md` 未进仓库，新克隆会构建失败。
   已改为只忽略生成的 `page-*.md` 并补入这两个文件。

MkDocs 的导航内联属默认主题行为而非配置错误：`nav` 未配置时由文件树自动生成
整棵导航并渲染进每页侧栏。这项开销可以量化：万页语料下，默认形态 252,975ms /
16.5GB，把 `nav` 限定为 1 个条目后降到 **14,020ms / 91MB**（同窗口、机器带负载，
安静窗口应更低；该配置只用于量化导航成本，不能反映任何真实站点）。
换句话说表中 MkDocs 的 93.11x 是「万页平铺、未配导航」的开箱默认值，真实大规模
站点通常会改用带导航剪枝的第三方主题，性能会显著不同。其余引擎的文章页没有
全站数据：200 页时单页 0.7 到 4.4KB（VitePress 例外，见下），MkDocs 是唯一把
全站导航塞进每页的实现。

VitePress 每页 14KB（200 页）到 426KB（万页）的来源是客户端路由哈希表
`__VP_HASH_MAP__`（200 页时该脚本 7,748 字节），属 SPA 路由所需，没有关闭开关。

其余 12 臂的脚手架自 10-27 轮起未改动，数值沿用；上述修复只影响被点名的臂。

**VuePress 的复测边界**：冷构建路径已核实（`.temp` 每轮开始前被删除，运行后重新生成
61KB），但 2026-10-31 尝试万页复测时本机被其他进程占用，同窗口的轻量臂全线变慢
1.7 到 2.9 倍（Flint 144→245ms、Hugo 371→856ms、Zola 185→531ms），VuePress 单轮
跑出 760 秒，该值不可用已丢弃。已发布的 94,209ms 来自 10-30 的安静窗口，保留并标注
为未在修正轮复测。矩阵数字只在同窗口内可比，这一点再次得到印证。

### 12.6 2026-10-31 十一臂基线（清单调整后）

按需求调整清单：移除 Pelican、VuePress、Statiq 三个长尾引擎；MkDocs 改为导航
裁剪配置（`nav` 只列首页，1 万页仍全部渲染）；VitePress 开启 `metaChunk` 并关闭
预加载。协议为预热 1 + 3 轮中位，含运行前门禁与产物校验。

| 排名 | 引擎 | 中位(ms) | vs 最快 | 产出(MB) | 峰值 RSS(MB) |
| --: | --- | --: | --: | --: | --: |
| 1 | Flint Liquid | 2572 | 1.00x | 7.0 | 194 |
| 2 | Flint Scriban | 2901 | 1.13x | 7.0 | 341 |
| 3 | Zola 0.23.6 | 4690 | 1.82x | 7.0 | 134 |
| 4 | Hugo 0.169.0 | 5217 | 2.03x | 7.0 | 444 |
| 5 | Eleventy 3.1.7 | 11553 | 4.49x | 7.0 | 434 |
| 6 | MkDocs 1.6.1（导航裁剪） | 13638 | 5.30x | 61.5 | 92 |
| 7 | Jekyll 4.4.1 | 18220 | 7.08x | 7.1 | 157 |
| 8 | Hexo 8.1.2 | 28196 | 10.96x | 7.1 | 811 |
| 9 | VitePress 1.6.4 | 71747 | 27.90x | 62.4 | 3060 |
| 10 | Gatsby 5.16.1 | 75194 | 29.24x | 20.4 | 1592 |
| 11 | Astro 7.4.4 | 75651 | 29.42x | 7.2 | 2473 |

配置调整带来的变化：

- **MkDocs**：354,032ms / 16.5GB 变为 13,638ms / 61.5MB（快 26 倍，产出小 267 倍），
  名次从末位升到第 6。
- **VitePress**：75,593ms / 3,620MB 变为 71,747ms / 62.4MB（产出小 98%），耗时基本持平。
- **总用时**：56 次构建 5,489 秒降到 44 次构建 1,107 秒。

校验：门禁读数 13.0%，运行中逐轮负载 2.6%–13.0%，11 臂全部通过 html 数、
零字节、正文抽样与“本次新生成”四项检查。

### 12.7 复现

矩阵资产已入库 `benchmarks/ssg-matrix/`：14 个站点的脚手架（配置与模板、
依赖版本锁定）、语料生成器、计时与内存峰值采样、产物审计，一条命令跑完。

```bash
dotnet run --project benchmarks/ssg-matrix/harness -c Release          # 默认 10000 页 / 3 轮
dotnet run --project benchmarks/ssg-matrix/harness -c Release -- --list # 就绪自查
```

版本清单、安装步骤与已知坑见 [ssg-matrix/README.md](ssg-matrix/README.md)。

两处与实测轮的差异：① 早期轮次在 Windows 用 `py` / `cmd` 包装器启动
Python 与 Jekyll 臂，仓库版改为直调（`python -m`、`ruby -S jekyll`），剥离
包装器开销，三臂时间改善幅度均低于 2%，不影响排名；② Statiq 实测用源码
main 构建，仓库版默认 NuGet 1.0.0-beta.60，该臂绝对时间会偏离实测行。
### 12.8 2026-10-31 晚间轮：逐臂最优配置审计 + 计时链修正

应要求对 11 臂逐一审计「是否最优配置/最佳实践」，并在保持真实反映性能的前提下
缩短全矩阵总时长。方法：每臂列候选旋钮，万页、同窗口、harness 冷构建口径下
A/B 实测；JS 臂单轮方差 ±10s（astro 同日观测 63.4–91.3s），单轮结果不下结论，
采纳项至少 3 轮中位。

#### 12.8.1 已采纳的配置（六臂）

| 臂 | 改动 | A/B（万页 3 轮中位） | 依据 |
| --- | --- | --- | --- |
| astro | `build.concurrency: 8`（默认 1） | 78.1s → 64.0s（-18%） | 官方并发旋钮，万页纯 CPU 渲染串行是主瓶颈 |
| gatsby | env `GATSBY_CPU_COUNT=4` + `GATSBY_TELEMETRY_DISABLED=1` | 81.7s → 64.2s（-22%） | 默认 worker 数（核数-1=31）抖动；阶段拆解：source and transform nodes（remark 转换）占 63%，SSR 仅 3.3s；4 < 8≈16 < 2≈1 |
| vitepress | `vite.build.minify: false` | 69.5s → 66.8s（约 -4%，[推断]） | 3 样本中位对比单基线；产出字节数不变（62.4MB） |
| mkdocs | `plugins: []`（关默认搜索） | 13.64s → 11.45s（-16%），产出 61.5→48.3MB | 其余 10 臂默认均不建搜索索引，保留属单侧惩罚 |
| hugo | `disableKinds = ["taxonomy","term","RSS","sitemap","robotsTXT"]` | 5.22s → 4.77s（-9%），峰值 RSS 444→284MB | 最小产物口径；其余各臂默认不产分类/RSS/sitemap |
| eleventy | harness 加 `--quiet` | 万页逐文件 Writing 日志省略；全量轮 11.55→10.63s（跨窗口，方向与预期一致） | hugo `--quiet`、hexo silent 已有，口径对齐 |

harness 侧配套：`Arm` 新增 `Env` 字典承载 Gatsby 环境变量；Eleventy 参数加 `--quiet`。

#### 12.8.2 审计后不动的臂（五臂）

| 臂 | 结论 |
| --- | --- |
| flint-liquid / flint-scriban | 已是最优：AOT + Server GC（csproj 显式开启）+ 并行满核；`--cache` 按冷构建口径关闭。两臂差异（Fluid vs Scriban）即矩阵要测的引擎差异本身 |
| zola | 配置即最小形态（无 sitemap 关闭开关）；无代码块时 syntect 不介入 |
| hexo | highlight 管线在无代码块时短路（`hexo/dist/plugins/filter/before_post_render/backtick_code_block.js:80`），语料无代码块零成本；marked 为默认渲染器，不引入替代品改变语义 |
| jekyll | 4 行配置即最小形态；`.jekyll-cache` 每轮冷清已覆盖 |
| vitepress（其余项） | `posts.data.js` 数据加载器多扫一遍 1 万文件（实测约 3.8s），但它是 VitePress 列出全部文章的唯一正道，保留；`metaChunk` + `shouldPreload: false` 沿用 12.6 |

#### 12.8.3 实测后否决的候选（防重探）

- astro `experimental.parallelPrerender`：106.6s / 峰值 7.5GB（默认 78.3s / 2.4GB）——
  31 个 worker 各自重建 Vite 模块图，负收益。
- astro `compressHTML: false`：79.2s，比默认差。
- astro 首页 `import.meta.glob(..., { import: 'frontmatter' })`：86.6s，比 eager 全量差；
  首页 glob 本身成本约 15s（stub 对照 63.4s vs 78.3s），但在产物对称约束内无更优等价实现。
- gatsby `GATSBY_CPU_COUNT=1/2`：SSR 阶段并行度跌水（3.0→6.1s），总时长回升到 73s。

#### 12.8.4 harness 五项改动

1. **删除并行化**：`Directory.Delete(recursive)` 逐文件串行，万页 1 万+ 文件使删除占
   全矩阵 16%（旧 176s/33 次）。改为并行删文件再删空目录树，同口径 159s/44 次，
   大产出臂单次约 5.3s→2.5s。反向验证：串行 vs 并行删除下 hexo/flint 构建计时方向
   相反（44.2s vs 35.9s），非计时污染源。
2. **墙钟口径修正**：旧「总用时」秒表在预热之后才启动（1,107s 只含 33 次计时构建，
   真实墙钟约 25 分钟）。现提前到预热前，报告单列「预热另计」，并新增每臂各轮明细表。
3. **语料生成随 `--only`**：旧版部分跑也生成全部 11 份语料（11 万文件）。
4. **Astro 冷清缺口修复**：Astro 的 `cacheDir` 默认在 `node_modules/.astro`
   （vite-sync 缓存），此前每轮冷清与 `--clean` 都不覆盖，现纳入。
5. Gatsby 环境变量、Eleventy `--quiet`（见 12.8.1）。

#### 12.8.5 新基线（同窗口全量复跑）

运行 19:00–19:25，门禁 CPU 5.6%，运行中逐轮 4.1–5.1%，语料指纹
5295e04953953f7b 与 12.6 基线一致，11 臂全部通过四项产物校验。
原始报告 `results/run-20261031-192507.md`。

| 排名 | 引擎 | 中位(ms) | vs 最快 | 产出(MB) | 峰值RSS(MB) | 12.6 基线比值 |
| --: | --- | --: | --: | --: | --: | --: |
| 1 | Flint Liquid | 2689 | 1.00x | 7.0 | 192 | 1.00x |
| 2 | Flint Scriban | 2882 | 1.07x | 7.0 | 335 | 1.13x |
| 3 | Zola 0.23.6 | 4518 | 1.68x | 7.0 | 136 | 1.82x |
| 4 | Hugo 0.169.0 | 5029 | 1.87x | 7.0 | 282 | 2.03x |
| 5 | Eleventy 3.1.7 | 10633 | 3.95x | 7.0 | 405 | 4.49x |
| 6 | MkDocs 1.6.1 | 12128 | 4.51x | 48.3 | 77 | 5.30x |
| 7 | Jekyll 4.4.1 | 17557 | 6.53x | 7.1 | 158 | 7.08x |
| 8 | Hexo 8.1.2 | 35364 | 13.15x | 7.1 | 810 | 10.96x |
| 9 | VitePress 1.6.4 | 67528 | 25.11x | 62.4 | 3068 | 27.90x |
| 10 | Gatsby 5.16.1 | 67773 | 25.21x | 20.4 | 1549 | 29.24x |
| 11 | Astro 7.4.4 | 68077 | 25.32x | 7.2 | 2498 | 29.42x |

跨窗口对照说明：配置改动与窗口漂移混在同一差值里，不能把「新表 − 12.6 表」
逐臂归因给配置。头部臂锚（flint 同窗 +4.5%、hugo/zola 反而更快）说明窗口本身
不系统性偏慢；Astro/Gatsby/VitePress 在同窗口内对配置 A/B 的改善幅度（-18%/-22%/-4%）
是一手证据。总用时：44 次构建 1,432s（含预热 335s），删除 159s。

**Hexo 窗口敏感性观察**：同日 16:04 / 19:25 / 19:37 三个窗口 3 轮中位分别为
28.2s / 35.4s / 44.2s，同期 flint 锚臂 2.57–2.69s 持平；已排除删除策略因素
（12.8.4 第 1 条），根因未定位，标注 [观察]，该臂跨窗口只取同窗比值。

#### 12.8.6 未覆盖声明

- jekyll/zola/hexo/flint 未找到进一步旋钮，仅做了配置层审计，未做引擎内部剖析。
- vitepress 的 `minify: false` 收益（约 -4%）弱于其他项，如需更强证据需补多窗口对照。
- astro 首页 eager glob 的约 15s 在产物对称约束内无已验证的更优实现（content
  collections 迁移会改 harness 语料路径，未纳入本轮）。
- 2026-10-31 同配置全量轮跑了 2 次（12.8.5 的 19:25 轮与 20:20 复现轮
  `results/run-20261031-202016.md`）：排名结构一致（前四名与后三名不变），
  三个 JS 臂（astro 26.59x / vitepress 26.70x / gatsby 26.75x）内部顺序在
  两轮间互换，差距 0.7% 以内属噪声；后者窗口更快（flint 锚 2213ms vs 2689ms），
  跨窗口只取同窗比值。

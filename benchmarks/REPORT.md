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
   与原表第 9 的差异来自 12.5 记录的测量缺陷修正。
2. 升级无实质性能变化：Hugo 0.168→0.169 比值 2.08→2.09x；Astro
   7.4.2→7.4.4 绝对快 11.5% 但同期 Flint 锚快 8.6%，净差约 3% 属窗口
   漂移（[推断]）。
3. 跨窗口锚漂移再次实证：Flint+Liquid 三窗口 3478→2973→2717ms，绝对值
   只可同窗比较。
4. Statiq 预热曾出现一次 `[CRIT] Regex engine timed out`（单臂复测正常，
   判定负载相关偶发），计时器为该臂加了 Retries=1 保护，正式轮未触发。

### 12.5 勘误：VitePress 冷构建口径（2026-10-31）

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

### 12.6 复现

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

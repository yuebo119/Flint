# Flint EPSL 地毯审计轮报告（2026-10-11）

> 触发：用户指令（EPSL v1.2 SOP 首次对 Flint 全流程运行）。基线 dev 分支（0695803 之后）。
> 时点声明与推翻机制：AOT 臂 = 9-24 22:04 预发布二进制（其后 Core 改动仅 BOM 剥离与版本号，性能形态等价）；套件口径为当日实测。同仓若有并行会话插入提交，本报告验证结论以本报告提交号为界。

## 1. 审计概览

| 阶段 | 结果 |
| --- | --- |
| 1.1 analyzer 全量 | Release 全解决方案 0 警告 0 错误（TreatWarningsAsErrors=true，无 NoWarn 豁免） |
| 1.2a perf gate（套件口径） | **9/9 PASS**，全部负 delta（-0.9% ~ -9.3%）；万页 6429.9ms（基线 6694，-3.9%） |
| 1.2b bench ssg（AOT 双引擎） | 万页冷构建 3 轮中位：**Flint 3790ms vs Hugo 4722ms = 0.80x**（预算 ≤1.0x PASS）；绝对值较 9 月基线（3361/3354）双引擎同向变慢 ≈13/41%，判环境态，比值口径有效 |
| 1.2b bench complexity | L1 515/672=**0.77x** · L2 851/895=**0.95x** · L3 1121/1328=**0.84x**；**峰值 RSS：L3 417MB vs Hugo 175MB = 2.4x**（内存主线未解的独立新证） |
| 1.3 复杂度契约测试 | **空缺**：perf-budgets §3 两行契约均为「候选（未实施）」，无架构测试载体（状态已诚实标注，非违规；转正依赖 R7 T2 组） |
| 1.5 自动归因 | 免（零红项） |
| 2 地毯深读 | 5 分片全量（Site/Templates 资产面/Scriban 渲染器/Content+Config/Assets+Server），40,003 行全覆盖，**35 条新候选**；1 次分片模型请求失败原样重派成功 |
| 主审抽查 | 高严重度 5 条 + 关键中条 2 条全部源码核实属实（7/7），分片继承论断可信 |
| 3 强制收敛 | A 批 2 项闭环（见 §5）；B 批负载面探针完成（见 §4）；其余终裁见发现总表 |

## 2. 阶段 1 补充记录

- **地板健全性**：SSG 双引擎对照无独立地板臂（数据库场景形态），Hugo 即对照臂，此项不适用。
- **环境税**：本轮 Hugo 臂 4722ms vs 9 月 3354ms（+41%）——非 Flint 回归（Flint 套件口径反而 -3.9%），判机器环境态；基线不重录。

## 3. 阶段 2 深读形态学

35 条候选中 9 条为**不对称残余**（同文件/同族先例已做、漏一格：GetDictParams 26 键 HashSet 漏改 FrozenSet、highlight 正则是 T1.4 九处外的第十处、LazyTaxonomies 是同族唯一漏锁者），其余为跨层数据流候选（analyzer 不可见的调用频次 × 单次成本形态）。零机制级 L1 算法新发现（两个 O(n²) 已在 ledger #1/#2 终裁）——与高优化度代码库收敛信号一致。

## 4. B 批负载面探针（本轮核心产出）

插桩点：ProviderLoad/TextReads/ReadBytes/ContentDigest/FileExists（临时插桩，已还原）。四负载面：

| 负载面 | Load 调用=文本读盘 | ContentDigest 次数/字节数 | fileExists | ReadBytes |
| --- | --- | --- | --- | --- |
| blowfish 21 页 | **9313** | **3880 / 229.8MB** | 0 | 0 |
| fixit | **5660** | 4057 / 137.8MB | 1865 | 0 |
| 万页合成 10001 页 | 0 | 0 | 0 | 0 |
| MDN 14622 页 | 0 | 0 | 0 | 0 |

**结论**（ledger #1/#2 同款负载面方法论）：
- **C1（resources.Get 无内容缓存）+ C5（变换缓存命中前全量 SHA256）在真实资产密集主题满额生效**：blowfish 单构建 9313 次重复读盘 + 230MB 内容哈希。万页/MDN 语料零调用。量级封顶：230MB SHA256（SIMD ~1.5-2GB/s）≈115-153ms + 9313 次读盘（OS 热缓存 ~10-20µs/次）≈93-186ms，合计约 210-340ms/构建级潜力（占比待修复实测）。
- **C3（ApplyImageOp 每次读原图字节）当前负载面为零**（ReadBytes=0×4）——分片机制属实但无触发语料，不做（触发器：位图变换主题出现）。
- C8（fileExists 无缓存）fixit 1865 次真实调用，小项可立项。

## 5. 修复列表（A 批 2 项）

| 提交号 | 修复内容 | 验证 |
| --- | --- | --- |
| （本轮提交①） | LazyTaxonomies 并发写加锁（ScribanTemplateRenderer.Objects.cs）——site 对象跨并行渲染共享，普通 Dictionary 无锁惰性写是竞态（同族其余包装器均有锁，此处曾漏） | 1126 测试全绿（含 21 主题矩阵，见下）；并发红探针按 SOP 探针器材条 5 降级为结构证据（竞态三段事实：共享 CWT × 并行渲染 × 无锁可变字典，源码核实） |
| （本轮提交①） | partial 解析缓存命中时跳过 FileTemplateLoader.Load（ScribanTemplateRenderer.cs）——原序每次 partial 调用付一次 mtime stat + 全文读（hugo-book 1871 次/构建实测注释自证），命中分支补 RenderDependencyTracker.Track（幂等） | 新增回归测试 RenderAsync_PartialFunction_DepsTrackedOnCacheHitToo（第二次渲染依赖仍登记——防增量构建漏重渲）+ RenderDependencyTests 全组 + 21 主题矩阵逐字节对拍；墙钟收益 Not-Tested（验收判据：下轮 perf gate 模板渲染列 / blowfish 构建墙钟对比） |

21 主题矩阵结果：**Flint 臂 21/21 通过（产出非空），20/21 对称绿（门禁④）**。fixit 行对称位不可比：其 **Hugo 基线臂构建失败（0 页）**——hugo.exe 独立进程与本轮 Flint Core 改动无关（旁证：探针轮 fixit 托管构建成功、loveit/blowfish 同族 22 页一致），判环境态待查；fixit 的 Flint 臂通过 22 页。

## 6. 发现总表（SOP 终局交付，逐条覆盖 35 候选）

状态枚举：已修复（A）/ 待裁决（B）/ 不修登记（不做）/ R7 归并（同族并单不重复立项）。锚点格式 文件:行。★=主审源码抽查核实。

| # | 问题/优化点 | 证据锚点 | 严重度 | 状态 | 修复方式/建议 |
| --- | --- | --- | --- | --- | --- |
| 1 | ★ LazyTaxonomies 无锁并发写（正确性） | ScribanTemplateRenderer.Objects.cs:2453-2472 | 高 | 已修复（A） | 全程锁，同族范本对齐；见 §5 |
| 2 | ★ partial 解析缓存判定在 Load 之后（每次 stat+全文读） | ScribanTemplateRenderer.cs:1121-1175 | 高 | 已修复（A） | 缓存判定提前+Track 补登记；见 §5 |
| 3 | ★ resources.Get/Load 无内容缓存，每次全文读盘 | TemplateResourceFunctions.cs:112-122,184-210 | 高 | R7 归并（B） | 探针 9313/5660 次读盘；mtime 验效文本缓存（FileTemplateLoader._textCache 同款，十行级）→ 立项 R7 T1.13 |
| 4 | ★ 变换缓存命中前 ContentDigest 全量 SHA256 | TemplateResourceFunctions.cs:3666-3683 | 中高 | R7 归并（B） | 探针 230MB/构建；与 #3 联动（摘要记忆化形态需实测裁决） |
| 5 | ★ ByType/Match 读盘先于类型过滤 + glob 正则每次重建 | TemplateResourceFunctions.cs:148-182,213-239 | 高→中 | R7 归并（B） | 随 #3 顺带：Create 探针预筛 + glob→Regex 缓存 |
| 6 | fileExists 每页双 stat 无缓存 | NamespacedFunctionImplementations.cs:491-492 | 低中 | R7 归并（B） | 探针 fixit 1865 次；构建期路径缓存（失效口径同 mtime 缓存） |
| 7 | ★ NeedsFullPipeline 单 `$` 误切全扩展管道 | MarkdownParser.cs:216-218,298-332 | 中 | 待裁决（B） | 语料 `$` 命中率 grep + 双管道 A/B（合成套件测不出：无 $）；fenced code 内排除 |
| 8 | ★ ComputeTaxonomySignature 每次增量全树排序+MB 级串哈希 | SiteBuilder.Incremental.cs:421-446 | 中 | 待裁决（B） | 增量预算 91ms 内占比实测（10k 页估算 15-25ms）；序无关聚合哈希重设计 |
| 9 | Tree 两阶段装配 O(S×N)（section 密集触发） | SiteBuilder.Tree.cs:339-381 | 中 | 待裁决（B） | 条件触发；探针=多 section 套件阶段计时；增量路径全量重付 |
| 10 | bundle 资源每页重新目录枚举 | SiteBuilder.Tree.cs:403-445 | 中 | 待裁决（B） | 条件触发（leaf bundle 密集站点）；树节点缓存资源清单 |
| 11 | 列表页 RSS 循环串行渲染+串行写盘 | SiteBuilder.Output.cs:299-334,635-651 | 中 | 待裁决（B） | 词条密集站点；并入并行批次机制（同 T3.1 族） |
| 12 | BuildTaxonomyTerms 每成员资格重算 slug | TaxonomyService.cs:109-130,168 | 低中 | 待裁决（B） | 按唯一词条记忆化；增量路径 10ms 级 |
| 13 | BuildTermPages 每分页页重建全部 term 投影（T²） | SiteBuilder.Render.cs:617-666,791,853 | 低 | 待裁决（B） | 条件触发（词条列表 .Paginate 主题）；term 投影循环外共享 |
| 14 | Related 评分每对重建 b 侧集合（4N 次） | PageListFunctions.cs:162-233 | 中高 | 待裁决（B） | b 侧提升循环外；Related 密集主题分配剖析先行 |
| 15 | GetMember 每元素重建 Split+大小写变体串 | BuiltinTemplateFunctions.cs:2795-2859 | 中 | 待裁决（B） | where/sort 热路径；变体按 key 缓存 |
| 16 | has_shortcode 每调用最多 8 趟全文扫描 | ScribanTemplateRenderer.Objects.cs:1098-1105 | 中 | 待裁决（B） | hugo-coder 42 调用点；页级 shortcode 名集合预计算 |
| 17 | RenderHook 每元素重建 stub 上下文图 | ScribanTemplateRenderer.cs:703-837,57-89 | 中 | 待裁决（B） | 内容解析段剖析先行；stub 图按 hook 名构建级共享 |
| 18 | dict partial 上下文每次全键复制+别名重建 | ScribanTemplateRenderer.cs:1291-1342,1411-1511 | 中低 | 待裁决（B） | 同 dict 引用合并产物缓存；菜单循环密集主题实测 |
| 19 | TemplateShortcode 每渲染反射 Import | Content/Shortcodes/TemplateShortcode.cs:150-165 | 中 | 待裁决（B） | 探针=反射计数+分配；短代码密集语料 |
| 20 | 短代码占位符 O(k·n) 整串重建 | Content/Shortcodes/ShortcodeProcessor.cs:153-167,276-290 | 低 | 待裁决（B） | Parse 阶段一次拼装（Angle 型已有范本） |
| 21 | GetDictParams 每文件重建 26 键 HashSet | FrontMatterParser.DictMapping.cs:341-360 | 低 | R7 归并 | FrozenSet 常量（Reader.cs:59 同族先例）→ T1.12 |
| 22 | highlight 短代码运行时正则（T1.4 第十处） | BuiltinShortcodes.cs:245-248 | 低 | R7 归并 | GeneratedRegex → T1.4 范围追加 |
| 23 | TOML 日期逐值反射归一化 | ConfigNormalizer.cs:51,71-83 | 低 | 待裁决（B） | TOML-FM 语料限定；探针=反射计数 |
| 24 | 资产内存缓存整段保留全部产物字节 | AssetPipeline.cs:30,100-147 | 中 | R7 归并 | EnableMemoryCache A/B（RSS 2.4x 新证）→ T5.2 关联注记 |
| 25 | 直通二进制全量读入内存后原样写盘 | AssetPipeline.cs:122-127,372-384 | 中 | R7 归并 | 流式 File.Copy 路径 → T5 组追加 |
| 26 | 图像并行宽度 × 全帧解码峰值乘数 | SiteBuilder.Output.cs:104-110 | 中 | R7 归并 | Parallelism=1 A/B → T5.2 关联注记 |
| 27 | 每个 JS 文件一次 esbuild 进程 spawn+临时目录 | JavaScriptBundler.cs:171-220 | 中 | R7 归并 | esbuild 常驻服务模式（T5.1 dart-sass 同族）→ T5 组追加 |
| 28 | LiveReload 广播锁内无超时串行发送 | LiveReloadNotifier.cs:162-199 | 中 | R7 归并 | 单发送超时 → T7 组追加（dev 健壮性） |
| 29 | 图像输入/输出各一次全量 ToArray 拷贝 | ImageProcessor.cs:164,189,298 | 低 | R7 归并 | T5.2 顺带 |
| 30 | 同一内容 2-3 遍 SHA256 | AssetPipeline.cs:382-384 等 | 低 | 不修登记 | SIMD 后 ~0.2-0.5s CPU/690MB，L3 墙钟 1121ms 内 <5%，且 #3/#4 修复后哈希基数大降；重开触发器：#3/#4 修后 CPU 采样仍显著 |
| 31 | GenerateResponsiveAsync 每宽度整图重解码 | ImageProcessor.cs:105,119-131 | 低 | 不修登记 | 全仓无生产调用方（分片核证）；触发器：模板侧接线 |
| 32 | ToLogicalName 每渲染 GetFullPath 重复 | ScribanTemplateRenderer.cs:427-447,321 | 低 | 不修登记 | 0.5-2µs/渲染 vs 15µs 预算 3-13% 跨线；探针后可重开 |
| 33 | PageStoreObject.Get 列表整拷贝+重包装 | PageStoreObject.cs:81-89 | 低中 | 不修登记 | n 通常小，无负载面证据；随 #3 修复重评估 |
| 34 | Track 全局锁内 O(G) RemoveAll | TemplateResourceFunctions.cs:3685-3698 | 低中 | 不修登记 | G=每页生成资源数（几十级），临界区微小；争无证据 |
| 35 | GoPrintf 'd' 分支双重 Pad 冗余调用 | GoPrintf.cs:150 | — | 不修登记（非性能） | 外层恒 no-op，正确性可疑项移交 tech-debt 观察位 |

弃报对账（分片已评估不报，供下轮免重扫）：HtmlOutputNormalizer 双遍正则（<1-5%，与写盘天花板并行）；FeedGenerator OrderBy+Take（.NET 部分排序优化）；SitemapGenerator 逐页 Now（~1ms/万页）；RadixTree.WalkDfs key 拼接（含于 #8 机制链）；Render.cs 重复 regularPages（常数）；jsonify 每次 new options（微）；静态 Regex 缓存覆盖的低频函数族；ConfigMerge/ModuleManager/GitDateProvider/OutputDirectoryEnsurer（冷路径已优化）。

## 7. 停止条件对账与移交

- 出口形态：**A 批 2 项闭环 + B 批带探针数据移交 + 不做批 5 项带分解数据**——三态并存，逐条对照发现总表，无凭印象宣告。
- 深读负结论与撞表计数：本轮零撞表（分片 brief 直接引用 R7+ledger 全清单，35 条全部为新候选），下轮分片 brief 应并入本表 35 条。
- 未决项（移交 R7，单一定义点）：T1.12（#21）/T1.13（#3+#4+#5，本轮最高价值立项，附探针数据）/T1.14（#6）；T1.4 追加 #22；T5 组追加 #24-27/#29；T7 组追加 #28；#7 NeedsFullPipeline 需语料实测后归组。
- 遗留：仓库根 `public/`（14622 文件，探针构建产物，`-s` 时 `-o` 默认相对 CWD 的工具陷阱）——SWG-5 拦截 rm -rf，待用户确认删除：`rm -rf public`（或 `git clean -fd public/`）。G15 门禁该项因此暂红，清理后复跑即绿。
- 量具缺陷登记：flint build `-s <dir>` 时输出目录默认相对 CWD 而非源目录，易污染仓库根（本轮实测踩中）——建议 R7 T0 量具组登记修正。

## 8. 本轮元数据

- 分片消耗：5 agents（1 次模型失败原样重派成功），主审抽查 7 条。
- 时长：阶段 1 基准约 25 分钟（与深读分片并行）；探针四负载约 3 分钟。
- 下轮触发建议：T1.13 实施后跑 blowfish/fixit 墙钟对比（探针计数复用本轮方法）。

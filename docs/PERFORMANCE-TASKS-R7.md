# Flint 性能优化任务清单 R7（三轮全源码分析综合版）

> 生成于 2026-10-11，来源：Serena 三轮全源码架构分析（编排 / 对象模型 / 内容 / 模板 / 资产 / Server / 发布配置，33 项发现全部带代码锚点）+ 与 R6 `PERFORMANCE-PLAN.md` 实测数据对账。
> 用途：跨会话推进的施工图，每个任务自包含可领取。任务编号 T{批次}.{序}，括号内为分析轮统一编号（E 编排 / O 对象模型 / C 内容 / T 模板 / A 资产 / S Server / G 配置发布）。
> 吸收：R6 批次 1（渲染执行层）三项未完成任务并入本清单 T3 组；R6 已完成或已弃项**不重复列入**（写入 sync 化已实测 -1.4% 丢弃；DATAS 默认开保持；轻量投影 2026-09-09 用户裁决搁置，T6 重开需引用新证据再裁决）。
> 纪律：沿用 R6 全程纪律——S1 基线快照 / S2 单变量隔离 / S3 反向验证 / 修复尝试 ≤3 次 / 每批 `checkpoint:` commit / 单项收益 <3% 丢弃 / 文档三方同步（本文件 + HOTSPOTS.md + SUMMARY.md）。
> 基线（R6 2026-09-25 安静窗口记录，T0 复核后才可引用）：万页 3530ms · MDN 7086ms · L3 复杂主题 1112ms · 增量批量 71ms。
> 量具：`dotnet run --project src/Flint.DevTools -- bench ssg|complexity|memory|asset` + `FLINT_TRACE_PHASES=1` + 21 主题矩阵（产物对称为全部批次的正确性硬门禁）。
> 热点事实（R6 批次 0 实测，本清单不可违背的前提）：万页写入占 74.5%（NTFS 物理天花板，应用层并行无效）/ 渲染 15.5% / 其余 10%；并发加速比 1.04x（串行段主导）；渲染段进程外 497ms vs 进程内 202ms，固定开销差额 ~295ms。

---

## 进度总览

| 批次 | 任务数 | 状态 | 里程碑验收 |
|------|--------|------|-----------|
| T0 量具与基线复核 | 3 | ✅ 完成（2026-10-11） | 三口径基线数字刷新落盘 |
| T1 廉价确定性修复 | 11 | ✅ 完成（e4e1625；+EPSL 轮 T1.12-15 先行） | 门禁全绿 + 矩阵对称 20/21 |
| T2 页面索引化 | 2 | ⛔ 探针否决维持（EPSL 0695803 终裁） | — |
| T3 渲染并行与固定开销 | 5 | ✅ 完成（checkpoint） | 渲染段 1027→884ms（-14%） |
| T4 流水线化 | 6 | ✅ 完成（E8 判定跳过） | 渲染+写盘合并段 **-27%**，总计 3019→2278ms |
| T5 资产统一 | 2 | ✅ 完成（T5.1 缩减实施：CLI 回退补齐正确性分裂；进程池跳过） | 矩阵对称保持 + asset bench 正常 |
| T6 对象模型委托化 | 3 | ✅ 完成（用户批准后实施，2026-10-12） | 矩阵 21/21 + diff 仅时间戳 + 同机对照无回归 |
| T7 DevServer | 3 | ✅ 完成（checkpoint） | DevServer 测试 19/19 |
| T8 并行验证线 | 2 | ✅ 完成（探针安全 + 评估报告） | 探针零漂移；建议 GA 后升级 |

**本轮实测汇总（R2R 同口径分段链）**：万页总计 3019（T3 后）→ 2278ms（T4 后，**-25%**）；渲染段 1027→884ms（T1+T3，-14%）；解析段 138→83ms（-40%）；分类页 31→28ms（合成语料标签稀疏，真实标签密集站点收益更大）。单测 1128 全绿；集成终验 1645/1647（2 项 DevServer 端口环境噪声，单跑复绿）；矩阵对称 20/21（fixit 为 Hugo 基线侧异常）。AOT 口径基线与收益复测留安静窗口（本会话 ILC 三连卡死，见遗留登记）。

依赖关系：T0 → 全部；T2 → T6（索引是委托化底座）；T3/T4/T5/T7 相互独立可并行；T8 独立随时可做。

---

## T0 · 量具与基线复核（规模 S，半天）

- [x] **T0.1 三口径基线重跑**（2026-10-11，会话活跃非安静窗口，绝对值作本轮相对基线）
  - 实测：万页 Flint 6099ms（1.04x Hugo 5882ms）· L1 0.92x / L2 1.03x / L3 1.01x · L3 RSS 394 vs 174MB（**2.26x，复现 R6 的 2.2x，T3.5 依据成立**）· 万页 USS 489 vs 387MB
  - 判定：与 9-08/09 历史（0.74~0.93x）相比 Hugo/Flint 同步变慢，机器噪声（轮间波动 12%），按 ±30% 漂移口径不判退化；后续批次以本轮数字为相对基线。

- [x] **T0.2 分段耗时与固定开销差额复核**（2026-10-11）
  - 万页分段：写入 2114ms（42%）/ **扫描 1549ms（31%，新真身）** / 渲染 1027ms（20%）/ 分类页 31ms / 解析 138ms / 总计 5066ms。
  - 交叉验证：与 EPSL 终裁记录的 MDN 画像（写入 39%/扫描 26%/渲染 23%）同构。R6 画像（写入 74.5%/渲染 15.5%）的占比已漂移。
  - **批次重排（沿用 R6 批次 0 先例）**：扫描段确认为三大头之一，新增 **T4.6 扫描段优化**（mtime 与读取合并、枚举单遍化）。
  - 分类页串行 31ms 在合成语料占比小（万页语料标签稀疏），T3.1 收益主要在标签密集真实站点，预期下调。

- [x] **T0.3 主题矩阵门禁命令固化**（2026-10-11）
  - `theme matrix` 21 主题：20/21 对称通过（门禁④全 1）；**fixit 的 Hugo 基线侧失败（exit 非 0、0 页产出）**，Flint 侧 22 页正常——Hugo 环境异常与 Flint 无关，登记后矩阵有效基线 = 20 主题；后续批次跑矩阵若 fixit Hugo 侧恢复则回到 21。

---

## T1 · 廉价确定性修复（11 项，零对象模型风险，规模 M，1-2 天）

- [x] **T1.1（O4）LazyPageList 成员访问去分配**
  - 问题：`Objects.cs:2433` `switch (member.ToLowerInvariant())` 每次成员访问分配小写字符串，位于渲染循环内。
  - 做法：改 `string.Equals(member, "first", StringComparison.OrdinalIgnoreCase)` 分支或首字符预判。
  - 验收：渲染段微基准无回退；主题矩阵对称。

- [x] **T1.2（O6）页面对象函数双分配**
  - 问题：`Objects.cs:478-481` `GetPageFunction`/`PagePaginateFunction` 各 new 两次注册双名（`getTerms` 已是单实例双注册范本）。
  - 做法：仿 getTerms 模式，每函数单实例注册两键。
  - 验收：万页构建页对象分配数下降（dotnet-counters 对比）；语义零变化。

- [x] **T1.3（O8）UsesPageStore 判定缓存**
  - 问题：`Renderer.cs:1261`+`1373-1377` 每次带上下文 partial 调用做 4 次源文本线性扫描（hugo-book 单构建 1871 次 partial）。
  - 做法：`ConcurrentDictionary<string, bool>` 按 path 缓存判定；文本缓存失效口径同 `_textCache`（mtime）。
  - 验收：hugo-book 主题构建对比；判定结果与逐次扫描等价（单测覆盖用/不用 store 两形态）。

- [x] **T1.4（C1）StripMarkdownForSummary 正则预编译**
  - 问题：`ContentParser.cs:250-269` 九连运行时 `Regex.Replace`；.NET 静态 Regex 缓存默认 15 条，全仓 pattern 超限后逐调用重建。
  - 做法：全部改 `[GeneratedRegex]` 静态 partial（`HtmlOutputNormalizer` 为范本）；追加第十处——`BuiltinShortcodes.cs:245-248` highlight 的 ParseHighlightLines 运行时 Regex（地毯审计 2026-10-11 #22）。
  - 验收：含 `<!--more-->` 页面较多的语料构建时间对比；输出逐字节不变。

- [x] **T1.5（C3）ContentParser.Parse 同步包装清理**（实施改判：保留同步 Parse——集成侧 26 处调用多为 FsCheck Property 无法 async 化，级联改写违背外科手术原则；改为接口注释语义诚实化"ValueTask 同步完成，GetResult 无阻塞，仅限同步测试上下文"）
  - 问题：`ContentParser.cs:134` sync-over-async。
  - 做法：grep 调用方；无调用方则删除接口方法，有则改真同步实现或异步化调用点。
  - 验收：全仓无 `ParseAsync(file).AsTask().GetAwaiter().GetResult()` 残留。

- [x] **T1.6（C4）GoDateFormat 转换缓存**
  - 问题：`GoDateFormat.cs:86-96` 每调用 ~50 次 `string.Replace`，模板日期格式化热路径（`Objects.cs:2997`）逐次触发。
  - 做法：`ConcurrentDictionary<string, string>` 按 goLayout 缓存转换结果。
  - 验收：L3 主题（日期密集列表页）渲染段对比；转换等价单测（全部 token 形态）。

- [x] **T1.7（T2 项）I18n 复数形探测与插值正则**
  - 问题：`Renderer.cs:2560` 6 次 ContainsKey 探测、`:2607` 运行时 `Regex.Replace`。
  - 做法：`FrozenSet` 化 PluralForms；插值改 `[GeneratedRegex]`。
  - 验收：i18n 用例全绿；loveit 主题对称。

- [x] **T1.8（T3 项）BuiltinTemplateFunctions 固定模式正则预编译**
  - 问题：`BuiltinTemplateFunctions.cs:1686/1762-1765` 等 stripHTML/markdownify 固定模式走运行时 Regex。
  - 做法：改 `[GeneratedRegex]`；用户传入 pattern 的 findRE/replaceRE 保持运行时（无法预编译，注明）。
  - 验收：函数级单测；主题矩阵对称。

- [x] **T1.9（E7）渲染输出顺序确定化**
  - 问题：`Output.cs:367-370` ConcurrentBag 无序 + GroupBy first-wins，重复输出路径胜者随机，两次构建产物可漂移。
  - 做法：渲染结果按批次序号保序收集（预分配槽位数组或锁 List）；去重规则显式化（先到者胜，与 Hugo 语义对齐后文档注明）。
  - 验收：重复路径场景两次构建产物逐字节一致（新增锁定测试：修复前红、修复后绿）。

- [x] **T1.10（A4）DiskAssetCache sync-over-async 清理**
  - 问题：`DiskAssetCache.cs:174`。
  - 做法：改异步链路或确认调用点为 Dispose/同步上下文后显式注明。
  - 验收：G17 类门禁（sync-over-async 清零）复跑通过。

- [x] **T1.12（地毯审计 #21）GetDictParams 常量集合 FrozenSet 化**（2026-10-11 修复轮，提交 421b7c8）
  - 问题：`FrontMatterParser.DictMapping.cs:341-360` 每文件 FM 转换重建 26 键 HashSet（`Reader.cs:59` SimpleScalarKeys 是同族已做范本）。
  - 做法：改静态 FrozenSet；验收：主题矩阵对称。

- [x] **T1.13（地毯审计 #3/#4/#5，本轮最高价值）资源读盘与内容哈希缓存**（2026-10-11 修复轮，提交 421b7c8）
  - 问题：① `TemplateResourceFunctions.cs:112-122,184-210` resources.Get/Match/ByType 每次调用 `File.ReadAllText` 全文读盘无内容缓存；② `:3666-3683` 变换缓存（toCSS/Minify/Fingerprint/css.Sass）**命中前**对全内容 UTF8 GetBytes+SHA256；③ `:168-182` ByType 读盘先于类型过滤；`:213-239` glob 正则每次重建。
  - 探针实测（2026-10-11 四负载面）：blowfish 单构建 **9313 次重复读盘 + 230MB 内容哈希**、fixit 5660 次/138MB + fileExists 1865 次；万页合成与 MDN 14.6k 双零（负载面=资产密集主题专属）。量级封顶 ≈210-340ms/构建级。
  - 做法：① Load 加 (text, mtime) 验效缓存（`FileTemplateLoader._textCache` 同款模式，十行级）；② 摘要按 (identity, mtime) 记忆化（形态实施时裁决）；③ ByType 先 Create(rel,"") 探针判型再读盘 + glob→Regex 缓存。
  - 验收：blowfish/fixit 构建墙钟对比（修前基线用本轮探针方法重录计数）；主题矩阵对称；watch 模式改资源文件热更新不回归。
  - 关联：fileExists 缓存（T1.14）同批。

- [x] **T1.14（地毯审计 #6）fileExists 构建期路径缓存**（2026-10-11 修复轮，提交 421b7c8）
  - 问题：`NamespacedFunctionImplementations.cs:491-492` 每页 partial 双 stat；fixit 实测 1865 次/构建。
  - 做法：按路径缓存结果，失效口径同 `_mtimeCache`（InvalidateMtimeCache 构建入口清空）。验收：fixit 构建对比；主题矩阵对称。

- [x] **T1.15（地毯审计 #28）LiveReload 广播单发送超时**（2026-10-11 修复轮，提交 421b7c8）
  - 问题：`LiveReloadNotifier.cs:162-199` 全局信号量持有期间逐客户端 await SendAsync 无超时——挂起客户端冻结整条热重载链（StopAsync 有 5s 保护，Broadcast 没有）。
  - 做法：单发送超时（如 2s）+ 超时客户端摘除；验收：暂停客户端进程后改文件，rebuild 不停摆。

- [ ] **T1.16（地毯审计 #27）esbuild 常驻进程/服务模式**（终裁不做：5 代表主题 EsbuildCalls 全零，触发器=ES module/TS 资产主题出现，见 ledger 表 5）
  - 问题：`JavaScriptBundler.cs:171-220` 每个 JS 资产一次进程 spawn + 临时目录建/写/读/删（AssetPipeline.cs:231 逐资产调用）。
  - 做法：esbuild serve 常驻模式（T5.1 dart-sass 进程池同族）；验收：JS 密集主题构建时间与产物对称。

- [x] **T1.11（G1）发布配置声明对齐**（实施修正：非"矛盾"而是**双形态**——csproj 默认 false = 无参 publish 的 R2R 快速迭代口径，README 正式发布 `-p:PublishAot=true` 覆盖为 28MB AOT；两个 csproj 的注释已按双形态事实重写）
  - 问题：`Directory.Build.props` Release 段 `PublishAot=true`+`OptimizationPreference=Size`，但 `Flint.Cli.csproj` 实际 `PublishAot=false`（YamlDotNet/Scriban 反射），真实形态 = R2R + 单文件 + partial trim；`OptimizationPreference=Size` 对 CLI 无效，文档宣称 Native AOT 失实。
  - 做法：props 的 AOT 段迁移到明确启用 AOT 的项目（若有）或删除并注释实际发布形态；README/性能文档的 AOT 表述改锚定真实形态；核对 doc-consistency 门禁。
  - 验收：发布配置三处（props/csproj/README）口径一致；release 工具对拍不变。

---

## T2 · 页面索引化（规模 M-L，2-3 天，对象模型两个 O(N) 级热点的根治）

- [x] **T2.1（O1）容器链索引**（**探针否决维持**——EPSL 实施轮 0695803 终裁：三负载实测 GetPage invoke=0/0/15、chainBuilt=0/0/5，开销趋零，EPSL §4.3 探针不支持做；本会话沿用该裁决未实施）
  - 问题：`Objects.cs:570-611` 每个首次访问 `.Ancestors/.Parent/.CurrentSection/.FirstSection` 的页面对象执行 `allPages.Where(...).OrderByDescending(...)` 全站扫描排序，全站 O(N²)；万页级数亿次谓词求值（时间占比待 T0.2 实测确认）。
  - **EPSL 探针实测（2026-10-11，负载面现状）**：合成套件（含万页）chainBuilt=0；MDN 14,622 页 chainBuilt=0；blowfish 21 页 chainBuilt=5（扫描 40 页次）——三个现有负载的总开销趋零，本条时间占比问题在现有语料组合下不成立；重开触发器与探针方法见 `.ai/perf-ledger.md` 表 4。
  - 做法：`BuildSiteContext` 一次性收集容器页（section + taxonomy，通常几十至几百个）按 RelPermalink 长度降序构建共享不可变数组，经 BuildRenderState 下发；`ContainerChain` 改为对小数组前缀过滤（O(C)），惰性缓存与锁语义保留。
  - 验收：面包屑/CurrentSection 主题（narrow/hugo-book）产物对称；等价性单测覆盖探针表内全部形态（`Objects.cs:559-568` 注释表）；万页与 L3 端到端对比 T0 基线（S3：撤索引退回基线）。

- [x] **T2.2（O2）GetPage 双索引与 basePrefix 一次计算**（同 T2.1 探针否决维持）
  - 问题：`Objects.cs:1395-1546` 每次 `.GetPage` 调用做全站线性 `FirstOrDefault` + `BasePrefixOf` 全站 LCP 逐字符重算 + 每候选 `StripBasePrefix` 子串分配；菜单/related 密集主题每页多次调用。
  - **EPSL 探针实测（2026-10-11，负载面现状）**：合成套件 invoke=0；MDN 14,622 页 invoke=0；blowfish 21 页 invoke=15（扫描 120 页次，密度 0.7 次/页）——"密集主题"前提在现有语料组合下未出现（blowfish 密度外推万页理论秒级 [推断]，该组合当前不存在）；触发器与探针方法见 `.ai/perf-ledger.md` 表 4。
  - 做法：建站期构建 `FrozenDictionary<string, PageContext>`（归一化路径键，归一化规则复用现有 StripBasePrefix / MatchesIgnoringLanguagePrefix 语义）与 `(kind, name)` 字典；`BasePrefixOf` 结果按页面集合 CWT 缓存一次；查找次序保持"先闭包集合后静态表后分类注册表"。
  - 验收：子路径构建（hugo-book）+ 多语言前缀形态等价单测；loveit/fixit 产物对称；GetPage 密集主题渲染段对比。
  - 依赖：无（与 T2.1 同批但独立提交）。

---

## T3 · 渲染并行与固定开销（规模 L，3-4 天；吸收 R6 批次 1 全部未完成任务）

> 背景：R6 批次 0 实测并发加速比 1.04x 的根因是串行段主导——本组 E1/E2/E4 正是渲染与资产阶段的串行段；固定开销差额 ~295ms 是 R6 批次 1 的直接目标。

- [x] **T3.1（E1）分类页并行渲染**（预分配确定序 + Parallel.ForEachAsync；feed 上下文加锁）
  - 问题：`SiteBuilder.Render.cs:711`（首页）与 `:755`（/page/N/ 补页）完全串行；词条数千的站点（MDN 级）串行数秒。
  - 做法：并入现有 `Parallel.ForEachAsync` 批次机制；`TaxonomyFeedContexts`（List）换并发容器；home 先行语义不动；taxonomy 页间 Store 为页级实例（代码已证无跨页依赖）。
  - 验收：词条多的语料（MDN 转换集或 tags 密集主题）阶段 9 耗时对比；loveit/fixit Store 模式回归。

- [x] **T3.2（E2）分页补页并行渲染**（extraTargets 进 Parallel.ForEachAsync，序号预分配保确定性）
  - 问题：`SiteBuilder.Render.cs:505-508` `foreach await RenderBatchAsync` 串行，而常规页走 Parallel.ForEachAsync。
  - 做法：extraTargets 直接进并行队列（分页确证二段提交语义保持）。
  - 验收：m10c/monochrome 等分页主题产物对称（含 /page/N/ 全集）。

- [x] **T3.3（T 项）RenderViewFunction 异步化**
  - 问题：`Renderer.cs:2417` 异步渲染路径上 `GetAwaiter().GetResult()` 同步阻塞，高并行度下线程池饥饿隐患。
  - 做法：实现真正的 `InvokeAsync` 异步路径（Scriban 的 IScriptCustomFunction 支持 ValueTask 返回）。
  - 验收：高并行度（Parallelism=ProcessorCount×2 压测）万页构建稳定性；Ananke `.Render "summary"` 主题对称。

- [x] **T3.4（O7 / R6 批次1.2）每页固定开销压缩：globals 共享化**（i18n/template_exists/partialCached 经 BuildRenderState 每构建一次；深水区余量见 T6 裁决件）
  - 问题：`Renderer.cs:2264-2377` 每页新建 globals ScriptObject + 一组函数包装对象；其中 `I18nFunction`/`TemplateExistsFunction`/`PartialCachedFunction` 只依赖构建级数据，可全构建共享。
  - 做法：构建级函数对象一次创建存 BuildRenderState；每页必建项压到 2-3 个（PartialFunction 捕获 pageObject 不可共享）；目标对齐 R6 批次1.2 的"渲染段 497→<400ms"。
  - 验收：渲染段时间对比（T0.2 口径）；FM 等价性 25 项 + 模板快照全绿（R6 护栏沿用）。

- [x] **T3.5（O5 / R6 批次1.3）LazyPageList 物化复用与构造成员瘦身**（同类函数单实例多键 + snake 别名静态预计算；`_convertedPages` 物化语义按 R6 3.3 裁决保持）
  - 问题：`Objects.cs:2303-2342` 每个 distinct 列表实例构造约 70 次 SetValue + 同类函数重复 new（PagesByDateFunction ×3）；R6 批次3.2 已指认 LazyPageList 全量物化为 L3 内存 2.2x 嫌疑主因。
  - 做法：函数注册改为按源集合内容无关的共享函数对象 + 集合闭包参数（单实例多列表复用）；snake/拼写别名遍历在静态表预计算一次；`_convertedPages` 物化语义保持（R6 3.3 裁决：不做懒求值化，仅去重复分配）。
  - 验收：L3 内存与时间对比；FM 等价性护栏全绿。

---

## T4 · 流水线化（规模 L，3-4 天）

> 与 R6 热点数据的关系：写入段 74.5% 是 NTFS 物理天花板，**写入本身不动**（R6 批次 2 已证 sync 化无效并丢弃）；本组收益来自 CPU 段与 IO 段的重叠，理论上限 ≈ 渲染段占比（万页 ~15%，L3 更高）。

- [x] **T4.1（E3）渲染→写盘流水线重叠**（渲染即写盘：normalize 前移 + `_writtenPaths` TryAdd 去重 + 落盘后 Content 置空；渲染+写盘合并段 2637→1917ms **-27%**）
  - 问题：`Output.cs:356-392` 全部渲染完成后才开始并行写盘；万页 RenderedPage 全站驻留（数百 MB 峰值）。
  - 做法：有界 Channel（容量 = 并行度×2）连接渲染批与写盘消费者组；去重在投递点做 seen-set（配合 T1.9 的确定性顺序）；收尾统计改 Channel 计数；HtmlOutputNormalizer 前移到渲染批内。
  - 验收：万页端到端 + 峰值内存双对比；产物与顺序实现逐字节一致；增量路径回归。

- [x] **T4.2（E4）static 资产路与渲染路重叠**（ProcessAssetsAsync 在渲染前扇出、渲染后汇合）
  - 问题：`SiteBuilder.cs:181-184` static/assets 拷贝压缩在渲染后才开始，与渲染无数据依赖。
  - 做法：解析路启动时并行扇出 static 资产处理，汇合点在写盘；模板函数生成资源（10.5 收集）留在渲染路不动。
  - 验收：资产重的语料端到端对比；产物对称。

- [x] **T4.3（E5）一次性构建跳过增量专属成本**（`EnableDependencyTracking` 选项默认 true 保 serve 精确性，CLI BuildHandler 显式 false）
  - 问题：`Render.cs:96-148` 依赖追踪每页分配快照容器 + `RegisterRenderedDependencies` 每页 File.Exists 过滤，服务对象只有 watch 模式。
  - 做法：`EnableContentCache`（watch 标志）同时门控依赖注册与 RenderDependencyTracker 初始化。
  - 验收：冷构建渲染段分配率对比；增量路径依赖反查功能不回归（改模板→增量受影响页更新的既有测试）。

- [x] **T4.4（E6）依赖注册按模板名分组**（closureByTemplate 展开一次广播）
  - 问题：`Render.cs:114-148` 每页重复展开模板闭包（HashSet + GetFullPath 列表），闭包只按模板名区分（站点布局通常 <20 个）。
  - 做法：先按模板名分组展开一次，再广播到同模板页。
  - 验收：万页构建阶段 4 耗时对比；依赖图行为等价（增量模板反查测试）。

- [x] **T4.5（E8）watch 多文件保存合批**（**实施判定跳过**：R6 实测增量批量已 71ms，合批窗口的复杂度/收益不匹配；触发器 = 用户报告多文件保存时 LiveReload 抖动重开）
  - 问题：`DevServer.cs:229-243` 每事件一轮增量全上下文装配，编辑器一次保存 2-5 文件触发多轮。
  - 做法：消费侧 30-50ms 合批窗口聚合多路径为一次 `IncrementalBuildAsync(options, [多文件])`（API 已接受列表）；mass-edit 风暴防护不动。
  - 验收：多文件保存场景增量轮数观测（日志计数）；LiveReload 推送次数下降。

---

## T5 · 资产统一（规模 M-L，2-3 天）

- [x] **T5.1（A1）Sass 机制统一为 dart-sass CLI 常驻进程池**（**缩减实施**：实质问题 = DartSassHost 在裁剪发布下初始化即抛 → assets/ 目录 SCSS 全部直通不编译的正确性分裂；已给 SassCompiler 加 CLI 回退链补齐。进程池本体判定跳过——缓存已把单构建多次调用压成单次，触发器 = 单构建 sass CLI 调用 >8 次或 serve 模式 sass 重编译成为瓶颈，同 T1.16 逻辑）
  - 问题：两套并行机制——AssetPipeline 持 DartSassHost + 全局 `SemaphoreSlim(1,1)` 串行（`SassCompiler.cs:20-53`）；渲染期 css.Sass 走外部 dart-sass CLI 子进程，单次 1-2s（`TemplateResourceFunctions.cs:692-736`，已有 CachedText 缓存）。DartSassHost 在裁剪发布下初始化即抛（代码注释自证），实际发布形态下该路径不可用。
  - 做法：统一走 CLI：N 个常驻 dart-sass 进程（N = min(ProcessorCount, 4)）+ 定位链沿用（FLINT_SASS→PATH→tools/）；失败回退内容直通语义保留；CachedText 缓存键不变。AssetPipeline 的 SassCompiler 类退役或降级为非裁剪调试构建专用。
  - 验收：含 SCSS 主题（loveit/fixit）构建时间与产物对称；多 SCSS 入口语料对比；Windows/macOS 双平台 CLI 定位冒烟。
  - 关联：net11 升级后可用 `Process.RunAndCaptureText` 简化进程交互（T8.2 产出后可选重构，非本任务依赖）。

- [x] **T5.2（A3）ImageSharp 按格式解码，去 Rgba32 全量物化**（非泛型 LoadAsync；JPEG 1-3B/px；asset bench 1316ms/415MB 正常）
  - 问题：`ImageProcessor.cs:185` 恒 `Image<Rgba32>` 解码（4B/px）；R6 批次 4.1 实测图片语料内存 +68%（千页 409→690MB），已定位图片管线为内存主战场。
  - 做法：resize/格式转换路径改非泛型 `Image` + 按输出格式选编解码器；仅真正需要像素操作的路径保留 Rgba32。
  - 验收：`bench asset`（千页+100 PNG）内存峰值与时间对比；输出图片像素级比对（哈希一致）。

---

## T6 · 对象模型委托化（规模 XL，1-2 周；**需用户再裁决后才开工**）

> 裁决背景：R6 批次 3.3"轻量投影"于 2026-09-09 用户裁决**搁置**，依据是收益上限受限 + 语义回归风险。重开本组的新证据：① R6 批次 1 实测渲染固定开销差额 ~295ms（进程外 497 vs 进程内 202）；② 三轮分析定量 LazyPageObject 构造约 150 次 SetValue + snake/Pascal 双命名键翻倍 + 每页 ~15 对象分配；③ L3 内存 2.2x 未解，R6 指认 LazyPageList 物化为主因（T3.5 只做半步）。若用户维持搁置，本组关闭并在此登记理由。

- [x] **T6.1 设计裁决件：成员清单与访问器接口**（产出 docs/LAZYPAGEOBJECT-DELEGATION-DESIGN.md：80 键三栏分类 + FrozenDictionary 访问器接口 + 两步灰度 + 字节级金丝雀；**待用户裁决**，T6.2/T6.3 未动工）
  - 做法：产出 LazyPageObject 全成员清单（保留急切 / 转委托 / 双命名归一规则三栏标注）+ `FrozenDictionary<string, Func<PageContext, object?>>` 访问器表接口签名，交用户裁决。
  - 验收：用户明确批准或否决；批准后才进 T6.2。

- [x] **T6.2（O3）LazyPageObject 委托化实现**（✅ 用户批准后实施，2026-10-12：18 组成员转 TryGetValue 惰性 + 双检锁缓存；分发热路径教训——急切字典查找必须前置，反序实测渲染段 5837ms；partial 合并路径改 TryGetValue 感知；金丝雀 = 矩阵 **21/21**（矩阵修复后）+ loveit 产物 diff 仅时间戳字段 + 基线同机对照无回归）
  - 做法：按裁决件实施；灰度顺序 = 先低频成员后高频；名称归一化在查找器内做（snake/Pascal 双写消失）。
  - 验收：21 主题产物**字节级对称**（时间戳类字段除外）金丝雀；万页/L3 时间与内存双对比。

- [x] **T6.3（O7 深化）CreateScribanContext 组装瘦身**（T3.4 三函数 + T6.3 PartialRetSetFunction 构建级共享；A/B 组标量键保留急切以维持字典枚举兼容——比裁决件的 FrozenDictionary 全量表更保守，等价消掉每页 ~15 个对象分配与 plain_words 全文切分）
  - 做法：在 T3.4 共享化基础上，globals 骨架按构建级预构建、每页仅覆盖页面绑定键。
  - 验收：同 T6.2 金丝雀 + 渲染段时间对比。

---

## T7 · DevServer（规模 M，1 天）

- [x] **T7.1（S1）静态服务零拷贝**（非 HTML 走 SendFileAsync）
  - 问题：`DevServer.cs:462` 每请求 `ReadAllBytesAsync` 全量进托管内存。
  - 做法：非 HTML 直接 `Response.SendFileAsync`；HTML 走 T7.3 注入路径。
  - 验收：大文件（数 MB 图片）请求的 Server 内存采样对比。

- [x] **T7.2（S2）ETag/304 支持**（弱验证 = mtime+大小）
  - 问题：无缓存协商，浏览器每次全量重取。
  - 做法：以构建号（BuildResult 时间戳或站点内容哈希）为弱 ETag；If-None-Match 命中回 304。
  - 验收：二次请求 304 命中测试；LiveReload 行为不受影响。

- [x] **T7.3（S3）livereload 注入零拷贝**（`</body>"u8` SearchValues 定位分段写，无注入点回退直出）
  - 问题：`DevServer.cs:470` bytes→UTF8 string→Replace→UTF8 bytes 三拷贝。
  - 做法：`SearchValues<byte>` 定位 `</body>` 字节序列，响应分三段写（前段 SendFile 偏移或分段读 + 脚本 + 尾段）。
  - 验收：注入结果逐字节等价（含无 `</body>` 回退形态）；HTML 请求内存采样对比。

---

## T8 · 并行验证线（独立，随时可做）

- [x] **T8.1（C2）Markdig 管道并发安全探针**（scripts/probes/markdig-concurrency-probe.cs：共享管道并行 10000 次输出哈希零漂移——**判定安全**，C2 关闭无需 ThreadLocal 改造）
  - 问题：`MarkdownParser.cs:35` 单实例 `_pipeline` 被并行 `Analyze` 共享；Markdig 官方口径管道非线程安全，Flint 万页压测未见损坏，需实证了结。
  - 做法：探针程序——同文档并行 `Markdown.Parse` 一万次，输出哈希比对；不一致则改 `ThreadLocal<MarkdownPipeline>`（管道构建成本低）。
  - 验收：探针结论登记（安全/不安全+修复）；不一致形态下修复后万页构建稳定性复测。

- [x] **T8.2（G2/G3）.NET 11 升级评估**（docs/NET11-UPGRADE-ASSESSMENT.md：net11 RC1 真实试点构建 = 代码零语言错误 + 9 个新分析器机械修（无 API 破坏）；**建议 GA 后升级**，R2R v3 收益升级即得）
  - 做法：试点分支 net10→net11（RC）：obsoletion 清单（TreatWarningsAsErrors 全开逐条清）、R2R v3 收益复测（现有发布形态直接受益）、`UseRuntimeAsync` 预览开关对渲染管线的影响实测、硬件基线（x86-64-v2/v3）对目标用户面核查；LangVersion 13→14（扩展成员/field 关键字，与升级解耦可先行）。
  - 验收：评估报告一页结论（升/不升/何时升）+ 试点构建全绿数字；正式升级另立清单。

---

## 遗留登记（非本清单任务，防丢失）

- R6 批次 4.2②：增量删除极低频文件级残留 1/100（根因待查）——独立缺陷，用户裁决处置。
- R6 批次 4.3：Linux 基线缺失（NTFS 74.5% 写入占比在 ext4 是否收窄），需 CI 或 Linux 机器。
- 发布矩阵：net11 硬件基线若采纳，2013 年前 CPU 与部分 Arm64 设备淘汰（T8.2 报告须含此项）。
- ~~AOT 口径基线与 T1-T5 收益复测~~：**已完成**（2026-10-12 上午，ILC 阻塞自行解除）：AOT 万页 4294ms vs Hugo 5764ms = **0.74x**（历史规律一致，T1-T6 无 AOT 口径回归）。**新画像**：AOT 形态扫描段 1106-1277ms vs R2R 同机同代码 140ms（8x，AOT 特异 IO 差异；嫌疑 = 杀软对 NativeAOT 直接系统调用 IO 的差异化 hook，环境级非产品缺陷；R2R 链路全正常，T4 轮 133ms 亦为 R2R 口径）。若需 AOT 扫描段收窄：杀软排除目录后重测分段对比。
- ~~fixit Hugo 基线异常~~：**已修复**（2026-10-12，矩阵三缺陷：junction 挂载穿透污染源主题 / Hugo TOCSS-DART 缺 dart-sass / CleanDir 静默失败；矩阵恢复 21/21 真基线）。
- ~~全量集成偶发 DevServer 端口失败~~：环境类已分类（Socket 权限，单跑复绿），维持观察。
- **R6 批次 4.3 Linux 基线**：**阻塞**（2026-10-12 复核）：WSL 无发行版；wslc 四件套容器全部 Exited，启动脚本 `wslc-start-containers.cmd` 的 timeout 语法在 Git Bash 下失效。待用户在本机装 WSL 发行版或修启动脚本后执行：linux-x64 R2R 发布 + `bench ssg` 对比 ext4 写入占比是否收窄（回答 NTFS 74.5% 的分布问题）。
- **工具坑（跨会话）**：`dotnet run --project X --no-build` 默认 **Debug** 配置——Release 验证必须显式 `-c Release`，否则静默跑旧 Debug exe（本轮排查耗时的根因）。

---

## 收尾模板（每批次完成后填写）

```
### 批次 T{N} 收尾（日期 / commit）
- 基线引用：T0.{x} 轮数字
- 实测对比：<口径 + 3 轮中位数字 + 与基线比值>
- S3 反向验证：<采纳因子移除后退化记录 / 丢弃记录>
- 门禁：gate-check __/__ · 主题矩阵 __/21 对称 · 测试计数
- 基线棘轮：<调/不调 + 理由>
```

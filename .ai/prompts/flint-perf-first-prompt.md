# 任务：Flint 全面性能优化（性能优先）

> 版本：2026-10-27 ｜ 适用：`dev` 最新代码 ｜ 起点自检依赖：`.ai/prompts/fix-presence-probe.sh`
> 定位：性能是主目标；正确性与安全是门禁，必须过但不占预算。需要完整漏洞审计时改用审计版提示词。

起点：`dev` 最新代码。结论锚定 `git rev-parse HEAD`。

性能优先的含义：80% 预算投画像、候选与实施；正确性与安全是**门禁**（必须过），不作为本轮审计目标，也不占预算。

## 0. 交付

一个主账本加两份附属清单：

- 性能账本：基线、阶段画像、每项收益的 A/B 与反向验证、累计端到端收益。
- 不做清单：被否的候选，附分解数据与重开触发器。
- 覆盖账本：模块 / 文件数 / 行数 / 深度 / 比例 / 未覆盖原因。

结论三选一：

- P-A：确认 N 项收益，累计端到端 X%（附方法），逐项给证据。
- P-B：覆盖 X% 内无可回收项 ≥5%，盲区如下。
- P-C：未完成，阻塞如下。

无测量方法的百分比不进账本。分段计时只用于定位，量化收益必须端到端 A/B。

## 1. 门禁（必须过，不占预算）

1. 每次改动后 Core.Tests（`-parallelMode none`）、Tools.Tests、ThemeMigrator.Tests 全绿；集成套件在每个里程碑跑一次。
2. `--clean` 后两次构建 `diff -rq` 全等（时间戳、顺序、命名任一漂移即失败）。
3. 安全回归四探针：
   - 路径：`%2e%2e`、`..%20`、`..%2e`、`..%5c` 四形态均不得越出输出目录。
   - 供应链：篡改 `Flint.lock` 的 sha256 后重装必须被拒且不落文件；`mod get owner/repo@--upload-pack=x` 必须被拒。
   - 模板：`include "../../x"`、自递归 partial、空模板、超大输出不崩页面。
   - 产物：`XmlReader` 严格读落盘 sitemap 成功。
4. 禁止以性能为名跳校验、放宽限额、删守卫、删测试、删注释。性能改动导致任一门禁红，判 P0 回滚。
5. 实验放 `tmp/` 或 `git worktree`；`rm -rf` 只用 `tmp/` 绝对路径；dev server 按端口查 PID 收尾（`TaskStop` 不杀孙进程）。
6. 取证编码安全：CLI 中文输出是 936 码页，`grep 中文` 会假阴性，用 `cat`、`od -c` 或 PowerShell。

## 2. 基线

```bash
git rev-parse HEAD && git status --short
bash .ai/prompts/fix-presence-probe.sh    # 出现 ABSENT 说明起点未同步修复，先报告再决定是否继续
dotnet build Flint.slnx -c Release --no-incremental
dotnet build src/Flint.DevTools -c Release && dotnet build src/Flint.AiGate -c Release
dotnet run --project tests/Flint.Core.Tests -c Release --no-build -- -parallelMode none
dotnet run --project src/Flint.AiGate -c Release --no-build -- gate-check
dotnet publish src/Flint.Cli -c Release -r win-x64 --self-contained -p:PublishAot=true -o tmp/perf-aot
dotnet run --project src/Flint.DevTools -- perf gate
bash scripts/engine-ab.sh 300 3 "$PWD/tmp/perf-aot/Flint"
```

记录：退出码、警告数、测试计数、构建耗时、AOT 产物大小与发布耗时、perf gate 每项比值、engine-ab 两臂中位数与形态。

AOT 冒烟：`tmp/perf-aot/Flint build` 退出码 0、`--verbose` 可用、`head -c 60 public/sitemap.xml` 声明与实际字节一致。

基线不绿只报基线。集成套件单独后台跑，不与构建并发（时间断言会抖）。

## 3. 已知事实与勿重开

先读 `.ai/perf-ledger.md` 三表、`perf-budgets.md`、`scripts/perf-baseline.json`，逐条比对。

已有画像（历史轮，同口径才可比）：扫描 IO 约 15%，写盘占打包约 74.5%（NTFS 小文件天花板），渲染段主体是 Scriban 解释与写盘让出，解析段主体是 Markdig。

勿重开（附数据，详见 ledger 表 2）：同一内容多遍 SHA256、`GenerateResponsiveAsync` 每宽度重解码、`ToLogicalName` 的 `GetFullPath` 重复规范化、`PageStoreObject.Get` 列表整拷贝、`Track` 全局锁内 O(G) RemoveAll。

## 4. 画像

```bash
FLINT_TRACE_PHASES=1 tmp/perf-aot/Flint build   # 三次取中位
```

占比 >15% 的阶段才深挖。每阶段给一个可数的量：syscall 次数（stat/open）、读/写字节、分配字节、锁等待、串行段时长。

三负载缺一即标"未测"：`benchmarks/corpus/ssg-bench`（万页）、`corpus-merged`（真实语料）、`demo-sites` 21 主题或 `matrix20`（结构复杂度）。

## 5. 候选（按阶段，先估上限）

每项动手前写收益上限：次数 × 单次成本，或字节 ÷ 带宽。上限 <5% 直接进不做清单。

1. 扫描与解析
   - 每文件 syscall 数（stat + open + read）与目录枚举缓冲区利用。
   - front matter 快路径命中率（复杂键多时回退完整解析的比例）。
   - 本轮加固成本专项：每页一次字典大小写归一化、每页两次诊断字符串扫描（首行探针与 U+FFFD）、模块安装多一次哈希。先量化，再在不删检查的前提下优化（例如把两次扫描合并、把 U+FFFD 检查并入已有遍历）。
2. 渲染
   - Scriban 解释循环、partial 缓存命中率、成员访问策略（每成员 ToPascalCase 的分配与缓存）。
   - HTML normalize 的正则分段（是否每页两次扫描、能否单遍）。
   - 每页新对象数量与可复用的上下文对象。
3. 写盘
   - 渲染即写盘的让出点、`WriteFileWithRetryAsync` 的重试路径、`--cache` 跳过写盘的命中率。
   - 输出目录创建次数（`OutputDirectoryEnsurer` 的缓存是否覆盖全部路径）。
4. 并发
   - `Parallel.ForEachAsync` 的并发度是否与 IO/CPU 混合负载匹配；`AssetPipeline` 的信号量是否串行化热点。
   - 锁粒度：页面对象、taxonomy、分页注册表、`_writtenPaths`。
5. 缓存
   - 键完备性（配置、引擎、主题、平台、大小写）；失效点是否齐全。
   - 命中率实测（ContentHashCache、ContentCacheStore、DiskAssetCache、模板文本与编译缓存）。键不完备会以重复工作形式表现为慢。
6. 增量与 serve
   - 依赖图反查命中率、`FilterContents` 成本、feed/sitemap 是否需要每次全量重生成。
   - 构建边界 Clear 的成本（taxonomy feed 上下文、产物队列、partialCached）。这些是正确性保障，不得为了省时间去掉。
7. 启动与内存
   - 模板预编译耗时占比、AOT 产物大小与启动、单文件解压。
   - Server GC 已开；关注大对象与每页驻留、缓存上界。

## 6. 实施纪律

每项固定六步：

1. 基线：同会话 3 轮中位，记绝对值、形态（AOT 或 JIT）、语料。
2. 单点改动：只改一处。
3. 门禁全过（第 1 节）。
4. A/B：交错 A/B/B/A，报比值区间。
5. S3 反向验证：移除该改动应退回基线；不退回即归因错误，该项作废。
6. 记录：进性能账本，含上限估算与实际收益。

禁止：并行求和累计时间当墙钟；用分段收益代替端到端；在 JIT 形态宣称 AOT 收益；改完不测写下一条。

## 7. 分级

| 级别 | 定义 |
| --- | --- |
| P0 | 性能回归、破坏确定性、或门禁红 |
| P1 | 特定负载 ≥10%，风险可控 |
| P2 | 5% 到 10%，或较大重构 |
| P3 | 5% 以下，默认不做 |

条目格式：`file:line` + 改动摘要 + 基线/改后/比值（轮数、形态、语料）+ S3 结果 + 锁定测试 + 风险与回退。

## 8. 停止条件

1. 同一阶段连续 3 个候选无 ≥5% 收益，该阶段收口。
2. 累计端到端收益 <5%，或已达 `perf-budgets.md` 预算，停止并报告。
3. 任一 P0 出现，回滚并登记。

## 9. 输出

1. 起点（HEAD、分支、工作树状态）
2. 结论（P-A / P-B / P-C，一句话，含累计收益）
3. 基线表
4. 阶段画像（占比 + 每阶段 top 热点）
5. 收益清单（按收益排序）
6. 不做清单（含分解数据与重开触发器）
7. 覆盖账本
8. 未测清单与下一步

## 附录：量具与坑

量具：`DevTools -- bench ssg / perf gate / theme matrix`；`scripts/engine-ab.sh <页数> <轮数> <AOT CLI>`（打印构建形态）。

坑：`dotnet test` 不可用；`slnx` 只含 6 个工程；AOT 漏 `-p:PublishAot=true` 得到的是 R2R 单文件，体积与 AOT 不同且不可比；本机无 sass/lightningcss/esbuild/hugo；JIT 与 AOT 非渲染段可差 10 到 15 倍；NTFS 等长改写可能 mtime 不前进；杀软会锁新写文件。TFM 与 SDK 以 `global.json` 和 `Directory.Build.props` 为准，提示词里的体积与耗时数字只作量级参考，报告一律用本轮实测。

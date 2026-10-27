# Flint 审计与性能统一提示词（开关式）

> 版本 2026-10-27 ｜ 一个文件覆盖三种轮次：全景三轴、性能优先、单轴性能。
> 开工前先解析 §S 开关并输出"生效清单"，否则会做错轮次。

## S. 开关

| 开关 | 取值 | 含义 | 默认 |
| --- | --- | --- | --- |
| S1 模式 | `F` / `PF` / `P` | `F` 三轴：漏洞审计为主战场，加优化空间与性能；`PF` 性能优先：性能为主轴，正确性与安全作为门禁；`P` 单轴性能：只做性能，安全只作约束不审计 | `PF` |
| S2 深度 | `quick` / `standard` / `deep` | `quick` 单负载 + 前 5 个候选 + 不做 S3；`standard` 三负载取二 + 全候选 + 做 S3；`deep` 三负载 + 全候选 + 每项 S3 + 分配与 GC 探针 + 集成每改必跑 | `standard` |

解析后必须先输出三项，再动手：

1. 生效段落：`§0` 到 `§10` 哪些必做、哪些跳过。
2. 预算分配：`F` 安全 60 / 优化空间 15 / 性能 25；`PF` 安全门禁 10 / 优化空间 20 / 性能 70；`P` 性能 100。
3. 深度对应的语料与验证范围。

未输出生效清单就开工，视为流程违规。

段落适用性一览：

| 段 | F | PF | P |
| --- | --- | --- | --- |
| §1 门禁 | 全六条 | 全六条 | 第一至四条（探针只跑路径与产物） |
| §2 起点自检 | 必 | 必 | 必 |
| §3 基线 | 全量 | 全量 | 全量（`engine-ab` 可跳） |
| §4 漏洞审计 | 主战场 | 跳过 | 跳过 |
| §5 优化空间 | 必 | 必 | 必 |
| §6 性能实施 | 必 | 必 | 必 |
| §7 分级 | 两套表 | 安全表只用于门禁判定 | 性能表 |
| §8 反作弊 | 全 | 全 | 全 |
| §10 输出 | 三账本 | 性能账本 + 两清单 | 性能账本 |

## 0. 交付

| 模式 | 产出 | 结论枚举 |
| --- | --- | --- |
| `F` | 安全账本、性能账本、覆盖账本 | 安全 S-A / S-B / S-C；性能 P-A / P-B / P-C；优化空间 O-A / O-B / O-C，三轴各一句，不合并 |
| `PF` | 性能账本、不做清单、覆盖账本（安全只记录门禁结果） | P-A / P-B / P-C；O-A / O-B / O-C |
| `P` | 性能账本 | P-A / P-B / P-C |

共用证据纪律：

- 安全条目必须有复现路径，否则降 P2 标"推断"，不得写成 P0。
- 性能收益必须有测量方法（轮数、形态、语料），分段计时只用于定位，量化收益用端到端 A/B。
- 覆盖账本必写未覆盖文件与原因；没有覆盖数据的"没问题"无效。

## 1. 门禁

1. 测试：每次改动后 Core.Tests（`-parallelMode none`）、Tools.Tests、ThemeMigrator.Tests 全绿；`F` 与 `deep`、`PF` 每个里程碑跑集成套件。
2. 确定性：`--clean` 后两次构建 `diff -rq` 全等。时间戳、顺序、命名任一漂移即失败。
3. 安全回归探针（`F`、`PF` 全跑；`P` 只跑路径与产物两项）：
   - 路径：`%2e%2e`、`..%20`、`..%2e`、`..%5c` 四形态不得越出输出目录。
   - 供应链：篡改 `Flint.lock` 的 sha256 后重装必须被拒且不落文件；`mod get owner/repo@--upload-pack=x` 必须被拒。
   - 模板：`include "../../x"`、自递归 partial、空模板、超大输出不崩页面。
   - 产物：`XmlReader` 严格读落盘 sitemap 成功。
4. 禁止以任何名义跳校验、放宽限额、删守卫、删测试、删注释。导致任一门禁红，判 P0 回滚。
5. 隔离：实验放 `tmp/` 或 `git worktree`，主工作树保持可交付。`rm -rf` 只用 `tmp/` 绝对路径，被 SWG 拦截时交用户确认。
6. 收尾与取证：dev server 与测试子进程会锁 `Flint.Core.dll`，按端口查 PID 收尾（`TaskStop` 不杀孙进程）。CLI 中文输出是 936 码页，`grep 中文` 会假阴性，用 `cat`、`od -c` 或 PowerShell。

## 2. 起点自检

```bash
git rev-parse HEAD && git status --short
bash .ai/prompts/fix-presence-probe.sh
```

判读：`ABSENT` 非 0 说明起点未含本项目历次审计的修复。此时先报告，并按"修复前的状态"重新评估 §4 的现状列（那一列假定修复已在，失真会误导预算）。

## 3. 基线（不改文件，保留原始输出）

```bash
dotnet build Flint.slnx -c Release --no-incremental
dotnet build src/Flint.DevTools -c Release && dotnet build src/Flint.AiGate -c Release && dotnet build src/Flint.ThemeMigrator -c Release
dotnet run --project tests/Flint.Core.Tests -c Release --no-build -- -parallelMode none
dotnet run --project tests/Flint.Tools.Tests -c Release --no-build
dotnet run --project tests/Flint.ThemeMigrator.Tests -c Release --no-build
dotnet run --project src/Flint.AiGate -c Release --no-build -- gate-check
dotnet publish src/Flint.Cli -c Release -r win-x64 --self-contained -p:PublishAot=true -o tmp/perf-aot
dotnet run --project src/Flint.DevTools -- perf gate
bash scripts/engine-ab.sh 300 3 "$PWD/tmp/perf-aot/Flint"
```

记录：退出码、警告数、测试计数、构建耗时、AOT 产物大小与发布耗时、perf gate 每项比值、engine-ab 两臂中位数与形态。

AOT 冒烟（必做，抓测试形态覆盖不到的问题）：`tmp/perf-aot/Flint build` 退出码 0、`--verbose` 可用、`head -c 60 public/sitemap.xml` 声明与实际字节一致。

集成套件单独后台跑，不与构建并发。红项先分环境与代码，时间敏感用例单跑再定性；基线不绿只报基线。

## 4. 漏洞审计【仅 F】

信任边界只有一条：站点内容、主题、配置、模块、模板都可能由第三方提供，却会被当作代码执行（模板表达式、include、短代码、外部 CLI）。

按面判定；"现状"列假定起点已含既定修复，按它分配预算：`已加固` 只做回归，`部分` 补完剩余分支，`待深挖` 是主战场。

| 面 | 要回答的问题 | 现状 |
| --- | --- | --- |
| A 内容与 front matter | YAML 锚点炸弹、超深嵌套、超长行、CRLF 混合、伪造日期造成的静默错误或 DoS | 已加固（大小写、未闭合、非 UTF-8 有诊断） |
| B 路由与输出路径 | theme/contentDir/publishDir/alias/permalink/slug/taxonomy 词条能否越出站点根、覆盖他人产物、绕过大小写去重 | 部分（theme 校验、clean 守卫、alias 去重已做；编码与保留名未测） |
| C 模板沙箱 | 成员访问能否触达任意 .NET；`resources.Get`/include 是否根内校验；超大输出、深递归、`params` 注入 | 部分（include 根内校验与递归上限已做；成员策略白名单未审计） |
| D 钩子与短代码 | 是否新执行入口；参数被当路径或命令；短代码注册表能否被内容劫持 | 待深挖 |
| E 外部进程 | sass/lightningcss/esbuild/git/aws 的参数传递是否走 ArgumentList；PATH 劫持；stderr 死锁；无超时；子进程输出注入产物 | 部分（ModuleManager 已做；Assets 三件套未审计） |
| F dev server | Host 头、目录列表、`.git`/`.env`/`.flint-cache` 暴露、缓存头、WS 消息处理、绑定地址 | 部分（Origin 与路径守卫已做并实测） |
| G 模块供应链 | TLS 与重定向、镜像劫持、404 与 403 混淆、lockfile 新旧格式、重装一致性 | 部分（参数注入、超时、解压限额、哈希比对已做） |
| H 部署 | token 是否进命令行/日志/临时目录；netlify/vercel 传参；`s3 sync --delete` 范围 | 部分（gh-pages 语义与 `--force` 已做） |
| I 依赖与许可 | `NuGetAudit=false` 下的已知漏洞、许可风险（ImageSharp 3.x 锁定）、过时与 transitive 包 | 待深挖 |
| J DoS 与资源 | 用户可控正则的回溯（replaceRE、split）、超大步数、并行度耗尽、缓存无界增长 | 部分（递归与解压限额已做） |

条目格式：

```
[HYG-001] 标题
级别：P0/P1/P2   置信度：已复现 / 高 / 推断
控制点：攻击者能控制什么
到达点：file:line + 代码原文
后果：错误产物 / 越权读 / 命令执行 / 崩溃 / DoS
探针：可复现命令或最小输入
修复方向：一句话
```

## 5. 优化空间【F、PF、P】

五个来源，先估上限再动手：

1. 画像：`FLINT_TRACE_PHASES=1` 三次中位，占比 >15% 的阶段才深挖。
2. 类型学：syscall 次数（stat/open）、读写字节、分配字节、锁等待、串行段时长、GC 暂停，每类给可数的量。
3. 结构复杂度：O(n²)、每页重复计算、全站遍历、排序与查找表。
4. 负空间：重复哈希、重复 stat、重复 normalize、重复 parse、写盘后重读、死路径。
5. 缓存完备性：键是否覆盖配置、引擎、主题、平台、大小写；失效点是否齐全。键不完备会以"重复工作"的形式表现为慢。

加固成本专项（三种模式都做）：每页一次字典大小写归一化、每页两次诊断字符串扫描、模块安装多一次哈希、渲染钩子每调用一次 stat。先量化，再在不删检查的前提下优化（合并扫描、并入已有遍历）。

收益上限（次数 × 单次成本，或字节 ÷ 带宽）低于 5% 直接进不做清单并记理由。三负载缺一即标"未测"：`benchmarks/corpus/ssg-bench`、`corpus-merged`、`demo-sites` 21 主题或 `matrix20`。

## 6. 性能实施【F、PF、P】

每项六步：

1. 基线：同会话 3 轮中位，记绝对值、形态（AOT 或 JIT）、语料。
2. 单点改动：只改一处。
3. 门禁全过（§1）。
4. A/B：交错 A/B/B/A，报比值区间。
5. S3 反向验证（`quick` 可跳）：移除该改动应退回基线，不退回即归因错误，该项作废。
6. 记录：进性能账本，含上限估算与实际收益。

禁止：并行求和累计时间当墙钟；用分段收益代替端到端；在 JIT 形态宣称 AOT 收益；改完不测写下一条。

候选按阶段取，先估后做：

1. 扫描与解析：每文件 syscall 数、目录枚举缓冲区利用、front matter 快路径命中率、§5 的加固成本专项。
2. 渲染：Scriban 解释循环、partial 缓存命中率、成员访问策略的每次调用分配、HTML normalize 的正则分段、每页新对象。
3. 写盘：渲染即写盘的让出点、`WriteFileWithRetryAsync` 的重试路径、`--cache` 跳过写盘命中率、输出目录创建次数。
4. 并发：`Parallel.ForEachAsync` 并发度与 IO/CPU 混合度、`AssetPipeline` 信号量是否串行化热点、锁粒度。
5. 缓存：键完备性与命中率（ContentHashCache、ContentCacheStore、DiskAssetCache、模板文本与编译缓存）。
6. 增量与 serve：依赖图反查命中率、`FilterContents` 成本、feed/sitemap 是否需要每次全量重生成、构建边界 Clear 的成本（正确性保障，不得为省时间去掉）。
7. 启动与内存：模板预编译占比、AOT 体积与启动、大对象、每页驻留、缓存上界。

## 7. 分级与证据

| 级别 | 安全轴 | 性能轴 |
| --- | --- | --- |
| P0 | 正常路径可触发：越权读写、命令执行、错误产物、数据丢失、确定性崩溃 | 性能回归、破坏确定性、削弱安全 |
| P1 | 特定输入或并发下越权、泄漏、逃逸、契约违反 | 特定负载 ≥10%，风险可控 |
| P2 | 健壮性缺口，不静默出错 | 5% 到 10%，或较大重构 |
| P3 | 风格，不作为缺陷 | 5% 以下，默认不做 |

安全条目必含：`file:line`、控制点、到达点、后果、复现、置信度、修复方向。
性能条目必含：`file:line`、改动摘要、基线/改后/比值（轮数、形态、语料）、S3 结果、锁定测试、风险与回退。

## 8. 反作弊

- 先读 `.ai/perf-ledger.md` 三表、`perf-budgets.md`、`scripts/perf-baseline.json`，逐条比对；命中"明确不做 / 勿重开"必须引用其分解数据才可重开。
- 代码注释里的 P 定案、ADR、blame 显示的取舍先确认是契约再判缺陷。
- 同根因合并一条，列全部落点，不按出现次数刷数量。
- 子代理与工具结论必须一手复验。
- 不报风格，不把"理论上更快或更安全"写成收益或风险。

## 9. 停止条件

1. 同一模块连续 3 个候选无 ≥5% 收益，该模块收口。
2. `F`：十个攻击面各给出结论且六级门禁全绿，安全轴收口；`PF` / `P`：不做安全轴。
3. 累计端到端收益 <5%，或已达 `perf-budgets.md` 预算，性能轴收口。
4. 任一 P0 出现，回滚并登记。

## 10. 输出

| 模式 | 章节 |
| --- | --- |
| `F` | 起点 → 三轴判定 → 基线表 → 安全账本（按严重度）→ 性能账本（按收益）→ 优化空间清单 → 不做清单 → 覆盖账本 → 未测与下一步 |
| `PF` | 起点 → 判定（P / O）→ 基线表 → 阶段画像 → 收益清单 → 不做清单 → 覆盖账本 → 未测与下一步 |
| `P` | 起点 → 判定（P）→ 基线表 → 阶段画像 → 收益清单 → 未测与下一步 |

结尾附一句本轮的门禁结果（测试计数、确定性、探针），便于复核。

## 附录 A：探针

```bash
# 路径穿越（serve -p <port> --open false 后）
curl -s -o /dev/null --max-time 6 --path-as-is -w "%{http_code}\n" "http://127.0.0.1:<port>/..%5cFlint.toml"
# WS 来源（101 会挂住，必须 --max-time）
curl -s -o /dev/null --max-time 6 -w "%{http_code}\n" --http1.1 -H "Origin: https://evil.example" \
  -H "Connection: Upgrade" -H "Upgrade: websocket" -H "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==" \
  -H "Sec-WebSocket-Version: 13" "http://127.0.0.1:<port>/__livereload"
# XML / 确定性 / 时区
powershell -NoProfile -Command "[System.Xml.XmlReader]::Create('<abs>/public/sitemap.xml').Read()"
<CLI> build --clean; cp -r public /tmp/b1; sleep 3; <CLI> build; diff -rq /tmp/b1 public
printf 'timeZone = "Asia/Shanghai"\n' >> <site>/hugo.toml && <AOT CLI> build; echo $?
# 供应链（篡改哈希后重装应被拒）
sed -i 's/"sha256" = ".*"/"sha256" = "deadbeef"/' <site>/Flint.lock && <CLI> mod get <owner>/<repo> --version <tag>
```

## 附录 B：量具与语料

量具：`DevTools -- bench ssg / perf gate / theme matrix`；`scripts/engine-ab.sh <页数> <轮数> <AOT CLI>`（打印构建形态）。

语料：`benchmarks/corpus/ssg-bench`（万页）、`corpus-merged`（真实语料）、`demo-sites` 21 主题、`matrix20`（结构复杂度）。

## 附录 C：环境坑

`dotnet test` 不可用（一律 `dotnet run --project`）；`slnx` 只含 6 个工程，DevTools / ThemeMigrator / AiGate / Tools.Tests / ThemeMigrator.Tests 需单独 build 与 run；AOT 漏 `-p:PublishAot=true` 得到的是 R2R 单文件，体积与 AOT 不同且不可比；本机无 sass/lightningcss/esbuild/hugo；JIT 与 AOT 非渲染段可差 10 到 15 倍；NTFS 等长改写可能 mtime 不前进；杀软会锁新写文件导致"装好了报失败"。TFM 与 SDK 以 `global.json` 和 `Directory.Build.props` 为准，提示词里的体积与耗时数字只作量级参考，报告一律用本轮实测。

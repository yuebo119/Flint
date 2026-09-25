# Flint 工程规范

> 本文档是 Flint 的唯一规范入口，系统化记录散落在 `.editorconfig`、`Directory.Build.props`、代码注释与提交历史中的隐式规范。
>
> **执行口径声明**： Flint 的 GitHub Actions 仅含 `release-assets`（跨平台发布资产打包，**不承担验证职责**），验证类"机械强制"仍指本地构建期或测试期拦截。凡本文档标注 `[约定]` 的条目靠评审与提交纪律维持，标注 `[机械]` 的条目有确定性守卫。每条规范都经过当前代码库核验（2026-09-25），不写做不到的要求。

---

## 目录

1. [项目结构与分层](#1-项目结构与分层)
2. [编译策略与 AOT 硬约束](#2-编译策略与-aot-硬约束)
3. [代码规范](#3-代码规范)
4. [注释规范](#4-注释规范)
5. [命名规范](#5-命名规范)
6. [工程规范](#6-工程规范)
7. [测试规范](#7-测试规范)
8. [Hugo 兼容专项规范](#8-hugo-兼容专项规范本项目特有)
9. [文档规范](#9-文档规范)
10. [Git 提交规范](#10-git-提交规范)
11. [演示站与主题矩阵工作流](#11-演示站与主题矩阵工作流)
12. [提交前验证清单](#12-提交前验证清单)
13. [规范执行矩阵](#13-规范执行矩阵)

---

## 1. 项目结构与分层

### 1.1 解决方案结构

```
Flint/
├── src/
│   ├── Flint.Core/          # 引擎核心：解析/模板/渲染/站点构建
│   ├── Flint.Cli/           # 命令行入口：build/serve/new/deploy
│   └── Flint.ThemeMigrator/ # Hugo 主题 → Flint 主题转换器（Go→Scriban）
├── tests/
│   ├── Flint.Core.Tests/    # 引擎单元 + Hugo 兼容语义（主力，1120 项）
│   ├── Flint.IntegrationTests/  # 站点级端到端构建
│   ├── Flint.ThemeMigrator.Tests/ # 迁移器回归
│   └── Flint.PerformanceTests/   # BenchmarkDotNet 基准
├── scripts/                 # 演示站/画廊/主题矩阵/性能脚本（bash + python）
├── docs/                    # 工程文档（清单见 §9.1）
├── tools/                   # 外部工具：themes/（21 主题源）、hugo-bin/（参照真源）
└── Flint.slnx
```

### 1.2 依赖方向

```
Flint.Cli → Flint.Core → （零项目依赖）
Flint.ThemeMigrator → Scriban 解析（独立工具链）
Flint.Core.Tests → Flint.Core
```

- `Flint.Core` **零项目引用**，不引用 CLI/Migrator
- 引擎不知道迁移器的存在；迁移器不依赖引擎运行时（仅共享 Scriban 语法目标）
- 测试项目 1:1 映射 src，另加 PerformanceTests

### 1.3 目录内规范

- `src/*/` 按功能域分文件，不用 `Helpers/`/`Utils/` 模糊目录
- 大型子系统按目录聚合：`Flint.Core/Templates/`（模板函数/对象模型）、
  `Flint.Core/Site/`（构建管线按 partial class 拆分：Tree/Render/Output/Incremental）
- **partial class 拆分惯例**：单文件超 ~1500 行时按职责拆 `<Type>.<Aspect>.cs`
  （SiteBuilder 拆 5 个文件、BuiltinTemplateFunctions 按命名空间拆）——文件名
  必须是 `<主类型>.<方面>`，不新建无关文件

---

## 2. 编译策略与 AOT 硬约束

### 2.1 编译配置（`Directory.Build.props`，全项目继承）`[机械]`

| 配置项 | 值 | 说明 |
|--------|---|------|
| `TargetFramework` | `net10.0` | 单目标，**不多目标** |
| `LangVersion` | `13.0` | 锁定语言版本，不随 SDK 漂移 |
| `Nullable` | `enable` | 全项目可空引用类型 |
| `TreatWarningsAsErrors` | `true` | **零警告门禁** |
| `AnalysisLevel` | `latest-all` | 启用所有最新分析器规则 |
| `EnforceCodeStyleInBuild` | `true` | 样式规则进构建 |
| `NuGetAudit` | `false` | 禁漏洞审计阻断构建（定期人工审查，见 §13） |

csproj 只写 `<PackageReference Include="..." />`（无 Version——CPM，见 §6.1）
与 `<ProjectReference>`；公共属性一律由 `Directory.Build.props` 继承。`[约定]`

### 2.2 Native AOT 配置（Release）`[机械]`

```xml
<PublishAot>true</PublishAot>
<OptimizationPreference>Size</OptimizationPreference>
<IlcOptimizationPreference>Size</IlcOptimizationPreference>
<InvariantGlobalization>true</InvariantGlobalization>
```

发布链：`dotnet publish src/Flint.Cli -c Release -r win-x64 --self-contained -p:PublishAot=true`
（缺 `-p:PublishAot=true` 时 csproj 的 `PublishAot=false` 生效，产出 ~89MB 非 AOT 单文件）。
**任何引擎改动的验收终点是 AOT 发布成功且产物可用**——调试态通过 ≠ 交付态通过。

跨平台资产（linux-x64 / osx-arm64 / osx-x64）由 `.github/workflows/release-assets.yml`
打包上传：发布 Release 页（published）自动触发，或 `workflow_dispatch` 按标签手动触发；
win-x64 走上述手动命令。

### 2.3 零反射红线（热路径）`[约定+评审]`

Flint 的渲染热路径**不允许新增反射**：

- 模板函数一律 `ScriptObject.Import` 显式注册（lambda 或 `IScriptCustomFunction`），
  禁运行时 Reflection.Emit
- 类型分派用泛型约束/字典查找，禁 `MakeGenericType`/`Activator.CreateInstance`
- **例外（已评估并存档）**：Scriban 引擎自身基于反射（`IlcDisableReflection=false`
  是有意配置）；Hugo 兼容语义中少量 `reflect` 类函数是主题模板可见 API 面的
  组成部分，不是 Flint 自身实现路径。新增例外必须在此段登记理由

---

## 3. 代码规范

### 3.1 异步模式

- 引擎对外 API 用 `Task`/`ValueTask`；**热路径优先 `ValueTask`**（同步完成零分配）
- 所有 await 调用必须 `ConfigureAwait(false)`（库代码，不绑定同步上下文）
- 禁 `async void`（事件处理器用 `async Task` + 显式调度）
- 取消令牌 `CancellationToken` 一路透传到 IO 边界，**禁止**在中途静默丢弃

### 3.2 性能优先类型选择

| 场景 | 选择 |
|------|------|
| 只读集合入参 | `IReadOnlyList<T>` |
| 大文本拼装 | `StringBuilder`（禁 `+=`） |
| 字节缓冲 | `Span<byte>` / `stackalloc` |
| 词法/语法扫描 | 索引推进，禁 LINQ 逐字符 |
| 高频查找表 | `Dictionary` + 初始容量；只读后不再变更 |

Flint 的处理对象是**全站页面 × 页面成员**的规模（700+ 页/站），单页 O(n²)
会被放大成整站不可用——性能审查的默认口径是"这个成员访问会不会每页重算"。

### 3.3 null 处理

- 公共入口 `ArgumentNullException.ThrowIfNull`
- 模板对象模型一律 nil 安全（`?.` 链）——主题作者会写 `$x?.y?.z`，引擎必须
  对 null 中间节点返回 null 而非抛异常（Hugo 语义：nil 成员访问宽容）
- **内部调用链信任非 null，不重复检查**

### 3.4 异常纪律

- 异常不用于控制流；预期情况用 `Try*` 模式或返回值
- 模板渲染失败**分级处理**：单页渲染错误记入 `errors` 集合继续构建（Hugo 语义：
  一个坏 partial 不打整站），仅结构级错误（配置/目录）中止
- catch 必须筛选：`when (ex is not OperationCanceledException)`；吞异常必须注释理由

---

## 4. 注释规范

### 4.1 语言

- **注释全中文**——与用户语言一致；代码标识符英文
- 注释写"为什么"不写"做什么"；**每处非显然决策必须带依据**（Hugo 版本号、
  探针命令、受影响主题名、实测数字）

### 4.2 Flint 注释三件套（本仓库传统）

从既有代码归纳的标准形态：

```csharp
// 1. 结论式注释：先说做了什么，再说为什么这样做而不是那样做
//    ——Scriban 的 | 把左值注入首参，Go 模板注入末参，故管道改写必须显式换序
// 2. 实测锚点：标注发现该问题的主题与验证方式
//    （fixit 首页文章列表全空 → hugo.exe v0.166 对照产出此结论）
// 3. 历史否决记录：被否决的方案与否决理由（防止反复重试同一思路）
//    ——曾试图像操作直通字节数组，产生 PNG 边界撕裂，S3 反向验证后否决
```

### 4.3 XML doc

- 公共 API 必须有 `<summary>`（`GenerateDocumentationFile=true` + CS1591 在
  WarningLevel 9999 下无处可逃）
- XML doc 也用中文；`<param>`/`<returns>` 可英文
- 复杂决策用 `/// <para>` 分节，与 4.2 的行内注释二选一，不重复

---

## 5. 命名规范

| 元素 | 规范 | 示例 |
|------|------|------|
| 类型/文件 | 主类型名 = 文件名 | `TemplateResource.cs` |
| 接口 | `I*` 前缀 | `ITemplateRenderer` |
| 模板函数类 | `*Functions` | `BuiltinTemplateFunctions` |
| 测试类 | `*Tests`，按行为主题分文件 | `HugoCompatSemanticsTests` |
| 测试方法 | 中文描述性名称，下划线连接 | `WithPaginator_子路径baseURL不二次叠加前缀` |
| Scriban 成员 | snake_case（`title`/`rel_permalink`） | 模板侧一律小写 |
| Hugo 兼容层 | 保留 Hugo 拼写在注释中，代码用 snake | `GetPage` → `GetPageFunction` |

**测试方法名是 Flint 的特殊点**：不用 `Method_Scenario` 英文式，用中文全称。
理由：1120 个测试的失败输出就是缺陷清单，中文名直接可读、可裁剪进提交信息。

---

## 6. 工程规范

### 6.1 中央包管理（CPM）`[机械]`

```xml
<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
<CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
```

版本集中在 `Directory.Packages.props`；csproj 不写 `<Version>`。新增依赖前
先答"BCL 能不能做到"（本项目已因此拒绝多个"方便"包）。

### 6.2 脚本规范

`scripts/` 下两类脚本，各有纪律：

| 类型 | 语言 | 规范 |
|------|------|------|
| 构建/演示编排 | bash | Git Bash 可跑；`set -u`；端口/路径集中头部变量 |
| 数据处理 | python | 仅数据生成/校验（corpus、bench），不碰引擎 |
| 单进程服务 | .NET 10 | `demo-sites/demo-serve`（TcpListener，零依赖） |

**铁律** `[P0]`：不用脚本（sed/perl/heredoc）批量改写**既有** `.cs` 源码或
配置文件——绕过 Read 前置且无可审查差异。改动一律逐处 Edit。脚本只允许
生成**新**文件（脚手架/语料/基准数据）。

### 6.3 外部工具登记

| 工具 | 位置 | 用途 |
|------|------|------|
| Hugo 参照真源 | `tools/hugo-bin/hugo.exe` | **兼容性判定的唯一仲裁者**（见 §8.2） |
| 主题源 | `theme-migrator/themes/<name>/` | 21 个 Hugo 主题原始码（迁移项目内缓存） |
| Dart Sass | 随 CLI 发布 `dart-sass.win-x64/` | SCSS 编译（toCSS） |

新增外部工具必须在本表登记，并在 `global.json`/props 中固定版本。

---

## 7. 测试规范

### 7.1 框架与运行 `[机械]`

- **xUnit v3 + Microsoft.Testing.Platform**（不用 VSTest；`dotnet test` 直接可用）
- 运行单个测试项目：`dotnet run --project tests/Flint.Core.Tests -c Release`
- 过滤器：`-filter "/<程序集>/<类>/*"`（Git Bash 下需 `MSYS_NO_PATHCONV=1`）
- 测试数基线：**1120 通过 + 1 环境依赖跳过**（Core.Tests，2026-09-24 实测）；
  新功能不允许以"暂不加测试"合入

### 7.2 测试分层

| 层 | 位置 | 判定对象 |
|----|------|---------|
| 单元 | `Flint.Core.Tests` | 纯函数/对象模型（模板函数、词法器、URL 生成） |
| 兼容语义 | `Flint.Core.Tests/Templates/` | Hugo 行为等价（每个用例有 Hugo 依据） |
| 端到端 | `Flint.IntegrationTests` | 整站构建的结构完整性 |
| 迁移器 | `Flint.ThemeMigrator.Tests` | Go→Scriban 转换保真 |
| 性能 | `Flint.PerformanceTests` | BenchmarkDotNet，不发 CI（本地烟测） |

### 7.3 编写纪律

- **先复现后修复**：改 bug 前先写失败测试（红→绿），不接受"改完再说"
- 断言写**期望值**不写"非空/非 null"——弱断言发现不了行为漂移
- 外部真源对照的语义测试，注释里必须写明对照结果（hugo 版本 + 命令）
- 环境依赖测试（需 git clone/网络/Docker）标 `[SKIP]` 原因，**不许静默跳过**

---

## 8. Hugo 兼容专项规范（本项目特有）

Flint 的核心承诺是 Hugo 兼容。本章是与其他项目差异最大的部分。

### 8.1 兼容性判定的三层验证 `[铁律]`

任何 Hugo 兼容相关改动，必须按序通过三层，缺一层不得声称"已修复"：

```
① 构建层   dotnet build 0 警告 + AOT publish 成功
② 对照层   与 tools/hugo-bin/hugo.exe 对同一站点/模板产出对比（差异必须可解释）
③ 浏览器层 Playwright 打开产物，控制台零错误，关键 DOM 元素存在
```

**只过①的修复 = 未验证**。历史教训：js.Build "修好了"但浏览器仍报 import
错误（②③缺失）；toCSS "编译成功"但产物是未编译 SCSS（②只看文件存在与否）。

### 8.2 差异处理优先级

发现与 Hugo 的行为差异时，按序判断：

1. **Hugo 是仲裁者**——除非有 Flint 侧明确的架构理由（如 AOT 约束），
   行为向 Hugo 对齐；理由必须写进注释与 `docs/HUGO-COMPAT-MATRIX.md`
2. **不得为"让测试通过"而改测试期望**——先确认是 Flint 错还是测试过时
3. 已知差异登记在 `docs/FLINT-VS-HUGO.md`，**不接受静默差异**

### 8.3 主题矩阵验收

21 个主题是全量回归编队：

```bash
bash demo-sites/demo-sites.sh            # 全量重建 + 逐主题服务
bash scripts/theme-matrix20.sh           # 结构相似度矩阵
bash demo-sites/build-gallery.sh         # 画廊（8400）
bash demo-sites/demo-stop.sh             # 停止所有服务
```

新引擎改动**必须**至少跑 `blast radius` 内的主题（改动涉及的模板函数被哪些
主题使用）；宣称"全矩阵通过"必须附 21 主题的通过数。

---

## 9. 文档规范

### 9.1 文档清单与职责

| 文档 | 职责 |
|------|------|
| `README.md` / `README.en.md` | 项目门面：定位/特性/快速开始 |
| `docs/USAGE.md` | 用户使用手册（CLI/模板函数/配置） |
| `docs/API.md` | 引擎 API 参考 |
| `docs/CONVENTIONS.md`（本文） | 工程规范唯一入口 |
| `docs/HUGO-COMPAT-MATRIX.md` | Hugo 兼容矩阵（逐主题逐功能状态） |
| `docs/FLINT-VS-HUGO.md` | 已知差异登记册 |
| `docs/HUGO-GAP-TASKS.md` | 差距任务清单 |
| `theme-migrator/THEME-MIGRATOR-PLAN.md` | 迁移器设计与进度 |
| `docs/THEME-COMPAT-PLAN.md` | 主题适配进度 |
| `docs/PERFORMANCE-OPTIMIZATION.md` | 性能优化记录（按阶段） |
| `docs/PERFORMANCE-PLAN.md` | 性能计划 |
| `demo-sites/README.md` | 演示站/画廊/主题矩阵工作流 |
| `docs/PAGE-TREE-DESIGN.md` | 页面树设计 |
| `docs/AI-QUALITY-AUDIT.md` | AI 产出质量审计 |

新增文档需在此登记；文档命名全大写下划线（既有惯例）。

### 9.2 三方一致（代码-文档-注释）

任何行为变更，同一次提交内必须同步：`[机械-grep]`

- 代码改了什么，`docs/HUGO-COMPAT-MATRIX.md`/`FLINT-VS-HUGO.md` 的状态位
- 提交模板函数/命令变更 → `docs/USAGE.md` 或 `docs/API.md`
- **验证方式**：`grep` 变更前的旧事实值（旧签名/旧数字/旧类名），
  代码+文档+注释零残留方可提交

### 9.3 已知问题登记

修不了的/暂不修的（环境依赖、上游缺陷）写进对应文档的"已知事项"段，
**禁止口头流传**——跨会话恢复只认文档。

---

## 10. Git 提交规范

### 10.1 提交信息格式

```
类型：中文描述（可带括号补充关键数字/主题名）
```

类型取自历史实际使用集合：`feat`（新功能）/ `fix`（修复，英文风格存量）/
`修复`（修复）/ `feat：`（新功能，中文冒号）/ `perf`（性能）/ `优化` /
`文档` / `重构` / `回退` / `环境` / `新增` / `演示站` / `画廊` / `门禁`。

正文要求 `[约定]`：

- **修了什么 → 之前会怎样**（一句话说完触发语义）
- 改动超过 3 个文件时列出根因清单
- 提交前跑 §12 验证清单（不写"未验证"提交，除非标注原因）

### 10.2 分支模型（2026-09-24 起为三层流程）

```
main      ← 发布分支：只从 dev 合并，不接受日常提交（含直接 push）
dev       ← 开发主分支：feature 的合并目标，日常集成分支
feature/* ← 功能/修复分支：从 dev 创建，完成后合并回 dev
```

**确立记录**：2026-09-24 由用户裁决从"单主干直接提交"升级为三层流程
（`37103be` 之前的 231 个提交均在单主干模式下产生，历史不回改）。
升级触发条件按原约定是"第一个外部协作者出现时"——本次为用户主动前置。

**日常开发流**（新功能/修复）：

```bash
git checkout dev && git pull origin dev
git checkout -b feature/xxx          # 分支名：feature/ 或 fix/ 前缀 + 简短英文
# ... 开发、按 §12 验证、按 §10.1 提交（一个功能多个 commit 也可以）...
git checkout dev && git merge --no-ff feature/xxx   # 保留功能上下文
git branch -d feature/xxx
git push origin dev
```

单人高频小步可跳过 feature 分支直接提交 dev（本仓库常态）；预计会有协作者
review 或需要 CI 兜底的改动，**必须**走 feature 分支 + PR。

**发布流**（仅发版时执行，需用户明确指示 · 端到端五步）：

```bash
# 0. bump（在 dev 上提交）：只改 Directory.Build.props 的 <Version>（单源，
#    FlintInfo/四平台产物版本自动跟随）；docs 中的当前版本号示例同步，grep 零残留
git checkout dev && git pull origin dev
#    …编辑 props 与示例 → 按 §10.1 格式提交
# 1. 跑 §12 验证清单（build / Core.Tests / AOT 发布 + version 冒烟核对输出）
# 2. 合并推送（fast-forward 保持线性）
git checkout main && git pull origin main
git merge dev && git push origin main && git push origin dev
# 3. 打 tag：名称 = v + props <Version>（如 0.2.0 → v0.2.0）
#    （步骤 2 合并后位于 main，tag 必须打在 main 的合并提交上）
git tag -a vX.Y.Z -m "<一句话摘要>" && git push origin vX.Y.Z
# 4. 建发布页 + 等四平台资产（published 事件触发 release-assets 流水线；
#    流水线从默认分支 main 读取——因此步骤 2 的合并必须先于建页，否则跑的是旧工作流）
python scripts/release-github.py --tag vX.Y.Z --notes notes.md
#    notes.md = 总结式变更日志：主要特性 / 主要更新 / 主要更改
# 5. 终验
python scripts/release-github.py --verify
#    （标题纯版本号 / assets=4 / label 全空 / 远端 tags 一致）+ 四平台 version 冒烟
```

**场景补充**：
- **补建资产**（发布页已在、资产缺失）：`python scripts/release-github.py --tag vX.Y.Z --dispatch`
- **撤销发布**（须用户明确指示）：删 release 页 → 删 tag（远端+本地）；已发布 tag 永不移动、永不重打

**硬规则** `[P0]`：

- ❌ 禁止直接 push/提交到 `main`（发布只经 dev 合并）
- ❌ 禁止 `git push --force`、`git reset --hard` 无确认执行
- ❌ 已发布 tag 不可移动/重打（要改就升版本号重发）；删除 release/tag 须用户明确指示
- ✅ `dev → main` 合并 = 发布动作，必须**用户明确确认后**才执行（AI 不得自主合并）
- ✅ tag 命名 = `v` + `Directory.Build.props` 的 `<Version>`；发布页标题 = 纯版本号（不带后缀）
- ✅ 变更日志为总结式（主要特性/主要更新/主要更改），禁止 git 日志直贴与 commit 链接；
  资产命名 `Flint-<tag>-<rid>[.exe]` 且不带 label（页面直显文件名）
- ✅ feature 分支合并后删除；dev 与 main 是常驻分支，**绝不** `-D` 删除

**GitHub 侧设置**（仓库管理员手动，一次性）：

- Settings → Branches：`main` 添加 protection（require PR, require CI 当 CI 建立后）
- Settings → General：✅ Default branch = `main`（2026-09-25 用户裁决——release 事件固定从
  默认分支解析工作流，故默认分支必须为 main；发布流**先 dev → main 合并、再从 main 建 release**，
  流水线随合并进入 main 后被触发读取）

### 10.3 提交粒度

- 一个 commit = 一个可独立回滚的变更（引擎修复 + 其回归测试 + 文档同步）
- 不把无关改动混入同一 commit
- 长会话每完成一个可验证里程碑立即提交（防上下文丢失）

---

## 11. 演示站与主题矩阵工作流

演示站既是交付物也是**回归编队**（§8.3）。要点：

### 11.1 服务拓扑（2026-09-24 起）

```
http://127.0.0.1:8400/   画廊（21 主题入口）
http://127.0.0.1:8401/   ananke（端口 = 8400 + 主题序号，映射见 demo-sites.sh）
...
http://127.0.0.1:8421/   yinyang
```

- **每主题独立端口**（用户裁决 2026-09-23）：SSG 主题是站点级能力，曾经尝试的
  单端口子路径归并方案因根命名空间撞车/同源存储共享被否决，实现保留在
  git 历史（`2c8fc81..ead327f`），教训登记于 cortex 决策记忆
- **服务是单进程**（`demo-sites/demo-serve/`，.NET 10 TcpListener 手写最小 HTTP，
  直启 apphost 实测 22 端口 **22MB**，与 python 版持平）——2026-09-25 全面替代
  python 版：22 个解释器 490MB → 单 python 进程 21MB → .NET 单进程 22MB；
  教训：经 `dotnet run` 启动会多挂 ~160MB 宿主父进程（曾误测为 145MB），
  脚本已改为 build 后直启 `demo-serve.exe`
- `demo-stop.sh` 停全部服务并清理僵孤进程；Windows 下重建前**必须先停**
  （解释器 CWD 锁 public/ 目录）

### 11.2 两大项目位置（2026-09-25 起入库；生成物不入库）

| 路径 | 内容 |
|------|------|
| `demo-sites/<主题>/` | 案例站项目：各主题站点源（`public/`、`themes/` 构建生成，gitignore） |
| `demo-sites/gallery/` | 画廊站（`static/shots/` 下 21 张预览图**入库**，勿整删；`public/` 不入库） |
| `theme-migrator/themes/` `candidates/` | 迁移项目主题语料缓存（可再生，gitignore） |

---

## 12. 提交前验证清单

按改动范围选跑，**引擎改动 = 全跑**：

```bash
# 1. 构建（0 警告 0 错误）
dotnet build Flint.slnx

# 2. 全量测试
dotnet run --project tests/Flint.Core.Tests -c Release

# 3. AOT 发布（引擎改动时）
dotnet publish src/Flint.Cli -c Release -r win-x64 --self-contained -p:PublishAot=true

# 4. Hugo 兼容改动：跑 §8.1 三层验证（对照 + 浏览器）

# 5. 主题矩阵（模板函数/渲染链改动）
bash demo-sites/demo-sites.sh      # 至少 blast radius 内主题

# 6. 文档同步（§9.2 的 grep 零残留）

# 7. git status 审查：只暂存本次任务相关文件
```

**验证失败时**：停止修改 → 报告错误 → 分析原因 → 重试（≤3 次换方案）→
仍失败则带着完整错误信息报告用户。禁止"看起来应该没问题"式提交。

---

## 13. 规范执行矩阵

| 规范 | 执行手段 | 强制级别 |
|------|---------|:--------:|
| 零警告 | `TreatWarningsAsErrors` + `AnalysisLevel=latest-all` | 构建期 `[机械]` |
| 单 TFM / C# 版本锁定 | `Directory.Build.props` | 构建期 `[机械]` |
| AOT 发布 | `PublishAot`+ 手动 publish 命令（win-x64）· `release-assets` Actions（linux/osx 资产） | 人工 `[约定]` + Actions `[机械]` |
| 热路径零反射 | 评审 + §2.3 例外登记 | 评审 `[约定]` |
| CPM 版本集中 | `ManagePackageVersionsCentrally` | 构建期 `[机械]` |
| 禁脚本批量改源码 | 提交审查（P0 纪律） | 评审 `[约定]` |
| 测试基线 1120+1 | Core.Tests 面板实测 | 提交前必跑 `[约定]` |
| Hugo 兼容三层验证 | §8.1 流程 | 评审 `[约定]` |
| 主题矩阵回归 | `demo-sites.sh` / `theme-matrix20.sh` | 提交前必跑 `[约定]` |
| 三方一致 | `grep` 旧事实值零残留 | 提交前必跑 `[约定]` |
| 提交信息格式 | §10 类型清单 | 评审 `[约定]` |
| 演示站单进程服务 | `demo-sites/demo-serve` | 脚本内建 `[机械]` |
| NuGet 漏洞审查 | `NuGetAudit=false`，人工定期 | 人工 `[约定]` |

**升级路径预告**：分支三层流程已于 2026-09-24 落地（§10.2）。下一级升级触发
条件：月提交 ≥50 或协作者 ≥2 时，把本表中 `[约定]` 的验证清单迁入 CI，并对
`dev` 启用分支保护——在那之前，纪律即门禁。

---

> **规范的元规则**：本文档的每条都必须能在代码库中找到对应实践；找不到实践
> 的规范删除，实践与规范冲突时改规范或改代码但**必须同步**。规范先于规模是
> 浪费，规模先于规范是债务——本文档按当前规模（单人、138 文件、1120 测试）
> 定制，不预支未来。

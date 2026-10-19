# Fluid 主力化迁移方案

| 项目 | 内容 |
|---|---|
| 日期 | 2026-10-19 |
| 状态 | 提案 |
| 相关提交 | `1a6e31f` 已接入 Fluid 作为 `.liquid` 备用分支 |
| 目标 | Fluid 做新站点和内置模板主力，Scriban 长期只做 Hugo 兼容层 |

## 一、方案总纲

### 1. 目标

- 新站点默认使用 Liquid/Fluid 模板。
- Flint 内置回退模板、内置 partial、分页、SEO 元信息等默认走 Fluid。
- Hugo 主题、ThemeMigrator 产物、现有 `.html` 模板继续走 Scriban。
- 两条引擎在渲染层平行，不共享缓存、依赖表、资源追踪器。
- 用站点级 BenchmarkDotNet 证明 Fluid 路径有真实收益，再逐步扩大。

### 2. 非目标

- 不在本方案中把现有 Hugo 主题自动转成 Liquid。
- 不一次性重写所有 Scriban 函数平面。
- 不在第一阶段迁移短代码、render hooks、Hugo Pipes 资源函数。
- 不删除 Scriban，直到 Hugo 兼容策略明确退役。

### 3. 引擎路由规则

推荐按“模板物理扩展名 + 内置模板提供者”双轨路由：

| 模板来源 | 引擎 |
|---|---|
| 新站点 `layouts/**/*.liquid` | Fluid |
| Flint 内置回退模板，新站点/Minimal 站点 | Fluid |
| Hugo 主题 `layouts/**/*.html` | Scriban |
| ThemeMigrator 输出 `.html` | Scriban |
| 显式 `engine: scriban` 配置 | 全站 Scriban，兼容模式 |
| 显式 `engine: fluid` 配置 | 全站 Fluid，实验模式 |

路由落在 `TemplateEngineRouter : ITemplateRenderer`，`SiteBuilder` 不改。

### 4. 架构图

```mermaid
flowchart LR
  A[SiteBuilder] --> B[TemplateEngineRouter]
  B -->|.liquid / 内置新模板| C[FluidTemplateRenderer]
  B -->|.html Hugo 主题模板| D[ScribanTemplateRenderer]
  C --> E[Fluid TemplateContext]
  D --> F[Scriban TemplateContext]
  E --> G[PageContext / SiteContext / Params]
  F --> G
```

### 5. 隔离硬规则

Fluid 热路径禁止出现：

- `Scriban.Template`
- `ScriptObject`
- `BuiltinTemplateFunctions` 静态调用
- Scriban 模板缓存、依赖缓存、资源追踪器

允许共享：

- `PageContext` / `SiteContext` / `Params` 输入模型
- 模板查找顺序、主题叠加语义
- 输出写盘、指纹、SRI
- 短代码/render hooks 服务接口，但默认实现可仍为 Scriban

## 二、架构改动点

### 1. 新增引擎路由

当前 CLI 构造点很集中：

- `src/Flint.Cli/Handlers/BuildHandler.cs:103`
- `src/Flint.Cli/Handlers/ServeHandler.cs:56`

把这两个 `new ScribanTemplateRenderer(...)` 换成 `new TemplateEngineRouter(fluidRenderer, scribanRenderer, enginePolicy)`。

`SiteBuilder` 继续只依赖 `ITemplateRenderer`。

### 2. 内置模板提供者抽象

当前内置模板硬编码在 `ScribanTemplateRenderer.BuiltinTemplates`，`FileTemplateLoader` 也直接读它。需要拆成：

```csharp
interface IBuiltinTemplateProvider
{
    bool TryGetTemplate(string name, out string content);
    IReadOnlyCollection<string> Names { get; }
}
```

实现：

- `ScribanBuiltinTemplateProvider`：包住现有 `BuiltinTemplates`
- `FluidBuiltinTemplateProvider`：新的 Liquid 版内置模板

`FileTemplateLoader` 和 renderer 都通过提供者取内置模板，不再直接依赖 Scriban 类。

### 3. Fluid 渲染器第一版能力

`FluidTemplateRenderer` 第一版只要能安全渲染新站点最小模板：

- 模板加载与查找
- 模板缓存与 mtime 失效
- 依赖追踪
- `GeneratedResources` 输出接口先返回空
- UTF-8 输出
- 每渲染独立 `TemplateContext`
- 模型直接绑 `PageContext` / `SiteContext` / `Params`
- `Params` 用 `StringComparer.OrdinalIgnoreCase`

### 4. 新站点脚手架 Liquid 化

当前 `SiteCreator` 直接写 Scriban 语法 `.html` 模板。需要新增一套 Liquid 版脚手架：

- `layouts/index.liquid`
- `layouts/_default/baseof.liquid`
- `layouts/_default/single.liquid`
- `layouts/_default/list.liquid`
- `layouts/_default/taxonomy.liquid`
- `layouts/_default/term.liquid`
- `layouts/partials/head.liquid`
- `layouts/partials/header.liquid`
- `layouts/partials/footer.liquid`

同时保留旧 `.html` 生成能力给兼容模式，或加 `Flint new site --engine fluid|scriban`。

## 三、分阶段任务清单

### Phase 0：基线与契约

- [ ] **P0-1** 抓当前 Scriban 站点级性能基线：新站点、21 主题代表、万页语料各一组。
- [ ] **P0-2** 固化渲染段、解析段、扫描段、写盘段占比，作为后续收益判据。
- [ ] **P0-3** 定义 `TemplateEnginePolicy`：`auto` / `fluid` / `scriban`。
- [ ] **P0-4** 定义引擎隔离规则，写入 `docs/CONVENTIONS.md`。
- [ ] **P0-5** 增加装配验证测试：Fluid 渲染路径不得引用 Scriban 程序集。

验收：基线数字可复现；策略枚举和隔离规则入库。

### Phase 1：Fluid 渲染器垂直切片

- [ ] **P1-1** 新增 `FluidTemplateRenderer : ITemplateRenderer`。
- [ ] **P1-2** 实现 Liquid 模板加载、查找、缓存、mtime 失效。
- [ ] **P1-3** 实现依赖追踪，保证增量构建不漏重渲。
- [ ] **P1-4** 实现模型绑定：`page` / `site` / `params` 直接进 `TemplateContext`。
- [ ] **P1-5** 实现 `partial` / `include` 的 Liquid 版，映射到 Flint 模板加载器。
- [ ] **P1-6** 新增 `TemplateEngineRouter`，先只路由一个内置模板到 Fluid。
- [ ] **P1-7** 补 Fluid 单元测试：缓存、依赖、partial、大小写不敏感成员、空值语义。
- [ ] **P1-8** NativeAOT 冒烟：Fluid 路径在 AOT 下可渲染。

验收：一个内置模板由 Fluid 渲染，其余不变；单测、集成、AOT 冒烟全绿。

### Phase 2：新站点默认 Fluid

- [ ] **P2-1** 抽象 `IBuiltinTemplateProvider`，拆分 Scriban/Fluid 两套内置模板。
- [ ] **P2-2** 用 Liquid 重写最小内置模板：`list` / `term` / `taxonomy` / `pagination`。
- [ ] **P2-3** `SiteCreator` 增加 Liquid 脚手架，新站点默认生成 `.liquid`。
- [ ] **P2-4** `Flint new site` 端到端测试：默认新站点不加载 Scriban 引擎。
- [ ] **P2-5** 新站点构建冒烟：首页、单页、列表、分类、RSS。
- [ ] **P2-6** 性能 A/B：同内容新站点，Scriban 版 vs Liquid 版，同机三轮中位数。
- [ ] **P2-7** 产物对称性审计：两种引擎产出文件数量与关键内容一致。

验收：`Flint new site` 默认走 Fluid；A/B 显示渲染段收益；现有 Hugo 主题矩阵不回归。

### Phase 3：内置模板全面 Liquid 化

- [ ] **P3-1** Liquid 化 Hugo 常用内置 partial：`opengraph`、`schema`、`twitter_cards`、`google_analytics`、`disqus`。
- [ ] **P3-2** Liquid 化 `_funcs/get-page-images`。
- [ ] **P3-3** Liquid 化 `rss.xml`、`sitemap.xml`、`robots.txt`、`404.html`。
- [ ] **P3-4** 统一内置模板查找：Fluid 站点只拿 Fluid builtin，Scriban 主题只拿 Scriban builtin。
- [ ] **P3-5** 补内置模板逐字节对比测试或语义对比测试。

验收：新站点零 Scriban 内置模板依赖；Hugo 主题仍用 Scriban builtin。

### Phase 4：函数与资源平面扩展

- [ ] **P4-1** 梳理 Scriban 侧 25 个命名空间函数，按新站点使用频率分级。
- [ ] **P4-2** 实现 Liquid 版核心 filters：`strings` / `math` / `collections` / `date` / `path` / `urls` / `compare`。
- [ ] **P4-3** 实现 Liquid 版 `resources.Get/Match/ByType/Concat/Copy/Fingerprint`。
- [ ] **P4-4** 实现 Liquid 版 `css.Sass` / `css.Build` / `js.Build` 或先以服务化 tag 提供。
- [ ] **P4-5** 实现 Liquid 版 `partial` 高级语义：dict 上下文、页面成员访问。
- [ ] **P4-6** 补函数对拍测试：同名函数 Scriban 与 Fluid 输出一致或有明确差异文档。

验收：新站点常用模板能力不弱于 Scriban；差异清单入库。

### Phase 5：内容管线解耦

- [ ] **P5-1** 短代码处理器抽象为 `IShortcodeRenderer`，默认仍 Scriban。
- [ ] **P5-2** render hooks 抽象为 `IRenderHookRenderer`，默认仍 Scriban。
- [ ] **P5-3** 新站点配置可关闭短代码或选择 Fluid 短代码渲染器。
- [ ] **P5-4** 实现 Liquid 版内置短代码：figure、ref、relref、highlight 等。
- [ ] **P5-5** 实现 Liquid 版 render hooks：link、image、heading。

验收：Fluid 主力站点可不经 Scriban 完成内容渲染；Hugo 主题行为不变。

### Phase 6：性能门禁与发布

- [ ] **P6-1** 增加站点级 BenchmarkDotNet 用例：Liquid 新站点 vs Scriban 新站点。
- [ ] **P6-2** 增加 AOT 体积对比：双引擎二进制 vs 纯 Scriban 二进制。
- [ ] **P6-3** 增加装配探针：Fluid 路径运行时不得加载 Scriban 程序集。
- [ ] **P6-4** 补文档：新站点模板规范、Liquid 函数清单、Scriban 兼容层边界。
- [ ] **P6-5** 发布说明明确：Fluid 主力，Scriban 仅 Hugo 兼容。

验收：性能门禁可复现；文档和发布说明无旧口径。

## 四、关键风险与对策

| 风险 | 对策 |
|---|---|
| Liquid 与 Scriban 语义差异 | 先做新站点最小集，差异逐条文档化 |
| Hugo 函数平面迁移量大 | 按新站点使用频率分级，不追求全量对齐 |
| 短代码和 render hooks 仍走 Scriban | Phase 5 显式解耦，避免 Fluid 热路径被拖回 |
| Fluid 3.0 beta 的 AOT 风险 | 每阶段都做 NativeAOT 冒烟 |
| 双引擎缓存互相影响 | 引擎隔离硬规则 + 装配测试 |
| 现有 Hugo 主题回归 | 每阶段跑 21 主题矩阵，Scriban 侧测试不全量重写 |
| 性能收益被 IO 稀释 | 站点级 A/B，不拿模板 microbenchmark 结案 |

## 五、建议实施顺序

先做 Phase 0 到 Phase 2，做出一个“新站点默认 Fluid、内置最小模板可用、Hugo 主题不受影响”的垂直切片。这一步就能验证：

- Fluid 是否真的能独立渲染 Flint 页面模型
- AOT 是否稳
- 站点级收益是否真实
- 双引擎隔离是否可守

Phase 3 之后再把 Fluid 从“新站点主力”推向“内置模板全面主力”。Phase 4 和 Phase 5 决定 Fluid 能否彻底摆脱 Scriban，尤其短代码和资源函数是最容易隐藏耦合的地方。

## 六、当前判断

以现有代码结构，这个方案可行。`SiteBuilder` 已经只依赖 `ITemplateRenderer`，CLI 构造点集中，内置模板虽然目前硬编码在 Scriban 类里，但拆 `IBuiltinTemplateProvider` 的代价可控。真正的大头在函数平面和内容管线，不在路由层。

当前提交 `1a6e31f` 里的 Fluid 只是 `.liquid` 备用分支，不能直接当主力。按上面 Phase 1 到 Phase 3 推进，才能得到平行的双引擎。

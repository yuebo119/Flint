# Flint 主题系统 Hugo 兼容完整方案（第二轮穷举 · v2）

> 依据：Hugo 官方文档（templates/types、lookup-order、shortcode templates、partials、
> InnerDeindent API 页）+ Ananke 主题实际调用统计 + Flint 代码逐项 grep 实测
> 目标：把"能兼容"的全部列出并分级；不兼容项给出代价判断而非模糊表述

## 一、已兼容（两轮累计）

模板回退链（含 `_partials`/`partials`、`_shortcodes`/`shortcodes` 双形态）、
static/assets 合并、主题 params 深合并、archetypes 回退、i18n（Hugo 形态）、
多主题叠加、三形态安装源、lockfile、短码双语义、data/（含主题）、
render hooks（含主题）、cascade、taxonomy/term 模板、输出格式变体、
内容视图 `render "view"`、分页多页产出与 `page.paginator`/`paginator`、
RSS/sitemap 模板覆盖、
aliases 重定向页、404 模板、robots.txt 模板、主题短码容错。

## 二、缺口清单（本轮新发现以 ★ 标记）

### A 组 · 主题分发的完整性（★ 本轮新发现，Ananke 实测证实）

| # | 缺口 | 依据 | 实现方式 | 收益 |
|---|---|---|---|---|
| A1 ★ | **主题 `config/_default/*.toml` 合并** | Ananke 有 `config/_default/{module,params}.toml`，Flint 只读 `theme.toml` 的 `[params]` | 读主题 `config/_default/*.{toml,yaml,json}`：`params` 深合并进站点（站点优先），`menus`/`taxonomies` 等同理 | 主题默认配置真正生效（当前 Ananke 的 ananke.* 参数全部丢失） |
| A2 ★ | **主题 `content/` 合并** | Hugo 主题可自带示例内容 | 站点 content 优先，主题 content 回退（同源路径不覆盖） | 主题 demo 内容可直接构建 |
| A3 ★★ | **`static/` 输出前缀剥离（已实证确认的 P0 缺口）** | Hugo 官方行为实证：`static/css/a.css` → 输出 `public/css/a.css`（**剥离 static/ 前缀**）；Flint 实证：→ `public/static/css/a.css`（保留前缀）。主题引用 `/css/style.css` 在 Flint 下必然 404 | `CollectAssetFiles` 的 `segment` 参数对 static 用空串（映射输出根），assets 保持 `assets/` 前缀（Hugo 中 assets 不直接发布） | **所有 Hugo 主题的资源引用立即可用**——影响面最大的单项 |
| A4 | 主题 `theme.toml` 元数据消费 | Ananke 有 license/tags/features/min_version/authors | 读取用于 `mod list` 展示与 min_version 校验 | 工具信息完整 |

### B 组 · 模板能力缺口（★ 多为 Ananke 实际调用）

| # | 缺口 | 依据 | 实现方式 |
|---|---|---|---|
| B1 ★ | **短码上下文 `.Get` / `.IsNamedParams` / `.Inner` / `.Params`** | Ananke：`.Get` 11 次、`.IsNamedParams` 2 次、`.Params` 2 次；Flint 只暴露裸参数与 `inner` | 短码模板注入 `get`（命名/位置双查）、`is_named_params`、`params` 字典；对齐 Hugo 短码 API |
| B2 ★ | **partial 返回值（`{{ return X }}`）** | Ananke：`:= partials.Include` 9 次（把 partial 当函数调用）；Hugo 文档："a partial must include a lone return statement" | Scriban 无 return 语句——需把 partial 渲染结果作为表达式值：`include` 已是表达式可返回值，补 `return` 语义（partial 内 `{{ return X }}` → 输出 X 且终止） |
| B3 ★ | **`partials.IncludeCached`** | Hugo v0.146 重命名，Ananke 在用 | 映射到既有 `partialcached` |
| B4 ★ | **`.File.*` 方法族** | Ananke：`.File.Path`；转换器已拦截为 TODO | PageContext 暴露 `file` 对象（path/dirname/basename/content_base_name/extension/unique_id） |
| B5 ★ | **`.Resources` 页面资源接口** | Ananke 的 GetFeaturedImage 依赖；`PageContext.Resources` 字段存在但**从未装配** | 装配 + 暴露 `resources`（get/match/get_match/by_type）+ 受限 `resize/fit` |
| B6 | `.Store` / `.Scratch` 页面级暂存 | 跨块状态传递 | 页面级可变字典（Scriban ScriptObject 挂载点） |
| B7 | `where` / `sort` / `first` / `last` / `uniq` / `shuffle` 集合函数 | 列表过滤排序高频 | 逐个补齐（Scriban 有部分等价：array.filter/map/sort） |
| B8 | `dict` / `slice` 构造与 `merge` / `index` | partial 传参与数据操作 | 映射到 Scriban 对象/数组字面量 |
| B9 | `markdownify` / `emojify` / `plainify` / `htmlUnescape` | 内容处理 | Flint 有部分（待逐一核对命名） |
| B10 | `absURL` / `relURL` / `urlize` / `anchorize` | URL 处理 | 补齐（部分已在 URL 函数族） |
| B11 | `.Site.GetPage` 路径查询 | 站点导航 | 按路径/kind 查页（树已有查询能力） |
| B12 | `.Param`（站点级参数查询，带点路径） | 主题参数读取 | 点路径解析 |

### C 组 · 输出与渲染管线

| # | 缺口 | 说明 |
|---|---|---|
| C1 ✅ | 分页 URL 产出（`/page/2/` 多页） | **已实现**：home/section 列表页按 `paginate` 切片，逐页产出 `{列表页}page/{N}/`；`PaginatorView` 逐页绑定 `page.paginator` 与全局 `paginator`（pages/page_number/total_pages/pager_size/has_prev/has_next/url/first/last/prev/next/pagers）；内置 `pagination` 模板（Hugo embedded default 格式的 Scriban 等价）供 `include "pagination"` 命中 |
| C2 | 完整输出格式矩阵（自定义 mediaType/多格式） | 页面级 outputs 已有；格式定义扩展不做 |
| C3 | 模板内资源管线（完整 `resources.Get \| toCSS \| minify` 链） | 受限等价：`resources.get` 映射管线产物 |
| C4 ✅ | 内容视图的 section 目录查找（`layouts/<section>/<view>.html`） | **已修正对齐 Hugo**：候选路径按页面 `.Path` 从深到浅逐级上溯（`docs/api/summary`→`docs/summary`→`summary`），精确目录匹配（不再用文件名段模糊匹配，消除同名视图跨目录误命中）；`render "view" <page>` 支持显式接收者（对齐 Hugo `.Render` 页面方法） |

### D 组 · 永久放弃（代价 > 价值）

多语言全站（languages 配置 + 双语言循环）、Hugo Modules（Go modules 图）、
Hugo Pipes 完整描述符链、content adapters（`_content.gotmpl`）、
partial decorators（v0.146 新特性，无生态采用）。

## 三、优先级与批次

### 批次三（本方案核心，预期收益最大）
**A3（static 前缀剥离，P0，实证确认）** + A1 + B1 + B2 + B3 + B4 ——
A3 是影响面最大的单项（所有主题的资源引用）；其余五项是 Ananke 剩余 5 个
文件报错的直接原因，且都是"主题作者高频使用"的能力。完成后 Ananke 应可达零解析错误。

> A3 的兼容性判断需用户裁决：剥离 static/ 前缀是对齐 Hugo 的正确行为，但会
> **改变 Flint 既有站点的输出路径**（`public/static/x` → `public/x`），属破坏性变更。
> 建议：默认对齐 Hugo（剥离），并在 CHANGELOG 标注迁移说明。

### 批次四
B5（`.Resources` 装配）+ B6（Store）+ B7/B8（集合与构造函数）+
B9/B10（内容/URL 函数补全）+ B11/B12（查询与参数）——按转换器 TODO 分布排序实施。

### 批次五 ✅（已完成）

A2 + A3 + A4（主题分发完整性）+ C1（分页多页）+ C4（视图查找序核对）。
C1/C4 为最后收官项：C1 增加列表页多页产出与逐页 Paginator 绑定；
C4 把视图查找从"文件名段模糊匹配"收紧为"Hugo 目录语义精确匹配 +
全路径逐级上溯"，并补 `render` 显式接收者。

同时修复了 Ananke 迁移的转换器残留：Scriban `with` 无 `else`（改
`$w = expr; if $w`）、`with $x = expr` 非法目标、多行布尔链、`.Related`/
`.Scratch` 等深度语义降级为可追溯 TODO。Ananke 探针站从 12 个构建错误
降到 0（详见下节）。

## 四、验收标准

1. 每批次附端到端测试（主题 fixture + 站点构建断言）
2. 每批次后跑 Ananke 迁移站：记录 RENDER 错误数与 TODO 数变化（目标：批次三后 RENDER 归零）
3. 性能门禁 perf-gate 通过；增量构建不劣化
4. 转换器知识库同步（新支持的能力从 TODO 转自动映射）

## 五、量化现状（本轮实测）

| 指标 | 数值 |
|---|---|
| Ananke 模板解析错误 | **0**（批次五后；此前 5 个文件、12 个构建错误） |
| Ananke TODO 标记 | 136 处（Hugo 深度语义降级，产出合法 Scriban + 可追溯注释） |
| Ananke 探针站产物 | 16 个页面（home/posts/8 篇 post/taxonomy 首页与词条/分页页）+ 199 资源，EXIT=0 |
| Flint 内置模板函数 | 136 个（Hugo 约 300+） |

### 批次五端到端实证（Ananke 探针站，paginate=3，8 篇文章）

```
EXIT=0，页面 16，资源 199
public/: 404, categories/, tags/, index.html, page/2/, page/3/,
         posts/index.html, posts/page/2/, posts/page/3/, posts/post-1..8/
posts/        → Post 8/7/6（第 1 页）
posts/page/2/ → Post 5/4/3
posts/page/3/ → Post 2/1
每张卡片 summary 渲染自身文章上下文（render 接收者修正）
分页导航：pagination-default 模板，First/Prev/页码槽/Next/Last 齐全
```

## 六、Ananke 迁移的已知降级（非错误）

以下 Hugo 深度语义无 Flint 等价，转换器产出 `##TODO-HUGO: ...##` 注释或
空值（不吞周围结构、不产出非法语法），功能表现为空/隐藏：

`.Related`（相关内容）、`.Scratch`/`.Store`（页面级暂存）、`templates.Defer`、
`collections.Dictionary` 传参 partial、多行布尔链、`compare.Conditional`、
`fmt.Printf`/`Warnf`、`urls.RelURL`、`resources.Get`、`.GetTerms`/`.Translations`/
`.Resources.*`/`.File.*`（部分）。如需真实语义需引擎侧新增能力。

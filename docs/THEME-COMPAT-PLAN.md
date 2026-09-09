# Flint 主题系统 Hugo 兼容完整方案（穷举清单）

> 依据：Hugo 官方模板类型体系（templates/types + lookup-order 文档，v0.146 新模板系统口径）
> 与 Flint 代码实测核对（cascade/分页/aliases/shortcodes/data/render hooks 等逐项 grep）
> 目标：Hugo 主题的非模板资产 100% 消费、模板语义最大对齐，明确放弃项与代价

## 现状总览（对照后）

**已兼容（含本轮 P0~P3 完成）**：模板回退链、static/assets 合并、主题 params 深合并、
archetypes 回退、i18n（Hugo 形态）、多主题叠加、三形态安装源、lockfile、短码双语义、
data/ 数据文件、render hooks（站点级）、cascade（front matter 级联）、taxonomy/term 模板、
输出格式变体模板、内容级 SRI/指纹。

**缺口 14 项**，按可实现性分三批 + 2 项永久放弃（见第三节）。

## 第一批 · 纯查找/合并扩展（机制同构复用，低成本）

| # | 缺口 | Hugo 语义 | 实现方式 | 验收 |
|---|---|---|---|---|
| 1 | 主题级 shortcodes | 主题 `layouts/shortcodes/` 站点覆盖 | `RegisterSiteShortcodes` 增主题目录（后注册不覆盖站点） | 主题短码在内容中展开 |
| 2 | 主题级 data/ | 主题 `data/*.toml` 进 `site.data` | DataFileLoader 多根合并（站点优先） | `site.data.<key>` 取到主题数据 |
| 3 | 主题级 render hooks | 主题 `_markup/render-*.html` | `RenderHooks.Load` 多根（站点优先） | 主题 hook 定制链接渲染 |
| 4 | front matter aliases | `aliases: [/old/]` 产出重定向页 | 构建期为每个别名产出 meta-refresh HTML（解析层 Aliases 已有，构建层消费缺失） | 别名 URL 200 且跳转 |
| 5 | 404 模板 | `layouts/404.html` | 单模板渲染 + 404.html 输出 | 主题 404 被使用 |
| 6 | robots.txt 模板 | `layouts/robots.txt` | 同上（无模板时保持现状不产出） | 有模板则产出 |

## 第二批 · 引擎接口（中等成本，真实主题强依赖）

| # | 缺口 | Hugo 语义 | 实现方式 | 验收 |
|---|---|---|---|---|
| 7 | 内容视图 `.Render "summary"` | 页面按视图名渲染子模板（`layouts/<section>/summary.html`），Ananke/列表型主题核心依赖 | Scriban 全局函数 `render`：PageContext + 视图名 → 模板查找链（含主题）→ 渲染 | Ananke 的 summary 视图可解析可渲染 |
| 8 | 分页模板接口 | 列表页 `{{ range .Paginator.Pages }}` + `.TotalPages/.HasPrev` | `site.paginator` 暴露（PaginationService 已有内核，缺模板层暴露）+ `paginatePath` 页面产出 | 分页页多页产出、Paginator 字段可用 |
| 9 | RSS/sitemap 模板可覆盖 | 主题自带 rss.xml/sitemap.xml 替代内置生成器 | 内置生成前查模板链（含主题），有则模板渲染 | 主题 RSS 模板生效 |
| 10 | 主题级 menus 合并 | 主题配置可带默认 `[menus]` | ThemeParamsMerger 扩展：menus 段深合并（站点优先） | 主题默认菜单 + 站点覆盖 |
| 11 | 增量失效接入主题资产 | 主题 shortcodes/data/hooks 变更触发受影响页失效 | 依赖图登记主题文件（防增量退化为全量，也防"改了不生效"） | 改主题 shortcode → 相关页增量更新 |

## 第三批 · 部分兼容（子集实现，明确边界）

| # | 缺口 | 边界 |
|---|---|---|
| 12 | 模板内资源管线（`resources.Get \| toCSS`） | 受限等价：`resources.get "path"` 映射管线产物路径；完整描述符链（resize/fit 处理链）不做——图片走构建期全局处理 |
| 13 | bundle 页面资源（`.Resources`） | leaf bundle 目录输出形态已支持；`.Resources` 模板接口首版不暴露 |
| 14 | 输出格式自定义 | 页面级 outputs（html/json 变体模板）已有；自定义媒体类型/输出格式矩阵不做 |

## 永久放弃（代价 > 价值，明确不做）

| 项 | 理由 |
|---|---|
| 多语言全站（languages 配置 + 双语言循环） | SiteBuilder 级重构；i18n 翻译已覆盖主题文本层 |
| Hugo Modules（Go modules 依赖图） | 绑 Go 工具链；git+lockfile 语义等价 |
| Hugo Pipes 完整描述符链 | 模板内资源编排范式冲突，#12 子集即可 |
| content adapters（_content.gotmpl 程序化内容） | 罕见特性，脚本预生成内容替代 |
| partial decorators | Hugo v0.146 新特性，等待真实需求 |

## 实施批次与验收

### 批次一（第一组 #1~6）——预期零性能影响
验收：六个独立小测试 + Ananke 迁移站构建（主题 shortcodes/data/hooks 被 Ananke 实际使用，是转换器 TODO 消化项）。

### 批次二（第二组 #7~11）——含增量失效接入
验收：分页/内容视图在 Ananke summary/list 场景渲染；性能门禁 perf-gate 通过；增量构建延迟基线不劣化。

### 批次三（第三组 #12~14）——按需
验收：受限函数 + 文档边界声明。

## 转换器联动

第一、二批落地后，转换器（scripts/gotmpl2scriban.py）的 HUGO_NS_RE 与知识库同步扩充：
`partials.Include`、`.Render`、`.Paginator`、`lang.Translate` 等从 TODO 列表转为自动映射——
Ananke 的 184 个 TODO 预计可再消化约 60%（[推断] 基于 TODO 形态分布统计）。

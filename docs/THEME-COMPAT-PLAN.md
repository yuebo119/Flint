# Flint 主题系统 Hugo 兼容完整方案（第三轮穷举 · v3）

> 依据：Hugo 官方文档（templates/types、lookup-order、new-templatesystem-overview、
> partials、shortcode templates、render hooks、output-formats、embedded templates、
> functions/、methods/page|site|resource|file）+ Hugo 源码描述符加权常量
> （tpl/tplimpl/templatedescriptor.go）+ Flint 代码逐文件实测（2026-09-10）。
>
> 目标：把"能兼容"的全部列出并分级；对不兼容项给出代价判断而非模糊表述。
> 前两轮（v1/v2）与批次三/四/五的实施记录见文末「历史批次」。

## 零、结论先行

Hugo 主题兼容的**充分必要条件**是三块，缺一不可：

| 块 | 内容 | Flint 现状 | 能否完全兼容 |
|---|---|---|---|
| **模板查找** | 页面感知的多维候选链（kind/type/section/layout/outputformat/lang） | 全局加权匹配，**不感知页面上下文** | 可完全兼容（需重写查找层） |
| **数据 API** | Page/Site/Resource/File 对象方法 + 函数库（含命名空间） | 约覆盖 40%；**命名空间函数 0 个** | 可完全兼容（纯补齐） |
| **模板语法** | Go template 语义（define/block/with/range/pipeline/`$`） | Scriban 引擎，语义不同 | **不可完全兼容**，须靠转换器 |

**核心判断**：Flint 用 Scriban 而非 Go template，语法层不可能原生执行 Go template。
因此"完全兼容"的现实定义是——**主题经 `scripts/gotmpl2scriban.py` 一次性转换后零改动运行**。
兼容性 = 转换器覆盖率 × 引擎数据 API 覆盖率。前者已有实践（Ananke 51 模板 /
1015 表达式全自动覆盖），后者是本方案的主体。

**本轮最大发现**：模板查找层是**架构级缺口**。当前 `TemplateLookup` 按文件名加权
全局匹配，没有"当前页面是谁"这个维度，导致 `layouts/posts/single.html` 会对**全站所有
页面**生效、`layouts/<type>/` 目录无法按 type 路由、语言变体与输出格式后缀（`list.rss.xml`）
完全不参与查找。这不是补几个路径能解决的，需要引入候选链解析层。

> **实施状态（2026-09-10 本轮已落地）**：A 组模板查找层核心已实施——新增
> `PageTemplateCandidates`（候选链纯函数）+ `TemplateLookup.ResolveLayered`（分层解析，
> 候选级为主判据、根序为次判据），渲染分派（页面/首页/分类页）全部改走候选链；
> `PageContext` 新增 `Kind`/`DeclaredType`/`TaxonomyName`/`TaxonomySingular`/`TaxonomyPlural`；
> 补 `page.Data` 对象（Singular/Plural/Terms/Pages）修复 Ananke `terms.html`。
> 实测验收：Ananke 目录形态四维正确路由（home→home.html / type=page→page/single.html /
> section=post→post/list.html / 其余→_default），分类页 Data.pages 产出词条对象。
> **无破坏性变更**：`Resolve`（partial 等无页面上下文场景）语义未动，站点优先仍成立。
> 详见文末「批次六实施记录」。

---

## 一、已兼容基线（前两轮 + 批次三/四/五累计）

| 能力 | 状态 |
|---|---|
| 模板回退链（站点 → 主题按序；`_partials`/`partials`、`_shortcodes`/`shortcodes` 双形态） | 已实现 |
| `_default/` 兜底目录（Hugo 旧形态）与一级形态（`single.html`/`list.html`/`index.html`/`taxonomy.html`/`term.html`） | 已实现 |
| static/ 前缀剥离（`static/css/a.css` → `public/css/a.css`） | 已实现（批次五） |
| assets/ 直接发布（Flint 扩展，`assets/` 前缀保留） | 已实现 |
| 主题 params 深合并（`theme.toml [params]` + `config/_default/params.*`） | 已实现（批次五） |
| 主题 `content/` 回退（站点优先） | 已实现（批次五） |
| 主题 `data/`、`i18n/`、`archetypes/`、`static/`、`assets/` 挂载 | 已实现 |
| 多主题叠加（`themes = [...]`，后者为底、前者覆盖） | 已实现 |
| 三形态安装源 + lockfile + SHA256 校验 | 已实现 |
| 短码双语义（`{{< >}}` 占位符 / `{{% %}}` 内联） | 已实现 |
| 短码模板上下文 `get`/`is_named_params`/`params`/`positional`/`inner`/`name` | 已实现（批次三 B1） |
| partial 返回值语义（`partial "x" .` 作函数用，标量类型还原） | 已实现（批次三 B2） |
| `partialCached` / `includeCached` / `include_cached` | 已实现（批次三 B3） |
| `.File.*` 基础族（Path/Dir/Filename/ContentBaseName/Ext 等） | 已实现（批次三 B4） |
| `.Resources` 字段装配（leaf bundle 资源归属） | 已实现（批次五） |
| 内容视图 `render "view"`（含显式接收者、目录逐级上溯） | 已实现（批次五 C4） |
| 分页多页产出 + `page.paginator` / 全局 `paginator` + 内置 pagination 模板 | 已实现（批次五 C1） |
| taxonomy / term 模板查找 + `page.terms` 数据模型 | 已实现 |
| 输出格式变体模板（`single.json.html`）+ [outputs] 配置消费 | 已实现 |
| render hooks：link / image / heading / codeblock（+ 语言专属变体） | 已实现（批次三 T5.1） |
| RSS / sitemap / robots.txt / 404 / aliases 模板覆盖 | 已实现 |
| cascade 沿树级联、i18n（Hugo 形态）、内置回退模板（taxonomy/term/list/pagination） | 已实现 |
| 内置短码 10 个（figure/highlight/ref/relref/gist/youtube/tweet/vimeo/instagram/param） | 已实现 |
| 内置模板函数约 136 个（全局扁平名，含 Hugo 旧别名双拼） | 已实现 |
| Go→Scriban 转换器（四轮迭代，Ananke 12 错误 → 0） | 已实现 |

---

## 二、本轮缺口全矩阵

标记：★ = 本轮新发现；★★ = 架构级（改动面大）；✅ = 可完全兼容；◐ = 可等价映射；
✖ = 放弃（理由见第六节）。优先级 P0（主题能否跑起来）> P1（体感完整）> P2（锦上添花）。

### A 组 · 模板查找系统（★★ 架构级，本轮最大缺口 · 实测证实）

**根因**：查找模型错位——现有实现是"给定模板名找最匹配文件"（名字→文件），
Hugo 是"给定页面找最匹配模板"（页面向量→模板）。缺少页面上下文维度。
实测证据见 7.1（Ananke 官方主题在 Flint 下全站被单一模板渲染）。

| # | 缺口 | Hugo 行为（官方） | Flint 现状（实测） | 方案 | 收益 | 代价 |
|---|---|---|---|---|---|---|
| A0 ★★★ | **候选路径特异性 vs 根序错位** | distance（目录具体度）为第一判据，站点/主题根序次之 | `Score` 根序占 100 分 → 站点 `_default` 永久压过主题 `posts/single.html` | **步骤 1（非破坏）**：翻转两级判据，特异性为主键、根序为次键 | 主题 section 布局从"100% 失效"到可用 | S |
| A1 ★★ | **页面感知候选链缺失** | 按 kind 构造有序候选路径表，逐个探测取首个命中 | `ResolveTemplatePath("single")` 单名全局加权；`posts/single.html` 对**全站**生效 | **步骤 2（破坏性）**：新增 `PageTemplateResolver` 按 kind/type/section/layout 生成有序候选名，交 `TemplateLookup.ResolveExact` 单名解析 | 消除跨 section/type 污染 | M |
| A2 ★★ | **type 目录维度** | `layouts/{type}/{layout}.html` → `layouts/{type}/single.html` | 无 type 目录概念（`page/single.html` 被全站命中） | 候选链第一段用 front matter `type` | 主题按 type 分布局（Ananke `page/single.html`）生效 | S（依赖 A1） |
| A3 ★★ | **section 目录维度** | `layouts/{section}/{layout}.html` → `layouts/{section}/single.html` | 无 section 约束（`post/list.html` 污染其他 section） | 候选链第二段用 `.Section` | section 专属布局正确隔离（Ananke `post/list.html`） | S（依赖 A1） |
| A4 ★ | **语言变体后缀** | `single.en.html` > `single.html` | TemplateLookup 无 language 维度 | 候选名展开加 `.{lang}` 变体 | 多语言主题布局生效 | S |
| A5 ★ | **输出格式 + 媒体类型后缀** | `list.rss.xml` / `section.json.json`；格式名 + 后缀双段 | 只支持 `{name}.{format}.html`（后缀硬编码 html） | 候选名按输出格式展开为 `{name}.{format}.{suffix}` | RSS/sitemap 主题模板可覆盖 | S |
| A6 ★★ | **一级与新型形态名** | `page.html`/`home.html`/`section.html`/`taxonomy.html`/`term.html`/`all.html` | `InferKind` 只认 single/list/index/taxonomy/term；**`home.html` 实测不识别 → 首页渲染为空**；Ananke 正在用 `home.html` | 候选链加入全部一级名（`home`/`page`/`section`/`all`）；`all.html` 作最泛兜底 | Ananke 等新式主题可运行 | S（依赖 A1） |
| A7 ★ | **`baseof` 候选链** | `layouts/{section}/baseof.html` → `layouts/{type}/baseof.html` → `layouts/_default/baseof.html` | Scriban `extends "baseof.html"` 走 FileTemplateLoader 探测，**无页面维度** | baseof 解析同样走候选链 | 按 section 定制骨架生效 | S（依赖 A1） |
| A8 ★ | **descriptor 加权完整度** | `distance` 第一判据；含 `weightLayoutAll=2`/`weightVariant1=6` | 简化加权：名精确+10/段+6/`_default`-2/格式+4/**根序+100（越权）** | 步骤 1 降根序为次键；步骤 2 后候选表内"首个命中即胜"（语义等价，实现更简） | 语义等价 | S |
| A9 ★ | **render hook 的页面路径层级** | `layouts/{pagepath}/_markup/render-link.html`（越近越优先） | 只查站点 `_markup/` → 主题 `_markup/` 两处，无页面路径段 | RenderHooks 探测路径加页面 section 上溯（同 C4 视图查找法） | 按 section 定制 hook | S |
| A10 ★ | **`_default/_markup/` 旧路径** | 旧主题放 `layouts/_default/_markup/` | 只查 `layouts/_markup/` | 探测列表补 `_default/_markup/` 形态 | 旧主题 hook 可用 | S |
| A11 ★ | **短码查找链** | `{name}.{lang}.{format}.{suffix}`；子目录（`media/audio`）；页面路径层级 | 只按 `<name>.html` 单形态加载 | 短码加载加语言/格式/子目录/页面路径维度 | 短码覆盖变体与分区 | S |

### B 组 · 模板语言桥接（◐ 靠转换器，本轮需补引擎侧配合）

| # | 缺口 | Hugo 形态 | 方案 | 优先级 |
|---|---|---|---|---|
| B1 ★★ | **`define` + `block` 骨架** | baseof 中 `{{ block "main" . }}`，子模板 `{{ define "main" }}` | Scriban **已有** `{{ extends }}` + `{{ block name }}` 等价能力（Flint 自建主题即用此形态）。转换器把 `define "main"` → `block main`、`block "main" .` → `block main`；引擎侧无需改 | P0 |
| B2 ★ | **`{{ template "name" . }}`** | 调用已定义模板并传上下文 | 转换器映射到 Scriban `include`（单参）；带上下文参数时降级为 `include` + 变量悬挂（共享 context 天然可见），TODO 标注 | P1 |
| B3 ★ | **`{{ return X }}` 模板级语义** | 模板/partial 内 return 立即终止并返回值 | partial 返回值已支持（B2 批次三）；模板级 return 需 Scriban `ret`/`func` 或转换器降级；建议降级 + TODO | P1 |
| B4 ◐ | **变量作用域（`$` 根上下文）** | `$` 恒指模板根上下文，`range`/`with` 内不变 | Scriban `$` 语义不同（用户变量前缀），`range` 内根上下文用 `this`/闭包变量；转换器需重写 `$` 引用（Ananke 已自动处理） | P0（转换器） |
| B5 ◐ | **pipeline 与 `with`/`else`** | `{{ with $x }}...{{ else }}...{{ end }}` | 已修复（批次五：Scriban 无 `with` else，降级为 `$w = expr; if $w`） | 已完成 |
| B6 ★ | **`.Scratch` / `.Store`** | 页面级可变暂存（跨块状态传递），`Set`/`Get`/`Add`/`SetInMap` | PageContext 加 `Store` 对象（ScriptObject 挂载点，per-render 实例）；`.Scratch` 作别名 | P1 |
| B7 ★ | **`templates.Inner`（partial decorators）** | 包裹式组件（v0.154+） | 无生态采用，✖ 放弃 | — |

### C 组 · 函数库（★ 本轮最大观感缺口：命名空间函数 0 个）

Hugo 0.146+ 官方文档**全部改用命名空间形式**，旧全局名保留为别名。Flint 只有全局扁平名，
新式主题的 `strings.ToUpper` / `collections.Where` / `math.Add` / `hugo.IsProduction`
会直接渲染失败。方案：新增命名空间对象层，把已有实现**同时**注册到新名（不需重写实现）。

| # | 命名空间 | 函数数 | Flint 现状 | 方案 |
|---|---|---|---|---|
| C1 ★ | `strings.*` | 31 | 全局同名约 20 个已实现 | 建 `strings` ScriptObject，别名指向现有实现；补 `chomp`/`containsAny`/`diff`/`findRE`/`findRESubmatch`/`firstUpper`/`replacePairs`/`runeCount`/`slicestr`/`trimPrefix`/`trimSuffix`/`trimSpace` |
| C2 ★ | `collections.*` | 22 | 全局同名约 12 个已实现 | 建 `collections` 对象；补 `Apply`/`Complement`/`Group`/`KeyVals`/`NewScratch`/`SymDiff`/`D` |
| C3 ★ | `math.*` | 30 | 全局同名约 14 个已实现 | 建 `math` 对象；补 `Abs`/`Acos`/`Asin`/`Atan`/`Atan2`/`Cos`/`Sin`/`Tan`/`MaxInt64`/`ModBool`/`Pi`/`Product`/`Rand`/`Sum`/`ToDegrees`/`ToRadians` |
| C4 ★ | `compare.*` / `cast.*` | 8 / 3 | 全局同名已实现 | 直接建对象别名 |
| C5 ★ | `crypto.*` / `encoding.*` / `hash.*` | 5 / 5 / 2 | md5/sha1/sha256/base64 已实现 | 建对象；补 `FNV32a`/`XxHash`/`HexEncode`/`HexDecode`/`HMAC` |
| C6 ★ | `transform.*` | 15 | markdownify/emojify/plainify/htmlEscape/htmlUnescape 已实现 | 建对象；补 `Highlight`（需语法高亮）/`Remarshal`/`Unmarshal`/`XMLEscape`/`HTMLToMarkdown` |
| C7 ★ | `urls.*` / `path.*` / `inflect.*` / `safe.*` | 13 / 7 / 3 / 6 | 大部分已实现 | 建对象别名；补 `AbsLangURL`/`RelLangURL`/`JoinPath`/`Parse`/`PathEscape`/`PathUnescape`/`safe.JSStr` |
| C8 ★★ | `resources.*` | 13 | **全部缺失**（`resources.Get` 无实现） | 需资源管线接口：`Get`/`GetMatch`/`Match`/`ByType`/`Concat`/`FromString`/`Copy`/`ExecuteAsTemplate`/`Fingerprint`/`Minify`/`ToCSS`/`PostProcess`/`Publish`。核心是 `Get`/`GetMatch`/`Match`/`Fingerprint`/`Minify`（主题 CSS/JS 处理链路） |
| C9 ★ | `js.*` / `css.*` | 3 / 6 | 有 AssetPipeline（Sass/esbuild）但无模板函数 | `js.Build` 映射 JavaScriptBundler；`css.Sass`/`css.Build` 映射 SassCompiler；`images.*` 部分映射 ImageProcessor |
| C10 ★ | `hugo.*` | 17 | 全部缺失 | 常量对象（Version/Environment/IsProduction/IsDevelopment/IsServer/Generator/WorkingDir...），零成本 |
| C11 ★ | `os.*` / `time.*` / `lang.*` | 5 / 6 / 8 | now/dateFormat/duration 已实现 | 建对象；`os.ReadFile`/`FileExists`/`Getenv`（注意安全边界）、`lang.Translate`（映射 i18n）、`lang.FormatNumber` |
| C12 ★ | `partials.*` | 2 | `partial`/`includeCached` 已实现 | 建 `partials` 对象：`Include`/`IncludeCached` 别名 |
| C13 ★ | `fmt.*` / `reflect.*` / `templates.*` / `debug.*` | 7 / 8 / 4 / 3 | errorf/warnf/printf/print 已实现 | 建对象；`templates.Exists` 映射 TemplateExists；`reflect.IsPage`/`IsMap`/`IsSlice` 低成本 |
| C14 ★ | `diagrams.*` / `openapi3.*` / `images.*` 全滤镜 | — | 无 | ✖ 放弃（后者见第六节） |

### D 组 · Page 对象方法补全

Hugo Page 共 80+ 方法（官方 methods/page），Flint `PageContext` 约 30 个键。缺口（★ 为本轮新发现）：

| # | 缺口方法 | 说明 | 方案 | 优先级 |
|---|---|---|---|---|
| D1 ★ | `.Truncated` / `.PlainWords` / `.FuzzyWordCount` / `.LinkTitle` | 摘要截断标志、纯文本词列表、模糊字数、链接标题 | PageContext 加字段（LinkTitle 缺省回退 Title） | P1 |
| D2 ★★ | `.TableOfContents` 结构化 / `.Fragments` | 当前 `IReadOnlyList<object>` 恒空；Hugo 提供 `.Fragments.Headings`/`.Identifiers`/`.HeadingsMap` 与 TOC 树 | MarkdownParser 产标题树 → 装配 TOC（含 id 生成与 `render-heading` 钩子联动） | **P1（主题目录树高频）** |
| D3 ★ | `.File.Filename` / `.LogicalName` / `.TranslationBaseName` / `.Section` / `.UniqueID` / `.IsContentAdapter` | BuildFileObject 已有 Path/Dir/ContentBaseName/Ext | 补 6 个键 | P1 |
| D4 ★★ | **Resource 对象方法** | `.Resize`/`.Fit`/`.Fill`/`.Crop`/`.Process`/`.Content`/`.MediaType`/`.Width`/`.Height`/`.Name`/`.Title`/`.Permalink`/`.RelPermalink`/`.Params` | 建 `ResourceView` 类型（ImageProcessor 已有变换能力，做薄映射）；非图像资源提供元数据 | P1 |
| D5 ★ | `.Ancestors` / `.CurrentSection` / `.FirstSection` / `.IsAncestor` / `.IsDescendant` / `.IsBranch` / `.BundleType` | 树结构导航 | 页面树已有父子关系，做投影（父链、is_* 判定、bundle 类型） | P1 |
| D6 ★ | `.GetTerms` / `.GetPage` / `.Ref` / `.RelRef` / `.RenderString` / `.RenderShortcodes` / `.HasShortcode` | 查询与二次渲染 | `GetPage` 用树路径查询；`GetTerms` 用 taxonomy 索引；`RenderString` 复用 MarkdownParser + 当前上下文 | P1 |
| D7 ★ | `.Related` | 相关内容（需 `related` 配置索引） | 需实现关键词索引配置；成本较高 | P2 |
| D8 ★ | `.Paginate`（方法形式） | `.Paginator` 已有；`.Paginate` 接受集合参数 | 映射到 PaginatorView 构造 | P1 |
| D9 ★ | `.Store` / `.Scratch` | 页面级暂存（同 B6） | PageContext 加 Store | P1 |
| D10 ★ | `.Next` / `.Prev` | Hugo `.Next`/`.Prev` 是**按日期序**的邻页，与 `.NextInSection`/`.PrevInSection` 及 Flint 的 `PrevPage`/`NextPage` 语义不同 | 补 4 个键（Next/Prev/NextInSection/PrevInSection） | P1 |
| D11 ★ | `.OutputFormats` / `.AlternativeOutputFormats` | 输出格式对象集合（Name/MediaType/Permalink/Rel） | 已有 OutputFormat 字段（字符串），升级为对象集合 | P2 |
| D12 ★ | `.Pages` 集合方法 | `ByDate`/`ByWeight`/`ByTitle`/`GroupBy*`/`Reverse`/`Limit`/`Next`/`Prev` | LazyPageList 加方法投影 | P1 |
| D13 ★ | `.InSection` / `.IsTranslated` / `.Translations` / `.AllTranslations` / `.TranslationKey` | 多语言相关 | 多语言未全站支持，做空集合/恒 false 的诚实降级 | P2 |
| D14 ★ | `.Sitemap` / `.Aliases` / `.GitInfo` / `.CodeOwners` / `.Rotate` / `.HeadingsFiltered` | 边缘方法 | 按需，`GitInfo` 复用 GitDateProvider | P2 |
| D15 ★ | `.Slug` / `.Kind` / `.Path` / `.Weight`(已有) / `.Description`(已有) | `.Kind` 当前无（模板靠 is_home/is_list 判断） | 补 `kind` 字符串（home/page/section/taxonomy/term） | **P1（主题高频 `eq .Kind "home"`）** |
| D16 ★ | `.Page`（页面集合上的 `.Page`） | Pages 集合方法 | 低成本 | P2 |

### E 组 · Site 对象方法补全

| # | 缺口方法 | 方案 | 优先级 |
|---|---|---|---|
| E1 ★ | `.Home` / `.Sections` / `.AllPages` / `.MainSections` / `.GetPage` | 树查询投影（`MainSections` 读配置） | P1 |
| E2 ★ | `.Lastmod`（现名 `LastChange`） | 加 `lastmod` 键别名 | P1 |
| E3 ★ | `.Store` / `.Scratch` | 站点级 Store 对象 | P2 |
| E4 ★ | `.Config` 暴露完整配置 / `.Version` / `.BuildDrafts` / `.IsDefault` / `.LanguagePrefix` / `.Copyright` | 常量投影（Copyright 读配置） | P1 |
| E5 ★ | `.Language` 对象（LangName/LanguageName/Weight）与 `.Languages` 集合 | 多语言未全站支持，做单语言对象 | P2 |
| E6 ★ | `.Sites` / `.Dimension` / `.Role` | ✖ 多站点矩阵不做（见第六节） | — |

### F 组 · 短码系统

| # | 缺口 | Hugo 行为 | 方案 | 优先级 |
|---|---|---|---|---|
| F1 ★ | **`.Ordinal` / `.Position`** | 短码在页面中的出现序号与源码位置 | ShortcodeContext 加 ordinal/position，解析期填充 | P1 |
| F2 ★ | **`.Page` / `.Parent` / `.Site` / `.Name`** | 短码可访问当前页与父短码 | 上下文注入 page 对象（`.Name` 已有） | **P1（主题高频 `.Page`）** |
| F3 ★ | `.InnerDeindent` | 去除缩进的 inner | 对 inner 做公共缩进剥离 | P1 |
| F4 ★ | `.Scratch` / `.Store` | 短码级/页级暂存 | 复用 B6 的 Store | P2 |
| F5 ★ | `.Ref` / `.RelRef` 方法 | 短码内引用其他页 | 复用 D6 的 ref/relref | P1 |
| F6 ★ | **短码查找链** | `{name}.{lang}.{format}.{suffix}` + 子目录 + 页面路径 | 见 A11 | P1 |
| F7 ★ | **内置短码补全** | Hugo 内置 17 个：figure/highlight/gist/instagram/instagram_simple/vimeo/vimeo_simple/youtube/x/x_simple/twitter/twitter_simple/param/qr/ref/relref/details/comment | 补 `details`/`comment`/`qr`/`x`(`twitter` 别名)/`*_simple` 变体（Flint 现有 `tweet` 应同时注册为 `x`/`twitter`） | **P1（`details`/`comment` 高频）** |
| F8 ★ | `{{< >}}` 内 Markdown 控制 | `.Inner` 原样（不渲染）；`{{% %}}` 渲染 | 已对齐（双语义），确认 `.Inner \| markdownify` 组合可用 | — |

### G 组 · Render hooks 补全

| # | 缺口 | Hugo 支持 | Flint 现状 | 方案 | 优先级 |
|---|---|---|---|---|---|
| G1 ★ | `render-table.html` | 表格 | 缺 | 加表格钩子（Markdig 表格渲染器） | P1 |
| G2 ★ | `render-passthrough.html`（+ `-block`/`-inline`） | 透传内容 | 缺 | 加钩子（Markdig 自定义） | P2 |
| G3 ★ | `render-blockquote.html`（+ `-alert`/`-regular`） | 引用块与告警 | 缺 | 加钩子 | P2 |
| G4 ★ | hook 变体完整维度 | `render-codeblock-{lang}.{kind}.{layout}.{lang}.{format}.{suffix}` | 仅 `render-codeblock-{lang}` | 变体目录扫描加维度 | P2 |
| G5 ★ | hook 的页面路径层级 | `layouts/{path}/_markup/` | 见 A9 | 见 A9 | P1 |
| G6 ★ | hook 上下文变量 | `Ordinal`/`Position`（0.160+）、`Attributes`/`Options`/`Inner`/`Type` | heading/codeblock 的 ordinal 此前诚实缺（Markdig info string 限制） | Markdig 0.44 局限记档；Attributes 用 info string 原文解析（非 key=value 结构化） | P2 |
| G7 ★ | embedded render hooks | embedded `render-image`/`render-link`/`render-table`/`render-codeblock-goat` | 走 Markdig 默认 | 无需（Markdig 默认即等价），仅在需要 Goat 高亮图时补 | P2 |
| G8 ★ | `_default/_markup/` 旧路径 | 旧主题形态 | 缺（见 A10） | 见 A10 | P1 |

### H 组 · 主题分发完整性

| # | 缺口 | 说明 | 方案 | 优先级 |
|---|---|---|---|---|
| H1 ★★ | **主题 `config/_default/*` 全键合并** | 当前只合并 `params`（`ThemeParamsMerger`）；Hugo 合并主题的 `menus`/`taxonomies`/`outputs`/`markup`/`imaging`/`module`/`languages`/`pagination` 等 | 把 `ThemeParamsMerger` 扩展为"主题 config 全键深合并"（站点优先），复用现有 DeepMerge 与 ThemeConfig 读取器 | **P1（主题默认菜单/分类高频）** |
| H2 ★ | 主题 `layouts/robots.txt` / `sitemap.xml` / `404.html` 回退链 | 已支持站点级模板覆盖，确认主题目录回退 | 探测链加主题目录（`SiteBuilder.Output.cs:417` 已有 candidate 机制，扩展到全部辅助模板） | P1 |
| H3 ★ | 主题 `theme.toml` 元数据消费 | min_version 校验、features/tags/license 展示 | `ModHandler` 读取并展示；min_version 不满足时警告 | P2 |
| H4 ★ | 主题 `module.mounts` | Hugo 模块挂载点（可把任意目录挂到任意虚拟路径） | ✖ 放弃（见第六节）；`themes[]` 顺序覆盖已满足常规需求 | — |
| H5 ★ | 多主题叠加顺序核对 | `themes = [a, b]`：a 覆盖 b | 已实现（Reverse 合并）；补一条端到端测试固化语义 | P2 |
| H6 ★ | 主题自带的 `layouts/_internal/` 路径 | Hugo 内部模板路径 | ✖ 内核概念，不暴露 | — |
| H7 ★ | 主题图片/字体等二进制资源（static/） | 已支持（static 收集） | 已完成 | — |

### I 组 · 输出格式与渲染管线

| # | 缺口 | 说明 | 方案 | 优先级 |
|---|---|---|---|---|
| I1 ★ | 自定义 mediaType / 多格式定义 | `[mediaTypes]` + `[outputFormats]` 完整定义（suffix/delimiter/isPlainText/baseName/rel/mediaType） | 扩展 OutputFormat 模型（当前注册 html/rss/json 三个内置） | P2 |
| I2 ★ | 输出格式变体模板查找 | `list.rss.xml`（见 A5） | 见 A5 | P0 |
| I3 ★ | sitemapindex.xml 模板 | 分 sitemap 索引 | 单站点无需，✖ 或按需 | — |
| I4 ★ | 模板内资源管线完整链 | `resources.Get \| toCSS \| minify \| fingerprint` | 见 C8/C9（受限等价：映射既有 AssetPipeline） | P1 |

---

## 三、分批实施计划

### 批次六（P0）✅ 已完成（2026-09-10）

**A1 + A2 + A3 + A5 + A6 + A7 + A8 + B1（引擎侧）+ D 部分（taxonomy Data）**

实施内容与验收见文末「批次六实施记录」。要点：
- 新增 `PageTemplateCandidates`（候选链）+ `TemplateLookup.ResolveLayered`（分层解析）
- 渲染分派（页面/首页/分类页）改走候选链；`Resolve` 存量语义未动（双路径并存，零破坏）
- 补 `page.Data`（Singular/Plural/Terms/Pages）——Ananke `terms.html` 由报错变为可用
- 顺带修复 `ThemeCreator` 生成 Scriban 不支持的 `extends`/`block` 模板（新建主题此前必失败）
- 未做（转下批）：A4 语言变体、A9/A10 hook 页面路径、A11 短码查找链

**验收**：Ananke 目录形态四维正确路由 + 分类页 Data 产出词条；单元 835/835、集成 1646/1646。

### 批次七（P1，数据 API 主体）
**D2 + D4 + D15 + E1 + E2 + E4 + C8 + C9 + F1 + F2 + F7 + H1**

- D2（TOC/Fragments）与 D4（Resource 方法）是主题实际调用最密集的两项。
- C8（`resources.*`）依赖 Assets 管线接口暴露，C9 复用既有 Sass/esbuild 实现。
- F7 内置短码补全（`details`/`comment`/`qr`/`x`）与 F2（`.Page`）是短码侧高频项。
- H1 主题 config 全键合并，收益立竿见影（主题默认菜单/分类直接生效）。

### 批次八（P1 收尾 + P2）
**C1-C7/C11/C13 命名空间对象 + D1/D3/D5/D6/D8/D10/D12 + F3/F5/F6 + G1/G5/G8 + H2**

- 命名空间对象层是一次性批量工作（别名指向现有实现 + 补齐少量缺失函数）。
- render hooks 表格/路径层级补全。

### 批次九（P2，按需）
**A4 语言变体 + A11 短码查找链 + D7/D11/D13/D14 + E3/E5 + G2/G3/G4/G6 + H3 + I1**

---

## 四、量化现状（2026-09-10 实测）

| 指标 | Hugo | Flint（本轮前 → 本轮后） | 覆盖率（后） |
|---|---|---|---|
| 内置模板函数（全局名） | ~300 | ~136 | 45% |
| 命名空间函数对象 | 28 个命名空间 | 0（未变） | 0% |
| Page 对象方法/字段 | 86 | ~30 → ~35（+Data/Singular/Plural/Kind） | 41% |
| Site 对象方法/字段 | 27 | ~15 | 56% |
| 内置短码 | 18 | 10 | 56% |
| render hooks | 7 类（+ 变体） | 4 类 | 57% |
| **模板查找维度** | 9（layout/kind/layout2/format/all/lang/media/path/type） | 2 → **6**（kind/layout/type/section/taxonomy/format） | **67%** |
| **一级形态名识别** | 8（page/home/section/taxonomy/term/single/list/all） | 5 → **8** | **100%** |
| **Ananke 目录形态可构建性** | 全站正确路由 | 全站单一渲染 → **四维正确路由** | **可用** |
| Ananke 转换后模板解析错误 | — | 0 | — |
| Ananke 转换 TODO 标记 | — | 136 处 | — |

> 查找维度剩 3 项未做：`lang`（A4 语言变体）、`media`（A5 后缀全矩阵）、
> `path`（A9/A10 页面路径层级，render hook 专用）。

---

## 五、验收标准

1. 每批次附端到端测试（Hugo 形态主题 fixture + 站点构建断言）。
2. 每批次后跑 Ananke 迁移站：记录 RENDER 错误数与 TODO 数变化，TODO 只减不增。
3. 性能门禁 perf-gate 通过；增量构建不劣化（查找链改动后重测模板渲染基线 19.2ms）。
4. 转换器知识库同步：新支持的能力从 TODO 转自动映射（`scripts/gotmpl2scriban.py`）。
5. **兼容性声明纪律**：只有端到端跑通的能力才写入"已兼容"清单；部分等价的标注降级方式
   （空值/隐藏/注释），不用"基本兼容"这类模糊表述。

---

## 六、永久放弃（代价 > 价值，记录决策避免反复）

| 项 | 不做的理由 |
|---|---|
| Go template 语法原生执行 | Scriban 引擎语义不同，须靠转换器；引擎级兼容层成本不成比例 |
| Hugo Modules（Go modules 图 / MVS / vendor） | 语言生态不同；lockfile + SHA256 已达同等安全底线 |
| `module.mounts` 任意挂载点 | 常规主题分发由 `themes[]` 顺序覆盖满足；挂载点引入的路径映射复杂度与收益不匹配 |
| content adapters（`_content.gotmpl`） | 需执行 Go 模板生成内容树；无生态采用 |
| partial decorators（`templates.Inner`，v0.154+） | 新特性，生态未采用 |
| `images.*` 全滤镜集（25 个） | 依赖 Go 图像库语义；Flint ImageProcessor 覆盖 Resize/Fit/Fill/Crop 高频四类即可 |
| `diagrams.*` / `openapi3.*` | 需外部工具/Go 库 |
| 多站点矩阵（version/role 维度、`.Sites`） | 无需求；页面树只保留 language 维度 |
| asciidoc/rst/pandoc/org 外部渲染器 | 外部二进制依赖与 Native AOT 单文件目标冲突 |
| `layouts/_internal/` 内部模板路径 | Hugo 内核概念，不对外 |

---

## 七、风险与前置裁决项

### 7.1 模板查找层：实测证据（2026-09-10，Flint Release 构建探针）

上节曾推断"重写会影响依赖旧行为的站点"。**实测推翻该推断**——现状不是"另一种合理设计"，
而是**三重缺陷**，且被 Hugo 官方 starter theme Ananke 实测证实不可用。

**证据 1：Ananke 官方主题正在使用被 Flint 忽略的模板名**（`benchmarks/corpus/gohugo-theme-ananke/layouts/`）：

```
baseof.html        ← 一级形态（Flint 可命中）
home.html          ← 一级形态（Flint 不识别）
list.html / single.html / taxonomy.html / terms.html
page/single.html   ← section/type 目录
post/list.html     ← section/type 目录
post/summary.html  ← 视图模板
```

**证据 2：用 Ananke 目录形态实测，全站被单一模板渲染**

```
探针：layouts/{home.html, page/single.html, post/list.html, _default/single.html}
构建：EXIT=0，无报错

首页 index.html      → ANANKE-PAGE-SINGLE
posts/a/index.html   → ANANKE-PAGE-SINGLE
pages/about/index.html → ANANKE-PAGE-SINGLE
```

首页、post 详情、page 详情**全部**由 `page/single.html` 渲染。`home.html` 未被识别。

**证据 3：`home.html` 不识别导致首页渲染为空**

```
探针：仅 layouts/home.html + layouts/_default/list.html
首页 index.html → 空（构建"成功"）
根因：SiteBuilder.Render.cs:275 `"home" => TemplateExists("index") ? "index" : "single"`——
      请求名硬编码为 index，再回退 single；TemplateLookup.InferKind 的 switch 只认
      single/list/index/taxonomy/term，不认 home/page/all
```

**证据 4：站点 `_default` 永久压过主题 section 模板**（interleave 语义错位）

```
探针：站点 layouts/_default/single.html  vs  主题 themes/mytheme/layouts/posts/single.html
Flint 结果：SITE-DEFAULT-SINGLE（站点 _default 胜）
Hugo 预期：主题 posts/single.html 胜（候选路径 posts/ 优先于 _default/）

根因：TemplateLookup.Score 中根序占 100 分（`100 - RootOrder*10`），
      跨根差异恒大于名/格式分，使根序成为第一判据。
Hugo 语义：候选路径特异性（distance）是第一判据，站点/主题根序是次要判据。
影响：SiteCreator 默认生成 layouts/_default/single.html——意味着**任何 Flint 站点下，
      主题的 section 布局都永久失效**（缺陷 A 的镜像面）。
```

**证据 5：真实主题确认多 section 模板共存**
`benchmarks/corpus/` 下主题含 `post`(6) / `posts`(3) / `page`(3) / `docs`(2) 目录模板。
多 section 模板共存时，Flint 让它们互相污染，胜者由**路径字典序**决定（`docs` 压过 `posts`），
结果不可预测。

**缺陷归因（模型层面）**：模板查找的本质是"给定页面，找最匹配的模板"（页面向量 → 模板）。
现有实现是"给定模板名，找最匹配的文件"（名字 → 文件）。**缺少页面上下文维度**是模型错位，
不是参数调整能修的。

### 7.2 BREAKING 风险评估（修正后）

| 依赖面 | 核查结果 | 风险 |
|---|---|---|
| 测试断言 | `grep 'layouts/posts\|layouts/blog\|posts/single' tests/` → 空 | 无 |
| README / docs 指导 | `grep` README.md + docs/ → 仅本方案文档提及 | 无 |
| 官方脚手架 | `SiteCreator`/`ThemeCreator` 只生成 `layouts/_default/` | 无 |
| "将错就错"站点 | 用户可能发现 section 模板全局生效而就地使用 | **低但存在** |

**修正结论**：BREAKING 面比推断值**小**（无测试/文档/脚手架依赖），
但现状问题的严重性**大**（主流主题完全不可用，非"部分站点输出变化"）。

### 7.3 实施记录：双路径并存（比原"两步走"方案更优，零破坏）

原方案设想"先翻 Score 判据、再引入页面作用域"（两步，第二步有破坏性）。
**实施时发现更优解**：不修改 `Resolve` 的既有语义，而是**新增**页面感知路径
`RenderPageAsync` + `TemplateLookup.ResolveLayered`，两条路径按场景分工：

| 路径 | 适用场景 | 判据 | 语义 |
|---|---|---|---|
| `Resolve`（存量，未改动） | partial / render hook / 视图（**无页面上下文**） | 根序优先 | 站点覆盖主题 —— **保持不变** |
| `ResolveLayered`（新增） | **页面布局**（候选链） | 候选级优先、根序次之 | 对齐 Hugo distance/interleave |

这样做的效果：
- 修复证据 1/3/4（主题 section 生效、`home.html` 识别、跨 section 隔离）
- `Resolve` 的存量测试（`TemplateLookupSnapshotTests`/`PriorityTests`）语义完全不变
- **无破坏性变更**：原担心的"依赖 section 模板全局生效的站点需迁移"不成立——
  该用法在修复前本就不正确工作（`docs` 页命中 `posts/single.html` 是 bug 不是契约）
- `Score` 无需改动（原方案 7.3 步骤 1 作废：新路径不经过加权器）

BREAKING 面实测（重构后复核）：测试断言无依赖、README/docs 无指导、
`SiteCreator`/`ThemeCreator` 只生成 `_default/`——**确认为零破坏**。

| 项 | 风险 | 处置 |
|---|---|---|
| 双路径并存 | 两套判据可能语义冲突 | 已按场景隔离：页面布局一律走 `ResolveLayered`，其余走 `Resolve`；`ResolveLayered` 不复用加权器 |
| 命名空间对象层（C 组） | 注册表膨胀（约 28 个对象 + 300 个键） | 用共享 ScriptObject + 惰性构造；实测注册开销后再决定是否按需构建 |
| TOC 结构化（D2） | 与 `render-heading` 钩子联动（id 生成顺序） | 先实现基础标题树，钩子联动作为后续 |
| Resource 方法（D4） | 图像变换的磁盘缓存与 AOT 兼容 | 复用 DiskAssetCache（已有），避免新增反射路径 |

---

## 批次六实施记录（2026-09-10）

### 交付内容

| 文件 | 变更 |
|---|---|
| `Templates/PageTemplateCandidates.cs` | **新增**：候选链纯函数（kind/layout/type/section/taxonomy/outputformat 六维），含 `TemplateCandidate`/`PageTemplateQuery` 记录类型 |
| `Templates/TemplateLookup.cs` | **新增** `ResolveLayered` + 派生路径索引（复用描述符快照，热路径零 IO）+ `Invalidate` 同步清理 |
| `Abstractions/ITemplateRenderer.cs` | **新增** `RenderPageAsync`/`PageTemplateExists` 接口方法；`PageContext` 新增 `Kind`/`DeclaredType`/`TaxonomyName`/`TaxonomySingular`/`TaxonomyPlural` |
| `Templates/ScribanTemplateRenderer.cs` | **新增** `RenderPageAsync`（候选链 → 物理模板/内置兜底）、`RenderLoadedAsync`、`GetOrLoadTemplateAtPathAsync`、`RenderBuiltinAsync`、`ToLogicalName` |
| `Templates/ScribanTemplateRenderer.Objects.cs` | **新增** `BuildPageDataObject`/`LazyTermPage`/`LazyTermsMap`/`ToRelPermalink`（Hugo `.Data` 语义） |
| `Site/SiteBuilder.Render.cs` | 渲染分派改走候选链（页面/首页/分类页），移除硬编码 `index`/`single`/`list` 回退分支 |
| `Site/SiteBuilder.Tree.cs` | 填充 `Kind`/`DeclaredType` |
| `Site/TaxonomyService.cs` | `TaxonomyPageInfo` 增 `TaxonomySingular`/`TaxonomyPlural`；新增 `GetTaxonomyNames` 访问器 |
| `Cli/Handlers/ThemeCreator.cs` | **修复**：生成的模板原用 Scriban 不支持的 `extends`/`block`，改为 `capture` + 命名参数 `include` |

### 实测验收（Ananke 目录形态）

```
候选链四维正确路由（修复前全站被 page/single.html 单一渲染）：
  layouts/home.html          → 首页 index.html
  layouts/page/single.html   → type: page 的页面
  layouts/post/list.html     → content/post/ 的 section 列表
  layouts/_default/single.html → 无特定模板的普通页

分类数据（修复前 terms.html 报 "page.Data.pages for a null object"）：
  /tags/       → TERMS s=tag p=tags [alpha@/tags/alpha/][beta@/tags/beta/]
  /tags/alpha/ → 内容页集合 One;Two;

真实 Ananke 探针站：构建成功，16 页面 / 199 资源，无 TAXONOMY001
```

### 确证的两个独立缺陷（本轮顺带修复/记录）

1. **Scriban 不支持 `block`/`extends`**（实证：`Template.Parse` 报
   "Found `<end>` statement without a corresponding beginning of a block"；
   渲染期报 "The function `extends` was not found"）。正确继承形态是
   `{{ capture content }}...{{ end }}{{ include "baseof.html" content: content }}`。
   → 已修复 `ThemeCreator`（新建主题此前**开箱即构建失败**）。
2. **转换器 `block`/`define` 处理缺失**（Ananke 产物无 `<!DOCTYPE html>` 的根因）。
   原实现：`block "main" .` 未被识别→原样保留为字面文本；`define "main"` → 自执行
   `func main()...{{ main() }}`，与 baseof 完全脱节。
   → 已修复 `scripts/gotmpl2scriban.py` 三处：
   - `block "x" .`（含默认内容）→ `capture __def_x` + 块结束处
     `{{ if $.blk_x }}{{ $.blk_x }}{{ else }}{{ __def_x }}{{ end }}`
   - `define "x"`（简单名）→ `capture blk_x` + 文件末尾
     `{{ include "baseof.html" blk_x: blk_x ... }}`（Scriban 命名参数以空白分隔，**无逗号**）
   - `template "x"`（调用非声明）→ `include "x"`（原误转为 `block "x"`）
   - 内联 partial 定义（`define "_partials/x.html"`）→ TODO 降级并保持 if/end 配对
   - `__blockdef__` 加入 `NON_CTX_FRAMES`（否则块内裸点被误映射为 `title.params.title`）

   实测验收（最小探针）：baseof 三块 + single 三 define → 产物含完整
   `<!DOCTYPE html>`，title/header/main 均被子模板正确覆写。

   Ananke 复核：**所有模板解析通过**（此前 baseof/single/home/list 全部失败）；
   剩余运行期错误为 `css.Build`/`resources.*`/`.Data.Integrity`（即 C8/C9/C 组
   命名空间缺口，非查找层问题），需批次七/八交付后 Ananke 才能端到端渲染。

### 测试

- `Templates/PageTemplateLookupTests.cs`：**新增** 19 条（候选链生成 10 + 分层解析 9）
- `Site/PageAwareLookupE2ETests.cs`：**新增** 6 条端到端（根序/隔离/home/type/Data/格式变体）
- 单元 **835/835**（1 skip 为环境依赖）、集成 **1646/1646** 全绿

---

## 历史批次（前三轮实施记录）

### 批次三 ✅
A3（static 前缀剥离）+ A1 部分（主题 params）+ B1/B2/B3/B4 + C1 前置（分页模板）。

### 批次四 ✅
B5（`.Resources` 装配）+ B6 前置 + B7/B8（集合与构造函数）+ B9/B10（内容/URL 函数补全）。

### 批次五 ✅
A2 + A3 + A4（主题分发完整性）+ C1（分页多页）+ C4（视图查找序核对）。

同时清零 Ananke 转换器残留：Scriban `with` 无 `else`、`with $x = expr` 非法目标、
多行布尔链、`.Related`/`.Scratch` 等深度语义降级为可追溯 TODO。Ananke 探针站
从 12 个构建错误降到 0。

### 批次五端到端实证（Ananke 探针站，paginate=3，8 篇文章）
```
EXIT=0，页面 16，资源 199
public/: 404, categories/, tags/, index.html, page/2/, page/3/,
         posts/index.html, posts/page/2/, posts/page/3/, posts/post-1..8/
分页导航：pagination-default 模板，First/Prev/页码槽/Next/Last 齐全
```

### Ananke 迁移的已知降级（非错误）
`.Related`、`.Scratch`/`.Store`、`templates.Defer`、`collections.Dictionary` 传参 partial、
多行布尔链、`compare.Conditional`、`fmt.Printf`/`Warnf`、`urls.RelURL`、`resources.Get`、
`.GetTerms`/`.Translations`/`.Resources.*`/`.File.*`（部分）。本方案 A-F 组即针对这批降级的清偿路径。

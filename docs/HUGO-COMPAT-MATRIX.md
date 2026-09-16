# Hugo 兼容差异清单（常驻对照表）

> 用途：把"Go 模板 / Hugo 引擎"与"Flint 渲染器"之间的**语义差异**逐类登记在案——
> 每类都写明 Hugo 的真实语义、Flint 当前处理、证据来源与**可执行回归**的位置。
> 新增差异必须在本文件登记并配回归测试；差异修复后不得删行，只能改状态（防重犯）。
>
> 证据基准：**Hugo v0.166.0 extended**（`tools/hugo-bin/hugo.exe`）+ Scriban 7.4.0。
> 文中"实测"均指探针脚本的真实输出，脚本形态见文末「探针方法」。
> 与 `THEME-MIGRATOR-PLAN.md` 的关系：那份是**逐轮施工日志**（按批次记录做了什么），
> 本文是**按差异类别组织的常驻清单**（现状 + 证据 + 回归位置）。

## 一、类别总览

| # | 类别 | Hugo 语义 | Flint 处理 | 回归测试 |
|---|------|-----------|-----------|----------|
| A | 真值判定 | 数字 0 / 空串 / 空集合 / nil / false **为假** | `FlintScribanContext.ToBool` 覆盖 Scriban 默认真值 | `HugoTemplateTruthinessTests`（16 例） |
| B | `and`/`or` | **惰性短路**且**返回操作数本身** | 转换器产出取值三元链（分支惰性） | `MigratorTests.逻辑函数转惰性取值/取值or保留操作数/取值and返回首个假值否则末值`、`HugoCompatSemanticsTests.取值三元链与Hugo一致/惰性等价形态不触碰未取分支` |
| C | `default` 空值判据 | `""`/`0`/空集合取兜底；`false` **保留** | `IsEmptyForDefault`（与"真值"刻意不同） | `HugoCompatSemanticsTests.Default的判据与Hugo一致` |
| D | `index` 数值键 | 列表下标接受整数（含变量与算术结果） | `SeqIndex` 先判数值键再走字典分支 | `HugoCompatSemanticsTests.Index的数值键可用` |
| E | `Scratch.Add` | 首个值原样存、后续按类型累加；**无返回值** | `AddValue` + `StoreFunction.AddAnd` 返回空串 | `EngineCapabilityTests.Add按Hugo语义累加/Add不产出文本`、`HugoCompatSemanticsTests.ScratchAdd按Hugo语义累加/Scratch里的页面列表可继续取值` |
| E′ | `lt`/`le`/`gt`/`ge` 跨类型比较 | 数值类型互通（int vs double）、数字串被强制、非数字串按类型序排在数值**之前** | `CompareHugo`（`lt/le/gt/ge` 与 `num_*` 共用一套） | `HugoCompatSemanticsTests.比较函数按Hugo语义跨类型/比较函数不再抛Int32装箱异常` |
| F | 管道参数序 | `X \| f A` = `f A X`（左值作**末参**） | `PipeValueLastFunctions` + `FoldWithLeft` 反序 | `PipeAndParserRegressionTests.管道左值作末参改写为显式调用/管道比较方向与Hugo一致/管道逻辑取值方向与Hugo一致` |
| G | 模板查找顺序 | 五类 kind 各有确定候选序（见第三节） | `PageTemplateCandidates.Build*` 按实测序产出 | `PageTemplateLookupTests`（候选链 + 分层解析） |
| H | 页面集合与分页产物 | `.Pages`/`.RegularPages` 默认日期降序；`ByDate` 升序；分页由模板调用驱动 | `SiteBuilder.Tree` + `PageListFunctions` + 分页注册表 | `PageCollectionAndParamsTests`、`PageAwareLookupE2ETests` |
| I | 分页尺寸来源 | **`[pagination] pagerSize`**（v0.128+；顶层 `paginate` 被忽略）；`.Paginate $pages N` 的第二参覆盖站点值，且 `/page/N/` 的生成也按该尺寸 | `ConfigParser` 读新键（旧键兜底）+ `PagePaginateFunction` 解析第二参 + 注册表把尺寸传到站点级生成 | `HugoCompatSemanticsTests.Paginate显式页大小生效/显式尺寸时每页内容按该尺寸切` |

## 二、本轮（第二十三轮）新增/修正的四项

### B. `and`/`or` 既惰性又**取值**

此前产出布尔常量（`(A) ? true : ((B) ? true : false)`），只在条件语境正确；
**赋值语境会丢值**——21 主题语料里有 74 处 `{{ $x := or A B }}` 形态
（stack 的 `$site_author`、PaperMod 的 `$title`、FixIt 的 `$source`、
`$title := or .Attributes.title ""` 等），那些变量此前拿到字符串 `"true"`。

Hugo v0.166 实测：

| 表达式 | 结果 |
|---|---|
| `or "" 0` | `0`（全假 → **末值**） |
| `or "a" "b"` | `a` |
| `or 0 ""` | 空串 |
| `and "a" "b"` | `b` |
| `and 1 0 2` | `0`（首个假值） |
| `and "a" "b" "c"` | `c`（全真 → 末值） |
| `if (or (eq 1 1) (div 1 0))` | `T`（**短路**，未触已达分支） |
| `if (or (eq 1 2) (div 1 0))` | **报错**（前项为假 → 逐项求值到 `div 1 0`） |

修法：三元链保留取值语义，被选中的操作数文本重复一次
（Scriban 只求值被选中的分支，故副作用不重复）：
`or A B …` → `(A) ? (A) : ((B) ? (B) : C)`；
`and A B …` → `(A) ? ((B) ? C : (B)) : (A)`。
错误行为也随之对齐：`or false (div 1 0)` 仍报错（与 Hugo 相同）。

### B′. 管道形态的方向

管道左值作末参，故 `X | or Y` = `or Y X`。实测（顺序敏感形态才测得出方向）：
`1 | gt 2` → `true`（= `gt 2 1`）、`"A" | or "B"` → `"B"`、`"A" | and "B"` → `"A"`。
此前按 `(左 op 右)` 拼接 → `1 | gt 2` 产出 `(1 > 2)`（`false`），方向做反。
21 主题语料里没有 `| gt/ge/lt/le` 写法（`eq`/`ne` 可交换，方向无影响），
故属潜伏错误，本轮一并修正。

### E. `Scratch.Add` 的返回值

Hugo 的 `Add`/`Set`/`Delete` 无返回值 → 模板里 `{{ $s.Add "k" v }}` 渲染为空。
Flint 此前 `Add` 返回存入值，使每个"只调用不接收"的 Add 都往页面吐文本
（`{{ $s.add "n" 5 }}` 实测输出 `5`）。已改为返回空串（`Set`/`Delete` 早已如此）。

### E′. 比较函数的跨类型语义（已修：解除了 `.Type`/mainSections 的引擎侧阻塞）

`lt`/`le`/`gt`/`ge` 曾用 `IComparable.CompareTo`。装箱的 `Int32` 与 `Double` 相比时
`int.CompareTo(object)` 抛 **"Object must be of type Int32"**——而这正是 ananke
`home.html(26,13)` 那行未转换代码 `{{ if compare.Ge $section_count (math.add $n_posts 1) }}`
的报错来源（`math.add` 产出 double）。引擎侧探针：

| 表达式 | 修前 | 修后 |
|---|---|---|
| `{{ compare.Ge 5 4 }}` | `true` | `true` |
| `{{ compare.Ge 5 (math.add 3 1) }}` | **报错 Int32** | `true` |
| `{{ $x = math.add 3 1 }}{{ compare.Ge 5 $x }}` | **报错 Int32** | `true` |
| `{{ num_ge 5 (math.add 3 1) }}` | `true`（走宽容实现） | `true` |

改为统一的 `CompareHugo`（`lt/le/gt/ge` 与 `num_*` 共用），Hugo v0.166 实测语义：

| 表达式 | 结果 | 规则 |
|---|---|---|
| `lt 1 2.5` / `ge 3 3.0` / `ge 3 3.5` | `true` / `true` / `false` | 两侧可数值化 → 数值比较 |
| `gt "5" 0` | `true` | 数字串被强制成数值 |
| `lt "B" 3` / `gt "B" 3` | `true` / `false` | 非数字串 **< 数值**（类型序） |
| `lt "a" "b"` | `true` | 两侧非数字 → 序号比较 |
| `eq 5 5.0` / `eq 1 "1"` | `false` / `false` | `eq`/`ne` 不做数值强转 |

### I. 分页（本轮修复三项，均以 Hugo v0.166 探针为准）

1. **配置键改名**：Hugo v0.128+ 用 `[pagination] pagerSize`，**顶层 `paginate` 已被忽略**
   （实测：顶层 `paginate = 2` + 3 篇文章 → 单页 `n=1`、无 `/page/2/`；
   `[pagination] pagerSize = 2` → `n=2`）。Flint 现读新键、旧键兜底、默认 10。
2. **`.Paginate $pages N` 的第二参**：显式页大小覆盖站点值（实测 `n=1` vs 不带第二参 `n=2`）。
   loveit 的 home 传主题配置 `params.home.posts.paginate = 6`（主题 `hugo.toml` 的
   `[params]` 合并进站点——Flint 的 `ThemeParamsMerger` 已实现该合并，本轮验证其必要性）。
3. **站点级 `/page/N/` 的生成要按模板实际用的尺寸**：注册表从"只记集合"改为
   "集合 + 尺寸"，否则页数按站点配置算，与模板分页结果不一致（表现为多出一页）。

### G. 模板查找顺序（逐级淘汰实测）

方法：把候选文件全部建出、内容写成自己的相对路径，构建后读输出得知胜出者，
删掉胜者重跑——如此得到**完整序**而非单点比较。

**首页（kind=home）**

```
_default/home → _default/index → home → index → _default/list → list → _default/all → all
```

`home` 在**每个形态内**都优先于 `index`（隔离对探：`home.html` 胜 `index.html`）。

**段页（kind=section，section=posts）**

```
posts/section → posts/list → section/section → section/list
→ _default/section → section → _default/list → list → _default/all → all
```

**普通页（kind=page，section=posts）**

```
posts/page → posts/single → _default/page → page → _default/single → single
→ _default/all → all
```

`page`（kind 等价名）在**每个位置**都优先于 `single`。

**taxonomy 列表页（kind=taxonomy，/tags/）**

```
tags/terms → tags/taxonomy → tags/list
→ taxonomy/terms → taxonomy/taxonomy → taxonomy/list
→ _default/terms → _default/taxonomy → taxonomy → _default/list → list → _default/all → all
```

两处反直觉但实测确凿：同形态内 **`terms` 优先于 `taxonomy`**（`_default/terms.html`
胜 `_default/taxonomy.html`——even 主题两文件俱全）；**根级 `terms.html` 不是候选**
（只有 `_default/terms` 与根级 `taxonomy.html` 是）——hugo-coder / xmin 只有根级
`terms.html`，Hugo 对 /tags/ 报 "found no layout file for kind taxonomy"。

**term 词条页（kind=term，/tags/词条/）**

```
tags/term → tags/list → term/term → term/list → taxonomy/term
→ _default/taxonomy → _default/term → term → _default/list → list → _default/all → all
```

字面 `term/` 目录级**排在** `taxonomy/` 字面目录级之前；`_default/taxonomy` 先于
`_default/term`（kind 名让位）。**不是**候选的：`{taxonomy}/terms`、
`{taxonomy}/taxonomy`、`taxonomy/list`、`_default/terms`、根级 `taxonomy.html`、
根级 `terms.html`（隔离对探：只放 `taxonomy.html` 时 Hugo 报
"no layout file for kind term"）。

**两条跨 kind 的通用规则**

1. **裸名**（无斜杠）的 `_default/` 形态先于根形态——`_default/section.html` >
   `section.html`、`_default/list.html` > `list.html`、`_default/all.html` > `all.html`、
   `_default/foo.html`（`layout: foo`）> `foo.html`，逐对隔离验证。
2. 候选级顺序（specificity）先于站点/主题根序，站点与主题的 `layouts/` 交错查找。

21 主题语料里**没有任何裸名同时具备两种形态**，故规则 1 对现有主题零影响；
它只在"主题同时提供两种形态"时改变结果（Hugo 选 `_default/` 那个）。

## 三、已知未支持 / 有意的差异| 项 | 状态 | 说明 |
|---|---|---|
| `.Type` 的 section 语义 | **已对齐** | `/` → `page`、`/posts/` → `posts`、`/tags/x/` → `tags`、`/about/` → `page`、front matter `type` 覆盖；实现 = `Metadata.Type ?? (Section 非空 ? Section : "page")` |
| `site.Params.mainSections` 默认值 | **已对齐** | 常规页最多的段（单元素）；并列取字典序最小；全根级页时为空；显式 `[params] mainSections` 优先 |
| 命名空间调用（`compare.*`/`math.*`/`collections.*`…） | **已对齐（保形转换）** | 迁移产物里**逐字保留**主题写法——命名空间调用在 Go 与 Scriban 里同形，且 Flint 引擎有同名命名空间（`compare`/`math`/`collections`/`strings`/`path`/`urls`）。此前记为"未转换/待办"是**误判**：当时 `compare.Ge 5 (math.add 3 1)` 报 "Object must be of type Int32"，根因是比较函数的 `CompareTo` 装箱缺陷（已修），与调用形态无关。引擎探针逐条确认：`compare.Ge 5 (math.add 3 1)` → true、`if (compare.Ge 5 (math.add 3 1))` → 进入、`math.add 3 (math.mul 2 2)` → 7、`collections.Delimit (slice "a" "b") ", "` → "a, b" |
| `single` 兜底级（home/section） | Flint 扩展 | Hugo 无此级；仅服务"只有 single.html 的极简站点"，排在 `all` 之后 |
| `Scratch.Get` 取回的页面列表带方法族 | Flint 超集 | Hugo 下 `.First` 之类在 Scratch 取出后渲染为空；Flint 多给一层方法族，不冲突 |
| `page.terms`（分类页） | Flint 扩展 | Hugo v0.166 的 /tags/ 页**没有** `.Terms`（实测报 "can't evaluate field Terms"），词条在 `.Pages` 里；Flint 两个都提供 |
| 分页 `/page/N/` 的产生 | 已对齐 | 仅在模板真的调用 `.Paginate`/`.Paginator` 时产出（Hugo 同） |
| `SitemapOptions/FeedOptions.ExcludedTypes` | 按 `.Type` 过滤 | 即 front matter type 或段名（Hugo 的 `.Type` 语义）；不是 kind 名 |

## 四、探针方法（复现指南）

三种手法，按"要回答什么问题"选用：

1. **单行 fixture**：改 `layouts/index.html`（或对应 kind 的模板）为一行表达式，
   `hugo --quiet --destination <tmp>` 后读产物。用于求值语义
   （真值、`and`/`or` 返回值、`default` 判据、`Scratch` 累加、`index` 键类型）。
   表达式出错的场景**保留 stderr**——`div 1 0` 这类"该报错就报错"的行为也是差异项。
2. **隔离对探**：候选池里只留两个文件（内容=自己的路径），构建读输出来判先后。
   用于形态优先级（`_default/x` vs `x`、`home` vs `index`）。
3. **逐级淘汰**：全部候选建出 → 构建 → 读输出得到胜者 → 删胜者 → 重复。
   用于完整候选序（五类 kind 的表就是这样得到的）。注意产物路径要对得上
   kind（`/tags/` vs `/tags/x/`，写错会得到"看起来像另一类页面"的假结果）。

## 五、维护规则

1. **先探针后改码**：任何"对齐 Hugo"的改动必须先有探针输出，期望值不得凭推理写。
2. **删不掉的行**：本清单的行只改状态（已修/未修/超集），不删除——被修过的差异重犯过。
3. **每条差异配回归**：新登记的差异必须同时落在某个测试类里（表内"回归测试"列），
   否则视为未登记。
4. **断言带证据来源**：测试注释里写清"实测了什么、用什么手法"，便于下轮复核。
5. **[推断] 要标注**：未逐项实测的部分（如字面 `taxonomy/` 目录级的 `terms` 位置）
   在代码注释与本清单里标 `[推断]`，不得写成实测结论。

### J. 分页后 `.Pages` 保持全集（本轮第五项修复）

Hugo v0.166 实测（3 篇文章、`pagerSize = 2`）：分页页上的 `.Pages` **仍是完整集合**，
当前页切片只在 `.Paginator.Pages`：

```
首页/列表第 1 页: tp=2  pagesLen=3  pagerItems=2   regular=3
      第 2 页: tp=2  pagesLen=3  pagerItems=1   regular=3
```

Flint 的 `PageContext.WithPaginator` 曾把 `.Pages` 换成当前页切片（注释写"pager 页面的
.Pages 即该页切片"，与实测不符）——于是"段页按 `.Pages` 过滤/计数"的主题看到被截断的
集合（papermod 的 `union .RegularPages .Sections` 得到 2 篇而非 3 篇），`/posts/page/2/`
永不生成 → 门禁④报不对称。改为保留全集后 papermod 恢复对称（24/24）。

另外 `.Paginate <空集合>` 必须是"空分页器"而不是"回落到本页 Pages"：
blog-awesome 的列表页调用 `.Paginate (where .Pages "Section" "blog")`（过滤结果为空），
回落会让 3 篇文章按 2 分页、多出 `/page/2/`。空序列现按合法（空）集合处理并登记。

### K. 自定义分类与分类页标题（本轮第六项修复）

两项均以 Hugo v0.166 探针为准：

1. **自定义分类从不产出**（真 bug）：Flint 的分类服务构造时**从不传站点配置**，
   注册表恒为默认 tags/categories → 站点声明 `[taxonomies] series = "my-series"`
   时 `/my-series/` 永不生成（探针：内容含 my-series/moods 时 Flint 只产出
   tags/categories，Hugo 产出全部四个）。修法：
   - `TaxonomyService.FromConfigured`：**键=单数、值=复数**，注册表按**复数**索引
     （复数名同时是 front matter 字段名与 URL 段）
   - 三处构造点（SiteBuilder / Incremental / Render）传入 `config.Taxonomies`
   - **声明即替换默认**（实测：只声明 `series = "my-series"` 时，即便页面有 tags
     也不产出 `/tags/`）——为此给 `TaxonomyConfig` 加 `Declared` 标志区分"未声明"
   - **主题级 `[taxonomies]` 会合并进站点**（FixIt 的 hugo.toml 声明
     `collection = "collections"`）：站点未声明时采用主题的（站点声明过则站点优先，
     对齐 Hugo 的 `_merge = "shallow"` 整体替换语义）；多主题时取优先级最高的那个（[推断]）

2. **分类页标题大小写**：Hugo 实测 `Title` 是"Title 化"的结果——
   taxonomy 列表页把复数名的 `-` 换空格再 Title 化（`/my-series/` → "My Series"），
   term 页对词条值 Title 化但**保留连字符**（`/my-series/first-run/` → "First-Run"）。
   采用 Go `strings.Title` 语义（只大写"非字母数字之后的首字母"，其余原样）。
   此前两者都直接输出原名（`tags`/`alpha`）。

### L. 转换器"不支持"清零（本轮第七项）——全语料体检结果

体检方法：21 个主题逐个 `Flint.ThemeMigrator` 迁移 + `--report`，
聚合 `SUMMARY` 的 `unsupported`/`downgraded` 与逐文件注记。

| 项 | 修前 | 修后 |
|---|---|---|
| 不支持（原样保留 Go 文本 / 条件降为 false） | 22 处 | **0 处** |
| 降级（转了但有已知语义差异） | 1102 处 | 1102 处（不变） |

修掉的两类"不支持"：

1. **Go 反引号原始串含 `"` / `\`**（12 处）：Go 原始串不做转义，Scriban 里反引号不是
   字符串定界符 → 原样输出被结构检查拦下（"引号不平衡"）并整行回退成 Go 原文。
   现转成 Scriban 双引号串并转义（`\`→`\`、`"`→`\"`、换行/制表 → `\n`/`\t`）。
   例：`` `id="([^"]*)"` `` → `"id=\"([^\"]*)\""`；`` `\s+width="[^"]*"` `` → `"\s+width=\"[^\"]*\""`
2. **管道形态 `X | not`**（10 处）：未折叠 → 产出 0 参 `not` → 条件回退成 `false`，
   判断块被**静默丢弃**。现折叠为 `!(X)`（`X | not Y` 在 Hugo 下非法，保持不支持）

**降级**仍是 1102 处，按注明细分五类（数值为表达式级计数；报告里按文件去重后 517 条）：

| 类别 | 处数 | 语义缺口 |
|---|---|---|
| `partial` 上下文参数以 page 绑定 | 231+59 | `partial "x" (dict …)` 的 dot 语义——Flint 直接绑定该 dict，Hugo 亦如此，属**等价改写** |
| `printf` 格式串按 Go 语义传入 | 145 | Go 的 `%q`/`%v` 等动词与 .NET 格式化不完全等价，运行时按 Go 语义解释 |
| 内联 partial 提取为独立文件 | 38 | `{{ define }}` 提取成 partial 文件（结构变化，语义等价） |
| 动态 partial 名（运行期解析） | 28+14 | `partial (printf …)` 目标名运行期才算得出，转换期无法静态校验 |
| 资源上下文裸方法（补 `page.resources.`） | 2 | `with .Resources.ByType` 块内 `.GetMatch` 的隐式接收者显式化 |

逐主题降级分布（前五）：fixit 236、blowfish 201、narrow 198、hugo-book 93、stack 81；
github-style / techdoc / xmin 为 0。

### M. `printf` 改用 Go fmt 语义（本轮第八项）

原实现把 Go 动词**翻译成 .NET 复合格式**再 `string.Format`，两类语义表达不出来：

| 形态 | Go/Hugo（v0.166 探针） | 旧实现（.NET 翻译层） |
|---|---|---|
| `%v` 切片 | `[1 a true]` | `System.Collections.Generic.List…`（类型名） |
| `%v` 映射 | `map[a:1 b:x]`（键排序） | 类型名 |
| `%v` bool | `true` | `True` |
| `%v` nil | `<nil>` | 空 |
| `%q` 字符串 | `"a\"b"`（转义） | `"a"b"`（只加引号） |
| `%q` 切片 | `["x"]`（逐元素） | `"x"`（整体引号化） |
| `%s`/`%d` 类型不符 | `%!s(int=5)` 标记 | 数字照打（`5`） |
| `%T` | `int` / `[]int` / `map[string]interface {}` | 打进 `{n}` 打印值 |

语料实测（21 主题、873 个格式动词）：`%s` 464、`%v` 241、`%q` 97、`%d` 55，
其余 `%T`(8)/`%#v`(2)/`%g`(2)/`%.2f`/`%02d`/`%5s` 等零头——**40% 走的是翻译层表达
不出来的语义**。

新实现 `Templates/GoPrintf.cs`（`printf`/`warnf`/`errorf`/`erroridf`/`warnidf`
全部改走它，旧的 `GoFormatToDotNet` 翻译层删除）：

- `%v`/`%#v`：复合值按 Go 语法渲染（切片 `[a b]`、映射键排序、bool 小写、nil `<nil>`、
  浮点最短往返 + 小写 `e`）；`%#v` 与 `%T` 按元素推断切片类型（`[]int`/`[]string`/`[]bool`/`[]interface {}`）
- `%q`：Go 转义（`\"`、`\`、`\n`、控制字符 `\xNN`）、数值按 rune 字面量、复合值逐元素
- `%s/%d/%t/%f/%e/%g/%x/%X/%o/%b/%T` + 宽度/精度/标志（`%02d`、`%5s`、`%-5s`、`%.2f`）
- 缺参 `%!v(MISSING)`、多余参 `%!(EXTRA type=value)`、`%%` 转义

**两处有意偏离 Go**（均由语料验证必要性）：

1. `%s`/`%d` 对**数值族**归一（Go 的 `%d` 遇 float64 产 `%!d(float64=5.7)`）——同一值在
   两引擎内部类型不同，严格照类型打标记会无谓增多；非数值仍产 `%!d(string=5)`（与 Go 一致）。
2. **nil 渲染空串**（Go 打 `%!s(<nil>)`）：标记会被写进 class/属性/URL。实测 ananke 的
   `$post_class = printf "page-%s" .ContentBaseName` 在值缺失时产出
   `class="page-%!s(<nil>=<nil>)"` → CSS class 污染、门禁④结构分下跌（49.0 → 46.5）、
   clarity 失去对称；改为空串后两者恢复。`%v`/`%T` 与 Go 一致（`<nil>`）。

回归见 `GoPrintfTests`（期望值全部来自 Hugo v0.166 探针）。

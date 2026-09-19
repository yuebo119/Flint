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
| I′ | 树导航四件套：`.Ancestors`/`.Parent`/`.CurrentSection`/`.FirstSection` | 由**真实容器页**（`section`/`taxonomy`）构成、**最近祖先在前、home 在末位**；不产页的合成目录与 pager 段不入链；容器页的 `CurrentSection` 是自己、`FirstSection` 是最外层 section | `LazyPageObject.ContainerChain` 按 URL 前缀从全站页集里挑容器页 + 追加 home（`.Ancestors` 返回页面集合，`.Reverse` 可用），三个单项成员在同一链上取值 | `HugoCompatSemanticsTests.祖先链跳过不产页的合成目录/祖先链跳过分页段/home页祖先链为空/词条页祖先含分类列表页/祖先链Reverse给出面包屑顺序/父级与所属顶级section按Hugo语义` |
| J′ | `.Render "view"` 的渲染上下文 | 渲染**点号所在的页**（`range .Pages` 体内每项渲染自己） | 转换器按作用域取接收者（range 体内 = 最内层循环变量，体外 = 页面根），引擎 `render "view" <page>` 以第二参为渲染上下文 | `MigratorTests.渲染视图在循环内用迭代项作上下文` |
| K′ | 日期格式串的三种形态 | `time.Format`/`.Date.Format` 的布局是 **Go 布局串**（`2006-01-02`）、**具名格式**（`:date_long`）或**运行期字符串**（`site.Params.dateFormat`） | `GoDateFormat.Convert`/`LooksLikeGoLayout`（引擎侧，运行期也吃下）+ `FormatHugoDate`（具名/Go/.NET 三方言统一） | `HugoCompatSemanticsTests.管道形态的日期格式按值在前布局在后/dateToString兼容Go布局与NET格式` |
| L′ | 日期值的成员：`.IsZero`/`.Unix` | Hugo 的 `time.Time` 值方法；**无日期页的 `.Date` 是零值时间** `0001-01-01T00:00:00Z` | 迁移器把 `.IsZero`/`.Unix` 改写为 `date.is_zero`/`date.unix`（带括号，避免被外层函数当多实参）；`SiteBuilder.Tree` 的 `Date` 缺省改为 `DateTimeOffset.MinValue` | `HugoCompatSemanticsTests.零值日期的IsZero与渲染/日期值方法的取值`、`MigratorTests.日期值方法改写为引擎函数/日期值方法在变量接收者上也改写` |
| M′ | 参数键别名与页成员 `.Site` | `.Site` 在任意页面可用；参数表按原键名访问（`dateFormat`），迁移产物用 snake（`date_format`） | `WrapParamValue` 递归包装嵌套字典与数组（每层都补 snake 别名）；`LazyPageObject` 暴露 `site`/`Site`（按构建登记站点对象） | `PageCollectionAndParamsTests`（既有）+ 主题回归：stack 的 `:date_full`、loveit/papermod 的参数表 |
| N′ | `i18n` 的复数选形与插值 | 键为**点分嵌套**；值是**复数子表**（`one`/`other`…）时按计数选形（`1 → one`、其余 `other`；**无计数 → other**）；值里的 `{{ .Count }}`/dict 键做插值，缺失渲染 `<no value>`；缺键输出空 | 加载端 `Translations.Flatten` 递归摊平为 `key.one`/`key.other`；引擎 `I18nFunction` 按计数选形 + 正则插值（2 参签名，第二参可为数字或 dict） | `I18nTests.Load_嵌套复数子表应摊平为点分键`、`HugoCompatSemanticsTests.I18n复数选形与插值`（断言值即探针值） |
| O′ | 跨文件命名模板的 `block` | Hugo 的命名模板是**全局**的：`partials/` 里的 `{{ block "X" . }}` 渲染任何文件 `{{ define "X" }}` 的块体 | 迁移器扫描"partial 里的 block 指向别的 partial 的 define"，把块体提取为 `_partials/<名>__block.html` 并把 block 调用点改为 `partial`（默认体用 `if false` 吞掉） | `MigratorTests.跨文件block改为渲染提取出的块体` |
| Q′ | 分类页的 `.Type` | `/tags/`（taxonomy 列表页）与 `/tags/x/`（term 页）的 `.Type` 都是**分类名**（`tags`），`.Kind` 才是 taxonomy/term（探针实测） | `SiteBuilder.Render` 的分类页构造改用 `taxPage.TaxonomyName`（此前写死 `"taxonomy"`，主题按 `.Type` 分支的列表渲染整段落空——github-style 的 posts.html 实测） | `PageCollectionAndParamsTests`（既有）+ 主题回归：github-style 的 `/categories/general/` 列表 |
| R′ | 集合与数值的比较 | 集合按其**长度**参与比较（探针：`gt (slice 1 2) 0` = true、`eq .Pages 0` = false） | `CompareHugo` 先算 `CollectionCount`（集合判定在 ScriptObject 排除之前——页面集合是 `ScriptObject + IList`） | `HugoCompatSemanticsTests.集合与数值比较按长度/页面集合与数值比较按长度` |
| S′ | 值打印（`{{ value }}`） | Go 的 fmt 默认：时间 → `2026-01-15 00:00:00 +0000 UTC`、字典 → `map[k:v …]`（键排序）、列表 → `[a b c]`、nil → `<nil>` | `FlintScribanContext.ObjectToString` 覆盖（页面对象与引擎内部投影不套用，避免把页面集合印成 map） | `HugoCompatSemanticsTests`（值打印经主题回归：github-style 的 `{{ time .Date }}`、hugo-coder 的 `{{ .Site.Params.author }}`） |
| T′ | i18n 文件格式 | Hugo 支持 `.toml`/`.yaml`/`.json`（blowfish 等主题用 YAML） | `Translations` 按扩展名分派（TOML 走 Tomlyn、YAML 走 SharedYaml、JSON 走 System.Text.Json），统一归一后摊平 | `I18nTests.Load_YAML与JSON形态应被加载` |
| U′ | 嵌套 partial 的上下文 | partial 不带参数时以**调用方的 dot** 渲染 | 转换器在 **partial 体内**把 dot 上下文显式传出（`partial "x" page`）——Flint 的 partial 不带上下文取渲染页而非调用方 dot | `MigratorTests`（partial 上下文回归）+ 主题回归：blowfish 卡片日期 |
| V′ | 分组分页 `.Paginate (.Pages.GroupByDate …)` | 切的是**底层页面**，`PageGroups` 把当前页切片**按原分组重新切分**（探针：tne=5、tp=3、第 1 页 `[2025:2]`、第 2 页 `[2025:1][2024:1]`） | 识别分组形状（元素带 `key`/`pages`）→ 摊平参与分页 + 记分组边界；`PaginatorView.PageGroups` 按边界与切片求交 | `HugoCompatSemanticsTests.分组分页的PageGroups/分组分页第二页跨组` |
| W′ | 槽位默认体的位置 | Go 的 `define` 是**解析期**注册（用在前、定义在后也生效） | baseof 的槽位默认体**上提到文件最前**（Scriban 的 capture 是顺序赋值，就地 capture 会让兜底拿到空值） | `MigratorTests.槽位默认体上提到文件最前` |
| X′ | `print` 的空格规则 | Go 的 `fmt.Sprint`：**相邻两个操作数都不是字符串**时才插空格（`print "a" "b"` = "ab"、`print 1 2` = "1 2"） | `GoSprint` 实现该规则；`println` 一律空格 + 换行 | `HugoCompatSemanticsTests.print按Go的fmtSprint空格规则` |
| Y′ | `resources.Get` 的 `.Content`（SVG） | SVG 是**文本资源**，`.Content` 给符号表原文（主题据此内联图标） | 资源装载对 `.svg` 读文本（位图仍不读）；`TemplateResourceTests.Get读取SVG内容` | `TemplateResourceTests.Get读取SVG内容` + 主题回归：monochrome 图标 |
| Z′ | `Scratch.SetInMap` 与 `Get` | `SetInMap MAP KEY VALUE` 之后 `Get MAP` 返回该**映射本身**（主题用它批量初始化参数再逐项读取） | `PageStoreObject.Get` 在扁平值表未命中时返回 `_maps` 里的映射（转 ScriptObject） | `PageStoreTests.SetInMap写入的映射可由Get读回` |
| A″ | 列表页的派生日期 | home/section/taxonomy/term 未显式设置日期时，`.Date`/`.Lastmod` 取**后代页面里的最大日期**（子页无 lastmod 时用自己的 date 参与聚合） | `SiteBuilder.WithDerivedListDates` 在两阶段装配里聚合（home 在**路径段数**降序中排最后）；`.Lastmod` 缺省 = `.Date` | `PageAwareLookupE2ETests.列表页日期由后代派生` |
| B″ | baseof 序幕与块体的求值顺序 | Hugo 的块体在 baseof 骨架**之内**求值：baseof 顶部的 init 链先写 `site.store`，块体随后读 | 迁移器扫描 baseof 顶部的**纯副作用动作**（不定义/不引用局部变量）提取为 `_partials/__baseof_prologue.html`，页面模板在块体捕获**之前** include 它（baseof 自身跳过该段） | `MigratorTests.baseof序幕提到块体之前` + 主题回归：fixit 首页卡片 |
| C″ | `dict` 键的蛇形读取 | 迁移产物把 `.displayName` 归一成 `.display_name`，而 `dict "displayName" …` 的键是驼峰且 ScriptObject 成员访问大小写敏感 | `ScriptDictWithSnakeAliases`（dict 与 merge 的产物均使用）：读取时去下划线 + 忽略大小写兜底 | `HugoCompatSemanticsTests.dict的驼峰键可蛇形读取` + 主题回归：narrow 的许可证链接文本 |
| D″ | `.OutputFormats.Get "rss"` | 列表 kind（home/section/taxonomy/term）未声明 `outputs` 时默认 **HTML + RSS**（探针：`with .OutputFormats.Get "rss"` 在 section 页可取到 `/posts/index.xml`）；非 HTML 格式的 `permalink` 是自身地址 | `BuildOutputFormatsObject` 按 kind 补默认格式；`BuildFormatObject` 的非 HTML 格式 `permalink` = `rel + /index.<suffix>`（此前指向 HTML 页地址） | `HugoCompatSemanticsTests.OutputFormatsGet返回RSS格式` + 主题回归：fixit 的 RSS 订阅链接 |
| E″ | `markdownify` 的行内语义 | 单段落输入输出**行内 HTML**（探针：`"Copyright" \| markdownify` → `Copyright` 无 `<p>` 包裹、`"**bold** text"` → `<strong>bold</strong> text`）；多段落（块级）输入才渲染出多个 `<p>` | `markdownify` 渲染后剥掉**唯一**的 `<p>` 包裹（内层再出现 `<p>` 或多段落时保持块级）；clarity 页脚 `T "copyright" \| markdownify` 的嵌套 `<p>` 由此消除 | `HugoCompatSemanticsTests.markdownify单段落输出行内HTML/markdownify多段落保持块级输出` |
| F″ | `.Param` 的站点回落与 `.FuzzyWordCount` | `.Param "x"` 页面参数未命中时**回落站点参数**；`.FuzzyWordCount` = **向上取整到百**（W=17→100、W=106→200）；`.ReadingTime` = `ceil(W/200)`（探针 W=425→3） | `ParamLookup` 页面 params 未命中 → 经 `page.site` 查站点 params；`FuzzyWordCount` 改 `ceil(W/100)*100`（此前 W<100 返原值、取整用四舍五入） | `HugoCompatSemanticsTests.FuzzyWordCount向上取整到百` + 主题回归：fixit 词数/阅读时长徽标 |
| G″ | `.NextInSection`/`.PrevInSection` | section 子页列表（Hugo 默认序）上的相邻页：**NextInSection = 更新的页**、PrevInSection = 更旧的页，边界 nil（探针实测） | `LazyPageObject.ResolveInSectionNeighbour`：所属 section 的 `Pages`（站点已按权重升/日期降构建）上取相邻页 | `MigratorTests.布局参数内嵌default管道不被外提`（配套）+ 主题回归：blowfish article-pagination 卡片 |
| H″ | `.Pages.ByWeight` 的等权重破平 | 权重相同按**日期降序**，再标题（探针：等权 Jan/Feb/Mar → `[c][b][a]`） | `PagesByWeightFunction` 改 `OrderBy(Weight).ThenByDescending(Date).ThenBy(Title)`（此前 ThenBy(Title)） | `HugoCompatSemanticsTests.ByWeight等权重按日期降序破平` + 主题回归：techdoc 菜单树 Prev/Next |
| P′ | 日期布局的产出策略与解析默认值 | 无 `timeZone` 配置时按 **UTC** 解释无偏移日期；`-0700` 输出 "+0000"（无冒号）、`MST` 输出时区缩写 | 解析端默认 `TimeSpan.Zero`；**含时区 token 的布局不编译期转换**（原样交给引擎，引擎做无冒号偏移/缩写后处理） | `ContentParserTests.ParseAsync_无站点时区时无偏移日期按UTC解释`、`HugoCompatSemanticsTests.日期布局的时区与变体覆盖` |

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
| `.Ancestors`（祖先链） | **已对齐** | 真实容器页构成、最近祖先在前 home 在末位、term 页的祖先是 taxonomy 列表页；探针值与实现要点见 §Q「残留清零」 |
| `.Parent`/`.CurrentSection`/`.FirstSection` | **已对齐** | 三者与 `.Ancestors` 同源（同一条容器链）：`Parent` = 链首（home 页为 nil）；容器页（section/taxonomy/term/home）的 `CurrentSection` 是自己，内容页取最近的 section，根级页落到 home；`FirstSection` 是最外层 section（term 页为分类列表页）。此前 `.CurrentSection` 是**按段名拼的假对象**（取不到 `.RegularPages`/`.GetPage`，嵌套段的 URL 也错），ananke 的 `section-link.html`/`summary.html` 正依赖它 |
| `i18n` 的复数子表与插值 | **已对齐** | 嵌套子表摊平为点分键 + 按计数选形（`one`/`other`）+ `{{ .Count }}`/dict 插值；探针值与实现见 §Q「十六」 |
| 模板级 `toCSS` 的 SCSS 编译 | **有意差异** | DartSassHost 在 Flint 的运行时配置下（全局关闭反射 JSON）无法初始化，模板级 `toCSS` 按 Hugo 的 OPTIONS 语义改写目标路径后**内容直通**（SCSS 原文）；主题自带编译产物的样式不受影响，依赖模板级编译的主题（loveit）样式表为 SCSS 原文，需构建期管线或预编译产物 |

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

### N. 语法覆盖面探针（回答"能否 100% 转换"）

语料 100% 只说明"21 个主题用到的语法能转"，不等于"Hugo 全语法能转"。为此另造一组
**语法覆盖面探针**（`/tmp/hp`，10 个模板文件、159 个表达式）：控制流（if/else if/with/
with-else/range 带 index/continue/break/seq）、变量（`:=`/`=`/`$`/作用域）、函数族
（strings/collections/compare/math/path/urls/transform/crypto/encoding/hash/time/i18n/
templates.Exists）、资源管线（GetMatch/ByType/FromString/Copy/Concat/ExecuteAsTemplate/
Minify/Fingerprint）、Scratch/Store/SetInMap、safe* 系列、短代码模板与渲染钩子
（`_markup/render-codeblock.html`）、define/block/template/partial/partialCached、
页面方法族（Kind/Type/Section/.File./GetPage/GetTerms/TableOfContents/Sections…）。

结果：**不支持 0、预检失败 0（探针集内）**，仅 1 条诊断来自**故意写错**的模板——
Hugo 自己在 layouts 里也不接受短代码调用（`{{< … >}}` → `unexpected "<" in command`），
Flint 现在**原样保留 + 记诊断**（此前会把定界符吃掉：`{{< sc x="1" >}}body{{< /sc >}}`
→ `{{ sc x "1" }}body{{ sc }}`，属静默损坏；探针集里该文件仍报 1 处 Scriban 预检失败，
这是**正确**的——那份模板在 Hugo 下同样无法渲染）。

同时修掉一条**过时注记**：迁移器对 `printf` 打"Flint 用 .NET 格式化，差异已标注"的降级
标记，而引擎侧已按 Go fmt 语义实现（见 M 节），故该形态改判为**保形**（Equivalent）——
21 主题的降级数因此从 1102 降到 **718**（差额全部来自 printf 去标记：fixit 236→77、
blowfish 201→144、narrow 198→165）。剩余 718 处的四类见 L 节表格，均为等价改写或
转换期不可静态校验（动态 partial 名）。

### O. 产物可用性检查（本轮第九项）——"能构建"之外的两类真缺陷

回答"这些主题正常可用了吗"时做的第三层检查：不只比页面集合，还把每个 Flint 产物里
**引用的本地资源**（href/src/url()）逐个核对文件是否存在，并与 Hugo 侧对照
（脚本见 `scripts/check-broken-assets.py`，与矩阵脚本同级，不在 git 仓内）。

结果暴露两类**Flint 独有**的坏引用（Hugo 侧没有）：

1. **`href="/assets/{name: "js/main.ts", …整个对象转储…}"`**（fixit 30 处、narrow 54 处、
   stack 17 处、monochrome 15 处）——`js.Build` 的形参声明是 `(string? path, …)`，
   而 Hugo 的签名是 `js.Build [OPTIONS] INPUT`（**输入在末位**），Scriban 的管道又把
   左值注入**首参**：两条路径参数序相反，资源对象被 Scriban 转成字符串当成了"路径"。
   修法：实现改为**按类型定位资源实参**（不按位置），并让 `css.Build/css.Sass/
   css.PostCSS/css.TailwindCSS/js.Babel/js.Batch` 同一口径（这些恒等实现此前会返回
   选项字典而不是资源）。顺带把内容**透传**（旧实现产出空内容资源 → JS 文件是空的）
2. **`/posts/page/2/page/2/`**（even、hugo-paper）——模板在**第 N 页**上再次调用
   `.Paginate` 时，Flint 恒按"第 1 页 + 本页 RelPermalink"重建 pager：基准 URL 变成
   pager 页自身，末页还多出个不存在的 `Next` 链接。修法：页码与基准取自**当前页已绑定
   的 pager**（`PaginatorView.BaseRelPermalink`/`PageNumber`），Hugo 语义即"返回当前页的 pager"

复核后：monochrome/stack 的坏引用清零、even/hugo-paper 分页链接正常、fixit/narrow 的
剩余坏引用只剩 Hugo 侧也有的（favicon 等主题静态资源，属内容缺失非 Flint 问题）。

**仍未覆盖**：视觉/交互验收（外观、CSS 生效、JS 交互、响应式），以及每个主题的
`grep -c` 级结构相似度只有约 50%——内容在（文本覆盖 78–97%），标记结构差异明显。

### P. "所有主题正常运行"的三阶段方案与实施

**结论先行**：页面集合层面已达成 **21/21 与 Hugo 完全一致**（此前 18/21），
结构性元素差异也定位到系统性缺口并修掉一批。方案分三阶段：

#### 阶段 1：页面集合对齐（已完成，21/21）

五处根因（均以 Hugo v0.166 探针定性）：

1. **首页 `.Pages`/`.Sections` 恒为空**：树节点 key 是 `/posts`、`/about`（带前导斜杠），
   而首页子级判定写 `!key.Contains('/')` → 一级节点全被排除。此前所有站点的首页
   `.Pages` 都是 0（Hugo 3）——凡首页用 `.Pages` 列文章的主题都缺整个列表区
2. **树装配单遍**：父节点拿到"尚未装配 Pages"的子 section（`site.home.sections[*].pages`
   恒为空、而 `site.pages` 里同名 section 有 3 条）→ 主题按 `.Sections`/`.Pages`
   递归遍历站点结构时出错甚至触发递归上限（techdoc 的 prev/next 导航树）。
   改为**按层级降序装配 + 按原顺序产出**
3. **隐式分页器的集合 = RegularPages**（Hugo 实测：home 只读 `.Paginator` 时
   `TotalNumberOfElements` = 站点全部常规页、`TotalPages` = 3，而 `.Pages` 只有 3 条中
   的"顶层子页 + 顶层 section"）→ m10c/monochrome 少产 `/page/2`、`/page/3`
4. **分类列表页 `.Pages` 是全集**（Hugo 实测 /tags/ 的 `.Pages` = 全部词条页，当前页切片
   只在 pager）→ clarity/stack 的 `range (.Paginate .Pages).Pages` 少算页数；
   同时 `/page/N/` 的产出改为以**模板实际分页的集合**为准（hugo-paper 的
   `union .RegularPages .Sections` 在 /tags/ 上是空集 → 只产 page/1）
5. **taxonomy 页 `.RegularPages` = 空**（Hugo 实测）

迁移器侧两处：**自名命名模板的递归调用**要落到提取出的 `<name>__named` 文件
（否则 wrapper↔named 互相调用 → techdoc "partial 嵌套深度超过 200"）；
**空白模板按原文保留**（Hugo 对 0 字节 404 模板不产出，而对 0 字节 single/partial 照用）。

#### 阶段 2：结构性元素差异审计（进行中）

方法：对 21 个主题逐页做元素签名多重集差（`scripts/audit-elements.py`，
与门禁④的 ElementDiff 同口径），按"涉及页面数"排序 —— 得到的是**系统性**缺口而非
单页噪声。审计出的头部缺口与处置：

| 缺口 | 涉及页数 | 性质 | 处置 |
|---|---|---|---|
| 缺 `meta[og:locale]` | 240 | 引擎：内置 opengraph 模板缺该行 | 已补（Hugo 内部模板逐行对齐） |
| 缺 `meta[generator]` | 100 | 引擎：`hugo.Generator` 只返回版本串，Hugo 返回**整段 meta 标签** | 已修（品牌名如实用 Flint） |
| 缺 `meta[article:section/tag/published_time/modified_time]` | 56/45/42/44 | 引擎：内置 opengraph 缺常规页的 article:* 组 | 已补 |
| 多 `meta[og:description]`/`twitter:description`/`description` | 96/82/32 | 引擎：空值仍输出标签（Hugo 空描述整条省略） | 已改为条件输出 |
| 缺/多 `a`、`span`、`div`、`li`、`p` 等 | 30–90 | 模板级（各主题自己的标记差异） | 逐主题个案，不属系统缺口 |

阶段 3（视觉验收）：用浏览器对 Hugo 与 Flint 产物逐主题截图对比（外观/CSS/交互），
以及链接可达性冒烟——需真实浏览器环境，尚未执行。

### Q. 视觉验收（阶段 3）发现：`assets/` 资源的 URL 约定不同（待裁决）

用 localhost baseURL 重建同一主题（hugo-paper）的 Hugo 与 Flint 产物，浏览器逐页对比：

**已确认正常**：Tailwind 样式生效、排版/间距/按钮与 Hugo 一致、日期文本正确
（`Mar 10, 2026`，具名格式已修）、首页列表与分页按钮正常、控制台除下面一条外无错误。

**发现的分歧（架构级，需用户裁决）**：`assets/` 目录下资源的发布路径——

| 引擎 | `assets/main.css` 的 `.RelPermalink` |
|---|---|
| Hugo v0.166（探针） | **`/main.css`**（相对 `assets/` 根，即发布到站根） |
| Flint | `/assets/main.css`（统一加 `/assets/` 前缀） |

影响（实测）：Hugo 把处理后的 CSS 发布在站根 → CSS 内 `url(./theme.png)` 解析为
`/theme.png` ✔ 存在；Flint 发布在 `/assets/` → 解析为 `/assets/theme.png` ✗ 404
（浏览器控制台 1 个错误、图标缺失）。同一约定还会让**每个资源链接**的文本与 Hugo 不同。

修法（两条路，需裁决）：
1. **对齐 Hugo**：`TemplateResource.RelPermalink` 改为"相对 assets 根"（`assets/x.css` →
   `/x.css`），资产写盘目录同步改为输出根。风险：改动面广（资产管线 + 所有
   `resources.*` 产物路径 + 硬编码 `/assets/` 的主题模板要另加兼容映射），需整轮验证。
2. **保留 Flint 约定**：在文档中标注为**有意差异**，并给"相对引用"加兜底
   （把 `assets/` 下的资源同时镜像到站根，或对 CSS 内的相对 `url()` 做重写）。

**已按方案 1 实施**（"继续"指令 + 本清单推荐）：`CollectAssetFiles` 对 `assets/` 不再加
`assets/` 前缀（与 `static/` 同前缀，即"站点资源同一命名空间"），`TemplateResource.Create`/
`Fingerprint` 的 `RelPermalink` 同步改为 `/x.css`。效果：样式链接与 Hugo 一致
（`/main.<hash>.css`）、CSS 内 `url(./theme.png)` 正确解析、浏览器控制台 **0 错误 0 警告**
（此前 1 错 1 警）；21 主题矩阵对称保持 **21/21**。

#### 残留（已修）

`clarity`/`stack` 的首页导航曾链到**不存在的** `/page/3/`。根因（定向探针确证）：Hugo 的
`.Paginate` 会**改写该页的 `.Paginator`**，随后读到的都是那一次创建的分页器；而 Flint 的
`page.paginator` 只返回构建期预绑定的**隐式**分页器（站点全部常规页 → 3 页）。stack 的
home 用 `where .Site.RegularPages "Type" "in" .Site.Params.mainSections` 得到空集
（其主题配置 `mainSections = ["post"]`，与内容段名 `posts` 不匹配）→ Hugo 侧 `.Paginate []`
= 1 页、不渲染页码、只产出 `/page/1/`；Flint 侧导航却按 3 页渲染 → 有链接无页面。

修法：新增"模板创建的分页器"登记表（按列表页 URL），`page.paginator` 读取时**优先**返回它，
只有模板没调过 `.Paginate` 才回落预绑定的隐式分页器；内置 `_internal/pagination.html`
也从全局 `paginator` 改读 `page.paginator`（Hugo 内部模板用的就是页面的 `.Paginator`，
而全局量在渲染前绑定、拿不到模板后续创建的分页器）。

结果：clarity/stack 的坏链接清零，**全语料 Flint 独有坏引用从 31 类降到 3 类**
（narrow 的 `/docs/guide/`、`/posts/page/` 与 fixit/hugo-coder 各 1 类），
21 主题矩阵对称保持 21/21。

#### 残留清零（本轮第十、十一项）：坏引用 3 类 → 0 类

| 主题 | 链接 | 根因 | 修法 |
|---|---|---|---|
| hugo-coder | `/tags/page/2/page/2/` ×1 | 分类页（term/taxonomy）分页页的 pager **基准 URL 取成了 pager 页自身**（`/tags/page/2/`），再拼 `/page/2/` 成了三段 | `SiteBuilder.Render` 的 `BaseRelPermalinkOf(taxPage)`：从 `rel` 里剥掉尾部的 `/<paginatePath>/<N>/` 再作基准（常规列表页那处上一轮已修，分类页路径漏了） |
| narrow | `/posts/page/` ×1 | `.Ancestors` 按 `RelPermalink` 的**路径段**拼接，pager 段（`page/2`）被当成一级目录 | `.Ancestors` 改由**真实容器页**构成（见下） |
| narrow | `/docs/guide/` ×1 | 同上：无 `_index.md` 的嵌套目录是**合成节点**，Hugo 不产出该页，却进了祖先链 | 同上；且只取 `Kind` 为 `section`/`taxonomy` 的页 |

##### `.Ancestors` 的 Hugo 语义（探针实测，v0.166）

探针站点 = home（`_index.md`）+ `docs`（有 `_index.md`）+ `docs/guide`（**无** `_index.md`）
+ `docs/guide/deep.md` + `posts` + `tags`/`tags/x`，模板打印每一项的 Title 与 Kind：

| 页面 | `.Ancestors` |
|---|---|
| home | 空（`len` = 0） |
| `/posts/`（section） | `[首页\|home\|/]` |
| `/tags/`（taxonomy） | `[首页\|home\|/]` |
| `/tags/x/`（term） | `[Tags\|taxonomy\|/tags/][首页\|home\|/]` |
| `/docs/guide/deep/` | `[文档区\|section\|/docs/][首页\|home\|/]`（`guide` 不出现） |

由此定三条：① **最近祖先在前、home 在末位**（主题写 `.Ancestors.Reverse` 才得到
"home → … → 父级"的面包屑顺序，narrow 的 `breadcrumb.html` 即此用法）；
② 元素是**真实页面对象**（`.Title`/`.Kind`/`.RelPermalink` 都可用，不是路径段拼出来的壳子）；
③ 参与构成的只有 `section` 与 `taxonomy` 两类容器页——内容页与词条页不可能是别人的祖先。

实现要点（`ScribanTemplateRenderer.Objects.cs`）：取**全站页集**里 `RelPermalink` 是本页
前缀的 `section`/`taxonomy` 页（自身除外），按 URL 长度降序（最近的在前）后追加 home；
返回**页面集合**（`LazyPageList`，与 `.Pages` 同型）而非手搓 `ScriptObject`——
手搓形状的 `range` 会把 `count`/`reverse` 这些成员也迭代出来，而页面集合上
`.Reverse`/`| len` 与 Hugo 的 Pages 方法族一致。另：该成员**惰性解析**（访问时才算，
先取构造参数、再退到本次构建登记的全站页集）——页面对象按引用跨渲染共享，
若某页先在页面集合迭代里被构造（那时没有站点页集），构造期算祖先会得到空链。

回归锁定：`HugoCompatSemanticsTests` 新增 5 条用例（合成目录/分页段不入链、home 为空、
term 页含 taxonomy 祖先、`Reverse` 的面包屑顺序），断言值即上表探针值。

结果：**全语料 Flint 独有坏引用 3 类 → 0 类**（21 主题、约 4000 个页面引用，
`scripts/check-broken-assets.py` 输出无 "← Flint 独有" 行），21 主题矩阵对称保持 21/21。

#### 渲染上下文与日期格式（本轮第十二、十三项）

死链清零后按"逐页视觉对比"继续体检，发现两族**文本级**缺陷（不影响构建与对称门禁，
但页面内容错）：

**十二、`.Render "view"` 在循环里渲染的是外层页面**。Hugo 的 `.Render` 是页面方法，
渲染的是**点号所在的那一页**；迁移器把接收者按"页面根"处理，落成 `render "summary" __page`
→ ananke 首页三张 summary 卡片全部渲染成 Home（标题、链接、日期都是首页的）。
修法：转换器按作用域取接收者（range 体内取最内层循环变量 `$__it0`，体外才回落页面根），
引擎的 `render "view" <page>` 以第二参为渲染上下文（该能力此前已实现，只是没人喂对值）。
ananke 首页文本相似度 88.3 → **90.9**。

**十三、日期格式串的三种形态**。`<time>` 文本全语料体检（21 主题）：6 个主题的日期是**乱码或
未转换的布局串**——ananke/hugo-coder 的 `Januar26 2, 2006`、bearblog 的 `02 Jan, 2006`、
blog-awesome 的 `2 Jan 2006`、fixit 的 `2006-01-02`、stack 的 `2026-09-16`。三条成因：

| 成因 | 现象 | 修法 |
|---|---|---|
| **参数序**：Hugo `time.Format` 是 (布局, 值)，Flint `date.to_string` 是 (值, 布局) | 布局串被当日期解析、日期被当格式串（`Januar26 2, 2006`） | 迁移器分形态处理：管道 `X \| time.Format FMT` 只传布局（Scriban 管道把左值注入首参 → 恰好是 (值, 布局)）；直接 `time.Format FMT VALUE` 换成 `date.to_string VALUE FMT` |
| **运行期布局**：`site.Params.dateFormat` 里的布局迁移期看不到 | 布局按 .NET 自定义格式解析 → 数字原样输出（`2026-09-16`、`2 Jan 2024`） | 引擎 `date.to_string` 用 `LooksLikeGoLayout` 判定后 `Convert`（已转换的 .NET 串不含 Go 特征 token，不会二次转换）；布局表补齐 `2 Jan 2006`/`02 Jan, 2006` 等缩略月变体 |
| **字面量漏引号**：链式名分支（`$Page.Date.Format "…"`）转格式串时丢引号 | Scriban 把 `yyyy-MM-ddTHH:mm:sszzz` 当变量表达式 → 求值 null → 回落默认格式（stack 的 `datetime='2026-01-15'`） | 该分支补引号（与 `.Date.Format` 分支一致） |

Hugo 的裸 `time` 在 Flint 里是**解析函数**（github-style/clarity 的 `time .Date` 要用），
故 `time.Format` 不能落成 `time.format`（成员查不到会去调用 `time` 本身，报
"Invalid number of arguments 0 passed to time"）——这条也在实现备注里写清。

验证：Core 997 全绿（新增 7 条日期/上下文用例）、迁移器 91 全绿（新增 3 条形态用例）；
21 主题矩阵对称保持 21/21；`<time>` 文本的"Flint 独有"从 6 主题的乱码降到
**只剩"无日期页被填成构建日"一族**（下一项，见下）。

**十四、无日期页被填成"构建当天"**。Hugo 对没有 front matter `date` 的页面给**零值时间**
（`0001-01-01T00:00:00Z`），Flint 此前回落 `DateTimeOffset.Now` —— 每个无日期页都显示
构建当天。两种主题行为都因此偏离：直接渲染的（bearblog 的 `/docs/` 应输出
`01 Jan, 0001`、`datetime='0001-01-01'`）显示成 `16 Sep, 2026`；用 `.Date.IsZero` 守卫的
（ananke 的 ShowDate、console/fixit 的 head 元标签、stack 的 details）本该**不渲染**日期，
却因为取不到 `is_zero`（值成员在 Scriban 侧不存在 → 静默空值）而守卫恒真、照常渲染。
修法两条：`Date` 缺省改零值时间；`.IsZero`/`.Unix` 由迁移器改写为引擎函数
（`date.is_zero`/`date.unix`，**带括号**——`add .Lastmod.Unix X` 改写后是两个 token）。
`<time>` 文本的 Flint 独有项从 6 主题降到 **0**（ananke/bearblog/console/stack 之外
fixit 仅剩 1 条 Hugo 独有）。

**十五、嵌套参数取不到值 / 页对象没有 `.Site`**。迁移器把 `.Site.Params.dateFormat.published`
归一成 `site.params.date_format.published`（或 `$page.site.params.date_format.published`），
而 Flint 只给**顶层**参数键补 snake 别名、页面对象没有 `site` 成员 → 主题拿不到格式化配置，
回落默认格式（stack 的 `<time>` 文本渲染成 `2026-01-15` 而非 `Thursday, January 15, 2026`）。
修法：`WrapParamValue` 递归包装（嵌套字典与数组逐层补别名）+ 页面对象暴露 `site`/`Site`
（构建入口登记站点对象）。loveit 文本 83.6 → 87.1、papermod 92.7 → 92.7（结构 55.5 → 56.2）、
stack 日期文本与 Hugo 完全一致。

**待办（下一项，已定位未修）**：i18n 的**复数子表与插值**——Hugo 的
`[article.readingTime] one/other` 子表按计数选形并渲染 `{{ .Count }}`（stack 显示
`1 minute read`、ananke 的 `readingTime`、blowfish 的 `(dict …)` 语境），Flint 的
`i18n` 只做**扁平键**查表 → 这类键整段输出空（stack 的阅读时长 `<time>` 为空）。

**二十五、`.OutputFormats` 的 kind 默认格式与格式 permalink（本轮第六项）**。fixit 的
section.html 用 `with .OutputFormats.Get "rss"` 渲染 RSS 订阅链接。两处缺陷叠加：
① 列表 kind 未声明 `outputs` 时 Hugo 默认 **HTML + RSS**，Flint 恒只有 html →
`Get "rss"` 为 null → 订阅链接整段不渲染；② 命中后 RSS 格式的 `permalink` 应是
`/posts/index.xml`（自身地址），Flint 给的是 HTML 页地址 `/posts/`。
修法：`BuildOutputFormatsObject` 按 kind 补默认格式；`BuildFormatObject` 的非 HTML
格式 permalink = `rel + /index.<suffix>`；同时 `SiteBuilder.Output` 为**每个列表页**生成
`<列表页>/index.xml`（此前只有站点根 /index.xml，ananke 全站 9 个 RSS 文件缺失、
`<link rel="alternate">` 全部 404，实测）。fixit 结构 49.6 → **50.0** / 文本 89.9 → **90.5**、
ananke 结构 55.6 → **56.7** / 文本 92.0。

**分页页不产出 RSS**（Hugo 实测）：blog-awesome 的 /page/2/ 与 /tags/page/2/ 在 Hugo
只有 HTML、无 index.xml——分页页 head 的 RSS 链接指向**列表根 feed**（/posts/index.xml）。
Flint 的 `BuildFormatObject` 对分页页（RelPermalink 形如 `/x/page/N/`）做归一，使其
RSS 链接指向列表根 feed。

**三十、ByWeight 破平、in-section 导航与格式参数内的管道（本轮第十项）**。
三处探针级修正：① **`.Pages.ByWeight` 等权重按日期降序破平**（探针：等权
Jan/Feb/Mar → `[c][b][a]`）；Flint 此前按标题破平，techdoc 的菜单树递归遍历
（`.Pages.ByWeight` + Scratch 传 prevPage/nextPage）整树反序——`/posts/second/`
的导航从 "Prev - First Post / Next - Third Post" 修正为 Hugo 的
"Prev - Third Post / Next - First Post"，文本 92.2 → **93.7**。② 实现
**`.NextInSection`/.PrevInSection**（探针：NextInSection = **更新的页**、
PrevInSection = **更旧的页**，边界 nil）——blowfish 的 article-pagination
上一张/下一张卡片（含日期）此前整段不渲染。③ 迁移器
`ParenthesizeIfCallWithArgs` 的 `IndexOf(" | ")` 不看括号深度——time.Format
格式参数内嵌的 `| default ":date_long"` 被切到调用外层，default 的作用对象
从**格式串**变成 date.to_string 的**结果**（结果非空 → default 永不生效）→
`:date_long` 具名格式被吞。改为括号深度感知扫描。blowfish 日期卡两侧一致；
loveit 结构 69.6 → **72.2**、narrow 31.8/94.4 → **32.2/94.6**。

**二十九、`.Param` 的站点回落与 `.FuzzyWordCount` 的取整（本轮第九项）**。
两处探针级修正：① **`.Param` 回落**——Hugo 的 `.Param "x"` 是页面参数未命中时
**回落站点参数**；Flint 此前 `paramLookup page` 只查页面 params，fixit 的
`.Param "word_count"` 门控因此永远关闭（词数/阅读时长徽标整段缺失）。修法：
页面 params 未命中 → 经 `page.site` 取站点 params 再查。fixit 文章页的
"About 100 words"/"One minute" 徽标由此恢复。② **`.FuzzyWordCount`**——Hugo
是**向上取整到百**（探针：W=17→100、W=99→100、W=100→100、W=106→200、
W=1070→1100）；Flint 此前 W<100 返回原值、取整用四舍五入，两处都与 Hugo 相反。
修正为 `ceil(W/100)*100`。另以探针核实 `.ReadingTime` = `ceil(W/200)`
（W=425→3 排除旧文档的 213——Flint 的 200 WPM 本就正确）。

**二十八、矩阵站点的 TOML 参数嵌套缺陷（本轮第八项）**。`make_config` 把
`params.*` 点号键写在 `[pagination]` 表头之后、`add_theme_params` 再追加在
`[menus]` 之后——TOML 语义下点号键挂进**最近的表头**，这些参数实际写成了
`pagination.params.*` 与 `menus.main[].params.*`，`.Site.Params` **从未收到过**
任何站点参数（含各主题专属参数）。缺陷潜伏的原因：多数主题对缺参渲染降级，
直到 even 的 baseof 显式校验 `params.version == "4.x"` 才显形。

重构：`make_config` 开显式 `[params]` 段、`add_theme_params` 在段内追加
（键名去掉 `params.` 前缀）、`write_menus` 收尾。补齐各主题 exampleSite 的
必要参数形状：even（`Author` 映射 + `version` + `archivePaginate`）、
blog-awesome（`Author.name/avatar` 映射）、blowfish/clarity（`Author` 映射）、
stack（`widgets` 表数组）。结果：even **首次全绿**（22/22 页、相似度
61.6/95.8）、blog-awesome 53.8/97.0、clarity 67.5/90.7、stack 57.4/96.0。

**遗留（已解决，见三十一）**：fixit 的 Hugo 基线需 Dart Sass（其 `to-css.html`
写死 `transpiler=dartsass`），本机 hugo.exe 仅有 libsass，`TOCSS-DART` 报
"feature not available"——环境限制，非引擎缺陷。

**三十五、图像操作的二进制直通（本轮第十五项）**。位图在装载期不读文本
（Content 为空），Fill/Resize 产物因此被判"空内容"跳过落盘 → HTML 引用了
产物名却无文件（blog-awesome 的 bio 头像 `.Fill "70x70 center webp"` 实测
Flint 独有断链）。修法：TemplateResource 新增 `BinaryContent` 载荷通道
（`ReadOnlyMemory<byte>`，写出端优先、绕过 UTF-8 往返），provider 加
`ReadBytes`；图像操作对位图取**原图字节**直通（尺寸变换不实现——与模板级
toCSS 同策略），规格串进 URL 前把空格替换为下划线。断链复查：
**Flint 独有断链清零**（其余缺失 Hugo 侧同样存在，行为一致）。

**三十四、右裁剪标记与 void 元素序列化（本轮第十四项）**。两个字节级对齐项：
① **词法器丢失 `-}}`**——`LexRightDelim` 消费右裁剪 `-` 时不产生独立 token，
解析器按"前一个 token 值 == '-'"判定 TrimRight **恒为 false**，迁移重写动作时
`{{- with X -}}` 变 `{{- $w = X; if $w }}`（尾部 `-` 丢失）→ 属性行首渗入模板
换行（narrow 首页 `content="
 Matrix test site"` vs Hugo
`content="Matrix test site"`）。修法：右裁剪并入定界 token 值（`-}}`，与左
定界 `{{-` 对称）。修后 narrow 的 description/author 属性值与 Hugo **逐字节
一致**。② **void 元素自闭合斜杠**——Go html/template 序列化时剥掉
`<meta … />` 的斜杠，Flint 此前透传；新增输出级 `HtmlOutputNormalizer`
（引号感知、仅限 void 元素）。

**三十三、内嵌 `schema.html` 对齐为 itemprop 形态（本轮第十三项）**。探针
（v0.166）：Hugo 内置 schema.html 输出的是 **itemprop meta**（name/description/
datePublished/dateModified/wordCount，日期布局 `-07:00` → `+00:00`），不是
JSON-LD。Flint 此前的等价实现恒输出 JSON-LD（home 为 WebSite、其余
BlogPosting）——凡调用 `partial "schema.html"` 的主题（hugo-book、even、
ananke、bearblog、hugo-paper、techdoc、narrow…）头部整段错形。对齐后相似度
大幅上升：even 61.6 → **73.1**/97.4、bearblog 39.2 → **56.0**/96.9、
ananke 55.7 → **67.6**/93.8、hugo-paper 60.7 → **67.6**/93.2、
hugo-book 42.3 → **46.4**/89.2、narrow 41.1/95.6、techdoc 42.2/96.5。

另：Flint 的 `MinifyJs` 只去注释/空白、不做标识符混淆（Hugo 用 tdewolff/minify
把 `var menu` 缩成 `var e`）——内联脚本文本因此不同但**功能等价**，登记为有意
差异（实现变量名混淆 = 重写压缩器，风险收益比不成立）。

**三十二、`.Params.summary` 只含显式 front matter（本轮第十二项）**。探针
（v0.166）：无显式 summary 时 `.Params` **没有** summary 键（有 title/draft/
iscjklanguage；显式设置时 description/summary 才出现）——自动摘要不进 params。
Flint 此前把自动计算的 Summary 投影进 `.Params.summary`，narrow 首页 meta
description、clarity/narrow 的 `if .Params.summary` 覆盖钩子因此恒真（home
走错回退分支，输出自动摘要而非 site.Params.description）。修法：PageContext 新增
`ExplicitSummary`（仅 front matter 显式值），params 投影改用它。

**三十一、Dart Sass 就位，21/21 满对称（本轮第十一项）**。网络恢复后从
GitHub Releases 装官方 dart-sass 1.104.1 到 `tools/dart-sass/`（矩阵脚本探测到
即入 PATH，hugo env 报 `compiler="1.104.1"`），fixit 的 Hugo 基线由"失败 0 页"
转为有效：22/22 页、对称=1、相似度 **53.1/95.2**。词数/阅读时长徽标与 Hugo
**逐字节一致**（"About 100 words"/"One minute"——第九项的 `.FuzzyWordCount`
与 `.Param` 回落修复经真实基线验证）。**21 主题矩阵首次满对称**。

**二十七、`markdownify` 的行内语义（本轮第七项）**。clarity 的页脚用
`{{ T "copyright" | markdownify }}`——i18n 值是纯文本，Hugo 的 `markdownify` 是
**行内**渲染（探针 v0.166：`"Copyright" | markdownify` → `Copyright`、
`"**bold** text"` → `<strong>bold</strong> text`，无 `<p>` 包裹）；Flint 此前走完整
Markdig 管道（块级）→ 产出 `<p><p>Copyright</p>…</p>` 嵌套结构。修法：渲染后剥掉
**唯一**的 `<p>` 包裹（内层再出现 `<p>` 或多段落时保持块级，与 Hugo 的多段行为一致）。
clarity 页脚版权行与 Hugo 逐字节一致（`Copyright&nbsp;<span class="year">…`）。

**二十六、门禁③管道死锁（loveit verify 卡死）**。`RunBuildGate` 先
`ReadToEnd(stdout)` 再 `ReadToEnd(stderr)`——子进程 stderr 写满 4KB 管道缓冲区时
两者互等，门禁③构建挂死（loveit verify 600/900 秒超时被杀，实测）。修法：并发排水
两根管道后再等进程退出。此修同时暴露并修复了 loveit 的真实回归：修复前 verify
挂死被杀 → public-flint 残缺 → loveit 显示 0.0/0.0。修复后 loveit 结构
65.8 → **69.6** / 文本 90.4 → **95.1**（verify 完整跑通后的真实数值）。

**二十四、`dict` 键的蛇形读取（本轮第五项）**。narrow 的 post-license.html 用
`dict "displayName" "知识共享署名…"` 存配置、再以 `$license.displayName` 读取；迁移产物
把读取端归一成 `$license.display_name`（蛇形），而 ScriptObject 的成员访问大小写敏感、
不做下划线归一 → 读回空 → **许可证类型链接的文本整段消失**（Hugo 渲染出
"知识共享署名-非商业性使用-相同方式共享 4.0 国际许可协议"）。
修法：dict 与 merge 的产物统一用 `ScriptDictWithSnakeAliases`（读取时去下划线 +
忽略大小写兜底）。narrow 文本 93.2 → **93.9**、m10c 结构 51.4 → **53.2** / 文本 87.9 → **88.7**、
monochrome 文本 90.5 → **91.5**、loveit 文本 89.6 → **89.6**（结构 56.5 → 57.5）。

**二十三、baseof 序幕与块体的求值顺序（本轮第四项）**。Hugo 的块体在 baseof 骨架之内
求值：baseof 顶部的 init 链先写 `site.store`，块体随后读取。而转换把块体捕获提到了
include 之前 → fixit 的 home.html 读 `.Site.Store.Get "mainSectionPages"` 时 init 链
还没跑 → **首页文章列表整段不渲染**（"published on" ×3 全缺）。

修法：迁移器扫描 baseof 顶部**连续的纯副作用动作**（partial 调用 / store 写入；
**不定义也不引用局部变量**——hugo-paper 的 `$.Scratch.Set "bg_color" (index $color_map …)`
引用变量，判非纯、不迁移）→ 提取为 `_partials/__baseof_prologue.html`，页面模板在块体
捕获之前 include（baseof 自身跳过该段，避免执行两次）。
fixit 首页 3 张卡片与 "published on" 全部恢复（文本 88.4 → **89.9**），
loveit 结构 56.5 → **57.5** / 文本 87.2 → **89.6**。

**二十二、列表页的派生日期（含装配顺序的一个坑）**。Hugo v0.166 探针：home/section/
taxonomy/term 未显式设置日期时，`.Date` = 后代页面里**最大**的 date、`.Lastmod` = 后代里
最大的 lastmod（子页未设 lastmod 时用自己的 date 参与聚合）；都无后代则零值。
Flint 此前给这些页填 `DateTimeOffset.Now`（显示构建当天）或留空 → techdoc 的
"Last updated on …" 在列表页渲染成空、排序与 sitemap lastmod 也偏。

同时修掉一个装配顺序的坑：两阶段的"深优先"排序原先按 **key 里的斜杠数**判层级，
而 home 的 key 是 `"/"`（1 个斜杠）→ 与一级节点并列 → home 排到 `/docs`、`/posts`
**之前**装配 → 派生日期的聚合读到尚未派生的子 section（home 的日期恒为零）。
改为按**路径段数**判层级（`"/"` → 0）。

相似度：monochrome 51.6→56.5、m10c 51.4→53.1、loveit 56.5→57.1、papermod 56.2→56.9、
techdoc 34.6→35.0、even 59.3→59.6。

**二十一、`print` 空格规则、SVG 资源内容、Store 映射读回（本轮三项）**。

- **`print` 的空格规则**：Hugo 的 `print` 就是 Go 的 `fmt.Sprint`——**仅当相邻两个操作数
  都不是字符串**时才插空格。此前一律空格连接 → monochrome 的
  `print .Title " - " .Site.Title` 渲染成 `About  -  Matrix Site`（双空格）。
- **SVG 是文本资源**：`resources.Get "…svg"` 的 `.Content` 要给符号表原文，主题据此
  内联图标（monochrome 的 svg/feather.html + `findRESubmatch` 抽 `<symbol>`）。
  此前 SVG 按图像处理 → Content 恒空 → 图标全渲染成空 `<svg>`。
- **`Scratch.SetInMap` 与 `Get`**：`SetInMap MAP KEY VALUE` 之后 `Get MAP` 要返回该
  **映射本身**；此前 `Get` 只看扁平值表 → 读回 null → monochrome 的 baseof 用
  `SetInMap "params" …` 初始化的整串参数在 head.html 里全部取不到 →
  opengraph/twitter_cards 头标签整段缺失。
  monochrome 结构相似度 16.6 → **51.6**、文本 80.5 → **90.5**（三项叠加）。

**二十、分组分页、集合计数的接口判定、槽位默认体的位置（本轮三项）**。

- **分组分页**：`.Paginate (.Pages.GroupByDate "2006")` 是 blowfish 列表页的写法。
  Hugo 探针（5 篇跨 2 年、pagerSize=2）：`TotalNumberOfElements` = 5（切的是**底层页面**，
  不是组数）、`TotalPages` = 3、第 1 页 `PageGroups` = `[2025:2]`、第 2 页 = `[2025:1][2024:1]`
  （跨组的页在两个组里各出现一次）。此前 `page_groups` 不存在 → 分组列表整段为空。
- **集合计数的接口判定顺序**：页面集合（LazyPageList）继承 Scriban 的 ScriptObject，
  而 ScriptObject 自身实现 `System.Collections.ICollection`（`Count` = **成员数 56**）——
  先判 ICollection 会把集合长度算成成员数（`gt .Pages 0` 侥幸为真、`eq .Pages 2` 恒假）。
  修法：**页面集合接口（`IList<ScriptObject>`/`IEnumerable<ScriptObject>`）先判**，
  非泛型兜底排除 ScriptObject。另据探针：`eq` **不参与**长度换算（`eq .Pages 1` 恒 false），
  只有有序比较（gt/lt/ge/le）按长度。
- **槽位默认体上提**：hugo-book 的 baseof 里 `{{ template "menu-container" . }}` 在文件开头、
  `{{ define "menu-container" }}` 在末尾；Go 的 define 是解析期注册，而转换把 define
  就地 capture 成 `__def_X`（Scriban 顺序赋值）→ 用在前拿到空值 → **侧边菜单/toc/header
  全部不渲染**。修法：baseof 的槽位默认体统一提到文件最前。
  hugo-book 结构相似度 38.8 → **43.9**、文本 80.0 → **86.9**。

**十九、集合比较、值打印、i18n 文件格式、嵌套 partial 上下文（同一轮的四项）**。

- **集合按长度比较**：`{{ if gt .Pages 0 }}` 是主题渲染列表区的开关（blowfish 的
  list.html），而 Flint 的比较函数把集合落到字符串序比较 → 恒 false →
  **整个文章列表区不渲染**。Hugo 探针：`gt (slice 1 2) 0` = true、`lt .Pages 0` = false、
  `eq .Pages 0` = false，即**按长度**参与比较。
- **值打印**：Go 的 `fmt` 默认格式——时间 `2026-01-15 00:00:00 +0000 UTC`、字典
  `map[link:… name:Tester]`（键排序）、列表 `[a b c]`。Flint 此前给 .NET 默认
  （`01/15/2026 00:00:00 +00:00`）与 Scriban 的 JSON 形态（`{name: "Tester", …}`），
  主题直接 `{{ .Site.Params.author }}` / `{{ time .Date }}` 时整段不同
  （hugo-coder/hugo-paper/github-style 实测）。
- **i18n 文件格式**：Hugo 支持 `.toml`/`.yaml`/`.json`；Flint 只找 `.toml` →
  blowfish 的 `i18n/en.yaml`（整个主题的文案）被忽略。blowfish 文本相似度
  82.8 → **89.4**。
- **嵌套 partial 的上下文**：Hugo 的 partial 不带参数时以**调用方的 dot** 渲染，
  而 Flint 的 `partial` 不带上下文取的是**渲染页** → 卡片 partial（dot = 文章）里
  再调元信息 partial（`{{ partial "article-meta/basic.html" . }}`）会拿到列表页 →
  每张卡片的日期整段消失。修法：partial 体内的 dot 上下文显式传出（`partial "x" page`）。

**十七、跨文件命名模板的 `block`（Hugo 的命名模板是全局的）**。`partials/` 下的文件里写
`{{ block "posts" . }}{{ end }}`，而 `posts` 的 `{{ define }}` 在另一个 partial 里：
Hugo 的 `block` 会渲染**任何文件** define 的同名模板（github-style 的 user-profile.html
就是这么拿到文章列表的）。迁移器的槽位机制只覆盖"baseof 声明 + 页面模板覆盖"的同名形态，
这类 block 落到空兜底 → github-style 的列表页/分类页整段文章列表不渲染。
修法：迁移器扫描这种跨文件引用，把块体提取为 `_partials/<名>__block.html`，
block 调用点改为 `partial` 调用（默认体用 `if false` 吞掉——命名模板已存在）。
github-style 结构相似度 76.8 → **83.1**、文本 93.1 → **97.4**。

**十八、日期解析的默认时区与"布局产出策略"**。两条彼此相关的修正：
- **无 `timeZone` 配置时按 UTC 解释无偏移日期**（Hugo 的 timeZone 默认就是 UTC；探针：
  无配置站点里 `date: 2026-03-10` 渲染成 `+0000`）。此前按本机时区解释 →
  github-style 的 RFC1123 输出差 8 小时（`+0800` vs `+0000`）。
- **含时区 token 的布局不编译期转换**：.NET 的自定义格式串没有"无冒号偏移"
  （Go 的 `-0700` → "+0000"）也没有时区缩写（Go 的 `MST` → "UTC"），只有引擎拿到
  **原始 Go 布局**才能做这两处后处理（`ConvertForEmit` 保留原样）。
另补布局表的常用变体（`Jan. 2, 2006`、`Mon, Jan 2, 2006`、RFC1123/RFC1123Z）。
console 的 `Feb. 2, 2026` 乱码与 github-style 的偏移格式因此对齐。

**十六、i18n 的复数子表与插值**。加载端只处理**一层**表且只认 `other`：主题普遍使用的
嵌套子表（`[article.readingTime] one/other`、`[error.404_title]` 这类）整块被丢弃，
引擎端又只做扁平键查表、不选形也不插值 → `i18n "article.readingTime" $page.ReadingTime`
输出空（stack 的阅读时长 `<time>` 为空、fixit/blowfish 的多处文案缺失）。

Hugo v0.166 探针（`[readingTime] one/other`、`[nested.deep]`、`[withdict] other`）：

| 调用 | Hugo 输出 |
|---|---|
| `i18n "simple"` | `Plain text`（扁平键原样） |
| `i18n "readingTime" 1` | `One minute read`（`one` 形） |
| `i18n "readingTime" 5` / `0` | `5 minutes read` / `0 minutes read`（`other` 形） |
| `i18n "readingTime"`（无计数） | `<no value> minutes read`（**仍选 other**，`.Count` 缺失） |
| `i18n "nested.deep" 3` | `3 items`（点分嵌套键） |
| `i18n "withdict" (dict "Count" 3 "Name" "Bob")` | `Bob has 3 items`（dict 插值） |
| `i18n "missing.key"` | 空 |

修法：加载端递归摊平为 `key.one`/`key.other`（并把 `other` 另存为主键，供无计数调用）；
引擎端 `I18nFunction` 改为 2 参（第二参数字或 dict）——有复数子表时按计数选形
（`1 → one`，其余 `other`），随后对值里的 `{{ .Key }}` 插值（缺失渲染 `<no value>`）。
相似度：stack 78.6 → **87.7**、fixit 84.4 → **88.4**；`<time>` 文本 Flint 独有保持 0。

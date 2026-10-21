# Flint 模板制作规范（Fluid 引擎）

| 项目 | 内容 |
|---|---|
| 日期 | 2026-10-21 |
| 状态 | 生效 |
| 目标读者 | 写 Flint 新站点模板的模板作者；维护 Fluid 过滤器的引擎开发者 |
| 关联文档 | `docs/LIQUID-GUIDE.md`（引擎技术细节）、`docs/CONVENTIONS.md` §8.4（引擎隔离硬规则） |

本文是 Flint 模板制作的**唯一权威规范**。模板作者按 §1-§4 编写模板；
引擎开发者按 §5 扩展过滤器。与 `docs/LIQUID-GUIDE.md` 的分工：本文管
"怎么写"，LIQUID-GUIDE 管"引擎怎么实现"。

---

## 1. 模板制作规范

### 1.1 目录结构

```
layouts/
├── index.liquid                  # 首页（kind=home）
├── _default/
│   ├── baseof.liquid             # 基础布局（所有页面共享的 HTML 骨架）
│   ├── single.liquid             # 单页（kind=page）
│   ├── list.liquid               # 列表页（kind=section）
│   ├── taxonomy.liquid           # 分类法列表（kind=taxonomy）
│   └── term.liquid               # 词条页（kind=term）
├── partials/
│   ├── head.liquid               # <head> 区域
│   ├── header.liquid             # 页头（导航菜单）
│   └── footer.liquid             # 页脚
├── shortcodes/
│   └── notice.liquid             # 短代码 {{< notice >}}…{{< /notice >}}
└── _markup/
    └── render-link.liquid        # Markdown 链接定制渲染
```

辅助模板（站点提供时覆盖内置生成器）：`404.liquid`、`robots.liquid`、
`sitemap.liquid`、`rss.liquid`。

### 1.2 文件命名约定

| 规则 | 说明 |
|---|---|
| 扩展名 | 新站点统一 `.liquid`；Hugo 主题 `.html` 走 Scriban 兼容层 |
| 布局命名 | `single`（单页）/ `list`（列表）/ `index` 或 `home`（首页）/ `taxonomy` / `term` |
| 输出格式变体 | `single.json.liquid`（JSON 输出格式）；无变体时仅 html |
| partial 命名 | 小写连字符：`page-header.liquid`、`social-share.liquid` |
| 短代码命名 | 小写连字符，与 Hugo 短代码名一致：`figure.liquid`、`youtube.liquid` |

### 1.3 模板查找顺序（页面 → 模板）

候选链按具体度降序，站点根优先于主题根：

```
{type}/{layout}.liquid          # 最具体：posts/single.liquid
{section}/{layout}.liquid       # posts/single.liquid（section 维度）
{layout}.liquid                 # single.liquid
_default/{layout}.liquid        # _default/single.liquid（兜底）
```

同根内 `.liquid` 优先于 `.html`（站点加 `.liquid` 覆盖既有 `.html` 是
既定迁移路径）。全部未命中且 kind 有内置回退（section/taxonomy/term）
时走 Fluid 内置模板。

### 1.4 布局组合模式

**baseof 模式**（推荐）：基础布局定义 HTML 骨架，页面模板定义内容块。

```liquid
{# _default/baseof.liquid #}
<!DOCTYPE html>
<html lang="{{ site.language }}">
<head>{% include 'partials/head' %}</head>
<body>
  {% include 'partials/header' %}
  <main>{{ block_content }}</main>
  {% include 'partials/footer' %}
</body>
</html>
```

Liquid 无 Hugo 的 `{{ define "main" }}` / `{{ block "main" . }}` 块机制。
等价形态是 **assign 后 include**（共享作用域）：

```liquid
{# _default/single.liquid #}
{% assign main_content = page.content %}
{% include '_default/baseof' %}
```

baseof 内 `{{ main_content }}` 取到 single 模板 assign 的值。这是
Liquid 的唯一块组合形态，由 `include_共享作用域` 单测锁定。

### 1.5 include vs render 选择规范

| 场景 | 用 | 原因 |
|---|---|---|
| partial 需要读取调用方 assign 的变量 | `{% include %}` | 共享作用域 |
| partial 需要隔离（不被调用方变量污染） | `{% render %}` | 隔离作用域 |
| partial 只用 `page.*` / `site.*` 全局模型 | 两者皆可 | 模型全局绑定，作用域无关 |
| 高频循环内的 partial | `{% render %}` | 隔离作用域避免变量泄漏到下次迭代 |

**性能提示**：两种形态的模板解析结果都经缓存（路径缓存 + 文本缓存，
mtime+length 验效），首次渲染后零磁盘 IO。区别仅在变量作用域，
不在性能。

---

## 2. Fluid 性能优势与保持规范

### 2.1 性能数据（三口径实测，2026-10-21）

| 口径 | 方法 | Fluid | Scriban | 比值 |
|---|---|---|---|---|
| 模板级（BDN 严格） | 同逻辑模板单次渲染 | 1.477 μs / 2.6 KB | 19.685 μs / 45.33 KB | **0.08x** |
| 模板级（perf 套件） | 3000 次交错三轮 | 190,135 页/秒 | 56,192 页/秒 | 0.296x |
| 站点级（端到端） | 300 篇交错三轮中位数 | 782.0 ms | 952.8 ms | **0.821x** |
| 站点级渲染段 | 同上，仅渲染阶段 | 270.5 ms | 359.8 ms | 0.752x |

模板级快约 13 倍、分配少约 17 倍。站点级 0.821x 是因为构建时间被
配置加载、内容解析、分类、写盘稀释（渲染段占 35%~47%）。
NativeAOT 体积 +0.48%（40.041 → 40.231 MB）。

### 2.2 性能来源（模板作者需要理解的）

Fluid 的性能优势来自四个层面，模板写法会影响其中两个：

1. **编译式渲染**：模板解析为 IFluidTemplate 后缓存，渲染时直接执行
   编译结果，不做 AST 解释。模板作者无法影响此层。
2. **低分配**：每次渲染分配 2.6 KB（Scriban 45.33 KB），主要来自
   FluidValue 的轻量封装。模板作者无法影响此层。
3. **成员访问无反射**：模型成员经 `FlintMemberAccessStrategy` 映射
   （snake_case → PascalCase），AOT 下经 `DynamicDependency` 保留清单
   防裁剪。模板作者无法影响此层。
4. **include 缓存**：路径解析缓存 + 文本缓存（mtime+length 验效），
   构建内零磁盘 IO。**模板作者通过 include 结构影响此层**。

### 2.3 保持性能的模板写法规范

**必须遵守**：

- partial 用 `{% include 'partials/x' %}` 或 `{% render 'partials/x' %}`，
  不要在模板内联大段 HTML 后复制到多个文件。include 的解析结果被缓存，
  复制则每份文件独立解析。
- 循环内不调用资源变换过滤器（`resource_concat` / `css_sass` /
  `js_build` / `resource_fingerprint`）。这些过滤器每次调用都做
  IO 或进程 spawn，循环内调用会放大为 N 次。正确形态是循环外
  assign 一次，循环内引用。

  ```liquid
  {# 错误：循环内每次迭代都编译 Sass #}
  {% for theme in themes %}{{ theme.path | css_sass }}{% endfor %}

  {# 正确：循环外编译一次 #}
  {% assign compiled = theme.path | css_sass %}
  {% for theme in themes %}{{ compiled }}{% endfor %}
  ```

- 不在 `{% for %}` 循环内用 `find_re` / `replace_re` 正则过滤器。
  正则 pattern 每次调用都编译（Liquid 单引号串的反斜杠须双写，
  且运行时编译无缓存）。循环内正则匹配改用 Fluid 内置的
  `where` / `map` / 字符串过滤器。

**推荐遵守**：

- 用 Fluid 内置过滤器替代手动循环。`where` / `map` / `sort` /
  `reject` / `uniq` / `slice` 是编译级优化，手动 `{% for %}` +
  `{% if %}` 逐元素判断在解释层执行。

  ```liquid
  {# 推荐：内置过滤器 #}
  {% assign recent = site.regular_pages | where: 'draft', false | sort: 'date' | reverse | slice: 0, 5 %}

  {# 不推荐：手动循环过滤 #}
  {% assign recent = '' | split: '' %}
  {% for p in site.regular_pages %}
    {% if p.draft == false %}{% assign recent = recent | concat: p %}{% endif %}
  {% endfor %}
  ```

- 用 `{% assign %}` 缓存循环内不变量。循环内每次迭代都重新计算的
  表达式应提取到循环外。

  ```liquid
  {# 推荐：循环外计算一次 #}
  {% assign base = site.base_url | split: '://' | last %}
  {% for p in pages %}{{ base }}/{{ p.slug }}{% endfor %}
  ```

- 分页用 `paginator` 模型（`page.paginator.pages` / `.pagers`），
  不用手动 `slice` + 索引算术。PaginatorView 的切片经缓存
  （`_currentSlice` 懒计算），手动 slice 每次迭代都分配新列表。

### 2.4 性能反模式（禁止）

| 反模式 | 后果 | 正确做法 |
|---|---|---|
| 循环内调用资源变换过滤器 | N 次 IO / 进程 spawn | 循环外 assign 一次 |
| 循环内用正则过滤器 | N 次正则编译 | 用 `where` / `map` 替代 |
| 模板间复制大段 HTML | 每份独立解析，缓存失效 | 提取为 partial include |
| 深层嵌套 include（>3 层） | 解析链变长，缓存查找开销增 | 扁平化 partial 结构 |
| 循环内重新计算不变量 | 冗余计算 | 循环外 assign |

---

## 3. Fluid 使用规范

### 3.1 语法规则

Liquid 用 `{% %}` 标签语法（Scriban 用 `{{ }}` 做控制流）：

```liquid
{% assign x = 1 %}                {# 赋值 #}
{% if x > 0 %}…{% endif %}        {# 条件 #}
{% for item in list %}…{% endfor %}{# 循环 #}
{% include 'partials/x' %}        {# 共享作用域 partial #}
{% render 'partials/x' %}         {# 隔离作用域 partial #}
{% comment %}…{% endcomment %}    {# 注释 #}
```

插值用 `{{ }}`（与 Scriban 相同）：`{{ page.title }}`。

### 3.2 成员访问

- 大小写不敏感：`page.title` 与 `page.TITLE` 等价。
- snake_case 映射：`page.word_count` 命中 `PageContext.WordCount`，
  `page.rel_permalink` 命中 `RelPermalink`。
- 空值语义：缺失成员取空（不抛异常），与 Hugo 宽容度对齐。
- 默认不转义：`{{ page.content }}` 原样输出 HTML。
- `params` / `data` 字典大小写不敏感：`params.foo` 与 `params.Foo` 等价。

### 3.3 变量作用域

| 形态 | 作用域 | 调用方 assign 可见 | page/site 模型可见 |
|---|---|---|---|
| `{% include 'x' %}` | 共享 | 是 | 是 |
| `{% render 'x' %}` | 隔离 | 否 | 是 |

`render` 的隔离作用域指调用方的局部 assign 不可见，但 `page.*` /
`site.*` 全局模型始终可用（模型在每渲染上下文绑定，不经作用域传递）。

### 3.4 已知陷阱（必须规避）

1. **正则 pattern 反斜杠须双写**：Liquid 单引号串把 `\` 当转义符，
   `'\w'` 直接解析失败。pattern 里的反斜杠须双写：
   `find_re: '(\\w+)'` 才是一个 `\w`。

2. **日期默认串是 UTC**：Fluid 的 `DateTimeValue` 输出 UTC（`…Z`），
   与 Scriban 的本地偏移不同。模板内日期显式格式化：
   `{{ page.date | date: '%Y-%m-%dT%H:%M:%S%:z' }}`。

3. **`reading_time` 是视图对象**：`page.reading_time.total_minutes`
   经 `ReadingTimeView` 暴露（Fluid 会把 TimeSpan 转成 DateTimeOffset，
   注册访问器返回保真视图）。字符串形态是 `00:01:00`。

4. **菜单/taxonomy 用索引器语法**：`site.menus.main` /
   `site.taxonomies.categories` 经 `IndexerMemberAccessor` 路由到
   C# 索引器。缺失键返回空集合（不抛异常）。

5. **`site.language` 是字符串**：Fluid 侧是语言码本身（如 `zh-cn`），
   不是 Scriban 侧的 `LanguageObject`（无 `.locale` / `.lang` 成员）。

6. **Liquid 无对象字面量**：`group` 过滤器返回二元组列表
   `[key, items]`（不是 dict）。消费形态：
   `{% for pair in pages | group: 'section' %}{{ pair[0] }}: {{ pair[1].size }}{% endfor %}`。

7. **Liquid 无对象方法调用**：`page.get_page "x"` 这类方法调用不可用。
   方法需求走过滤器或预计算。

### 3.5 配置写入位置

`engine` 配置写在 Flint.toml **任何段头之前**。追到 `[params]` 段后
TOML 会把它归进 params 子表（标准 TOML 语义，2026-10-19 实测坑）。

```toml
baseURL = "http://localhost:1313/"
engine = "scriban"          # 正确：段头之前

[params]
description = "..."         # 段内键归 params 子表
```

---

## 4. 函数使用规范

### 4.1 函数平面总览

Flint Fluid 模板可用两类函数：

| 类别 | 数量 | 来源 |
|---|---|---|
| Fluid 内置过滤器 | 72 个 | Fluid 库（`where`/`sort`/`map`/`slice`/`date`/`plus` 等） |
| Flint 追加 Hugo 名过滤器 | 约 50 个 | `FluidHugoFilters` + `FluidResourceFilters` + `FluidTemplateFilters` |

### 4.2 选择规范：优先用内置，再用 Flint 追加

**优先级从高到低**：

1. **Fluid 内置过滤器**：编译级优化，性能最好。覆盖数学
   （`plus`/`minus`/`times`/`divided_by`/`modulo`/`round`/`ceil`/
   `floor`/`abs`）、字符串（`replace`/`split`/`join`/`upcase`/
   `downcase`/`strip`/`truncate`/`escape`）、集合（`where`/`sort`/
   `map`/`reject`/`uniq`/`compact`/`concat`/`slice`/`reverse`/`first`/
   `last`/`size`/`sum`）、日期（`date`/`format_date`）、哈希
   （`md5`/`sha1`/`sha256`）、编码（`base64_*`/`url_encode`/
   `url_decode`）。

2. **Flint 追加 Hugo 名过滤器**：Fluid 内置不覆盖的 Hugo 名。
   文本族（`titleize`/`humanize`/`urlize`/`singularize`/`pluralize`/
   `markdownify`/`plainify`）、集合族（`delimit`/`after`/`union`/
   `intersect`/`complement`/`group`/`apply`/`seq`/`shuffle`）、
   path 族（`path_base`/`path_dir`/`path_ext`/`path_join`/`path_split`）、
   URL 族（`abs_url`/`rel_url`）、i18n（`i18n`/`T`）。

3. **资源变换过滤器**：Hugo Pipes 的 Liquid 形态（`resource`/
   `resource_concat`/`resource_fingerprint`/`css_sass`/`js_build` 等）。
   产物型变换结果经 `GeneratedResources` 收集后由 SiteBuilder 写盘。

### 4.3 高频过滤器速查

| 需求 | 过滤器 | 示例 |
|---|---|---|
| 最近 N 篇文章 | `where` + `sort` + `reverse` + `slice` | `site.regular_pages \| where: 'draft', false \| sort: 'date' \| reverse \| slice: 0, 5` |
| 按标签过滤 | `where` | `pages \| where: 'tags', 'go'` |
| 列表拼接 | `join` / `delimit` | `tags \| join: ', '` / `tags \| delimit: ', ', ' and '` |
| 字符串变形 | `titleize` / `humanize` / `urlize` | `'my-post' \| humanize` → `My post` |
| 绝对 URL | `abs_url` | `'/img/x.png' \| abs_url` |
| Markdown 转 HTML | `markdownify` | `'**bold**' \| markdownify` |
| 路径拼接 | `path_join` | `'css' \| path_join: 'main.css'` → `css/main.css` |
| 整数序列 | `seq` | `5 \| seq: first: 1, step: 2` → `[1, 3, 5]` |
| 分组 | `group` | `pages \| group: 'section'` → `[['posts', [...]], ['about', [...]]]` |
| 逐元素变换 | `apply` | `pages \| apply: 'titleize'` |
| 资源指纹 | `resource_fingerprint` | `$r \| resource_fingerprint` → `theme.abc123.css` |
| SCSS 编译 | `css_sass` | `$r \| css_sass` → 编译后 .css 资源 |
| JS 打包 | `js_build` | `$r \| js_build: format: 'esm'` |
| 翻译 | `i18n` | `'read_more' \| i18n` |
| 正则查找 | `find_re` | `text \| find_re: '(\\w+)@(\\w+)'`（反斜杠双写） |

### 4.4 资源变换过滤器使用规范

产物型变换（`resource_concat` / `resource_copy` /
`resource_fingerprint` / `resource_from_string` / `resource_minify` /
`css_sass` / `css_build` / `js_build`）的结果经渲染器
`GeneratedResources` 收集，由 SiteBuilder 输出阶段写盘。模板引用的
`RelPermalink` 因此真实存在（不会 404）。

**使用规则**：

- 资源变换在模板顶层或循环外调用，不在循环内（见 §2.3）。
- `css_sass` 的 `vars` 命名实参接受 SCSS 变量前导文本（Liquid 无
  对象字面量，Hugo 的 dict vars 用前导文本表达）：
  `{{ $r | css_sass: vars: '$primary: #333;' }}`。
- `js_build` 的 `format: 'esm'` 保持 ES 模块形态；`params: '<json>'`
  提供 `@params` 虚拟模块。
- `resource_concat` 的位置实参为待拼接资源（或路径字符串），
  `target` 命名实参为产物名：`{{ $a | resource_concat: $b, target: 'all.css' }}`。

### 4.5 函数分级与递延（P4-1 台账）

**已实现（T1）**：§4.2 全部过滤器。

**递延（T2）**：`strings.*` / `math.*` 命名空间**函数**形态
（Liquid 语法无方法调用，过滤器形态是唯一自然表达）；
`group` 的 dict 返回形态（Liquid 无对象字面量，二元组列表是唯一
可消费形态）。

---

## 5. 函数自定义规范（引擎开发者）

### 5.1 注册位置

新过滤器按类别注册到对应文件：

| 过滤器类别 | 注册文件 | 注册方法 |
|---|---|---|
| Hugo 名文本/集合/i18n | `FluidHugoFilters.cs` | `FluidHugoFilters.Register(options)` |
| 资源与资产变换 | `FluidResourceFilters.cs` | `FluidResourceFilters.Register(options, provider)` |
| URL 族 | `FluidTemplateFilters.cs` | `FluidTemplateFilters.Register(options)` |

三个 Register 方法在 `FluidTemplateRenderer` 构造函数中调用一次
（`_options` 构造后）。新增过滤器只需在对应 Register 方法中加一行
`f.AddFilter("name", Handler)`。

### 5.2 过滤器实现规范

**签名**：`private static ValueTask<FluidValue> Handler(FluidValue input, FilterArguments args, TemplateContext ctx)`

**必须遵守**：

- **AOT 安全**：禁反射（`Type.GetProperty` / `GetMethod`）、禁
  `dynamic`、禁运行时代码生成。固定 pattern 的正则用
  `[GeneratedRegex]` source-gen；用户 pattern 用 `Regex` 非 source-gen
  （与 `find_re`/`replace_re` 同口径，pattern 来自模板作者可信源）。
- **每渲染数据经 AmbientValues**：不用静态字段传每渲染数据（baseUrl、
  翻译表、资源提供者、产物队列）。静态可变状态在并行多站点构建下
  会串站点。AmbientValues 键在对应过滤器类中定义为 `internal const string`。
- **纯函数或经 AmbientValues 取数**：过滤器本身不做 IO。IO 需求
  （资源读取、Sass 编译）经 `TemplateAssetTransforms` 服务或
  `ITemplateResourceProvider` 提供者。
- **空值安全**：`input.IsNil()` 时返回合理默认值（空串/空列表/null），
  不抛异常。与 Hugo 宽容度对齐。
- **文本函数共享**：文本变换（humanize/urlize/singularize 等）经
  `TemplateTextFunctions` 单一事实源，不在过滤器内重复实现。

**推荐遵守**：

- 同步完成的过滤器用 `new(FluidValue.Create(...))` 返回
  `ValueTask<FluidValue>`（不标 async），避免状态机开销。
- 需要 await 的过滤器（枚举集合、成员访问）标 `async`，用
  `await` 不用 `GetAwaiter().GetResult()`（并行渲染下同步阻塞有
  线程池饥饿风险）。
- 过滤器名同时注册 Hugo 驼峰与 snake 两种拼写（如 `abs_url` /
  `absURL`），对齐 Hugo 模板的两种写法。

### 5.3 共享服务规范

跨引擎共享的变换核心放 `TemplateAssetTransforms`（静态无状态）：

- `MinifyCss` / `MinifyJs`：委派 `BuiltinTemplateFunctions`（Scriban 侧
  既有实现，单一事实源）。
- `CompileSass`：外部 Dart Sass CLI 编译，`varsPrelude` 参数接受
  SCSS 变量前导文本。
- `BuildJs`：ES module 打包（`EsmBundler`）+ TS 扩展名改写。
- `Concat` / `Copy` / `Fingerprint` / `FromString`：资源派生。

新增共享变换时：核心逻辑放 `TemplateAssetTransforms`，Scriban 侧
（`TemplateResourceFunctions` 命名空间函数）与 Fluid 侧
（`FluidResourceFilters` 过滤器）各自只做参数形态解析。

### 5.4 测试规范

新增过滤器必须同时满足：

- **行为测试**：精确相等断言（`Assert.Equal(expected, actual)`），
  不用 `Contains` / `NotContain` / 计数断言。产物型变换须断言
  内容级结果（如 fingerprint 断言哈希段存在、minify 断言压缩后
  内容形态），不能只断言资源名存在。
- **边界测试**：空值（null/空串/空列表）、单元素、错误路径
  （模板不存在、解析失败、坏正则）。
- **AOT 冒烟**：若过滤器访问模型成员，对应模型类型须在
  `FluidTemplateRenderer` 的 `[DynamicDependency]` 清单中，且有
  Fluid 渲染测试触及该成员（AOT 失败是静默的，无测试不会红）。
- **对拍测试**：与 Scriban 侧同名函数的行为对拍（一致或有文档化
  差异，差异写入 `docs/FLINT-VS-HUGO.md`）。

### 5.5 隔离硬规则（CONVENTIONS §8.4.2）

Fluid 热路径（`FluidTemplateRenderer`、`FluidBuiltinTemplateProvider`、
Liquid 版函数/短代码/render hooks）禁止出现：

- `Scriban.Template` / `ScriptObject` / `BuiltinTemplateFunctions` 静态调用
- Scriban 模板缓存、依赖缓存、资源追踪器

允许共享：输入模型（`PageContext`/`SiteContext`/`Params`/`Data`）、
模板查找顺序、输出写盘/指纹/SRI、短代码与 render hooks 的服务接口、
`TemplateAssetTransforms` 变换核心、`TemplateTextFunctions` 文本函数。

承载物：`EngineIsolationTests`（IL 扫描）+ `engine-probe`（运行期
装配探针）。新增 Fluid 侧代码先对照本表再动手。

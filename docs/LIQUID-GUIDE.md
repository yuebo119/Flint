# Liquid 模板指南（Fluid 引擎）

| 项目 | 内容 |
|---|---|
| 日期 | 2026-10-19 |
| 状态 | 生效（FLUID-MIGRATION-PLAN Phase 0-6 实施后） |
| 相关提交 | 见 `git log --oneline --grep=Fluid` 迁移系列 |
| 目标读者 | 写 Flint 新站点模板的模板作者；维护双引擎的引擎开发者 |

## 1. 双引擎与路由

Flint 自 2026-10-19 起为双模板引擎：**Fluid（Liquid）主力 + Scriban 兼容层**。
新站点默认生成 `.liquid` 脚手架；Hugo 主题（`.html`）与 ThemeMigrator 产物
继续走 Scriban。两条引擎平行，不共享缓存/依赖表/资源追踪器
（隔离硬规则见 `docs/CONVENTIONS.md` §8.4）。

### 1.1 路由规则

| 模板来源 | 引擎 |
|---|---|
| 站点 `layouts/**/*.liquid` | Fluid |
| Fluid 原生站点（layouts 含任一 `.liquid`）的内置回退模板 | Fluid |
| Hugo 主题 `layouts/**/*.html` | Scriban |
| ThemeMigrator 输出 `.html` | Scriban |
| 显式 `engine: scriban` 配置 | 全站 Scriban，兼容模式 |
| 显式 `engine: fluid` 配置 | 全站 Fluid，实验模式 |

默认（`engine: auto`）即上表前四行的双轨分发；同根内 `.liquid` 优先于
`.html`（站点加 `.liquid` 覆盖既有 `.html` 是既定迁移路径）。
配置写在 Flint.toml **任何段头之前**（追到 `[params]` 段后 TOML 会把它归进
子表——2026-10-19 实测坑）。

### 1.2 CLI

```bash
Flint new site mysite                # 默认生成 .liquid 脚手架
Flint new site mysite --engine scriban   # 旧 .html 脚手架（Hugo 兼容模式）
```

## 2. .liquid 模板编写规范

### 2.1 基本语法（Liquid 标准 + Fluid 扩展）

```liquid
{%- comment -%} 赋值、条件、循环 {%- endcomment -%}
{% assign recent = site.regular_pages | slice: 0, 5 %}
{% if page.title %}<h1>{{ page.title }}</h1>{% endif %}
{% for post in recent %}
  <a href="{{ post.permalink }}">{{ post.title }}</a>
{% endfor %}
{% include 'partials/head' %}          {# include 共享作用域 #}
{% render 'partials/card' %}           {# render 隔离作用域 #}
```

**partial 语义（P4-5）**：Hugo 的 `partial "x" (dict "a" 1)` 传 dict 上下文，
Liquid 无对象字面量，等价形态是 **assign 后 include**（共享作用域，
partial 内直取变量）；`render` 是隔离作用域（调用方 assign 不可见，
页面成员 `page.*`/`site.*` 仍可用——模型全局绑定）。两种作用域语义由
`include_共享作用域` / `render_隔离作用域` 单测锁定。

- 成员访问**大小写不敏感**，且支持 snake_case：`page.word_count` 命中
  `PageContext.WordCount`（`FlintMemberAccessStrategy` 映射）。
- 默认**不转义**（与 Scriban 侧 HTML 契约一致）：`{{ page.content }}` 原样输出。
- `params`/`data` 字典经 OrdinalIgnoreCase 副本绑定：`params.foo` 与
  `params.Foo` 等价。
- 空值语义：缺失成员取空（不抛异常），与 Hugo 宽容度对齐。

### 2.2 脚手架文件清单（`Flint new site` 默认）

```
layouts/
├── index.liquid                  # 首页
├── _default/
│   ├── baseof.liquid             # 基础布局（参考）
│   ├── single.liquid             # 单页
│   ├── list.liquid               # 列表页
│   ├── taxonomy.liquid           # 分类法列表
│   └── term.liquid               # 词条页
└── partials/
    ├── head.liquid               # <head>
    ├── header.liquid             # 页头（site.menus.main 菜单）
    └── footer.liquid             # 页脚（site.build_date 年份）
```

辅助模板（站点提供时覆盖内置生成器）：`404.liquid`、`robots.liquid`、
`sitemap.liquid`、`rss.liquid`。404 是**模板门控**产物（无模板不产出，
与 Hugo 一致）；sitemap/rss 无模板时走内置 C# 生成器。

### 2.3 内置回退模板（Hugo embedded templates 的 Liquid 等价）

站点缺布局时按 kind 回退：`list`（section）/ `term` / `taxonomy`，
另有 `pagination` 与内置 partial `opengraph` / `schema` /
`twitter_cards` / `google_analytics` / `disqus` /
`_funcs/get-page-images`。与 Scriban 内置版**逐条同构**（空白归一化后
输出一致，`EngineBuiltinParityTests` 8 项对拍锁定）。

### 2.4 内容管线（.liquid 短代码与 render hooks）

```
layouts/shortcodes/notice.liquid       # {{< notice type="warning" >}}…{{< /notice >}}
layouts/_markup/render-link.liquid     # Markdown 链接定制渲染
```

变量约定（与 Scriban 侧一致）：短代码有 `params`（命名+位置数字键）/
`positional` / `inner` / `name` / `page` / `site` / `ordinal` / `parent`；
render hook 有 `destination` / `text` / `plain_text` / `level` / `id` /
`title` 等平铺变量。同根内 `.liquid` 覆盖 `.html`；`engine: scriban`
模式下不注册 `.liquid` 内容（缺失短代码构建失败）。

内置短代码（figure/details/qr/highlight/ref/relref/gist/youtube/tweet/
vimeo/instagram 等）是 **C# 实现**，与引擎无关，两种站点都可用。
`disableShortcodes = true` 时连内置短代码也不注册（内容管线完全不经
短代码，P5-3）。短代码注册与 render hook 装配经 `IShortcodeRenderer` /
`IRenderHookRenderer` 接口（P5-1/P5-2，TemplateEngineRouter 实现），
SiteBuilder 不认识具体引擎。

## 3. 函数清单

### 3.1 Fluid 内置（72 个，节选高频）

`default` `size` `first` `last` `join` `split` `replace` `truncate`
`truncatewords` `strip_html` `where` `sort` `sort_natural` `map` `reject`
`uniq` `compact` `concat` `slice` `reverse` `sum` `round` `ceil` `floor`
`abs` `plus` `minus` `times` `divided_by` `modulo` `at_least` `at_most`
`date` `format_date` `escape` `escape_once` `upcase` `downcase`
`capitalize` `lstrip` `rstrip` `strip` `json` `base64_*` `md5` `sha1`
`sha256` `url_encode` `url_decode` `handle` `handleize` `find`
`find_index` `has` `prepend` `append` `remove` `remove_first`
`remove_last` `newline_to_br` `strip_newlines` `time_zone`

### 3.2 Flint 追加（Hugo 名，对齐 Scriban 侧语义）

| 过滤器 | 语义 | 备注 |
|---|---|---|
| `abs_url` / `absURL` | baseURL origin + 子路径 + 路径 | 与 Scriban `absURL` 同语义 |
| `rel_url` / `relURL` | 子路径 + 路径 | 同上 |
| `titleize` / `title` | 逐词首字母大写 | 不过滤虚词 |
| `humanize` | `-`/`_` 转空格 + 首字母大写 | 共享 `TemplateTextFunctions` |
| `urlize` / `anchorize` | 小写 + 非字母数字转连字符 | 同上 |
| `singularize` | ies→y、去尾 s | Flint 既有规则（双引擎一致） |
| `pluralize` | `count \| pluralize: singular, plural` | 计数形态 |
| `markdownify` | Markdown → HTML | 共享 `MarkdownParser`（无钩子管道） |
| `plainify` | 剥 HTML 标签 | |
| `jsonify` | 序列化为 JSON | AOT 下对任意图形有裁剪风险（pragma 抑制） |
| `delimit` | `delimit SEP [LAST]` | 末分隔符可定制 |
| `after` | `after N` 跳过前 N 个 | |
| `union` / `intersect` / `complement` | 集合运算 | 字符串集合 |
| `resource` / `resources_get` | `'css/x.css' \| resource` | Hugo `resources.Get` 的过滤器形态 |
| `resources_match` | glob 匹配 | Hugo `resources.Match` |
| `resources_by_type` | 按类型列出 | Hugo `resources.ByType` |
| `resource_concat` | `$a \| resource_concat: $b, target: 'x.css'` | 产物型，结果落盘（P4-3） |
| `resource_copy` | `$r \| resource_copy: 'other.css'` | 产物型 |
| `resource_fingerprint` | `$r \| resource_fingerprint` | 内容哈希进文件名，产物落盘 |
| `resource_from_string` | `'name' \| resource_from_string: '内容'` | 虚拟资源构造 |
| `resource_minify` | `$r \| resource_minify` | 按类型压缩（css/js） |
| `css_sass` / `sass` / `to_css` | `$r \| css_sass` | 外部 Dart Sass CLI 编译，产物 .css |
| `css_build` | `$r \| css_build` | 内容可选压缩（minify 缺省 true） |
| `js_build` | `$r \| js_build` | ES 打包 + TS 扩展名改写 + 可选压缩 |
| `path_base` / `path_dir` / `path_ext` / `path_join` / `path_split` | Hugo `path.*` 的过滤器形态 | |
| `group` | `$pages \| group: 'section'` | 二元组列表 `[key, items]`（Liquid 无对象字面量） |
| `apply` | `$seq \| apply: 'upcase'` | 逐元素应用指定名过滤器 |
| `seq` | `5 \| seq: first: 2, step: 2` | 整数序列（Hugo seq 语义） |
| `shuffle` | `$items \| shuffle` | 随机乱序（Random.Shared） |
| `find_re` / `replace_re` | 正则查找/替换 | **pattern 的反斜杠须双写**（见 §4） |
| `i18n` / `T` | 翻译表查键，缺键空串 | 站点 i18n/ 经 AmbientValues 传入 |
| `safe_html` 等 6 个 | 恒等透传 | Flint 默认不转义 |

产物型变换（concat/copy/fingerprint/from_string/minify/sass/build）的结果经
渲染器 `GeneratedResources` 收集，由 SiteBuilder 输出阶段写盘——模板引用的
`RelPermalink` 因此真实存在。css/js 变换核心走 `TemplateAssetTransforms`
服务（与 Scriban 侧命名空间函数同一组原语：MinifyCss/MinifyJs/Sass CLI/EsmBundler）。

### 3.3 分级与递延（P4-1 台账）

**T1 已实现**：上表全部（P4-2 的 strings/math/collections/date/path/
urls/compare 全覆盖——math/compare/date 由 Fluid 内置 72 个过滤器与操作符
提供，urls/path/strings/collections 的 Hugo 名见上表；P4-3 产物型与
P4-4 资产变换含 vars/format/params 选项的 Liquid 形态）。**T2 递延**：
`strings.*`/`math.*` 命名空间**函数**形态（Liquid 语法无方法调用，
过滤器形态是唯一自然表达——映射表见 §3.2）；`group` 的 dict 返回形态
（Liquid 无对象字面量，二元组列表是唯一可消费形态）。

## 4. 已知差异清单（Liquid vs Scriban/Hugo）

实施期实测并登记（对拍测试锁定一致性项，差异项逐条记录）：

1. **空白形态**：Liquid `{% %}` 标签行与 Scriban `{{ }}` 行尾空白不同；
   内置模板对拍经空白归一化。站点模板作者可用 `{%- -%}` 控制 trim。
2. **日期默认串**：Fluid 的 `DateTimeValue` 输出 UTC（`…Z`），Scriban 输出
   本地偏移。内置模板内日期显式经 `| date: '%Y-%m-%dT%H:%M:%S%:z'` 格式化
   规避；站点模板建议同样显式格式化。
3. **`reading_time` 阅读时长**：Fluid 的 `FluidValue.Create` 会把 `TimeSpan`
   转成 `DateTimeOffset`（纪元+时长），故 `page.reading_time` 经注册访问器
   返回 `ReadingTimeView`（`total_minutes`/`minutes` 双形态），字符串形态
   与 Scriban 一致（`00:01:00`）。
4. **`_funcs/get-page-images`**：Liquid 版经 include 共享作用域 assign
   回写 `page_images`，赋**原始图片路径数组**（Liquid 无对象字面量）；
   Scriban 版经 partialValue 返回 `{Permalink, RelPermalink}` 对象。
   调用方须自行 `| abs_url`。
5. **菜单/taxonomy 索引器**：`site.menus.main` / `site.taxonomies.categories`
    经 `IndexerMemberAccessor` 路由到 C# 索引器（缺失键空集合）。
6. **`site.language`**：Scriban 侧是 `LanguageObject`（`.locale`/`.lang` 等
   成员），Fluid 侧是语言码字符串本身——内置模板的 locale 链已按此对齐。
7. **复数规则**：`singularize` 是 Flint 既有简化规则（`boxes → boxe`，
   不过滤 `-es`），非完整 Hugo 变形表。
8. **正则 pattern 的反斜杠**：Liquid 单引号串把 `\` 当转义符，`'\w'` 直接
   解析失败（"End of tag '}}' was expected"）——pattern 里的反斜杠须
   **双写**（`'(\\w+)'` 才是一个 `\w`；`find_re`/`replace_re` 实测陷阱）。
9. **`css_sass`/`js_build` 的选项形态**：Liquid 无对象字面量，Scriban 侧的
   dict 选项在 Liquid 用**命名实参**表达（`css_sass: vars: '$primary: #333;'`
   的 vars 是 SCSS 变量前导文本而非键值映射——同一用例的 Liquid 原生形态；
   `js_build: format: 'esm', params: '<json>'` 与 Scriban 同义）。
   `group` 返回二元组列表 `[key, items]`（Liquid 无对象字面量，
   Scriban 侧返回 dict）。

## 5. 性能与验证口径

- **BenchmarkDotNet 模板级基准**（P6-1，`EngineRenderBenchmarks`，
  .NET 11 RC2 / Ryzen 9 8945HX，2026-10-19）：Fluid 1.367μs / 2.76KB
  vs Scriban 15.103μs / 45.33KB，**比值 0.09**（快约 11 倍、每次渲染
  分配少约 16 倍）。运行：`dotnet run -c Release --project
  tests/Flint.PerformanceTests -- bench --filter *EngineRenderBenchmarks*`。
- **模板级对拍**（perf 套件 `双引擎渲染对拍`，同逻辑模板 3000 次交错
  三轮）：Fluid 190,135 页/秒 vs Scriban 56,192 页/秒（0.296x）——
  含循环开销的口径，BDN 是纯渲染严格口径，两者都进报告。
- **站点级 A/B**（`scripts/engine-ab.sh [文章数] [轮数] [CLI路径]`，
  300 篇同内容交错三轮中位数，2026-10-22）：JIT 口径总计 Fluid
  498.2ms vs Scriban 684.9ms（0.727x）、AOT 口径 102.0ms vs
  186.3ms（0.548x）；产物文件集 343 个完全一致。站点级构建含重 IO，
  是 BDN 反模式，故用交错中位数脚本而非 BDN。第三参传 Release+AOT
  发布产物即得生产口径（JIT 会把非渲染段膨胀 10~15 倍，稀释渲染
  优势）。
- NativeAOT 冒烟：AOT 二进制构建 .liquid 站点 10 页全正确；模型属性经
  `DynamicDependency` 保留清单防裁剪（清单随模型类型演进而维护）。
  AOT 体积对比（迁移前 1a6e31f vs 迁移后，win-x64 单文件）：
  40.041 MB → 40.231 MB（**+0.48%**）。
- 运行期装配探针：`dotnet run --project src/Flint.DevTools -- engine-probe`
  （Fluid 渲染路径不加载 Scriban 程序集，P6-3）。探针含**router 级
  全链路**断言：经 TemplateEngineRouter（Scriban 工厂懒构造，Lazy&lt;T&gt;
  线程安全）+ SiteBuilder 完整构建纯 Fluid 站点，Scriban 渲染器全程
  未构造（P2-4 实质化：新站点构建不加载 Scriban 程序集——此前 router
  急构 Scriban，“不加载”只是名义）。
- 静态隔离门禁：`EngineIsolationTests`（IL 扫描，mutation probe 验证灵敏度）。
- 增量重渲（P1-3）：`.liquid` partial 变更触发依赖页面重渲染
  （`FluidIncrementalTests` 锁定；修复了增量路径 templateChanges 只认
  .html 的过滤缺口，以及合成 home 页不在树里导致模板变化不重渲的
  兜底缺口）。
- 审计修复回归（2026-10-21）：`FluidAuditFixTests` 锁定 router 懒构造
  并发单实例、include 路径/文本缓存（mtime+length 验效 + 构建边界
  失效）、resource_concat 字符串路径实参。
- 主题矩阵回归（29 个矩阵站点，迁移后二进制 vs 迁移前二进制同命令重建）：
  **零回归**——24 个站点页数一致；6 个站点（archie/congo/hermit/hextra/
  mainroad/relearn）在两个二进制上同样失败（预先存在的主题不兼容，
  非迁移引入）。
- 回归修复记录（2026-10-19）：`SiteBuilder.Render` 的三处
  `is ScribanTemplateRenderer` 类型检查在 router 模式下恒 false，
  曾导致 .GetPage 查不到 section 页（hugo-book 实测）、翻译表回填与
  零 IO 依赖判定失效——已修为 router 穿透。

## 6. Liquid 与 Scriban 对比（旧引擎 → 新引擎）

### 6.1 共享与隔离

| 维度 | 共享 | 隔离 |
|---|---|---|
| 输入模型 | `PageContext` / `SiteContext` / `Params` / `Data`（同一份对象，两引擎直绑） | — |
| 模板查找 | 候选链顺序、主题叠加语义（`TemplateLookup` 扩展名参数化，两引擎各持一实例） | 描述符缓存 |
| 输出 | 写盘、指纹、SRI、输出格式变体 | — |
| 内容管线 | 短代码/render hook 的服务接口（`IShortcodeRenderer` / `IRenderHookRenderer`） | 具体实现按扩展名分发 |
| 缓存 | — | 模板缓存、依赖表、资源追踪器、include 路径/文本缓存各自独立 |
| 内置模板 | — | Scriban 内置（Hugo 主题用）与 Liquid 内置（新站点用）两套，逐条同构 |

### 6.2 语法与语义差异

| 方面 | Scriban（旧） | Liquid/Fluid（新） |
|---|---|---|
| 插值 | `{{ page.title }}` | `{{ page.title }}`（相同） |
| 控制流 | `{{ for x in y }}` / `{{ if x }}` | `{% for x in y %}` / `{% if x %}`（标签语法） |
| 赋值 | `{{ x = 1 }}` | `{% assign x = 1 %}` |
| partial | `{{ include "x" }}` / `{{ partial "x" . }}` | `{% include 'x' %}`（共享作用域）/ `{% render 'x' %}`（隔离作用域） |
| 对象字面量 | `{{ dict "a" 1 }}` | **无**（Liquid 语法限制；`group` 返回二元组列表替代） |
| 对象方法调用 | `page.get_page "x"` | **无**（仅属性访问；方法需求走过滤器） |
| 块捕获 | `{{ capture x }}…{{ end }}` | 无（assign 拼接替代） |
| HTML 转义 | 默认不转义 | 默认不转义（相同契约） |
| 成员大小写 | 不敏感 | 不敏感（相同） |
| snake_case 成员 | `ScriptDictWithSnakeAliases` 别名表 | `FlintMemberAccessStrategy` 映射（相同效果） |
| 空值语义 | 缺失成员取空不抛 | 相同 |
| 函数平面 | 25 个命名空间 / 200+ 函数 | Fluid 内置 72 过滤器 + Flint 追加 Hugo 名过滤器（见 §3.2） |

### 6.3 性能（三口径，全部本轮实测）

| 口径 | 方法 | Fluid | Scriban | 比值 |
|---|---|---|---|---|
| 模板级（BDN 严格） | `EngineRenderBenchmarks`，同逻辑模板单次渲染 | 1.477 μs / 2.6 KB | 19.685 μs / 45.33 KB | **0.08x**（快约 13 倍，分配少约 17 倍） |
| 模板级（perf 套件） | 3000 次交错三轮 | 190,135 页/秒 | 56,192 页/秒 | 0.296x |
| 站点级（端到端，JIT） | 300 篇同内容交错三轮中位数，Debug CLI | 498.2 ms | 684.9 ms | **0.727x** |
| 站点级（端到端，AOT） | 同上，Release + NativeAOT CLI | 102.0 ms | 186.3 ms | **0.548x** |
| 站点级渲染段（AOT） | 同上，仅渲染+写盘阶段 | 52.9 ms | 113.1 ms | 0.47x |
| 站点级渲染段（JIT） | 同上，仅渲染+写盘阶段 | 170.8 ms | 300.3 ms | 0.569x |

**口径解读**（2026-10-22 复测，产物 343 文件对称）：

1. **AOT 才是生产口径**。JIT 形态下非渲染阶段被 JIT 编译开销膨胀
   10~15 倍（解析段 JIT 141ms vs AOT 7ms、页面上下文 42 vs 2.3ms），
   渲染段占比 JIT 34% vs AOT 52%，站点级优势因此被稀释：JIT 口径
   0.727x，AOT 口径 0.548x（Fluid 站点级快约 45%）。
2. **稀释系数**（站点级比值 ÷ 渲染段比值）：AOT 1.17、JIT 1.28。
   剩余稀释来自双引擎共享的非渲染阶段，AOT 形态下最大项是
   扫描+解析流式段（Fluid 27.3ms，占构建 27%）与写盘（NTFS 小文件
   物理墙，Hugo 同样承担）。
3. **优化轨迹**：迁移期 0.821x → 迁移后修复 0.777x → 非渲染阶段
   三轮优化后 JIT 0.727x / AOT 0.548x。三轮优化 = CleanOutput 默认
   值修复（消除每次构建全量删+重写，重复构建 -27.3%）、写盘同步化
   （overlapped IRP 开销）、扫描+解析流式重叠（阶段级 -4.1%）。
4. BDN 与 perf 套件的差距（0.08x vs 0.296x）来自循环开销与 GC 采样
   口径不同，两者都进报告。

### 6.4 二进制体积

NativeAOT 单文件（win-x64）：迁移前 40.041 MB → 迁移后 40.231 MB，
**+0.48%**（双引擎共存，Fluid 运行时 + 保留清单的代价）。

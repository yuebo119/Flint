// Flint 主题迁移工具
// AST → Scriban 转换器（结构驱动，非字符串切分）
//
// 与正则版的关键区别：所有转换决策基于 AST 结构，故嵌套表达式必然正确。
// 每个产出都过"结构守恒"检查——这是静默损坏的防线（正则版实测会把
// `lt (len .Pages) 10` 转成语义垃圾 `(len < page.pages) 10`）。

using System.Text;

namespace Flint.ThemeMigrator.Conversion;

/// <summary>
/// 表达式转换器：AST → Scriban 文本。
/// 无状态（上下文栈按调用传入），可并发使用。
/// </summary>
internal sealed class ScribanConverter(
    MigrationMap map,
    IReadOnlySet<string>? valueReturningPartials = null,
    string? selfPartialName = null,
    bool selfNamedTemplateExtracted = false)
{
    private readonly MigrationMap _map = map;

    /// <summary>含 {{ return }} 的 partial 规范名集合（Hugo 返回值语义）</summary>
    private readonly IReadOnlySet<string>? _valueReturning = valueReturningPartials;

    /// <summary>本文件对应的 partial 规范名（null = 非 partial 文件）</summary>
    public string? SelfPartialName { get; } = selfPartialName;

    /// <summary>
    /// 本文件内是否**确有**同名命名模板被提取（define 与文件同名的情形）。
    /// 只有它为真时调用路径才加后缀——否则会把"partial 递归调用自己"这种
    /// 合法写法改成指向不存在的文件
    ///（fixit 的 camel-case-keys.html 递归处理嵌套 map 实测：
    ///  "Could not find a part of the path …camel-case-keys__named"）
    /// </summary>
    private readonly bool _selfNamedTemplateExtracted = selfNamedTemplateExtracted;

    /// <summary>partialValue 的 Store 键前缀（与引擎侧 ScribanTemplateRenderer 一致）</summary>
    internal const string RetKeyPrefix = "__partial_ret_";

    /// <summary>
    /// 源码写 `page` / `Page` 时产出的**全局当前页**根名（引擎同名全局）。
    /// Hugo 实测（v0.166）：以 dict 调用 partial 时 `.` 是 dict，而 `page` 仍是当前页
    /// （`{{ .Resources | default page.Resources }}` 这类写法依赖它）；
    /// Flint 用 `page` 承担 dot 语义（迁移产物把 `.X` 统一成 `page.x`），
    /// 故源码侧 `page` 必须走另一个名字，否则两者相互覆盖
    /// （FixIt 的 plugin/image.html 实测 "$Resources.getmatch for a null object"）
    /// </summary>
    internal const string GlobalPageRoot = "__page";

    /// <summary>转换诊断（降级/不支持项）</summary>
    public List<string> Diagnostics { get; } = [];

    /// <summary>裸函数调用的实参：多 token 表达式需括号（避免被当作连续实参）</summary>
    private static string ParenthesizeIfNeeded(string text)
    {
        var t = text.Trim();
        return NeedsParens(t) ? "(" + t + ")" : t;
    }

    /// <summary>
    /// 表达式文本是否需要括号包裹才能作为函数实参：含空白（多 token）且未自带
    /// 括号/方括号/花括号包裹时需要
    /// </summary>
    private static bool NeedsParens(string text)
    {
        var t = text.Trim();
        if (t.Length == 0 || t.StartsWith('(') || t.StartsWith('[') || t.StartsWith('{'))
        {
            return false;
        }
        return t.Contains(' ', StringComparison.Ordinal);
    }

    /// <summary>
    /// 管道左值应作为**末参**的函数（Go/Hugo 语义：`X | f A B` == `f A B X`）。
    /// 这些函数的输入参数在末位（Hugo 为可管道化如此声明）：
    /// <c>resources.FromString(name, content)</c>、<c>resources.Copy(target, resource)</c>、
    /// <c>printf(format, args…)</c>、<c>errorf/warnf(format, args…)</c>、
    /// <c>i18n(key, args…)</c>。其余函数多为"输入在首"（Scriban 风格），
    /// 管道语义与 Scriban 一致，无需改写
    /// </summary>
    private static readonly HashSet<string> PipeValueLastFunctions = new(StringComparer.Ordinal)
    {
        "resources.FromString", "resources.Copy", "resources.Concat", "resources.ExecuteAsTemplate",
        "printf", "fmt.Printf",
        "errorf", "fmt.Errorf", "warnf", "fmt.Warnf",
        "erroridf", "fmt.Erroridf", "warnidf", "fmt.Warnidf",
        "i18n", "lang.Translate",
        // dict：Go 的 `dict KEY VALUE …` 键在前，管道左值补**末位值**
        //（`X | dict "Some"` = `dict "Some" X`）——Scriban 管道把左值放首参，
        // 直接沿用会产出 `{X: "Some"}`（键值颠倒）。FixIt 的 section.html 标题行
        // `.Section | dict "Some" | T "allSome"` 实测：此前整条链判不支持 →
        // 模板预检失败 → 文档列表页回退内置模板（95 字节）
        "dict", "collections.Dictionary",
        // index：Go 的 `index SET INDEX…` 值在末位（`$index | add -1 | index
        // $paginator.Pages` = `index $paginator.Pages (add $index -1)`）——
        // 直接沿用 Scriban 管道会产出 `index (add …) $pages`（参数颠倒）→ null，
        // `$lastElement.Date` 报 for a null object（even 的 section.html 实测）
        "index",
        // 正则/裁剪族（Hugo v0.166 管道形态实测确认"值在末位"）：
        //   replaceRE PATTERN REPLACEMENT INPUT · findRE PATTERN INPUT [LIMIT] ·
        //   findRESubmatch PATTERN INPUT [LIMIT] ·
        //   strings.Trim/TrimLeft/TrimRight CUTSET STRING ·
        //   strings.TrimPrefix PREFIX STRING · strings.TrimSuffix SUFFIX STRING
        // 漏掉它们的后果：`$content | replaceRE "<table(.*?)>" …` 被拼成
        // `replace_re $content …`，**页面正文落到模式位** →
        // "Invalid pattern '<!-- end-chunk -->…'"（monochrome 实测 4 处）
        "replaceRE", "replace_re",
        "findRE", "find_re",
        "findRESubmatch", "find_re_submatch",
        "strings.Trim", "strings.TrimLeft", "strings.TrimRight",
        "strings.TrimPrefix", "strings.TrimSuffix"
    };

    /// <summary>
    /// partial 名规范化：剥扩展名与路径前缀，使调用名与文件路径可对齐。
    /// "func/X.html" 与 "layouts/_partials/func/X.html" 都归一到 "func/X"
    /// </summary>
    internal static string CanonicalPartialName(string raw)
    {
        var n = raw.Replace((char)92, '/').Trim();
        if (n.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            n = n[..^5];
        }
        foreach (var prefix in new[]
                 {
                     "layouts/_partials/", "layouts/partials/", "_partials/", "partials/", "/"
                 })
        {
            if (n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                n = n[prefix.Length..];
            }
        }
        return n;
    }

    /// <summary>
    /// partial 调用点的**模板路径**：Hugo 的 partial 只在 <c>layouts/_partials/</c>
    /// 与 <c>layouts/partials/</c> 查找，**不会命中原目录下的同名页面模板**。
    /// 若只产出裸名，`partial "404.html"` 会解析到 <c>layouts/404.html</c>
    /// （页面模板自身）→ 自递归至 "Exceeding number of recursive depth limit" 而
    /// 构建失败（hugo-coder 实测）。故统一产出显式 <c>_partials/</c> 前缀路径，
    /// 由 FileTemplateLoader 在 partials 目录（含旧目录回退）内解析。
    /// <para>
    /// **本文件内同名命名模板**要加后缀：Hugo 允许在 <c>_partials/pagination.html</c>
    /// 里写 <c>{{ define "pagination" }}</c>，而提取出的命名模板路径恰好与所在文件相同
    /// ——不加后缀会覆盖外层内容（techdoc 的 pagination.html 实测：外层 nav 与
    /// prev/next 全丢，只剩 define 体；调用与提取两侧必须用同一后缀）
    /// </para>
    /// </summary>
    internal string PartialPathFor(string raw)
    {
        var canonical = CanonicalPartialName(raw);
        if (_selfNamedTemplateExtracted &&
            SelfPartialName is { } self &&
            string.Equals(CanonicalPartialName(self), canonical, StringComparison.OrdinalIgnoreCase))
        {
            canonical += NamedTemplateSelfSuffix;
        }
        return "_partials/" + canonical;
    }

    /// <summary>本文件内同名命名模板提取/调用时的后缀（与 InlinePartialExtractor 一致）</summary>
    internal const string NamedTemplateSelfSuffix = "__named";

    /// <summary>转换一个管道（表达式主体）</summary>
    public ConversionResult ConvertPipeline(Parsing.Pipeline pipeline, IReadOnlyList<string> scope, bool resourceContext = false)
    {
        if (pipeline.Commands.Count == 0)
        {
            return new ConversionResult("", ConversionKind.Equivalent);
        }

        var kind = ConversionKind.Equivalent;
        string? note = null;
        string? acc = null;

        for (var i = 0; i < pipeline.Commands.Count; i++)
        {
            var cmd = pipeline.Commands[i];

            // 管道折叠：`x | or y` / `x | and y` / `x | eq y` 这类**逻辑与比较**
            // 函数必须把左值作为参数合并，不能继续用 | 连接——原因有二：
            //   1) Scriban 的 | 把左值注入**首参**，Go 注入**末参**
            //   2) 我的 and/or/eq 转换产出**中缀表达式**（`&&`/`||`/`==`），
            //      不可作为管道目标
            // 此前直接拼 `x | y`（函数名丢失）→ 非法 Scriban（PaperMod 的
            // head.html 实测：`hugo.IsProduction | or (...) | and (...)`）
            if (acc is not null && cmd.Operands.Count > 0
                && cmd.Operands[0] is Parsing.IdentifierExpr pid
                && pid.Name is "and" or "or" or "eq" or "ne" or "gt" or "ge" or "lt" or "le")
            {
                // 左值 acc 已是**转换后的 Scriban 文本**，不能再走表达式转换
                // （否则 `$comment?.enable` 会被当作标识符二次 nil 安全化，
                //  产出 `$comment??.enable`——LoveIt 实测的语法错误）。
                // 故此处直接按已转换文本参与 and/or 折叠
                var folded = FoldWithLeft(pid.Name, cmd.Operands.Skip(1), acc, scope);
                if (folded.Kind == ConversionKind.Unsupported)
                {
                    return folded;
                }
                if (folded.Kind != ConversionKind.Equivalent)
                {
                    kind = folded.Kind;
                    note = folded.Note;
                }
                acc = folded.Text;
                continue;
            }

            // ---- 管道左值应作**末参**的 Hugo 函数 ----
            // Go/Hugo 的 `X | f A B` 等价 `f A B X`（管道值追加为末参），而 Scriban 的
            // `|` 注入**首参**。对"输入在末位"的函数（Hugo 为了可管道化而把输入声明在
            // 最后一个参数，如 FromString(name, content)、printf(format, args...)），
            // 直接沿用 | 语义会完全错位——实测：PaperMod 的
            // `" " | resources.FromString "assets/css/includes-blank.css"` 产出名为
            // " " 的资源（RelPermalink "/assets/ " → 输出路径 "assets/ " → 构建必失败）。
            // 故对这类函数改写为显式调用形态，把左值放回末位
            if (acc is not null && cmd.Operands.Count > 0
                && cmd.Operands[0] is Parsing.IdentifierExpr pipeLastId
                && PipeValueLastFunctions.Contains(pipeLastId.Name))
            {
                var lastName = _map.MapFunction(pipeLastId.Name);
                var lastArgs = new List<string> { lastName };
                var lastOk = true;
                foreach (var a in cmd.Operands.Skip(1))
                {
                    var rl = ConvertExpr(a, scope, false);
                    if (rl.Kind == ConversionKind.Unsupported)
                    {
                        return rl;
                    }
                    lastArgs.Add(rl.Text);
                }
                if (lastOk)
                {
                    // 左值是多 token 表达式（如已转换的调用文本
                    // `collections.Delimit $x ", "`）时必须加括号，否则与
                    // 前面参数连成一串被 Scriban 当作同一函数的多余实参
                    //（实测：`resources.FromString "a.css" collections.Delimit $x ", "`
                    // → FromString 收到 4 个实参，产出错误资源）
                    lastArgs.Add(NeedsParens(acc) ? "(" + acc + ")" : acc);
                    acc = string.Join(" ", lastArgs);
                    continue;
                }
            }

            // ---- 管道末段是时间格式化方法 `.Receiver.Format`（无显式参数）----
            // Hugo 写法 `EXPR | .PublishDate.Format`：格式串由管道左值提供
            //（`.PublishDate.Format EXPR`）。它在 AST 里是**单个 FieldExpr 操作数**，
            // 不走"字段+参数"分支，裸转字段路径会产出 `page?.publish_date?.format`，
            // Scriban 把管道目标当函数名 → "The function `...?.format` was not found"
            //（LoveIt 实测 3 处）
            if (acc is not null && cmd.Operands.Count == 1
                && cmd.Operands[0] is Parsing.FieldExpr pipeFmt
                && ToSnakePath(pipeFmt.Path).EndsWith(".format", StringComparison.OrdinalIgnoreCase))
            {
                var pipeTailPlain = ToSnakePath(ToSnakePath(pipeFmt.Path)[..^".format".Length]);
                var pipeRecv = scope.Count > 0 &&
                    !pipeTailPlain.StartsWith(".site", StringComparison.OrdinalIgnoreCase)
                    ? scope[^1] + pipeTailPlain
                    : "page" + pipeTailPlain;
                acc = $"date.to_string {pipeRecv} {acc}";
                continue;
            }

            // 管道注入 partial/include：Hugo 的 `X | partial "name"` 等价于
            // `partial "name" X`（管道值**追加为末参**），而 Scriban 的 | 把左值注入
            // **首参**——直接沿用会变成 partial(X, "name")，模板名拿到一个字典
            //（LoveIt home.html 实测："文件名、目录名或卷标语法不正确:
            //  '...\layouts\{Content: "", Ruby: null, ...}'"）。
            // 故此处显式改写为函数调用形态，把左值放回末位。
            // **同一"值在末位"家族**（Hugo v0.166 实测确认参数序）：
            //   replaceRE PATTERN REPLACEMENT INPUT、findRE PATTERN INPUT [LIMIT]、
            //   strings.Trim CUTSET STRING、strings.TrimPrefix PREFIX STRING、
            //   strings.TrimSuffix SUFFIX STRING …
            // 它们都比 Scriban 的管道少一个"值在首位"的假设，直接拼 `|` 会把
            // **页面正文当成模式**：monochrome 的 `$content | replaceRE "<table(.*?)>" …`
            // 实测报 "Invalid pattern '<!-- end-chunk -->…'"（正文被当正则）
            if (acc is not null && cmd.Operands.Count > 0
                && cmd.Operands[0] is Parsing.IdentifierExpr pid2
                && pid2.Name is "partial" or "partialCached" or "include" or "includeCached")
            {
                var callRes = ConvertCommand(cmd, scope, isPipeSegment: false, resourceContext);
                if (callRes.Kind == ConversionKind.Unsupported)
                {
                    return callRes;
                }
                if (callRes.Kind != ConversionKind.Equivalent)
                {
                    kind = callRes.Kind;
                    note = callRes.Note;
                }
                // partialValue 改写仅对 dot 上下文生效（见 ConvertCall）；
                // 此处左值非 dot 时保持 include，行为差异已在 Note 标注。
                // 左值是**多 token 调用**（`dict "Page" . "Preview" true`）时必须加
                // 括号，否则 Scriban 把它解析成多个实参——模板名之后紧跟 `dict`
                // 标识符，上下文参数整体错位（FixIt 的 rss.html / summary.html：
                // partial 内 `$page := .Page` 变成 `$page = page` 取到函数对象，
                // `$page.resources.getmatch` 报 null object）
                acc = callRes.Text + " " + ParenthesizeIfNeeded(acc);
                continue;
            }


            var r = ConvertCommand(cmd, scope, isPipeSegment: i > 0, resourceContext);
            if (r.Kind != ConversionKind.Equivalent)
            {
                kind = r.Kind;
                note = r.Note;
            }
            if (r.Kind == ConversionKind.Unsupported)
            {
                // 任一段不支持 → 整体不支持（避免产出半成品）
                return r;
            }
            acc = acc is null ? r.Text : acc + " | " + r.Text;
        }

        var text = acc ?? "";

        // ---- 静默损坏防线 ----
        var guard = StructuralGuard.Check(text);
        if (guard is not null)
        {
            Diagnostics.Add($"结构检查失败: {guard} | 产出: {text}");
            return new ConversionResult(text, ConversionKind.Unsupported, $"结构检查失败: {guard}");
        }

        return new ConversionResult(text, kind, note);
    }

    /// <summary>转换单个命令</summary>
    private ConversionResult ConvertCommand(Parsing.Command command, IReadOnlyList<string> scope, bool isPipeSegment, bool resourceContext = false)
    {
        var operands = command.Operands;
        if (operands.Count == 0)
        {
            return new ConversionResult("", ConversionKind.Equivalent);
        }

        var first = operands[0];

        // 带参数的函数调用：f a b → 映射后 target arg1 arg2
        // 中缀运算符（eq/ne/gt/ge/lt/le/and/or/not）需特殊处理
        if (first is Parsing.IdentifierExpr id)
        {
            return ConvertCall(id, operands.Skip(1).ToList(), scope, isPipeSegment);
        }

        // ---- 资源上下文内的裸方法（with .Resources.ByType 块内的 .GetMatch）----
        // 这类方法的隐式接收者是资源对象，静态不可知 → 补 resources 前缀
        if (resourceContext && first is Parsing.FieldExpr fctx && operands.Count >= 1)
        {
            var lowerCtx = fctx.Path.ToLowerInvariant();
            if (lowerCtx is ".getmatch" or ".bytype" or ".match" or ".get")
            {
                var argsCtx = new List<string>();
                foreach (var a in operands.Skip(1))
                {
                    var r = ConvertExpr(a, scope, false);
                    if (r.Kind == ConversionKind.Unsupported)
                    {
                        return r;
                    }
                    argsCtx.Add(r.Text);
                }
                var method = lowerCtx[1..];
                return new ConversionResult(
                    $"page.resources.{method} {string.Join(" ", argsCtx)}".Trim(),
                    ConversionKind.Downgraded, "资源上下文裸方法（隐式接收者补 page.resources）");
            }
        }

        // 字段开头 + 后续参数：Hugo 的 method 调用形态
        // （.Resources.ByType "image" → page.resources.bytype "image"）
        if (first is Parsing.FieldExpr fe && operands.Count > 1)
        {
            // .Param "x"：页面参数点路径查询 → paramLookup page "x"
            if (fe.Path is ".Param" or ".param")
            {
                if (operands.Count == 2 && operands[1] is Parsing.LiteralExpr litPm)
                {
                    return new ConversionResult(
                        $"paramLookup page \"{litPm.Unquoted}\"", ConversionKind.Equivalent);
                }
            }

            // .Date.Format / .PublishDate.Format：日期格式化方法
            var feSnake = ToSnakePath(fe.Path);
            if (feSnake.EndsWith(".format", StringComparison.OrdinalIgnoreCase))
            {
                // 接收者根同样受作用域影响：range 内的 `.Date.Format` 应为
                // 循环变量的 date（`$__it0.date`），而非 page.date（实测 bug）。
                // **`.Site.X.Format` 必须换到 site 根**（Stack 的 `.Site.Lastmod.Format`
                // 此前产出 `page.site.lastmod`——链上 page 无 site 成员 → 空对象）
                // 接收者用**普通点**（非 ?.）：这是 date.to_string 的实参，
                // 而 `page?.publish_date?.format` 形态会被 Scriban 当作函数名
                // → "function not found"（LoveIt 实测）
                var dateTailPlain = ToSnakePath(feSnake[..^".format".Length]);
                string dateRecv;
                if (dateTailPlain.StartsWith(".site", StringComparison.OrdinalIgnoreCase))
                {
                    dateRecv = "site" + dateTailPlain[".site".Length..];
                }
                else if (scope.Count > 0)
                {
                    dateRecv = scope[^1] + dateTailPlain;
                }
                else
                {
                    dateRecv = "page" + dateTailPlain;
                }
                var fmtArgsFe = new List<string>();
                foreach (var a in operands.Skip(1))
                {
                    if (a is Parsing.LiteralExpr litFe)
                    {
                        fmtArgsFe.Add("\"" + GoDateFormatConverter.Convert(litFe.Unquoted) + "\"");
                    }
                    else
                    {
                        var rFe = ConvertExpr(a, scope, false);
                        if (rFe.Kind == ConversionKind.Unsupported)
                        {
                            return rFe;
                        }
                        fmtArgsFe.Add(rFe.Text);
                    }
                }
                return new ConversionResult(
                    $"date.to_string {dateRecv} {string.Join(" ", fmtArgsFe)}",
                    ConversionKind.Equivalent);
            }

            // 根切换：`.Site.X` → site.X（其余保持 page + 完整路径）。
            // 不可剥首段——`.Resources.ByType` 的 `resources` 段是资源上下文语义的
            // 一部分（剥掉会产出 `page.bytype`，丢失 resources 层，测试实测）
            var isSiteRoot = fe.Path.StartsWith(".Site", StringComparison.OrdinalIgnoreCase)
                && (fe.Path.Length == ".Site".Length || fe.Path[".Site".Length] == '.');
            // `.Page.X` 与 `.Site.X` 对称：`.Page` 段即"当前页"，须先剥掉再补 page 根，
            // 否则产出 `page.page.x`（LoveIt 的 `.Page.Scratch.Get "params"` 22 处、
            // Console 的 `.Page.Resources` 同因，实测）
            var isPageRoot = fe.Path.StartsWith(".Page", StringComparison.OrdinalIgnoreCase)
                && (fe.Path.Length == ".Page".Length || fe.Path[".Page".Length] == '.');
            // **裸 `.Page`**（无后续段）不能简单映射成 `page`：在"以 dict 调用的 partial"
            // 里 `.` 是调用点传的 dict，`(slice .Page)` 取的是 **dict 的 Page 键**
            // （hugo-book 的 `dict "Scratch" $scratch "Page" .` 递归收集章节页），
            // 映射成 `page` 会把绑定对象自身收进列表 → 元素不是页面对象 → 集合方法族失效
            //（`$pages.Next` 报 function not found）。
            // 双上下文通吃的写法：dict 有 Page 键时取它（引擎把 dict 键并入绑定对象，
            // 大小写别名齐备），否则回落到页面自身（页面上下文的 `.Page` 就是自己）
            if (fe.Path.Equals(".Page", StringComparison.OrdinalIgnoreCase))
            {
                return new ConversionResult("(page.page ?? page)", ConversionKind.Equivalent);
            }
            // 【曾试】把"字段+参数"的接收者也套用作用域变量（scope[^1]）以修 narrow 的
            // 嵌套 range（内层 `.Pages` 属外层分组对象）。全量矩阵判为回归：
            // monochrome 的 single.html 出现 IndexOutOfRange、页数 103→89——
            // 该分支的接收者语义比"无参字段链"复杂（`with`/`range` 之外还有
            // 资源上下文与页面方法链），统一替换会打翻既有正确映射。
            // 结论：**只保留无参字段链的作用域替换**；带参数形态的嵌套作用域
            // 暂不支持（narrow 的 archives.html 内层仍用 page 根，见第三十二节 F）
            var feMapped = isSiteRoot
                ? "site" + ToSnakePath(fe.Path[".Site".Length..])
                : isPageRoot
                    ? "page" + ToSnakePath(fe.Path[".Page".Length..])
                    : "page" + ToSnakePath(fe.Path);
            var mapped = MapChainMethod(feMapped);
            if (mapped is not null)
            {
                var argTexts = new List<string>();
                foreach (var a in operands.Skip(1))
                {
                    var r = ConvertExpr(a, scope, false);
                    if (r.Kind == ConversionKind.Unsupported)
                    {
                        return r;
                    }
                    argTexts.Add(r.Text);
                }

                // .Render "view"：页面方法 → 全局 render "view" <page>
                if (mapped.EndsWith(".render", StringComparison.Ordinal))
                {
                    var recv = mapped[..^".render".Length];
                    return new ConversionResult(
                        ("render " + string.Join(" ", argTexts) + " " + recv).Trim(),
                        ConversionKind.Equivalent);
                }

                // 有参调用不能用 nil 安全分隔符（Scriban 会把 `(x)?.f a` 整体当函数名）
                var feTarget = argTexts.Count > 0 ? UnsafeTarget(mapped) : mapped;
                return new ConversionResult(
                    feTarget + " " + string.Join(" ", argTexts), ConversionKind.Equivalent);
            }
        }

        // 非标识符开头的命令：单操作数（字段/字面量/括号）
        if (operands.Count == 1)
        {
            return ConvertExpr(first, scope, isPipeSegment);
        }

        // **复杂接收者的 `.Format "fmt"`**：转入 date.to_string。
        // 简单接收者的 .Format 由"字段 + 参数"分支处理；接收者是链/括号表达式时
        // 走不到那里，此前被容错拼接成 `<chain>.lastmod.format "fmt"`——
        // 而 `.format` 不是值成员而是转换器概念，Flint 里对应 date.to_string
        //（FixIt 的 RSS：`(index $pages.ByLastmod.Reverse 0).LastMod.Format "Mon, …"`）
        if (operands.Count >= 2
            && operands[0] is Parsing.ChainExpr chainFmt
            && chainFmt.Fields.Count > 0
            // 末字段可能是合并形态（词法把 `.LastMod.Format` 当一个字段）→ 按后缀判定
            && chainFmt.Fields[^1].EndsWith(".Format", StringComparison.OrdinalIgnoreCase)
            && operands[1] is Parsing.LiteralExpr fmtLit)
        {
            // 剥掉末字段里的 `.Format` 后缀（为空则丢弃该字段，否则保留前段作接收者）
            var fmtTail = chainFmt.Fields[^1][..^".Format".Length];
            var recvFields = chainFmt.Fields.Take(chainFmt.Fields.Count - 1).ToList();
            if (fmtTail.Length > 0)
            {
                recvFields.Add(fmtTail);
            }
            var recvExpr = recvFields.Count == 0
                ? (Parsing.Expr)chainFmt.Base
                : new Parsing.ChainExpr(chainFmt.Base, recvFields);
            var recvConv = ConvertExpr(recvExpr, scope, false);
            if (recvConv.Kind != ConversionKind.Unsupported)
            {
                // 格式化串是**字符串字面量**（不能走 ParenthesizeIfNeeded，否则带空格/逗号的
                // Go 格式串会被当表达式加括号）
                return new ConversionResult(
                    $"date.to_string {ParenthesizeIfNeeded(recvConv.Text)} " +
                    "\"" + GoDateFormatConverter.Convert(fmtLit.Unquoted) + "\"",
                    ConversionKind.Equivalent);
            }
        }

        // 多操作数但首个非标识符：Go 里非法形态，容错拼接
        var texts = new List<string>();
        foreach (var o in operands)
        {
            var r = ConvertExpr(o, scope, isPipeSegment);
            if (r.Kind == ConversionKind.Unsupported)
            {
                return r;
            }
            texts.Add(r.Text);
        }
        return new ConversionResult(string.Join(" ", texts), ConversionKind.Equivalent);
    }

    /// <summary>转换函数调用（含中缀运算符特判）</summary>
    private ConversionResult ConvertCall(
        Parsing.IdentifierExpr fn, List<Parsing.Expr> args, IReadOnlyList<string> scope,
        bool isPipeSegment = false)
    {
        var name = fn.Name;

        // ---- 逻辑/比较：前缀 → 中缀 ----
        if (name is "eq" or "ne" or "gt" or "ge" or "lt" or "le")
        {
            if (args.Count < 2)
            {
                Diagnostics.Add($"{name} 参数不足（{args.Count}）");
                return new ConversionResult("", ConversionKind.Unsupported, $"{name} 参数不足");
            }
            var op = name switch
            {
                "eq" => "==", "ne" => "!=", "gt" => ">", "ge" => ">=", "lt" => "<", _ => "<="
            };
            // `>`/`>=`/`<`/`<=` 改产出**宽容比较函数**：Scriban 的运算符在两侧类型
            // 不一致时抛 "Unable to convert type object to int"（map 值为字符串时，
            // Clarity 实测 29 处）；Hugo 的比较会做类型强制
            var cmpFn = name switch
            {
                "gt" => "num_gt", "ge" => "num_ge", "lt" => "num_lt", "le" => "num_le", _ => null
            };
            // Go 的 eq/lt 等支持多参（eq a b c → a==b || a==c），Hugo 模板常用两参
            if ((name is "eq" or "ne") && args.Count > 2)
            {
                var clauses = new List<string>();
                var firstR = ConvertExpr(args[0], scope, false);
                if (firstR.Kind == ConversionKind.Unsupported)
                {
                    return firstR;
                }
                for (var i = 1; i < args.Count; i++)
                {
                    var r = ConvertExpr(args[i], scope, false);
                    if (r.Kind == ConversionKind.Unsupported)
                    {
                        return r;
                    }
                    clauses.Add($"{firstR.Text} {op} {r.Text}");
                }
                var joined = string.Join(op == "==" ? " || " : " && ", clauses);
                return new ConversionResult("(" + joined + ")", ConversionKind.Equivalent);
            }

            var left = ConvertExpr(args[0], scope, false);
            var right = ConvertExpr(args[1], scope, false);
            if (left.Kind == ConversionKind.Unsupported || right.Kind == ConversionKind.Unsupported)
            {
                return left.Kind == ConversionKind.Unsupported ? left : right;
            }
            return cmpFn is null
                ? new ConversionResult($"({left.Text} {op} {right.Text})", ConversionKind.Equivalent)
                : new ConversionResult(
                    $"{cmpFn} {ParenthesizeIfNeeded(left.Text)} {ParenthesizeIfNeeded(right.Text)}"
                        .Trim(),
                    ConversionKind.Equivalent);
        }

        if (name is "and" or "or")
        {
            // Go 的 and/or 是**惰性**短路，Scriban 的 &&/|| 是急切求值——
            // 直接映射会打掉"靠短路保护 nil"的写法（even 的 section.html：
            // `{{ if or (eq $index 0) (ne ($lastElement.Date.Format "2006") $thisYear) }}`
            // 首轮 index 取到 -1 → nil 的 `.Date` 在急切求值下抛错）。
            // 用**分支惰性**的三元表达，见 LazyLogical
            var parts = new List<string>();
            foreach (var a in args)
            {
                var r = ConvertExpr(a, scope, false);
                if (r.Kind == ConversionKind.Unsupported)
                {
                    return r;
                }
                parts.Add(r.Text);
            }
            return new ConversionResult(LazyLogical(name, parts), ConversionKind.Equivalent);
        }

        if (name == "not")
        {
            if (args.Count != 1)
            {
                Diagnostics.Add($"not 参数数异常（{args.Count}）");
                return new ConversionResult("", ConversionKind.Unsupported, "not 参数数异常");
            }
            var r = ConvertExpr(args[0], scope, false);
            if (r.Kind == ConversionKind.Unsupported)
            {
                return r;
            }
            return new ConversionResult($"!({r.Text})", ConversionKind.Equivalent);
        }

        // ---- arg 语义特判：dict / slice / partial ----
        // default 的**非管道**形态 `default DEF GIVEN` 需要重排为 Scriban 期望的
        // 参数顺序 (GIVEN, DEF)：Flint 的 default 按 Scriban 语义取首参（管道注入位），
        // 而 Hugo 的 default 是 `default DEFAULT GIVEN`（默认值在前）。
        // 管道形态 `X | default DEF` 已是 (X, DEF)，无需重排
        if (name is "default" or "compare.Default" && !isPipeSegment && args.Count == 2)
        {
            var defR = ConvertExpr(args[0], scope, false);
            var givenR = ConvertExpr(args[1], scope, false);
            if (defR.Kind == ConversionKind.Unsupported || givenR.Kind == ConversionKind.Unsupported)
            {
                return defR.Kind == ConversionKind.Unsupported ? defR : givenR;
            }
            return new ConversionResult(
                $"default {givenR.Text} {defR.Text}", ConversionKind.Equivalent);
        }

        if (name is "dict" or "collections.Dictionary")
        {
            return ConvertDict(args, scope);
        }
        if (name is "slice" or "collections.Slice")
        {
            var items = new List<string>();
            foreach (var a in args)
            {
                var r = ConvertExpr(a, scope, false);
                if (r.Kind == ConversionKind.Unsupported)
                {
                    return r;
                }
                items.Add(r.Text);
            }
            return new ConversionResult("[" + string.Join(", ", items) + "]", ConversionKind.Equivalent);
        }
        if (name is "partial" or "partialCached" or "partials.Include" or "partials.IncludeCached")
        {
            return ConvertPartial(name, args, scope);
        }
        // time.Format / dateFormat（管道段）：Scriban 管道把左值注入首参，
        // 故只传格式串；Flint 的 date.to_string 签名是 (date, format)
        if (name is "time.Format" or "time.format" or "dateFormat" or "dateformat")
        {
            var fmtArgsPipe = new List<string>();
            foreach (var a in args)
            {
                if (a is Parsing.LiteralExpr litP)
                {
                    fmtArgsPipe.Add("\"" + GoDateFormatConverter.Convert(litP.Unquoted) + "\"");
                }
                else
                {
                    var rP = ConvertExpr(a, scope, false);
                    if (rP.Kind == ConversionKind.Unsupported)
                    {
                        return rP;
                    }
                    fmtArgsPipe.Add(rP.Text);
                }
            }
            return new ConversionResult(
                "date.to_string " + string.Join(" ", fmtArgsPipe), ConversionKind.Equivalent);
        }

        // .Format / .Date.Format：Go 时间格式化方法 → Flint 的 date.to_string
        if (name.EndsWith(".Format", StringComparison.OrdinalIgnoreCase))
        {
            var recv = name[..^".Format".Length];
            // 接收者也要过页面根映射：`$.PublishDate.Format` 的接收者是 $.PublishDate，
            // 原样输出会让 Scriban 把 `$` 当未定义符号（报 "Cannot get the member
            // $.PublishDate for a null object"，FixIt 的 posts/single.html 实测 3 处）
            var recvText = recv.Length == 0 || recv == "$" || recv == "."
                ? "page"
                : MapIdentifierPath(recv) ?? recv;
            var fmtArgs = new List<string>();
            foreach (var a in args)
            {
                if (a is Parsing.LiteralExpr lit)
                {
                    fmtArgs.Add(GoDateFormatConverter.Convert(lit.Unquoted));
                }
                else
                {
                    var r = ConvertExpr(a, scope, false);
                    if (r.Kind == ConversionKind.Unsupported)
                    {
                        return r;
                    }
                    fmtArgs.Add(r.Text);
                }
            }
            return new ConversionResult(
                $"date.to_string {recvText} {string.Join(" ", fmtArgs)}", ConversionKind.Equivalent);
        }

        // fmt.Printf：Flint 的 printf 不支持反引号原始串与 Go 动词，
        // 降级为 string.format 风格（保留参数，格式串原样）
        if (name is "fmt.Printf" or "printf")
        {
            var printfArgs = new List<string>();
            foreach (var a in args)
            {
                var r = ConvertExpr0(a, scope);
                if (r is null)
                {
                    return new ConversionResult("", ConversionKind.Unsupported, "printf 参数无法解析");
                }
                printfArgs.Add(r);
            }
            return new ConversionResult(
                "printf " + string.Join(" ", printfArgs), ConversionKind.Downgraded,
                "printf 格式串按 Go 语义传入（Flint 用 .NET 格式化，差异已标注）");
        }

        if (name is "return")
        {
            // Hugo 的 return：立即终止 partial 渲染并返回**任意类型**的值
            // （资源对象/字典等）。Scriban 的 include 只能文本化，故用 Store 作
            // 对象通道——本文件是 partial 时改写为：
            //   {{ return X }} → {{ page.store.set "__partial_ret_<self>" X }}{{ ret }}
            // 调用方的 partialValue 渲染后从 Store 取回真实对象（实测：跨 include 保真）
            if (args.Count == 0)
            {
                return new ConversionResult("ret", ConversionKind.Equivalent);
            }

            var r = ConvertExpr(args[0], scope, false);
            if (r.Kind == ConversionKind.Unsupported)
            {
                return r;
            }

            // 多参调用的括号包裹在 TemplateConverter 的 return 分支完成
            //（此处保持原文本，避免与之重复）
            if (SelfPartialName is not null)
            {
                var key = RetKeyPrefix + SelfPartialName;
                return new ConversionResult(
                    $"__partial_ret_set \"{key}\" {r.Text} }}}}}}{{{{ ret",
                    ConversionKind.Equivalent);
            }

            return new ConversionResult("ret " + r.Text, ConversionKind.Equivalent);
        }

        // ---- 菜单归属判断（Hugo 页面方法）----
        // `$.IsMenuCurrent "main" ENTRY` / `$.HasMenuCurrent "main" ENTRY`：
        // 也可写成局部变量接收者（`$currentPage.HasMenuCurrent "main" .`，Stack 实测）。
        // Flint 注册为全局函数 is_menu_current/has_menu_current（引擎按菜单项的
        // is_active 判定，该标志在菜单构建期已按当前页路径计算）。不加此映射时
        // 函数名原样输出 → "The function `$.IsMenuCurrent` was not found"
        if (name is "$.IsMenuCurrent" or "$.HasMenuCurrent" or ".IsMenuCurrent" or ".HasMenuCurrent"
            or "IsMenuCurrent" or "HasMenuCurrent"
            || name.EndsWith(".IsMenuCurrent", StringComparison.Ordinal)
            || name.EndsWith(".HasMenuCurrent", StringComparison.Ordinal))
        {
            var menuFn = name.EndsWith("HasMenuCurrent", StringComparison.Ordinal)
                ? "has_menu_current" : "is_menu_current";
            var menuArgs = new List<string>();
            foreach (var a in args)
            {
                var r = ConvertExpr(a, scope, false);
                if (r.Kind == ConversionKind.Unsupported)
                {
                    return r;
                }
                menuArgs.Add(r.Text);
            }
            return new ConversionResult(
                menuArgs.Count == 0 ? menuFn : menuFn + " " + string.Join(" ", menuArgs),
                ConversionKind.Equivalent);
        }

        // ---- 链式方法调用（X.Method args）----
        // Hugo 的 .Resources.ByType / .Resources.GetMatch 等是 method 调用，
        // Flint 侧注册为 bytype/getmatch（Scriban 成员名不区分大小写，但
        // 转换期产出须与引擎注册名一致）
        if (name.Contains('.', StringComparison.Ordinal) || name.Contains('$', StringComparison.Ordinal))
        {
            var mapped = MapChainMethod(name);
            if (mapped is not null)
            {
                // 无参的链式形态本质是**字段访问**（`$posts.paginate`），不是方法调用：
                // 变量为 nil 时 Hugo 返回 nil，Scriban 抛 "Cannot get the member"。
                // 改用 nil 安全分隔符（`$posts?.paginate`）；有参时才是方法调用，
                // 必须用普通点（`page?.get_terms` 会被 Scriban 当函数名）
                if (args.Count == 0)
                {
                    var lastDotIdx = mapped.LastIndexOf('.');
                    if (lastDotIdx > 0)
                    {
                        var recvPart = mapped[..lastDotIdx];
                        var isVarRecv = recvPart.Length > 1 && recvPart[0] == '$' && recvPart[1] != '.';
                        if (isVarRecv || recvPart == "$" || recvPart.StartsWith('('))
                        {
                            return new ConversionResult(
                                recvPart + "?." + mapped[(lastDotIdx + 1)..], ConversionKind.Equivalent);
                        }
                    }
                    return new ConversionResult(mapped, ConversionKind.Equivalent);
                }

                var argTexts0 = new List<string>();
                foreach (var a in args)
                {
                    var r0 = ConvertExpr(a, scope, false);
                    if (r0.Kind == ConversionKind.Unsupported)
                    {
                        return r0;
                    }
                    argTexts0.Add(r0.Text);
                }

                // .Render "view" 是页面方法，Flint 的 render 是全局函数
                // （签名 render "view" <page>）→ 追加页面接收者
                if (mapped.EndsWith(".render", StringComparison.Ordinal))
                {
                    var recv = mapped[..^".render".Length];
                    var callR = "render " + string.Join(" ", argTexts0) + " " + recv;
                    return new ConversionResult(callR.Trim(), ConversionKind.Equivalent);
                }

                // **有参调用不能用 nil 安全分隔符**：Scriban 会把 `(x)?.f a` 整体当作
                // 函数名（"The function `(x)?.f` was not found"）。MapChainMethod 为
                // 接收者是括号/变量的**字段访问**（无参）加了 `?.`，调用形态必须退回普通点
                //（yinyang 的 `(where …).GroupByDate "2006"` 实测 22 处）
                var callTarget = argTexts0.Count > 0 ? UnsafeTarget(mapped) : mapped;
                var call0 = argTexts0.Count == 0 ? mapped : callTarget + " " + string.Join(" ", argTexts0);
                return new ConversionResult(call0, ConversionKind.Equivalent);
            }
        }


        // ---- $.Param / .Param 特判 ----
        // Hugo 的 $.Param "x" 是页面参数点路径查询，Flint 无 $.param 成员
        // （Scriban 的 $ 是函数参数数组）→ 映射为 paramLookup page "x"
        if (name is "$.Param" or "$.param" or ".Param" or "page.param" or "$.param")
        {
            if (args.Count == 1 && args[0] is Parsing.LiteralExpr lit)
            {
                return new ConversionResult(
                    $"paramLookup page \"{lit.Unquoted}\"", ConversionKind.Equivalent);
            }
            Diagnostics.Add("$.Param 参数非字面量");
            return new ConversionResult("", ConversionKind.Unsupported, "$.Param 参数非字面量");
        }

        // ---- $.Param / .Param 特判 ----
        // Hugo 的 $.Param "x" 是页面参数点路径查询，Flint 无 $.param 成员
        // （Scriban 的 $ 是函数参数数组）→ 映射为 paramLookup page "x"
        if (name is "$.Param" or "$.param" or ".Param" or "page.param" or "$.param")
        {
            if (args.Count == 1 && args[0] is Parsing.LiteralExpr lit)
            {
                return new ConversionResult(
                    $"paramLookup page \"{lit.Unquoted}\"", ConversionKind.Equivalent);
            }
            Diagnostics.Add("$.Param 参数非字面量");
            return new ConversionResult("", ConversionKind.Unsupported, "$.Param 参数非字面量");
        }

        // ---- 无参的点路径标识符（site.Params.x / page.Params.y）----
        // 解析器把 `site.Params.ananke.home` 合并为一个 IdentifierExpr，
        // 此处按点路径规则映射（无参数才走此分支，避免与函数调用混淆）
        if (args.Count == 0 && name.Contains('.', StringComparison.Ordinal))
        {
            // 局部变量的成员访问（$bc.RelPermalink / $params.subtitle / $pag.Pagers）：
            // 用 nil 安全 `?.`——Hugo 的 `$x.y` 在 $x 为 nil 时返回 nil（宽容），
            // Scriban 抛 "Cannot get the member ... for a null object"。
            // 矩阵验证中多主题受阻于此（PaperMod/LoveIt/Stack 同名模式）
            if (name.StartsWith('$') && !name.StartsWith("$.", StringComparison.Ordinal))
            {
                var segs = name.Split('.');
                var chain = new System.Text.StringBuilder(segs[0]);
                for (var si = 1; si < segs.Length; si++)
                {
                    var seg = ToSnakePath("." + segs[si]).TrimStart('.');
                    chain.Append("?.").Append(seg.Length == 0 ? segs[si] : seg);
                }
                return new ConversionResult(chain.ToString(), ConversionKind.Equivalent);
            }

            var mappedPath = MapIdentifierPath(name);
            if (mappedPath is not null)
            {
                return new ConversionResult(mappedPath, ConversionKind.Equivalent);
            }
        }

        // ---- 常规函数调用 ----
        var target = _map.MapFunction(name);
        var argTexts = new List<string>();
        foreach (var a in args)
        {
            var r = ConvertExpr(a, scope, false);
            if (r.Kind == ConversionKind.Unsupported)
            {
                return r;
            }
            argTexts.Add(r.Text);
        }

        var call = argTexts.Count == 0 ? target : target + " " + string.Join(" ", argTexts);
        return new ConversionResult(call, ConversionKind.Equivalent);
    }

    /// <summary>
    /// 链式方法名映射：把 Hugo/Go 形态的点路径方法名映射为 Flint 注册名。
    /// 未命中返回 null（交回常规处理）
    /// </summary>
    private static string? MapChainMethod(string name)
    {
        // 末尾方法段（.ByType → bytype）
        var lastDot = name.LastIndexOf('.');
        if (lastDot < 0 || lastDot == name.Length - 1)
        {
            return null;
        }

        var head = name[..lastDot];
        var method = name[(lastDot + 1)..];

        // 方法名规范化：可能已被 ToSnakePath 变成 by_type，转回 ByType 再查表
        var canonical = string.Concat(method.Split('_')
            .Where(p => p.Length > 0)
            .Select(p => char.ToUpperInvariant(p[0]) + p[1..]));

        var flintMethod = canonical switch
        {
            "ByType" => "bytype",
            "GetMatch" => "getmatch",
            "ByDate" => "bydate",
            "ByTitle" => "bytitle",
            "ByWeight" => "byweight",
            "ByLength" => "bylength",
            "ByLastmod" => "bylastmod",
            "ByParam" => "byparam",
            "GroupBy" => "groupby",
            "GroupByDate" => "groupbydate",
            "IndexOf" => "indexof",
            "Get" => "get",
            // Hugo 的页面方法（供"字段+参数"分支产出合法的 `page.get_page "x"`）
            "GetPage" => "get_page",
            "GetTerms" => "get_terms",
            // Blowfish 等主题用 `$.Page.HasShortcode "x"` / `.Page.Param` 形态
            "HasShortcode" => "has_shortcode",
            "Param" => "param",
            "Paginate" => "paginate",
            "RenderString" => "render_string",
            "RenderShortcodes" => "render_shortcodes",
            "Reverse" => "reverse",
            "Limit" => "limit",
            "Related" => "related",
            "Render" => "render",
            "First" => "first",
            "Last" => "last",
            "Uniq" => "uniq",
            "Sort" => "sort",
            "Where" => "where",
            // .Scratch/.Store 方法（Hugo 文档写 PascalCase，主题照抄：
            // `$.Scratch.Set "x" false`）。引擎已注册 Pascal/snake 双别名，
            // 但 `$.Scratch.Set` 作为函数名须先映射到 `page.store.set`，
            // 否则原样输出 → "Cannot get the member $.Scratch.Set for a null object"
            //（LoveIt paginator.html、PaperMod post_meta.html 实测）
            "Set" => "set",
            "Add" => "add",
            "Delete" => "delete",
            "SetInMap" => "setinmap",
            "DeleteInMap" => "deleteinmap",
            "GetSortedMapValues" => "getsortedmapvalues",
            // .Scratch.Values / .Scratch.Get（Get 已在上面）
            "Values" => "values",
            _ => (string?)null
        };

        if (flintMethod is null)
        {
            return null;
        }

        // head 归一：Hugo 的 $ 指页面上下文（Scriban 的 $ 是函数参数数组，
        // 语义完全不同），故 $.X → page.x，$.Site.X → site.x。
        // **例外**：`$name`（非 `$.`）是局部变量，是函数的接收者本身——
        // 若也按"页面根"改写，`$scratch.Add` 会变成 `pagescratch.add`
        //（凭空多出 page 前缀，变量丢失 → 空对象）
        var headText = head switch
        {
            // `$.Page` 与裸 `$` 都指当前页；`$.Site` 指站点
            "$.Site" or "site" => "site",
            "$.Page" or "$" or "." => "page",
            "page" or "Page" => GlobalPageRoot,
            "$.Site.RegularPages" => "site.regular_pages",
            "$.Site.Pages" => "site.pages",
            _ when head.StartsWith("$.Site.", StringComparison.Ordinal) =>
                // 切片长度须含 `$.` 前缀（6 字符）：head[".Site".Length..] 少算一位，
                // `$.Site.Store.Set` 会产出 `sitee.store.set`（FixIt 的 base/paginator.html 实测）
                "site" + ToSnakePathNilSafe(head["$.Site".Length..]),
            // `$.Page.GetTerms` / `$.GetTerms`：`$.X` 的 X 是页面成员，须剥掉
            // `$.` 前缀再映射（此前 head[1..] 产出 `.GetTerms` → page.get_terms 正常，
            // 但 `$.Page.X` 形态会产出 `page.page.x`——此处统一走 Page 剥除）
            _ when head.StartsWith("$.Page.", StringComparison.Ordinal) =>
                "page" + ToSnakePathNilSafe(head["$.Page".Length..]),
            _ when head.StartsWith("$.", StringComparison.Ordinal) ||
                   head.StartsWith('.') =>
                "page" + ToSnakePathNilSafe(head.StartsWith('.') ? head : head[1..]),
            _ => head
        };

        // 局部变量接收者（`$scratch.Add` / `$pag.Pagers`）：head 就是变量名本身，
        // 不能再加 page 前缀。**裸 `$` 与 `$.` 除外**——那是 Hugo 的页面上下文
        // （`$.GetTerms` → page.get_terms，正是 head 分支已给出的结果）
        if (head.Length > 1 && head[0] == '$' && head[1] != '.')
        {
            // 变量之后的字段段同样要映射：`$page.Resources.GetMatch` 若原样保留
            // 接收者，产出 `$page.Resources.getmatch`，而页面对象上的键是小写
            // `resources` → "Cannot get the member $page.Resources.getmatch for a
            // null object"（FixIt get-cover.html 实测）
            var dot = head.IndexOf('.');
            headText = dot < 0 ? head : head[..dot] + ToSnakePath(head[dot..]);
        }

        return headText + "." + flintMethod;
    }

    /// <summary>
    /// 拼接点路径段：末尾段若命中已知方法名，用规范化后的 Flint 名
    /// （GetMatch → getmatch 而非 get_match，须与引擎注册名一致）
    /// </summary>
    private static string JoinPathSegments(IEnumerable<string> segments, bool nilSafe = false)
    {
        var list = segments.ToList();
        if (list.Count == 0)
        {
            return "";
        }

        var sep = nilSafe ? "?." : ".";
        var sb2 = new System.Text.StringBuilder();
        for (var i = 0; i < list.Count; i++)
        {
            var last = list[i];
            var mapped = MapChainMethod("x." + last);
            var seg2 = mapped is not null ? mapped["x.".Length..] : Seg(last);
            sb2.Append(sep).Append(seg2.Length == 0 ? last : seg2);
        }
        return sb2.ToString();
    }

    /// <summary>表达式转文本的容错版：失败返回 null（供 printf 等参数收集用）</summary>
    private string? ConvertExpr0(Parsing.Expr expr, IReadOnlyList<string> scope)
    {
        var r = ConvertExpr(expr, scope, false);
        return r.Kind == ConversionKind.Unsupported ? null : r.Text;
    }

    /// <summary>
    /// 管道折叠的 and/or/比较：左值是**已转换文本**（不可再转换），
    /// 右侧参数正常转换，产出中缀表达式
    /// </summary>
    private ConversionResult FoldWithLeft(
        string fn, IEnumerable<Parsing.Expr> rightArgs, string left, IReadOnlyList<string> scope)
    {
        var texts = new List<string> { left };
        foreach (var a in rightArgs)
        {
            var r = ConvertExpr(a, scope, false);
            if (r.Kind == ConversionKind.Unsupported)
            {
                return r;
            }
            texts.Add(r.Text);
        }

        if (fn is "and" or "or")
        {
            // 与 ConvertCall 同口径：惰性短路（Go）↔ 分支惰性三元（Scriban）
            return new ConversionResult(LazyLogical(fn, texts), ConversionKind.Equivalent);
        }

        var op = fn switch
        {
            "eq" => "==", "ne" => "!=", "gt" => ">", "ge" => ">=", "lt" => "<", "le" => "<=",
            _ => "&&"
        };

        // Go 语义：`x | or y` 等价 `or y x`（管道值作**末参**）——
        // 参数序对比较函数无影响，对 and/or 也无影响（可结合）
        return new ConversionResult("(" + string.Join($" {op} ", texts) + ")", ConversionKind.Equivalent);
    }

    /// <summary>
    /// 点路径的 nil 安全化：`.A.B.C` → `?.a?.b?.c`（供拼接根后形成
    /// `page?.a?.b?.c`）。用于让缺失的中间层返回 null 而非抛异常（对齐 Hugo）
    /// </summary>
    private static string NilSafePath(string dotted)
    {
        var segs = dotted.TrimStart('.').Split('.', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(segs.Select(s2 =>
        {
            var snake = ToSnakePath("." + s2).TrimStart('.');
            return "?." + (snake.Length == 0 ? s2 : snake);
        }));
    }

    /// <summary>
    /// 点路径标识符映射（site.Params.x → site.params.x；page.Params.y → page.params.y）。
    /// 返回 null 表示不是可识别的路径根（交回常规处理）
    /// </summary>
    private static string? MapIdentifierPath(string name)
    {
        // 数据路径（hugo.Data.x / Site.Data.x）：段名不 snake 化，见 TryMapDataPath
        if (TryMapDataPath(name) is { } dataPath)
        {
            return dataPath;
        }

        var segs = name.Split('.');
        if (segs.Length < 2)
        {
            return null;
        }

        var root = segs[0];

        // $ 根：Hugo 的 $ 指页面上下文；$.Site.* 指站点。
        // 段连接用 nil 安全 `?.`：Hugo 对缺失中间层返回 nil（宽容），
        // Scriban 的普通点链遇 null 抛异常（矩阵验证跨主题高频阻断）
        if (root == "$")
        {
            if (segs.Length >= 2 && (segs[1] == "Site" || segs[1] == "site"))
            {
                var siteRest = JoinPathSegments(segs.Skip(2), nilSafe: true);
                return siteRest.Length == 0 ? "site" : "site" + siteRest;
            }
            // `$.Page.X`：Page 段即"当前页"，剥掉后补 page 根
            //（否则产出 `page?.Page?.X`；Blowfish 的 `$.Page.HasShortcode` 1574 处实测）
            if (segs.Length >= 2 && (segs[1] == "Page" || segs[1] == "page"))
            {
                var afterPage = JoinPathSegments(segs.Skip(2), nilSafe: true);
                return afterPage.Length == 0 ? "page" : "page" + afterPage;
            }
            var pageRest = JoinPathSegments(segs.Skip(1), nilSafe: true);
            return pageRest.Length == 0 ? "page" : "page" + pageRest;
        }

        var restSafe = JoinPathSegments(segs.Skip(1), nilSafe: true);
        return root switch
        {
            "site" or "Site" => "site" + restSafe,
            // 源码写的 `page.X` 是 Hugo 的**全局当前页**（不是 dot）：partial 以 dict
            // 调用时 `.` 是 dict 而 `page` 仍是当前页（Hugo v0.166 实测）。
            // Flint 用 `page` 承担 dot，故源码侧 page 走独立全局 `__page`
            "page" or "Page" => GlobalPageRoot + restSafe,
            _ => null
        };
    }

    /// <summary>dict k1 v1 k2 v2 → { k1: v1, k2: v2 }</summary>
    /// <summary>
    /// Go 的 <c>and</c>/<c>or</c> 是**惰性**短路（Go 1.18 起 and/or 短路），
    /// 而 Scriban 的 <c>&amp;&amp;</c>/<c>||</c> 是**急切**求值——直接映射会打掉
    /// "靠短路保护 nil"的写法（even 的 <c>section.html</c>：
    /// <c>{{ if or (eq $index 0) (ne ($lastElement.Date.Format "2006") $thisYear) }}</c>，
    /// 首轮 <c>index $pages -1</c> 为 nil，急切求值下 <c>.Date</c> 抛
    /// "Cannot get the member … for a null object"）。
    /// 用 Scriban 的**三元**表达（探针确认其分支是惰性的）：
    /// <c>or A B …</c> → <c>(A) ? true : ((B) ? true : false)</c>；
    /// <c>and A B …</c> → <c>(A) ? ((B) ? true : false) : false</c>。
    /// 条件语境只看真值，故取布尔（Go 的"首个真值/末值"取值差异只在赋值语境可见，
    /// 实测主题里没有这种用法）
    /// </summary>
    private static string LazyLogical(string fn, IReadOnlyList<string> parts)
    {
        var acc = fn == "and" ? "true" : "false";
        for (var i = parts.Count - 1; i >= 0; i--)
        {
            acc = fn == "and"
                ? $"({parts[i]}) ? ({acc}) : false"
                : $"({parts[i]}) ? true : ({acc})";
        }
        return acc;
    }

    private ConversionResult ConvertDict(
        List<Parsing.Expr> args, IReadOnlyList<string> scope)
    {
        // 管道段里的 `dict` 由 PipeValueLastFunctions 改写为"键在前、值（管道左值）
        // 在末位"的显式调用，故这里仍是"奇数即不支持"（顺序错位比报错更危险）
        if (args.Count % 2 != 0)
        {
            Diagnostics.Add($"dict 参数数为奇数（{args.Count}）");
            return new ConversionResult("", ConversionKind.Unsupported, "dict 参数数为奇数");
        }

        var pairs = new List<string>();
        for (var i = 0; i < args.Count; i += 2)
        {
            var keyExpr = args[i];
            // 键可以是**任意表达式**：字面量加引号，标识符/变量/表达式按原样产出。
            // 此前只接受字面量与标识符，`dict $newKey $newValue`（变量键）被判 Unsupported
            // 并**静默产出空串**——FixIt 的 camel-case-keys.html 因此把
            // `$output = merge $output (dict $newKey $newValue)` 转成 `$output = ""`，
            // 返回值通道发空、下游 `index $output 0` 越界
            //（"Index was outside the bounds of the array"，fixit/stack 同源）。
            // Scriban 的 dict 接受任意表达式作键（实测 `dict $k $v` → {"myKey":7}）
            string keyText;
            switch (keyExpr)
            {
                case Parsing.LiteralExpr lit:
                    // 键一律用引号：主题用 "a=1" / "data-src" 这类非标识符键（裸写会报
                    // "Unexpected token `-` Expecting a colon"），统一引号形态也就不必区分
                    keyText = "\"" + lit.Unquoted.Replace("\\", "\\\\", StringComparison.Ordinal)
                        .Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
                    break;
                case Parsing.IdentifierExpr idExpr:
                    keyText = idExpr.Name;
                    break;
                default:
                    var k = ConvertExpr(keyExpr, scope, false);
                    if (k.Kind == ConversionKind.Unsupported)
                    {
                        return k;
                    }
                    keyText = k.Text;
                    break;
            }


            var v = ConvertExpr(args[i + 1], scope, false);
            if (v.Kind == ConversionKind.Unsupported)
            {
                return v;
            }
            pairs.Add(keyText);
            pairs.Add(v.Text);
        }

        // 一律走 dict 函数：Scriban **没有对象字面量**，`{ k: v }` 会被当成语句块
        // （作为函数参数时 jsonify 收到 0 参 → "Argument index must be < 1"，
        //  也产出不了对象；Congo 的 schema.html、FixIt 的 taxonomy.html 实测）
        return new ConversionResult("dict " + string.Join(" ", pairs), ConversionKind.Equivalent);
    }

    /// <summary>
    /// partial "x" ctx → include "x"（Scriban 的 include 共享调用者上下文，
    /// 故 Hugo 的第二参数 dot 无需显式传递；带 dict 参数的降级记录）
    /// </summary>
    private ConversionResult ConvertPartial(string name, List<Parsing.Expr> args, IReadOnlyList<string> scope)
    {
        if (args.Count == 0)
        {
            return new ConversionResult("", ConversionKind.Unsupported, "partial 缺名称");
        }

        var nameExpr = args[0] switch
        {
            Parsing.LiteralExpr lit => lit.Unquoted,
            _ => null
        };
        if (nameExpr is null)
        {
            // **动态 partial 名**（`partial $partial .`）：Flint 的 partial 在运行期解析
            // 字符串名，故直接把名字表达式透传，不必在迁移期定名。
            // 此前产 TODO 占位 → 命中分支只有注释、正文为空（Congo 的 index.html
            // `templates.Exists` → `partial $partial .` 实测：整站首页空壳）
            var dynName = ConvertExpr(args[0], scope, false);
            if (dynName.Kind != ConversionKind.Unsupported && dynName.Text.Length > 0)
            {
                var dynCtxText = "";
                if (args.Count > 1)
                {
                    var ctxResult = ConvertExpr(args[1], scope, false);
                    if (ctxResult.Kind != ConversionKind.Unsupported)
                    {
                        dynCtxText = ctxResult.Text;
                    }
                }
                var call = dynCtxText.Length > 0
                    ? $"partial {ParenthesizeIfNeeded(dynName.Text)} {ParenthesizeIfNeeded(dynCtxText)}"
                    : $"partial {ParenthesizeIfNeeded(dynName.Text)}";
                Diagnostics.Add($"动态 partial 名（{dynName.Text}）：运行期解析");
                return new ConversionResult(call, ConversionKind.Downgraded, "partial 名为动态表达式");
            }
            return new ConversionResult("", ConversionKind.Unsupported, "partial 名称非字面量");
        }

        var isCached = name is "partialCached" or "partials.IncludeCached";
        // 目标函数用 Flint 自己的 partial（不是 Scriban 内置 include）：
        // 内置 include 在模板缺失时**硬抛**（"Unexpected exception while creating
        // template from path …"），而主题常引用由 Hugo Module 提供的 partial
        //（FixIt 的 `_funcs/get-page-images` 由 LoveIt 模块提供，独立克隆时不存在）
        // ——Flint 的 partial 缺模板时输出空并记录诊断，不打断整站
        var target = isCached ? "partialcached" : "partial";

        // 返回值型 partial → partialValue（Hugo 返回对象 vs Scriban 文本化）。
        // 上下文参数一并传出：partialValue 支持 context 参数（引擎侧同签名），
        // 不传会让 partial 内的 `.` 落到外层 page——FixIt 的
        // `partial "function/camel-case-keys.html" $value` 因此把页面对象当输入
        var canonical = CanonicalPartialName(nameExpr);
        var isValueReturning = _valueReturning is not null && _valueReturning.Contains(canonical);
        // 值返回型 partial 一律走 partialValue（**含 partialCached**）：Hugo 的
        // partialCached 同样支持 `{{ return ... }}` 返回对象，而 Flint 的
        // partialcached 走文本通道 → 调用点拿到字符串后 `.Next`/`.Prev` 等集合方法
        // 报 "The function `$pages.Next` was not found"（hugo-book 的
        // _partials/docs/prev-next.html 实测）。代价：值返回型丢失缓存（仅性能）
        if (isValueReturning)
        {
            var isDotValueCtx = args.Count <= 1 || args[1] is Parsing.DotExpr
                or Parsing.FieldExpr { Path: "." };
            if (isDotValueCtx)
            {
                return new ConversionResult(
                    $"partialValue \"{PartialPathFor(nameExpr)}\"", ConversionKind.Equivalent);
            }
            var ctxValue = ConvertExpr(args[1], scope, false);
            if (ctxValue.Kind != ConversionKind.Unsupported &&
                ctxValue.Text.Length > 0 && ctxValue.Text != "null")
            {
                return new ConversionResult(
                    $"partialValue \"{PartialPathFor(nameExpr)}\" {ctxValue.Text}",
                    ConversionKind.Downgraded,
                    "返回值型 partial 的上下文参数以 page 绑定（Hugo dot 语义等价）");
            }
            Diagnostics.Add($"返回值型 partial {nameExpr} 的上下文参数无法转换，保持 include（语义可能不等价）");
        }

        // 第二参数是 dot：**with/range 块内 dot 不等于 page**（它是块上下文变量），
        // 须显式传出，否则 partial 内的 `.` 会错指外层 page
        //（Stack 的 `{{ with $icon }}{{ partial "helper/icon" . }}{{ end }}` 实测：
        // 不传时 icon.html 把整个 page 对象当图标名 → "icon '%s.svg' is not found"）。
        // 块外 dot 就是 page，保持 include（Scriban 共享上下文天然满足）。
        // 引擎侧对含 page.store 的 partial 有兜底：覆盖 page 时自动合并/退回共享上下文，
        // 故此处可以放心传出块变量
        var isDotCtx = args.Count <= 1 || args[1] is Parsing.DotExpr
            or Parsing.FieldExpr { Path: "." };
        if (isDotCtx)
        {
            if (args.Count > 1 && scope.Count > 0 && !isValueReturning)
            {
                return new ConversionResult(
                    $"partial \"{PartialPathFor(nameExpr)}\" {scope[^1]}", ConversionKind.Downgraded,
                    "partial 上下文参数以 page 绑定（with/range 块内 dot 语义等价）");
            }
            return new ConversionResult(
                $"{target} \"{PartialPathFor(nameExpr)}\"", ConversionKind.Equivalent);
        }

        // 第二参数是括号表达式（dict/slice 等，Hugo 最常用的上下文形态）：
        // 同样交给 `partial NAME CONTEXT` —— 上下文内容成为 partial 内的 `.`。
        // 此前直接丢弃，使被调 partial 的 `.X` 全部落到**外层** page
        //（Stack 的 `partial "widget/taxonomy" (dict "Context" . "Params" ...)`
        // → 内层 `default .Params.taxonomy .Params.icon` 取空 → 图标名缺失）。
        // 值返回型 partial 在上方已跳过（保护 page.store 通道）
        if (args[1] is Parsing.ParenExpr parenCtx && !isValueReturning)
        {
            var inner = ConvertPipeline(parenCtx.Inner, scope);
            if (inner.Kind != ConversionKind.Unsupported)
            {
                return new ConversionResult(
                    $"partial \"{PartialPathFor(nameExpr)}\" ({inner.Text})", ConversionKind.Downgraded,
                    "partial 上下文参数以 page 绑定（Hugo dot 语义等价）");
            }
        }

        // 第二参数是**标量/变量/字段**：Hugo 的 `partial "x" VALUE` 把该值作为
        // partial 内的 `.`。Scriban 内置的 `include` 不接收上下文参数，故改用 Flint
        // 自定义的 `partial` 函数（签名 partial NAME CONTEXT，内部把 CONTEXT 临时绑成
        // page——Hugo dot 语义等价）。此前丢弃参数，使 partial 内 `.` 取不到值
        //（Stack 的 `partial "helper/icon" "search"` / `... $icon` 报
        //  "icon '%s.svg' is not found"）。
        // 值返回型 partial 跳过：其 `return` 已改写为 page.store.set，覆盖 page 会
        // 使写入落到临时对象上（引擎侧另有 UsesPageStore 兜底，双层防护）
        if (!isValueReturning)
        {
            var ctxR = ConvertExpr(args[1], scope, false);
            if (ctxR.Kind != ConversionKind.Unsupported &&
                ctxR.Text.Length > 0 &&
                ctxR.Text != "null")
            {
                return new ConversionResult(
                    $"partial \"{PartialPathFor(nameExpr)}\" {ctxR.Text}", ConversionKind.Downgraded,
                    "partial 上下文参数以 page 绑定（Hugo dot 语义等价）");
            }
        }

        // 带显式上下文/参数：Scriban 无等价（include 不接收上下文参数）→ 降级
        Diagnostics.Add($"partial 带上下文参数（{nameExpr}）：Scriban include 共享上下文，参数已省略");
        return new ConversionResult($"{target} \"{PartialPathFor(nameExpr)}\"", ConversionKind.Downgraded,
            "partial 上下文参数省略（Scriban include 共享调用者上下文）");
    }

    /// <summary>转换单个表达式节点</summary>
    private ConversionResult ConvertExpr(Parsing.Expr expr, IReadOnlyList<string> scope, bool isPipeSegment)
    {
        switch (expr)
        {
            case Parsing.LiteralExpr lit:
                return new ConversionResult(lit.Raw, ConversionKind.Equivalent);

            case Parsing.DotExpr:
                // 裸点：映射到当前上下文（range 变量或 page）
                return new ConversionResult(scope.Count > 0 ? scope[^1] : "page", ConversionKind.Equivalent);

            case Parsing.NilExpr:
                return new ConversionResult("null", ConversionKind.Equivalent);

            case Parsing.FieldExpr f:
            {
                // .Title → page.title；.Params.a → page.params.a
                var raw = f.Path;

                // 块内裸点：range/with 作用域下 `.Field` 的接收者是**当前循环/上下文变量**，
                // 而非 page。此前一律映射为 page.* —— 使 `{{ range .Pages }}{{ .Title }}`
                // 产出 `page.title`（循环变量被忽略，渲染错误页面的标题）。
                // 注意：`.Site.*` / `$.*` 是显式根，不受作用域影响
                // 排除显式根：`.Site`/`.Page` 必须是**完整段**（后跟 . 或结尾），
                // 否则 `.PageNumber`/`.Pages` 会被 `.Page` 前缀误伤（实测 bug：
                // range 内的 `.PageNumber` 未映射到循环变量，产出裸 `page_number`）
                var isExplicitRoot =
                    raw.Equals(".Site", StringComparison.OrdinalIgnoreCase) ||
                    raw.StartsWith(".Site.", StringComparison.OrdinalIgnoreCase) ||
                    raw.Equals(".Page", StringComparison.OrdinalIgnoreCase) ||
                    raw.StartsWith(".Page.", StringComparison.OrdinalIgnoreCase);
                if (scope.Count > 0
                    && raw.StartsWith('.')
                    && !isExplicitRoot
                    && raw.Length > 1)
                {
                    // 循环/上下文变量为 nil 时 Hugo 对成员访问返回 nil（宽容），
                    // Scriban 抛 "Cannot get the member $x.params.new_tab for a null
                    // object"（Stack sidebar 的 range 内 `.Params.new_tab` 实测）——
                    // 故链式段一律 nil 安全；末段若命中方法名用规范化名
                    var root = scope[^1];
                    var segsScope = raw.TrimStart('.')
                        .Split('.', StringSplitOptions.RemoveEmptyEntries);
                    var sbScope = new StringBuilder(root);
                    foreach (var segS in segsScope)
                    {
                        var mappedSegS = MapChainMethod("x." + segS);
                        var normalizedS = mappedSegS is not null
                            ? mappedSegS["x.".Length..]
                            : ToSnakePath(segS);
                        sbScope.Append("?.").Append(normalizedS);
                    }
                    return new ConversionResult(sbScope.ToString(), ConversionKind.Equivalent);
                }

                if (_map.MapPath(raw) is { } mapped)
                {
                    return new ConversionResult(mapped, ConversionKind.Equivalent);
                }

                    // 未命中：按 .A.B → page.a.b 规则映射。
                // 但 .Site.* / .Page.* 前缀须换根（Hugo 的 .Site.Params.x → site.params.x）
                // 前导 $ 的字段路径（$.Site.RegularPages / $.Resources.GetMatch）：
                // Hugo 的 $ 指页面上下文，须换根
                if (raw.StartsWith("$.", StringComparison.Ordinal))
                {
                    var loweredDollar = ToSnakePath(raw[1..]);
                    if (loweredDollar.StartsWith(".site", StringComparison.Ordinal))
                    {
                        return new ConversionResult(
                            "site" + loweredDollar[".site".Length..], ConversionKind.Equivalent);
                    }
                    return new ConversionResult(
                        "page" + loweredDollar, ConversionKind.Equivalent);
                }

                // 局部变量的成员访问（`$posts.paginate` / `$config.disable_messages`）：
                // 变量在数据缺失时为 nil，须用 nil 安全 `?.`（Hugo 返回 nil，Scriban 抛
                // "Cannot get the member $posts.paginate for a null object"——
                // LoveIt home.html 在未配置 params.home.posts 时实测）
                if (raw.StartsWith('$') && raw.Contains('.', StringComparison.Ordinal))
                {
                    var varName = raw[..raw.IndexOf('.', StringComparison.Ordinal)];
                    var restSegs = raw[(varName.Length + 1)..]
                        .Split('.', StringSplitOptions.RemoveEmptyEntries);
                    var sbVar = new StringBuilder(varName);
                    foreach (var segV in restSegs)
                    {
                        var mappedSegV = MapChainMethod("x." + segV);
                        var normalized = mappedSegV is not null ? mappedSegV["x.".Length..] : ToSnakePath(segV);
                        sbVar.Append("?.").Append(normalized);
                    }
                    return new ConversionResult(sbVar.ToString(), ConversionKind.Equivalent);
                }

                // `.Page.X` 无参形态同样剥除首段（与上面的字段+参数分支对称）
                if (raw.StartsWith(".Page", StringComparison.OrdinalIgnoreCase) &&
                    (raw.Length == ".Page".Length || raw[".Page".Length] == '.'))
                {
                    // 裸 `.Page`（无后续段）：dict 上下文里指的是 **dict 的 Page 键**
                    //（hugo-book 的 `(slice .Page)` 递归收集章节页），页面上下文里才是
                    // 页面自身——`page.page ?? page` 两种上下文通吃（与字段+参数分支同一判据）
                    if (raw.Equals(".Page", StringComparison.OrdinalIgnoreCase))
                    {
                        return new ConversionResult("(page.page ?? page)", ConversionKind.Equivalent);
                    }
                    // 链式段用 nil 安全 `?.`（与普通字段路径一致：Hugo 遇 nil 返回 nil）
                    return new ConversionResult(
                        "page" + NilSafePath(raw[".Page".Length..]),
                        ConversionKind.Equivalent);
                }

                if (raw.StartsWith('.') && raw.Length > 1)
                {
                    // `.Site.Data.x` / `.Data.x`：数据段名保持原样（见 TryMapDataPath）
                    if (TryMapDataPath(raw) is { } dataPathFe)
                    {
                        return new ConversionResult(dataPathFe, ConversionKind.Equivalent);
                    }
                    // 全部成员链用 nil 安全 `?.`：Hugo 的 `.A.B.C` 任一层为 nil 时
                    // 返回 nil（宽容），Scriban 的 `.` 链式访问遇 null 抛
                    // "Cannot get the member ... for a null object"。
                    // 这是矩阵验证中**跨主题最高频**的阻断原因
                    // （PaperMod/LoveIt/Stack/Ananke 均命中）
                    var loweredSafe = NilSafePath(raw);
                    // 已知方法名不用 nil 安全（它们是**函数调用目标**，`?.` 会使
                    // Scriban 把 `page?.get_page` 当函数名 → "function not found"，
                    // Stack/LoveIt 实测）。ConvertCommand 的字段+参数分支本应先行
                    // 拦截，此处兜底防止漏网。
                    // 例外：路径穿过 `.Params` 的是**数据袋**，同名末段（如
                    // `.Site.Params.list.paginate` 的 paginate）是普通字段不是方法——
                    // 若误判为方法就走非 nil 安全路径，链上中间层为 null 时抛
                    // "Cannot get the member ... for a null object"（LoveIt 实测）
                    // 末段命中已知集合方法名时**整条降级为普通点**：它们是函数调用目标，
                    // `?.` 会让 Scriban 把 `page?.x` 当函数名（Stack/LoveIt 实测）。
                    // 例外：路径穿过 `.Params` 的是**数据袋**，同名末段是普通字段。
                    // 【曾试放宽为"一律 nil 安全"以修 FixIt 的 `.Config.limit`，
                    //   全量矩阵判为回归：github-style 出现 IndexOutOfRange、
                    //   narrow 的 `page.pages.groupbydate` 报 null——该规则是必需的】
                    // 末段命中集合方法名时降级为普通点，**但仅在接收者确实是页面集合成员**时：
                    // 该规则原意是防"`?.` 会被 Scriban 当函数名"（Stack/LoveIt 实测），
                    // 而按名字全局降级会误伤**用户数据键**——FixIt 的 `.Config.limit`
                    // （params.feed 里的数据键）被当成集合方法 → 产出 `page.config.limit`
                    // 普通点链 → 最小配置下 params.feed 缺失即抛 "…for a null object"
                    //（Hugo 返回 nil、`ge nil 1` 为假）。故改为按**接收者段名**判定：
                    // pages/regular_pages/all_pages/sections/translations 这类才是集合
                    var pathSegs = raw.TrimStart('.').Split('.', StringSplitOptions.RemoveEmptyEntries);
                    var receiverSeg = pathSegs.Length >= 2 ? pathSegs[^2] : "";
                    var isCollectionReceiver = receiverSeg is
                        "pages" or "Pages" or "regular_pages" or "RegularPages"
                        or "all_pages" or "AllPages" or "site_pages" or "SitePages"
                        or "sections" or "Sections" or "translations" or "Translations"
                        or "subsections" or "Subsections";
                    var isMethodTarget = isCollectionReceiver
                        && raw.Contains('.', StringComparison.Ordinal)
                        && !raw.Contains(".Params.", StringComparison.OrdinalIgnoreCase)
                        && !raw.EndsWith(".Params", StringComparison.OrdinalIgnoreCase)
                        && MapChainMethod("page" + ToSnakePath(raw)) is not null;
                    {
                    }
                    if (_map.MapPath(raw) is null && !isMethodTarget)
                    {
                        // 显式根判定基于**首段**（`.Site` → site 根）：链式段用 `?.` 后，
                        // 形态是 `?.site?.params`，不能用 StartsWith(".site.") 匹配
                        var first = raw.TrimStart('.').Split('.', StringSplitOptions.RemoveEmptyEntries)
                            .FirstOrDefault() ?? "";
                        // NilSafePath 含首段（`?.site?.params`）——显式根需**替换**首段
                        // 而非前置拼接（否则 `site?.site?.params`）
                        // 从索引 2 起找第二个 `?.`（首个在位置 0，属首段，
                        // 须被替换掉——否则 `site?.site?.params`）
                        var secondMarker = loweredSafe.IndexOf("?.", 2, StringComparison.Ordinal);
                        var tail = secondMarker >= 0 ? loweredSafe[secondMarker..] : "";
                        var body = first.Equals("Site", StringComparison.OrdinalIgnoreCase)
                            ? "site" + tail
                            : first.Equals("Page", StringComparison.OrdinalIgnoreCase)
                                ? "page" + tail
                                : "page" + loweredSafe;
                        return new ConversionResult(body, ConversionKind.Equivalent);
                    }

                    var lowered = ToSnakePath(raw);

                    // 根切换必须是**完整段**匹配（`.site.` / `.page.`）——
                    // 用 StartsWith(".page") 会把 `.Pages.Len`（→`.pages.len`）
                    // 误当 `.Page` 根，产出 `pages.len`（丢失 page 根，实测 bug）。
                    // 同类陷阱：`.PageNumber`、`.Sitemap`
                    if (lowered.StartsWith(".site.", StringComparison.Ordinal))
                    {
                        return new ConversionResult("site." + lowered[".site.".Length..], ConversionKind.Equivalent);
                    }
                    if (lowered.StartsWith(".page.", StringComparison.Ordinal))
                    {
                        return new ConversionResult("page." + lowered[".page.".Length..], ConversionKind.Equivalent);
                    }
                    return new ConversionResult("page" + lowered, ConversionKind.Equivalent);
                }
                if (raw == ".")
                {
                    return new ConversionResult(scope.Count > 0 ? scope[^1] : "page", ConversionKind.Equivalent);
                }
                return new ConversionResult(raw, ConversionKind.Downgraded, "未映射字段路径");
            }

            case Parsing.VariableExpr v:
                // 裸 `$` 是 Hugo 的**顶层上下文**（布局里即当前页），Scriban 的 `$`
                // 却是"函数参数数组"——两个语义毫不相干。原样透传会让
                // `partial $partialPath $`（narrow 的 home.html 实测）把空参数数组当
                // 上下文传给 partial，被调方 `page` 变 null，报
                // "Cannot get the member page.content for a null object"
                return new ConversionResult(
                    v.Name == "$" ? "page" : v.Name, ConversionKind.Equivalent);

            case Parsing.ChainExpr { Base: Parsing.VariableExpr { Name: not "$" } ve } vc:
            {
                // 变量的成员访问用 **nil 安全操作符** `?.`：Hugo 的 `$x.y` 在 $x 为
                // nil 时返回 nil（宽容），Scriban 的 `$x.y` 抛
                // "Cannot get the member ... for a null object"。
                // 实测矩阵验证中多主题受阻于此（$bc.RelPermalink / $value.name /
                // $params.subtitle / $.Paginate 等）
                var sb2 = new System.Text.StringBuilder(ve.Name);
                foreach (var field in vc.Fields)
                {
                    var seg = ToSnakePath(field).TrimStart('.');
                    sb2.Append("?.").Append(seg.Length == 0 ? field.TrimStart('.') : seg);
                }
                return new ConversionResult(sb2.ToString(), ConversionKind.Equivalent);
            }

            case Parsing.IdentifierExpr id:
                // 含点的标识符（$.Site.RegularPages / site.Params.x）走路径映射；
                // 无参数场景下它是"数据引用"而非"函数调用"
                if (id.Name.Contains('.', StringComparison.Ordinal))
                {
                    // **带点的函数名**（compare.Ge / math.add / path.Join …）优先查函数映射：
                    // 它们走"含点标识符"分支，此前只当作**路径**处理（MapIdentifierPath 返回
                    // null 时原样保留）→ 命名空间调用从未被映射成全局函数，而命名空间成员
                    // 在**括号内**用空格实参会被 Scriban 误解析
                    //（`compare.Ge $sc (math.add $np 1)` 报 "Object must be of type Int32"，ananke 实测）
                    if (_map.HasFunction(id.Name))
                    {
                        return new ConversionResult(_map.MapFunction(id.Name), ConversionKind.Equivalent);
                    }

                    // 站点数据路径（hugo.Data.x / site.Data.x）：**段名保持原样**，
                    // 仅首段归一 + 全部 nil 安全。数据文件的键是作者定义的
                    // （Stack 的 data/external.toml 用 `[PhotoSwipe]`/`Style` 驼峰），
                    // 若按 ToSnakePath 转成 `photo_swipe`/`style` 会取不到值
                    if (TryMapDataPath(id.Name) is { } dataPath)
                    {
                        return new ConversionResult(dataPath, ConversionKind.Equivalent);
                    }

                    // 局部变量的成员访问（$bc.RelPermalink / $params.subtitle）：
                    // 用 nil 安全操作符 `?.`——Hugo 的 `$x.y` 在 $x 为 nil 时返回 nil
                    // （宽容），Scriban 抛 "Cannot get the member ... for a null object"。
                    // 矩阵验证中多主题受阻于此（PaperMod 的 $bc / LoveIt 的 $params /
                    // Stack 的 $params），故统一 nil 安全化
                    if (id.Name.StartsWith('$') && !id.Name.StartsWith("$.", StringComparison.Ordinal))
                    {
                        var segs = id.Name.Split('.');
                        var chain = new System.Text.StringBuilder(segs[0]);
                        for (var si = 1; si < segs.Length; si++)
                        {
                            var seg = ToSnakePath("." + segs[si]).TrimStart('.');
                            chain.Append("?.").Append(seg);
                        }
                        return new ConversionResult(chain.ToString(), ConversionKind.Equivalent);
                    }

                    var mappedId = MapIdentifierPath(id.Name);
                    if (mappedId is not null)
                    {
                        return new ConversionResult(mappedId, ConversionKind.Equivalent);
                    }
                }
                return id.Name is "page" or "Page"
                    ? new ConversionResult(GlobalPageRoot, ConversionKind.Equivalent)
                    : new ConversionResult(id.Name, ConversionKind.Equivalent);

            case Parsing.ParenExpr p:
            {
                var inner = ConvertPipeline(p.Inner, scope);
                if (inner.Kind == ConversionKind.Unsupported)
                {
                    return inner;
                }
                return new ConversionResult("(" + inner.Text + ")", ConversionKind.Equivalent);
            }

            case Parsing.ChainExpr c:
            {
                // base 为 $（Hugo 的页面上下文）：换根 + 路径映射
                // （$.Site.RegularPages → site.regular_pages / $.Resources.GetMatch → page.resources.getmatch）
                if (c.Base is Parsing.VariableExpr { Name: "$" })
                {
                    var joined = "$" + string.Concat(c.Fields);
                    var mappedChain = MapIdentifierPath(joined);
                    if (mappedChain is not null)
                    {
                        return new ConversionResult(mappedChain, ConversionKind.Equivalent);
                    }
                }

                var baseR = ConvertExpr(c.Base, scope, false);
                if (baseR.Kind == ConversionKind.Unsupported)
                {
                    return baseR;
                }
                // 基表达式是**调用结果或局部变量**时用 nil 安全 `?.`：
                // Hugo 对 nil 结果的链式成员访问返回 nil（宽容），Scriban 抛
                // "Cannot get the member ... for a null object"。典型形态
                // `(.Scratch.Get "params").share` / `$posts.paginate` /
                // `(site.GetPage X).Layout`（LoveIt/PaperMod 实测）。
                // ParenExpr 转换后自带括号（`(f a b)`），故索引为 `?.` 而非 `.?`
                // **FieldExpr 基也 nil 安全**：`.A.B.C` 的解析形态视词法而变（可能是单个
                // FieldExpr，也可能是 ChainExpr+FieldExpr 基），此前只有后者走普通点 →
                // 同一条路径在不同形态下产出不同（`if ge .Config.limit 1` 走 ChainExpr →
                // `page.config.limit` 普通点，最小配置下抛 null 成员错；而
                // `first .Config.limit` 走无参分支 → `page?.config?.limit` ✔）。
                // Hugo 对缺失中间层返回 nil，故统一 nil 安全；调用形态由
                // FixNilSafeCalls 在 Wrap 阶段降级为普通点
                var nilSafeBase = c.Base is Parsing.ParenExpr or Parsing.VariableExpr
                    or Parsing.CallExpr or Parsing.FieldExpr;
                var sb = new StringBuilder(baseR.Text);
                for (var fi = 0; fi < c.Fields.Count; fi++)
                {
                    // 末段若是已知方法名，用规范化名（getmatch 而非 get_match）。
                    // 段文本可能带**前导点**（解析器把 `.share` 存成带点形态）——
                    // 拼接前必须剥掉，否则与分隔符叠加成 `?..layout`（非法 token，
                    // PaperMod/LoveIt 实测大面积解析失败）
                    var field = c.Fields[fi].TrimStart('.');
                    string seg;
                    if (fi == c.Fields.Count - 1)
                    {
                        var m2 = MapChainMethod("x." + field);
                        seg = m2 is not null ? m2["x.".Length..] : ToSnakePath(field);
                    }
                    else
                    {
                        seg = ToSnakePath(field);
                    }
                    sb.Append(nilSafeBase ? "?." : ".").Append(seg);
                }
                return new ConversionResult(sb.ToString(), ConversionKind.Equivalent);
            }

            default:
                return new ConversionResult("", ConversionKind.Unsupported, $"未知表达式 {expr.GetType().Name}");
        }
    }

    /// <summary>
    /// 把"接收者与方法之间的 nil 安全分隔符"退回普通点：仅用于**有参调用**形态。
    /// Scriban 不支持 `(x)?.f a`（整体被当函数名），而字段访问（无参）保留 `?.` 的宽容语义
    /// </summary>
    private static string UnsafeTarget(string mapped)
    {
        var idx = mapped.LastIndexOf("?.", StringComparison.Ordinal);
        return idx < 0 ? mapped : mapped[..idx] + "." + mapped[(idx + 2)..];
    }

    /// <summary>Scriban 对象键的合法标识符判定（字母/下划线开头，字母数字下划线）</summary>
    private static bool IsIdentifier(string s)
    {
        if (string.IsNullOrEmpty(s) || (!char.IsLetter(s[0]) && s[0] != '_'))
        {
            return false;
        }
        return s.All(c => char.IsLetterOrDigit(c) || c == '_');
    }

    /// <summary>
    /// 字段路径转 snake：.Params.Title → .params.title；.Title → .title。
    /// 连续大写（.URL）整体小写
    /// </summary>
    /// <summary>
    /// <see cref="ToSnakePath"/> 的 **nil 安全**形态：首段之后的段间用 <c>?.</c>。
    /// Hugo 对缺失中间层返回 nil（宽容），Scriban 的普通点链遇 null 抛
    /// "Cannot get the member … for a null object"。链式方法接收者此前走普通点
    ///（FixIt 的 `.Config.limit` 在最小配置下 `params.feed` 缺失 → 报错；
    ///  `$.Site.Store.Set` 同族）
    /// </summary>
    private static string ToSnakePathNilSafe(string path)
    {
        var snake = ToSnakePath(path);
        var sb = new StringBuilder(snake.Length + 8);
        for (var i = 0; i < snake.Length; i++)
        {
            if (snake[i] == '.' && i > 0)
            {
                sb.Append("?.");
            }
            else
            {
                sb.Append(snake[i]);
            }
        }
        return sb.ToString();
    }

    private static string ToSnakePath(string path)
    {
        var sb = new StringBuilder();
        var i = 0;
        while (i < path.Length)
        {
            var c = path[i];
            if (c == '.')
            {
                sb.Append('.');
                i++;
                continue;
            }

            // 收集一段标识符
            var start = i;
            while (i < path.Length && path[i] != '.')
            {
                i++;
            }
            var seg = path[start..i];
            sb.Append(Seg(seg));
        }
        return sb.ToString();
    }

    /// <summary>
    /// 站点数据路径映射（Hugo 的 <c>hugo.Data.x</c> / <c>.Site.Data.x</c> / <c>.Data.x</c>）：
    /// 首段归一为 <c>hugo.data</c> / <c>site.data</c> / <c>page.data</c>，
    /// **其余段原样保留**（数据文件键由作者定义，不能 snake 化），
    /// 段间统一 nil 安全（缺数据时 Hugo 返回 nil，Scriban 普通点链抛异常）。
    /// 非数据路径返回 null
    /// </summary>
    private static string? TryMapDataPath(string raw)
    {
        var s = raw.StartsWith('.') ? raw[1..] : raw;
        var segs = s.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segs.Length < 2)
        {
            return null;
        }

        // 仅处理第二段是 Data 的路径（hugo.Data.x / Site.Data.x / Data.x）
        if (!segs[1].Equals("Data", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var root = segs[0].ToLowerInvariant() switch
        {
            // Hugo 的 hugo.Data 就是站点数据（data/ 目录），映射到 site.data——
            // 那里才有实际内容（hugo 对象的 data 字段未由构建入口填充，实测为空）
            "hugo" => "site",
            "site" => "site",
            "page" => GlobalPageRoot,
            "$" => "page",
            _ => null
        };
        if (root is null)
        {
            return null;
        }

        var sb = new StringBuilder(root).Append("?.data");
        for (var i = 2; i < segs.Length; i++)
        {
            sb.Append("?.").Append(segs[i]);
        }
        return sb.ToString();
    }

    private static string Seg(string seg)
    {
        if (seg.Length == 0)
        {
            return seg;
        }
        // 全大写（URL/ID）→ 整体小写
        if (seg.All(char.IsUpper))
        {
            return seg.ToLowerInvariant();
        }
        // 首字母大写 → 小写 + 下划线分隔
        var sb = new StringBuilder();
        for (var i = 0; i < seg.Length; i++)
        {
            var c = seg[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                {
                    sb.Append('_');
                }
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}

/// <summary>
/// 结构守恒检查（静默损坏防线）。
/// 检出"语法合法但语义形状可疑"的产出：
/// - 括号不平衡
/// - 比较运算符后紧跟操作数（`(a < b) c` 这类畸形）
/// - 空产出但输入非空
/// </summary>
internal static class StructuralGuard
{
    /// <summary>返回问题描述；null 表示通过</summary>
    public static string? Check(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "产出为空";
        }

        if (!Balanced(text, '(', ')'))
        {
            return "括号不平衡";
        }
        if (!Balanced(text, '[', ']'))
        {
            return "方括号不平衡";
        }
        if (!Balanced(text, '{', '}'))
        {
            return "花括号不平衡";
        }

        // 引号配对（跳过转义）
        if (!QuotesBalanced(text))
        {
            return "引号不平衡";
        }

        // 畸形状：`) 操作数` —— 括号闭合后直接跟标识符/数字/字符串（缺运算符）。
        // **只在括号处于"被调用位置"时才算可疑**：`(a) (b)` 是把括号表达式当函数名，
        // Scriban 不支持（转换器用 HoistParenReceiver 提取临时变量来绕开）。
        // 括号出现在**实参**位置则是合法形态，此前被误判——
        // `dict "k" (slice 1 2) "k2" "v2"`、`index (slice 1 2) 0` 这类多参调用里，
        // 只要括号前还有其他实参，原白名单 `[A-Za-z_][\w.]*\s*\(`（要求标识符**紧邻**左括号）
        // 就匹配不上，整条表达式被判 Unsupported，产出退化为空字符串：
        // fixit 的 get-taxonomy-icon（`$defaults = ""` → index 越界）
        // 与 stack 的 helper/image 实测 17 处
        var bad = System.Text.RegularExpressions.Regex.IsMatch(
            text, @"^\s*\([^()]*\)\s*[A-Za-z0-9_""']",
            System.Text.RegularExpressions.RegexOptions.None);
        if (bad)
        {
            return "括号后紧跟操作数（可能缺运算符）";
        }

        return null;
    }

    private static bool Balanced(string s, char open, char close)
    {
        var depth = 0;
        char quote = '\0';
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (quote != '\0')
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == quote)
                {
                    quote = '\0';
                }
                continue;
            }
            if (c is '"' or '\'' or '`')
            {
                quote = c;
                continue;
            }
            if (c == open)
            {
                depth++;
            }
            else if (c == close && --depth < 0)
            {
                return false;
            }
        }
        return depth == 0;
    }

    private static bool QuotesBalanced(string s)
    {
        char quote = '\0';
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (quote != '\0')
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == quote)
                {
                    quote = '\0';
                }
                continue;
            }
            if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '{' || c == '[')
            {
                continue;
            }
        }
        return quote == '\0';
    }
}

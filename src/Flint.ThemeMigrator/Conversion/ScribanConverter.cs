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
    string? selfPartialName = null)
{
    private readonly MigrationMap _map = map;

    /// <summary>含 {{ return }} 的 partial 规范名集合（Hugo 返回值语义）</summary>
    private readonly IReadOnlySet<string>? _valueReturning = valueReturningPartials;

    /// <summary>本文件对应的 partial 规范名（null = 非 partial 文件）</summary>
    public string? SelfPartialName { get; } = selfPartialName;

    /// <summary>partialValue 的 Store 键前缀（与引擎侧 ScribanTemplateRenderer 一致）</summary>
    internal const string RetKeyPrefix = "__partial_ret_";

    /// <summary>转换诊断（降级/不支持项）</summary>
    public List<string> Diagnostics { get; } = [];

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
        "resources.FromString", "resources.Copy",
        "printf", "fmt.Printf",
        "errorf", "fmt.Errorf", "warnf", "fmt.Warnf",
        "erroridf", "fmt.Erroridf", "warnidf", "fmt.Warnidf",
        "i18n", "lang.Translate"
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
    /// 由 FileTemplateLoader 在 partials 目录（含旧目录回退）内解析
    /// </summary>
    internal static string PartialPathFor(string raw) => "_partials/" + CanonicalPartialName(raw);

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
            // 故此处显式改写为函数调用形态，把左值放回末位
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
                // 此处左值非 dot 时保持 include，行为差异已在 Note 标注
                acc = callRes.Text + " " + acc;
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
                var dateRecv = dateTailPlain.StartsWith(".site", StringComparison.OrdinalIgnoreCase)
                    ? "site" + dateTailPlain[".site".Length..]
                    : scope.Count > 0
                        ? scope[^1] + dateTailPlain
                        : "page" + dateTailPlain;
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
            var feMapped = isSiteRoot
                ? "site" + ToSnakePath(fe.Path[".Site".Length..])
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

                return new ConversionResult(
                    mapped + " " + string.Join(" ", argTexts), ConversionKind.Equivalent);
            }
        }

        // 非标识符开头的命令：单操作数（字段/字面量/括号）
        if (operands.Count == 1)
        {
            return ConvertExpr(first, scope, isPipeSegment);
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
            return new ConversionResult($"({left.Text} {op} {right.Text})", ConversionKind.Equivalent);
        }

        if (name is "and" or "or")
        {
            var op = name == "and" ? "&&" : "||";
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
            return new ConversionResult("(" + string.Join($" {op} ", parts) + ")", ConversionKind.Equivalent);
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
            var recvText = recv.Length == 0 || recv == "$" || recv == "."
                ? "page"
                : recv;
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
                    $"page.store.set \"{key}\" {r.Text} }}}}}}{{{{ ret",
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

                var call0 = argTexts0.Count == 0 ? mapped : mapped + " " + string.Join(" ", argTexts0);
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
            "$.Page" or "$" or "." or "page" => "page",
            "$.Site.RegularPages" => "site.regular_pages",
            "$.Site.Pages" => "site.pages",
            _ when head.StartsWith("$.Site.", StringComparison.Ordinal) =>
                "site" + ToSnakePath(head[".Site".Length..]),
            // `$.Page.GetTerms` / `$.GetTerms`：`$.X` 的 X 是页面成员，须剥掉
            // `$.` 前缀再映射（此前 head[1..] 产出 `.GetTerms` → page.get_terms 正常，
            // 但 `$.Page.X` 形态会产出 `page.page.x`——此处统一走 Page 剥除）
            _ when head.StartsWith("$.Page.", StringComparison.Ordinal) =>
                "page" + ToSnakePath(head[".Page".Length..]),
            _ when head.StartsWith("$.", StringComparison.Ordinal) ||
                   head.StartsWith('.') =>
                "page" + ToSnakePath(head.StartsWith('.') ? head : head[1..]),
            _ => head
        };

        // 局部变量接收者（`$scratch.Add` / `$pag.Pagers`）：head 就是变量名本身，
        // 不能再加 page 前缀。**裸 `$` 与 `$.` 除外**——那是 Hugo 的页面上下文
        // （`$.GetTerms` → page.get_terms，正是 head 分支已给出的结果）
        if (head.Length > 1 && head[0] == '$' && head[1] != '.')
        {
            headText = head;
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

        var op = fn switch
        {
            "and" => "&&", "or" => "||",
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
            var pageRest = JoinPathSegments(segs.Skip(1), nilSafe: true);
            return pageRest.Length == 0 ? "page" : "page" + pageRest;
        }

        var restSafe = JoinPathSegments(segs.Skip(1), nilSafe: true);
        return root switch
        {
            "site" or "Site" => "site" + restSafe,
            "page" or "Page" => "page" + restSafe,
            _ => null
        };
    }

    /// <summary>dict k1 v1 k2 v2 → { k1: v1, k2: v2 }</summary>
    private ConversionResult ConvertDict(List<Parsing.Expr> args, IReadOnlyList<string> scope)
    {
        if (args.Count % 2 != 0)
        {
            Diagnostics.Add($"dict 参数数为奇数（{args.Count}）");
            return new ConversionResult("", ConversionKind.Unsupported, "dict 参数数为奇数");
        }

        var pairs = new List<string>();
        for (var i = 0; i < args.Count; i += 2)
        {
            var keyExpr = args[i];
            var key = keyExpr switch
            {
                Parsing.LiteralExpr lit => lit.Unquoted,
                Parsing.IdentifierExpr idExpr => idExpr.Name,
                _ => null
            };
            if (key is null)
            {
                Diagnostics.Add($"dict 键非字面量: {keyExpr.GetType().Name}");
                return new ConversionResult("", ConversionKind.Unsupported, "dict 键非字面量");
            }

            var v = ConvertExpr(args[i + 1], scope, false);
            if (v.Kind == ConversionKind.Unsupported)
            {
                return v;
            }
            // 键须为合法 Scriban 标识符，否则用引号形式（"data-x": v）。
            // 实测主题用 "a=1" / "data-src" 这类非标识符键，裸写会报
            // "Unexpected token `-` Expecting a colon"
            var keyText = IsIdentifier(key) ? key : "\"" + key.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
            pairs.Add($"{keyText}: {v.Text}");
        }

        // 空 dict：Scriban 无法解析空对象字面量 `{ }`（实测 PARSE-ERR），
        // 改用 dict 函数形态；非空用对象字面量（更贴近 Hugo 语义且可读）
        if (pairs.Count == 0)
        {
            return new ConversionResult("dict", ConversionKind.Equivalent);
        }

        return new ConversionResult("{ " + string.Join(", ", pairs) + " }", ConversionKind.Equivalent);
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
            return new ConversionResult("", ConversionKind.Unsupported, "partial 名称非字面量");
        }

        var isCached = name is "partialCached" or "partials.IncludeCached";
        var target = isCached ? "partialcached" : "include";

        // 返回值型 partial → partialValue（Hugo 返回对象 vs Scriban 文本化）。
        // 仅当上下文参数是 dot（共享上下文等价）时改写；传其他对象时保持 include
        var canonical = CanonicalPartialName(nameExpr);
        var isValueReturning = _valueReturning is not null && _valueReturning.Contains(canonical);
        if (!isCached && isValueReturning)
        {
            var isDotValueCtx = args.Count <= 1 || args[1] is Parsing.DotExpr
                or Parsing.FieldExpr { Path: "." };
            if (isDotValueCtx)
            {
                return new ConversionResult(
                    $"partialValue \"{PartialPathFor(nameExpr)}\"", ConversionKind.Equivalent);
            }
            Diagnostics.Add($"返回值型 partial {nameExpr} 的上下文参数非 dot，保持 include（语义可能不等价）");
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
                    var isMethodTarget = raw.Contains('.', StringComparison.Ordinal)
                        && !raw.Contains(".Params.", StringComparison.OrdinalIgnoreCase)
                        && !raw.EndsWith(".Params", StringComparison.OrdinalIgnoreCase)
                        && MapChainMethod("page" + ToSnakePath(raw)) is not null;
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
                return new ConversionResult(v.Name, ConversionKind.Equivalent);

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
                return new ConversionResult(id.Name, ConversionKind.Equivalent);

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
                var nilSafeBase = c.Base is Parsing.ParenExpr or Parsing.VariableExpr
                    or Parsing.CallExpr;
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
            "page" or "$" => "page",
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

        // 畸形状：`) 操作数` —— 括号闭合后直接跟标识符/数字（缺运算符）
        var bad = System.Text.RegularExpressions.Regex.IsMatch(
            text, @"\)\s*[A-Za-z0-9_""']",
            System.Text.RegularExpressions.RegexOptions.None);
        // 允许函数调用形态 f(...) 与数组字面量
        if (bad && !System.Text.RegularExpressions.Regex.IsMatch(text, @"[A-Za-z_][\w.]*\s*\("))
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

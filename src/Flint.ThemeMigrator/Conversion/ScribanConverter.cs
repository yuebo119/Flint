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

    /// <summary>转换一个管道（表达式主体）</summary>
    public ConversionResult ConvertPipeline(Parsing.Pipeline pipeline, IReadOnlyList<string> scope, bool resourceContext = false)
    {
        if (pipeline.Commands.Count == 0)
        {
            return new ConversionResult("", ConversionKind.Equivalent);
        }

        var parts = new List<string>();
        var kind = ConversionKind.Equivalent;
        string? note = null;

        for (var i = 0; i < pipeline.Commands.Count; i++)
        {
            var r = ConvertCommand(pipeline.Commands[i], scope, isPipeSegment: i > 0, resourceContext);
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
            parts.Add(r.Text);
        }

        var text = string.Join(" | ", parts);

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
            return ConvertCall(id, operands.Skip(1).ToList(), scope);
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
                var dateRecv = "page" + feSnake[..^".format".Length];
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

            var mapped = MapChainMethod("page" + ToSnakePath(fe.Path));
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
    private ConversionResult ConvertCall(Parsing.IdentifierExpr fn, List<Parsing.Expr> args, IReadOnlyList<string> scope)
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

            if (SelfPartialName is not null)
            {
                var key = RetKeyPrefix + SelfPartialName;
                return new ConversionResult(
                    $"page.store.set \"{key}\" {r.Text} }}}}}}{{{{ ret",
                    ConversionKind.Equivalent);
            }

            return new ConversionResult("ret " + r.Text, ConversionKind.Equivalent);
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
            "Reverse" => "reverse",
            "Limit" => "limit",
            "Related" => "related",
            "Render" => "render",
            "First" => "first",
            "Last" => "last",
            "Uniq" => "uniq",
            "Sort" => "sort",
            "Where" => "where",
            _ => (string?)null
        };

        if (flintMethod is null)
        {
            return null;
        }

        // head 归一：Hugo 的 $ 指页面上下文（Scriban 的 $ 是函数参数数组，
        // 语义完全不同），故 $.X → page.x，$.Site.X → site.x
        var headText = head switch
        {
            "$.Site" or "site" => "site",
            "$.Page" or "$" or "." or "page" => "page",
            "$.Site.RegularPages" => "site.regular_pages",
            "$.Site.Pages" => "site.pages",
            _ when head.StartsWith("$.Site.", StringComparison.Ordinal) =>
                "site" + ToSnakePath(head[".Site".Length..]),
            _ when head.StartsWith('$') =>
                "page" + ToSnakePath(head[1..]),
            _ => head
        };

        if (headText.StartsWith('.'))
        {
            headText = "page" + ToSnakePath(headText);
        }

        return headText + "." + flintMethod;
    }

    /// <summary>
    /// 拼接点路径段：末尾段若命中已知方法名，用规范化后的 Flint 名
    /// （GetMatch → getmatch 而非 get_match，须与引擎注册名一致）
    /// </summary>
    private static string JoinPathSegments(IEnumerable<string> segments)
    {
        var list = segments.ToList();
        if (list.Count == 0)
        {
            return "";
        }

        var head = string.Join(".", list.Take(list.Count - 1).Select(Seg));
        var last = list[^1];
        var lastMapped = MapChainMethod("x." + last);
        var lastSeg = lastMapped is not null
            ? lastMapped["x.".Length..]
            : Seg(last);
        return head.Length == 0 ? lastSeg : head + "." + lastSeg;
    }

    /// <summary>表达式转文本的容错版：失败返回 null（供 printf 等参数收集用）</summary>
    private string? ConvertExpr0(Parsing.Expr expr, IReadOnlyList<string> scope)
    {
        var r = ConvertExpr(expr, scope, false);
        return r.Kind == ConversionKind.Unsupported ? null : r.Text;
    }

    /// <summary>
    /// 点路径标识符映射（site.Params.x → site.params.x；page.Params.y → page.params.y）。
    /// 返回 null 表示不是可识别的路径根（交回常规处理）
    /// </summary>
    private static string? MapIdentifierPath(string name)
    {
        var segs = name.Split('.');
        if (segs.Length < 2)
        {
            return null;
        }

        var root = segs[0];

        // $ 根：Hugo 的 $ 指页面上下文；$.Site.* 指站点
        if (root == "$")
        {
            if (segs.Length >= 2 && (segs[1] == "Site" || segs[1] == "site"))
            {
                var siteRest = JoinPathSegments(segs.Skip(2));
                return siteRest.Length == 0 ? "site" : "site." + siteRest;
            }
            var pageRest = JoinPathSegments(segs.Skip(1));
            return pageRest.Length == 0 ? "page" : "page." + pageRest;
        }

        var rest = string.Join(".", segs.Skip(1).Select(Seg));
        return root switch
        {
            "site" or "Site" => "site." + rest,
            "page" or "Page" => "page." + rest,
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
        if (!isCached && _valueReturning is not null && _valueReturning.Contains(canonical))
        {
            var isDotContext = args.Count <= 1 || args[1] is Parsing.DotExpr
                or Parsing.FieldExpr { Path: "." };
            if (isDotContext)
            {
                return new ConversionResult(
                    $"partialValue \"{nameExpr}\"", ConversionKind.Equivalent);
            }
            Diagnostics.Add($"返回值型 partial {nameExpr} 的上下文参数非 dot，保持 include（语义可能不等价）");
        }

        // 第二参数是 dot / page：Scriban include 天然共享上下文，省略
        if (args.Count <= 1 || args[1] is Parsing.DotExpr or Parsing.FieldExpr { Path: "." })
        {
            return new ConversionResult($"{target} \"{nameExpr}\"", ConversionKind.Equivalent);
        }

        // 第二参数是括号表达式（dict/slice 等）：Scriban 的 include 不接收上下文参数，
        // 但参数内容仍可转换——产出为注释化提示（内容不丢失，行为差异已标注）
        if (args[1] is Parsing.ParenExpr paren)
        {
            var inner = ConvertPipeline(paren.Inner, scope);
            if (inner.Kind != ConversionKind.Unsupported)
            {
                Diagnostics.Add($"partial 上下文参数（{nameExpr}）：Scriban include 共享上下文，参数已省略");
                // 参数内容记入 Note（不写入产物——Scriban 注释在动作内会被
                // 解析器当作对象初始化器，实测致 8 个预检失败）
                return new ConversionResult(
                    $"{target} \"{nameExpr}\"",
                    ConversionKind.Downgraded,
                    $"partial 上下文参数省略（原参数: {inner.Text}）");
            }
        }

        // 带显式上下文/参数：Scriban 无等价（include 不接收上下文参数）→ 降级
        Diagnostics.Add($"partial 带上下文参数（{nameExpr}）：Scriban include 共享上下文，参数已省略");
        return new ConversionResult($"{target} \"{nameExpr}\"", ConversionKind.Downgraded,
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

                if (raw.StartsWith('.') && raw.Length > 1)
                {
                    var lowered = ToSnakePath(raw);
                    if (lowered.StartsWith(".site", StringComparison.Ordinal))
                    {
                        return new ConversionResult("site" + lowered[".site".Length..], ConversionKind.Equivalent);
                    }
                    if (lowered.StartsWith(".page", StringComparison.Ordinal))
                    {
                        return new ConversionResult("page" + lowered[".page".Length..], ConversionKind.Equivalent);
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

            case Parsing.IdentifierExpr id:
                // 含点的标识符（$.Site.RegularPages / site.Params.x）走路径映射；
                // 无参数场景下它是"数据引用"而非"函数调用"
                if (id.Name.Contains('.', StringComparison.Ordinal))
                {
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
                var sb = new StringBuilder(baseR.Text);
                for (var fi = 0; fi < c.Fields.Count; fi++)
                {
                    // 末段若是已知方法名，用规范化名（getmatch 而非 get_match）
                    var field = c.Fields[fi];
                    if (fi == c.Fields.Count - 1)
                    {
                        var m2 = MapChainMethod("x." + field);
                        sb.Append(m2 is not null ? m2["x.".Length..] : ToSnakePath(field));
                    }
                    else
                    {
                        sb.Append(ToSnakePath(field));
                    }
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

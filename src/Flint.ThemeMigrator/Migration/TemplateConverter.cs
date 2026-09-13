// Flint 主题迁移工具
// 模板级转换：结构块（if/with/range/define/block）编排
//
// 结构性约束（实测得出）：
// 1. Scriban 不支持 `for k, v in obj`（PARSE-ERR）→ 双变量 range 展开为
//    单变量迭代 + 块首从 .key/.value 解构
// 2. Scriban 无 block/extends 语句 → baseof 的 block 声明转 capture + 条件输出，
//    子模板的 define 转 capture + 文件末尾 include 命名参数
// 3. with 无 else → 降级为 `$w = expr; if $w`

using System.Text;
using Flint.ThemeMigrator.Conversion;
using Flint.ThemeMigrator.Parsing;

namespace Flint.ThemeMigrator.Migration;

/// <summary>模板转换统计</summary>
internal sealed class TemplateConversionStats
{
    public int Actions { get; set; }
    public int Expresssions { get; set; }
    public int Unsupported { get; set; }
    public int Downgraded { get; set; }
    public List<string> Notes { get; } = [];
}

/// <summary>
/// 模板转换器：TemplatePart 列表 → Scriban 文本。
/// 有状态（块栈、define 收集），每个模板文件用一个实例。
/// </summary>
internal sealed class TemplateConverter(
    MigrationMap map,
    IReadOnlySet<string>? valueReturningPartials = null,
    string? selfPartialName = null,
    bool baseofAvailable = false)
{
    private readonly ScribanConverter _expr = new(map, valueReturningPartials, selfPartialName);
    private readonly List<(string Kind, string? Var)> _blockStack = [];
    private readonly List<string> _definedBlocks = [];
    private int _syntheticIndex;

    public TemplateConversionStats Stats { get; } = new();

    public IReadOnlyList<string> Diagnostics => _expr.Diagnostics;

    /// <summary>转换整个模板</summary>
    public string Convert(IReadOnlyList<TemplatePart> parts)
    {
        var sb = new StringBuilder();
        var scope = new List<string>(); // range/with 上下文变量栈

        foreach (var part in parts)
        {
            switch (part)
            {
                case TextPart t:
                    sb.Append(t.Text);
                    break;

                case CommentPart:
                    // Hugo 注释不产出。此前写成 Scriban 注释 {{# ... #}}，
                    // 但注释内含 `* /`（转义后）与换行时仍会被 Scriban 解析器
                    // 误判（实测 15 个预检失败）——直接丢弃最安全
                    break;

                case ActionPart a:
                    sb.Append(ConvertAction(a, scope));
                    break;
            }
        }

        // 子模板块通过 include 命名参数传给 baseof（若有 define）
        if (_definedBlocks.Count > 0)
        {
            var pairs = string.Join(" ", _definedBlocks.Select(b => $"{b}: {b}"));
            sb.Append("\n{{ include \"baseof.html\" ").Append(pairs).Append(" }}\n");
        }
        // Hugo 的「仅用 define 触发 baseof 继承」写法：hugo-book 的 single.html/
        // list.html 全部内容只是 `{{ define "dummy" }}{{ end }}`——define 被提取为
        // 独立 partial 后本文件变空，但 Hugo 仍会渲染 baseof 骨架。不补 include 时
        // 整站每页都是空文件（hugo-book 实测：14 页合计 94 字节）
        else if (baseofAvailable && sb.ToString().Trim().Length == 0)
        {
            sb.Append("{{ include \"baseof.html\" }}\n");
        }

        return sb.ToString();
    }

    /// <summary>转换单个动作</summary>
    private string ConvertAction(ActionPart action, List<string> scope)
    {
        Stats.Actions++;
        var trimL = action.TrimLeft ? "-" : "";
        var trimR = action.TrimRight ? "-" : "";

        switch (action.Body)
        {
            case KeywordBody kb:
                return ConvertKeyword(kb, scope, trimL, trimR);

            case ExprBody eb:
                return ConvertExprBody(eb, scope, trimL, trimR);

            default:
                return Wrap(action.Raw, trimL, trimR);
        }
    }

    private string ConvertKeyword(KeywordBody kb, List<string> scope, string trimL, string trimR)
    {
        switch (kb.Name)
        {
            case "if":
            {
                var cond = ConvertPipelineText(kb.Pipeline, scope);
                _blockStack.Add(("if", null));
                return Wrap($"if {cond}", trimL, trimR);
            }

            case "else":
            {
                // Hugo 语义：with 的 else 分支**恢复外层 dot**（不再是 with 的值）。
                // 此前不处理，使 `{{ with .Description }}...{{ else }}{{ if .IsPage }}`
                // 的 `.IsPage` 被映射为 with 变量的成员（`$__w0.is_page`）→
                // 运行期 "Cannot get the member ... for a null object"
                // （Ananke 的 baseof.html meta description 实证）
                if (_blockStack.Count > 0 && _blockStack[^1].Kind == "with")
                {
                    _blockStack[^1] = ("with-else", null);
                    if (scope.Count > 0)
                    {
                        scope.RemoveAt(scope.Count - 1);
                    }
                }

                // else if 带管道
                if (kb.Pipeline is { Commands.Count: > 0 })
                {
                    var cond = ConvertPipelineText(kb.Pipeline, scope);
                    return Wrap($"else if {cond}", trimL, trimR);
                }
                return Wrap("else", trimL, trimR);
            }

            case "end":
            {
                var (kind, extra) = _blockStack.Count > 0
                    ? (_blockStack[^1].Kind, _blockStack[^1].Var)
                    : ("unknown", null);
                if (_blockStack.Count > 0)
                {
                    _blockStack.RemoveAt(_blockStack.Count - 1);
                }
                // with 帧才弹 scope；with-else 已在 else 分支弹过（避免双重弹出）
                if (scope.Count > 0 && kind is "with" or "range")
                {
                    scope.RemoveAt(scope.Count - 1);
                }

                // baseof 的 block 声明：结束处补条件输出（子模板值优先，默认值兜底）
                if (kind == "blockdef" && extra is not null)
                {
                    // extra = 块名（如 title）→ 子模板键 blk_title / 默认值键 __def_title
                    var key = SanitizeIdent(extra);
                    var childKey = "blk_" + key;
                    return Wrap(
                        // `$.blk_x` 曾按页面根映射输出——Scriban 里 `$` 是未定义符号，
                        // 报 "Cannot get the member $.blk_x for a null object" 让整页失败。
                        // 块值由 `capture blk_x` 落在**全局变量**上，直接按名引用即可
                        $"end }}}}{{{{ if {childKey} }}}}{{{{ {childKey} }}}}{{{{ else }}}}{{{{ __def_{key} }}}}{{{{ end",
                        trimL, trimR);
                }
                return Wrap("end", trimL, trimR);
            }

            case "with":
            {
                // Scriban 无 with/else → $w = expr; if $w。
                // Go 的 `with $v := EXPR` **带变量声明**：必须用 $v 本身承载值
                // （原实现一律生成 $__wN，把 $v 当成了被赋值对象 → 产出
                //  `$__w1 = $terms page?.get_terms $taxonomy`，Scriban 把 $terms
                //  当函数调用报 "The function `$terms` was not found"
                //  ——hugo-book 的 post-meta.html 实测）
                var var = kb.Vars.Count > 0 ? kb.Vars[0] : $"$__w{_blockStack.Count}";
                var arg = ConvertPipelineText(kb.Pipeline, scope);

                // 记录接收者语义：with 的资源上下文内，裸方法（.GetMatch/.ByType）
                // 的隐式接收者是资源对象，转换期需补 resources 前缀
                var isResourceCtx = arg.Contains("resources.", StringComparison.Ordinal) ||
                                    arg.Contains("Resources.", StringComparison.Ordinal);
                _blockStack.Add(("with", isResourceCtx ? "resources" : null));
                scope.Add(var);
                return Wrap($"{var} = {arg}; if {var}", trimL, trimR);
            }

            case "range":
                return ConvertRange(kb, scope, trimL, trimR);

            case "define":
            {
                var name = kb.Names.Count > 0 ? kb.Names[0] : "unnamed";
                // 简单名 → capture（block 语义）；路径名 → 内联 partial（降级）
                if (name.Contains('/', StringComparison.Ordinal))
                {
                    Stats.Unsupported++;
                    Stats.Notes.Add($"内联 partial 定义 {name}（Scriban 无等价）");
                    _blockStack.Add(("skip", null));
                    return Wrap($"##TODO-HUGO(内联partial定义): define \"{name}\"## }}}}{{{{ if false", trimL, trimR);
                }
                _blockStack.Add(("define", null));
                _definedBlocks.Add("blk_" + SanitizeIdent(name));
                return Wrap("capture blk_" + SanitizeIdent(name), trimL, trimR);
            }

            case "block":
            {
                var name = kb.Names.Count > 0 ? kb.Names[0] : "unnamed";
                // extra 存**块名本身**：end 分支用它拼 `__def_<name>`（默认值变量名）。
                // 此前存 "blk_"+name，使产出的兜底变量名错为 `__def_blk_title`
                // （capture 的是 `__def_title`）→ block 默认内容丢失（mini fixture 实测）
                _blockStack.Add(("blockdef", name));
                return Wrap("capture __def_" + SanitizeIdent(name), trimL, trimR);
            }

            case "template":
            {
                // Go/Hugo 的 `template "X" CTX`：X 是 define 出来的**命名模板**
                // （或 `_internal/...` 内置模板）。命名模板已被提取为
                // `_partials/X.html`，故调用点走 partial（带上下文语义）；
                // 内置模板（`_internal/xxx`）仍是 include（命中引擎内置表）
                var name = kb.Names.Count > 0 ? kb.Names[0] : "";
                var isBuiltin = name.StartsWith("_internal/", StringComparison.OrdinalIgnoreCase);
                // `template "X" CTX` 的 CTX 是 X 内的 `.`（Hugo 语义）——
                // 丢弃它会让被调模板里的 `.Field` 落到外层 page 上（hugo-book 的
                // `template "integrity" $styles` 实测：partial 内 .RelPermalink 取不到）
                var ctx = kb.Pipeline is { Commands.Count: > 0 }
                    ? ConvertPipelineText(kb.Pipeline, scope)
                    : null;
                if (isBuiltin || name.Contains('/', StringComparison.Ordinal))
                {
                    return Wrap($"include \"{name}\"", trimL, trimR);
                }
                var templatePath = ScribanConverter.PartialPathFor(name);
                return ctx is null
                    ? Wrap($"include \"{templatePath}\"", trimL, trimR)
                    : Wrap($"partial \"{templatePath}\" {ctx}", trimL, trimR);
            }

            case "return":
            {
                // Hugo 的 return：终止 partial 并返回值（任意类型）。
                // 本文件是 partial 时改写为 store 通道（partialValue 机制）
                if (kb.Pipeline is null || kb.Pipeline.Commands.Count == 0)
                {
                    return Wrap("ret", trimL, trimR);
                }

                var retVal = ConvertPipelineText(kb.Pipeline, scope);
                // 括号包裹**仅在多参调用时**需要：store.set/ret 是函数调用形态，
                // 裸 `replace $c $a $b` 会被 Scriban 误解析（`store.set "k" replace $c $a $b`
                // → replace 收到 0 参，LoveIt function/checkbox.html 实测）。
                // ParenthesizeIfCallWithArgs 对单值（`$x`）原样返回，故不引入多余括号
                retVal = ParenthesizeIfCallWithArgs(retVal);
                var selfName = _expr.SelfPartialName;
                if (selfName is not null)
                {
                    var key = ScribanConverter.RetKeyPrefix + selfName;
                    return Wrap(
                        $"__partial_ret_set \"{key}\" {retVal} }}}}}}{{{{ ret",
                        trimL, trimR);
                }
                return Wrap($"ret {retVal}", trimL, trimR);
            }

            case "break":
                return Wrap("break", trimL, trimR);

            case "continue":
                return Wrap("continue", trimL, trimR);

            default:
                Stats.Unsupported++;
                return Wrap($"##TODO-HUGO: {kb.Name}##", trimL, trimR);
        }
    }

    /// <summary>
    /// range 转换（含双变量形态）。
    /// Scriban 限制实测：不支持 `for k, v in`；迭代 ScriptObject 产出
    /// {key, value} 对象（x[0] 取不到，须 x.key/x.value）
    /// </summary>
    private string ConvertRange(KeywordBody kb, List<string> scope, string trimL, string trimR)
    {
        var coll = ConvertPipelineText(kb.Pipeline, scope);
        // Scriban 的 `for x in f a b` 不接受裸参数列表（实测 PARSE-ERR：
        // "Invalid token found `,`. Expecting <EOL>/end of line"），
        // 含多参数函数调用时须加括号：`for x in (f a b)`
        coll = ParenthesizeIfCallWithArgs(coll);
        // 集合表达式按 Hugo 的 range 语义归一（nil/false → 空集合不迭代；
        // 标量 → 单元素）。Scriban 的 `for x in false` 会抛
        // "Unexpected type `System.Boolean` for iterator"（Blowfish 的
        // `range (or .social .links)` 两者皆空时实测 1574 处）
        coll = $"as_list ({coll})";

        // 双变量：range $k, $v := X
        if (kb.Vars.Count >= 2)
        {
            var kvar = kb.Vars[0];
            var vvar = kb.Vars[1];
            var pair = $"$__pair{_syntheticIndex++}";
            _blockStack.Add(("range", null));
            scope.Add(vvar);
            // Scriban 迭代**映射**产出 `{Key, Value}` 对象（PascalCase——`x.key`
            // 取不到，实测），迭代**序列**产出元素本身。Hugo 的双变量 range
            // 在映射上给 (key,value)、在序列上给 (index,value)，故用
            // `pair.Key ?? for.index` / `pair.Value ?? pair` 统一两种形态
            //（PaperMod 的 `range $index, $page := $paginator.Pages` 此前
            // 产出 `$page = pair.value` = null → "$page.title for a null object"）
            // 显式拼接而非插值：`}}`/`{{` 在插值串里要写成 4 个花括号，
            // 极易多写一个（历史 bug：写成 6 个 → 产出 3 个 `}`，
            // 使**每个**双变量 range 都向页面注入一个字面 `}`）
            var body = "for " + pair + " in " + coll + " }}{{ " +
                       kvar + " = " + pair + ".Key ?? for.index; " +
                       vvar + " = " + pair + ".Value ?? " + pair;
            return Wrap(body, trimL, trimR);
        }

        // 单变量：range $x := X 或 range X
        var loopVar = kb.Vars.Count == 1 ? kb.Vars[0] : $"$__it{_syntheticIndex++}";
        _blockStack.Add(("range", null));
        scope.Add(loopVar);
        return Wrap($"for {loopVar} in {coll}", trimL, trimR);
    }

    /// <summary>当前是否处于 with .Resources.* 块内（裸方法属资源接收者）</summary>
    private bool InResourceContext() =>
        _blockStack.Any(b => b.Var == "resources");

    private string ConvertExprBody(ExprBody eb, List<string> scope, string trimL, string trimR)
    {
        Stats.Expresssions++;

        var r = _expr.ConvertPipeline(eb.Pipeline, scope, InResourceContext());

        if (r.Kind == ConversionKind.Unsupported)
        {
            Stats.Unsupported++;
            var note = r.Note ?? "无法转换";
            Stats.Notes.Add(note);
            // 赋值形态降级为空值（保持变量存在，避免后续 member-of-null）
            if (eb.Vars.Count > 0)
            {
                // Hugo 的 := / = 在 Scriban 统一为 =；降级值为空串
                var assign = string.Join("; ", eb.Vars.Select(v => $"{v} = \"\""));
                return Wrap(assign, trimL, trimR);
            }
            return Wrap($"##TODO-HUGO: {Truncate(eb.Pipeline)}##", trimL, trimR);
        }

        if (r.Kind == ConversionKind.Downgraded)
        {
            Stats.Downgraded++;
            if (r.Note is not null)
            {
                Stats.Notes.Add(r.Note);
            }
        }

        // 赋值：$x := expr → $x = expr
        if (eb.Vars.Count > 0)
        {
            var assign = string.Join("; ", eb.Vars.Select(v => $"{v} = {r.Text}"));
            return Wrap(assign, trimL, trimR);
        }

        return Wrap(r.Text, trimL, trimR);
    }

    /// <summary>
    /// 集合表达式含多参数函数调用时加括号（Scriban 的 for 语法要求）。
    /// 单值/单参数/已括号化的表达式保持不变
    /// </summary>
    private static string ParenthesizeIfCallWithArgs(string expr)
    {
        var t = expr.Trim();
        if (t.Length == 0 || t.StartsWith('(') || t.StartsWith('[') || t.StartsWith('{'))
        {
            return t;
        }

        // 判断是否为 "fname a b"（有空白分隔的参数）
        var sp = t.IndexOf(' ', StringComparison.Ordinal);
        if (sp < 0)
        {
            return t;
        }

        var head = t[..sp];
        // 头部标识符（函数名）才需要括号；管道表达式分段处理
        if (!head.All(c => char.IsLetterOrDigit(c) || c is '_' or '.'))
        {
            return t;
        }

        // 含管道的表达式：只括号化首段
        var pipeIdx = t.IndexOf(" | ", StringComparison.Ordinal);
        if (pipeIdx >= 0)
        {
            var first = t[..pipeIdx];
            var rest = t[pipeIdx..];
            return "(" + first + ")" + rest;
        }

        return "(" + t + ")";
    }

    private string ConvertPipelineText(Pipeline? p, List<string> scope)
    {
        if (p is null || p.Commands.Count == 0)
        {
            return "false";
        }
        var r = _expr.ConvertPipeline(p, scope, InResourceContext());
        if (r.Kind == ConversionKind.Unsupported)
        {
            Stats.Unsupported++;
            Stats.Notes.Add(r.Note ?? "条件无法转换");
            return "false";
        }
        if (r.Kind == ConversionKind.Downgraded && r.Note is not null)
        {
            Stats.Downgraded++;
            Stats.Notes.Add(r.Note);
        }
        return r.Text;
    }

    private static string Wrap(string body, string trimL, string trimR) =>
        "{{" + trimL + " " + body.Trim() + " " + trimR + "}}";

    /// <summary>
    /// 块名 → 合法 Scriban 标识符。Hugo 允许块名含连字符（Stack 的
    /// <c>block "body-class"</c>），而 Scriban 的变量名不接受连字符——
    /// 直接拼接会产出 `__def_body-class`（`-` 被解析为减法）→
    /// "Unsupported target expression for assignment"（Stack 实测 3 处）
    /// </summary>
    private static string SanitizeIdent(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var ch in name)
        {
            sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
        }
        return sb.ToString();
    }

    private static string Truncate(Pipeline p)
    {
        var s = string.Join(" ", p.Commands.Select(c => c.ToString()));
        return s.Length <= 60 ? s : s[..60];
    }
}

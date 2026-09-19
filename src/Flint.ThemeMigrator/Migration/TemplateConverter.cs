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
internal sealed partial class TemplateConverter(
    MigrationMap map,
    IReadOnlySet<string>? valueReturningPartials = null,
    string? selfPartialName = null,
    bool baseofAvailable = false,
    bool selfNamedTemplateExtracted = false,
    IReadOnlySet<string>? slotNames = null,
    bool isBaseTemplate = false,
    IEnumerable<string>? crossFileBlockNames = null,
    string? baseofProloguePath = null,
    int baseofProloguePartCount = 0)
{
    private readonly ScribanConverter _expr =
        new(map, valueReturningPartials, selfPartialName, selfNamedTemplateExtracted);
    private readonly HashSet<string> _crossFileBlocks = new(
        crossFileBlockNames ?? [], StringComparer.Ordinal);
    private readonly List<(string Kind, string? Var)> _blockStack = [];
    private readonly List<string> _definedBlocks = [];
    private int _syntheticIndex;

    public TemplateConversionStats Stats { get; } = new();

    public IReadOnlyList<string> Diagnostics => _expr.Diagnostics;

    /// <summary>转换整个模板</summary>
    /// <summary>
    /// 把槽位默认体的 capture 提到文件最前（见 <see cref="Convert"/> 的说明）。
    /// 只处理**本文件顶层**的槽位 define；被上提的 part 索引记入 <paramref name="skip"/>
    /// </summary>
    private void HoistSlotDefaults(
        IReadOnlyList<TemplatePart> parts, StringBuilder output, HashSet<int> skip)
    {
        for (var i = 0; i < parts.Count; i++)
        {
            if (parts[i] is not ActionPart { Body: KeywordBody { Name: "define", Names.Count: > 0 } d }
                || !slotNames!.Contains(d.Names[0]))
            {
                continue;
            }

            // 收集到匹配的 end（含嵌套层数），与 InlinePartialExtractor 同一手法
            var depth = 1;
            var body = new List<TemplatePart>();
            var j = i + 1;
            while (j < parts.Count && depth > 0)
            {
                if (parts[j] is ActionPart { Body: KeywordBody inner } &&
                    inner.Name is "define" or "if" or "with" or "range" or "block")
                {
                    depth++;
                }
                else if (parts[j] is ActionPart { Body: KeywordBody { Name: "end" } })
                {
                    depth--;
                    if (depth == 0)
                    {
                        j++;
                        break;
                    }
                }

                body.Add(parts[j]);
                j++;
            }

            // 体用同一套选项转换（不含 isBaseTemplate：默认体自己不再上提）
            var innerConverter = new TemplateConverter(
                map, valueReturningPartials, selfPartialName, baseofAvailable: false,
                selfNamedTemplateExtracted, slotNames, isBaseTemplate: false);
            output.Append("{{ capture __def_")
                .Append(SanitizeIdent(d.Names[0]))
                .Append(" }}")
                .Append(innerConverter.Convert(body))
                .Append("{{ end }}");

            for (var k = i; k < j; k++)
            {
                skip.Add(k);
            }

            i = j - 1;
        }
    }

    public string Convert(IReadOnlyList<TemplatePart> parts)
    {
        var sb = new StringBuilder();
        var scope = new List<string>(); // range/with 上下文变量栈

        // **baseof 序幕**（纯副作用动作）的处理见 Convert 的收尾：页面模板先跑序幕
        // （init 链写 site.store），再捕获块体——否则块体里的 store 读取拿到空值

        // **槽位默认体上提**（hugo-book 的 baseof 形态）：`{{ template "X" . }}` 可能出现在
        // `{{ define "X" }}` **之前**，而转换把 define 就地 capture 成 `__def_X`——
        // Scriban 的 capture 是顺序执行的赋值，用在前、定义在后 → 兜底拿到空值
        //（hugo-book 的 baseof：`{{ template "menu-container" . }}` 在文件开头、
        //  define 在文件末尾 → 侧边菜单/toc/header 全部不渲染，实测）。
        // Go 模板的 define 是**解析期**注册的，与位置无关，故这里把槽位默认体统一
        // 提到文件最前面
        var hoisted = new StringBuilder();
        var hoistedParts = new HashSet<int>();
        if (isBaseTemplate && slotNames is not null)
        {
            HoistSlotDefaults(parts, hoisted, hoistedParts);
        }

        for (var i = 0; i < parts.Count; i++)
        {
            if (hoistedParts.Contains(i) || (isBaseTemplate && i < baseofProloguePartCount))
            {
                continue;
            }

            var part = parts[i];
            switch (part)
            {
                case TextPart t:
                    // 文本以 `{` 结尾且紧接动作块时补一个空格：`{` + `{{` 相邻会成为
                    // `{{{`，Scriban 解析器在此报 "Invalid token found }"（模板里的
                    // CSS/JS 花括号与 `{{ }}` 混排时高发；techdoc 的 custom-css.html
                    // `:root {` 实测）。CSS/JS/HTML 中块前的空白无副作用
                    if (t.Text.EndsWith('{') && i + 1 < parts.Count && parts[i + 1] is ActionPart)
                    {
                        sb.Append(t.Text).Append(' ');
                        break;
                    }
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

        // **页面模板先跑 baseof 序幕**：Hugo 的块体在 baseof 骨架**之内**求值
        //（baseof 顶部的 init 链先写 site.store，块体随后读），而转换把块体捕获提到
        // 了 include 之前 → 块体读 store 拿到空值（fixit 的 home.html 读
        // `.Site.Store.Get "mainSectionPages"` 得到空集 → 首页文章列表整段不渲染）。
        // 序幕是**纯副作用动作**（不定义/不引用局部变量，见 ScanBaseofPrologue），
        // 故可安全前置且只执行一次
        if (baseofProloguePath is not null && !isBaseTemplate &&
            _definedBlocks.Count > 0 && sb.Length > 0)
        {
            sb.Insert(0, $"{{{{ partial \"{baseofProloguePath}\" }}}}\n");
        }

        if (hoisted.Length > 0)
        {
            hoisted.Append(sb);
            sb = hoisted;
        }

        // 子模板块通过 include 命名参数传给 baseof（若有 define）。
        // 外壳（baseof.html）自身不 include 自己（否则自递归）：它的槽位默认体已就地
        // capture 成 __def_X，由调用点的条件输出兜底
        if (_definedBlocks.Count > 0 && !isBaseTemplate)
        {
            var pairs = string.Join(" ", _definedBlocks.Select(b => $"{b}: {b}"));
            sb.Append("\n{{ include \"baseof.html\" ").Append(pairs).Append(" }}\n");
        }
        // Hugo 的「仅用 define 触发 baseof 继承」写法：hugo-book 的 single.html/
        // list.html 全部内容只是 `{{ define "dummy" }}{{ end }}`——define 被提取为
        // 独立 partial 后本文件变空，但 Hugo 仍会渲染 baseof 骨架。不补 include 时
        // 整站每页都是空文件（hugo-book 实测：14 页合计 94 字节）
        else if (baseofAvailable && !isBaseTemplate && sb.ToString().Trim().Length == 0)
        {
            sb.Append("{{ include \"baseof.html\" }}\n");
        }

        return sb.ToString();
    }

    /// <summary>
    /// nil 安全分隔符的**调用形态**修正：Scriban 不支持 `a?.f ARG`——整个
    /// `a?.f` 会被当成函数名（"The function `(where …)?.groupbydate` was not found"）。
    /// 转换期给"接收者为括号/变量的成员访问"加 `?.` 是为了字段访问的宽容语义
    /// （`$x?.y` 在 $x 为 nil 时返回 nil），而**调用**（后跟实参）必须退回普通点。
    /// 这里对每个动作文本做一次收敛：`?.name` 之后若是**实参**（非运算符/关键字）则改点。
    /// yinyang 的 `(where …).GroupByDate "2006"` 实测 22 处
    /// </summary>
    private static readonly string[] NotArgumentKeywords =
    [
        "and", "or", "not", "if", "else", "end", "in", "with", "range", "break", "continue", "ret",
        "as", "this", "null", "true", "false"
    ];

    /// <summary>
    /// nil 安全分隔符的**调用形态**修正（深度感知）：Scriban 不支持 `a?.f ARG`——
    /// 整个 `a?.f` 会被当成函数名（"The function `(where …)?.groupbydate` was not found"）。
    /// 转换期给"接收者为括号/变量的成员访问"加 `?.` 是为了字段访问的宽容语义，而**调用**
    /// （后跟实参）必须退回普通点。
    ///
    /// 只在**动作文本的顶层**（括号/方括号/字符串之外）判定：嵌套在实参里的
    /// `where page?.data?.pages "Type"` 里的 `?.pages` 是**实参表达式**，其后随的
    /// 引号属于外层调用——按正则一刀切会把它误改（实测）
    /// </summary>
    private static string FixNilSafeCalls(string actionText)
    {
        if (!actionText.Contains("?.", StringComparison.Ordinal))
        {
            return actionText;
        }

        var sb = new StringBuilder(actionText.Length);
        var depth = 0;
        var quote = (char)0;
        var i = 0;
        while (i < actionText.Length)
        {
            var ch = actionText[i];
            if (quote != (char)0)
            {
                sb.Append(ch);
                if (ch == quote)
                {
                    quote = (char)0;
                }
                i++;
                continue;
            }
            if (ch == 34 || ch == (char)39)
            {
                quote = ch;
                sb.Append(ch);
                i++;
                continue;
            }
            if (ch is '(' or '[' or '{')
            {
                depth++;
                sb.Append(ch);
                i++;
                continue;
            }
            if (ch is ')' or ']' or '}')
            {
                depth = Math.Max(0, depth - 1);
                sb.Append(ch);
                i++;
                continue;
            }
            // nil 安全链 + 实参 = **调用**形态：Scriban **只支持单段** `x?.f ARG`。
            // 实测边界（探针）：
            //   `page?.get_page ""`            → 可用（单段 + 调用）
            //   `$x?.date.to_string "2006"`    → "The function … was not found"（多段）
            //   `(index $a 0)?.title "x"`      → 同上（括号接收者）
            // 故扫出整条链后判定：若其后确实跟实参，且【接收者是括号表达式】或【链有 ≥2 段】，
            // 就把链上的 `?.` 全部降级为 `.`（Hugo 对 nil 接收者做方法调用同样报错，
            // 语义一致；这条规则覆盖 FixIt 的
            // `(index $pages?.by_lastmod?.reverse 0)?.lastmod.format "…"`）
            if (ch == '?' && i + 1 < actionText.Length && actionText[i + 1] == '.')
            {
                var recvIsParen = i > 0 && actionText[i - 1] == ')';
                // 扫描链：?.(word)(  (?|.) (word) )*
                var segCount = 0;
                var chainEnd = i + 2;
                while (true)
                {
                    var wordEnd = ReadWordEnd(actionText, chainEnd);
                    if (wordEnd == chainEnd)
                    {
                        break;
                    }
                    segCount++;
                    chainEnd = wordEnd;
                    // 段间分隔符：普通点，或下一个 nil 安全点
                    if (chainEnd < actionText.Length && actionText[chainEnd] == '.')
                    {
                        chainEnd++;
                        continue;
                    }
                    if (chainEnd + 1 < actionText.Length && actionText[chainEnd] == '?' &&
                        actionText[chainEnd + 1] == '.')
                    {
                        chainEnd += 2;
                        continue;
                    }
                    break;
                }

                // **调用位置判据**：链后跟实参**不足以**判定它是"调用目标"——
                // `num_ge page?.config?.limit 1` 里链是**前一个函数的实参**，
                // 降级会抹掉 nil 容错（FixIt 的 `.Config.limit` 实测）。
                // 故再看链的**起始位置**前面是什么：前面是标识符/`)`/`]`/引号/点
                // 说明本链是别的调用/表达式的延续（实参位）→ 保留 `?.`；
                // 前面是行首/`(`/`|`/运算符/`=` 才是调用位置 → 降级。
                var chainStart = i;
                if (recvIsParen)
                {
                    var d2 = 0;
                    while (chainStart > 0)
                    {
                        chainStart--;
                        if (actionText[chainStart] == ')')
                        {
                            d2++;
                        }
                        else if (actionText[chainStart] == '(')
                        {
                            if (--d2 <= 0)
                            {
                                break;
                            }
                        }
                    }
                }
                else
                {
                    while (chainStart > 0 &&
                           (char.IsLetterOrDigit(actionText[chainStart - 1]) ||
                            actionText[chainStart - 1] is '_' or '$'))
                    {
                        chainStart--;
                    }
                }
                var prevNonWs = chainStart;
                while (prevNonWs > 0 && char.IsWhiteSpace(actionText[prevNonWs - 1]))
                {
                    prevNonWs--;
                }
                var prevChar = prevNonWs > 0 ? actionText[prevNonWs - 1] : (char)0;
                var atCallPosition = prevChar == (char)0 ||
                    prevChar is '(' or '|' or '=' or '+' or '-' or '*' or '/' or '!' or ',' or ':' ||
                    prevChar == 34 || prevChar == (char)39;

                if (segCount > 0 && (recvIsParen || segCount >= 2) && atCallPosition)
                {
                    var afterWs = chainEnd;
                    while (afterWs < actionText.Length && char.IsWhiteSpace(actionText[afterWs]))
                    {
                        afterWs++;
                    }
                    var nextChar = afterWs < actionText.Length ? actionText[afterWs] : (char)0;
                    var word = nextChar != (char)0 ? ReadWord(actionText, afterWs) : "";
                    var isCall = nextChar != (char)0 && IsArgumentStart(nextChar) &&
                        (word.Length == 0 || !NotArgumentKeywords.Contains(word, StringComparer.Ordinal));
                    if (isCall)
                    {
                        sb.Append(actionText[i..chainEnd].Replace("?.", ".", StringComparison.Ordinal));
                        i = chainEnd;
                        continue;
                    }
                }
            }
            sb.Append(ch);
            i++;
        }
        return sb.ToString();
    }

    private static bool IsArgumentStart(char c) =>
        char.IsLetterOrDigit(c) || c == 34 || c == (char)39 || c is '[' or '(' or '$';

    private static string ReadWord(string text, int start) => text[start..ReadWordEnd(text, start)];

    private static int ReadWordEnd(string text, int start)
    {
        var end = start;
        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_'))
        {
            end++;
        }
        return end;
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

    /// <summary>
    /// 记下槽位覆盖（页面模板里的同名 define）：捕获为 <c>blk_X</c> 并登记，
    /// 由文件末尾的 <c>include "baseof.html" blk_X: blk_X</c> 命名参数回传外壳
    /// </summary>
    private string AddSlotOverride(string name, string trimL, string trimR)
    {
        _definedBlocks.Add("blk_" + SanitizeIdent(name));
        return Wrap("capture blk_" + SanitizeIdent(name), trimL, trimR);
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
                // 槽位命名模板（多文件同名 define，hugo-book 类）：外壳里的定义是**默认体**
                //（capture __def_X，不作为块的提供者、也不触发自 include），页面模板里的
                // 定义是**覆盖**（capture blk_X，随 include 的命名参数回传外壳）。
                // 两者都就地 capture——提取到同一 partial 会互相覆盖（实测内容丢失）
                if (slotNames is not null && slotNames.Contains(name))
                {
                    return isBaseTemplate
                        ? Wrap("capture __def_" + SanitizeIdent(name), trimL, trimR)
                        : AddSlotOverride(name, trimL, trimR);
                }

                _definedBlocks.Add("blk_" + SanitizeIdent(name));
                // 块体用 `capture`（立即求值）而非 `func`（延迟求值）。
                // 曾试产 `func` 以对齐 Hugo 的求值顺序（baseof 外层先跑、块体在使用点求值），
                // 实测**不可行**：Scriban 的 func 体看不到文件顶层变量
                //（`{{ $v = "OUTER" }}{{ func f }}{{ $v }}{{ end }}{{ f }}` → 空；
                //  而 page/site 全局可见），而多个主题的子模板块体引用了顶层变量
                //（narrow 的 archives.html、github-style 的 list.html 实测回归）。
                // 代价：块体求值早于 baseof 外层副作用（FixIt 的 init 链写 site.store
                // 晚于块体读取）——该差异记录在 docs/THEME-MIGRATOR-PLAN.md 第三十二节
                return Wrap("capture blk_" + SanitizeIdent(name), trimL, trimR);
            }

            case "block":
            {
                var name = kb.Names.Count > 0 ? kb.Names[0] : "unnamed";
                // **跨文件命名模板**（partial 里 `block "X"` 指向别的 partial 的 define）：
                // Hugo 的命名模板是全局的，提取出的块体用 partial 调用来渲染；
                // 命名模板已存在，故默认体不会被用到——用 `if false` 吞掉
                //（github-style 的 user-profile.html：`block "posts"` 此前落到空兜底，
                //  整段文章列表不渲染）
                if (_crossFileBlocks.Contains(name))
                {
                    _blockStack.Add(("skip", null));
                    return Wrap(
                        $"partial \"_partials/{SanitizeIdent(name)}__block\" }}}}{{{{ if false",
                        trimL, trimR);
                }

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
                // 槽位调用（hugo-book 类的外壳：`{{ template "main" . }}`）：
                // 覆盖优先（页面模板的 capture blk_X 随 include 命名参数传进来）、
                // 默认兜底（外壳自己的 capture __def_X）。CTX 被忽略——槽位体在其
                // 定义文件的作用域里 capture，hugo-book 的调用点传的都是当前页 `.`，
                // 两者一致；若外壳传了别的上下文，该形态应改用单定义命名模板（走 partial + CTX）
                if (slotNames is not null && slotNames.Contains(name))
                {
                    // 单动作输出（`??` 空合并）：覆盖优先、外壳默认兜底。
                    // 不能输出 `{{ if blk_X }}...{{ end }}` 多动作串——Wrap 会再包一层
                    // `{{ }}`，嵌套动作被 Scriban 当对象字面量而预检失败（实测 3 处）
                    var ident = SanitizeIdent(name);
                    return Wrap($"blk_{ident} ?? __def_{ident}", trimL, trimR);
                }

                if (isBuiltin || name.Contains('/', StringComparison.Ordinal))
                {
                    return Wrap($"include \"{name}\"", trimL, trimR);
                }
                var templatePath = _expr.PartialPathFor(name);
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
                    // partial 内的提前返回改用 Flint 自有可捕获信号（`__flint_partial_return`）
                    // 的尝试**已回退**：Scriban 的 `ret` 会泄漏 FlowState 截断调用者，
                    // 但信号方案在与 Clarity 的 partialcached 链同用时使整站塌成 12 页
                    //（信号在 Clarity 的调用链里逃逸，未定位到具体路径），而 `ret` 方案下
                    // 只有 Stack 的 11 处 IndexOutOfRange（产出仍有 129KB）。
                    // 两害相权取产出量级更优者，Stack 的残留问题记录在案
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

        // **括号接收者的成员调用要提取临时变量**：Scriban 不支持 `(expr).method ARG`
        // （把 `(expr)` 整体当函数名 → "The function `(where …)` was not found"），
        // 而**变量接收者**的成员调用合法。故把 `(expr).method …` 的 `(expr)` 提到
        // `$__accN`，集合表达式改用 `$__accN.method …`
        //（yinyang 的 `range (where .Data.Pages "Type" "in" …).GroupByDate "2006"` 实测）
        var hoist = "";
        var hoisted = HoistParenReceiver(coll, ref hoist);
        if (hoisted is not null)
        {
            coll = hoisted;
        }
        // 集合归一交给单/双变量各自的分支（as_list = 值序列、as_pairs = 键值对序列）：
        // 两者的共同前提是 nil/false → 空、标量 → 单元素——Scriban 的
        // `for x in false` 会抛 "Unexpected type `System.Boolean` for iterator"
        //（Blowfish 的 `range (or .social .links)` 两者皆空时实测 1574 处）

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
            // 双变量用 **as_pairs**（Hugo v0.166 实测：映射 → (key,value)、
            // 序列 → (index,value)）。早期靠 as_list + `pair.Key ?? for.index` /
            // `pair.Value ?? pair` 兜底，而 as_list 当时把映射当**标量**（单元素）
            // → $pair 就是那个映射本身 → 主题的"递归转换 map 键"辅助函数
            //（FixIt 的 camel-case-keys.html）沿 map 无限自递归
            var body = "for " + pair + " in as_pairs (" + coll + ") }}{{ " +
                       kvar + " = " + pair + ".Key; " +
                       vvar + " = " + pair + ".Value";
            return hoist + Wrap(body, trimL, trimR);
        }

        // 单变量：range $x := X 或 range X
        var loopVar = kb.Vars.Count == 1 ? kb.Vars[0] : $"$__it{_syntheticIndex++}";
        _blockStack.Add(("range", null));
        scope.Add(loopVar);
        return hoist + Wrap($"for {loopVar} in as_list ({coll})", trimL, trimR);
    }

    /// <summary>
    /// 把集合表达式里"括号接收者的成员调用"提取为临时变量：
    /// `(expr).method ARG` → `$__accN.method ARG`（并把 `expr` 赋给 `$__accN`，
    /// 赋值动作随 range 一起返回）。Scriban 不支持对括号表达式调用成员函数
    /// （`(x).f a` 整体被当函数名）。无可提取者时返回 null
    /// </summary>
    private string? HoistParenReceiver(string coll, ref string prelude)
    {
        for (var open = 0; open < coll.Length; open++)
        {
            if (coll[open] != '(')
            {
                continue;
            }

            var depth = 0;
            var close = -1;
            for (var i = open; i < coll.Length; i++)
            {
                if (coll[i] == '(')
                {
                    depth++;
                }
                else if (coll[i] == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        close = i;
                        break;
                    }
                }
            }

            // sep: 本阶段 coll 里可能还是 `?.`（nil 安全修正发生在 Wrap 阶段，晚于此处），
            // 两种分隔符都要接受；重组时统一用普通点（调用形态不能用 `?.`）
            var sepLen = coll.Length > close + 1 && coll[close + 1] == '.'
                ? 1
                : coll.Length > close + 2 && coll[close + 1] == '?' && coll[close + 2] == '.'
                    ? 2
                    : 0;
            if (close < 0 || sepLen == 0)
            {
                continue;
            }

            var memberStart = close + 1 + sepLen;
            var memberEnd = memberStart;

            // 之后必须是"标识符 + 空白 + 实参"——否则是字段访问（`(x).count`），
            // 无需提取（Scriban 的字段访问对括号接收者合法）
            while (memberEnd < coll.Length &&
                   (char.IsLetterOrDigit(coll[memberEnd]) || coll[memberEnd] == '_'))
            {
                memberEnd++;
            }

            if (memberEnd == memberStart)
            {
                continue;
            }

            var k = memberEnd;
            while (k < coll.Length && char.IsWhiteSpace(coll[k]))
            {
                k++;
            }

            if (k >= coll.Length ||
                !(char.IsLetterOrDigit(coll[k]) || coll[k] == '"' || coll[k] == '(' || coll[k] == '$'))
            {
                continue;
            }

            var inner = coll[open..(close + 1)];
            var variable = $"$__acc{_syntheticIndex++}";
            prelude = "{{ " + variable + " = " + inner + " -}}";
            return coll[..open] + variable + "." + coll[memberStart..];
        }

        return null;
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
        // 头部标识符（函数名，或带 `$` 的**变量成员调用**）才需要括号；管道表达式分段处理。
        // `$` 必须放行：`{{ return $scratch.get "BookPages" }}` 这类变量成员调用不加括号时，
        // Scriban 把实参绑到外层函数（`__partial_ret_set "k" $scratch.get "BookPages"`
        // → 存进通道的是 get 函数本身），调用点据 token 取页面集合时报
        // "The function `$pages.Next` was not found"（hugo-book 的 prev-next.html 实测）
        if (!head.All(c => char.IsLetterOrDigit(c) || c is '_' or '.' or '$'))
        {
            return t;
        }

        // 含管道的表达式：只括号化首段。切分点必须是**括号深度 0** 的 " | "——
        // 深度盲切会把函数参数里嵌套的管道切出来：
        //   `time.Format (site.Params.dateFormat | default ":date_long") .`
        //   → 转换产物 `date.to_string page (site?... | default ":date_long")`
        //   在此处被切成 `(date.to_string page (site?...) | default ":date_long")`，
        //   default 的作用对象从**格式串**变成 date.to_string 的**结果**——
        //   nil date_format 时结果已是 ISO 串（非空），default 永不生效，
        //   blowfish 的日期卡因此在 Flint 里显示 `2026-01-15` 而 Hugo 显示
        //   `January 15, 2026`（:date_long 具名格式被吞）
        var depth = 0;
        var pipeIdx = -1;
        for (var i = 0; i < t.Length - 2; i++)
        {
            var c = t[i];
            if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}')
            {
                depth--;
            }
            else if (depth == 0 && c == ' ' && t[i + 1] == '|' && t[i + 2] == ' ')
            {
                pipeIdx = i;
                break;
            }
        }

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
        "{{" + trimL + " " + FixNilSafeCalls(body.Trim()) + " " + trimR + "}}";

    /// <summary>
    /// 块名 → 合法 Scriban 标识符。Hugo 允许块名含连字符（Stack 的
    /// <c>block "body-class"</c>），而 Scriban 的变量名不接受连字符——
    /// 直接拼接会产出 `__def_body-class`（`-` 被解析为减法）→
    /// "Unsupported target expression for assignment"（Stack 实测 3 处）
    /// </summary>
    internal static string SanitizeIdent(string name)
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

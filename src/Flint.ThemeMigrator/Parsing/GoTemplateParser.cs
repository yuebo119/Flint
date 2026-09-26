// Flint 主题迁移工具
// Go template 语法树与递归下降解析器
//
// Go template 的语法特性（关键简化）：
// **没有中缀运算符**。比较/逻辑都是前缀函数（eq a b / and x y），
// 故无需优先级表——语法只有四层：
//   Pipeline := Command ('|' Command)*
//   Command  := Operand+
//   Operand  := Term Field*          （字段链）
//   Term     := 字面量 | 变量 | 点 | 括号表达式 | 标识符
//
// 这是"可正确解析"的根本原因：Python 版用正则做**字符串切分**，
// 无法知道 `(len .Pages)` 是一个整体参数；本解析器按语法结构构建树，
// 从根本上避免 `lt (len .Pages) 10` → `(len < page.pages) 10` 这类静默损坏。

namespace Flint.ThemeMigrator.Parsing;

// ---------- 表达式 ----------

/// <summary>表达式基类</summary>
internal abstract record Expr
{
    /// <summary>源码原文（用于诊断与静默损坏检测的对照）</summary>
    public string Raw { get; init; } = "";
}

/// <summary>字段访问：.Title / .Params.author / .（裸点由 <see cref="DotExpr"/> 表示）</summary>
internal sealed record FieldExpr(string Path) : Expr;

/// <summary>变量：$x / $ / $1</summary>
internal sealed record VariableExpr(string Name) : Expr;

/// <summary>裸点（当前上下文）</summary>
internal sealed record DotExpr : Expr;

/// <summary>nil</summary>
internal sealed record NilExpr : Expr;

/// <summary>字面量（字符串/数字/布尔/字符）；Raw 保留引号</summary>
internal sealed record LiteralExpr(string Value, LiteralKind Kind) : Expr
{
    /// <summary>去掉引号的文本值</summary>
    public string Unquoted => Kind switch
    {
        LiteralKind.String when Value.Length >= 2 => Value[1..^1],
        LiteralKind.RawString when Value.Length >= 2 => Value[1..^1],
        LiteralKind.Char when Value.Length >= 2 => Value[1..^1],
        _ => Value
    };
}

internal enum LiteralKind
{
    String,
    RawString,
    Char,
    Number,
    Bool
}

/// <summary>标识符（函数名或裸标识符）</summary>
internal sealed record IdentifierExpr(string Name) : Expr;

/// <summary>括号表达式 (pipeline)</summary>
internal sealed record ParenExpr(Pipeline Inner) : Expr;

/// <summary>字段链：base.Field.Sub（如 (expr).Foo 或 $x.Foo）</summary>
internal sealed record ChainExpr(Expr Base, IReadOnlyList<string> Fields) : Expr;

/// <summary>调用：目标 + 参数（Hugo 的前缀函数形态 f a b）</summary>
internal sealed record CallExpr(Expr Target, IReadOnlyList<Pipeline> Args) : Expr;

// ---------- 管道与命令 ----------

/// <summary>命令：一个或多个操作数（首个为函数/值，其余为参数）</summary>
internal sealed record Command(IReadOnlyList<Expr> Operands)
{
    public override string ToString() => string.Join(" ", Operands.Select(o => o.ToString()));
}

/// <summary>管道：命令以 | 连接</summary>
internal sealed record Pipeline(IReadOnlyList<Command> Commands);

// ---------- 顶层结构 ----------

/// <summary>动作体</summary>
internal abstract record ActionBody;

/// <summary>关键字动作：if/with/range/define/block/template/else/end/break/continue</summary>
internal sealed record KeywordBody(
    string Name,
    Pipeline? Pipeline,
    IReadOnlyList<string> Names,
    IReadOnlyList<string> Vars,
    string? AssignOp) : ActionBody;

/// <summary>纯表达式动作（含赋值）</summary>
internal sealed record ExprBody(
    Pipeline Pipeline,
    IReadOnlyList<string> Vars,
    string? AssignOp) : ActionBody;

/// <summary>模板片段</summary>
internal abstract record TemplatePart;

internal sealed record TextPart(string Text) : TemplatePart;

internal sealed record CommentPart(string Raw) : TemplatePart;

internal sealed record ActionPart(bool TrimLeft, bool TrimRight, ActionBody Body, string Raw) : TemplatePart;

/// <summary>解析异常（带行号定位）</summary>
internal sealed class GoTemplateParseException(string message, int line)
    : Exception($"解析失败（行 {line}）: {message}")
{
    public int Line { get; } = line;
}

/// <summary>
/// Go template 解析器：Token 序列 → TemplatePart 列表。
/// 单个动作失败不阻断整体（降级为该动作的原文保留 + 诊断记录）。
/// </summary>
internal sealed class GoTemplateParser
{
    private readonly IReadOnlyList<Token> _tokens;
    private readonly List<string> _diagnostics = [];
    private int _pos;

    public GoTemplateParser(IReadOnlyList<Token> tokens) => _tokens = tokens;

    /// <summary>解析诊断（非致命问题的记录）</summary>
    public IReadOnlyList<string> Diagnostics => _diagnostics;

    /// <summary>解析整个模板</summary>
    public IReadOnlyList<TemplatePart> Parse()
    {
        var parts = new List<TemplatePart>();
        while (true)
        {
            var t = Peek();
            switch (t.Type)
            {
                case TokenType.Eof:
                    return parts;
                case TokenType.Text:
                    Next();
                    parts.Add(new TextPart(t.Value));
                    break;
                case TokenType.LeftDelim:
                    parts.Add(ParseAction());
                    break;
                // 短代码调用块：Hugo 只在**内容**里支持该语法，layouts 里 Hugo 自己报
                // `unexpected "<" in command`。这里**原样保留**并留诊断——此前按动作解析
                // 会把 `<`/`>`/`%` 悄悄吃掉，产出 `{{ sc x "1" }}body{{ sc }}` 这类
                // 静默损坏（不报错、不保留原文）
                case TokenType.ShortcodeCall:
                    _diagnostics.Add(
                        $"行 {t.Line}: 短代码调用语法（layouts 内 Hugo 自身也不支持：" +
                        "unexpected \"<\" in command）——原文保留");
                    Next();
                    parts.Add(new TextPart(t.Value));
                    break;
                default:
                    // 动作外出现非文本 token：容错跳过（记录诊断）
                    _diagnostics.Add($"行 {t.Line}: 动作外出现 {t.Type}（已跳过）");
                    Next();
                    break;
            }
        }
    }

    /// <summary>解析一个完整动作：{{ ... }}</summary>
    private TemplatePart ParseAction()
    {
        var open = Next(); // LeftDelim
        var trimLeft = open.Value.Length > 0 && open.Value[^1] == '-';
        var startLine = open.Line;
        var rawStart = _pos;

        // 收集动作内 token（到 RightDelim 或 Eof）
        var bodyTokens = new List<Token>();
        var trimRight = false;
        while (true)
        {
            var t = Peek();
            if (t.Type == TokenType.Eof)
            {
                _diagnostics.Add($"行 {startLine}: 动作未闭合");
                break;
            }
            if (t.Type == TokenType.RightDelim)
            {
                Next();
                // 右裁剪标记并入定界 token 值（"-}}"，见 LexRightDelim）；
                // Next() 之后该 token 位于 _pos-1
                trimRight = _pos >= 1 && _tokens[_pos - 1].Value.StartsWith('-');
                break;
            }
            bodyTokens.Add(t);
            Next();
        }

        var raw = string.Concat(bodyTokens.Select(b => b.Value));
        _ = rawStart;

        // 纯注释动作 {{/* ... */}}：作为注释处理（不进表达式转换）
        if (bodyTokens.Count > 0 && bodyTokens.All(b => b.Type is TokenType.Comment or TokenType.Space))
        {
            var commentText = string.Concat(
                bodyTokens.Where(b => b.Type == TokenType.Comment).Select(b => b.Value));
            return new CommentPart(commentText);
        }

        var body = ParseBody(bodyTokens, startLine);
        return new ActionPart(trimLeft, trimRight, body, raw);
    }

    /// <summary>动作体解析：关键字 / 赋值 / 表达式</summary>
    private ActionBody ParseBody(List<Token> tokens, int line)
    {
        // 保留 Space token（ParseOperands 靠它区分 `strings.ToUpper` 与 `eq .Kind`），
        // 仅剔除首尾空白
        var meaningful = new List<Token>(tokens);
        while (meaningful.Count > 0 && meaningful[0].Type == TokenType.Space)
        {
            meaningful.RemoveAt(0);
        }
        while (meaningful.Count > 0 && meaningful[^1].Type == TokenType.Space)
        {
            meaningful.RemoveAt(meaningful.Count - 1);
        }

        if (meaningful.Count == 0)
        {
            return new ExprBody(new Pipeline([]), [], null);
        }

        var first = meaningful[0];

        // 关键字分派（传过滤掉空白的剩余列表）
        if (first.Type == TokenType.Identifier && IsKeyword(first.Value))
        {
            // 保留 Space（ParseOperands 靠它区分命名空间调用与函数+参数）
            var rest = meaningful.Skip(1).ToList();
            return ParseKeyword(first.Value, rest, line);
        }

        // 赋值形态：$x := / $x = / $x, $y :=
        var assignOpIdx = meaningful.FindIndex(t => t.Type is TokenType.Declare or TokenType.Assign);
        if (assignOpIdx > 0 && meaningful.Take(assignOpIdx)
                .All(t => t.Type is TokenType.Variable or TokenType.Char or TokenType.Space))
        {
            var vars = meaningful.Take(assignOpIdx)
                .Where(t => t.Type == TokenType.Variable)
                .Select(t => t.Value)
                .ToList();
            var op = meaningful[assignOpIdx].Type == TokenType.Declare ? ":=" : "=";
            var pipeline = ParsePipeline(meaningful.Skip(assignOpIdx + 1).ToList(), line);
            return new ExprBody(pipeline, vars, op);
        }

        return new ExprBody(ParsePipeline(meaningful, line), [], null);
    }

    private static bool IsKeyword(string word) =>
        word is "if" or "else" or "end" or "range" or "with" or "define" or "block" or "template"
            or "break" or "continue" or "return";

    private ActionBody ParseKeyword(string name, List<Token> rest, int line)
    {
        // 剔除首尾空白（内部空白保留给 ParseOperands）
        while (rest.Count > 0 && rest[0].Type == TokenType.Space)
        {
            rest.RemoveAt(0);
        }
        while (rest.Count > 0 && rest[^1].Type == TokenType.Space)
        {
            rest.RemoveAt(rest.Count - 1);
        }

        // end / else / break / continue：可能无参数
        if (rest.Count == 0)
        {
            return new KeywordBody(name, null, [], [], null);
        }

        // define / block：后跟名字字符串
        if (name is "define" or "block")
        {
            var names = rest
                .Where(t => t.Type is TokenType.String or TokenType.RawString)
                .Select(t => t.Value.Trim('"', '`'))
                .ToList();
            return new KeywordBody(name, null, names, [], null);
        }

        // template "name" [CTX]
        if (name == "template")
        {
            var nameIdx = rest.FindIndex(t => t.Type is TokenType.String or TokenType.RawString);
            if (nameIdx < 0)
            {
                return new KeywordBody(name, null, [], [], null);
            }

            var names = new List<string> { rest[nameIdx].Value.Trim('"', '`') };
            // CTX 是**名字之后**的全部 token——按"位置"切分而非按类型过滤：
            // 按类型过滤会把上下文里的字符串（`(dict "currentnode" …)` 的键）
            // 一并删掉，dict 变成奇数参数 → 整体判 Unsupported 并产出 `false`
            var ctxTokens = rest.Skip(nameIdx + 1).ToList();
            while (ctxTokens.Count > 0 && ctxTokens[0].Type == TokenType.Space)
            {
                ctxTokens.RemoveAt(0);
            }
            var ctxPipeline = ctxTokens.Count > 0 ? ParsePipeline(ctxTokens, line) : null;
            return new KeywordBody(name, ctxPipeline, names, [], null);
        }

        // else if ...
        if (name == "else" && rest[0].Type == TokenType.Identifier && rest[0].Value == "if")
        {
            return new KeywordBody("else", ParsePipeline(rest.Skip(1).ToList(), line), [], [], null);
        }

        // range $k, $v := X  /  with $v := X（Go 允许 with 带变量声明，
        // 变量声明必须解析出来，否则转换器会把变量名当成被赋值对象
        // ——hugo-book 的 `with $terms := $.GetTerms $taxonomy` 实测：
        //  产出 `$__w1 = $terms page?.get_terms …`，$terms 被当函数调用报错）
        if (name is "range" or "with")
        {
            var opIdx = rest.FindIndex(t => t.Type is TokenType.Declare or TokenType.Assign);
            if (opIdx > 0)
            {
                var vars = rest.Take(opIdx)
                    .Where(t => t.Type == TokenType.Variable)
                    .Select(t => t.Value)
                    .ToList();
                var op = rest[opIdx].Type == TokenType.Declare ? ":=" : "=";
                var pipeline = ParsePipeline(rest.Skip(opIdx + 1).ToList(), line);
                return new KeywordBody(name, pipeline, [], vars, op);
            }
        }

        // return [pipeline]：Hugo 的返回值语句（可无参 = 提前退出）
        if (name == "return")
        {
            return new KeywordBody(name, ParsePipeline(rest, line), [], [], null);
        }

        // if / with / range（无变量）
        return new KeywordBody(name, ParsePipeline(rest, line), [], [], null);
    }

    /// <summary>
    /// 解析管道：命令以 | 分隔。
    /// **仅在括号深度 0 处切分**——括号内的 | 属于内层管道，须交给
    /// ParenExpr 内部递归解析。此前不跟踪深度，`slice "a" (X | default "y") $z`
    /// 会从内层 | 处断开，产出 `["a", (X)] | default "y" $z`（数组提前闭合，
    /// 语义完全错乱——Ananke baseof 的 body_classes 实测）
    /// </summary>
    private Pipeline ParsePipeline(List<Token> tokens, int line)
    {
        var commands = new List<Command>();
        var current = new List<Token>();
        var depth = 0;

        foreach (var t in tokens)
        {
            if (t.Type == TokenType.LeftParen)
            {
                depth++;
            }
            else if (t.Type == TokenType.RightParen && depth > 0)
            {
                depth--;
            }

            if (t.Type == TokenType.Pipe && depth == 0)
            {
                if (current.Count > 0)
                {
                    commands.Add(new Command(ParseOperands(current, line)));
                    current = [];
                }
                continue;
            }
            current.Add(t);
        }

        if (current.Count > 0)
        {
            commands.Add(new Command(ParseOperands(current, line)));
        }

        return new Pipeline(commands);
    }

    /// <summary>
    /// 解析命令的操作数列表。
    /// Go 的命令是"操作数序列"（f a b 的第一个操作数是函数名，其余是参数），
    /// 但括号会把内部包成一个操作数——这是正确处理嵌套的关键。
    /// </summary>
    private List<Expr> ParseOperands(List<Token> tokens, int line)
    {
        var operands = new List<Expr>();
        var i = 0;
        while (i < tokens.Count)
        {
            var t = tokens[i];

            // 空白：跳过（不产出操作数，仅用于相邻性判断）
            if (t.Type == TokenType.Space)
            {
                i++;
                continue;
            }

            // 括号表达式
            if (t.Type == TokenType.LeftParen)
            {
                var depth = 0;
                var inner = new List<Token>();
                i++;
                while (i < tokens.Count)
                {
                    if (tokens[i].Type == TokenType.LeftParen)
                    {
                        depth++;
                    }
                    else if (tokens[i].Type == TokenType.RightParen)
                    {
                        if (depth == 0)
                        {
                            break;
                        }
                        depth--;
                    }
                    inner.Add(tokens[i]);
                    i++;
                }
                i++; // 跳过右括号
                var paren = new ParenExpr(ParsePipeline(inner, line));
                operands.Add(WithChain(paren, tokens, ref i));
                continue;
            }

            // 字段：.Path 或 .（裸点）
            if (t.Type == TokenType.Field)
            {
                operands.Add(new FieldExpr(t.Value) { Raw = t.Value });
                i++;
                continue;
            }
            if (t.Type == TokenType.Dot)
            {
                i++; // 消费 Dot 本身
                operands.Add(WithChain(new DotExpr { Raw = "." }, tokens, ref i));
                continue;
            }

            // 变量：$x（可能后跟 .Field 链）
            if (t.Type == TokenType.Variable)
            {
                i++; // 消费变量本身
                // 变量后紧跟字段且作为命令首操作数：合并为点名（$.Param / $x.Method），
                // 使转换器能按完整名判定（Hugo 的 $.Param 需特殊映射）
                if (i < tokens.Count && tokens[i].Type == TokenType.Field)
                {
                    var vname = t.Value;
                    while (i < tokens.Count && tokens[i].Type == TokenType.Field)
                    {
                        vname += tokens[i].Value;
                        i++;
                    }
                    operands.Add(new IdentifierExpr(vname) { Raw = vname });
                    continue;
                }
                operands.Add(WithChain(new VariableExpr(t.Value) { Raw = t.Value }, tokens, ref i));
                continue;
            }

            // 字面量
            if (t.Type is TokenType.String or TokenType.RawString or TokenType.CharConstant
                or TokenType.Number or TokenType.Bool)
            {
                operands.Add(new LiteralExpr(t.Value, MapLiteralKind(t.Type)) { Raw = t.Value });
                i++;
                continue;
            }

            // nil
            if (t.Type == TokenType.Nil)
            {
                operands.Add(new NilExpr { Raw = "nil" });
                i++;
                continue;
            }

            // 标识符（函数名）：仅当**紧随其后无空白**的 Field 才合并为点路径。
            // 命名空间调用（strings.ToUpper）在词法上与 `eq .Kind` 的区别正是空格：
            //   strings.ToUpper  → Identifier + Field（无空格，同一调用目标）
            //   eq .Kind         → Identifier + Space + Field（函数名 + 参数）
            // 此前无空格判断，把 `eq .Kind` 误并成 `eq.Kind`（测试实测）
            if (t.Type == TokenType.Identifier)
            {
                var name = t.Value;
                i++;
                var noSpaceBeforeField = i < tokens.Count && tokens[i].Type == TokenType.Field;
                if (noSpaceBeforeField)
                {
                    while (i < tokens.Count && tokens[i].Type == TokenType.Field)
                    {
                        name += tokens[i].Value;
                        i++;
                    }
                }
                operands.Add(new IdentifierExpr(name) { Raw = name });
                continue;
            }

            // 其他（逗号等）：记录诊断并跳过
            _diagnostics.Add($"行 {t.Line}: 无法解析的 token {t.Type}='{t.Value}'（已跳过）");
            i++;
        }

        return operands;
    }

    /// <summary>
    /// 消费 base 之后紧跟的字段链（.Foo.Bar）。
    /// 字段 token 含点（.Foo），直接拼进链。
    /// </summary>
    /// <summary>
    /// 消费 base 之后紧跟的字段链（.Foo 形式）。
    /// 调用方约定：i 已指向 base 之后的位置，本方法只消费 Field token。
    /// （此前实现会额外 i++ 跳过一个 base，导致 `f (x) "y"` 丢失最后的 "y"）
    /// </summary>
    private static Expr WithChain(Expr baseExpr, List<Token> tokens, ref int i)
    {
        var fields = new List<string>();
        while (i < tokens.Count && tokens[i].Type == TokenType.Field)
        {
            fields.Add(tokens[i].Value);
            i++;
        }

        return fields.Count == 0
            ? baseExpr
            : new ChainExpr(baseExpr, fields) { Raw = baseExpr.Raw + string.Concat(fields) };
    }

    private static LiteralKind MapLiteralKind(TokenType type) => type switch
    {
        TokenType.String => LiteralKind.String,
        TokenType.RawString => LiteralKind.RawString,
        TokenType.CharConstant => LiteralKind.Char,
        TokenType.Number => LiteralKind.Number,
        _ => LiteralKind.Bool
    };

    private Token Peek() => _pos < _tokens.Count ? _tokens[_pos] : _tokens[^1];

    private Token Next() => _pos < _tokens.Count ? _tokens[_pos++] : _tokens[^1];
}

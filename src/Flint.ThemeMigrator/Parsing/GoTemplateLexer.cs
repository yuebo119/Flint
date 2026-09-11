// Flint 主题迁移工具
// Go template 词法器（移植 Go 标准库 text/template/parse/lex.go）
//
// 移植理由（实测依据）：现有 Python 转换器用 `re.finditer(r"\{\{.*?\}\}")` 切分，
// 在嵌套/引号场景必然出错——
//   {{ printf "}}" }}   →  正则切在引号内，残渣 "}}" 泄漏成正文（静默损坏）
// Go 的 lexer 按状态机扫描，能正确处理引号、反引号、注释与括号嵌套。
//
// 与 Go 版本的差异（有意简化，对本工具无影响）：
// - 不实现 itemComment 的 emitComment 选项（本工具需要注释内容）
// - 不实现 break/continue 的 lexOptions 门控（统一允许，由转换期判定）
// - 位置信息只保留行号（错误定位够用）

using System.Globalization;
using System.Text;

namespace Flint.ThemeMigrator.Parsing;

/// <summary>词法单元类型（对照 Go 的 itemType）</summary>
internal enum TokenType
{
    Error,
    Text,          // 动作外的纯文本
    LeftDelim,     // {{
    RightDelim,    // }}
    Identifier,    // 不以 . 开头的标识符（函数名/关键字）
    Field,         // 以 . 开头的字段路径（.Params.Title）
    Variable,      // $ 开头（$ / $1 / $hello）
    Dot,           // 单独的 .
    Number,        // 数字
    String,        // 双引号字符串（含引号）
    RawString,     // 反引号字符串（含反引号）
    CharConstant,  // 单引号字符常量
    Bool,          // true / false
    Nil,           // nil
    Assign,        // =
    Declare,       // :=
    Pipe,          // |
    LeftParen,     // (
    RightParen,    // )
    Space,         // 空白串
    Comment,       // /* ... */
    Char,          // 其他可打印字符（逗号等）
    Eof,
}

/// <summary>词法单元</summary>
/// <param name="Type">类型</param>
/// <param name="Value">原文（含引号的字符串保留引号，便于原样回写）</param>
/// <param name="Line">1 起的行号</param>
internal readonly record struct Token(TokenType Type, string Value, int Line)
{
    public override string ToString() => $"{Type}:{Value}";
}

/// <summary>
/// Go template 词法器。
/// 扫描输入串，产出 Token 序列；对引号/反引号/注释/括号做状态化处理。
/// </summary>
internal sealed class GoTemplateLexer
{
    private const char EofChar = '\uFFFF';
    private const string SpaceChars = " \t\r\n";

    private static readonly Dictionary<string, TokenType> Keywords = new(StringComparer.Ordinal)
    {
        // 关键字（Go lex.go 的 key 表）——block/define/end/if/range/with/template
        // 在转换期需要识别结构；break/continue/nil 保留类型
        ["block"] = TokenType.Identifier,
        ["define"] = TokenType.Identifier,
        ["else"] = TokenType.Identifier,
        ["end"] = TokenType.Identifier,
        ["if"] = TokenType.Identifier,
        ["range"] = TokenType.Identifier,
        ["with"] = TokenType.Identifier,
        ["template"] = TokenType.Identifier,
        ["break"] = TokenType.Identifier,
        ["continue"] = TokenType.Identifier,
        // Hugo 的 return（Go 标准库的 key 表无此项，是 Hugo 模板引擎扩展）：
        // 不识别会被当普通标识符，产出 "IdentifierExpr { Raw = return }" 这类 TODO
        ["return"] = TokenType.Identifier,
        ["nil"] = TokenType.Nil,
        ["true"] = TokenType.Bool,
        ["false"] = TokenType.Bool,
    };

    private readonly string _input;
    private readonly string _leftDelim;
    private readonly string _rightDelim;
    private readonly List<Token> _tokens = [];
    private int _pos;
    private int _line = 1;
    private bool _insideAction;
    private int _parenDepth;

    public GoTemplateLexer(string input, string leftDelim = "{{", string rightDelim = "}}")
    {
        _input = input ?? "";
        _leftDelim = leftDelim;
        _rightDelim = rightDelim;
    }

    /// <summary>执行词法分析，返回 Token 列表（含末尾 Eof）</summary>
    public IReadOnlyList<Token> Tokenize()
    {
        while (_pos < _input.Length)
        {
            if (_insideAction)
            {
                LexInsideAction();
            }
            else
            {
                LexText();
            }
        }

        _tokens.Add(new Token(TokenType.Eof, "", _line));
        return _tokens;
    }

    private char Peek() => _pos < _input.Length ? _input[_pos] : EofChar;

    private char Next()
    {
        if (_pos >= _input.Length)
        {
            return EofChar;
        }

        var c = _input[_pos++];
        if (c == '\n')
        {
            _line++;
        }
        return c;
    }

    private void Backup()
    {
        if (_pos > 0)
        {
            _pos--;
            if (_input[_pos] == '\n')
            {
                _line--;
            }
        }
    }

    private void Emit(TokenType type, int start, int startLine)
    {
        var value = _input[start.._pos];
        _tokens.Add(new Token(type, value, startLine));
    }

    private void Emit(TokenType type, string value, int line) =>
        _tokens.Add(new Token(type, value, line));

    /// <summary>
    /// 动作外文本：扫描到下一个左定界符。
    /// 处理 {{- 的左裁剪语义（Go 的 trim marker）
    /// </summary>
    private void LexText()
    {
        var start = _pos;
        var startLine = _line;
        var idx = _input.IndexOf(_leftDelim, _pos, StringComparison.Ordinal);
        if (idx < 0)
        {
            // 无更多动作：剩余全是文本
            while (_pos < _input.Length)
            {
                Next();
            }
            if (_pos > start)
            {
                Emit(TokenType.Text, start, startLine);
            }
            return;
        }

        // 是否紧跟左裁剪标记（{{- ）
        var afterDelim = idx + _leftDelim.Length;
        var hasLeftTrim = afterDelim < _input.Length
                          && _input[afterDelim] == '-'
                          && afterDelim + 1 < _input.Length
                          && SpaceChars.Contains(_input[afterDelim + 1], StringComparison.Ordinal);

        var end = idx;
        if (hasLeftTrim)
        {
            // 从 end 往前吃掉空白
            while (end > start && SpaceChars.Contains(_input[end - 1], StringComparison.Ordinal))
            {
                end--;
            }
        }

        _pos = end;
        if (end > start)
        {
            Emit(TokenType.Text, start, startLine);
        }

        _pos = idx;
        LexLeftDelim();
    }

    /// <summary>左定界符：处理 {{ / {{- 与 {{/* 注释起点</summary>
    private void LexLeftDelim()
    {
        var start = _pos;
        var startLine = _line;
        while (_pos < _input.Length && _pos - start < _leftDelim.Length)
        {
            Next();
        }

        _insideAction = true;
        _parenDepth = 0;

        // {{- 形式
        var isTrimLeft = _pos < _input.Length
                         && _input[_pos] == '-'
                         && _pos + 1 < _input.Length
                         && SpaceChars.Contains(_input[_pos + 1], StringComparison.Ordinal);
        if (isTrimLeft)
        {
            Next();
        }

        // 跳过左裁剪标记后的空白，再判定注释（`{{- /* ... */ -}}` 形态）
        var probe = _pos;
        while (probe < _input.Length && _input[probe] == ' ')
        {
            probe++;
        }

        // 注释起始 {{/* ... */}}
        if (probe + 1 < _input.Length && _input[probe] == '/' && _input[probe + 1] == '*')
        {
            Emit(TokenType.LeftDelim, start, startLine);
            _pos = probe;
            LexComment();
            return;
        }

        Emit(TokenType.LeftDelim, start, startLine);
    }

    /// <summary>注释：扫描到 */ 再吃掉可选右定界符</summary>
    private void LexComment()
    {
        var start = _pos;
        var startLine = _line;
        Next(); // /
        Next(); // *

        var end = _input.IndexOf("*/", _pos, StringComparison.Ordinal);
        if (end < 0)
        {
            // 未闭合：吞到末尾
            while (_pos < _input.Length)
            {
                Next();
            }
            Emit(TokenType.Comment, start, startLine);
            _insideAction = false;
            return;
        }

        while (_pos < end + 2)
        {
            Next();
        }
        Emit(TokenType.Comment, start, startLine);

        // 注释可自带右裁剪与右定界符
        if (_pos < _input.Length && _input[_pos] == '-')
        {
            Next();
        }
        if (_pos + 1 < _input.Length && _input[_pos] == '}' && _input[_pos + 1] == '}')
        {
            Next();
            Next();
            Emit(TokenType.RightDelim, "}}", _line);
            _insideAction = false;
        }
    }

    /// <summary>动作内：按首字符分派到具体扫描器</summary>
    private void LexInsideAction()
    {
        if (_parenDepth == 0 && AtRightDelim())
        {
            LexRightDelim();
            return;
        }

        var c = Peek();
        var startLine = _line;

        if (c == EofChar)
        {
            return;
        }

        if (SpaceChars.Contains(c, StringComparison.Ordinal))
        {
            LexSpace();
            return;
        }

        switch (c)
        {
            case '=':
                Next();
                Emit(TokenType.Assign, "=", startLine);
                return;
            case ':':
                Next();
                if (Peek() == '=')
                {
                    Next();
                    Emit(TokenType.Declare, ":=", startLine);
                }
                else
                {
                    Emit(TokenType.Error, "期望 :=", startLine);
                }
                return;
            case '|':
                Next();
                Emit(TokenType.Pipe, "|", startLine);
                return;
            case '"':
                LexQuote();
                return;
            case '`':
                LexRawQuote();
                return;
            case '\'':
                LexCharConstant();
                return;
            case '$':
                LexVariable();
                return;
            case '.':
                // .field（后跟非数字）或数字小数
                if (_pos + 1 < _input.Length && !char.IsAsciiDigit(_input[_pos + 1]))
                {
                    LexField();
                    return;
                }
                LexNumber();
                return;
            case '(':
                Next();
                _parenDepth++;
                Emit(TokenType.LeftParen, "(", startLine);
                return;
            case ')':
                Next();
                _parenDepth--;
                Emit(TokenType.RightParen, ")", startLine);
                return;
        }

        if (c is '+' or '-' || char.IsAsciiDigit(c))
        {
            LexNumber();
            return;
        }

        if (IsAlphaNumeric(c))
        {
            LexIdentifier();
            return;
        }

        // 其他可打印字符（逗号、比较符等）
        Next();
        Emit(TokenType.Char, c.ToString(), startLine);
    }

    private bool AtRightDelim()
    {
        var p = _pos;
        // 右裁剪形式 "-}}"
        if (p < _input.Length && _input[p] == '-')
        {
            p++;
        }
        return _input.AsSpan(p).StartsWith(_rightDelim, StringComparison.Ordinal);
    }

    private void LexRightDelim()
    {
        var startLine = _line;
        // 可选右裁剪标记
        if (Peek() == '-')
        {
            Next();
        }
        // 吃掉右定界符字符
        for (var i = 0; i < _rightDelim.Length && _pos < _input.Length; i++)
        {
            Next();
        }
        Emit(TokenType.RightDelim, _rightDelim, startLine);
        _insideAction = false;
        _parenDepth = 0;
    }

    private void LexSpace()
    {
        var start = _pos;
        var startLine = _line;
        while (_pos < _input.Length && SpaceChars.Contains(_input[_pos], StringComparison.Ordinal))
        {
            Next();
        }
        Emit(TokenType.Space, start, startLine);
    }

    /// <summary>双引号字符串：处理 \ 转义</summary>
    private void LexQuote()
    {
        var start = _pos;
        var startLine = _line;
        Next(); // 开引号
        while (true)
        {
            var c = Next();
            if (c == EofChar || c == '\n')
            {
                Emit(TokenType.Error, "未闭合的字符串", startLine);
                return;
            }
            if (c == '\\')
            {
                Next(); // 转义下一字符
                continue;
            }
            if (c == '"')
            {
                break;
            }
        }
        Emit(TokenType.String, start, startLine);
    }

    /// <summary>反引号原始字符串：不处理转义</summary>
    private void LexRawQuote()
    {
        var start = _pos;
        var startLine = _line;
        Next();
        while (true)
        {
            var c = Next();
            if (c == EofChar)
            {
                Emit(TokenType.Error, "未闭合的原始字符串", startLine);
                return;
            }
            if (c == '`')
            {
                break;
            }
        }
        Emit(TokenType.RawString, start, startLine);
    }

    /// <summary>字符常量 'x' 或 '\n'</summary>
    private void LexCharConstant()
    {
        var start = _pos;
        var startLine = _line;
        Next(); // '
        while (true)
        {
            var c = Next();
            if (c == EofChar || c == '\n')
            {
                Emit(TokenType.Error, "未闭合的字符常量", startLine);
                return;
            }
            if (c == '\\')
            {
                Next();
                continue;
            }
            if (c == '\'')
            {
                break;
            }
        }
        Emit(TokenType.CharConstant, start, startLine);
    }

    /// <summary>$ 变量：$ / $1 / $name</summary>
    private void LexVariable()
    {
        var start = _pos;
        var startLine = _line;
        Next(); // $
        LexFieldOrVariable(TokenType.Variable, start, startLine);
    }

    /// <summary>. 字段：. / .Field.Sub</summary>
    private void LexField()
    {
        var start = _pos;
        var startLine = _line;
        LexFieldOrVariable(TokenType.Field, start, startLine);
    }

    /// <summary>
    /// 字段/变量共用扫描：字母数字（含 Unicode）与点、下划线、数字延续。
    /// Go 语义：$ 与 . 后可跟字母数字序列（.Params.Title 整段一个 token）
    /// </summary>
    private void LexFieldOrVariable(TokenType type, int start, int startLine)
    {
        // 字段（.）消费点以支持 .Params.Title 单 token；变量（$x）不消费点，
        // 使 $x.Method 成为 Variable + Field 两个 token（对齐 Go 的 chain 语义）
        var allowDot = type == TokenType.Field;
        while (true)
        {
            var c = Peek();
            if (c == EofChar || !(IsAlphaNumeric(c) || (allowDot && c == '.')))
            {
                break;
            }
            Next();
        }

        // 单独的 "." 识别为 Dot（Go 的 itemDot）
        if (type == TokenType.Field && _input.AsSpan(start, _pos - start) is ".")
        {
            Emit(TokenType.Dot, ".", startLine);
            return;
        }

        Emit(type, start, startLine);
    }

    /// <summary>标识符/关键字：字母开头，字母数字下划线延续</summary>
    private void LexIdentifier()
    {
        var start = _pos;
        var startLine = _line;
        while (true)
        {
            var c = Peek();
            if (c == EofChar || !(IsAlphaNumeric(c) || c == '_'))
            {
                break;
            }
            Next();
        }

        var word = _input[start.._pos];
        if (Keywords.TryGetValue(word, out var keywordType))
        {
            Emit(keywordType, start, startLine);
            return;
        }

        Emit(TokenType.Identifier, start, startLine);
    }

    /// <summary>数字：整数/小数/十六进制/虚数（虚数退化为 Identifier 后缀处理）</summary>
    private void LexNumber()
    {
        var start = _pos;
        var startLine = _line;
        var isHex = false;
        var hasDecimal = false;

        if (Peek() is '+' or '-')
        {
            Next();
        }

        if (Peek() == '0' && (_pos + 1 < _input.Length && (_input[_pos + 1] is 'x' or 'X')))
        {
            Next();
            Next();
            isHex = true;
        }

        while (true)
        {
            var c = Peek();
            if (isHex && Uri.IsHexDigit(c))
            {
                Next();
                continue;
            }
            if (char.IsAsciiDigit(c))
            {
                Next();
                continue;
            }
            if (c == '.' && !hasDecimal)
            {
                hasDecimal = true;
                Next();
                continue;
            }
            if (c is 'e' or 'E' && !isHex)
            {
                Next();
                if (Peek() is '+' or '-')
                {
                    Next();
                }
                continue;
            }
            break;
        }

        Emit(TokenType.Number, start, startLine);
    }

    private static bool IsAlphaNumeric(char c) =>
        char.IsLetterOrDigit(c) || c == '_' || c > 127;
}

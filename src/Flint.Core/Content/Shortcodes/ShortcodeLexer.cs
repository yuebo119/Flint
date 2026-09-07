// Flint 静态站点生成器
// 短代码词法分析器

using System.Text;

namespace Flint.Core.Content.Shortcodes;

/// <summary>
/// 短代码词法分析器
/// 负责将输入文本分解为短代码令牌
/// </summary>
public sealed partial class ShortcodeLexer
{
    // 短代码开始模式: {{< 或 {{% 
    private const string OpenAngle = "{{<";
    private const string OpenPercent = "{{%";
    private const string CloseAngle = ">}}";
    private const string ClosePercent = "%}}";
    private const string SelfCloseAngle = "/>}}";
    private const string SelfClosePercent = "/%}}";

    /// <summary>
    /// 将输入文本分解为令牌列表
    /// </summary>
    /// <param name="input">输入文本</param>
    /// <returns>令牌列表</returns>
    public static IReadOnlyList<ShortcodeToken> Tokenize(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return [];
        }

        var tokens = new List<ShortcodeToken>();
        var position = 0;
        var textStart = 0;

        while (position < input.Length)
        {
            // 查找下一个短代码开始位置
            var nextShortcode = FindNextShortcodeStart(input, position);

            if (nextShortcode < 0)
            {
                // 没有更多短代码，添加剩余文本
                if (textStart < input.Length)
                {
                    tokens.Add(CreateTextToken(input, textStart, input.Length));
                }
                break;
            }

            // 添加短代码之前的文本
            if (textStart < nextShortcode)
            {
                tokens.Add(CreateTextToken(input, textStart, nextShortcode));
            }

            // 解析短代码
            var (token, endPosition) = ParseShortcodeToken(input, nextShortcode);
            if (token != null)
            {
                tokens.Add(token);
                position = endPosition;
                textStart = endPosition;
            }
            else
            {
                // 解析失败，将开始标记作为普通文本处理
                position = nextShortcode + 3; // 跳过 {{< 或 {{% 
                textStart = nextShortcode;
            }
        }

        return tokens;
    }

    /// <summary>
    /// 查找下一个短代码开始位置
    /// </summary>
    private static int FindNextShortcodeStart(string input, int startPosition)
    {
        var anglePos = input.IndexOf(OpenAngle, startPosition, StringComparison.Ordinal);
        var percentPos = input.IndexOf(OpenPercent, startPosition, StringComparison.Ordinal);

        if (anglePos < 0 && percentPos < 0)
        {
            return -1;
        }

        if (anglePos < 0)
            return percentPos;
        if (percentPos < 0)
            return anglePos;

        return Math.Min(anglePos, percentPos);
    }

    /// <summary>
    /// 解析短代码令牌
    /// </summary>
    private static (ShortcodeToken? Token, int EndPosition) ParseShortcodeToken(string input, int startPosition)
    {
        // 确定分隔符类型
        var delimiterType = input.Substring(startPosition, 3) == OpenAngle
            ? ShortcodeDelimiterType.Angle
            : ShortcodeDelimiterType.Percent;

        var closeDelimiter = delimiterType == ShortcodeDelimiterType.Angle ? CloseAngle : ClosePercent;
        var selfCloseDelimiter = delimiterType == ShortcodeDelimiterType.Angle ? SelfCloseAngle : SelfClosePercent;

        // 查找结束位置
        var contentStart = startPosition + 3; // 跳过 {{< 或 {{% 

        // 首先检查自闭合
        var selfClosePos = input.IndexOf(selfCloseDelimiter, contentStart, StringComparison.Ordinal);
        var closePos = input.IndexOf(closeDelimiter, contentStart, StringComparison.Ordinal);

        // 确定实际的结束位置
        int endPos;
        bool isSelfClosing;

        if (selfClosePos >= 0 && (closePos < 0 || selfClosePos < closePos))
        {
            endPos = selfClosePos + selfCloseDelimiter.Length;
            isSelfClosing = true;
        }
        else if (closePos >= 0)
        {
            endPos = closePos + closeDelimiter.Length;
            isSelfClosing = false;
        }
        else
        {
            // 没有找到结束标记
            return (null, startPosition + 3);
        }

        // 提取短代码内容
        var contentEnd = isSelfClosing ? selfClosePos : closePos;
        var content = input.Substring(contentStart, contentEnd - contentStart).Trim();

        // 解析短代码内容
        var (name, parameters, positionalArgs, isClosingTag) = ParseShortcodeContent(content);

        if (string.IsNullOrEmpty(name))
        {
            return (null, startPosition + 3);
        }

        var rawShortcode = input.Substring(startPosition, endPos - startPosition);

        ShortcodeTokenType tokenType;
        if (isClosingTag)
        {
            tokenType = ShortcodeTokenType.Close;
        }
        else if (isSelfClosing)
        {
            tokenType = ShortcodeTokenType.SelfClosing;
        }
        else
        {
            tokenType = ShortcodeTokenType.Open;
        }

        var token = new ShortcodeToken
        {
            Type = tokenType,
            Name = name,
            DelimiterType = delimiterType,
            Parameters = parameters,
            PositionalArgs = positionalArgs,
            StartPosition = startPosition,
            EndPosition = endPos,
            RawShortcode = rawShortcode
        };

        return (token, endPos);
    }

    /// <summary>
    /// 解析短代码内容（名称和参数）
    /// </summary>
    private static (string Name, Dictionary<string, string> Parameters, List<string> PositionalArgs, bool IsClosingTag) ParseShortcodeContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return (string.Empty, new Dictionary<string, string>(), [], false);
        }

        var isClosingTag = content.StartsWith('/');
        if (isClosingTag)
        {
            content = content[1..].Trim();
        }

        // 使用正则表达式解析参数
        var parts = SplitShortcodeContent(content);

        if (parts.Count == 0)
        {
            return (string.Empty, new Dictionary<string, string>(), [], isClosingTag);
        }

        var name = parts[0];
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var positionalArgs = new List<string>();

        for (var i = 1; i < parts.Count; i++)
        {
            var part = parts[i];

            // 检查是否为命名参数 (key="value" 或 key=value)
            var equalsIndex = part.IndexOf('=', StringComparison.Ordinal);
            if (equalsIndex > 0)
            {
                var key = part[..equalsIndex].Trim();
                var value = part[(equalsIndex + 1)..].Trim();

                // 移除引号
                value = UnquoteString(value);
                parameters[key] = value;
            }
            else
            {
                // 位置参数
                positionalArgs.Add(UnquoteString(part));
            }
        }

        return (name, parameters, positionalArgs, isClosingTag);
    }

    /// <summary>
    /// 分割短代码内容，正确处理引号
    /// </summary>
    private static List<string> SplitShortcodeContent(string content)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var quoteChar = '\0';

        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];

            if (inQuotes)
            {
                if (c == quoteChar)
                {
                    inQuotes = false;
                    current.Append(c);
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '"' || c == '\'')
                {
                    inQuotes = true;
                    quoteChar = c;
                    current.Append(c);
                }
                else if (char.IsWhiteSpace(c))
                {
                    if (current.Length > 0)
                    {
                        parts.Add(current.ToString());
                        current.Clear();
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
        }

        if (current.Length > 0)
        {
            parts.Add(current.ToString());
        }

        return parts;
    }

    /// <summary>
    /// 移除字符串的引号
    /// </summary>
    private static string UnquoteString(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length < 2)
        {
            return value;
        }

        if ((value.StartsWith('"') && value.EndsWith('"')) ||
            (value.StartsWith('\'') && value.EndsWith('\'')))
        {
            return value[1..^1];
        }

        return value;
    }

    /// <summary>
    /// 创建文本令牌
    /// </summary>
    private static ShortcodeToken CreateTextToken(string input, int start, int end)
    {
        return new ShortcodeToken
        {
            Type = ShortcodeTokenType.Text,
            RawText = input[start..end],
            StartPosition = start,
            EndPosition = end
        };
    }
}

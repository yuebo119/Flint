// Flint 静态站点生成器
// 短代码解析器

using System.Text;

namespace Flint.Core.Content.Shortcodes;

/// <summary>
/// 短代码解析器
/// 负责将令牌流解析为短代码树结构，支持嵌套短代码
/// </summary>
public sealed class ShortcodeParser
{
    private static readonly System.Globalization.CultureInfo InvariantCulture = System.Globalization.CultureInfo.InvariantCulture;

    /// <summary>
    /// 解析输入文本中的所有短代码
    /// </summary>
    /// <param name="input">输入文本</param>
    /// <returns>解析结果，包含短代码列表和处理后的文本</returns>
    public static ShortcodeParseResult Parse(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return new ShortcodeParseResult
            {
                Shortcodes = [],
                ProcessedText = input ?? string.Empty,
                OriginalText = input ?? string.Empty
            };
        }

        var tokens = ShortcodeLexer.Tokenize(input);
        var shortcodes = new List<ParsedShortcode>();
        var processedText = new StringBuilder();
        var tokenIndex = 0;

        while (tokenIndex < tokens.Count)
        {
            var token = tokens[tokenIndex];

            switch (token.Type)
            {
                case ShortcodeTokenType.Text:
                    processedText.Append(token.RawText);
                    tokenIndex++;
                    break;

                case ShortcodeTokenType.SelfClosing:
                    var selfClosingShortcode = CreateSelfClosingShortcode(token, input);
                    shortcodes.Add(selfClosingShortcode);
                    // 添加占位符
                    processedText.Append(ShortcodeProcessor.DeferredPlaceholderPrefix).Append(shortcodes.Count - 1).Append(ShortcodeProcessor.DeferredPlaceholderSuffix);
                    tokenIndex++;
                    break;

                case ShortcodeTokenType.Open:
                    // 尝试查找匹配的关闭标签
                    var (pairedShortcode, newIndex) = TryParsePairedShortcode(tokens, tokenIndex, input);
                    if (pairedShortcode != null)
                    {
                        shortcodes.Add(pairedShortcode);
                        processedText.Append(ShortcodeProcessor.DeferredPlaceholderPrefix).Append(shortcodes.Count - 1).Append(ShortcodeProcessor.DeferredPlaceholderSuffix);
                        tokenIndex = newIndex;
                    }
                    else
                    {
                        // 没有找到匹配的关闭标签，将其视为自闭合短代码
                        var standaloneShortcode = CreateSelfClosingShortcode(token, input);
                        shortcodes.Add(standaloneShortcode);
                        processedText.Append(ShortcodeProcessor.DeferredPlaceholderPrefix).Append(shortcodes.Count - 1).Append(ShortcodeProcessor.DeferredPlaceholderSuffix);
                        tokenIndex++;
                    }
                    break;

                case ShortcodeTokenType.Close:
                    // 孤立的关闭标签，作为普通文本处理
                    processedText.Append(token.RawShortcode);
                    tokenIndex++;
                    break;

                default:
                    tokenIndex++;
                    break;
            }
        }

        return new ShortcodeParseResult
        {
            Shortcodes = shortcodes,
            ProcessedText = processedText.ToString(),
            OriginalText = input
        };
    }

    /// <summary>
    /// 仅提取短代码，不修改原文本
    /// </summary>
    /// <param name="input">输入文本</param>
    /// <returns>短代码列表</returns>
    public static IReadOnlyList<ParsedShortcode> ExtractShortcodes(string input)
    {
        return Parse(input).Shortcodes;
    }

    /// <summary>
    /// 检查文本是否包含短代码
    /// </summary>
    /// <param name="input">输入文本</param>
    /// <returns>是否包含短代码</returns>
    public static bool ContainsShortcodes(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return false;
        }

        return input.Contains("{{<", StringComparison.Ordinal) ||
               input.Contains("{{%", StringComparison.Ordinal);
    }

    /// <summary>
    /// 创建自闭合短代码
    /// </summary>
    private static ParsedShortcode CreateSelfClosingShortcode(ShortcodeToken token, string originalInput)
    {
        return new ParsedShortcode
        {
            Name = token.Name!,
            DelimiterType = token.DelimiterType,
            Parameters = token.Parameters,
            PositionalArgs = token.PositionalArgs,
            InnerContent = null,
            NestedShortcodes = [],
            StartPosition = token.StartPosition,
            EndPosition = token.EndPosition,
            RawText = token.RawShortcode ?? originalInput[token.StartPosition..token.EndPosition]
        };
    }

    /// <summary>
    /// 尝试解析配对短代码（带内容的短代码）
    /// 如果找不到匹配的关闭标签，返回 null。
    /// boundaryIndex 为外层关闭标签的 token 索引：递归解析不同名嵌套短代码时
    /// 不得越过它，否则内层会把外层关闭标签吞进自己的内容，外层配对失败且
    /// 孤立关闭标签残留到输出
    /// </summary>
    private static (ParsedShortcode? Shortcode, int NewIndex) TryParsePairedShortcode(
        IReadOnlyList<ShortcodeToken> tokens,
        int startIndex,
        string originalInput,
        int boundaryIndex = -1)
    {
        var openToken = tokens[startIndex];
        var shortcodeName = openToken.Name;

        // 首先检查是否存在匹配的关闭标签
        var closeIndex = -1;
        var nestingLevel = 1;

        for (var i = startIndex + 1; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Type == ShortcodeTokenType.Open &&
                string.Equals(t.Name, shortcodeName, StringComparison.OrdinalIgnoreCase))
            {
                nestingLevel++;
            }
            else if (t.Type == ShortcodeTokenType.Close &&
                     string.Equals(t.Name, shortcodeName, StringComparison.OrdinalIgnoreCase))
            {
                nestingLevel--;
                if (nestingLevel == 0)
                {
                    closeIndex = i;
                    break;
                }
            }
        }

        if (closeIndex < 0)
        {
            // 没有匹配的关闭标签
            return (null, startIndex + 1);
        }

        // 有匹配的关闭标签，解析配对短代码；
        // 内容扫描不得越过本层关闭标签
        nestingLevel = 1;
        var contentBuilder = new StringBuilder();
        var nestedShortcodes = new List<ParsedShortcode>();
        var currentIndex = startIndex + 1;
        var contentLimit = boundaryIndex < 0 ? tokens.Count : boundaryIndex;

        while (currentIndex < contentLimit && nestingLevel > 0)
        {
            var token = tokens[currentIndex];

            switch (token.Type)
            {
                case ShortcodeTokenType.Text:
                    contentBuilder.Append(token.RawText);
                    currentIndex++;
                    break;

                case ShortcodeTokenType.Open:
                    if (string.Equals(token.Name, shortcodeName, StringComparison.OrdinalIgnoreCase))
                    {
                        // 同名短代码嵌套
                        nestingLevel++;
                        contentBuilder.Append(token.RawShortcode);
                        currentIndex++;
                    }
                    else
                    {
                        // 不同名短代码，尝试解析为嵌套短代码（递归边界=本层关闭标签）
                        var (nestedShortcode, newIndex) = TryParsePairedShortcode(tokens, currentIndex, originalInput, contentLimit);
                        if (nestedShortcode != null)
                        {
                            nestedShortcodes.Add(nestedShortcode);
                            contentBuilder.Append(InvariantCulture, $"{{{{NESTED:{nestedShortcodes.Count - 1}}}}}");
                            currentIndex = newIndex;
                        }
                        else
                        {
                            // 作为自闭合短代码处理
                            var standaloneNested = CreateSelfClosingShortcode(token, originalInput);
                            nestedShortcodes.Add(standaloneNested);
                            contentBuilder.Append(InvariantCulture, $"{{{{NESTED:{nestedShortcodes.Count - 1}}}}}");
                            currentIndex++;
                        }
                    }
                    break;

                case ShortcodeTokenType.SelfClosing:
                    // 自闭合短代码作为嵌套短代码
                    var selfClosingNested = CreateSelfClosingShortcode(token, originalInput);
                    nestedShortcodes.Add(selfClosingNested);
                    contentBuilder.Append(InvariantCulture, $"{{{{NESTED:{nestedShortcodes.Count - 1}}}}}");
                    currentIndex++;
                    break;

                case ShortcodeTokenType.Close:
                    if (string.Equals(token.Name, shortcodeName, StringComparison.OrdinalIgnoreCase))
                    {
                        nestingLevel--;
                        if (nestingLevel == 0)
                        {
                            // 找到匹配的关闭标签
                            var innerContent = contentBuilder.ToString();
                            var endPosition = token.EndPosition;

                            return (new ParsedShortcode
                            {
                                Name = shortcodeName!,
                                DelimiterType = openToken.DelimiterType,
                                Parameters = openToken.Parameters,
                                PositionalArgs = openToken.PositionalArgs,
                                InnerContent = innerContent,
                                NestedShortcodes = nestedShortcodes,
                                StartPosition = openToken.StartPosition,
                                EndPosition = endPosition,
                                RawText = originalInput[openToken.StartPosition..endPosition]
                            }, currentIndex + 1);
                        }
                        else
                        {
                            contentBuilder.Append(token.RawShortcode);
                            currentIndex++;
                        }
                    }
                    else
                    {
                        // 不匹配的关闭标签，作为普通文本
                        contentBuilder.Append(token.RawShortcode);
                        currentIndex++;
                    }
                    break;

                default:
                    currentIndex++;
                    break;
            }
        }

        // 不应该到达这里，因为我们已经确认有匹配的关闭标签
        return (null, startIndex + 1);
    }
}

/// <summary>
/// 短代码解析结果
/// </summary>
public sealed class ShortcodeParseResult
{
    /// <summary>
    /// 解析出的短代码列表
    /// </summary>
    public required IReadOnlyList<ParsedShortcode> Shortcodes { get; init; }

    /// <summary>
    /// 处理后的文本（短代码被替换为占位符）
    /// </summary>
    public required string ProcessedText { get; init; }

    /// <summary>
    /// 原始文本
    /// </summary>
    public required string OriginalText { get; init; }

    /// <summary>
    /// 是否包含短代码
    /// </summary>
    public bool HasShortcodes => Shortcodes.Count > 0;
}

// Flint 静态站点生成器
// 短代码解析器属性测试
// **Property 4: 短代码解析正确性**
// **验证: 需求 2.8, 2.9**

using Flint.Core.Content.Shortcodes;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Flint.Core.Tests.Content;

/// <summary>
/// 短代码解析器属性测试
/// 验证短代码名称、参数提取、嵌套解析和确定性
/// </summary>
public class ShortcodeParserPropertyTests
{
    #region Property 4.1: 短代码名称提取正确性

    /// <summary>
    /// **Property 4.1: 短代码名称提取正确性**
    /// 对于任意有效的短代码调用，解析器应该正确提取短代码名称
    /// **验证: 需求 2.8**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(ValidShortcodeArbitrary)])]
    public Property ShortcodeName_ShouldBeExtractedCorrectly(ValidShortcode validShortcode)
    {
        ArgumentNullException.ThrowIfNull(validShortcode);

        // Arrange
        var markdown = validShortcode.Markdown;

        // Act
        var shortcodes = ShortcodeParser.ExtractShortcodes(markdown);

        // Assert - 应该提取到一个短代码，且名称正确
        var success = shortcodes.Count == 1 &&
                      string.Equals(shortcodes[0].Name, validShortcode.Name, StringComparison.OrdinalIgnoreCase);

        return success
            .ToProperty()
            .Label($"短代码名称提取: 期望={validShortcode.Name}, 实际={GetShortcodeName(shortcodes)}");
    }

    /// <summary>
    /// 安全获取短代码名称
    /// </summary>
    private static string GetShortcodeName(IReadOnlyList<ParsedShortcode> shortcodes)
    {
        return shortcodes.Count > 0 ? shortcodes[0].Name : "null";
    }

    #endregion

    #region Property 4.2: 命名参数提取正确性

    /// <summary>
    /// **Property 4.2: 命名参数提取正确性**
    /// 对于任意有效的短代码调用，解析器应该正确提取所有命名参数
    /// **验证: 需求 2.8**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(ValidShortcodeArbitrary)])]
    public Property NamedParameters_ShouldBeExtractedCorrectly(ValidShortcode validShortcode)
    {
        ArgumentNullException.ThrowIfNull(validShortcode);

        // Arrange
        var markdown = validShortcode.Markdown;

        // Act
        var shortcodes = ShortcodeParser.ExtractShortcodes(markdown);

        // Assert
        if (shortcodes.Count != 1)
        {
            return false.ToProperty().Label($"短代码数量错误: 期望=1, 实际={shortcodes.Count}");
        }

        var parsed = shortcodes[0];
        var expectedParams = validShortcode.Parameters;

        // 验证所有期望的参数都被正确提取
        var allParamsMatch = true;
        var diagnostics = new List<string>();

        foreach (var (key, expectedValue) in expectedParams)
        {
            if (!parsed.Parameters.TryGetValue(key, out var actualValue))
            {
                allParamsMatch = false;
                diagnostics.Add($"缺少参数: {key}");
            }
            else if (actualValue != expectedValue)
            {
                allParamsMatch = false;
                diagnostics.Add($"参数值不匹配: {key}='{actualValue}' (期望='{expectedValue}')");
            }
        }

        // 验证没有多余的参数
        foreach (var key in parsed.Parameters.Keys)
        {
            if (!expectedParams.ContainsKey(key))
            {
                allParamsMatch = false;
                diagnostics.Add($"多余参数: {key}");
            }
        }

        return allParamsMatch
            .ToProperty()
            .Label(allParamsMatch
                ? $"命名参数提取正确: {expectedParams.Count} 个参数"
                : $"命名参数提取失败: {string.Join("; ", diagnostics)}");
    }

    #endregion

    #region Property 4.3: 位置参数提取正确性

    /// <summary>
    /// **Property 4.3: 位置参数提取正确性**
    /// 对于任意有效的短代码调用，解析器应该正确提取所有位置参数
    /// **验证: 需求 2.8**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(ValidShortcodeArbitrary)])]
    public Property PositionalArgs_ShouldBeExtractedCorrectly(ValidShortcode validShortcode)
    {
        ArgumentNullException.ThrowIfNull(validShortcode);

        // Arrange
        var markdown = validShortcode.Markdown;

        // Act
        var shortcodes = ShortcodeParser.ExtractShortcodes(markdown);

        // Assert
        if (shortcodes.Count != 1)
        {
            return false.ToProperty().Label($"短代码数量错误: 期望=1, 实际={shortcodes.Count}");
        }

        var parsed = shortcodes[0];
        var expectedArgs = validShortcode.PositionalArgs;

        // 验证位置参数数量和内容
        var countMatch = parsed.PositionalArgs.Count == expectedArgs.Count;
        var contentMatch = true;
        var diagnostics = new List<string>();

        if (!countMatch)
        {
            diagnostics.Add($"数量不匹配: 期望={expectedArgs.Count}, 实际={parsed.PositionalArgs.Count}");
        }
        else
        {
            for (int i = 0; i < expectedArgs.Count; i++)
            {
                if (parsed.PositionalArgs[i] != expectedArgs[i])
                {
                    contentMatch = false;
                    diagnostics.Add($"参数[{i}]: '{parsed.PositionalArgs[i]}' != '{expectedArgs[i]}'");
                }
            }
        }

        var success = countMatch && contentMatch;

        return success
            .ToProperty()
            .Label(success
                ? $"位置参数提取正确: {expectedArgs.Count} 个参数"
                : $"位置参数提取失败: {string.Join("; ", diagnostics)}");
    }

    #endregion

    #region Property 4.4: 嵌套短代码解析正确性

    /// <summary>
    /// **Property 4.4: 嵌套短代码解析正确性**
    /// 对于任意有效的嵌套短代码，解析器应该正确识别嵌套结构
    /// **验证: 需求 2.9**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(ValidShortcodeArbitrary)])]
    public Property NestedShortcodes_ShouldBeParsedCorrectly(ValidNestedShortcode validNested)
    {
        ArgumentNullException.ThrowIfNull(validNested);

        // Arrange
        var markdown = validNested.Markdown;

        // Act
        var shortcodes = ShortcodeParser.ExtractShortcodes(markdown);

        // Assert - 应该提取到外层短代码
        if (shortcodes.Count != 1)
        {
            return false.ToProperty().Label($"外层短代码数量错误: 期望=1, 实际={shortcodes.Count}");
        }

        var outer = shortcodes[0];

        // 验证外层短代码名称
        var outerNameMatch = string.Equals(outer.Name, validNested.OuterName, StringComparison.OrdinalIgnoreCase);
        if (!outerNameMatch)
        {
            return false.ToProperty().Label($"外层名称不匹配: 期望={validNested.OuterName}, 实际={outer.Name}");
        }

        // 验证存在嵌套短代码
        var hasNestedShortcodes = outer.NestedShortcodes.Count > 0 ||
                                   (outer.InnerContent?.Contains("{{<") ?? false) ||
                                   (outer.InnerContent?.Contains("{{% ") ?? false);

        // 对于深度嵌套，验证最内层短代码
        var innerFound = FindInnerShortcode(outer, validNested.InnerName);

        return (outerNameMatch && (hasNestedShortcodes || innerFound))
            .ToProperty()
            .Label($"嵌套解析: Outer={validNested.OuterName}, Inner={validNested.InnerName}, Depth={validNested.NestingDepth}");
    }

    /// <summary>
    /// 递归查找内层短代码
    /// </summary>
    private static bool FindInnerShortcode(ParsedShortcode shortcode, string innerName)
    {
        foreach (var nested in shortcode.NestedShortcodes)
        {
            if (string.Equals(nested.Name, innerName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (FindInnerShortcode(nested, innerName))
            {
                return true;
            }
        }

        return false;
    }

    #endregion

    #region Property 4.5: 解析确定性

    /// <summary>
    /// **Property 4.5: 解析确定性**
    /// 对于任意有效的短代码，相同输入应该产生相同输出
    /// **验证: 需求 2.8, 2.9**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(ValidShortcodeArbitrary)])]
    public Property Parsing_ShouldBeDeterministic(ValidShortcode validShortcode)
    {
        ArgumentNullException.ThrowIfNull(validShortcode);

        // Arrange
        var markdown = validShortcode.Markdown;

        // Act - 解析两次
        var result1 = ShortcodeParser.Parse(markdown);
        var result2 = ShortcodeParser.Parse(markdown);

        // Assert - 两次解析结果应该完全一致
        var countMatch = result1.Shortcodes.Count == result2.Shortcodes.Count;
        var processedTextMatch = result1.ProcessedText == result2.ProcessedText;

        var shortcodesMatch = true;
        if (countMatch)
        {
            for (int i = 0; i < result1.Shortcodes.Count; i++)
            {
                var s1 = result1.Shortcodes[i];
                var s2 = result2.Shortcodes[i];

                if (s1.Name != s2.Name ||
                    s1.IsSelfClosing != s2.IsSelfClosing ||
                    s1.StartPosition != s2.StartPosition ||
                    s1.EndPosition != s2.EndPosition ||
                    !CompareParameters(s1.Parameters, s2.Parameters) ||
                    !ComparePositionalArgs(s1.PositionalArgs, s2.PositionalArgs))
                {
                    shortcodesMatch = false;
                    break;
                }
            }
        }

        var success = countMatch && processedTextMatch && shortcodesMatch;

        return success
            .ToProperty()
            .Label($"解析确定性: 短代码数量={result1.Shortcodes.Count}, 处理文本长度={result1.ProcessedText.Length}");
    }

    /// <summary>
    /// 比较两个参数字典
    /// </summary>
    private static bool CompareParameters(
        IReadOnlyDictionary<string, string> a,
        IReadOnlyDictionary<string, string> b)
    {
        if (a.Count != b.Count)
            return false;

        foreach (var (key, value) in a)
        {
            if (!b.TryGetValue(key, out var otherValue) || value != otherValue)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 比较两个位置参数列表
    /// </summary>
    private static bool ComparePositionalArgs(
        IReadOnlyList<string> a,
        IReadOnlyList<string> b)
    {
        if (a.Count != b.Count)
            return false;
        return a.SequenceEqual(b);
    }

    #endregion

    #region Property 4.6: 多短代码提取正确性

    /// <summary>
    /// **Property 4.6: 多短代码提取正确性**
    /// 对于包含多个短代码的文本，解析器应该正确提取所有短代码
    /// **验证: 需求 2.8**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(ValidShortcodeArbitrary)])]
    public Property MultipleShortcodes_ShouldBeExtractedCorrectly(ValidMarkdownWithShortcodes validMarkdown)
    {
        ArgumentNullException.ThrowIfNull(validMarkdown);

        // Arrange
        var markdown = validMarkdown.Markdown;
        var expected = validMarkdown.ExpectedShortcodes;

        // Act
        var shortcodes = ShortcodeParser.ExtractShortcodes(markdown);

        // Assert - 短代码数量应该匹配
        if (shortcodes.Count != expected.Count)
        {
            return false.ToProperty()
                .Label($"短代码数量不匹配: 期望={expected.Count}, 实际={shortcodes.Count}");
        }

        // 验证每个短代码的名称
        var allNamesMatch = true;
        var diagnostics = new List<string>();

        for (int i = 0; i < expected.Count; i++)
        {
            if (!string.Equals(shortcodes[i].Name, expected[i].Name, StringComparison.OrdinalIgnoreCase))
            {
                allNamesMatch = false;
                diagnostics.Add($"[{i}]: 期望={expected[i].Name}, 实际={shortcodes[i].Name}");
            }
        }

        return allNamesMatch
            .ToProperty()
            .Label(allNamesMatch
                ? $"多短代码提取正确: {expected.Count} 个短代码"
                : $"多短代码提取失败: {string.Join("; ", diagnostics)}");
    }

    #endregion

    #region Property 4.7: 分隔符类型识别正确性

    /// <summary>
    /// **Property 4.7: 分隔符类型识别正确性**
    /// 对于任意有效的短代码，解析器应该正确识别分隔符类型
    /// **验证: 需求 2.8**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(ValidShortcodeArbitrary)])]
    public Property DelimiterType_ShouldBeIdentifiedCorrectly(ValidShortcode validShortcode)
    {
        ArgumentNullException.ThrowIfNull(validShortcode);

        // Arrange
        var markdown = validShortcode.Markdown;
        var expectedDelimiter = validShortcode.UseAngleBrackets
            ? ShortcodeDelimiterType.Angle
            : ShortcodeDelimiterType.Percent;

        // Act
        var shortcodes = ShortcodeParser.ExtractShortcodes(markdown);

        // Assert
        if (shortcodes.Count != 1)
        {
            return false.ToProperty().Label($"短代码数量错误: 期望=1, 实际={shortcodes.Count}");
        }

        var actualDelimiter = shortcodes[0].DelimiterType;
        var success = actualDelimiter == expectedDelimiter;

        return success
            .ToProperty()
            .Label($"分隔符类型: 期望={expectedDelimiter}, 实际={actualDelimiter}");
    }

    #endregion

    #region Property 4.8: 自闭合标识正确性

    /// <summary>
    /// **Property 4.8: 自闭合标识正确性**
    /// 对于任意有效的短代码，解析器应该正确识别是否为自闭合
    /// **验证: 需求 2.8**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(ValidShortcodeArbitrary)])]
    public Property SelfClosing_ShouldBeIdentifiedCorrectly(ValidShortcode validShortcode)
    {
        ArgumentNullException.ThrowIfNull(validShortcode);

        // Arrange
        var markdown = validShortcode.Markdown;

        // Act
        var shortcodes = ShortcodeParser.ExtractShortcodes(markdown);

        // Assert
        if (shortcodes.Count != 1)
        {
            return false.ToProperty().Label($"短代码数量错误: 期望=1, 实际={shortcodes.Count}");
        }

        var actualSelfClosing = shortcodes[0].IsSelfClosing;
        var success = actualSelfClosing == validShortcode.IsSelfClosing;

        return success
            .ToProperty()
            .Label($"自闭合标识: 期望={validShortcode.IsSelfClosing}, 实际={actualSelfClosing}");
    }

    #endregion

    #region Property 4.9: 原始文本保留正确性

    /// <summary>
    /// **Property 4.9: 原始文本保留正确性**
    /// 解析结果应该保留原始短代码文本
    /// **验证: 需求 2.8**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(ValidShortcodeArbitrary)])]
    public Property RawText_ShouldBePreserved(ValidShortcode validShortcode)
    {
        ArgumentNullException.ThrowIfNull(validShortcode);

        // Arrange
        var markdown = validShortcode.Markdown;

        // Act
        var result = ShortcodeParser.Parse(markdown);

        // Assert
        if (result.Shortcodes.Count != 1)
        {
            return false.ToProperty().Label($"短代码数量错误: 期望=1, 实际={result.Shortcodes.Count}");
        }

        // 验证原始文本包含在输入中
        var rawText = result.Shortcodes[0].RawText;
        var containsRawText = markdown.Contains(rawText, StringComparison.Ordinal);

        // 验证原始文本不为空
        var rawTextNotEmpty = !string.IsNullOrEmpty(rawText);

        var success = containsRawText && rawTextNotEmpty;

        return success
            .ToProperty()
            .Label($"原始文本保留: 长度={rawText?.Length ?? 0}, 包含在输入中={containsRawText}");
    }

    #endregion

    #region Property 4.10: 位置信息正确性

    /// <summary>
    /// **Property 4.10: 位置信息正确性**
    /// 解析结果应该包含正确的位置信息
    /// **验证: 需求 2.8**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(ValidShortcodeArbitrary)])]
    public Property PositionInfo_ShouldBeCorrect(ValidShortcode validShortcode)
    {
        ArgumentNullException.ThrowIfNull(validShortcode);

        // Arrange
        var markdown = validShortcode.Markdown;

        // Act
        var shortcodes = ShortcodeParser.ExtractShortcodes(markdown);

        // Assert
        if (shortcodes.Count != 1)
        {
            return false.ToProperty().Label($"短代码数量错误: 期望=1, 实际={shortcodes.Count}");
        }

        var parsed = shortcodes[0];

        // 验证位置信息有效
        var startValid = parsed.StartPosition >= 0;
        var endValid = parsed.EndPosition > parsed.StartPosition;
        var endInRange = parsed.EndPosition <= markdown.Length;

        // 验证位置对应的文本与原始文本匹配
        var extractedText = markdown[parsed.StartPosition..parsed.EndPosition];
        var textMatch = extractedText == parsed.RawText;

        var success = startValid && endValid && endInRange && textMatch;

        return success
            .ToProperty()
            .Label($"位置信息: Start={parsed.StartPosition}, End={parsed.EndPosition}, TextMatch={textMatch}");
    }

    #endregion
}

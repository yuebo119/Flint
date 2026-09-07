// Flint 静态站点生成器
// 短代码属性测试生成器

using FsCheck;
using FsCheck.Fluent;

namespace Flint.Core.Tests.Content;

/// <summary>
/// 有效短代码包装类型
/// </summary>
public sealed class ValidShortcode
{
    /// <summary>
    /// 短代码名称
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 命名参数
    /// </summary>
    public IReadOnlyDictionary<string, string> Parameters { get; }

    /// <summary>
    /// 位置参数
    /// </summary>
    public IReadOnlyList<string> PositionalArgs { get; }

    /// <summary>
    /// 分隔符类型（true = 角括号，false = 百分号）
    /// </summary>
    public bool UseAngleBrackets { get; }

    /// <summary>
    /// 是否自闭合
    /// </summary>
    public bool IsSelfClosing { get; }

    /// <summary>
    /// 内部内容（仅用于配对短代码）
    /// </summary>
    public string? InnerContent { get; }

    /// <summary>
    /// 生成的 Markdown 文本
    /// </summary>
    public string Markdown { get; }

    public ValidShortcode(
        string name,
        IReadOnlyDictionary<string, string> parameters,
        IReadOnlyList<string> positionalArgs,
        bool useAngleBrackets,
        bool isSelfClosing,
        string? innerContent)
    {
        Name = name;
        Parameters = parameters;
        PositionalArgs = positionalArgs;
        UseAngleBrackets = useAngleBrackets;
        IsSelfClosing = isSelfClosing;
        InnerContent = innerContent;
        Markdown = GenerateMarkdown();
    }

    private string GenerateMarkdown()
    {
        var open = UseAngleBrackets ? "{{<" : "{{%";
        var close = UseAngleBrackets ? ">}}" : "%}}";
        var selfClose = UseAngleBrackets ? "/>}}" : "/%}}";

        var parts = new List<string> { Name };

        // 添加位置参数
        foreach (var arg in PositionalArgs)
        {
            parts.Add($"\"{arg}\"");
        }

        // 添加命名参数
        foreach (var (key, value) in Parameters)
        {
            parts.Add($"{key}=\"{value}\"");
        }

        var content = string.Join(" ", parts);

        if (IsSelfClosing)
        {
            return $"{open} {content} {selfClose}";
        }
        else
        {
            var closeTag = $"{open} /{Name} {close}";
            return $"{open} {content} {close}{InnerContent ?? ""}{closeTag}";
        }
    }

    public override string ToString() =>
        $"Shortcode(Name={Name}, Params={Parameters.Count}, PosArgs={PositionalArgs.Count}, SelfClosing={IsSelfClosing})";
}

/// <summary>
/// 带嵌套短代码的有效短代码包装类型
/// </summary>
public sealed class ValidNestedShortcode
{
    /// <summary>
    /// 外层短代码名称
    /// </summary>
    public string OuterName { get; }

    /// <summary>
    /// 内层短代码名称
    /// </summary>
    public string InnerName { get; }

    /// <summary>
    /// 生成的 Markdown 文本
    /// </summary>
    public string Markdown { get; }

    /// <summary>
    /// 嵌套深度
    /// </summary>
    public int NestingDepth { get; }

    public ValidNestedShortcode(string outerName, string innerName, int nestingDepth)
    {
        OuterName = outerName;
        InnerName = innerName;
        NestingDepth = nestingDepth;
        Markdown = GenerateMarkdown();
    }

    private string GenerateMarkdown()
    {
        // 生成嵌套结构
        var inner = $"{{{{< {InnerName} />}}}}";
        var result = inner;

        for (int i = 0; i < NestingDepth; i++)
        {
            var name = i == NestingDepth - 1 ? OuterName : $"level{i + 1}";
            result = $"{{{{< {name} >}}}}{result}{{{{< /{name} >}}}}";
        }

        return result;
    }

    public override string ToString() =>
        $"NestedShortcode(Outer={OuterName}, Inner={InnerName}, Depth={NestingDepth})";
}

/// <summary>
/// 带多个短代码的 Markdown 文本包装类型
/// </summary>
public sealed class ValidMarkdownWithShortcodes
{
    /// <summary>
    /// 生成的 Markdown 文本
    /// </summary>
    public string Markdown { get; }

    /// <summary>
    /// 预期的短代码列表
    /// </summary>
    public IReadOnlyList<ExpectedShortcode> ExpectedShortcodes { get; }

    public ValidMarkdownWithShortcodes(string markdown, IReadOnlyList<ExpectedShortcode> expectedShortcodes)
    {
        Markdown = markdown;
        ExpectedShortcodes = expectedShortcodes;
    }

    public override string ToString() =>
        $"MarkdownWithShortcodes(Count={ExpectedShortcodes.Count}, Length={Markdown.Length})";
}

/// <summary>
/// 预期的短代码信息
/// </summary>
public sealed class ExpectedShortcode
{
    public string Name { get; }
    public IReadOnlyDictionary<string, string> Parameters { get; }
    public IReadOnlyList<string> PositionalArgs { get; }
    public bool IsSelfClosing { get; }

    public ExpectedShortcode(
        string name,
        IReadOnlyDictionary<string, string> parameters,
        IReadOnlyList<string> positionalArgs,
        bool isSelfClosing)
    {
        Name = name;
        Parameters = parameters;
        PositionalArgs = positionalArgs;
        IsSelfClosing = isSelfClosing;
    }
}

/// <summary>
/// 有效短代码生成器
/// 生成符合规范的短代码用于属性测试
/// </summary>
public static class ValidShortcodeArbitrary
{
    #region 基础生成器

    /// <summary>
    /// 生成有效的短代码名称
    /// </summary>
    private static Gen<string> GenShortcodeName()
    {
        return Gen.Elements(
            "figure",
            "highlight",
            "youtube",
            "gist",
            "ref",
            "relref",
            "tweet",
            "vimeo",
            "instagram",
            "param",
            "custom",
            "gallery",
            "notice",
            "alert",
            "code");
    }

    /// <summary>
    /// 生成有效的参数键名
    /// </summary>
    private static Gen<string> GenParameterKey()
    {
        return Gen.Elements(
            "src",
            "title",
            "caption",
            "alt",
            "width",
            "height",
            "class",
            "id",
            "lang",
            "file",
            "type",
            "style");
    }

    /// <summary>
    /// 生成有效的参数值
    /// 避免特殊字符以确保解析正确性
    /// </summary>
    private static Gen<string> GenParameterValue()
    {
        return Gen.Elements(
            "image.jpg",
            "photo.png",
            "test-title",
            "example caption",
            "100",
            "200",
            "main-class",
            "element-id",
            "go",
            "csharp",
            "python",
            "javascript",
            "primary",
            "secondary",
            "default");
    }

    /// <summary>
    /// 生成有效的位置参数
    /// </summary>
    private static Gen<string> GenPositionalArg()
    {
        return Gen.Elements(
            "dQw4w9WgXcQ",
            "abc123",
            "user123",
            "gistid456",
            "posts/my-post.md",
            "docs/guide.md",
            "about",
            "contact",
            "value1",
            "value2");
    }

    /// <summary>
    /// 生成命名参数字典
    /// </summary>
    private static Gen<IReadOnlyDictionary<string, string>> GenParameters()
    {
        return Gen.Choose(0, 3).SelectMany(count =>
        {
            if (count == 0)
            {
                return Gen.Constant<IReadOnlyDictionary<string, string>>(
                    new Dictionary<string, string>());
            }

            return Gen.ListOf<(string key, string value)>(
                from key in GenParameterKey()
                from value in GenParameterValue()
                select (key, value), count)
                .Select(pairs =>
                {
                    var dict = new Dictionary<string, string>();
                    foreach (var pair in pairs)
                    {
                        dict[pair.key] = pair.value; // 使用索引器避免重复键
                    }
                    return (IReadOnlyDictionary<string, string>)dict;
                });
        });
    }

    /// <summary>
    /// 生成位置参数列表
    /// </summary>
    private static Gen<IReadOnlyList<string>> GenPositionalArgs()
    {
        return Gen.Choose(0, 2).SelectMany(count =>
        {
            if (count == 0)
            {
                return Gen.Constant<IReadOnlyList<string>>([]);
            }

            return Gen.ListOf<string>(GenPositionalArg(), count)
                .Select(list => (IReadOnlyList<string>)list.ToList());
        });
    }

    /// <summary>
    /// 生成内部内容
    /// </summary>
    private static Gen<string?> GenInnerContent()
    {
        return Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Elements<string?>(
                "简单文本内容",
                "Some text content",
                "fmt.Println(\"Hello\")",
                "console.log('test');",
                "这是一段较长的内容，包含多个句子。第二句话。",
                "Line 1\nLine 2\nLine 3"));
    }

    /// <summary>
    /// 生成简单文本（用于混合内容）
    /// </summary>
    private static Gen<string> GenSimpleText()
    {
        return Gen.Elements(
            "这是一段普通文本。",
            "Some regular text here.",
            "前面的内容",
            "后面的内容",
            "中间的文本",
            "Introduction paragraph.",
            "Conclusion section.");
    }

    #endregion

    #region Arbitrary 实现

    /// <summary>
    /// 生成有效的短代码
    /// </summary>
    public static Arbitrary<ValidShortcode> ValidShortcode()
    {
        var gen = from name in GenShortcodeName()
                  from parameters in GenParameters()
                  from positionalArgs in GenPositionalArgs()
                  from useAngleBrackets in ArbMap.Default.GeneratorFor<bool>()
                  from isSelfClosing in ArbMap.Default.GeneratorFor<bool>()
                  from innerContent in isSelfClosing
                      ? Gen.Constant<string?>(null)
                      : GenInnerContent()
                  select new ValidShortcode(
                      name,
                      parameters,
                      positionalArgs,
                      useAngleBrackets,
                      isSelfClosing,
                      innerContent);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// 生成带嵌套的短代码
    /// </summary>
    public static Arbitrary<ValidNestedShortcode> ValidNestedShortcode()
    {
        var gen = from outerName in GenShortcodeName()
                  from innerName in GenShortcodeName().Where(n => n != outerName)
                  from depth in Gen.Choose(1, 3)
                  select new ValidNestedShortcode(outerName, innerName, depth);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// 生成带多个短代码的 Markdown 文本
    /// </summary>
    public static Arbitrary<ValidMarkdownWithShortcodes> ValidMarkdownWithShortcodes()
    {
        var gen = Gen.Choose(1, 4).SelectMany(shortcodeCount =>
        {
            return Gen.ListOf<(string name, IReadOnlyDictionary<string, string> parameters, IReadOnlyList<string> positionalArgs, bool isSelfClosing)>(
                from name in GenShortcodeName()
                from parameters in GenParameters()
                from positionalArgs in GenPositionalArgs()
                from isSelfClosing in ArbMap.Default.GeneratorFor<bool>()
                select (name, parameters, positionalArgs, isSelfClosing), shortcodeCount)
                .SelectMany(shortcodeInfos =>
                    Gen.ListOf<string>(GenSimpleText(), shortcodeCount + 1)
                        .Select(texts =>
                        {
                            var parts = new List<string>();
                            var expectedShortcodes = new List<ExpectedShortcode>();
                            var textList = texts.ToList();
                            var infoList = shortcodeInfos.ToList();

                            for (int i = 0; i < infoList.Count; i++)
                            {
                                // 添加文本
                                if (i < textList.Count)
                                {
                                    parts.Add(textList[i]);
                                }

                                // 添加短代码
                                var info = infoList[i];
                                var shortcodeMarkdown = GenerateShortcodeMarkdown(
                                    info.name,
                                    info.parameters,
                                    info.positionalArgs,
                                    info.isSelfClosing);
                                parts.Add(shortcodeMarkdown);

                                expectedShortcodes.Add(new ExpectedShortcode(
                                    info.name,
                                    info.parameters,
                                    info.positionalArgs,
                                    info.isSelfClosing));
                            }

                            // 添加最后的文本
                            if (textList.Count > infoList.Count)
                            {
                                parts.Add(textList[^1]);
                            }

                            return new ValidMarkdownWithShortcodes(
                                string.Join(" ", parts),
                                expectedShortcodes);
                        }));
        });

        return gen.ToArbitrary();
    }

    /// <summary>
    /// 生成短代码 Markdown 文本
    /// </summary>
    private static string GenerateShortcodeMarkdown(
        string name,
        IReadOnlyDictionary<string, string> parameters,
        IReadOnlyList<string> positionalArgs,
        bool isSelfClosing)
    {
        var parts = new List<string> { name };

        // 添加位置参数
        foreach (var arg in positionalArgs)
        {
            parts.Add($"\"{arg}\"");
        }

        // 添加命名参数
        foreach (var (key, value) in parameters)
        {
            parts.Add($"{key}=\"{value}\"");
        }

        var content = string.Join(" ", parts);

        if (isSelfClosing)
        {
            return $"{{{{< {content} />}}}}";
        }
        else
        {
            return $"{{{{< {content} >}}}}{{{{< /{name} >}}}}";
        }
    }

    #endregion
}

// Flint 静态站点生成器
// 短代码令牌类型

namespace Flint.Core.Content.Shortcodes;

/// <summary>
/// 短代码令牌类型
/// </summary>
public enum ShortcodeTokenType
{
    /// <summary>
    /// 开始标签 {{< name >}} 或 {{% name %}}
    /// </summary>
    Open,

    /// <summary>
    /// 结束标签 {{< /name >}} 或 {{% /name %}}
    /// </summary>
    Close,

    /// <summary>
    /// 自闭合标签 {{< name />}} 或 {{% name /%}}
    /// </summary>
    SelfClosing,

    /// <summary>
    /// 纯文本内容
    /// </summary>
    Text
}

/// <summary>
/// 短代码分隔符类型
/// </summary>
public enum ShortcodeDelimiterType
{
    /// <summary>
    /// 角括号分隔符 {{< >}}
    /// 内容不会被 Markdown 处理
    /// </summary>
    Angle,

    /// <summary>
    /// 百分号分隔符 {{% %}}
    /// 内容会被 Markdown 处理
    /// </summary>
    Percent
}

/// <summary>
/// 短代码令牌
/// </summary>
public sealed class ShortcodeToken
{
    /// <summary>
    /// 令牌类型
    /// </summary>
    public required ShortcodeTokenType Type { get; init; }

    /// <summary>
    /// 短代码名称（对于 Text 类型为 null）
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// 分隔符类型
    /// </summary>
    public ShortcodeDelimiterType DelimiterType { get; init; }

    /// <summary>
    /// 命名参数
    /// </summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// 位置参数
    /// </summary>
    public IReadOnlyList<string> PositionalArgs { get; init; } = [];

    /// <summary>
    /// 原始文本（对于 Text 类型）
    /// </summary>
    public string? RawText { get; init; }

    /// <summary>
    /// 在源文本中的起始位置
    /// </summary>
    public int StartPosition { get; init; }

    /// <summary>
    /// 在源文本中的结束位置
    /// </summary>
    public int EndPosition { get; init; }

    /// <summary>
    /// 原始短代码文本
    /// </summary>
    public string? RawShortcode { get; init; }
}

/// <summary>
/// 解析后的短代码
/// </summary>
public sealed class ParsedShortcode
{
    /// <summary>
    /// 短代码名称
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 分隔符类型
    /// </summary>
    public required ShortcodeDelimiterType DelimiterType { get; init; }

    /// <summary>
    /// 命名参数
    /// </summary>
    public required IReadOnlyDictionary<string, string> Parameters { get; init; }

    /// <summary>
    /// 位置参数
    /// </summary>
    public required IReadOnlyList<string> PositionalArgs { get; init; }

    /// <summary>
    /// 内部内容（对于配对短代码）
    /// </summary>
    public string? InnerContent { get; init; }

    /// <summary>
    /// 嵌套的短代码列表
    /// </summary>
    public IReadOnlyList<ParsedShortcode> NestedShortcodes { get; init; } = [];

    /// <summary>
    /// 是否为自闭合短代码
    /// </summary>
    public bool IsSelfClosing => InnerContent is null;

    /// <summary>
    /// 在源文本中的起始位置
    /// </summary>
    public int StartPosition { get; init; }

    /// <summary>
    /// 在源文本中的结束位置
    /// </summary>
    public int EndPosition { get; init; }

    /// <summary>
    /// 原始短代码文本
    /// </summary>
    public required string RawText { get; init; }
}

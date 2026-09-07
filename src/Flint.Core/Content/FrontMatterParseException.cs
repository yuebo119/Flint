// Flint 静态站点生成器
// Front Matter 解析异常

namespace Flint.Core.Content;

/// <summary>
/// Front Matter 解析异常
/// 当文档存在完整的 Front Matter 分隔符但内容格式错误时抛出，
/// 用于区分"没有 Front Matter"（正常情况）与"Front Matter 格式错误"（应当让构建失败）。
/// </summary>
public sealed class FrontMatterParseException : FormatException
{
    /// <summary>
    /// Front Matter 格式（Yaml/Toml/Json）
    /// </summary>
    public string Format { get; }

    /// <summary>
    /// 错误所在行（1-based；无法定位时为 0）
    /// </summary>
    public int Line { get; }

    /// <summary>
    /// 错误所在列（1-based；无法定位时为 0）
    /// </summary>
    public int Column { get; }

    /// <summary>
    /// 创建 Front Matter 解析异常
    /// </summary>
    public FrontMatterParseException(string format, string message, Exception? innerException = null, int line = 0, int column = 0)
        : base(message, innerException)
    {
        Format = format;
        Line = line;
        Column = column;
    }
}

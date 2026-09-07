// Flint 静态站点生成器
// 内容解析器接口

using Flint.Core.Models;

namespace Flint.Core.Abstractions;

/// <summary>
/// 内容解析器接口
/// 负责解析 Markdown 文件并生成 ParsedContent
/// </summary>
public interface IContentParser
{
    /// <summary>
    /// 异步解析内容文件
    /// </summary>
    /// <param name="file">内容文件</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>解析后的内容</returns>
    ValueTask<ParsedContent> ParseAsync(
        ContentFile file,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 同步解析内容（用于性能关键路径）
    /// </summary>
    /// <param name="file">内容文件</param>
    /// <returns>解析后的内容</returns>
    ParsedContent Parse(ContentFile file);

    /// <summary>
    /// 批量解析内容文件
    /// </summary>
    /// <param name="files">内容文件列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>解析后的内容列表</returns>
    IAsyncEnumerable<ParsedContent> ParseBatchAsync(
        IEnumerable<ContentFile> files,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取短代码处理器（用于注册站点级/自定义短代码）
    /// </summary>
    Content.Shortcodes.ShortcodeProcessor ShortcodeProcessor { get; }
}

/// <summary>
/// Front Matter 解析器接口
/// </summary>
public interface IFrontMatterParser
{
    /// <summary>
    /// 尝试解析 Front Matter
    /// </summary>
    /// <param name="content">内容字符串</param>
    /// <param name="frontMatter">解析出的 Front Matter</param>
    /// <param name="remainingContent">剩余内容</param>
    /// <param name="siteTimeZone">
    /// 站点时区（Hugo <c>timeZone</c> 配置）：无偏移的日期值按此时区解释；
    /// null 时沿用本机时区（旧行为）。带显式偏移的日期不受影响。
    /// </param>
    /// <returns>是否成功解析；内容中没有 Front Matter 时返回 false</returns>
    /// <exception cref="Content.FrontMatterParseException">
    /// 文档存在完整的 Front Matter 分隔符但内容格式错误时抛出（fail-fast，防止元数据被静默丢弃）。
    /// </exception>
    bool TryParse(
        ReadOnlySpan<char> content,
        out FrontMatter? frontMatter,
        out ReadOnlySpan<char> remainingContent,
        TimeZoneInfo? siteTimeZone = null);

    /// <summary>
    /// 解析 Front Matter（抛出异常版本）
    /// </summary>
    /// <param name="content">内容字符串</param>
    /// <param name="siteTimeZone">站点时区，语义同 <see cref="TryParse"/></param>
    /// <returns>Front Matter 和剩余内容的元组</returns>
    (FrontMatter FrontMatter, string RemainingContent) Parse(
        string content,
        TimeZoneInfo? siteTimeZone = null);

    /// <summary>
    /// 检测 Front Matter 格式
    /// </summary>
    /// <param name="content">内容字符串</param>
    /// <returns>Front Matter 格式类型</returns>
    FrontMatterFormat DetectFormat(ReadOnlySpan<char> content);

    /// <summary>
    /// 将 Front Matter 序列化为字符串
    /// </summary>
    /// <param name="frontMatter">Front Matter 对象</param>
    /// <param name="format">输出格式</param>
    /// <returns>序列化后的字符串</returns>
    string Serialize(FrontMatter frontMatter, FrontMatterFormat format = FrontMatterFormat.Yaml);
}

/// <summary>
/// 短代码处理器接口
/// </summary>
public interface IShortcodeProcessor
{
    /// <summary>
    /// 短代码名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 短代码描述
    /// </summary>
    string Description { get; }

    /// <summary>
    /// 处理短代码
    /// </summary>
    /// <param name="context">短代码上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>处理后的 HTML</returns>
    ValueTask<string> ProcessAsync(
        ShortcodeContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 短代码上下文
/// </summary>
public readonly record struct ShortcodeContext
{
    /// <summary>
    /// 短代码名称
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 短代码参数
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
    /// 当前页面上下文
    /// </summary>
    public object? Page { get; init; }

    /// <summary>
    /// 站点上下文
    /// </summary>
    public object? Site { get; init; }

    /// <summary>
    /// 是否为自闭合短代码
    /// </summary>
    public bool IsSelfClosing => InnerContent is null;
}

/// <summary>
/// 短代码注册表接口
/// </summary>
public interface IShortcodeRegistry
{
    /// <summary>
    /// 注册短代码处理器
    /// </summary>
    /// <param name="processor">短代码处理器</param>
    void Register(IShortcodeProcessor processor);

    /// <summary>
    /// 获取短代码处理器
    /// </summary>
    /// <param name="name">短代码名称</param>
    /// <returns>短代码处理器，如果不存在则返回 null</returns>
    IShortcodeProcessor? Get(string name);

    /// <summary>
    /// 检查短代码是否已注册
    /// </summary>
    /// <param name="name">短代码名称</param>
    /// <returns>是否已注册</returns>
    bool Contains(string name);

    /// <summary>
    /// 获取所有已注册的短代码名称
    /// </summary>
    IEnumerable<string> GetRegisteredNames();
}

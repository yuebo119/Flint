// Flint 静态站点生成器
// OutputFormats——输出格式注册表（对齐 Hugo outputFormats 概念的最小集）

namespace Flint.Core.Site;

/// <summary>
/// 输出格式定义：名称、媒体类型与输出文件形态
/// </summary>
public sealed record OutputFormat(
    string Name,
    string MediaType,
    string BaseName,
    string Extension);

/// <summary>
/// 内置输出格式注册表与 kind → 输出格式解析。
/// 当前消费点：home 的 RSS 输出开关（feed.xml/atom.xml 是否产出）；
/// html 为所有 kind 的默认格式。json/amp 已注册可配置，暂无内置渲染器。
/// </summary>
public static class OutputFormats
{
    /// <summary>HTML 页面（所有 kind 的默认输出）</summary>
    public static readonly OutputFormat Html = new("html", "text/html", "index", ".html");

    /// <summary>RSS 订阅（home 默认输出之一）</summary>
    public static readonly OutputFormat Rss = new("rss", "application/rss+xml", "index", ".xml");

    /// <summary>JSON（可配置，暂无内置渲染器）</summary>
    public static readonly OutputFormat Json = new("json", "application/json", "index", ".json");

    /// <summary>
    /// 按名称解析内置格式（大小写不敏感，对齐 Hugo outputs 配置书写习惯）
    /// </summary>
    public static OutputFormat? Resolve(string name) => name.Trim().ToLowerInvariant() switch
    {
        "html" => Html,
        "rss" => Rss,
        "json" => Json,
        _ => null
    };

    /// <summary>
    /// 判断指定 kind 的输出格式列表是否包含某格式（大小写不敏感）
    /// </summary>
    public static bool Includes(IReadOnlyList<string> formats, string name) =>
        formats.Any(f => f.Equals(name, StringComparison.OrdinalIgnoreCase));
}

// Flint 静态站点生成器
// RSS/Atom Feed 生成器实现

using System.Text;
using System.Xml;
using Flint.Core.Abstractions;

namespace Flint.Core.Site;

/// <summary>
/// Feed 生成器
/// 生成 RSS 2.0 和 Atom 1.0 格式的订阅源
/// </summary>
public sealed class FeedGenerator
{
    private const string AtomNamespace = "http://www.w3.org/2005/Atom";
    private const string ContentNamespace = "http://purl.org/rss/1.0/modules/content/";

    private readonly FeedOptions _options;

    /// <summary>
    /// 创建 Feed 生成器
    /// </summary>
    /// <param name="options">生成选项</param>
    public FeedGenerator(FeedOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// 生成 RSS 2.0 Feed
    /// </summary>
    /// <param name="pages">页面列表</param>
    /// <returns>XML 内容</returns>
    public string GenerateRss(IReadOnlyList<PageContext> pages)
    {
        var sb = new StringBuilder();
        var settings = XmlOutput.CreateSettings(indent: true);

        using var writer = XmlWriter.Create(sb, settings);

        writer.WriteStartElement("rss");
        writer.WriteAttributeString("version", "2.0");
        writer.WriteAttributeString("xmlns", "atom", null, AtomNamespace);
        writer.WriteAttributeString("xmlns", "content", null, ContentNamespace);

        var feedPages = GetFeedPages(pages);

        writer.WriteStartElement("channel");
        WriteRssChannelInfo(writer, LatestFeedDate(feedPages));

        foreach (var page in feedPages)
        {
            WriteRssItem(writer, page);
        }

        writer.WriteEndElement(); // channel
        writer.WriteEndElement(); // rss
        writer.Flush();

        return XmlOutput.WithPreamble(sb);
    }

    /// <summary>
    /// 生成 Atom 1.0 Feed
    /// </summary>
    /// <param name="pages">页面列表</param>
    /// <returns>XML 内容</returns>
    public string GenerateAtom(IReadOnlyList<PageContext> pages)
    {
        var sb = new StringBuilder();
        var settings = XmlOutput.CreateSettings(indent: true);

        using var writer = XmlWriter.Create(sb, settings);

        writer.WriteStartElement("feed", AtomNamespace);

        var feedPages = GetFeedPages(pages);
        WriteAtomFeedInfo(writer, LatestFeedDate(feedPages));

        foreach (var page in feedPages)
        {
            WriteAtomEntry(writer, page);
        }

        writer.WriteEndElement(); // feed
        writer.Flush();

        return XmlOutput.WithPreamble(sb);
    }

    /// <summary>
    /// feed 级时间戳的取值：页面最大日期（feedPages 已按日期降序）。
    /// 不用构建时刻：同一输入两次构建会产出不同字节，且 CDN/差分部署与
    /// --cache 写盘跳过都会因此失效（2026-10-22 审计 P0-4）。
    /// 无可用日期返回 null，调用方省略该元素。
    /// </summary>
    private static DateTimeOffset? LatestFeedDate(IReadOnlyList<PageContext> pages)
    {
        foreach (var page in pages)
        {
            var date = page.LastMod ?? page.Date;
            if (date != DateTimeOffset.MinValue)
            {
                return date;
            }
        }

        return null;
    }

    private void WriteRssChannelInfo(XmlWriter writer, DateTimeOffset? lastBuildDate)
    {
        writer.WriteElementString("title", _options.Title);
        writer.WriteElementString("link", _options.BaseUrl);
        writer.WriteElementString("description", _options.Description ?? "");
        writer.WriteElementString("language", _options.Language);
        if (lastBuildDate.HasValue)
        {
            writer.WriteElementString("lastBuildDate", FormatRssDate(lastBuildDate.Value));
        }

        if (!string.IsNullOrEmpty(_options.Copyright))
        {
            writer.WriteElementString("copyright", _options.Copyright);
        }

        if (!string.IsNullOrEmpty(_options.Author))
        {
            writer.WriteElementString("managingEditor", _options.Author);
        }

        // Atom self link
        writer.WriteStartElement("atom", "link", AtomNamespace);
        writer.WriteAttributeString("href", GetAbsoluteUrl(_options.FeedPath ?? "/rss.xml"));
        writer.WriteAttributeString("rel", "self");
        writer.WriteAttributeString("type", "application/rss+xml");
        writer.WriteEndElement();
    }

    private void WriteRssItem(XmlWriter writer, PageContext page)
    {
        writer.WriteStartElement("item");

        writer.WriteElementString("title", page.Title);
        writer.WriteElementString("link", GetAbsoluteUrl(page.Permalink));

        // GUID
        writer.WriteStartElement("guid");
        writer.WriteAttributeString("isPermaLink", "true");
        writer.WriteString(GetAbsoluteUrl(page.Permalink));
        writer.WriteEndElement();

        // pubDate 可选元素：零值时间省略，避免 0001-01-01 进入订阅源
        if (page.Date != DateTimeOffset.MinValue)
        {
            writer.WriteElementString("pubDate", FormatRssDate(page.Date));
        }

        // 描述/摘要
        var description = page.Summary ?? page.Description ?? "";
        writer.WriteElementString("description", description);

        // 完整内容
        if (_options.IncludeFullContent && !string.IsNullOrEmpty(page.Content))
        {
            writer.WriteStartElement("content", "encoded", ContentNamespace);
            writer.WriteCData(page.Content);
            writer.WriteEndElement();
        }

        // 分类
        foreach (var category in page.Categories)
        {
            writer.WriteElementString("category", category);
        }

        foreach (var tag in page.Tags)
        {
            writer.WriteElementString("category", tag);
        }

        writer.WriteEndElement(); // item
    }

    private void WriteAtomFeedInfo(XmlWriter writer, DateTimeOffset? updated)
    {
        writer.WriteElementString("title", _options.Title);
        writer.WriteElementString("subtitle", _options.Description ?? "");
        writer.WriteElementString("id", _options.BaseUrl);
        if (updated.HasValue)
        {
            writer.WriteElementString("updated", FormatAtomDate(updated.Value));
        }

        // Self link
        writer.WriteStartElement("link");
        writer.WriteAttributeString("href", GetAbsoluteUrl(_options.FeedPath ?? "/atom.xml"));
        writer.WriteAttributeString("rel", "self");
        writer.WriteAttributeString("type", "application/atom+xml");
        writer.WriteEndElement();

        // Alternate link
        writer.WriteStartElement("link");
        writer.WriteAttributeString("href", _options.BaseUrl);
        writer.WriteAttributeString("rel", "alternate");
        writer.WriteAttributeString("type", "text/html");
        writer.WriteEndElement();

        // Author
        if (!string.IsNullOrEmpty(_options.Author))
        {
            writer.WriteStartElement("author");
            writer.WriteElementString("name", _options.Author);
            writer.WriteEndElement();
        }

        // Rights
        if (!string.IsNullOrEmpty(_options.Copyright))
        {
            writer.WriteElementString("rights", _options.Copyright);
        }

        // Generator
        writer.WriteStartElement("generator");
        writer.WriteAttributeString("uri", "https://github.com/yuebo119/Flint");
        writer.WriteAttributeString("version", "1.0");
        writer.WriteString("Flint");
        writer.WriteEndElement();
    }

    private void WriteAtomEntry(XmlWriter writer, PageContext page)
    {
        writer.WriteStartElement("entry");

        writer.WriteElementString("title", page.Title);
        writer.WriteElementString("id", GetAbsoluteUrl(page.Permalink));

        // Link
        writer.WriteStartElement("link");
        writer.WriteAttributeString("href", GetAbsoluteUrl(page.Permalink));
        writer.WriteAttributeString("rel", "alternate");
        writer.WriteAttributeString("type", "text/html");
        writer.WriteEndElement();

        // published/updated 在无日期页上省略（零值时间不是有效时间戳）
        if (page.Date != DateTimeOffset.MinValue)
        {
            writer.WriteElementString("published", FormatAtomDate(page.Date));
        }

        var entryUpdated = page.LastMod ?? page.Date;
        if (entryUpdated != DateTimeOffset.MinValue)
        {
            writer.WriteElementString("updated", FormatAtomDate(entryUpdated));
        }

        // Summary
        var summary = page.Summary ?? page.Description ?? "";
        if (!string.IsNullOrEmpty(summary))
        {
            writer.WriteStartElement("summary");
            writer.WriteAttributeString("type", "html");
            writer.WriteString(summary);
            writer.WriteEndElement();
        }

        // Content
        if (_options.IncludeFullContent && !string.IsNullOrEmpty(page.Content))
        {
            writer.WriteStartElement("content");
            writer.WriteAttributeString("type", "html");
            writer.WriteCData(page.Content);
            writer.WriteEndElement();
        }

        // Categories
        foreach (var category in page.Categories.Concat(page.Tags))
        {
            writer.WriteStartElement("category");
            writer.WriteAttributeString("term", category);
            writer.WriteEndElement();
        }

        writer.WriteEndElement(); // entry
    }

    private IReadOnlyList<PageContext> GetFeedPages(IReadOnlyList<PageContext> pages)
    {
        return pages
            .Where(p => !p.Draft || _options.IncludeDrafts)
            // 按 `.Type` 过滤（Hugo 语义：front matter `type` 优先，否则段名）
            .Where(p => !_options.ExcludedTypes.Contains(p.Type ?? ""))
            .OrderByDescending(p => p.Date)
            .Take(_options.MaxItems)
            .ToList();
    }

    private string GetAbsoluteUrl(string path)
    {
        // 精确 scheme 判定：StartsWith("http") 会把 httpx-guide 类
        // 以 http 开头的相对路径误判为绝对 URL
        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        var baseUrl = _options.BaseUrl.TrimEnd('/');
        var normalizedPath = path.StartsWith('/') ? path : "/" + path;
        return baseUrl + normalizedPath;
    }

    private static string FormatRssDate(DateTimeOffset date)
    {
        // RSS 2.0 引用 RFC 822：偏移为 +0800 形态（无冒号、显式符号）；
        // "zzz" 产出 +08:00 是 RFC 3339 风格，TimeSpan "hhmm" 又会丢失正号
        var offset = date.Offset;
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var magnitude = offset < TimeSpan.Zero ? offset.Negate() : offset;
        return date.ToString("ddd, dd MMM yyyy HH:mm:ss ", System.Globalization.CultureInfo.InvariantCulture) +
               sign + magnitude.ToString("hhmm", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string FormatAtomDate(DateTimeOffset date)
    {
        return date.ToString("O");
    }
}

/// <summary>
/// Feed 生成选项
/// </summary>
public sealed record FeedOptions
{
    /// <summary>
    /// 站点标题
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// 站点基础 URL
    /// </summary>
    public required string BaseUrl { get; init; }

    /// <summary>
    /// 站点描述
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// 语言代码
    /// </summary>
    public string Language { get; init; } = "en";

    /// <summary>
    /// 作者
    /// </summary>
    public string? Author { get; init; }

    /// <summary>
    /// 版权信息
    /// </summary>
    public string? Copyright { get; init; }

    /// <summary>
    /// Feed 路径
    /// </summary>
    public string? FeedPath { get; init; }

    /// <summary>
    /// 最大条目数
    /// </summary>
    public int MaxItems { get; init; } = 20;

    /// <summary>
    /// 是否包含完整内容
    /// </summary>
    public bool IncludeFullContent { get; init; } = true;

    /// <summary>
    /// 是否包含草稿
    /// </summary>
    public bool IncludeDrafts { get; init; }

    /// <summary>
    /// 排除的页面类型
    /// </summary>
    public IReadOnlySet<string> ExcludedTypes { get; init; } = new HashSet<string>();
}

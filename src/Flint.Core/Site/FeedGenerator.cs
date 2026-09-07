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
        var settings = CreateXmlSettings();

        using var writer = XmlWriter.Create(sb, settings);

        writer.WriteStartDocument();
        writer.WriteStartElement("rss");
        writer.WriteAttributeString("version", "2.0");
        writer.WriteAttributeString("xmlns", "atom", null, AtomNamespace);
        writer.WriteAttributeString("xmlns", "content", null, ContentNamespace);

        writer.WriteStartElement("channel");
        WriteRssChannelInfo(writer);

        var feedPages = GetFeedPages(pages);
        foreach (var page in feedPages)
        {
            WriteRssItem(writer, page);
        }

        writer.WriteEndElement(); // channel
        writer.WriteEndElement(); // rss
        writer.WriteEndDocument();
        writer.Flush();

        return sb.ToString();
    }

    /// <summary>
    /// 生成 Atom 1.0 Feed
    /// </summary>
    /// <param name="pages">页面列表</param>
    /// <returns>XML 内容</returns>
    public string GenerateAtom(IReadOnlyList<PageContext> pages)
    {
        var sb = new StringBuilder();
        var settings = CreateXmlSettings();

        using var writer = XmlWriter.Create(sb, settings);

        writer.WriteStartDocument();
        writer.WriteStartElement("feed", AtomNamespace);

        WriteAtomFeedInfo(writer);

        var feedPages = GetFeedPages(pages);
        foreach (var page in feedPages)
        {
            WriteAtomEntry(writer, page);
        }

        writer.WriteEndElement(); // feed
        writer.WriteEndDocument();
        writer.Flush();

        return sb.ToString();
    }

    /// <summary>
    /// 生成 JSON Feed
    /// </summary>
    /// <param name="pages">页面列表</param>
    /// <returns>JSON 内容</returns>
    public string GenerateJsonFeed(IReadOnlyList<PageContext> pages)
    {
        var feedPages = GetFeedPages(pages);
        var items = feedPages.Select(page => new JsonFeedItem
        {
            Id = GetAbsoluteUrl(page.Permalink),
            Url = GetAbsoluteUrl(page.Permalink),
            Title = page.Title,
            ContentHtml = _options.IncludeFullContent ? page.Content : null,
            Summary = page.Summary ?? page.Description,
            DatePublished = page.Date.ToString("O"),
            DateModified = (page.LastMod ?? page.Date).ToString("O"),
            Tags = page.Tags
        }).ToList();

        var feed = new JsonFeed
        {
            Version = "https://jsonfeed.org/version/1.1",
            Title = _options.Title,
            HomePageUrl = _options.BaseUrl,
            FeedUrl = GetAbsoluteUrl(_options.FeedPath ?? "/feed.json"),
            Description = _options.Description,
            Language = _options.Language,
            Items = items
        };

        // source-gen 序列化（AOT/trim 安全）
        return System.Text.Json.JsonSerializer.Serialize(feed, FeedJsonContext.Default.JsonFeed);
    }

    private void WriteRssChannelInfo(XmlWriter writer)
    {
        writer.WriteElementString("title", _options.Title);
        writer.WriteElementString("link", _options.BaseUrl);
        writer.WriteElementString("description", _options.Description ?? "");
        writer.WriteElementString("language", _options.Language);
        writer.WriteElementString("lastBuildDate", FormatRssDate(DateTimeOffset.Now));

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

        writer.WriteElementString("pubDate", FormatRssDate(page.Date));

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

    private void WriteAtomFeedInfo(XmlWriter writer)
    {
        writer.WriteElementString("title", _options.Title);
        writer.WriteElementString("subtitle", _options.Description ?? "");
        writer.WriteElementString("id", _options.BaseUrl);
        writer.WriteElementString("updated", FormatAtomDate(DateTimeOffset.Now));

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

        writer.WriteElementString("published", FormatAtomDate(page.Date));
        writer.WriteElementString("updated", FormatAtomDate(page.LastMod ?? page.Date));

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

    private static XmlWriterSettings CreateXmlSettings()
    {
        return new XmlWriterSettings
        {
            Indent = true,
            Encoding = Encoding.UTF8,
            OmitXmlDeclaration = false
        };
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

/// <summary>
/// JSON Feed 1.1 顶层文档
/// </summary>
internal sealed record JsonFeed
{
    [System.Text.Json.Serialization.JsonPropertyName("version")]
    public required string Version { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("title")]
    public required string Title { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("home_page_url")]
    public string? HomePageUrl { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("feed_url")]
    public string? FeedUrl { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("description")]
    public string? Description { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("language")]
    public string? Language { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("items")]
    public required IReadOnlyList<JsonFeedItem> Items { get; init; }
}

/// <summary>
/// JSON Feed 条目
/// </summary>
internal sealed record JsonFeedItem
{
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public required string Id { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("url")]
    public string? Url { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("title")]
    public string? Title { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("content_html")]
    public string? ContentHtml { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("summary")]
    public string? Summary { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("date_published")]
    public string? DatePublished { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("date_modified")]
    public string? DateModified { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("tags")]
    public IReadOnlyList<string>? Tags { get; init; }
}

/// <summary>
/// JSON Feed 序列化上下文（source-gen，AOT/trim 友好）
/// </summary>
[System.Text.Json.Serialization.JsonSerializable(typeof(JsonFeed))]
[System.Text.Json.Serialization.JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class FeedJsonContext : System.Text.Json.Serialization.JsonSerializerContext
{
}

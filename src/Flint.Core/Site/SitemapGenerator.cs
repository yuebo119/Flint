// Flint 静态站点生成器
// Sitemap 生成器实现

using System.Text;
using System.Xml;
using Flint.Core.Abstractions;

namespace Flint.Core.Site;

/// <summary>
/// Sitemap 生成器
/// 生成符合 sitemap.org 规范的 XML 站点地图
/// </summary>
public sealed class SitemapGenerator
{
    private const string SitemapNamespace = "http://www.sitemaps.org/schemas/sitemap/0.9";
    private const string XhtmlNamespace = "http://www.w3.org/1999/xhtml";

    private readonly string _baseUrl;
    private readonly SitemapOptions _options;

    /// <summary>
    /// 创建 Sitemap 生成器
    /// </summary>
    /// <param name="baseUrl">站点基础 URL</param>
    /// <param name="options">生成选项</param>
    public SitemapGenerator(string baseUrl, SitemapOptions? options = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _options = options ?? new SitemapOptions();
    }

    /// <summary>
    /// 生成 sitemap.xml 内容
    /// </summary>
    /// <param name="pages">页面列表</param>
    /// <returns>XML 内容</returns>
    public string Generate(IReadOnlyList<PageContext> pages)
    {
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings
        {
            Indent = _options.Indent,
            Encoding = Encoding.UTF8,
            OmitXmlDeclaration = false
        };

        using var writer = XmlWriter.Create(sb, settings);

        writer.WriteStartDocument();
        writer.WriteStartElement("urlset", SitemapNamespace);

        if (_options.IncludeAlternateLanguages)
        {
            writer.WriteAttributeString("xmlns", "xhtml", null, XhtmlNamespace);
        }

        foreach (var page in pages.Where(ShouldIncludePage))
        {
            WriteUrlEntry(writer, page);
        }

        writer.WriteEndElement(); // urlset
        writer.WriteEndDocument();
        writer.Flush();

        return sb.ToString();
    }

    private void WriteUrlEntry(XmlWriter writer, PageContext page)
    {
        writer.WriteStartElement("url");

        // loc - 必需（相对 permalink 补前导 '/'，与 FeedGenerator.GetAbsoluteUrl 归一一致；
        // 否则 example.com + blog/post/ 拼出 example.comblog/post/）
        var permalink = page.Permalink.StartsWith('/') ? page.Permalink : "/" + page.Permalink;
        // 精确 scheme 判定（同 FeedGenerator）：httpx-guide 类相对路径不以 // 结尾判定
        var url = page.Permalink.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                  page.Permalink.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? page.Permalink
            : _baseUrl + permalink;
        writer.WriteElementString("loc", url);

        // lastmod - 可选
        var lastMod = page.LastMod ?? page.Date;
        writer.WriteElementString("lastmod", FormatDate(lastMod));

        // changefreq - 可选
        if (_options.IncludeChangeFreq)
        {
            var changeFreq = DetermineChangeFreq(page);
            writer.WriteElementString("changefreq", changeFreq);
        }

        // priority - 可选
        if (_options.IncludePriority)
        {
            var priority = DeterminePriority(page);
            writer.WriteElementString("priority", priority.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
        }

        writer.WriteEndElement(); // url
    }

    private bool ShouldIncludePage(PageContext page)
    {
        // 排除草稿
        if (page.Draft && !_options.IncludeDrafts)
        {
            return false;
        }

        // 排除特定类型
        if (_options.ExcludedTypes.Contains(page.Type ?? ""))
        {
            return false;
        }

        return true;
    }

    private static string DetermineChangeFreq(PageContext page)
    {
        var age = DateTimeOffset.Now - page.Date;

        return age.TotalDays switch
        {
            < 1 => "hourly",
            < 7 => "daily",
            < 30 => "weekly",
            < 365 => "monthly",
            _ => "yearly"
        };
    }

    private double DeterminePriority(PageContext page)
    {
        // 首页优先级最高
        if (page.RelPermalink == "/" || page.RelPermalink == "/index.html")
        {
            return 1.0;
        }

        // 根据页面类型确定优先级
        return page.Type?.ToLowerInvariant() switch
        {
            "page" => 0.8,
            "post" => 0.6,
            "category" => 0.5,
            "tag" => 0.4,
            _ => _options.DefaultPriority
        };
    }

    private static string FormatDate(DateTimeOffset date)
    {
        return date.ToString("yyyy-MM-dd");
    }
}

/// <summary>
/// Sitemap 生成选项
/// </summary>
public sealed class SitemapOptions
{
    /// <summary>
    /// 是否缩进 XML
    /// </summary>
    public bool Indent { get; init; } = true;

    /// <summary>
    /// 是否包含更新频率
    /// </summary>
    public bool IncludeChangeFreq { get; init; } = true;

    /// <summary>
    /// 是否包含优先级
    /// </summary>
    public bool IncludePriority { get; init; } = true;

    /// <summary>
    /// 是否包含草稿
    /// </summary>
    public bool IncludeDrafts { get; init; }

    /// <summary>
    /// 是否包含多语言替代链接
    /// </summary>
    public bool IncludeAlternateLanguages { get; init; }

    /// <summary>
    /// 默认优先级
    /// </summary>
    public double DefaultPriority { get; init; } = 0.5;

    /// <summary>
    /// 排除的页面类型
    /// </summary>
    public IReadOnlySet<string> ExcludedTypes { get; init; } = new HashSet<string>();
}

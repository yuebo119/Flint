// Flint 静态站点生成器
// FsCheck 自定义生成器类
// 为属性测试提供 Flint 特定类型的随机数据生成

using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.Models;
using FsCheck;
using FsCheck.Fluent;

namespace Flint.IntegrationTests.Generators;

/// <summary>
/// Flint 类型的 FsCheck 自定义生成器
/// 提供 SiteConfig、FrontMatter、ContentFile、BuildOptions、AssetFile 等类型的生成器
/// </summary>
public static class FlintArbitraries
{
    #region 基础生成器

    /// <summary>
    /// 生成有效的站点名称：常用名 + 高熵 slug（PBT 的价值在高熵输入暴露未知边界，
    /// 纯固定枚举退化为"随机挑样例"）
    /// </summary>
    public static Arbitrary<string> ValidSiteName() =>
        Gen.OneOf(
            Gen.Elements(
                "my-site", "blog", "docs", "portfolio", "company-site",
                "personal-blog", "tech-docs", "api-docs", "landing-page",
                "news-site", "magazine", "shop", "gallery", "wiki"
            ),
            from len in Gen.Choose(2, 10)
            from head in Gen.Elements("my", "tech", "dev", "open", "web", "code")
            from tail in Gen.ArrayOf(Gen.Elements("abcdefghijklmnopqrstuvwxyz0123456789".ToCharArray()), len)
            select $"{head}-{new string(tail)}"
        ).ToArbitrary();

    /// <summary>
    /// 生成有效的 URL：常用形态 + 高熵主机名
    /// </summary>
    public static Arbitrary<string> ValidUrl() =>
        Gen.OneOf(
            Gen.Elements(
                "https://example.com",
                "https://www.example.org",
                "https://blog.example.net",
                "https://docs.example.io",
                "http://localhost:1313",
                "https://my-site.github.io",
                "https://subdomain.example.com/path"
            ),
            from scheme in Gen.Elements("https")
            from host in Gen.ArrayOf(Gen.Elements("abcdefghijklmnopqrstuvwxyz".ToCharArray()), 6)
            from tld in Gen.Elements("com", "org", "net", "io", "dev")
            select $"{scheme}://www.{new string(host)}.{tld}/"
        ).ToArbitrary();

    /// <summary>
    /// 生成有效的语言代码
    /// </summary>
    public static Arbitrary<string> ValidLanguageCode() =>
        Gen.Elements(
            "en", "zh", "zh-CN", "zh-TW", "ja", "ko", "de", "fr", "es",
            "pt", "ru", "ar", "hi", "it", "nl", "pl", "tr", "vi", "th"
        ).ToArbitrary();

    /// <summary>
    /// 生成有效的主题名称
    /// </summary>
    public static Arbitrary<string> ValidThemeName() =>
        Gen.Elements(
            "", "default", "minimal", "clean", "modern", "classic",
            "dark", "light", "material", "bootstrap", "tailwind"
        ).ToArbitrary();

    /// <summary>
    /// 生成有效的文件路径
    /// </summary>
    public static Arbitrary<string> ValidFilePath() =>
        Gen.OneOf(
            Gen.Constant("index.md"),
            Gen.Constant("about.md"),
            Gen.Constant("posts/first-post.md"),
            Gen.Constant("posts/2024/01/hello-world.md"),
            Gen.Constant("docs/getting-started.md"),
            Gen.Constant("blog/tech/programming.md"),
            from dir in Gen.Elements("posts", "docs", "blog", "pages", "articles")
            from name in Gen.Elements("index", "about", "contact", "readme", "guide")
            select $"{dir}/{name}.md"
        ).ToArbitrary();

    /// <summary>
    /// 生成有效的标签
    /// </summary>
    public static Arbitrary<string> ValidTag() =>
        Gen.Elements(
            "programming", "web", "dotnet", "csharp", "javascript",
            "tutorial", "guide", "tips", "news", "review",
            "技术", "编程", "教程", "博客", "开发"
        ).ToArbitrary();

    /// <summary>
    /// 生成有效的分类
    /// </summary>
    public static Arbitrary<string> ValidCategory() =>
        Gen.Elements(
            "Technology", "Programming", "Web Development", "Tutorial",
            "News", "Review", "Opinion", "Guide", "Reference",
            "技术", "编程", "教程", "新闻", "评论"
        ).ToArbitrary();

    #endregion

    #region SiteConfig 生成器

    /// <summary>
    /// SiteConfig 生成器
    /// </summary>
    public static Arbitrary<SiteConfig> SiteConfigArb() =>
        (from baseUrl in ValidUrl().Generator
         from title in Gen.Elements("My Site", "Blog", "Docs", "Portfolio", "我的站点", "博客", "文档")
         from languageCode in ValidLanguageCode().Generator
         from theme in ValidThemeName().Generator
         from buildDrafts in ArbMap.Default.GeneratorFor<bool>()
         from buildFuture in ArbMap.Default.GeneratorFor<bool>()
         from buildExpired in ArbMap.Default.GeneratorFor<bool>()
         from paginate in Gen.Choose(5, 50)
         from summaryLength in Gen.Choose(50, 200)
         from enableGitInfo in ArbMap.Default.GeneratorFor<bool>()
         select new SiteConfig
         {
             BaseURL = baseUrl,
             Title = title,
             LanguageCode = languageCode,
             Theme = theme,
             BuildDrafts = buildDrafts,
             BuildFuture = buildFuture,
             BuildExpired = buildExpired,
             Paginate = paginate,
             SummaryLength = summaryLength,
             EnableGitInfo = enableGitInfo,
             Permalinks = new PermalinkConfig(),
             Taxonomies = new TaxonomyConfig(),
             Menus = new MenuConfig(),
             Markup = new MarkupConfig(),
             Outputs = new OutputConfig(),
             Module = new ModuleConfig(),
             Security = new SecurityConfig(),
             Caches = new CacheConfig()
         }).ToArbitrary();

    /// <summary>
    /// 最小 SiteConfig 生成器（只包含必需字段）
    /// </summary>
    public static Arbitrary<SiteConfig> MinimalSiteConfigArb() =>
        (from baseUrl in ValidUrl().Generator
         from title in Gen.Elements("Site", "Blog", "Docs")
         select new SiteConfig
         {
             BaseURL = baseUrl,
             Title = title
         }).ToArbitrary();

    /// <summary>
    /// 完整 SiteConfig 生成器（包含所有可选字段）
    /// </summary>
    public static Arbitrary<SiteConfig> FullSiteConfigArb() =>
        (from baseUrl in ValidUrl().Generator
         from title in Gen.Elements("My Full Site", "Complete Blog", "Full Docs")
         from languageCode in ValidLanguageCode().Generator
         from theme in Gen.Elements("default", "minimal", "modern")
         from copyright in Gen.Elements("© 2024", "All rights reserved", "CC BY 4.0")
         from authorName in Gen.Elements("John Doe", "Jane Smith", "张三")
         select new SiteConfig
         {
             BaseURL = baseUrl,
             Title = title,
             LanguageCode = languageCode,
             Theme = theme,
             BuildDrafts = false,
             BuildFuture = false,
             BuildExpired = false,
             Paginate = 10,
             PaginatePath = "page",
             SummaryLength = 70,
             EnableGitInfo = true,
             Copyright = copyright,
             Author = new AuthorConfig { Name = authorName },
             ContentDir = "content",
             LayoutDir = "layouts",
             StaticDir = "static",
             AssetDir = "assets",
             DataDir = "data",
             PublishDir = "public",
             ArchetypeDir = "archetypes"
         }).ToArbitrary();

    #endregion

    #region FrontMatter 生成器

    /// <summary>
    /// FrontMatter 生成器
    /// </summary>
    public static Arbitrary<FrontMatter> FrontMatterArb() =>
        (from title in Gen.Elements("Hello World", "Getting Started", "Tutorial", "第一篇文章", "入门指南")
         from date in Gen.Choose(2020, 2026).Select(y => new DateTimeOffset(y, 1, 1, 0, 0, 0, TimeSpan.Zero))
         from draft in ArbMap.Default.GeneratorFor<bool>()
         from tagCount in Gen.Choose(0, 3)
         from tags in Gen.ListOf<string>(ValidTag().Generator).Select(t => (IReadOnlyList<string>)t.Take(tagCount).ToList())
         from catCount in Gen.Choose(0, 2)
         from categories in Gen.ListOf<string>(ValidCategory().Generator).Select(c => (IReadOnlyList<string>)c.Take(catCount).ToList())
         from weight in Gen.Choose(0, 100)
         from format in Gen.Elements(FrontMatterFormat.Yaml, FrontMatterFormat.Toml, FrontMatterFormat.Json)
         select new FrontMatter
         {
             Title = title,
             Date = date,
             Draft = draft,
             Tags = tags,
             Categories = categories,
             Weight = weight,
             Format = format
         }).ToArbitrary();

    /// <summary>
    /// 最小 FrontMatter 生成器
    /// </summary>
    public static Arbitrary<FrontMatter> MinimalFrontMatterArb() =>
        (from title in Gen.Elements("Post", "Page", "Article")
         select new FrontMatter { Title = title }).ToArbitrary();

    /// <summary>
    /// 完整 FrontMatter 生成器
    /// </summary>
    public static Arbitrary<FrontMatter> FullFrontMatterArb() =>
        (from title in Gen.Elements("Complete Post", "Full Article", "详细文章")
         from date in Gen.Choose(2020, 2026).Select(y => new DateTimeOffset(y, 6, 15, 12, 0, 0, TimeSpan.Zero))
         from lastMod in Gen.Choose(2020, 2026).Select(y => new DateTimeOffset(y, 12, 1, 0, 0, 0, TimeSpan.Zero))
         from draft in ArbMap.Default.GeneratorFor<bool>()
         from tags in Gen.ListOf<string>(ValidTag().Generator).Select(t => (IReadOnlyList<string>)t.Take(3).ToList())
         from categories in Gen.ListOf<string>(ValidCategory().Generator).Select(c => (IReadOnlyList<string>)c.Take(2).ToList())
         from layout in Gen.Elements("single", "post", "page", "default")
         from slug in Gen.Elements("my-post", "article-1", "guide")
         from description in Gen.Elements("A great post", "Tutorial article", "精彩文章")
         from author in Gen.Elements("John", "Jane", "张三")
         from weight in Gen.Choose(0, 100)
         from format in Gen.Elements(FrontMatterFormat.Yaml, FrontMatterFormat.Toml, FrontMatterFormat.Json)
         select new FrontMatter
         {
             Title = title,
             Date = date,
             LastMod = lastMod,
             Draft = draft,
             Tags = tags,
             Categories = categories,
             Layout = layout,
             Slug = slug,
             Description = description,
             Author = author,
             Weight = weight,
             Format = format
         }).ToArbitrary();

    /// <summary>
    /// 草稿 FrontMatter 生成器
    /// </summary>
    public static Arbitrary<FrontMatter> DraftFrontMatterArb() =>
        (from title in Gen.Elements("Draft Post", "WIP Article", "草稿")
         from date in Gen.Choose(2020, 2026).Select(y => new DateTimeOffset(y, 1, 1, 0, 0, 0, TimeSpan.Zero))
         select new FrontMatter
         {
             Title = title,
             Date = date,
             Draft = true
         }).ToArbitrary();

    /// <summary>
    /// 未来日期 FrontMatter 生成器
    /// </summary>
    public static Arbitrary<FrontMatter> FutureFrontMatterArb() =>
        (from title in Gen.Elements("Future Post", "Scheduled Article", "定时发布")
         from daysInFuture in Gen.Choose(1, 365)
         select new FrontMatter
         {
             Title = title,
             Date = DateTimeOffset.UtcNow.AddDays(daysInFuture),
             Draft = false
         }).ToArbitrary();

    #endregion

    #region ContentFile 生成器

    /// <summary>
    /// ContentFile 生成器
    /// </summary>
    public static Arbitrary<ContentFile> ContentFileArb() =>
        (from path in ValidFilePath().Generator
         from content in MarkdownContentArb().Generator
         from modifiedTime in Gen.Choose(2020, 2026).Select(y => new DateTimeOffset(y, 6, 15, 12, 0, 0, TimeSpan.Zero))
         select new ContentFile
         {
             Path = path,
             RawContent = System.Text.Encoding.UTF8.GetBytes(content),
             ModifiedTime = modifiedTime
         }).ToArbitrary();

    /// <summary>
    /// Markdown 内容生成器
    /// </summary>
    public static Arbitrary<string> MarkdownContentArb() =>
        (from frontMatter in FrontMatterStringArb().Generator
         from body in MarkdownBodyArb().Generator
         select frontMatter + "\n" + body).ToArbitrary();

    /// <summary>
    /// FrontMatter 字符串生成器（YAML 格式）
    /// </summary>
    public static Arbitrary<string> FrontMatterStringArb() =>
        (from title in Gen.Elements("Hello World", "Getting Started", "Tutorial", "文章标题")
         from date in Gen.Choose(2020, 2026).Select(y => $"{y}-01-15")
         from draft in ArbMap.Default.GeneratorFor<bool>()
         select $"""
                ---
                title: "{title}"
                date: {date}
                draft: {draft.ToString().ToLowerInvariant()}
                ---
                """).ToArbitrary();

    /// <summary>
    /// Markdown 正文生成器
    /// </summary>
    public static Arbitrary<string> MarkdownBodyArb() =>
        Gen.OneOf(
            Gen.Constant("# Hello World\n\nThis is a test post."),
            Gen.Constant("## Introduction\n\nWelcome to my blog.\n\n### Section 1\n\nSome content here."),
            Gen.Constant("# 你好世界\n\n这是一篇测试文章。\n\n## 第一节\n\n一些内容。"),
            from heading in Gen.Elements("# Title", "## Subtitle", "### Section")
            from para1 in Gen.Elements("First paragraph.", "Introduction text.", "开头段落。")
            from para2 in Gen.Elements("Second paragraph.", "More content.", "更多内容。")
            select $"{heading}\n\n{para1}\n\n{para2}"
        ).ToArbitrary();

    #endregion

    #region BuildOptions 生成器

    /// <summary>
    /// BuildOptions 生成器
    /// </summary>
    public static Arbitrary<BuildOptions> BuildOptionsArb() =>
        (from sourcePath in Gen.Constant("./site")
         from outputPath in Gen.Constant("./public")
         from minify in ArbMap.Default.GeneratorFor<bool>()
         from includeDrafts in ArbMap.Default.GeneratorFor<bool>()
         from includeFuture in ArbMap.Default.GeneratorFor<bool>()
         from includeExpired in ArbMap.Default.GeneratorFor<bool>()
         from environment in Gen.Elements<string?>(null, "development", "production", "staging")
         from parallelism in Gen.Choose(1, 8)
         from enableCache in ArbMap.Default.GeneratorFor<bool>()
         from cleanOutput in ArbMap.Default.GeneratorFor<bool>()
         from generateSourceMaps in ArbMap.Default.GeneratorFor<bool>()
         from verbose in ArbMap.Default.GeneratorFor<bool>()
         from debug in ArbMap.Default.GeneratorFor<bool>()
         select new BuildOptions
         {
             SourcePath = sourcePath,
             OutputPath = outputPath,
             Minify = minify,
             IncludeDrafts = includeDrafts,
             IncludeFuture = includeFuture,
             IncludeExpired = includeExpired,
             Environment = environment,
             Parallelism = parallelism,
             EnableCache = enableCache,
             CleanOutput = cleanOutput,
             GenerateSourceMaps = generateSourceMaps,
             Verbose = verbose,
             Debug = debug
         }).ToArbitrary();

    /// <summary>
    /// 生产环境 BuildOptions 生成器
    /// </summary>
    public static Arbitrary<BuildOptions> ProductionBuildOptionsArb() =>
        (from sourcePath in Gen.Constant("./site")
         from outputPath in Gen.Constant("./public")
         select new BuildOptions
         {
             SourcePath = sourcePath,
             OutputPath = outputPath,
             Environment = "production",
             Minify = true,
             IncludeDrafts = false,
             IncludeFuture = false,
             IncludeExpired = false,
             EnableCache = true,
             CleanOutput = true,
             GenerateSourceMaps = false,
             Verbose = false,
             Debug = false
         }).ToArbitrary();

    /// <summary>
    /// 开发环境 BuildOptions 生成器
    /// </summary>
    public static Arbitrary<BuildOptions> DevelopmentBuildOptionsArb() =>
        (from sourcePath in Gen.Constant("./site")
         from outputPath in Gen.Constant("./public")
         select new BuildOptions
         {
             SourcePath = sourcePath,
             OutputPath = outputPath,
             Environment = "development",
             Minify = false,
             IncludeDrafts = true,
             IncludeFuture = true,
             IncludeExpired = false,
             EnableCache = true,
             CleanOutput = false,
             GenerateSourceMaps = true,
             Verbose = true,
             Debug = false
         }).ToArbitrary();

    #endregion

    #region AssetFile 生成器

    /// <summary>
    /// AssetFile 生成器
    /// </summary>
    public static Arbitrary<AssetFile> AssetFileArb() =>
        Gen.OneOf(
            ImageAssetArb().Generator,
            StyleAssetArb().Generator,
            ScriptAssetArb().Generator,
            FontAssetArb().Generator
        ).ToArbitrary();

    /// <summary>
    /// 图片资源生成器
    /// </summary>
    public static Arbitrary<AssetFile> ImageAssetArb() =>
        (from ext in Gen.Elements(".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg")
         from name in Gen.Elements("logo", "banner", "icon", "photo", "image")
         from modifiedTime in Gen.Choose(2020, 2026).Select(y => new DateTimeOffset(y, 6, 15, 12, 0, 0, TimeSpan.Zero))
         select new AssetFile
         {
             SourcePath = $"images/{name}{ext}",
             MediaType = GetMediaType(ext),
             Content = GenerateDummyImageContent(ext),
             ModifiedTime = modifiedTime
         }).ToArbitrary();

    /// <summary>
    /// 样式资源生成器
    /// </summary>
    public static Arbitrary<AssetFile> StyleAssetArb() =>
        (from ext in Gen.Elements(".css", ".scss", ".sass")
         from name in Gen.Elements("main", "style", "theme", "variables", "components")
         from modifiedTime in Gen.Choose(2020, 2026).Select(y => new DateTimeOffset(y, 6, 15, 12, 0, 0, TimeSpan.Zero))
         let content = ext switch
         {
             ".scss" or ".sass" => "$primary: #007bff;\n.container { color: $primary; }",
             _ => ".container { color: #007bff; }"
         }
         select new AssetFile
         {
             SourcePath = $"styles/{name}{ext}",
             MediaType = GetMediaType(ext),
             Content = System.Text.Encoding.UTF8.GetBytes(content),
             ModifiedTime = modifiedTime
         }).ToArbitrary();

    /// <summary>
    /// 脚本资源生成器
    /// </summary>
    public static Arbitrary<AssetFile> ScriptAssetArb() =>
        (from ext in Gen.Elements(".js", ".mjs", ".ts")
         from name in Gen.Elements("main", "app", "utils", "helpers", "index")
         from modifiedTime in Gen.Choose(2020, 2026).Select(y => new DateTimeOffset(y, 6, 15, 12, 0, 0, TimeSpan.Zero))
         let content = ext switch
         {
             ".ts" => "const greeting: string = 'Hello';\nconsole.log(greeting);",
             _ => "const greeting = 'Hello';\nconsole.log(greeting);"
         }
         select new AssetFile
         {
             SourcePath = $"scripts/{name}{ext}",
             MediaType = GetMediaType(ext),
             Content = System.Text.Encoding.UTF8.GetBytes(content),
             ModifiedTime = modifiedTime
         }).ToArbitrary();

    /// <summary>
    /// 字体资源生成器
    /// </summary>
    public static Arbitrary<AssetFile> FontAssetArb() =>
        (from ext in Gen.Elements(".woff", ".woff2", ".ttf", ".otf", ".eot")
         from name in Gen.Elements("roboto", "opensans", "lato", "montserrat", "sourcesans")
         from modifiedTime in Gen.Choose(2020, 2026).Select(y => new DateTimeOffset(y, 6, 15, 12, 0, 0, TimeSpan.Zero))
         select new AssetFile
         {
             SourcePath = $"fonts/{name}{ext}",
             MediaType = GetMediaType(ext),
             Content = GenerateDummyFontContent(),
             ModifiedTime = modifiedTime
         }).ToArbitrary();

    #endregion

    #region ConfigFormat 生成器

    /// <summary>
    /// ConfigFormat 生成器
    /// </summary>
    public static Arbitrary<ConfigFormat> ConfigFormatArb() =>
        Gen.Elements(ConfigFormat.Toml, ConfigFormat.Yaml, ConfigFormat.Json).ToArbitrary();

    /// <summary>
    /// FrontMatterFormat 生成器
    /// </summary>
    public static Arbitrary<FrontMatterFormat> FrontMatterFormatArb() =>
        Gen.Elements(FrontMatterFormat.Yaml, FrontMatterFormat.Toml, FrontMatterFormat.Json).ToArbitrary();

    #endregion

    #region 辅助方法

    /// <summary>
    /// 根据扩展名获取 MIME 类型
    /// </summary>
    private static string GetMediaType(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        ".ico" => "image/x-icon",
        ".css" => "text/css",
        ".scss" or ".sass" => "text/x-scss",
        ".js" or ".mjs" => "application/javascript",
        ".ts" or ".tsx" => "application/typescript",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        ".ttf" => "font/ttf",
        ".otf" => "font/otf",
        ".eot" => "application/vnd.ms-fontobject",
        _ => "application/octet-stream"
    };

    /// <summary>
    /// 生成虚拟图片内容
    /// </summary>
    private static ReadOnlyMemory<byte> GenerateDummyImageContent(string extension)
    {
        // 生成最小有效的图片文件头
        return extension.ToLowerInvariant() switch
        {
            ".png" => new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, // PNG 文件头
            ".jpg" or ".jpeg" => new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, // JPEG 文件头
            ".gif" => new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, // GIF89a
            ".webp" => new byte[] { 0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50 }, // RIFF....WEBP
            ".svg" => System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>"),
            _ => new byte[] { 0x00 }
        };
    }

    /// <summary>
    /// 生成虚拟字体内容
    /// </summary>
    private static ReadOnlyMemory<byte> GenerateDummyFontContent()
    {
        // 生成最小的虚拟字体数据
        return new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00, 0x0A, 0x00, 0x80 };
    }

    #endregion
}

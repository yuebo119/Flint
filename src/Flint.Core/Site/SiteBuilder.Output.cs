// Flint 静态站点生成器
// SiteBuilder 输出写入聚合：输出目录清理、资源处理、sitemap/feed、文件写入与输出路径计算

using System.Collections.Concurrent;
using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.Models;

namespace Flint.Core.Site;

/// <summary>
/// 站点构建器（partial）：输出写入聚合
/// </summary>
public sealed partial class SiteBuilder
{
    /// <inheritdoc />
    public async ValueTask CleanAsync(
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        // 通用护栏：递归删除不可逆，拒绝盘根/一级目录/当前工作目录及其祖先——
        // 这些路径不可能是合法输出目录，只可能是误配
        var full = Path.GetFullPath(outputPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var root = Path.GetPathRoot(full)?
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? string.Empty;
        // 两端 trim：root 截断后残留的前导分隔符会让 Split 产出空首段，深度虚增
        var relative = full[root.Length..]
            .Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var depth = string.IsNullOrEmpty(relative)
            ? 0
            : relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length;
        var cwd = Environment.CurrentDirectory
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var isCwdOrAncestor = string.Equals(full, cwd, StringComparison.OrdinalIgnoreCase)
            || cwd.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        if (depth <= 1 || isCwdOrAncestor)
        {
            throw new InvalidOperationException(
                $"拒绝清理危险路径: {full}（盘根、一级目录、当前工作目录及其祖先不可作为输出目录删除）");
        }

        if (Directory.Exists(full))
        {
            await Task.Run(() =>
            {
                Directory.Delete(full, recursive: true);
            }, cancellationToken);
        }
    }

    private async Task<List<ProcessedAsset>> ProcessAssetsAsync(
        string sourcePath,
        IReadOnlyList<string> themeNames,
        BuildOptions options,
        ConcurrentBag<BuildError> errors,
        CancellationToken cancellationToken)
    {
        // 资源收集（对齐 Hugo 主题语义）：站点 assets/static 优先，主题目录回退——
        // 同一相对路径站点覆盖主题（与模板查找同规则）。键形态 "static/<rel>"、
        // "assets/<rel>"，跨目录不冲突
        var assetsByRelative = new Dictionary<string, (string Path, bool IsTheme)>(StringComparer.OrdinalIgnoreCase);
        CollectAssetFiles(Path.Combine(sourcePath, "assets"), "assets", isTheme: false, assetsByRelative);
        CollectAssetFiles(Path.Combine(sourcePath, "static"), "static", isTheme: false, assetsByRelative);

        // 主题列表按序收集（前面的优先，TryAdd 先到先得形成覆盖链）
        foreach (var themeName in themeNames)
        {
            var themeRoot = Path.Combine(sourcePath, "themes", themeName);
            CollectAssetFiles(Path.Combine(themeRoot, "assets"), "assets", isTheme: true, assetsByRelative);
            CollectAssetFiles(Path.Combine(themeRoot, "static"), "static", isTheme: true, assetsByRelative);
        }

        var results = new ConcurrentBag<ProcessedAsset>();

        await Parallel.ForEachAsync(
            assetsByRelative,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = options.Parallelism,
                CancellationToken = cancellationToken
            },
            async (entry, ct) =>
            {
                var (relativeKey, (filePath, isTheme)) = entry;
                try
                {
                    var asset = new AssetFile
                    {
                        SourcePath = filePath,
                        MediaType = GetMediaType(filePath),
                        Content = await File.ReadAllBytesAsync(filePath, ct),
                        ModifiedTime = File.GetLastWriteTimeUtc(filePath)
                    };

                    var processed = await _assetPipeline.ProcessAsync(asset, ct);
                    // 主题文件的物理路径含 themes/<name>/ 前缀，产物 OutputPath 派生自
                    // 该物理路径——重映射回站点相对位置（"static/<rel>"、"assets/<rel>"），
                    // 主题资源与站点资源输出到同一命名空间；非预期形态保留原样
                    if (isTheme)
                    {
                        var remapped = RemapThemeOutputPath(processed.OutputPath, themeNames);
                        if (remapped is not null)
                        {
                            processed = processed with { OutputPath = remapped };
                        }
                    }
                    results.Add(processed);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errors.Add(new BuildError
                    {
                        FilePath = filePath,
                        Line = 0,
                        Column = 0,
                        Message = ex.Message,
                        ErrorCode = "ASSET001"
                    });
                }
            });

        return [.. results];
    }

    /// <summary>
    /// 收集目录下的资源文件到 relativeKey → 物理路径 字典；键 = "<segment>/<相对路径>"。
    /// 已存在的键不覆盖（先注册者胜——站点先于主题注册即形成覆盖语义）
    /// </summary>
    private static void CollectAssetFiles(
        string directory, string segment, bool isTheme,
        Dictionary<string, (string Path, bool IsTheme)> assetsByRelative)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(directory, file).Replace('\\', '/');
            var key = segment + "/" + rel;
            assetsByRelative.TryAdd(key, (file, isTheme));
        }
    }

    /// <summary>
    /// 主题产物路径重映射：把 OutputPath 中 "themes/&lt;name&gt;/(static|assets)/" 之前的
    /// 部分（输出根 + 主题目录链）剥除，保留 "&lt;static|assets&gt;/&lt;rel&gt;" 尾段——
    /// 与站点同名资源的输出位置一致；不匹配预期形态时返回 null（保留原样）
    /// </summary>
    private static string? RemapThemeOutputPath(string outputPath, IReadOnlyList<string> themeNames)
    {
        var normalized = outputPath.Replace('\\', '/');
        foreach (var themeName in themeNames)
        {
            var marker = "/themes/" + themeName + "/";
            var idx = normalized.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                continue;
            }

            var tail = normalized[(idx + marker.Length)..];
            return tail.StartsWith("static/", StringComparison.OrdinalIgnoreCase) ||
                   tail.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)
                ? tail
                : null;
        }
        return null;
    }

    private async Task GenerateSitemapAndFeedsAsync(
        List<PageContext> pages,
        SiteContext siteContext,
        SiteConfig config,
        BuildOptions options,
        CancellationToken cancellationToken)
    {
        // disableKinds 消费点（对齐 Hugo 语义）：RSS/sitemap 可被配置禁用；
        // outputs 配置消费点（T3.2）：home 输出格式列表不含 rss 时不产出订阅文件
        var disabled = config.DisableKinds;
        var sitemapDisabled = disabled.Contains("sitemap", StringComparer.OrdinalIgnoreCase)
            || disabled.Contains("Sitemap", StringComparer.OrdinalIgnoreCase);
        var rssDisabled = disabled.Contains("rss", StringComparer.OrdinalIgnoreCase)
            || disabled.Contains("RSS", StringComparer.OrdinalIgnoreCase)
            || !OutputFormats.Includes(config.Outputs.Home, "rss");

        var homePage = pages.FirstOrDefault(p => p.Type == "home") ?? pages.FirstOrDefault();

        // 生成 Sitemap（主题兼容批次二 #9：站点/主题 sitemap 模板存在时覆盖内置生成器）
        if (!sitemapDisabled)
        {
            if (_templateRenderer.TemplateExists("sitemap"))
            {
                var ctx = new TemplateContext { Site = siteContext, Page = homePage ?? new PageContext
                {
                    Title = config.Title, Content = "", Permalink = config.BaseURL,
                    RelPermalink = "/", Date = DateTimeOffset.Now, Tags = [], Categories = [],
                    WordCount = 0, ReadingTime = TimeSpan.Zero, Type = "home"
                }};
                var sitemap = await _templateRenderer.RenderAsync("sitemap", ctx, cancellationToken);
                await File.WriteAllTextAsync(Path.Combine(options.OutputPath, "sitemap.xml"), sitemap, cancellationToken);
            }
            else
            {
                var sitemapGenerator = new SitemapGenerator(config.BaseURL);
                var sitemap = sitemapGenerator.Generate(pages);
                await File.WriteAllTextAsync(Path.Combine(options.OutputPath, "sitemap.xml"), sitemap, cancellationToken);
            }
        }

        if (rssDisabled)
        {
            return;
        }

        // 生成 RSS Feed
        var feedOptions = new FeedOptions
        {
            Title = config.Title,
            BaseUrl = config.BaseURL,
            Language = config.LanguageCode,
            FeedPath = "/rss.xml"
        };
        var feedGenerator = new FeedGenerator(feedOptions);

        // 主题兼容批次二 #9：rss 模板存在时覆盖内置生成器
        if (_templateRenderer.TemplateExists("rss"))
        {
            var rssCtx = new TemplateContext { Site = siteContext, Page = homePage ?? new PageContext
            {
                Title = config.Title, Content = "", Permalink = config.BaseURL,
                RelPermalink = "/", Date = DateTimeOffset.Now, Tags = [], Categories = [],
                WordCount = 0, ReadingTime = TimeSpan.Zero, Type = "home"
            }};
            var rssContent = await _templateRenderer.RenderAsync("rss", rssCtx, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(options.OutputPath, "rss.xml"), rssContent, cancellationToken);
            return;
        }

        var rss = feedGenerator.GenerateRss(pages);
        var rssPath = Path.Combine(options.OutputPath, "rss.xml");
        await File.WriteAllTextAsync(rssPath, rss, cancellationToken);

        // 生成 Atom Feed
        feedOptions = feedOptions with { FeedPath = "/atom.xml" };
        var atomGenerator = new FeedGenerator(feedOptions);
        var atom = atomGenerator.GenerateAtom(pages);
        var atomPath = Path.Combine(options.OutputPath, "atom.xml");
        await File.WriteAllTextAsync(atomPath, atom, cancellationToken);
    }

    private async Task WriteOutputAsync(
        List<RenderedPage> pages,
        List<RenderedPage> taxonomyPages,
        List<ProcessedAsset> assets,
        BuildOptions options,
        CancellationToken cancellationToken)
    {
        // 确保输出目录存在
        Directory.CreateDirectory(options.OutputPath);

        // 优化：并行写入页面（去重以避免并发写入同一文件）
        var allPages = pages.Concat(taxonomyPages)
            .GroupBy(p => p.OutputPath)
            .Select(g => g.First())  // 去重：相同路径只保留第一个
            .ToArray();

        await Parallel.ForEachAsync(
            allPages,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = cancellationToken
            },
            async (page, ct) =>
            {
                var dir = Path.GetDirectoryName(page.OutputPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                // 添加重试逻辑以处理文件访问冲突
                await WriteFileWithRetryAsync(page.OutputPath, page.Content, ct);
            });

        // 优化：并行写入资源（去重以避免并发写入同一文件）
        var uniqueAssets = assets
            .GroupBy(a => Path.Combine(options.OutputPath, a.OutputPath.TrimStart('/')))
            .Select(g => g.First())
            .ToArray();

        await Parallel.ForEachAsync(
            uniqueAssets,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = cancellationToken
            },
            async (asset, ct) =>
            {
                var outputPath = Path.Combine(options.OutputPath, asset.OutputPath.TrimStart('/'));
                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                // 添加重试逻辑以处理文件访问冲突
                await WriteBytesWithRetryAsync(outputPath, asset.Content.ToArray(), ct);
            });
    }

    /// <summary>
    /// 带重试的文件写入（处理文件访问冲突）
    /// </summary>
    private static async Task WriteFileWithRetryAsync(string path, string content, CancellationToken ct, int maxRetries = 3)
    {
        for (var i = 0; i < maxRetries; i++)
        {
            try
            {
                await File.WriteAllTextAsync(path, content, ct);
                return;
            }
            catch (IOException) when (i < maxRetries - 1)
            {
                // 等待一小段时间后重试
                await Task.Delay(10 * (i + 1), ct);
            }
        }
    }

    /// <summary>
    /// 带重试的字节写入（处理文件访问冲突）
    /// </summary>
    private static async Task WriteBytesWithRetryAsync(string path, byte[] content, CancellationToken ct, int maxRetries = 3)
    {
        for (var i = 0; i < maxRetries; i++)
        {
            try
            {
                await File.WriteAllBytesAsync(path, content, ct);
                return;
            }
            catch (IOException) when (i < maxRetries - 1)
            {
                // 等待一小段时间后重试
                await Task.Delay(10 * (i + 1), ct);
            }
        }
    }

    /// <summary>按输出格式计算输出路径：非 html 格式用其 BaseName+Extension（index.json 等）</summary>
    private static string GetOutputPathForFormat(
        string relPermalink, Site.OutputFormat format, string outputPath)
    {
        if (format.Name == "html")
        {
            return GetOutputPath(relPermalink, outputPath);
        }

        // 目录形态 permalink → /x/index.json；文件形态 → /x.json
        var rel = relPermalink.TrimEnd('/');
        if (rel.Length == 0)
        {
            return Path.Combine(outputPath, format.BaseName + format.Extension);
        }
        // rel 源自 permalink（用户可控），与 GetOutputPath 同样做越界护栏
        // （补尾分隔符防兄弟目录前缀误判）
        var relPath = rel.TrimStart('/');
        // 文件形态 permalink（"/:title.html" → "foo.html"）：先剥 .html 再拼格式名，
        // 否则产出 foo.html\index.json 目录，与 html 输出的同名文件冲突
        if (relPath.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            relPath = relPath[..^5];
        }
        var combined = Path.Combine(outputPath, relPath, format.BaseName + format.Extension);
        var outputRoot = Path.GetFullPath(outputPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(combined).StartsWith(outputRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"页面输出路径逃逸输出目录，已拒绝: {relPermalink}");
        }
        return combined;
    }

    /// <summary>站点优先、主题回退的辅助模板文件查找（如 robots.txt）</summary>
    private static string? FindAuxTemplateFile(string sourcePath, SiteConfig config, string fileName)
    {
        var layoutDirName = string.IsNullOrEmpty(config.LayoutDir) ? "layouts" : config.LayoutDir;
        foreach (var themeName in config.ThemeNames.Reverse())
        {
            var candidate = Path.Combine(sourcePath, "themes", themeName, layoutDirName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        var siteCandidate = Path.Combine(sourcePath, layoutDirName, fileName);
        return File.Exists(siteCandidate) ? siteCandidate : null;
    }

    private static string GetOutputPath(string relPermalink, string outputPath)
    {
        var path = relPermalink.TrimStart('/');
        if (path.Length == 0)
        {
            // 首页 "/"：TrimStart 后为空串。Path.Combine(output, "") 返回 output 自身，
            // 而把 "/" 直接参与 Combine 会因根路径语义返回盘根
            return Path.Combine(outputPath, "index.html");
        }
        if (path.EndsWith('/'))
        {
            path += "index.html";
        }
        else if (!path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            path += "/index.html";
        }

        // permalink 里的 slug 源自 front matter（用户可控），含 ".." 时会写出输出目录之外——
        // 组合后做前缀校验，越界即 fail loud（补尾分隔符防兄弟目录前缀误判）
        var combined = Path.Combine(outputPath, path);
        var outputRoot = Path.GetFullPath(outputPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(combined).StartsWith(outputRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"页面输出路径逃逸输出目录，已拒绝: {relPermalink}");
        }
        return combined;
    }

    private static string GetMediaType(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".json" => "application/json",
            ".html" => "text/html",
            ".xml" => "application/xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".webp" => "image/webp",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".eot" => "application/vnd.ms-fontobject",
            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// 辅助输出（主题兼容批次一）：front matter aliases 重定向页、404 模板、robots.txt 模板。
    /// 全部为可选能力——模板不存在/页面未声明别名时零产出
    /// </summary>
    private async Task GenerateAuxiliaryOutputsAsync(
        List<PageContext> pages,
        SiteContext siteContext,
        SiteConfig config,
        BuildOptions options,
        CancellationToken cancellationToken)
    {
        // 1. aliases 重定向页（Hugo front matter aliases 语义）：每个别名路径产出
        //    meta-refresh 页面，指向页面真实 Permalink；路径逃逸的别名跳过
        foreach (var page in pages.Where(p => p.Aliases is { Count: > 0 }))
        {
            foreach (var alias in page.Aliases!)
            {
                var aliasOutput = ResolveAliasOutputPath(alias, options.OutputPath);
                if (aliasOutput is null)
                {
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(aliasOutput)!);
                var redirect = "<!DOCTYPE html>\n<html><head><meta charset=\"utf-8\">" +
                    $"<title>{System.Net.WebUtility.HtmlEncode(page.Title)}</title>" +
                    $"<link rel=\"canonical\" href=\"{page.Permalink}\">" +
                    $"<meta http-equiv=\"refresh\" content=\"0; url={page.Permalink}\">" +
                    $"</head><body>Redirecting to <a href=\"{page.Permalink}\">{page.Permalink}</a></body></html>\n";
                await File.WriteAllTextAsync(aliasOutput, redirect, cancellationToken);
            }
        }

        // 2. 404 模板（layouts/404.html 存在时渲染输出 404.html）
        if (_templateRenderer.TemplateExists("404"))
        {
            var notFoundPage = new PageContext
            {
                Title = "404 Page not found",
                Content = "",
                Permalink = config.BaseURL.TrimEnd('/') + "/404.html",
                RelPermalink = "/404.html",
                Date = DateTimeOffset.Now,
                Tags = [],
                Categories = [],
                WordCount = 0,
                ReadingTime = TimeSpan.Zero,
                Type = "page"
            };
            var ctx = new TemplateContext { Page = notFoundPage, Site = siteContext };
            var html = await _templateRenderer.RenderAsync("404", ctx, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(options.OutputPath, "404.html"), html, cancellationToken);
        }

        // 3. robots.txt 模板（layouts/robots.txt 站点优先、主题回退）。
        // 文件直读+模板渲染（模板查找链只扫 .html，不覆盖 .txt 形态）
        var robotsSrc = FindAuxTemplateFile(options.SourcePath, config, "robots.txt");
        if (robotsSrc is not null)
        {
            var robotsPage = new PageContext
            {
                Title = "robots",
                Content = "",
                Permalink = config.BaseURL.TrimEnd('/') + "/robots.txt",
                RelPermalink = "/robots.txt",
                Date = DateTimeOffset.Now,
                Tags = [],
                Categories = [],
                WordCount = 0,
                ReadingTime = TimeSpan.Zero,
                Type = "page"
            };
            var ctx = new TemplateContext { Page = robotsPage, Site = siteContext };
            var txt = await _templateRenderer.RenderTemplateFileAsync(robotsSrc, ctx, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(options.OutputPath, "robots.txt"), txt, cancellationToken);
        }
    }

    /// <summary>
    /// 别名 → 输出路径（目录形态 index.html）；路径逃逸（越出输出根）返回 null
    /// </summary>
    private static string? ResolveAliasOutputPath(string alias, string outputRootPath)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return null;
        }
        var rel = alias.Trim().TrimStart('/');
        if (rel.Length == 0)
        {
            return null;
        }
        if (rel.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            rel = rel[..^5];
        }
        var combined = Path.Combine(outputRootPath, rel.Replace('/', Path.DirectorySeparatorChar), "index.html");
        var outputRoot = Path.GetFullPath(outputRootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(combined).StartsWith(outputRoot, StringComparison.OrdinalIgnoreCase))
        {
            return null; // 逃逸拒绝
        }
        return combined;
    }

}
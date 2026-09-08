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
        BuildOptions options,
        ConcurrentBag<BuildError> errors,
        CancellationToken cancellationToken)
    {
        var assetsPath = Path.Combine(sourcePath, "assets");
        var staticPath = Path.Combine(sourcePath, "static");
        var results = new ConcurrentBag<ProcessedAsset>();

        var assetFiles = new List<string>();
        if (Directory.Exists(assetsPath))
        {
            assetFiles.AddRange(Directory.EnumerateFiles(assetsPath, "*", SearchOption.AllDirectories));
        }
        if (Directory.Exists(staticPath))
        {
            assetFiles.AddRange(Directory.EnumerateFiles(staticPath, "*", SearchOption.AllDirectories));
        }

        await Parallel.ForEachAsync(
            assetFiles,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = options.Parallelism,
                CancellationToken = cancellationToken
            },
            async (filePath, ct) =>
            {
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

    private async Task GenerateSitemapAndFeedsAsync(
        List<PageContext> pages,
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

        // 生成 Sitemap
        if (!sitemapDisabled)
        {
            var sitemapGenerator = new SitemapGenerator(config.BaseURL);
            var sitemap = sitemapGenerator.Generate(pages);
            var sitemapPath = Path.Combine(options.OutputPath, "sitemap.xml");
            await File.WriteAllTextAsync(sitemapPath, sitemap, cancellationToken);
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
}

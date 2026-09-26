// Flint 静态站点生成器
// SiteBuilder 输出写入聚合：输出目录清理、资源处理、sitemap/feed、文件写入与输出路径计算

using System.Collections.Concurrent;
using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.IO;
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
        CancellationToken cancellationToken,
        string basePathPrefix = "")
    {
        // 资源收集（对齐 Hugo 语义）：站点 assets/static 优先，主题目录回退——
        // 同一输出相对路径站点覆盖主题。**输出前缀语义**（Hugo 对齐，2026-09-10 实证）：
        //   static/ 内容映射到输出根（static/css/a.css → public/css/a.css）
        //   assets/ 内容保留 assets/ 前缀（Flint 扩展：assets 直接发布，Hugo 需 resources.Get）
        // 键 = 输出相对路径，站点先注册即形成覆盖（与模板查找同规则）。
        // **basePathPrefix**：baseURL 子路径（如 fixit/）——多主题站按子路径
        // 归并单一端口时，静态资源也要落到各主题子目录
        var outputPrefixBase = string.IsNullOrEmpty(basePathPrefix)
            ? ""
            : basePathPrefix.Trim('/') + "/";
        //   assets/ 内容保留 assets/ 前缀（Flint 扩展：assets 直接发布，Hugo 需 resources.Get）
        // 键 = 输出相对路径，站点先注册即形成覆盖（与模板查找同规则）
        var assetsByRelative = new Dictionary<string, (string Path, bool IsTheme, string OutputRelative)>(StringComparer.OrdinalIgnoreCase);
        // **资源发布在站根**（对齐 Hugo v0.166 实测：`assets/main.css` 的
        // `.RelPermalink` = `/main.css`，即"相对 assets/ 根"，不加 `assets/` 前缀）。
        // 此前统一放到 `/assets/…`：链接文本与 Hugo 全线不同，且**主题 CSS 内的相对引用
        // 会 404**（hugo-paper 的 `url(./theme.png)`：Hugo 下解析为 /theme.png ✔、
        // Flint 下解析为 /assets/theme.png ✗）。static/ 本就映射到输出根，两者同前缀即
        // Hugo 的"站点资源同一命名空间"语义（static 先收集 → 同路径时 static 胜）
        CollectAssetFiles(Path.Combine(sourcePath, "assets"), outputPrefixBase, isTheme: false, assetsByRelative);
        CollectAssetFiles(Path.Combine(sourcePath, "static"), outputPrefixBase, isTheme: false, assetsByRelative);

        // 主题列表按序收集（前面的优先，TryAdd 先到先得形成覆盖链）
        foreach (var themeName in themeNames)
        {
            var themeRoot = Path.Combine(sourcePath, "themes", themeName);
            CollectAssetFiles(Path.Combine(themeRoot, "assets"), outputPrefixBase, isTheme: true, assetsByRelative);
            CollectAssetFiles(Path.Combine(themeRoot, "static"), outputPrefixBase, isTheme: true, assetsByRelative);
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
                var (outputRelative, (filePath, _, _)) = entry;
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
                    // 输出路径以收集期计算的「输出相对路径」为准（含 static 剥前缀、
                    // 主题链剥离）——不依赖物理路径推导，站点与主题资源统一命名空间
                    processed = RepathAsset(processed, outputRelative, options.OutputPath);
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
    /// 按输出相对路径重定位资源产物（OutputPath 与 FingerprintedPath 同步）；
    /// 指纹文件名沿用处理器的 ContentHash，仅目录前缀被替换
    /// </summary>
    private static ProcessedAsset RepathAsset(ProcessedAsset asset, string outputRelative, string outputRoot)
    {
        var outputPath = Path.Combine(
            outputRoot, outputRelative.Replace('/', Path.DirectorySeparatorChar));
        string? fingerprinted = null;
        if (!string.IsNullOrEmpty(asset.FingerprintedPath) && !string.IsNullOrEmpty(asset.ContentHash))
        {
            fingerprinted = Path.Combine(
                Path.GetDirectoryName(outputPath) ?? string.Empty,
                $"{Path.GetFileNameWithoutExtension(outputPath)}.{asset.ContentHash[..8]}{Path.GetExtension(outputPath)}");
        }
        return asset with { OutputPath = outputPath, FingerprintedPath = fingerprinted };
    }

    /// <summary>
    /// 收集目录下的资源到「输出相对路径 → (物理路径, 是否主题)」字典。
    /// outputPrefix 为输出前缀（assets/ 保留、static/ 为空表示映射到输出根）；
    /// TryAdd 先到先得形成站点覆盖主题的优先级链
    /// </summary>
    private static void CollectAssetFiles(
        string directory, string outputPrefix, bool isTheme,
        Dictionary<string, (string Path, bool IsTheme, string OutputRelative)> assetsByRelative)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(directory, file).Replace('\\', '/');
            var key = outputPrefix + rel;
            assetsByRelative.TryAdd(key, (file, isTheme, key));
        }
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

        var homePage = pages.FirstOrDefault(p => p.Kind == "home") ?? pages.FirstOrDefault();

        // **baseURL 子路径前缀**：sitemap/RSS/atom 也要落到各主题子目录
        var feedPrefix = Flint.Core.Templates.TemplateResource.BasePathOf(config.BaseURL)
            .TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var feedRoot = string.IsNullOrEmpty(feedPrefix)
            ? options.OutputPath
            : Path.Combine(options.OutputPath, feedPrefix);
        Directory.CreateDirectory(feedRoot);

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
                await File.WriteAllTextAsync(Path.Combine(feedRoot, "sitemap.xml"), sitemap, cancellationToken);
            }
            else
            {
                var sitemapGenerator = new SitemapGenerator(config.BaseURL);
                var sitemap = sitemapGenerator.Generate(pages);
                await File.WriteAllTextAsync(Path.Combine(feedRoot, "sitemap.xml"), sitemap, cancellationToken);
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
        var useTemplate = _templateRenderer.TemplateExists("rss");
        if (useTemplate)
        {
            var rssCtx = new TemplateContext { Site = siteContext, Page = homePage ?? new PageContext
            {
                Title = config.Title, Content = "", Permalink = config.BaseURL,
                RelPermalink = "/", Date = DateTimeOffset.Now, Tags = [], Categories = [],
                WordCount = 0, ReadingTime = TimeSpan.Zero, Type = "home"
            }};
            var rssContent = await _templateRenderer.RenderAsync("rss", rssCtx, cancellationToken);
            await WriteRssAliasesAsync(options, rssContent, cancellationToken, feedRoot);
        }
        else
        {
            var rss = feedGenerator.GenerateRss(pages);
            await WriteRssAliasesAsync(options, rss, cancellationToken, feedRoot);

            // 生成 Atom Feed
            feedOptions = feedOptions with { FeedPath = "/atom.xml" };
            var atomGenerator = new FeedGenerator(feedOptions);
            var atom = atomGenerator.GenerateAtom(pages);
            var atomPath = Path.Combine(feedRoot, "atom.xml");
            await File.WriteAllTextAsync(atomPath, atom, cancellationToken);
        }

        // **各列表页的 RSS**（Hugo v0.166 探针）：home/section/taxonomy/term 各自产出
        // `<列表页>/index.xml`（如 /posts/index.xml、/tags/intro/index.xml）。主题 head 里的
        // `.OutputFormats.Get "rss"` 链接指向这些文件——列表页缺 RSS 产物时
        // `<link rel="alternate">` 404（ananke 全站 9 个 index.xml 缺失，实测）。
        // 首页的 /index.xml 已由上方别名写出，此处只处理其余列表页与分页页。
        // **注意顺序**：rss 模板存在时必须**先写完根 RSS 再循环各列表页**——
        // 原先模板分支 `return` 会跳过列表页 RSS（blog-awesome/fixit 实测）
        // **每个列表页（含分页页）各自产出 RSS**：Hugo v0.166 探针——/posts/ 产出
        // /posts/index.xml，分页页产出 /posts/page/2/index.xml；taxonomy/term 同理。
        // 主题 head 的 `.OutputFormats.Get "rss"` 链接指向这些文件——缺产物即 404
        //（ananke 全站 9 个 index.xml 缺失、fixit/loveit 订阅链接缺失，实测）。
        // 分页信息来自分页注册表（模板调用过 .Paginate/.Paginator 的列表页才登记）
        // **taxonomy/term 页不在 pageContexts 里**（分类渲染阶段单独构建），需并入
        foreach (var listPage in pages
            .Where(p => p.Kind is "home" or "section" or "taxonomy" or "term" &&
                        !string.IsNullOrEmpty(p.RelPermalink))
            .Concat(TaxonomyFeedContexts.Where(c => c.Pages is { Count: > 0 })))
        {
            var baseRel = listPage.RelPermalink!.TrimEnd('/');
            if (baseRel.Length == 0)
            {
                baseRel = "/";
            }

            var registration = Flint.Core.Templates.ScribanTemplateRenderer.GetPaginateCollection(baseRel);
            var items = registration?.Items ?? listPage.Pages ?? [];
            var size = registration is { Size: > 0 } reg ? reg.Size : Math.Max(1, config.Paginate);
            if (items.Count == 0)
            {
                size = 1;
            }

            var target = Path.Combine(
                options.OutputPath, baseRel.TrimStart('/').Replace('/', Path.DirectorySeparatorChar), "index.xml");
            OutputDirectoryEnsurer.Ensure(Path.GetDirectoryName(target)!);

            if (useTemplate)
            {
                var rssCtx = new TemplateContext { Page = listPage, Site = siteContext };
                var content = await _templateRenderer.RenderAsync("rss", rssCtx, cancellationToken);
                await File.WriteAllTextAsync(target, content, cancellationToken);
            }
            else
            {
                var sectionFeed = new FeedGenerator(feedOptions with { FeedPath = baseRel + "/index.xml" });
                var sectionRss = sectionFeed.GenerateRss(items);
                await File.WriteAllTextAsync(target, sectionRss, cancellationToken);
            }
        }
    }

    /// <summary>
    /// 首页 RSS 的产物名：**主名 <c>index.xml</c>**（Hugo v0.166 实测：首页 RSS 输出路径
    /// 就是 <c>/index.xml</c>，各 section 是 <c>{section}/index.xml</c>），
    /// <c>rss.xml</c> 作为 Flint 历史别名一并写出（老站点/工具可能直接引它）。
    /// 此前只写 <c>rss.xml</c> → 主题的 feed 链接
    /// <c>&lt;link rel="alternate" href="/index.xml"&gt;</c> 404（fixit 实测）
    /// </summary>
    private static async Task WriteRssAliasesAsync(
        BuildOptions options, string content, CancellationToken cancellationToken,
        string feedRootPath = "")
    {
        var root = string.IsNullOrEmpty(feedRootPath) ? options.OutputPath : feedRootPath;
        foreach (var name in new[] { "index.xml", "rss.xml" })
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, name), content, cancellationToken);
        }
    }

    private async Task WriteOutputAsync(
        List<RenderedPage> pages,
        List<RenderedPage> taxonomyPages,
        List<ProcessedAsset> assets,
        BuildOptions options,
        CancellationToken cancellationToken)
    {
        // 确保输出目录存在
        OutputDirectoryEnsurer.Ensure(options.OutputPath);

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
                    OutputDirectoryEnsurer.Ensure(dir);
                }
                // html 产物对齐 Go html/template 的 void 元素序列化（/> 剥斜杠）
                var content = page.OutputPath.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                    ? HtmlOutputNormalizer.Normalize(page.Content)
                    : page.Content;
                // 添加重试逻辑以处理文件访问冲突
                await WriteFileWithRetryAsync(page.OutputPath, content, ct);
            });

        // 优化：并行写入资源（去重以避免并发写入同一文件）
        // 资源产物路径由 ProcessAssetsAsync 统一计算（含 static 剥前缀），
        // 此处仅解析为绝对输出路径（已是绝对路径时直接采用）
        var uniqueAssets = assets
            .GroupBy(a => ResolveAssetOutputPath(a.OutputPath, options.OutputPath))
            .Select(g => g.First())
            // 目录形态输出路径（无文件名，如模板产物名仅空白产生的 "assets/"）不可作为
            // 文件写入——写盘会抛 DirectoryNotFoundException 并中断整次构建。此类条目
            // 无有效载荷，直接排除（上游 TemplateResourceFunctions.Track 已挡一层，
            // 此处是不依赖调用链的第二道防线）
            .Where(a => Path.GetFileName(a.OutputPath).Length > 0)
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
                var outputPath = ResolveAssetOutputPath(asset.OutputPath, options.OutputPath);
                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    OutputDirectoryEnsurer.Ensure(dir);
                }
                // 添加重试逻辑以处理文件访问冲突
                await WriteBytesWithRetryAsync(outputPath, asset.Content.ToArray(), ct);
            });
    }

    /// <summary>
    /// 资源产物输出路径解析：已是绝对路径（ProcessAssetsAsync 统一计算的结果）直接采用，
    /// 否则按输出根拼接（兼容旧形态的调用方）
    /// </summary>
    private static string ResolveAssetOutputPath(string assetOutputPath, string outputRoot)
    {
        return Path.IsPathRooted(assetOutputPath)
            ? assetOutputPath
            : Path.Combine(outputRoot, assetOutputPath.TrimStart('/', '\\'));
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

    /// <summary>**文件形态**辅助输出的落盘路径（robots.txt 这类非页面产物）：
    /// RelPermalink 即输出文件的相对路径，不再补 <c>index.html</c>（GetOutputPath
    /// 会把 <c>/robots.txt</c> 解析成目录 <c>robots.txt/index.html</c>，写出时报
    /// "找不到路径的一部分"——ananke/bearblog 带 robots.txt 模板时实测）</summary>
    private static string GetFileFormOutputPath(string relPermalink, string outputPath)
    {
        var path = relPermalink.TrimStart('/');
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
            // 文本类（A3 后 static/ 直通输出根，文本类型必须正确而非 octet-stream）
            ".css" => "text/css",
            ".js" or ".mjs" => "application/javascript",
            ".json" => "application/json",
            ".html" or ".htm" => "text/html",
            ".xml" => "application/xml",
            ".txt" => "text/plain",
            ".md" => "text/markdown",
            ".csv" => "text/csv",
            ".yml" or ".yaml" => "text/yaml",
            ".toml" => "text/plain",
            ".map" => "application/json",
            // 图像
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".webp" => "image/webp",
            ".avif" => "image/avif",
            ".ico" => "image/x-icon",
            ".bmp" => "image/bmp",
            // 字体
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".otf" => "font/otf",
            ".eot" => "application/vnd.ms-fontobject",
            // 文档与音视频
            ".pdf" => "application/pdf",
            ".zip" => "application/zip",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".mp3" => "audio/mpeg",
            ".ogg" => "audio/ogg",
            ".wasm" => "application/wasm",
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
                OutputDirectoryEnsurer.Ensure(Path.GetDirectoryName(aliasOutput)!);
                var redirect = "<!DOCTYPE html>\n<html><head><meta charset=\"utf-8\">" +
                    $"<title>{System.Net.WebUtility.HtmlEncode(page.Title)}</title>" +
                    $"<link rel=\"canonical\" href=\"{page.Permalink}\">" +
                    $"<meta http-equiv=\"refresh\" content=\"0; url={page.Permalink}\">" +
                    $"</head><body>Redirecting to <a href=\"{page.Permalink}\">{page.Permalink}</a></body></html>\n";
                await File.WriteAllTextAsync(aliasOutput, redirect, cancellationToken);
            }
        }

        // 2. 404 模板（layouts/404.html 存在时渲染输出 404.html）
        //    空文件例外：Hugo v0.166 实测 0 字节的 404 模板**不产出** 404.html
        //（monochrome 的 layouts/404.html 正是 0 字节；而同样 0 字节的
        //  `_default/single.html`/`_partials/x.html` Hugo 照用作模板）
        if (_templateRenderer.TemplateExists("404") && !_templateRenderer.TemplateFileIsEmpty("404"))
        {
            var notFoundPage = new PageContext
            {
                Title = "404 Page not found",
                Content = "",
                Permalink = config.BaseURL.TrimEnd('/') + "/404.html",
                // 子路径构建时 rel 带前缀，输出才能落到 /<主题>/404.html
                RelPermalink = Templates.TemplateResource.BasePathOf(config.BaseURL) + "/404.html",
                Date = DateTimeOffset.Now,
                Tags = [],
                Categories = [],
                WordCount = 0,
                ReadingTime = TimeSpan.Zero,
                Type = "page"
            };
            var ctx = new TemplateContext { Page = notFoundPage, Site = siteContext };
            var html = await _templateRenderer.RenderAsync("404", ctx, cancellationToken);
            var notFoundOutput = GetFileFormOutputPath(notFoundPage.RelPermalink, options.OutputPath);
            // 子路径构建时输出目录（<out>/<sub>/）可能还没有任何页面写过——
            // 无内容的站点 404 也会产出（Hugo 语义），先建目录再写
            OutputDirectoryEnsurer.Ensure(Path.GetDirectoryName(notFoundOutput)!);
            await File.WriteAllTextAsync(notFoundOutput, html, cancellationToken);
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
                // 子路径构建时 rel 带前缀，输出才能落到 /<主题>/robots.txt
                RelPermalink = Templates.TemplateResource.BasePathOf(config.BaseURL) + "/robots.txt",
                Date = DateTimeOffset.Now,
                Tags = [],
                Categories = [],
                WordCount = 0,
                ReadingTime = TimeSpan.Zero,
                Type = "page"
            };
            var ctx = new TemplateContext { Page = robotsPage, Site = siteContext };
            var txt = await _templateRenderer.RenderTemplateFileAsync(robotsSrc, ctx, cancellationToken);
            // 文件形态输出：RelPermalink 即落盘相对路径（不补 index.html）
            var robotsOutput = GetFileFormOutputPath(robotsPage.RelPermalink, options.OutputPath);
            OutputDirectoryEnsurer.Ensure(Path.GetDirectoryName(robotsOutput)!);
            await File.WriteAllTextAsync(robotsOutput, txt, cancellationToken);
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
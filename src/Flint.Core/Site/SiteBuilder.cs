// Flint 静态站点生成器
// 站点构建器实现

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Flint.Core.Abstractions;
using Flint.Core.Content;
using Flint.Core.Content.Shortcodes;
using Flint.Core.Models;
using Flint.Core.Templates;
using PageBundleType = Flint.Core.Site.PageTrees.PageBundleType;
using PageTree = Flint.Core.Site.PageTrees.PageTree;
using PageTreeNode = Flint.Core.Site.PageTrees.PageTreeNode;
using PageTreeWalker = Flint.Core.Site.PageTrees.PageTreeWalker;

namespace Flint.Core.Site;

/// <summary>
/// 站点构建器
/// 使用 Channel 并发管道实现高效构建
/// </summary>
public sealed class SiteBuilder : ISiteBuilder
{
    private readonly IContentParser _contentParser;
    private readonly ITemplateRenderer _templateRenderer;
    private readonly IAssetPipeline _assetPipeline;
    private readonly IConfigLoader _configLoader;
    private readonly SiteBuilderOptions _options;
    private readonly DataFileLoader _dataLoader = new();
    private readonly ContentHashCache? _contentCache;
    // 页面 → 模板（含 partial 闭包）依赖图：模板变化时扩展出受影响页面
    private readonly IncrementalBuilder _dependencyGraph = new();
    // 当前构建周期的页面树（T2.3 增量构建做 section 级精确替换的查询基础）
    private PageTree? _currentPageTree;
    private bool _templatesPrecompiled;

    /// <summary>
    /// 创建站点构建器
    /// </summary>
    public SiteBuilder(
        IContentParser contentParser,
        ITemplateRenderer templateRenderer,
        IAssetPipeline assetPipeline,
        IConfigLoader configLoader,
        SiteBuilderOptions? options = null)
    {
        _contentParser = contentParser;
        _templateRenderer = templateRenderer;
        _assetPipeline = assetPipeline;
        _configLoader = configLoader;
        _options = options ?? new SiteBuilderOptions();
        // P3 优化：只在 watch 模式下启用内容哈希缓存
        _contentCache = _options.EnableContentCache ? new ContentHashCache() : null;
    }

    /// <inheritdoc />
    public async ValueTask<BuildResult> BuildAsync(
        BuildOptions options,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var errors = new ConcurrentBag<BuildError>();
        var warnings = new ConcurrentBag<BuildWarning>();

        // P2 优化：预编译模板（只在首次构建时执行）
        if (!_templatesPrecompiled && _templateRenderer is ScribanTemplateRenderer scribanRenderer)
        {
            await scribanRenderer.PrecompileTemplatesAsync(cancellationToken);
            _templatesPrecompiled = true;
        }

        try
        {
            // 0. 如果启用了 CleanOutput，先清理输出目录
            if (options.CleanOutput)
            {
                await CleanAsync(options.OutputPath, cancellationToken);
            }

            // 1. 加载配置（自动检测 Flint/config/hugo × TOML/YAML/JSON 全部候选文件名）
            var config = await _configLoader.AutoLoadAsync(
                options.SourcePath, cancellationToken);

            // 1.5 注册站点级短代码（layouts/shortcodes/*.html，对齐 Hugo"短代码即模板"；
            // 项目模板覆盖内置同名短代码）
            RegisterSiteShortcodes(options.SourcePath, config);

            // 2. 扫描内容文件
            var contentFiles = await ScanContentFilesAsync(options.SourcePath, cancellationToken);

            // 3. 使用 Channel 并发解析内容
            var parsedContents = await ParseContentsAsync(
                contentFiles, options, errors, cancellationToken);

            // 4. 过滤草稿和未来内容
            var filteredContents = FilterContents(parsedContents, options);

            // 5. 构建页面上下文
            var pageContexts = BuildPageContexts(filteredContents, config, options.SourcePath);

            // 5.5 注册 页面→模板 依赖（增量构建时模板变化可反查受影响页面）
            RegisterTemplateDependencies(pageContexts, config, options.SourcePath);

            // 6. 构建分类系统
            var taxonomyService = new TaxonomyService(config.BaseURL);
            var taxonomies = taxonomyService.BuildTaxonomies(pageContexts);

            // 7. 构建站点上下文（含 data/ 目录数据 → site.data 模板变量）
            var siteData = await LoadSiteDataAsync(options, cancellationToken);
            var siteContext = BuildSiteContext(config, pageContexts, taxonomies, siteData);

            // 8. 渲染页面
            var renderedPages = await RenderPagesAsync(
                pageContexts, siteContext, options, errors, cancellationToken);

            // 8.5 生成首页——仅当树中不存在 home 页时兜底：
            // kind 分派（home→index）已让树版 home 页（带完整 front matter/params）
            // 渲染 index.html；此处再手建一份会与树版写同一路径，
            // 且兜底页缺 front matter 数据，失败反而误报 HOME001 使构建假失败
            if (!pageContexts.Any(p => p.Type == "home"))
            {
                var homePage = await GenerateHomePageAsync(
                    siteContext, config, options, errors, cancellationToken);
                if (homePage != null)
                {
                    renderedPages.Add(homePage);
                }
            }

            // 9. 生成分类页面
            var taxonomyPages = await GenerateTaxonomyPagesAsync(
                taxonomies, siteContext, config, options, errors, cancellationToken);

            // 10. 处理资源文件
            var processedAssets = await ProcessAssetsAsync(
                options.SourcePath, options, errors, cancellationToken);

            // 11. 写入输出文件（先创建目录）
            await WriteOutputAsync(
                renderedPages, taxonomyPages, processedAssets, options, cancellationToken);

            // 12. 生成 Sitemap 和 Feed（在输出目录创建后）
            await GenerateSitemapAndFeedsAsync(
                pageContexts, config, options, cancellationToken);

            stopwatch.Stop();

            return new BuildResult
            {
                Success = errors.IsEmpty,
                PagesBuilt = renderedPages.Count + taxonomyPages.Count,
                AssetsProcessed = processedAssets.Count,
                Duration = stopwatch.Elapsed,
                MemoryUsed = GC.GetTotalMemory(false),
                Errors = [.. errors],
                Warnings = [.. warnings],
                OutputPath = options.OutputPath
            };
        }
        catch (OperationCanceledException)
        {
            // 取消不是构建失败：重抛交还调用方，错误列表不被假 BUILD001 污染
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            errors.Add(new BuildError
            {
                FilePath = options.SourcePath,
                Line = 0,
                Column = 0,
                Message = ex.Message,
                ErrorCode = "BUILD001"
            });

            return new BuildResult
            {
                Success = false,
                PagesBuilt = 0,
                AssetsProcessed = 0,
                Duration = stopwatch.Elapsed,
                MemoryUsed = GC.GetTotalMemory(false),
                Errors = [.. errors],
                Warnings = [.. warnings],
                OutputPath = options.OutputPath
            };
        }
    }

    /// <inheritdoc />
    public async ValueTask<BuildResult> IncrementalBuildAsync(
        BuildOptions options,
        IReadOnlyList<string> changedFiles,
        CancellationToken cancellationToken = default)
    {
        // 增量构建：只处理变化的文件
        var stopwatch = Stopwatch.StartNew();
        var errors = new ConcurrentBag<BuildError>();
        var warnings = new ConcurrentBag<BuildWarning>();

        try
        {
            var config = await _configLoader.AutoLoadAsync(
                options.SourcePath, cancellationToken);

            // 依赖图扩展：模板/partial 变化 → 使用它们的页面（此前依赖图无人喂，改 layout 页面不会更新）
            var affectedFiles = _dependencyGraph.GetAffectedFiles(changedFiles).ToList();

            // 全局 identity 防御：config/data/archetypes 变化影响面广（配置重载/
            // 全站数据/原型模板），增量装配的渲染集合只覆盖变化页+列表页，
            // 全量语义只能由全量构建保证——直接转全量，静默忽略会让修改对产物毫无影响
            var forcesFullAssemble = changedFiles.Any(f => IsGlobalIdentityPath(f, options.SourcePath));
            if (forcesFullAssemble)
            {
                return await BuildAsync(options, cancellationToken);
            }

            var contentChanges = affectedFiles
                .Where(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // 模板变化（T4.2）：layouts 下的文件从"资源"中分流，走受影响页重渲染而非全量重建
            var layoutsRoot = Path.GetFullPath(Path.Combine(
                options.SourcePath,
                string.IsNullOrEmpty(config.LayoutDir) ? "layouts" : config.LayoutDir)) + Path.DirectorySeparatorChar;
            var templateChanges = affectedFiles
                .Where(f => f.EndsWith(".html", StringComparison.OrdinalIgnoreCase) &&
                            f.StartsWith(layoutsRoot, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var assetChanges = affectedFiles
                .Where(f => !f.EndsWith(".md", StringComparison.OrdinalIgnoreCase) &&
                            !templateChanges.Contains(f, StringComparer.OrdinalIgnoreCase))
                .ToList();

            var pagesBuilt = 0;
            var assetsProcessed = 0;

            // 处理内容/模板变化：树上精确替换/删除/插入，全量 siteContext 从树产出——
            // 不再重新扫描解析全部文件（此前为重建 siteContext 每次增量都付全量解析成本）
            if (contentChanges.Count > 0 || templateChanges.Count > 0)
            {
                var tree = _currentPageTree;
                string taxonomySignatureBefore;
                if (tree is null)
                {
                    // 兜底：无先前全量构建（如直接调用增量 API）——先全量装配。
                    // 装配后的树已含本次变更，"更新前旧签名"无从取起（前后同树恒等、
                    // 比较永假），以空哨兵强制 taxonomy 页重产
                    var allFiles = await ScanContentFilesAsync(options.SourcePath, cancellationToken);
                    var allParsed = await ParseContentsAsync(allFiles, options, errors, cancellationToken);
                    tree = AssemblePageTree(FilterContents(allParsed, options), options.SourcePath, config);
                    _currentPageTree = tree;
                    taxonomySignatureBefore = string.Empty;
                }
                else
                {
                    taxonomySignatureBefore = ComputeTaxonomySignature(tree);
                }

                // 1. 变化文件逐个应用到树（新增/更新 → 原位替换；删除 → 摘除节点）
                // File.Exists 分桶与读取之间存在 TOCTOU 窗口（编辑器原子替换），
                // 读取时消失的文件按删除处理，不让单个文件放大为整个增量失败
                var deletedChanges = contentChanges.Where(f => !File.Exists(f)).ToList();
                var changedFilesList = new List<ContentFile>();
                foreach (var path in contentChanges.Where(File.Exists))
                {
                    try
                    {
                        changedFilesList.Add(new ContentFile
                        {
                            Path = path,
                            RawContent = File.ReadAllBytes(path),
                            ModifiedTime = File.GetLastWriteTimeUtc(path)
                        });
                    }
                    catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
                    {
                        deletedChanges.Add(path);
                    }
                }

                var parsedChanges = await ParseContentsAsync(
                    changedFilesList, options, errors, cancellationToken);
                var filteredChanges = FilterContents(parsedChanges, options);

                foreach (var parsed in filteredChanges)
                {
                    var key = ContentKeyFor(options.SourcePath, config, parsed.SourcePath);
                    var existing = tree.Get(key);
                    if (existing is not null)
                    {
                        existing.Content = parsed;
                    }
                    else
                    {
                        var (newKey, newBundleType) = PageTrees.PageTree.KeyForContentFile(
                            Path.GetRelativePath(
                                Path.Combine(options.SourcePath,
                                    string.IsNullOrEmpty(config.ContentDir) ? "content" : config.ContentDir),
                                parsed.SourcePath));
                        tree.Insert(new PageTreeNode
                        {
                            Key = key,
                            BundleType = newBundleType,
                            SourcePath = parsed.SourcePath,
                            Content = parsed
                        });
                        EnsureAncestors(tree, key);
                    }
                }

                foreach (var deleted in deletedChanges)
                {
                    // 陈旧输出按被删节点生成时的 permalink 定位（URL 扁平规则与树 key 不同构），
                    // 必须在摘除节点前取 permalink
                    var key = ContentKeyFor(options.SourcePath, config, deleted);
                    var oldNode = tree.Get(key);
                    if (oldNode?.Content is not null)
                    {
                        var oldRelPermalink = GeneratePermalink(oldNode.Content, config);
                        var staleOutput = GetOutputPath(oldRelPermalink, options.OutputPath);
                        if (File.Exists(staleOutput))
                        {
                            File.Delete(staleOutput);
                        }
                    }
                    tree.Delete(key);
                }

                // 2. 全量上下文从树产出（零重解析）
                var allPageContexts = BuildPageContextsFromTree(tree, config);
                var taxonomySignatureAfter = ComputeTaxonomySignature(tree);
                // taxonomy identity：签名一致（纯正文编辑）时 term/taxonomy 页
                // 内容不变，仅模板变化时才需重渲染
                var taxonomyDataChanged = taxonomySignatureBefore != taxonomySignatureAfter;
                var taxonomyService = new TaxonomyService(config.BaseURL);
                var taxonomies = taxonomyService.BuildTaxonomies(allPageContexts);
                // data identity 已在 IsGlobalIdentityPath 归为全量装配——此处 data 变化
                // 必然伴随全量重建，重新加载保证 site.data 反映最新内容
                var siteData = await LoadSiteDataAsync(options, cancellationToken);
                var siteContext = BuildSiteContext(config, allPageContexts, taxonomies, siteData);
// 3. 渲染集合 = 变化页自身 + 全部 section/home 列表页
                //    （列表页聚合"最新内容"，任何内容变化都可能影响；数量 = section 数，远小于页数。
                //     Hugo 以运行时依赖追踪精确到页，此处为无追踪前提下的保守折中）
                var changedPathSet = new HashSet<string>(
                    affectedFiles, StringComparer.OrdinalIgnoreCase);
                var renderSet = new List<PageContext>();
                foreach (var context in allPageContexts)
                {
                    if (context.Type is "section" or "home" ||
                        (context.SourcePath is not null &&
                         changedPathSet.Contains(context.SourcePath)))
                    {
                        renderSet.Add(context);
                    }
                }

                // fast render mode（T4.4）：模板变化时受影响内容页可能很多（如改 single 模板影响全部页），
                // 只重渲染浏览器正在访问的 URL 对应页面；列表页数量小不裁剪。
                // 访问集为空（浏览器不在站点内）时不过滤，保持保守完整增量。
                // 豁免集合必须是真实内容变化文件（changedFiles 原始参数过滤 .md）——
                // contentChanges/changedPathSet 都派生自 affectedFiles（依赖图传播后，
                // 改模板时全部依赖页都被传播进来），用它豁免=废除 fast render
                if (templateChanges.Count > 0 &&
                    options.PreferredUrls is { } preferred && preferred.Count > 0)
                {
                    var contentChangedSet = new HashSet<string>(
                        changedFiles.Where(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase)),
                        StringComparer.OrdinalIgnoreCase);
                    var fastRenderSet = renderSet
                        .Where(c => c.Type is "section" or "home" ||
                                    MatchesPreferredUrl(c, preferred) ||
                                    // 内容变化页始终渲染：其 URL 浏览器可能从未访问过，
                                    // 不豁免会导致 HTML 旧而 taxonomy/sitemap 新的不一致
                                    (c.SourcePath is not null && contentChangedSet.Contains(c.SourcePath)))
                        .ToList();
                    if (fastRenderSet.Count > 0)
                    {
                        renderSet = fastRenderSet;
                    }
                }

                // identity 分离（layout identity vs content identity）：
                // 只有真实内容文件（.md）变化才影响页面集与聚合产物——
                // sitemap/feeds 的 URL 集与 lastmod 均来自内容，仅模板变化时
                // 重产是纯浪费。判定必须基于 changedFiles 原始参数而非
                // affectedFiles（后者经依赖图传播，改模板时全部依赖页都被卷入）
                var hasContentChanges = changedFiles.Any(
                    f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase));

                var renderedPages = await RenderPagesAsync(
                    renderSet, siteContext, options, errors, cancellationToken);

                // 分类/词条页聚合全部内容的 tags/categories——tags/categories 增删改
                // 使其陈旧必须重产；纯正文编辑（taxonomy 签名一致）跳过重产。
                // 模板变化时也重渲染（term/taxonomy 页外观由模板决定）
                var taxonomyPagesIncremental = templateChanges.Count > 0 || taxonomyDataChanged
                    ? await GenerateTaxonomyPagesAsync(
                        taxonomies, siteContext, config, options, errors, cancellationToken)
                    : [];

                await WriteOutputAsync(renderedPages, taxonomyPagesIncremental, [], options, cancellationToken);

                // sitemap/rss/atom 是全局聚合产物，内容变化后必须重生成
                // （此前增量路径完全跳过，删除内容后 sitemap 持久保留死链）；
                // 仅模板变化时页面集与 lastmod 均未变，重产是纯浪费——跳过
                if (hasContentChanges)
                {
                    await GenerateSitemapAndFeedsAsync(
                        allPageContexts, config, options, cancellationToken);
                }

                pagesBuilt = renderedPages.Count + taxonomyPagesIncremental.Count;
            }

            // 处理资源变化
            if (assetChanges.Count > 0)
            {
                foreach (var assetPath in assetChanges.Where(File.Exists))
                {
                    var asset = new AssetFile
                    {
                        SourcePath = assetPath,
                        MediaType = GetMediaType(assetPath),
                        Content = await File.ReadAllBytesAsync(assetPath, cancellationToken),
                        ModifiedTime = File.GetLastWriteTimeUtc(assetPath)
                    };

                    var processed = await _assetPipeline.ProcessAsync(asset, cancellationToken);

                    // 写入处理后的资源
                    var outputPath = Path.Combine(options.OutputPath, processed.OutputPath.TrimStart('/'));
                    var dir = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    // 与全量路径一致的写入重试：dev server 下文件可能被占用，
                    // 单资产失败不应放大为整个增量失败
                    await WriteBytesWithRetryAsync(outputPath, processed.Content.ToArray(), cancellationToken);

                    assetsProcessed++;
                }
            }

            stopwatch.Stop();

            return new BuildResult
            {
                Success = errors.IsEmpty,
                PagesBuilt = pagesBuilt,
                AssetsProcessed = assetsProcessed,
                Duration = stopwatch.Elapsed,
                MemoryUsed = GC.GetTotalMemory(false),
                Errors = [.. errors],
                Warnings = [.. warnings],
                OutputPath = options.OutputPath
            };
        }
        catch (OperationCanceledException)
        {
            // 取消不是构建失败：重抛交还调用方（同 BUILD001 分支）
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            errors.Add(new BuildError
            {
                FilePath = options.SourcePath,
                Line = 0,
                Column = 0,
                Message = ex.Message,
                ErrorCode = "BUILD002"
            });

            return new BuildResult
            {
                Success = false,
                PagesBuilt = 0,
                AssetsProcessed = 0,
                Duration = stopwatch.Elapsed,
                MemoryUsed = GC.GetTotalMemory(false),
                Errors = [.. errors],
                Warnings = [.. warnings],
                OutputPath = options.OutputPath
            };
        }
    }

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

    #region 私有方法

    private async Task<List<ContentFile>> ScanContentFilesAsync(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        var contentPath = Path.Combine(sourcePath, "content");
        if (!Directory.Exists(contentPath))
        {
            return [];
        }

        // 优化：先收集所有文件路径，然后并行读取
        var mdFiles = Directory.EnumerateFiles(contentPath, "*.md", SearchOption.AllDirectories)
            .ToArray();

        if (mdFiles.Length == 0)
        {
            return [];
        }

        // 预分配数组，避免 List 扩容
        var files = new ContentFile[mdFiles.Length];

        // 并行读取文件内容
        await Parallel.ForEachAsync(
            Enumerable.Range(0, mdFiles.Length),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = cancellationToken
            },
            async (i, ct) =>
            {
                var filePath = mdFiles[i];
                var content = await File.ReadAllBytesAsync(filePath, ct);
                files[i] = new ContentFile
                {
                    Path = filePath,
                    RawContent = content,
                    ModifiedTime = File.GetLastWriteTimeUtc(filePath)
                };
            });

        return [.. files];
    }

    private async Task<List<ParsedContent>> ParseContentsAsync(
        List<ContentFile> contentFiles,
        BuildOptions options,
        ConcurrentBag<BuildError> errors,
        CancellationToken cancellationToken)
    {
        // 优化：预分配结果集合容量
        var results = new ConcurrentBag<ParsedContent>();

        // P3 优化：只在启用缓存时使用内容哈希缓存
        var filesToParse = new List<ContentFile>();
        if (_contentCache != null)
        {
            foreach (var file in contentFiles)
            {
                if (_contentCache.TryGetCached(file, out var cached) && cached != null)
                {
                    results.Add(cached);
                }
                else
                {
                    filesToParse.Add(file);
                }
            }
        }
        else
        {
            filesToParse = contentFiles;
        }

        // 如果所有文件都命中缓存，直接返回
        if (filesToParse.Count == 0)
        {
            return [.. results];
        }

        // 优化：对于小批量直接并行处理，避免 Channel 开销
        if (filesToParse.Count <= options.Parallelism * 4)
        {
            await Parallel.ForEachAsync(
                filesToParse,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = options.Parallelism,
                    CancellationToken = cancellationToken
                },
                async (file, ct) =>
                {
                    try
                    {
                        var parsed = await _contentParser.ParseAsync(file, ct);
                        results.Add(parsed);
                        // 更新缓存（仅在启用时）
                        _contentCache?.Set(file, parsed);
                    }
                    // 取消必须逃逸：否则 ForEachAsync 感知不到取消，
                    // 会把剩余文件逐个记成假解析错误
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        errors.Add(new BuildError
                        {
                            FilePath = file.Path,
                            Line = 0,
                            Column = 0,
                            Message = ex.Message,
                            ErrorCode = "PARSE001"
                        });
                    }
                });

            return [.. results];
        }

        // 大批量使用 Channel 进行流式处理
        var channel = Channel.CreateBounded<ContentFile>(
            new BoundedChannelOptions(options.Parallelism * 2)
            {
                FullMode = BoundedChannelFullMode.Wait
            });

        var channelResults = new ConcurrentBag<ParsedContent>();

        // 生产者：将文件放入 Channel（只放入需要解析的文件）
        var producer = Task.Run(async () =>
        {
            foreach (var file in filesToParse)
            {
                await channel.Writer.WriteAsync(file, cancellationToken);
            }
            channel.Writer.Complete();
        }, cancellationToken);

        // 消费者：并行解析文件
        var consumers = Enumerable.Range(0, options.Parallelism)
            .Select(_ => Task.Run(async () =>
            {
                await foreach (var file in channel.Reader.ReadAllAsync(cancellationToken))
                {
                    try
                    {
                        var parsed = await _contentParser.ParseAsync(file, cancellationToken);
                        channelResults.Add(parsed);
                        // 更新缓存（仅在启用时）
                        _contentCache?.Set(file, parsed);
                    }
                    // 取消必须逃逸（同小批量分支）
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        errors.Add(new BuildError
                        {
                            FilePath = file.Path,
                            Line = 0,
                            Column = 0,
                            Message = ex.Message,
                            ErrorCode = "PARSE001"
                        });
                    }
                }
            }, cancellationToken))
            .ToArray();

        await producer;
        await Task.WhenAll(consumers);

        // 合并缓存命中的结果和新解析的结果
        foreach (var item in channelResults)
        {
            results.Add(item);
        }

        return [.. results];
    }

    private List<ParsedContent> FilterContents(
        List<ParsedContent> contents,
        BuildOptions options)
    {
        var now = DateTimeOffset.Now;

        var filtered = contents.Where(c =>
        {
            // 过滤草稿
            if (c.Metadata.Draft && !options.IncludeDrafts)
            {
                return false;
            }

            // 过滤未来内容
            if (!options.IncludeFuture)
            {
                var publishDate = c.Metadata.PublishDate ?? c.Metadata.Date;
                if (publishDate.HasValue && publishDate.Value > now)
                {
                    return false;
                }
            }

            // 过滤过期内容（考虑 IncludeExpired 选项）
            if (!options.IncludeExpired && c.Metadata.ExpiryDate.HasValue && c.Metadata.ExpiryDate.Value < now)
            {
                return false;
            }

            return true;
        }).ToList();

        return filtered;
    }

    private List<PageContext> BuildPageContexts(
        List<ParsedContent> contents,
        SiteConfig config,
        string sourcePath)
    {
        // 装配页面树（bundle 语义 + 缺失 section/home 合成补齐），再从树遍历产出页面上下文。
        // 树保留到构建周期：增量构建据此做 section 级精确替换，无需重新解析全部文件
        var tree = AssemblePageTree(contents, sourcePath, config);
        _currentPageTree = tree;

        return BuildPageContextsFromTree(tree, config);
    }

    /// <summary>从（最新状态的）页面树遍历产出全部页面上下文：cascade 合并 + 日期降序契约</summary>
    private List<PageContext> BuildPageContextsFromTree(PageTree tree, SiteConfig config)
    {
        // cascade 索引：携带 cascade 的 branch/synthesized 节点 → 完整级联配置
        //（Data 字段 + _target 的 path/kind 过滤）
        var cascades = CollectCascades(tree);
        var pages = new List<PageContext>(tree.Count);
        tree.Walk(new PageTreeWalker
        {
            Tree = tree,
            Handle = (_, node) =>
            {
                var nodeKind = KindOfNode(node);
                var (cascaded, dataChain) = MergeAncestorCascades(node.Key, nodeKind, cascades);
                pages.Add(NodeToPageContext(node, config, cascaded, dataChain));
                return false;
            }
        });

        // 保持既有输出契约：日期降序
        return pages
            .OrderByDescending(p => p.Date)
            .ToList();
    }

    /// <summary>节点 → Hugo kind 语义字符串（cascade _target 的 kind 过滤依据）</summary>
    private static string KindOfNode(PageTreeNode node)
    {
        if (node.Key.Length == 0)
        {
            return "home";
        }
        return node.IsBranch ? "section" : "page";
    }

    /// <summary>
    /// cascade _target 的 path 匹配（简化 Hugo glob 语义，边界已声明）：
    /// null/空 或 "**" = 匹配全部；"/**" 结尾 = 路径前缀；含 "*" 的单段通配 =
    /// 段数一致且非通配段前缀匹配；否则 = 精确或子路径（/posts 匹配 /posts/x）
    /// </summary>
    internal static bool CascadeTargetMatchesPath(string? target, string nodeKey)
    {
        if (string.IsNullOrWhiteSpace(target) || target == "**")
        {
            return true;
        }

        var normalized = target.Trim().TrimEnd('/').ToLowerInvariant();
        if (normalized.Length == 0 || normalized == "**")
        {
            return true;
        }

        if (normalized.EndsWith("/**", StringComparison.Ordinal))
        {
            var prefix = normalized[..^3];
            return nodeKey.StartsWith(prefix, StringComparison.Ordinal);
        }

        if (normalized.Contains('*', StringComparison.Ordinal))
        {
            // 段级通配：段数一致且逐段匹配（* = 任意单段内容）
            var targetSegs = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var nodeSegs = nodeKey.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (targetSegs.Length != nodeSegs.Length)
            {
                return false;
            }
            for (var i = 0; i < targetSegs.Length; i++)
            {
                if (targetSegs[i] != "*" &&
                    !nodeSegs[i].Equals(targetSegs[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        return nodeKey == normalized || nodeKey.StartsWith(normalized + "/", StringComparison.Ordinal);
    }

    /// <summary>
    /// 收集树中携带 cascade 的节点（key → 完整级联配置，含 Data 字段与 _target）
    /// </summary>
    private static Dictionary<string, CascadeConfig> CollectCascades(PageTree tree)
    {
        var result = new Dictionary<string, CascadeConfig>(StringComparer.Ordinal);
        tree.Walk(new PageTreeWalker
        {
            Tree = tree,
            Handle = (_, node) =>
            {
                var cascade = node.Content?.Metadata.Cascade;
                if (cascade is not null && (cascade.Data is not null || !string.IsNullOrEmpty(cascade.Target) || !string.IsNullOrEmpty(cascade.Kind)))
                {
                    result[node.Key] = cascade;
                }
                return false;
            }
        });
        return result;
    }

    /// <summary>
    /// 合并 key 全部祖先的 cascade：home → 逐级到直接父级（近者覆盖远者）。
    /// 每条 cascade 先过 _target 的 path/kind 过滤再合入。
    /// 返回：①合并后的 Params（页面显式值由调用方最后覆盖）②近→远的 Data 链
    ///（供非 Params 字段的先到先得兜底）
    /// </summary>
    private static (IReadOnlyDictionary<string, object> Params, List<FrontMatter> DataChain)
        MergeAncestorCascades(
            string key,
            string nodeKind,
            Dictionary<string, CascadeConfig> cascades)
    {
        var mergedParams = new Dictionary<string, object>(StringComparer.Ordinal);
        var dataChain = new List<FrontMatter>();
        var segments = key.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = "";
        foreach (var segment in segments) // 逐级祖先："" → /a → /a/b（不含 key 自身）
        {
            // 祖先自身 key 对应的 cascade 不作用于自己（Hugo：cascade 面向后代）
            if (cascades.TryGetValue(current, out var layer) &&
                CascadeTargetMatchesPath(layer.Target, key) &&
                KindMatches(layer.Kind, nodeKind))
            {
                if (layer.Data is not null)
                {
                    foreach (var (k, v) in layer.Data.Params)
                    {
                        mergedParams[k] = v; // 近层覆盖远层
                    }
                    dataChain.Add(layer.Data); // 近→远，供字段先到先得
                }
            }
            current = current.Length == 0 ? "/" + segment : current + "/" + segment;
        }

        return (mergedParams, dataChain);
    }

    private static bool KindMatches(string? cascadeKind, string nodeKind)
    {
        return string.IsNullOrWhiteSpace(cascadeKind) ||
               cascadeKind.Equals(nodeKind, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// taxonomy 输入签名：全部内容页的（树 key + 标题 + 日期 + tags + categories + draft）
    /// 规范化哈希。签名一致 = term/taxonomy 页的数据输入未变（纯正文编辑），可跳过重产
    /// </summary>
    private static string ComputeTaxonomySignature(PageTree tree)
    {
        var entries = new List<string>();
        tree.Walk(new PageTreeWalker
        {
            Tree = tree,
            Handle = (_, node) =>
            {
                var md = node.Content?.Metadata;
                if (md is not null)
                {
                    entries.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                        $"{node.Key}|{md.Title}|{md.Slug}|{md.Date:O}|{md.Draft}|{{{string.Join(",", md.Tags.OrderBy(t => t, StringComparer.Ordinal))}}}|{{{string.Join(",", md.Categories.OrderBy(c => c, StringComparer.Ordinal))}}}"));
                }
                return false;
            }
        });
        entries.Sort(StringComparer.Ordinal);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(string.Join("\n", entries))));
    }

    /// <summary>
    /// 全局 identity 判定：config 文件（flint./config./hugo. 前缀）与
    /// data/、archetypes/ 目录的变化影响面广（配置重载/全站数据/原型模板），
    /// 增量路径收到这类路径时丢弃缓存树走全量装配
    /// </summary>
    internal static bool IsGlobalIdentityPath(string path, string sourcePath)
    {
        var normalized = Path.GetFullPath(path);
        var root = Path.GetFullPath(sourcePath);
        if (!normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relative = normalized[root.Length..].TrimStart(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var segments = relative.Split(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        var fileName = segments[^1];
        if (segments.Length == 1 &&
            (fileName.StartsWith("flint.", StringComparison.OrdinalIgnoreCase)
             || fileName.StartsWith("config.", StringComparison.OrdinalIgnoreCase)
             || fileName.StartsWith("hugo.", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // 与上方 config 前缀判定同口径：目录大小写不同的站点（Data/Archetypes）
        // 变更同样归入全局 identity
        return string.Equals(segments[0], "data", StringComparison.OrdinalIgnoreCase)
            || string.Equals(segments[0], "archetypes", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>内容文件绝对路径 → 树 key（与装配时的推导规则一致）</summary>
    private static string ContentKeyFor(string sourcePath, SiteConfig config, string absoluteFilePath)    {
        var contentDir = Path.Combine(sourcePath,
            string.IsNullOrEmpty(config.ContentDir) ? "content" : config.ContentDir);
        var relative = Path.GetRelativePath(contentDir, absoluteFilePath);
        return PageTrees.PageTree.KeyForContentFile(relative).Key;
    }

    /// <summary>
    /// 将解析结果装配为页面树：key 由内容文件相对路径推导（bundle 语义），
    /// 缺失的 section/home 以合成节点补齐（无文件来源，模板可见）
    /// </summary>
    private static PageTree AssemblePageTree(
        List<ParsedContent> contents,
        string sourcePath,
        SiteConfig config)
    {
        var tree = new PageTree();
        var contentDir = Path.Combine(sourcePath,
            string.IsNullOrEmpty(config.ContentDir) ? "content" : config.ContentDir);

        foreach (var content in contents)
        {
            var relative = Path.GetRelativePath(contentDir, content.SourcePath);
            var (key, bundleType) = PageTrees.PageTree.KeyForContentFile(relative);
            tree.Insert(new PageTreeNode
            {
                Key = key,
                BundleType = bundleType,
                SourcePath = content.SourcePath,
                Content = content
            });
        }

        // 补缺：确保每个节点的祖先链与 home 存在（合成 section/home）
        var keys = new List<string>(tree.Count);
        tree.Walk(new PageTreeWalker
        {
            Tree = tree,
            Handle = (key, _) =>
            {
                keys.Add(key);
                return false;
            }
        });

        foreach (var key in keys)
        {
            EnsureAncestors(tree, key);
        }

        if (!tree.Has(""))
        {
            tree.Insert(new PageTreeNode
            {
                Key = "",
                BundleType = PageBundleType.Synthesized,
                SourcePath = "",
                Title = config.Title
            });
        }

        
        return tree;
    }

    /// <summary>确保 key 的全部祖先（不含自身）在树中存在，缺失则插入合成 section</summary>
    private static void EnsureAncestors(PageTree tree, string key)
    {
        var segments = key.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = "";
        for (var i = 0; i < segments.Length - 1; i++)
        {
            current = current.Length == 0 ? "/" + segments[i] : current + "/" + segments[i];
            if (tree.Has(current))
            {
                continue;
            }

            tree.Insert(new PageTreeNode
            {
                Key = current,
                BundleType = PageBundleType.Synthesized,
                SourcePath = "",
                Title = segments[i]
            });
        }
    }

    /// <summary>树节点 → 页面上下文：真实节点走 CreatePageContext，合成节点手建（Content 为空）</summary>
    private PageContext NodeToPageContext(
        PageTreeNode node,
        SiteConfig config,
        IReadOnlyDictionary<string, object> cascadedParams,
        List<FrontMatter>? cascadeDataChain = null)
    {
        if (node.Content is not null)
        {
            return CreatePageContext(node.Content, config, cascadedParams, cascadeDataChain);
        }

        var kind = node.Key.Length == 0 ? "home" : "section";
        var relPermalink = node.Key.Length == 0 ? "/" : node.Key + "/";

        // 合成节点的 cascade 字段兜底（近→远先到先得；合成节点无自身 front matter）
        string? title = node.Title ?? node.Key;
        bool draft = false;
        if (cascadeDataChain is { Count: > 0 })
        {
            foreach (var data in cascadeDataChain)
            {
                if (title == node.Title && !string.IsNullOrEmpty(data.Title) && data.Title != "Untitled")
                {
                    title = data.Title;
                }
                if (!draft && data.Draft)
                {
                    draft = true;
                }
            }
        }

        return new PageContext
        {
            Title = title,
            Content = "",
            Permalink = config.BaseURL.TrimEnd('/') + relPermalink,
            RelPermalink = relPermalink,
            Date = DateTimeOffset.Now,
            Tags = [],
            Categories = [],
            WordCount = 0,
            ReadingTime = TimeSpan.Zero,
            Type = kind,
            Draft = draft,
            Params = cascadedParams
        };
    }

    private PageContext CreatePageContext(
        ParsedContent content,
        SiteConfig config,
        IReadOnlyDictionary<string, object>? cascadedParams = null,
        List<FrontMatter>? cascadeDataChain = null)
    {
        var permalink = GeneratePermalink(content, config);
        var relPermalink = permalink.StartsWith(config.BaseURL)
            ? permalink[config.BaseURL.Length..]
            : permalink;

        // cascade 合并：祖先级联值先入，页面自身显式 Params 覆盖（对齐 Hugo 优先级）
        var ownParams = content.Metadata.Params;
        var effectiveParams = cascadedParams is { Count: > 0 }
            ? MergeParams(cascadedParams, ownParams)
            : ownParams;

        // cascade 字段兜底（近→远先到先得）：页面 front matter 为默认值时
        // 才取 cascade 值——页面显式值恒优先（对齐 Hugo 语义）
        var title = content.Metadata.Title;
        var description = content.Metadata.Description;
        var layout = content.Metadata.Layout;
        var weight = content.Metadata.Weight;
        var draft = content.Metadata.Draft;
        if (cascadeDataChain is { Count: > 0 })
        {
            foreach (var data in cascadeDataChain)
            {
                if (title == "Untitled" && !string.IsNullOrEmpty(data.Title))
                    title = data.Title;
                if (description is null && !string.IsNullOrEmpty(data.Description))
                    description = data.Description;
                if (layout is null && !string.IsNullOrEmpty(data.Layout))
                    layout = data.Layout;
                if (weight == 0 && data.Weight != 0)
                    weight = data.Weight;
                if (!draft && data.Draft)
                    draft = true;
            }
        }

        return new PageContext
        {
            Title = title,
            Content = content.HtmlContent,
            Permalink = config.BaseURL.TrimEnd('/') + relPermalink,
            RelPermalink = relPermalink,
            Date = content.Metadata.Date ?? DateTimeOffset.Now,
            LastMod = content.Metadata.LastMod,
            Tags = content.Metadata.Tags,
            Categories = content.Metadata.Categories,
            WordCount = content.WordCount,
            ReadingTime = content.ReadingTime,
            Description = description,
            Summary = content.Summary,
            Type = content.Metadata.Type ?? "page",
            Layout = layout,
            Outputs = content.Metadata.Outputs,
            SourcePath = content.SourcePath,
            Draft = draft,
            Weight = weight,
            Params = effectiveParams,
            Plain = content.PlainText,
            RawContent = content.RawMarkdown
        };
    }

    /// <summary>合并参数字典：base 先入，override 覆盖同名键</summary>
    private static IReadOnlyDictionary<string, object> MergeParams(
        IReadOnlyDictionary<string, object> baseParams,
        IReadOnlyDictionary<string, object> overrideParams)
    {
        var merged = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (k, v) in baseParams)
        {
            merged[k] = v;
        }
        foreach (var (k, v) in overrideParams)
        {
            merged[k] = v;
        }
        return merged;
    }

    private string GeneratePermalink(ParsedContent content, SiteConfig config)
    {
        // 如果有自定义 slug，使用它
        if (!string.IsNullOrEmpty(content.Metadata.Slug))
        {
            return "/" + content.Metadata.Slug.Trim('/') + "/";
        }

        // 根据内容类型选择 permalink 模式
        var pattern = content.Metadata.Type?.ToLowerInvariant() switch
        {
            "post" => config.Permalinks?.Posts ?? "/:year/:month/:title/",
            _ => config.Permalinks?.Pages ?? "/:title/"
        };

        var date = content.Metadata.Date ?? DateTimeOffset.Now;

        // 优先使用文件名作为 slug（不含扩展名），这样更符合 Hugo 的行为
        // 如果文件名是 index.md，则使用父目录名
        var fileName = Path.GetFileNameWithoutExtension(content.SourcePath);
        string slug;
        if (fileName.Equals("index", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("_index", StringComparison.OrdinalIgnoreCase))
        {
            // 使用父目录名
            var parentDir = Path.GetDirectoryName(content.SourcePath);
            slug = !string.IsNullOrEmpty(parentDir)
                ? GenerateSlug(Path.GetFileName(parentDir))
                : GenerateSlug(content.Metadata.Title);
        }
        else
        {
            slug = GenerateSlug(fileName);
        }

        var permalink = ExpandPermalinkTokens(
            pattern, date, slug, content.Metadata.Slug ?? "",
            content.SourcePath, config.ContentDir);

        return permalink;
    }

    /// <summary>
    /// 展开 permalink 模式 token（对齐 Hugo 常用 token 集）。
    /// sections 需要源路径与内容目录推算目录链；title/slug 语义保持历史行为
    /// （slug 化文件名；入口已优先显式 slug）
    /// </summary>
    internal static string ExpandPermalinkTokens(
        string pattern,
        DateTimeOffset date,
        string slug,
        string frontMatterSlug,
        string sourcePath,
        string contentDir)
    {
        // :section/:sections 从源路径目录链推算：取路径中最后一个 "content"
        // 目录段之后的部分（不依赖调用方传内容根）
        var sections = Array.Empty<string>();
        if (!string.IsNullOrEmpty(sourcePath))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(sourcePath));
            if (!string.IsNullOrEmpty(dir))
            {
                var parts = dir.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                var contentIndex = Array.FindLastIndex(
                    parts, p => p.Equals("content", StringComparison.OrdinalIgnoreCase));
                if (contentIndex >= 0 && contentIndex + 1 < parts.Length)
                {
                    sections = parts[(contentIndex + 1)..];
                }
            }
        }

        var sb = new System.Text.StringBuilder(pattern.Length + 32);
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] != ':')
            {
                sb.Append(pattern[i]);
                continue;
            }

            // 读完整 token（字母连续段），识别后追加展开值
            var j = i + 1;
            while (j < pattern.Length && (char.IsLetter(pattern[j]) || pattern[j] == '_'))
            {
                j++;
            }
            var token = pattern[(i + 1)..j].ToLowerInvariant();

            switch (token)
            {
                case "year": sb.Append(date.Year.ToString()); break;
                case "month": sb.Append(date.Month.ToString("D2")); break;
                case "day": sb.Append(date.Day.ToString("D2")); break;
                case "monthname": sb.Append(date.ToString("MMMM", System.Globalization.CultureInfo.InvariantCulture)); break;
                case "dayname": sb.Append(date.ToString("dddd", System.Globalization.CultureInfo.InvariantCulture)); break;
                case "yearday": sb.Append(date.DayOfYear.ToString()); break;
                case "weekdayname": sb.Append(date.ToString("dddd", System.Globalization.CultureInfo.InvariantCulture)); break;
                case "title" or "slug": sb.Append(slug); break;
                case "slugorfilename": sb.Append(string.IsNullOrEmpty(frontMatterSlug) ? slug : frontMatterSlug); break;
                case "filename": sb.Append(slug); break;
                case "contentbasename":
                    var baseName = Path.GetFileNameWithoutExtension(sourcePath ?? "");
                    sb.Append(baseName.Equals("_index", StringComparison.OrdinalIgnoreCase)
                        ? Path.GetFileName(Path.GetDirectoryName(sourcePath) ?? "") ?? ""
                        : baseName);
                    break;
                case "section":
                    sb.Append(sections.Length > 0 ? sections[^1] : "");
                    break;
                case "sections":
                    sb.Append(sections.Length > 0 ? "/" + string.Join("/", sections) : "");
                    break;
                default:
                    // 未知 token 原样保留（含 ':yearXX' 这类非 token 前缀场景）
                    sb.Append(pattern[i..j]);
                    break;
            }
            i = j - 1;
        }

        return sb.ToString();
    }

    /// <summary>
    /// 从标题生成 URL slug
    /// 对于非 ASCII 字符（如中文），保持原样
    /// </summary>
    private static string GenerateSlug(string title)
    {
        // 将空格和下划线替换为连字符
        var slug = title.Replace(" ", "-").Replace("_", "-");

        // 对于纯 ASCII 字符串，转换为小写
        // 对于包含非 ASCII 字符的字符串（如中文），保持原样
        if (slug.All(c => c < 128))
        {
            slug = slug.ToLowerInvariant();
        }

        return slug;
    }

    /// <summary>
    /// 注册站点级短代码：扫描 layouts/shortcodes/*.html，每个文件注册为 TemplateShortcode
    /// （模板语法错误让构建失败——fail-fast 与未知短代码语义一致）
    /// </summary>
    private void RegisterSiteShortcodes(string sourcePath, SiteConfig config)
    {
        var shortcodesDir = Path.Combine(
            sourcePath,
            string.IsNullOrEmpty(config.LayoutDir) ? "layouts" : config.LayoutDir,
            "shortcodes");

        if (!Directory.Exists(shortcodesDir))
        {
            return;
        }

        var registry = _contentParser.ShortcodeProcessor.Registry;
        foreach (var file in Directory.EnumerateFiles(shortcodesDir, "*.html"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var content = File.ReadAllText(file);
            try
            {
                registry.Register(new TemplateShortcode(name, content));
            }
            catch (FormatException ex)
            {
                throw new FormatException($"加载站点短代码失败: {file}", ex);
            }
        }
    }

    /// <summary>
    /// 注册 页面→模板 依赖关系到依赖图（模板名含 partial 闭包，规范化为物理路径）
    /// </summary>
    /// <summary>
    /// 页面 permalink 是否命中最近访问 URL（去尾斜杠差异后精确比对；"/" 首页由 home 类型天然保留）
    /// </summary>
    private static bool MatchesPreferredUrl(PageContext page, IReadOnlySet<string> preferred)
    {
        var rel = page.RelPermalink.TrimEnd('/');
        if (rel.Length == 0)
        {
            return false; // "/" 首页：home 类型已保留在渲染集合
        }

        foreach (var url in preferred)
        {
            var normalized = url.TrimEnd('/');
            if (normalized.Length > 0 &&
                (rel.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
                 page.Permalink.TrimEnd('/').Equals(normalized, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 渲染期依赖注册（T4.1）：用本次渲染实际 include 的模板物理路径
    /// 覆盖静态闭包注册（条件 include 只记实际走的分支，精确化反查）。
    /// data:site.* 键不是文件路径，不进依赖图（页面集合失效由树重算覆盖），
    /// 保留在 RenderedDependencies 供后续值缓存分层消费
    /// </summary>
    private void RegisterRenderedDependencies(TemplateContext context)
    {
        if (context.RenderedDependencies is not { } deps || string.IsNullOrEmpty(context.Page.SourcePath))
        {
            return;
        }

        var physicalPaths = deps
            .Where(File.Exists)
            .Select(Path.GetFullPath)
            .ToList();

        if (physicalPaths.Count > 0)
        {
            _dependencyGraph.RegisterDependency(context.Page.SourcePath, physicalPaths);
        }
    }

    private void RegisterTemplateDependencies(
        List<PageContext> pages,
        SiteConfig config,
        string sourcePath)
    {        var layoutsDir = Path.Combine(sourcePath, string.IsNullOrEmpty(config.LayoutDir) ? "layouts" : config.LayoutDir);

        foreach (var page in pages)
        {
            if (string.IsNullOrEmpty(page.SourcePath))
            {
                continue;
            }

            var templateName = page.Layout ?? "single";
            var templateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                templateName
            };

            // 收集模板的 include/partial 闭包（递归）
            foreach (var dep in _templateRenderer.GetDependencies(templateName))
            {
                templateNames.Add(dep);
            }

            // 逻辑模板名 → layouts 目录下的物理路径（与 FileTemplateLoader 的搜索规则一致）
            var physicalPaths = templateNames
                .Select(name => Path.GetFullPath(Path.Combine(
                    layoutsDir,
                    name.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ? name : name + ".html")))
                .ToList();

            _dependencyGraph.RegisterDependency(page.SourcePath, physicalPaths);
        }
    }

    /// <summary>
    /// 加载 data/ 目录数据（site.data 模板变量的来源）；
    /// 目录不存在时返回空字典
    /// </summary>
    private async Task<IReadOnlyDictionary<string, object>> LoadSiteDataAsync(
        BuildOptions options,
        CancellationToken cancellationToken)
    {
        var dataDir = Path.Combine(options.SourcePath,
            "data");
        return await _dataLoader.LoadAsync(dataDir, cancellationToken);
    }

    private SiteContext BuildSiteContext(
        SiteConfig config,
        List<PageContext> pages,
        TaxonomyCollection taxonomies,
        IReadOnlyDictionary<string, object>? siteData = null)
    {
        // 当页面列表为空时，使用当前时间作为 LastChange
        var lastChange = pages.Count > 0
            ? pages.Max(p => p.LastMod ?? p.Date)
            : DateTimeOffset.Now;

        return new SiteContext
        {
            Title = config.Title,
            BaseURL = config.BaseURL,
            Language = config.LanguageCode,
            Pages = pages,
            RegularPages = pages.Where(p => p.Type != "section").ToList(),
            Taxonomies = taxonomies,
            Menus = new MenuCollection { Menus = new Dictionary<string, IReadOnlyList<MenuItem>>() },
            Config = config,
            Data = siteData ?? new Dictionary<string, object>(),
            BuildDate = DateTimeOffset.Now,
            LastChange = lastChange,
            IsMultiLingual = false,
            Languages = [config.LanguageCode]
        };
    }

    private async Task<List<RenderedPage>> RenderPagesAsync(
        List<PageContext> pages,
        SiteContext siteContext,
        BuildOptions options,
        ConcurrentBag<BuildError> errors,
        CancellationToken cancellationToken)
    {
        var results = new ConcurrentBag<RenderedPage>();

        // 优化：批量处理以减少并发调度开销
        // 每批处理多个页面，共享模板查找开销
        const int batchSize = 20;
        var batches = pages.Chunk(batchSize).ToArray();

        await Parallel.ForEachAsync(
            batches,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = options.Parallelism,
                CancellationToken = cancellationToken
            },
            async (batch, ct) =>
            {
                foreach (var page in batch)
                {
                    try
                    {
                        // 模板选择对齐 Hugo kind 语义：home→index、section→list、普通页→single；
                        // front matter layout 声明优先，缺文件回退 single（与旧行为一致，
                        // 内置回退模板继续兜底 term/taxonomy）。此前一律 single：
                        // 首页 layouts/index.html 被 renderSet 的 home 页覆盖、section 永远不走 list
                        var baseTemplateName = page.Type switch
                        {
                            "home" => page.Layout ?? (_templateRenderer.TemplateExists("index") ? "index" : "single"),
                            "section" => page.Layout ?? (_templateRenderer.TemplateExists("list") ? "list" : "single"),
                            _ => page.Layout ?? "single"
                        };

                        // 输出格式：页面级 outputs 覆盖（对齐 Hugo），空则仅 html。
                        // html 恒渲染；非 html 格式（json 等）需存在输出格式变体模板
                        //（{kind/ayout 名}.{format}.html）才产出，无模板跳过
                        var formats = page.Outputs.Count > 0
                            ? page.Outputs
                            : (IReadOnlyList<string>)["html"];

                        foreach (var formatName in formats)
                        {
                            var format = OutputFormats.Resolve(formatName);
                            if (format is null)
                            {
                                continue;
                            }

                            var context = new TemplateContext
                            {
                                Page = page,
                                Site = siteContext,
                                IsSingle = baseTemplateName == "single",
                                // 随 kind 分派同步置位：首页/列表页模板的 is_home/is_list
                                // 判断依赖此标志（taxonomy 路径已设 IsList，此处对齐）
                                IsHome = page.Type == "home",
                                IsList = page.Type == "section"
                            };

                            string html;
                            if (format.Name == "html")
                            {
                                html = await _templateRenderer.RenderAsync(baseTemplateName, context, ct);
                            }
                            else
                            {
                                // 输出格式变体模板：{基模板名}.{格式名}（如 single.json）
                                var variantTemplate = $"{baseTemplateName}.{format.Name}";
                                if (!_templateRenderer.TemplateExists(variantTemplate))
                                {
                                    continue;
                                }
                                html = await _templateRenderer.RenderAsync(variantTemplate, context, ct);
                            }

                            RegisterRenderedDependencies(context);
                            results.Add(new RenderedPage
                            {
                                OutputPath = GetOutputPathForFormat(page.RelPermalink, format, options.OutputPath),
                                Content = html
                            });
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        errors.Add(new BuildError
                        {
                            FilePath = page.RelPermalink,
                            Line = 0,
                            Column = 0,
                            Message = ex.Message,
                            ErrorCode = "RENDER001"
                        });
                    }
                }
            });

        return [.. results];
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

    private async Task<RenderedPage?> GenerateHomePageAsync(
        SiteContext siteContext,
        SiteConfig config,
        BuildOptions options,
        ConcurrentBag<BuildError> errors,
        CancellationToken cancellationToken)
    {
        try
        {
            // 检查是否存在首页模板
            if (!_templateRenderer.TemplateExists("index"))
            {
                return null;
            }

            // 创建首页上下文
            var pageContext = new PageContext
            {
                Title = config.Title,
                Content = "",
                Permalink = config.BaseURL,
                RelPermalink = "/",
                Date = DateTimeOffset.Now,
                Tags = [],
                Categories = [],
                WordCount = 0,
                ReadingTime = TimeSpan.Zero,
                Type = "home"
            };

            var context = new TemplateContext
            {
                Page = pageContext,
                Site = siteContext,
                IsHome = true
            };

            var html = await _templateRenderer.RenderAsync("index", context, cancellationToken);
            RegisterRenderedDependencies(context);
            return new RenderedPage
            {
                OutputPath = Path.Combine(options.OutputPath, "index.html"),
                Content = html
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            errors.Add(new BuildError
            {
                FilePath = "/",
                Line = 0,
                Column = 0,
                Message = $"首页生成失败: {ex.Message}",
                ErrorCode = "HOME001"
            });
            return null;
        }
    }

    private async Task<List<RenderedPage>> GenerateTaxonomyPagesAsync(
        TaxonomyCollection taxonomies,
        SiteContext siteContext,
        SiteConfig config,
        BuildOptions options,
        ConcurrentBag<BuildError> errors,
        CancellationToken cancellationToken)
    {
        var results = new List<RenderedPage>();

        // disableKinds 消费点：taxonomy/term 分类页可被配置禁用（对齐 Hugo kind 语义）。
        // 两者独立判定——此前 && 连接，只禁 taxonomy 或只禁 term 单独配置时不生效
        var disabled = config.DisableKinds;
        var taxonomyDisabled = disabled.Contains("taxonomy", StringComparer.OrdinalIgnoreCase);
        var termDisabled = disabled.Contains("term", StringComparer.OrdinalIgnoreCase);
        if (taxonomyDisabled && termDisabled)
        {
            return results;
        }

        var taxonomyService = new TaxonomyService(config.BaseURL);
        var paginationService = new PaginationService(config.PaginatePath, config.Paginate);
        var generator = new TaxonomyPageGenerator(taxonomyService, paginationService);

        var taxonomyPages = generator.GeneratePages(taxonomies, config.Paginate);

        foreach (var taxPage in taxonomyPages)
        {
            // 列表页（无 TermName）受 "taxonomy" 控制，term 页受 "term" 控制
            if (taxPage.TermName is null ? taxonomyDisabled : termDisabled)
            {
                continue;
            }
            try
            {
                // 从完整 URL 中提取相对路径
                var relPermalink = taxPage.Permalink ?? "/";
                var baseUrl = config.BaseURL.TrimEnd('/');
                if (relPermalink.StartsWith(baseUrl, StringComparison.OrdinalIgnoreCase))
                {
                    relPermalink = relPermalink[baseUrl.Length..];
                }
                if (!relPermalink.StartsWith('/'))
                {
                    relPermalink = "/" + relPermalink;
                }

                // 创建分类页面的上下文
                var pageContext = new PageContext
                {
                    Title = taxPage.TermName ?? taxPage.TaxonomyName,
                    Content = "",
                    Permalink = taxPage.Permalink ?? "/",
                    RelPermalink = relPermalink,
                    Date = DateTimeOffset.Now,
                    Tags = [],
                    Categories = [],
                    WordCount = 0,
                    ReadingTime = TimeSpan.Zero,
                    Type = "taxonomy",
                    // 模板 page.pages（term 页词条页面集合）与 page.terms（taxonomy
                    // 页词条列表）的数据源——默认主题模板依赖此二者，缺省时
                    // 词条/分类页渲染为空列表（端到端冒烟发现的回归）
                    Pages = taxPage.Pages,
                    Terms = taxPage.Terms
                };

                var templateName = taxPage.PageType == TaxonomyPageType.TaxonomyList
                    ? "taxonomy"
                    : "term";

                // 如果 term 模板不存在，回退到 taxonomy 或 list 模板
                if (templateName == "term" && !_templateRenderer.TemplateExists("term"))
                {
                    if (_templateRenderer.TemplateExists("taxonomy"))
                    {
                        templateName = "taxonomy";
                    }
                    else if (_templateRenderer.TemplateExists("list"))
                    {
                        templateName = "list";
                    }
                }
                // 如果 taxonomy 模板不存在，回退到 list 模板
                else if (templateName == "taxonomy" && !_templateRenderer.TemplateExists("taxonomy"))
                {
                    if (_templateRenderer.TemplateExists("list"))
                    {
                        templateName = "list";
                    }
                }

                var context = new TemplateContext
                {
                    Page = pageContext,
                    Site = siteContext,
                    IsList = true,
                    // term 页注入词条专属页面集合（Hugo .Pages 语义）：
                    // 此前 term 模板的 site.regular_pages 会列出全站页面而非该词条页面
                    Pages = taxPage.Pages
                };

                var html = await _templateRenderer.RenderAsync(templateName, context, cancellationToken);
                RegisterRenderedDependencies(context);
                results.Add(new RenderedPage
                {
                    OutputPath = GetOutputPath(relPermalink, options.OutputPath),
                    Content = html
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add(new BuildError
                {
                    FilePath = taxPage.Permalink ?? "unknown",
                    Line = 0,
                    Column = 0,
                    Message = ex.Message,
                    ErrorCode = "TAXONOMY001"
                });
            }
        }

        return results;
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

    #endregion
}

/// <summary>
/// 站点构建器选项
/// </summary>
public sealed class SiteBuilderOptions
{
    /// <summary>
    /// 是否启用缓存
    /// </summary>
    public bool EnableCache { get; init; } = true;

    /// <summary>
    /// 是否启用内容哈希缓存（建议只在 watch 模式下启用）
    /// </summary>
    public bool EnableContentCache { get; init; }

    /// <summary>
    /// 缓存目录
    /// </summary>
    public string? CacheDirectory { get; init; }
}

/// <summary>
/// 渲染后的页面
/// </summary>
public sealed class RenderedPage
{
    /// <summary>
    /// 输出路径
    /// </summary>
    public required string OutputPath { get; init; }

    /// <summary>
    /// HTML 内容
    /// </summary>
    public required string Content { get; init; }
}

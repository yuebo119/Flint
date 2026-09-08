// Flint 静态站点生成器
// 站点构建器实现（编排主体：字段/构造/BuildAsync；方法群按聚合拆分于同目录 partial 文件：
// SiteBuilder.Tree.cs 树装配+cascade+PageContext、SiteBuilder.Incremental.cs 增量、
// SiteBuilder.Render.cs 渲染调度、SiteBuilder.Output.cs 输出写入）

using System.Collections.Concurrent;
using System.Diagnostics;
using Flint.Core.Abstractions;
using Flint.Core.Content;
using Flint.Core.Models;
using PageTree = Flint.Core.Site.PageTrees.PageTree;

namespace Flint.Core.Site;

/// <summary>
/// 站点构建器
/// 使用 Channel 并发管道实现高效构建
/// </summary>
public sealed partial class SiteBuilder : ISiteBuilder
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
    // taxonomy 签名的节点级条目缓存（key = 树节点 key）：增量只重算变化页的
    // 条目，未变化页直接复用——每次增量的全站遍历从"格式化全部条目"降为
    // "拼缓存条目 + 一次排序哈希"（500 页站点省 499 次条目格式化）
    private readonly Dictionary<string, string> _taxonomyEntryCache = new(StringComparer.Ordinal);

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

        // 构建边界：清 mtime 短窗缓存，保证本次构建看到全部模板的最新状态
        // （TTL 缓存只在单次构建内部生效，见渲染器 InvalidateMtimeCache）
        _templateRenderer.InvalidateMtimeCache();

        // P2 优化：预编译模板（只在首次构建时执行）
        if (!_templatesPrecompiled)
        {
            await _templateRenderer.PrecompileTemplatesAsync(cancellationToken);
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

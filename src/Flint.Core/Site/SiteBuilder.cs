// Flint 静态站点生成器
// 站点构建器实现（编排主体：字段/构造/BuildAsync；方法群按聚合拆分于同目录 partial 文件：
// SiteBuilder.Tree.cs 树装配+cascade+PageContext、SiteBuilder.Incremental.cs 增量、
// SiteBuilder.Render.cs 渲染调度、SiteBuilder.Output.cs 输出写入）

using System.Collections.Concurrent;
using System.Diagnostics;
using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.Content;
using Flint.Core.IO;
using Flint.Core.Models;
using Flint.Core.Templates;
using PageTree = Flint.Core.Site.PageTrees.PageTree;

namespace Flint.Core.Site;

/// <summary>
/// 站点构建器
/// 使用 Channel 并发管道实现高效构建
/// </summary>
public sealed partial class SiteBuilder : ISiteBuilder
{
    private readonly IContentParser _contentParser;

    /// <summary>
    /// 分类页（taxonomy/term）的列表上下文：生成各列表页 RSS 用。
    /// Hugo 为每个 taxonomy/term 列表页产出 &lt;列表页&gt;/index.xml——
    /// 这些页在分类渲染阶段创建，不进 pageContexts 主列表，故单独收集
    /// </summary>
    private readonly List<PageContext> TaxonomyFeedContexts = new();
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
    // taxonomy 签名的节点级条目缓存（key = 树节点 key）：条目以 128-bit FNV 哈希
    // 形态缓存，聚合用 XOR（顺序无关）——增量前后的全站遍历从"格式化全部条目
    // + 全量排序 + 大字符串拼接 + SHA256"降为"拼缓存哈希 + XOR"（#3：
    // 原 Sort/Join 每次增量在万页站点产生 MB 级字符串分配）
    private readonly Dictionary<string, (ulong Lo, ulong Hi)> _taxonomyEntryCache = new(StringComparer.Ordinal);

    // taxonomy lookup 投影缓存（#12）：键 = BaseURL + 签名 + taxonomy 声明键集。
    // 签名未变（纯正文编辑）时词条查找投影整体复用，省全词条遍历 + 投影构建
    private readonly Dictionary<string, List<PageContext>> _lookupProjectionCache = new(StringComparer.Ordinal);

    // 渲染即写盘（T4.1）的路径去重表：先完成渲染者赢得写入权（TryAdd），
    // 防止重复输出路径的并发双写；每次构建开头清空。
    // 比较器跟随文件系统大小写语义：Windows/macOS 下 `About/x.html` 与
    // `about/x.html` 是同一文件，Ordinal 会当成两条路径而双写
    internal static readonly StringComparer OutputPathComparer =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private readonly ConcurrentDictionary<string, byte> _writtenPaths = new(OutputPathComparer);

    // 依赖追踪开关（T4.3）：watch 模式需要渲染期精确登记供增量反查；
    // 一次性构建跳过每页 File.Exists stat
    private readonly bool _dependencyTracking;

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
        _dependencyTracking = _options.EnableDependencyTracking;
    }

    /// <inheritdoc />
    public async ValueTask<BuildResult> BuildAsync(
        BuildOptions options,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var errors = new ConcurrentBag<BuildError>();
        var warnings = new ConcurrentBag<BuildWarning>();

        // 构建作用域：渲染期登记（分页标记/全量页面/站点对象/分类查找）按执行流隔离。
        // 必须在同步前缀（首个 await 前）创建，本次构建的全部并行渲染子任务才会
        // 继承同一实例——同进程并行构建（集成测试多站点 in-process）互不串写。
        // **BuildRenderStateAccess**（2026-10-21 架构居中于 Fluid：从
        // ScribanTemplateRenderer 移至引擎中立类，SiteBuilder 不再静态调用
        // Scriban 渲染器）
        BuildRenderStateAccess.BeginBuildScope();

        // 输出目录缓存随构建重置：下方 CleanOutput 会删除输出目录，旧缓存
        // 若命中会跳过重建导致写入阶段报"目录不存在"
        OutputDirectoryEnsurer.Reset();
        _writtenPaths.Clear();
        InitOutputCache(options);

        // 阶段计时诊断（热点画像/优化验证基建）：FLINT_TRACE_PHASES=1 时向 stderr
        // 输出各阶段耗时；默认关闭，仅一个 bool 判断的开销
        var tracePhases = Environment.GetEnvironmentVariable("FLINT_TRACE_PHASES") == "1";
        var lastPhaseTs = 0L;
        void Phase(string name)
        {
            if (!tracePhases) return;
            var ts = stopwatch.ElapsedTicks;
            Console.Error.WriteLine($"[phase] {name,-24} {(ts - lastPhaseTs) * 1000.0 / Stopwatch.Frequency,9:F1}ms");
            lastPhaseTs = ts;
        }

        // 构建边界：清 mtime 短窗缓存，保证本次构建看到全部模板的最新状态
        // （TTL 缓存只在单次构建内部生效，见渲染器 InvalidateMtimeCache）
        _templateRenderer.InvalidateMtimeCache();

        // P2 优化：预编译模板（只在首次构建时执行）
        if (!_templatesPrecompiled)
        {
            await _templateRenderer.PrecompileTemplatesAsync(cancellationToken);
            _templatesPrecompiled = true;
        }
        Phase("0.模板预编译");

        try
        {
            // 0. 如果启用了 CleanOutput，先清理输出目录
            if (options.CleanOutput)
            {
                await CleanAsync(options.OutputPath, cancellationToken);
            }

            // 1. 加载配置（优先用 CLI 预加载的配置，避免重复 AutoLoadAsync——
            // 2026-10-21 性能优化：BuildHandler 已加载一次，此处复用）
            var config = options.PreloadedConfig ?? await _configLoader.AutoLoadAsync(
                options.SourcePath, cancellationToken);

            // 1.5 注册站点级短代码（layouts/shortcodes/*.html，对齐 Hugo"短代码即模板"；
            // 项目模板覆盖内置同名短代码）
            RegisterSiteShortcodes(options.SourcePath, config);
            Phase("1.配置加载+短码");

            // 2+3. 扫描+解析流式管线（2026-10-22 合并两阶段：读取 IO 与
            // 解析 CPU 在 Channel worker 内重叠，省 min(读,解析) 串行时间）
            var parsedContents = await ScanAndParseContentsAsync(
                options.SourcePath, config.ThemeNames, options, errors, warnings, cancellationToken);
            Phase("2+3.扫描+解析(流式)");

            // 4. 过滤草稿和未来内容
            var filteredContents = FilterContents(parsedContents, options);

            // 5. 构建页面上下文
            var pageContexts = BuildPageContexts(filteredContents, config, options.SourcePath);

            // 5.5 注册 页面→模板 依赖（增量构建时模板变化可反查受影响页面）
            RegisterTemplateDependencies(pageContexts, config, options.SourcePath);
            Phase("4.页面上下文");

            // 6. 构建分类系统
            var taxonomyService = new TaxonomyService(config.BaseURL, TaxonomyService.FromConfigured(config.Taxonomies));
            var taxonomies = taxonomyService.BuildTaxonomies(pageContexts);
            Phase("6.taxonomy");

            // 7. 构建站点上下文（含 data/ 目录数据 → site.data 模板变量）。
            // 签名顺带算出并传给 BuildSiteContext（#12 投影复用；XOR 聚合成本
            // 为一次全树 Walk，万页毫秒级）
            var siteData = await LoadSiteDataAsync(options, config.ThemeNames, config, cancellationToken);
            var translations = Translations.Load(
                options.SourcePath, config.ThemeNames, config.LanguageCode);
            var taxonomySignature = _currentPageTree is null ? null : ComputeTaxonomySignature(_currentPageTree);
            var siteContext = BuildSiteContext(config, pageContexts, taxonomies, siteData, translations, taxonomySignature);
            Phase("7.站点上下文");

            // 8. 渲染页面。资产路提前并行扇出（T4.2）：static/assets 的拷贝压缩
            // 与页面渲染无数据依赖（只读 config 与源目录），两路线程池互不阻塞，
            // 渲染完成后汇合
            var assetsTask = ProcessAssetsAsync(
                options.SourcePath, config.ThemeNames, options, config, errors, cancellationToken,
                Flint.Core.Templates.TemplateResource.BasePathOf(config.BaseURL));

            var renderedPages = await RenderPagesAsync(
                pageContexts, siteContext, config, options, errors, cancellationToken);
            Phase("8.渲染页面");

            // 8.5 生成首页——仅当树中不存在 home 页时兜底：
            // kind 分派（home→index）已让树版 home 页（带完整 front matter/params）
            // 渲染 index.html；此处再手建一份会与树版写同一路径，
            // 且兜底页缺 front matter 数据，失败反而误报 HOME001 使构建假失败
            if (!pageContexts.Any(p => p.Kind == "home"))
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
            Phase("9.分类页");

            // 10. 汇合资产路（T4.2：与阶段 8 并行启动，此处只等剩余部分）
            var processedAssets = await assetsTask;

            // 10.5 模板函数生成的资源产物（resources.Concat/FromString/Fingerprint）：
            // 模板引用的 RelPermalink 必须真实存在，否则 404。渲染已完成，此时收集
            foreach (var generated in _templateRenderer.GeneratedResources)
            {
                var binary = generated.BinaryContent;
                if (binary is null && string.IsNullOrEmpty(generated.Content))
                {
                    continue;
                }
                if (string.IsNullOrEmpty(generated.RelPermalink))
                {
                    continue;
                }

                processedAssets.Add(new ProcessedAsset
                {
                    OutputPath = generated.RelPermalink.TrimStart('/'),
                    SourcePath = generated.Name,
                    // 二进制载荷（位图直通）优先，避免 UTF-8 往返破坏 PNG/JPEG 字节
                    Content = binary ?? System.Text.Encoding.UTF8.GetBytes(generated.Content),
                    MediaType = generated.MediaType,
                    ContentHash = ""
                });
            }
            Phase("10.资源");

            // 11. 写入输出文件（先创建目录）
            await WriteOutputAsync(
                renderedPages, taxonomyPages, processedAssets, options, warnings, cancellationToken);
            Phase("11.写入输出");

            // 11.5 \u8f85\u52a9\u8f93\u51fa\uff08\u4e3b\u9898\u517c\u5bb9\u6279\u6b21\u4e00\uff09\uff1aaliases \u91cd\u5b9a\u5411\u9875/404/robots
            await GenerateAuxiliaryOutputsAsync(
                pageContexts, siteContext, config, options, cancellationToken);
            Phase("11.5辅助输出");

            // 12. 生成 Sitemap 和 Feed（在输出目录创建后）
            await GenerateSitemapAndFeedsAsync(
                pageContexts, siteContext, config, options, cancellationToken);
            Phase("12.sitemap/feed");

            // 12.5 汇聚模板 errorf 记录的错误：Hugo 的 errorf 不中断渲染
            //（页面照常产出），但构建结束按错误计数判失败——Flint 早期抛异常
            // 中止整页渲染，主题里一处 errorf 就把整站打成空页
            foreach (var message in _templateRenderer.DrainTemplateErrors())
            {
                errors.Add(new BuildError
                {
                    ErrorCode = "TEMPLATE001",
                    Message = message,
                    FilePath = options.SourcePath,
                    Line = 0,
                    Column = 0,
                    Severity = ErrorSeverity.Error
                });
            }
            Phase("12.5模板错误汇聚");

            stopwatch.Stop();

            if (tracePhases)
            {
                Console.Error.WriteLine($"[phase] {"总计",-24} {stopwatch.Elapsed.TotalMilliseconds,9:F1}ms");
            }

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
    /// 渲染期依赖精确登记（T4.3）：watch/serve 模式保持默认 true（增量反查依赖图
    /// 需要精确依赖）；一次性 CLI 构建显式 false，跳过每页 File.Exists 过滤
    /// </summary>
    public bool EnableDependencyTracking { get; init; } = true;

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
    /// HTML 内容。T4.1 起渲染即写盘，落盘后由 <c>TryWriteRenderedPageNowAsync</c>
    /// 置空（万页站点不再驻留全部 HTML）——故可变 set 而非 init
    /// </summary>
    public required string Content { get; set; }

    /// <summary>
    /// 确定性排序键：renderTargets 序 × 8 + 输出格式序（补页/分类页取高段位）。
    /// 并发渲染完成顺序不定，同路径去重时按本键取先序者胜——
    /// 胜者由页面集合顺序决定，与线程调度无关，跨构建产物稳定
    /// </summary>
    public int Sequence { get; init; }
}

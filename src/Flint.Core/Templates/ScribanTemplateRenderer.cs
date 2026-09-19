// Flint 静态站点生成器
// Scriban 模板渲染器实现

using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Flint.Core.Abstractions;
using Scriban;
using Scriban.Runtime;
// 使用别名避免命名空间冲突
using FlintTemplateContext = Flint.Core.Abstractions.TemplateContext;

namespace Flint.Core.Templates;

/// <summary>
/// 基于 Scriban 的模板渲染器实现
/// 支持模板继承、partial 引用和自定义函数
/// </summary>
public sealed partial class ScribanTemplateRenderer : ITemplateRenderer
{
    /// <summary>缓存条目：编译结果 + 源文件路径 + mtime（增量/长驻渲染的失效依据）</summary>
    private sealed record CachedTemplate(Template Template, string? SourcePath, DateTime ModifiedTimeUtc);

    private readonly string _templatesPath;
    // 主题布局回退目录（Hugo overlayfs 语义：站点 layouts 优先，主题按序回退）
    private readonly string[] _themeTemplatePaths;
    private readonly ConcurrentDictionary<string, CachedTemplate> _templateCache = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _dependencyCache = new();
    private readonly BuiltinTemplateFunctions _builtinFunctions;
    private readonly ITemplateLoader _templateLoader;
    private volatile bool _precompiled;

    /// <summary>
    /// 创建 Scriban 模板渲染器
    /// </summary>
    /// <param name="templatesPath">模板目录路径</param>
    /// <param name="baseUrl">站点基础 URL</param>
    /// <param name="themeTemplatePaths">主题布局回退目录（按序回退：站点 layouts 优先，主题次之，对齐 Hugo 主题叠加语义）</param>
    /// <param name="timeProvider">时间源（mtime 短窗缓存的时间戳用，可注入便于测试）</param>
    public ScribanTemplateRenderer(string templatesPath, string baseUrl = "", params string[] themeTemplatePaths)
    {
        _templatesPath = templatesPath;
        _themeTemplatePaths = themeTemplatePaths.Where(p => !string.IsNullOrEmpty(p)).ToArray();
        _builtinFunctions = new BuiltinTemplateFunctions(baseUrl);
        _templateLoader = new FileTemplateLoader(templatesPath, themeTemplatePaths);
        _timeProvider = TimeProvider.System;
        _builtinFunctions.TemplateExistsProbe = ProbeTemplateExists;
        _builtinFunctions.ErrorReporter = _templateErrors.Enqueue;
        InstallShortcodeContext();
    }

    /// <summary>
    /// 装配短代码渲染上下文：注册内置函数、日期对象与 partial 函数。
    /// 短代码在内容解析期用独立上下文渲染（无页面上下文），此前连 `partial` 都没有
    /// （Clarity 的 `partial "sprite"` 29 处 "function not found"）
    /// </summary>
    private void InstallShortcodeContext()
    {
        Content.Shortcodes.TemplateShortcode.EnrichContext = (context, shortcodeGlobals) =>
        {
            EnsureFunctionObjects(context);
            var pageLike = new ScriptObject { ["store"] = new PageStoreObject() };
            pageLike["Store"] = pageLike["store"];
            pageLike["scratch"] = pageLike["store"];
            pageLike["Scratch"] = pageLike["store"];
            // 短代码上下文的成员同样挂到 page 对象上：Hugo 短代码里 `.` 是**短代码上下文**
            // （.Name/.Parent/.Ordinal/.Get/.Inner），而迁移产物把所有"点上下文"统一写成
            // `page.x`（转换器无法区分布局与短代码里的点）。只挂短代码自己的键，
            // 不覆盖 page 已有的同名键（真实页面上用的成员优先）
            // —— narrow 的 tab.html 实测：`page?.parent`/`page?.name`/`page?.ordinal`
            // 此前全取空，6 处报 "must be nested inside tabs"
            foreach (var key in shortcodeGlobals.Keys)
            {
                if (!pageLike.ContainsKey(key))
                {
                    pageLike[key] = shortcodeGlobals[key];
                }
            }
            context.PushGlobal(new ScriptObject
            {
                ["page"] = pageLike,
                ["Page"] = pageLike,
                // 短代码阶段的页面上下文尚未接线（pageContext 传 null），
                // 故与 page 同值：迁移产物里的 `__page.X` 至少不因缺名字报错
                ["__page"] = pageLike
            });
            context.PushGlobal(BuildPartialGlobals(pageLike));
        };
    }

    /// <summary>
    /// 完整构造：接入模板资源提供者与环境信息（resources.*/css.*/hugo.* 命名空间）。
    /// 构建入口用此重载；未提供时对应命名空间注册为空对象（模板访问得 null 而非崩溃）
    /// </summary>
    public ScribanTemplateRenderer(
        string templatesPath,
        string baseUrl,
        TemplateEnvironmentInfo environment,
        ITemplateResourceProvider? resources,
        params string[] themeTemplatePaths)
    {
        _templatesPath = templatesPath;
        _themeTemplatePaths = themeTemplatePaths.Where(p => !string.IsNullOrEmpty(p)).ToArray();
        _builtinFunctions = new BuiltinTemplateFunctions(baseUrl, resources, environment);
        _templateLoader = new FileTemplateLoader(templatesPath, themeTemplatePaths);
        _timeProvider = TimeProvider.System;
        _builtinFunctions.TemplateExistsProbe = ProbeTemplateExists;
        _builtinFunctions.ErrorReporter = _templateErrors.Enqueue;
        InstallShortcodeContext();
    }

    /// <summary>
    /// 本构建内模板函数产生的资源产物（resources.Concat/FromString/Fingerprint 结果），
    /// 由 SiteBuilder 输出阶段写盘——否则模板引用的 RelPermalink 会 404
    /// </summary>
    public IReadOnlyList<TemplateResource> GeneratedResources => _builtinFunctions.GeneratedResources;

    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _templateErrors = new();

    /// <summary>记录一条模板级错误（渲染器内部的宽容降级路径用；
    /// 与模板 errorf 同一汇聚通道，构建收尾计入 BuildResult.Errors）</summary>
    private void ReportTemplateError(string message) => _templateErrors.Enqueue(message);

    /// <summary>
    /// 模板 <c>errorf</c> 记录的错误（Hugo 语义：记录后继续渲染，页面照常产出，
    /// 构建结束按错误计数判失败）。SiteBuilder 在构建收尾时取走并计入 BuildResult.Errors
    /// </summary>
    public IReadOnlyList<string> DrainTemplateErrors()
    {
        var list = new List<string>();
        while (_templateErrors.TryDequeue(out var message))
        {
            list.Add(message);
        }
        return list;
    }

    /// <summary>测试专用构造：时间源可注入</summary>
    public ScribanTemplateRenderer(string templatesPath, string baseUrl, TimeProvider timeProvider, params string[] themeTemplatePaths)
    {
        _templatesPath = templatesPath;
        _themeTemplatePaths = themeTemplatePaths.Where(p => !string.IsNullOrEmpty(p)).ToArray();
        _builtinFunctions = new BuiltinTemplateFunctions(baseUrl);
        _templateLoader = new FileTemplateLoader(templatesPath, themeTemplatePaths);
        _timeProvider = timeProvider;
        _builtinFunctions.TemplateExistsProbe = ProbeTemplateExists;
        InstallShortcodeContext();
    }

    /// <summary>
    /// 构造含 partial 家族的 globals（供短代码等"无页面上下文"的渲染路径使用）。
    /// 注意 partial 的上下文参数（dict 等）由此正常传递；partial 内访问 page 时
    /// 得到空对象（解析期无页面），但不会抛异常
    /// </summary>
    private ScriptObject BuildPartialGlobals(ScriptObject pageLike)
    {
        var g = new ScriptObject();
        g.TrySetValue(null, default, "partial", new PartialFunction(this, pageLike), readOnly: true);
        g.TrySetValue(null, default, "partialValue", new PartialValueFunction(this, pageLike), readOnly: true);
        g.TrySetValue(null, default, "partialcached", new PartialCachedFunction(this), readOnly: true);
        g.TrySetValue(null, default, "includeCached", new PartialCachedFunction(this), readOnly: true);
        g.TrySetValue(null, default, "template_exists",
            new TemplateExistsFunction((FileTemplateLoader)_templateLoader), readOnly: true);
        g[RetStoreKey] = new PageStoreObject();
        g.TrySetValue(null, default, "__partial_ret_set", new PartialRetSetFunction(), readOnly: true);
        return g;
    }

    /// <summary>templates.Exists 的探测实现：用与 include 相同的解析规则查文件</summary>
    private bool ProbeTemplateExists(string name)
    {
        try
        {
            var path = _templateLoader.GetPath(null!, default, name);
            return !string.IsNullOrEmpty(path) && File.Exists(path);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }

    /// <summary>
    /// 预编译所有模板（可选优化）
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>预编译的模板数量</returns>
    public async Task<int> PrecompileTemplatesAsync(CancellationToken cancellationToken = default)
    {
        if (_precompiled || !Directory.Exists(_templatesPath))
        {
            return 0;
        }

        // 缓存键是逻辑名（相对路径去 .html）：站点与主题同名模板必须站点优先。
        // 并行写入对同键非确定序——站点文件全量扫描，主题文件剔除与站点同名的
        // 逻辑名后追加
        var siteRelativeNames = Directory.Exists(_templatesPath)
            ? Directory.EnumerateFiles(_templatesPath, "*.html", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(_templatesPath, f).Replace('\\', '/'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var templateFiles = new List<(string Path, string Root)>(
            Directory.Exists(_templatesPath)
                ? Directory.EnumerateFiles(_templatesPath, "*.html", SearchOption.AllDirectories)
                    .Select(f => (f, _templatesPath))
                : []);
        foreach (var themePath in _themeTemplatePaths)
        {
            if (!Directory.Exists(themePath))
            {
                continue;
            }
            templateFiles.AddRange(Directory.EnumerateFiles(themePath, "*.html", SearchOption.AllDirectories)
                .Where(f => !siteRelativeNames.Contains(
                    Path.GetRelativePath(themePath, f).Replace('\\', '/'),
                    StringComparer.OrdinalIgnoreCase))
                .Select(f => (f, themePath)));
        }

        await Parallel.ForEachAsync(
            templateFiles,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = cancellationToken
            },
            async (entry, ct) =>
            {
                var (file, root) = entry;
                try
                {
                    // 缓存键按文件所属根计算相对路径：主题文件用站点根计算会得到
                    // ".." 前缀垃圾键，预编译结果在渲染期永不命中（纯浪费）
                    var relativePath = Path.GetRelativePath(root, file);
                    var templateName = relativePath.Replace('\\', '/');

                    // 移除 .html 扩展名
                    if (templateName.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                    {
                        templateName = templateName[..^5];
                    }

                    var content = await File.ReadAllTextAsync(file, ct);
                    var template = Template.Parse(content, file);

                    if (!template.HasErrors)
                    {
                        _templateCache[templateName] = new CachedTemplate(template, file, File.GetLastWriteTimeUtc(file));
                    }
                }
                catch (OperationCanceledException)
                {
                    // 取消必须逃逸，不能被当成"无法解析的模板"吞掉
                    throw;
                }
                catch (Exception)
                {
                    // 解析失败的模板静默缺席缓存；渲染期按需加载仍会暴露真实错误
                    // （诊断时机后移但信息不丢）
                }
            });

        _precompiled = true;
        return _templateCache.Count;
    }

    /// <summary>
    /// 页面感知渲染（A 组查找链）：候选链解析 → 物理模板 / 内置兜底 → 渲染。
    /// 候选链未命中且无内置兜底时抛 TemplateNotFoundException（SiteBuilder 据此
    /// 在 Skip 模式跳页、Error 模式 fail-fast）。
    /// </summary>
    public async ValueTask<string> RenderPageAsync(
        PageTemplateQuery query,
        FlintTemplateContext context,
        CancellationToken cancellationToken = default)
    {
        var path = ResolvePageTemplatePath(query);
        if (path is null)
        {
            // 内置兜底（对齐 Hugo embedded templates）：kind → 内置模板名。
            // 仅 html 格式——内置模板是 HTML 形态，格式变体不适用
            var builtinName = BuiltinNameForKind(query.Kind);
            if (builtinName is not null && IsHtmlOutputFormat(query.OutputFormat))
            {
                return await RenderBuiltinAsync(builtinName, context, cancellationToken);
            }

            throw new TemplateNotFoundException(
                DescribePageQuery(query),
                PageTemplateCandidates.Build(query)
                    .SelectMany(level => new[]
                    {
                        level.Name + ".html",
                        "_default/" + level.Name + ".html"
                    })
                    .ToList());
        }

        var logicalName = ToLogicalName(path);
        var template = await GetOrLoadTemplateAtPathAsync(logicalName, path, cancellationToken);
        return await RenderLoadedAsync(template, logicalName, path, context, cancellationToken);
    }

    /// <summary>
    /// 共用渲染入口：依赖追踪（逻辑名 + 已知物理路径）+ 运行时异常包装。
    /// <paramref name="physicalPath"/> 为 null 时（内置模板）只记逻辑名。
    /// </summary>
    private async ValueTask<string> RenderLoadedAsync(
        Template template,
        string templateName,
        string? physicalPath,
        FlintTemplateContext context,
        CancellationToken cancellationToken)
    {
        var scribanContext = CreateScribanContext(context);
        RenderDependencyTracker.Track(scribanContext, templateName);
        if (physicalPath is not null)
        {
            RenderDependencyTracker.Track(scribanContext, Path.GetFullPath(physicalPath));
        }

        try
        {
            var result = await template.RenderAsync(scribanContext);
            context.RenderedDependencies = RenderDependencyTracker.Extract(scribanContext);
            return result;
        }
        catch (Scriban.Syntax.ScriptRuntimeException ex)
        {
            // 携带模板名与行列位置，避免裸的 Scriban 内部堆栈直达用户
            throw new TemplateRenderException(
                templateName, ex.Message,
                ex.Span.Start.Line + 1, ex.Span.Start.Column + 1, ex);
        }
    }

    /// <summary>
    /// 按已知物理路径加载模板（页面感知查找链命中后的加载）：
    /// 缓存键为逻辑名，与 <see cref="GetOrLoadTemplateAsync"/> 的 mtime 失效语义一致
    /// </summary>
    private async ValueTask<Template> GetOrLoadTemplateAtPathAsync(
        string templateName,
        string templatePath,
        CancellationToken cancellationToken)
    {
        if (_templateCache.TryGetValue(templateName, out var cached) && !IsStale(cached))
        {
            return cached.Template;
        }

        string content;
        DateTime mtimeUtc;
        // 读前取 mtime、读后复核：读期间被写会产出"旧内容+新 mtime"条目使 stale 永久失效
        for (var attempt = 0; ; attempt++)
        {
            var mtimeBefore = File.GetLastWriteTimeUtc(templatePath);
            content = await File.ReadAllTextAsync(templatePath, cancellationToken);
            mtimeUtc = File.GetLastWriteTimeUtc(templatePath);
            if (mtimeUtc == mtimeBefore || attempt >= 2)
            {
                break;
            }
        }

        var parsed = Template.Parse(content, templatePath);
        if (parsed.HasErrors)
        {
            throw new TemplateParseException(
                templateName,
                parsed.Messages.Select(m => m.ToString()).ToList());
        }

        _templateCache[templateName] = new CachedTemplate(parsed, templatePath, mtimeUtc);
        // 模板重载说明磁盘模板可能变化，partialCached 结果缓存必须失效
        _partialResultCache.Clear();
        return parsed;
    }

    /// <summary>页面感知的物理模板存在性（不含内置兜底）</summary>
    public bool PageTemplateExists(PageTemplateQuery query) => ResolvePageTemplatePath(query) is not null;

    private string? ResolvePageTemplatePath(PageTemplateQuery query)
    {
        var lookup = _lookup ??= new TemplateLookup(_templatesPath, _themeTemplatePaths);
        return lookup.ResolveLayered(PageTemplateCandidates.Build(query));
    }

    /// <summary>kind → 内置兜底模板名（仅 taxonomy/term/list 有内置；page/home 无）</summary>
    private static string? BuiltinNameForKind(string kind) => kind.ToLowerInvariant() switch
    {
        "section" => "list",
        "taxonomy" => "taxonomy",
        "term" => "term",
        _ => null
    };

    private static bool IsHtmlOutputFormat(string? format) =>
        string.IsNullOrEmpty(format) || format.Equals("html", StringComparison.OrdinalIgnoreCase);

    /// <summary>渲染内置模板（无物理文件路径，缓存键加 builtin: 前缀避免与同名物理模板冲突）</summary>
    private async ValueTask<string> RenderBuiltinAsync(
        string builtinName,
        FlintTemplateContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cacheKey = "builtin:" + builtinName;
        if (!_templateCache.TryGetValue(cacheKey, out var cached))
        {
            if (!BuiltinTemplates.TryGetValue(builtinName, out var builtinContent))
            {
                throw new TemplateNotFoundException(builtinName, []);
            }

            // SourcePath 为 null → IsStale 恒 false（内置内容不随磁盘变化）
            cached = new CachedTemplate(
                Template.Parse(builtinContent, cacheKey), null, DateTime.MinValue);
            _templateCache[cacheKey] = cached;
        }

        return await RenderLoadedAsync(cached.Template, cacheKey, null, context, cancellationToken);
    }

    /// <summary>物理路径 → 逻辑名（相对站点 templates 根，统一 / 分隔；跨根时取相对该根的路径）</summary>
    private string ToLogicalName(string physicalPath)
    {
        var full = Path.GetFullPath(physicalPath);
        foreach (var root in new[] { _templatesPath }.Concat(_themeTemplatePaths))
        {
            if (string.IsNullOrEmpty(root))
            {
                continue;
            }

            var rootFull = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            {
                return full[rootFull.Length..].Replace('\\', '/');
            }
        }

        return Path.GetFileName(full);
    }

    private static string DescribePageQuery(PageTemplateQuery query)
    {
        var parts = new List<string> { "kind=" + query.Kind };
        if (!string.IsNullOrEmpty(query.Layout))
        {
            parts.Add("layout=" + query.Layout);
        }

        if (!string.IsNullOrEmpty(query.DeclaredType))
        {
            parts.Add("type=" + query.DeclaredType);
        }

        if (!string.IsNullOrEmpty(query.Section))
        {
            parts.Add("section=" + query.Section);
        }

        if (!string.IsNullOrEmpty(query.Taxonomy))
        {
            parts.Add("taxonomy=" + query.Taxonomy);
        }

        if (!IsHtmlOutputFormat(query.OutputFormat))
        {
            parts.Add("format=" + query.OutputFormat);
        }

        return string.Join(", ", parts);
    }

    /// <inheritdoc />
    public async ValueTask<string> RenderTemplateFileAsync(
        string filePath,
        FlintTemplateContext context,
        CancellationToken cancellationToken = default)
    {
        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        var template = Template.Parse(content, filePath);
        if (template.HasErrors)
        {
            throw new TemplateParseException(
                filePath,
                template.Messages.Select(m => m.ToString()).ToList());
        }

        var scribanContext = CreateScribanContext(context);
        return await template.RenderAsync(scribanContext);
    }

    /// <inheritdoc />
    public async ValueTask<string> RenderAsync(
        string templateName,
        FlintTemplateContext context,
        CancellationToken cancellationToken = default)
    {
        var template = await GetOrLoadTemplateAsync(templateName, cancellationToken);
        var scribanContext = CreateScribanContext(context);
        TrackTemplateDependency(scribanContext, templateName);

        try
        {
            var result = await template.RenderAsync(scribanContext);
            context.RenderedDependencies = RenderDependencyTracker.Extract(scribanContext);
            return result;
        }
        catch (Scriban.Syntax.ScriptRuntimeException ex)
        {
            // 携带模板名与行列位置，避免裸的 Scriban 内部堆栈直达用户
            throw new TemplateRenderException(
                templateName, ex.Message,
                ex.Span.Start.Line + 1, ex.Span.Start.Column + 1, ex);
        }
    }

    /// <summary>
    /// 本次构建中被模板调用过 <c>.Paginate</c> 的列表页 URL。
    /// Hugo 的分页页由**模板调用**驱动：探测实证（v0.166）home 模板不调用
    /// <c>.Paginate</c> 时站点不产出 <c>/page/2/</c>，而列表模板调用时产出
    /// <c>/page/1/</c>（跳转页）+ <c>/page/2..N/</c>。故分页页产出前先看是否有标记。
    /// 并发渲染下用并发字典；站点渲染开始时经 <see cref="ResetPaginateTracking"/> 清空
    /// （用静态状态是因为页面对象工厂与分页函数都是静态路径，拿不到渲染器实例）
    /// </summary>
    private static readonly ConcurrentDictionary<string, byte> PaginatedListUrls = new(StringComparer.Ordinal);

    /// <summary>
    /// 当前构建的**全量站点页面**（含 section/term 页）——<c>.GetPage</c> 用它解析路径。
    /// 页面对象按引用共享缓存（CWT），创建时机不同会拿到不同快照（列表渲染早于页面渲染），
    /// 只靠构造参数会让 `<c>.GetPage "docs"</c>` 时有时无地找不到 section（hugo-book 实测），
    /// 故由站点渲染入口统一登记，调用时读取（与分页标记同一模式）
    /// </summary>
    private static IReadOnlyList<Flint.Core.Abstractions.PageContext>? CurrentSitePages;

    /// <summary>登记当前构建的全量站点页面（站点渲染开始时调用）</summary>
    internal static void SetCurrentSitePages(IReadOnlyList<Flint.Core.Abstractions.PageContext> pages) => CurrentSitePages = pages;

    /// <summary>
    /// 当前构建的**站点对象**——页面对象上的 <c>.Site</c> 用它（Hugo 的 `.Site` 在任意
    /// 页面上可用；迁移产物里的 `$page.Site.Params…` 形态会落到 `$page.site.params`）。
    /// 与 <see cref="CurrentSitePages"/> 同一模式：页面对象按引用共享、构造时机不定，
    /// 故由站点渲染入口统一登记
    /// </summary>
    private static Flint.Core.Abstractions.SiteContext? CurrentSite;

    /// <summary>登记当前构建的站点对象（站点渲染开始时调用）</summary>
    internal static void SetCurrentSite(Flint.Core.Abstractions.SiteContext site) => CurrentSite = site;

    /// <summary>取当前构建的站点对象（未登记时为 null）</summary>
    internal static ScriptObject? CurrentSiteObject() =>
        CurrentSite is null ? null : CreateSiteObject(CurrentSite);

    /// <summary>标记某列表页被模板分页（由 <c>.Paginate</c> 调用触发）</summary>
    internal static void NotePaginateInvoked(string? relPermalink)
    {
        if (!string.IsNullOrEmpty(relPermalink))
        {
            PaginatedListUrls[relPermalink] = 0;
        }
    }

    /// <summary>
    /// 模板传给 <c>.Paginate</c> 的**显式集合与页大小**（Hugo 语义：按传入集合与尺寸分页）。
    /// 站点侧据此计算分页页数与每页内容，而不是照当前页 <c>Pages</c> 切片
    /// </summary>
    /// <param name="Items">显式集合（Hugo 的 `.Paginate $pages` 第一参）</param>
    /// <param name="Size">显式页大小（`.Paginate $pages N` 第二参）；未给时为 0 → 用站点配置</param>
    internal sealed record PaginateRegistration(
        IReadOnlyList<Flint.Core.Abstractions.PageContext> Items, int Size);

    private static readonly ConcurrentDictionary<string, PaginateRegistration> PaginateCollections = new(StringComparer.Ordinal);

    /// <summary>登记显式分页集合与页大小</summary>
    internal static void NotePaginateCollection(
        string? relPermalink, IReadOnlyList<Flint.Core.Abstractions.PageContext> items, int size = 0)
    {
        if (!string.IsNullOrEmpty(relPermalink))
        {
            PaginateCollections[relPermalink] = new PaginateRegistration(items, size);
        }
    }

    /// <summary>取显式分页集合与页大小（无则 null）</summary>
    internal static PaginateRegistration? GetPaginateCollection(string relPermalink) =>
        PaginateCollections.TryGetValue(relPermalink, out var registration) ? registration : null;

    /// <summary>
    /// 模板 <c>.Paginate</c> **实际创建**的分页器（按列表页 URL 登记）。
    /// Hugo 语义：`.Paginate` 会**改写该页的 `.Paginator`**，随后的 `.Paginator` 读到的是
    /// 这一次创建的分页器——而不是站点预绑定的隐式分页器。
    /// 反例（修复前实测）：stack 的 home 用
    /// <c>where .Site.RegularPages "Type" "in" site.Params.mainSections</c> 得到**空集**
    /// （其主题配置 mainSections = ["post"]，与内容段名 posts 不匹配）→ Hugo 侧
    /// <c>.Paginate []</c> → `.Paginator` 为 1 页、不渲染页码链接、只产出 /page/1/；
    /// 而预绑定的隐式分页器是"站点全部常规页"→ 3 页 → Flint 渲染出指向未产出页的链接
    /// </summary>
    private static readonly ConcurrentDictionary<string, PaginatorView> PaginatePagers = new(StringComparer.Ordinal);

    /// <summary>登记模板创建的分页器</summary>
    internal static void NotePaginatePager(string? relPermalink, PaginatorView pager)
    {
        if (!string.IsNullOrEmpty(relPermalink))
        {
            PaginatePagers[relPermalink] = pager;
        }
    }

    /// <summary>取模板创建的分页器（无则 null → 用预绑定的隐式分页器）</summary>
    internal static PaginatorView? GetPaginatePager(string relPermalink) =>
        PaginatePagers.TryGetValue(relPermalink, out var pager) ? pager : null;

    /// <summary>该列表页是否被模板分页过</summary>
    internal static bool WasPaginateInvoked(string relPermalink) =>
        PaginatedListUrls.ContainsKey(relPermalink);

    /// <summary>清空分页标记（站点渲染开始时调用，避免跨构建串味）</summary>
    internal static void ResetPaginateTracking()
    {
        PaginatedListUrls.Clear();
        PaginateCollections.Clear();
        PaginatePagers.Clear();
    }

    /// <summary>
    /// 依赖记录中的顶层模板：逻辑名 + 物理路径（内置回退模板无文件，只记逻辑名）。
    /// 只记逻辑名会被依赖注册的文件过滤丢弃，导致覆盖注册后顶层模板变化反查不到页面
    /// </summary>
    private void TrackTemplateDependency(Scriban.TemplateContext scribanContext, string templateName)
    {
        RenderDependencyTracker.Track(scribanContext, templateName);
        try
        {
            RenderDependencyTracker.Track(scribanContext, Path.GetFullPath(ResolveTemplatePath(templateName)));
        }
        catch (TemplateNotFoundException)
        {
            // 内置回退模板：无物理文件
        }
    }

    /// <summary>
    /// 渲染 hook 模板（render-link/image/heading）：上下文为简单键值对，
    /// 无页面/站点对象（对齐 Hugo render hooks 的最小变量集）。
    /// 同步执行——Markdig 渲染管线是同步的；模板不存在返回 null
    /// </summary>
    public string? RenderHookTemplate(string hookTemplateName, IReadOnlyDictionary<string, object> vars)
    {
        Template template;
        try
        {
            template = GetOrLoadTemplate(hookTemplateName);
        }
        catch (TemplateNotFoundException)
        {
            return null;
        }

        var globals = new ScriptObject();
        // 钩子变量同时挂在顶层（Hugo 的 `.Level`/`.Text` 直取）与 `page` 之下
        // （迁移器把钩子内裸 `.X` 统一转成 `page?.x`，与页面模板同规则）——
        // 两者并存使原生 Flint 模板与迁移产物都能解析
        var pageLike = new ScriptObject();
        // 页面级 Store 必须一并提供：hook 内 `include` 的 partial 若走
        // `page.store.set` 返回值通道（Stack helper/image.html 实测），
        // 缺 store 会报 "Cannot get the member page.store.set for a null object"
        // 缺失键返回空对象：hook 早于布局渲染，暂存里的值（如 LoveIt 的 "params"）
        // 尚未写入，直接给 null 会让 `$params.code` 报 null object 并使整篇解析失败
        var hookStore = new PageStoreObject { MissingKeyReturnsEmptyObject = true };
        pageLike["store"] = hookStore;
        pageLike["Store"] = hookStore;
        pageLike["scratch"] = hookStore;
        pageLike["Scratch"] = hookStore;
        // .GetPage / .GetTerms 等页面方法在 hook 上下文无站点索引可用：
        // 注册**宽松 stub**（返回 null，不抛异常），让主题的 with/if 守卫安全跳过
        //（Congo 的 _markup/render-link.html 用 `page.get_page`，70 处实测）
        var hookGetPage = new StubPageMethodFunction();
        pageLike["get_page"] = hookGetPage;
        pageLike["GetPage"] = hookGetPage;
        pageLike["get_terms"] = hookGetPage;
        pageLike["GetTerms"] = hookGetPage;
        // .Param / .HasShortcode 同族：hook 里也常按页面方法调用
        //（FixIt 的 _markup/render-heading.html → _partials/function/param.html
        //  内 `.Page.Param`，实测 "The function `page.param` was not found" 使
        //  整篇内容解析失败）。param 返回 null 让主题的默认值链继续走，
        //  render_string 返回空串（字符串运算不因 null 抛错）
        pageLike["param"] = hookGetPage;
        pageLike["Param"] = hookGetPage;
        pageLike["has_shortcode"] = hookGetPage;
        pageLike["HasShortcode"] = hookGetPage;
        // .Resources（页面资源对象）：render hook 在内容解析期运行，拿不到页面
        // 资源索引，注册**宽松 stub**（查不到 → null / 空集合），让主题的
        // `{{ with .Resources.GetMatch $src }}` 守卫安全跳过并走回退分支。
        // 缺它会报 "Cannot get the member page.resources.getmatch for a null object"
        // 并让**整篇内容解析失败**（Congo 的 _markup/render-image.html 实测）
        var hookResources = new ScriptObject();
        var resourceLookupStub = new StubPageMethodFunction();
        foreach (var resName in new[] { "getmatch", "GetMatch", "get", "Get", "minify", "Minify",
                                       "fingerprint", "Fingerprint", "concat", "Concat",
                                       "process", "Process", "fill", "Fill", "resize", "Resize",
                                       "fit", "Fit", "crop", "Crop" })
        {
            hookResources[resName] = resourceLookupStub;
        }
        var resourceListStub = new StubPageMethodFunction(new ScriptArray());
        foreach (var resName in new[] { "match", "Match", "bytype", "ByType" })
        {
            hookResources[resName] = resourceListStub;
        }
        pageLike["resources"] = hookResources;
        pageLike["Resources"] = hookResources;

        var hookRenderString = new StubPageMethodFunction("");
        pageLike["render_string"] = hookRenderString;
        pageLike["RenderString"] = hookRenderString;
        foreach (var kv in vars)
        {
            globals[kv.Key] = kv.Value;
            pageLike[kv.Key] = kv.Value;
            // 迁移产物用 snake_case 键（如 plain_text/anchor），补一份
            pageLike[ToSnakeKey(kv.Key)] = kv.Value;
        }
        globals["store"] = hookStore;
        globals["scratch"] = hookStore;
        globals["page"] = pageLike;
        globals["Page"] = pageLike;
        globals["__page"] = pageLike;

        var context = new FlintScribanContext
        {
            TemplateLoader = _templateLoader,
            MemberRenamer = member => member.Name,
            StrictVariables = false,
            // 对齐 Hugo：循环迭代数不做 1000 级人为限制——万页站点的列表/
            // taxonomy 页单循环即超默认值（同数据集对比测试实证）
            // Scriban 的函数递归计数在"嵌套渲染 + ret 提前返回"时不递减，
            // 会**跨调用累积**：partial 调用上百次的主题（hugo-book 每页调
            // docs/title.html / icon.html 等）会假性超限（opengraph 的
            // "Exceeding number of recursive depth limit 100 for node: default site.title"）。
            // 与 LoopLimit 同口径关掉引擎侧计数，递归安全由 partial 渲染的
            // 自有深度守卫负责
            RecursiveLimit = 0,
            LoopLimit = 1_000_000
        };
        // 内置函数（含 safe_html 等 safe* 家族）在钩子路径同样可用——缺失时
        // `{{ .Text | safeHTML }}` 会报 "The function `safe_html` was not found"
        EnsureFunctionObjects(context);

        // partial / partialValue：hook 内 include 的 partial 可能走
        // `page.store.set` 返回值通道（FixIt 的 _markup/render-heading.html 等），
        // 故与页面路径同样注册（hookStore 作为通道容器）
        globals.TrySetValue(context, default, "partial",
            new PartialFunction(this, pageLike), readOnly: true);
        globals.TrySetValue(context, default, "partialValue",
            new PartialValueFunction(this, pageLike), readOnly: true);
        globals[RetStoreKey] = new PageStoreObject();
        globals.TrySetValue(context, default, "__partial_ret_set",
            new PartialRetSetFunction(), readOnly: true);
        globals.TrySetValue(context, default, "template_exists",
            new TemplateExistsFunction((FileTemplateLoader)_templateLoader), readOnly: true);

        // i18n：hook 渲染发生在内容解析期（可能早于站点上下文装配），
        // 故用可设置的翻译表快照；未装配时为空表（查不到的键渲染为空串，
        // 与 Hugo 缺键语义一致）。这是已知限制：hook 内的翻译在首次构建
        // 的极早期可能为空（congo/hextra/fixit 的 render-heading 用 i18n）
        globals.TrySetValue(context, default, "i18n",
            new I18nFunction(Translations ?? EmptyTranslations), readOnly: true);

        context.PushGlobal(globals);

        try
        {
            return template.Render(context);
        }
        catch (Scriban.Syntax.ScriptRuntimeException ex)
        {
            throw new TemplateRenderException(
                hookTemplateName, ex.Message,
                ex.Span.Start.Line + 1, ex.Span.Start.Column + 1, ex);
        }
    }

    /// <summary>camelCase/PascalCase → snake_case（`plainText`/`PlainText` → `plain_text`）</summary>
    private static string ToSnakeKey(string key)
    {
        var sb = new System.Text.StringBuilder(key.Length + 4);
        for (var i = 0; i < key.Length; i++)
        {
            var c = key[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && (char.IsLower(key[i - 1]) || (i + 1 < key.Length && char.IsLower(key[i + 1]))))
                {
                    sb.Append('_');
                }
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    /// <inheritdoc />
    /// <summary>
    /// 模板文件是否为**空文件**（0 字节）。用于 404 这类"空模板即视为缺失"的判定：
    /// Hugo v0.166 实测空的 404 模板不产出 404.html，而空的 single/partial 都是合法模板
    /// （主题普遍用空 hook 文件）——故不能在模板扫描里一刀切跳过空文件
    /// </summary>
    public bool TemplateFileIsEmpty(string templateName)
    {
        try
        {
            var path = ResolveTemplatePath(templateName);
            return !string.IsNullOrEmpty(path) && new FileInfo(path) is { Exists: true, Length: 0 };
        }
        catch (TemplateNotFoundException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public bool TemplateExists(string templateName)
    {
        try
        {
            ResolveTemplatePath(templateName);
            return true;
        }
        catch (TemplateNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// 精确相对路径的模板存在性（C4 视图查找用）：只接受逐字相等的相对路径，
    /// 不做文件名段模糊匹配（Hugo 视图要求目录逐段匹配）
    /// </summary>
    internal bool TemplateExistsExact(string relativeName)
    {
        var lookup = _lookup ??= new TemplateLookup(_templatesPath, _themeTemplatePaths);
        return lookup.ResolveExact(relativeName) is not null;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetDependencies(string templateName)
    {
        if (_dependencyCache.TryGetValue(templateName, out var cached))
        {
            return cached;
        }

        var dependencies = new List<string>();
        CollectDependencies(templateName, dependencies, new HashSet<string>());

        var result = dependencies.AsReadOnly();
        _dependencyCache.TryAdd(templateName, result);
        return result;
    }

    /// <summary>
    /// 清除模板缓存
    /// </summary>
    public void ClearCache()
    {
        _templateCache.Clear();
        _dependencyCache.Clear();
        // partialCached 结果缓存：模板文件变化后必须失效（结果按 build 生命周期缓存）
        _partialResultCache.Clear();
    }

    /// <summary>partialCached 结果缓存：键 = name + variants（对齐 Hugo partialCached 语义）</summary>
    private readonly ConcurrentDictionary<string, string> _partialResultCache = new();

    /// <summary>
    /// 渲染 partial 并按 name+variants 缓存结果（对齐 Hugo partialCached）：
    /// 输出以隔离 context 渲染——不共享调用页的输出流（避免交错双写）；
    /// partial 内不应引用 page 变量（对齐 Hugo：调用方须保证输出只依赖
    /// name+variants，依赖页面的 partial 用普通 include）
    /// </summary>
    internal object RenderPartialCached(
        Scriban.TemplateContext scribanContext,
        string name,
        System.Collections.Generic.IReadOnlyList<object> variants)
    {
        ArgumentNullException.ThrowIfNull(name);
        var path = _templateLoader.GetPath(scribanContext, default, name)
            ?? throw new InvalidOperationException($"partial 路径解析失败: {name}");
        // 结果缓存键纳入 partial 源文件 mtime：partial 被修改后键变化自然失效——
        // 不依赖主模板重载触发（partial 变化时主模板缓存命中、不走重载清缓存路径）。
        // 已知取舍：partial 内嵌套 include 的子 partial 变化不改变外层 mtime，
        // 外层缓存条目不失效（精确传递失效需 partial 级依赖图，见记录项）
        var mtimeTicks = GetMtimeUtc(path).Ticks;
        // 缓存键的 variant 签名：页面对象用 permalink 区分（默认 ToString 只给类型名，
        // 会使所有页面共用一条缓存 → 首个页面的输出被复用，实测缺陷）
        var cacheKey = name + "\u0001" + string.Join("\u0002", variants.Select(VariantSignature))
            + "\u0003" + mtimeTicks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return _partialResultCache.GetOrAdd(cacheKey, _ =>
        {
            var content = _templateLoader.Load(scribanContext, default, path)
                ?? throw new InvalidOperationException($"partial 未找到: {name}");
            var partialTemplate = Template.Parse(content, path);
            if (partialTemplate.HasErrors)
            {
                throw new TemplateParseException(
                    name,
                    partialTemplate.Messages.Select(m => m.ToString()).ToList());
            }

            // 隔离输出流但**继承调用者全局**：Hugo 的 partialCached 内可访问
            // site/page/params（缓存正确性由模板作者保证——输出只依赖 name+variants，
            // Hugo 同此约定）。此前完全剥离上下文，使引用 site.params 的
            // partialCached 报 "Cannot get the member site.params for a null object"
            // （Ananke 的 social/follow.html 实证）
            var isolatedContext = new FlintScribanContext
            {
                TemplateLoader = _templateLoader,
                MemberRenamer = member => member.Name,
                StrictVariables = false,
                // Scriban 的函数递归计数在"嵌套渲染 + ret 提前返回"时不递减，
            // 会**跨调用累积**：partial 调用上百次的主题（hugo-book 每页调
            // docs/title.html / icon.html 等）会假性超限（opengraph 的
            // "Exceeding number of recursive depth limit 100 for node: default site.title"）。
            // 与 LoopLimit 同口径关掉引擎侧计数，递归安全由 partial 渲染的
            // 自有深度守卫负责
            RecursiveLimit = 0,
            LoopLimit = 1_000_000
            };
            // 内置函数 + 日期对象都要装：只装前者时 `date.to_string` 会落到
            // Scriban **内置**的 date 对象（要求 DateTime），而 Flint 页面日期是
            // DateTimeOffset → "Unable to convert type `DateTimeOffset` to `DateTime`"
            //（Ananke site-footer.html 经 partialcached 渲染时实测 14 处）
            EnsureFunctionObjects(isolatedContext);
            ScriptObject? callerGlobals = null;
            if (scribanContext.CurrentGlobal is { } cg && cg is ScriptObject cgObj)
            {
                callerGlobals = cgObj;
                isolatedContext.PushGlobal(cgObj);
            }

            // partial 家族必须在隔离上下文里显式注册：CurrentGlobal 只是**最顶层**
            // 的 global 对象，而 partial/partialValue 挂在被它覆盖的下层 → 隔离上下文
            // 里没有 partial（Clarity 的 `partialcached "top"` 内再调
            // `partial "sprite"` 报 "The function `partial` was not found"，29 处）
            var cachedPage = callerGlobals is not null &&
                callerGlobals.ContainsKey("page") && callerGlobals["page"] is ScriptObject pg
                ? pg
                : new ScriptObject { ["store"] = new PageStoreObject() };
            isolatedContext.PushGlobal(BuildPartialGlobals(cachedPage));

            // variants 置于栈顶（覆盖同名全局），对齐 Hugo 点参数语义
            var partialGlobals = new ScriptObject();
            partialGlobals["variants"] = new ScriptArray(variants.Select(v => (object)v));
            isolatedContext.PushGlobal(partialGlobals);
            // 同步 Render：隔离 context 的模板加载（FileTemplateLoader）为同步实现，
            // 无需异步——避免 sync-over-async（G17 棘轮）
            return partialTemplate.Render(isolatedContext);
        });
    }

    /// <summary>
    /// 以调用者上下文渲染 partial 并返回**类型还原**后的值（B2，对齐 Hugo partial 返回值）：
    /// Hugo 的 <c>{{ partial "x" . }}</c> 可作函数用（partial 内 <c>{{ return X }}</c> 返回值），
    /// Ananke 等主题大量使用 <c>{{ $v := partial "func/Foo" . }}</c>。
    /// Scriban 的 include 只给字符串——此函数把 "true"/"false" 还原为布尔、
    /// 纯数字还原为数值，使 <c>{{ if (partial "x" .) }}</c> 的布尔判断语义正确
    /// </summary>
    /// <summary>
    /// partial 嵌套深度守卫（替代已关闭的 Scriban 递归计数）：真无限递归
    /// （partial A → B → A）给出明确错误，而不是无限循环或误导性的
    /// "recursive depth limit 100"
    /// </summary>
    [ThreadStatic]
    private static int _partialDepth;

    /// <summary>诊断用：按深度环形记录 partial 名（溢出时报出最近 12 层调用链）</summary>
    [ThreadStatic]
    private static string[]? _partialTrace;

    private const int MaxPartialDepth = 200;

    private static void EnterPartial(string name)
    {
        _partialTrace ??= new string[16];
        var depth = ++_partialDepth;
        _partialTrace[depth % 16] = name;
        if (depth > MaxPartialDepth)
        {
            var tail = new List<string>();
            for (var d = depth - 12; d < depth; d++)
            {
                if (d >= 0 && _partialTrace[d % 16] is { } n)
                {
                    tail.Add($"{d}:{n}");
                }
            }
            _partialDepth = 0;
            throw new InvalidOperationException(
                $"partial 嵌套深度超过 {MaxPartialDepth}：疑似 partial 互相递归。最近 12 层: {string.Join(" → ", tail)}");
        }
        // ret 键记录栈：与 partial 渲染栈同生命周期（null = 本层未调 __partial_ret_set）
        RetKeyStack ??= new List<string?>();
        RetKeyStack.Add(null);
    }

    private static void ExitPartial() => _partialDepth--;

    /// <summary>当前 partial 层的 ret 键（<see cref="PartialRetSetFunction"/> 写入）</summary>
    internal static List<string?>? RetKeyStack { get; private set; }

    internal static void RecordRetKey(string key)
    {
        if (RetKeyStack is { Count: > 0 })
        {
            RetKeyStack[^1] = key;
        }
    }

    internal static string? PopRetKey()
    {
        if (RetKeyStack is { Count: > 0 })
        {
            var key = RetKeyStack[^1];
            RetKeyStack.RemoveAt(RetKeyStack.Count - 1);
            return key;
        }
        return null;
    }



    internal object RenderPartialWithType(
        Scriban.TemplateContext callerContext,
        string name,
        Scriban.Parsing.SourceSpan callerSpan = default)
    {
        // 模板缺失 → **宽容**：记录诊断并输出空。主题常引用由 Hugo Module 提供的
        // partial（FixIt 的 `_funcs/get-page-images` 来自 LoveIt 模块），独立克隆
        // 时不存在；硬抛会让整页（乃至整站）失败，而 Hugo 侧该分支常常从未执行。
        // callerSpan 决定"相对调用者目录"的候选（Hugo 的 partial 名可相对调用者目录
        // 解析：PaperMod 的 `_partials/templates/opengraph.html` 调 `_funcs/get-page-images`
        // 命中 `_partials/templates/_funcs/get-page-images.html`）——走 Scriban 内置
        // include 时该 span 由 Scriban 提供，改走 Flint 自己的 partial 后必须显式传入
        var path = _templateLoader.GetPath(callerContext, callerSpan, name);
        if (path is null)
        {
            ReportTemplateError($"partial 未找到（已按空输出处理）: {name}");
            return string.Empty;
        }
        string? content;
        try
        {
            content = _templateLoader.Load(callerContext, default, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // loader 未命中时返回「候选路径」交 Load 抛错（带上下文）；
            // 此处兜住并降级——IO/路径异常不该让整页失败
            ReportTemplateError($"partial 未找到（已按空输出处理）: {name} — {ex.Message}");
            return string.Empty;
        }
        if (content is null)
        {
            ReportTemplateError($"partial 内容不可读（已按空输出处理）: {name}");
            return string.Empty;
        }
        // partial 模板按物理路径**缓存解析结果**：每次调用都 Template.Parse 时，
        // ① 深调用链下解析器自身的递归会顶到栈（"The parser recursive depth limit
        // was reached near a stack overflow"，hugo-book 的 title.html 实测）；
        // ② 高频 partial 反复解析（hugo-book 单次构建调 title.html 1871 次）
        var cacheKey = "partial:" + path;
        // 内置模板的 path 是**哨兵**（U+0001builtin:xxx）而非物理文件：不能走 mtime
        // （File.GetLastWriteTimeUtc 内部 Path.GetFullPath 会把哨兵当相对路径解析并抛
        // ArgumentException），改为固定 mtime 缓存
        CachedTemplate? cached;
        if (path.StartsWith(FileTemplateLoader.BuiltinPrefix, StringComparison.Ordinal))
        {
            if (!_templateCache.TryGetValue(cacheKey, out var builtinCached))
            {
                var builtinParsed = Template.Parse(content, path);
                if (builtinParsed.HasErrors)
                {
                    throw new TemplateParseException(
                        name, builtinParsed.Messages.Select(m => m.ToString()).ToList());
                }
                builtinCached = new CachedTemplate(builtinParsed, null, DateTime.MinValue);
                _templateCache[cacheKey] = builtinCached;
            }
            cached = builtinCached;
        }
        else if (!_templateCache.TryGetValue(cacheKey, out cached) || IsStale(cached))
        {
            var parsedPartial = Template.Parse(content, path);
            if (parsedPartial.HasErrors)
            {
                throw new TemplateParseException(
                    name,
                    parsedPartial.Messages.Select(m => m.ToString()).ToList());
            }
            cached = new CachedTemplate(parsedPartial, path, GetMtimeUtc(path));
            _templateCache[cacheKey] = cached;
        }
        // 两个分支都赋了非空值（缓存未命中时当场解析）
        var partialTemplate = cached!.Template;

        // 复用调用者上下文：partial 内可见 page/site/内置函数（Hugo partial 的
        // 第二参数语义在 Scriban 中由共享上下文天然满足）
        EnterPartial(name);
        try
        {
            // 输出缓冲隔离（否则会清掉调用者已累积的输出）：
            // Scriban 的 Template.Render 把 context.Output 当作**自己的**缓冲——
            // 渲染完读取其内容后执行 `Builder.Length = 0`。直接传调用者的 context，
            // 读到的就是"调用者已写内容 + partial 自身输出"，随后被整体清空；
            // 只有该返回值又恰好被写回输出时才看不出问题，一旦调用点不写回
            // （如条件分支、赋值、多级嵌套），调用者此前的内容就凭空消失——
            // narrow 实测：baseof 里 head 之后的 header 一渲染，TOP/MID 全丢，
            // 整页只剩空白（构建仍报"成功"，属静默失败）。
            // PushOutput 是 Scriban 为此提供的隔离手段：新缓冲只装本次 partial 的
            // 输出，返回的字符串因此也恰好是 partial 的纯输出
            callerContext.PushOutput();
            try
            {
                var rendered = partialTemplate.Render(callerContext);
                // Hugo 的 `{{ partial "x" . }}` 语句会**输出**返回值：partial 内的
                // return 改写为 store 通道 + ret，若本次渲染调过 __partial_ret_set
                // （有 ret 键），把返回值**追加**到文本输出——否则 title 类 partial
                // 经文本通道调用时输出为空（hugo-book 菜单标题实测）
                if (PopRetKey() is { } retKey)
                {
                    var retText = ResolveStore(callerContext)?.Get(retKey)?.ToString();
                    if (!string.IsNullOrEmpty(retText))
                    {
                        rendered += retText;
                    }
                }
                return RestoreScalarType(rendered);
            }
            finally
            {
                callerContext.PopOutput();
            }
        }
        finally
        {
            ExitPartial();
        }
    }

    /// <summary>
    /// 以**显式上下文**渲染 partial（Hugo 的 <c>partial "x" CONTEXT</c>）：
    /// CONTEXT 成为 partial 内的 <c>.</c>（点）。迁移产物的裸 <c>.X</c> 被转成
    /// <c>page.x</c>，故把 CONTEXT 覆盖到调用者上下文的 <c>page</c> 全局——
    /// 用 <see cref="Scriban.TemplateContext.PushGlobal"/> 上一层同名对象遮蔽，
    /// 渲染后 <c>PopGlobal</c> 恢复（作用域严格限于本次调用）。
    /// 实测动机：Stack 的 <c>partial "helper/icon" "search"</c> 原先丢弃 "search"，
    /// 使 partial 内 <c>.</c> 取不到值（报 "icon '%s.svg' is not found"）
    /// </summary>
    internal object RenderPartialWithContext(
        Scriban.TemplateContext callerContext,
        string name,
        object? context,
        ScriptObject pageObject,
        Scriban.Parsing.SourceSpan callerSpan = default)
    {
        // 目标 partial 若读写页面 Store（partial 返回值通道 / 显式暂存），
        // 而 CONTEXT 又是**非脚本对象**（字符串等）时无法承载 store → 退回共享上下文
        //（Ananke/Stack 实测 "page.store.set / page.Store.get for a null object"）。
        // 该判定按 partial 源文本做确定性检查（含其 include 链上的名字）
        var path = _templateLoader.GetPath(callerContext, callerSpan, name) ?? "";
        var source = path.Length > 0 ? _templateLoader.Load(callerContext, default, path) : null;
        if (source is null)
        {
            return RenderPartialWithType(callerContext, name);
        }

        // Store 实例：取调用者页面对象上的实例（返回值通道就是它）。
        // 注意**只认 ContainsKey**：LazyPageObject 的 store 通过 TryGetValue 懒提供，
        // 用 Store 属性探测会让"凡是真实页面的 dict 上下文 partial"全部走合并分支——
        // 合并对象带着页面成员，主题对它做 reflect.IsMap/遍历时行为随之改变
        //（ananke 的 hook→filter 链实测：页面全空且无错误报出）
        // store 只在**目标 partial 真的用页面 store** 时注入显式上下文：
        // 否则传进来的 dict 会多出 store/scratch 成员，主题把它当数据遍历时
        // 会顺着 store 钻进 Flint 内部对象（内部投影对象是 ScriptObject，
        // 与"数据 map"在类型上无从区分）→ 递归不收敛。
        // FixIt 的 camel-case-keys.html 实测：递归转换 map 键时输入里出现
        // `{"a_b":1,"store":{…}}`，逐层下钻到 `{"to_html":…}` 类内部对象，
        // 200 层后触发 partial 深度守卫（转换率与产出双降）。
        // 判据复用既有的 UsesPageStore（按 partial 源文本 + include 链判定）
        var contextUsesStore = UsesPageStore(source);
        object? store = contextUsesStore && pageObject.ContainsKey("store") ? pageObject["store"] : null;

        if (context is not ScriptObject && UsesPageStore(source))
        {
            return RenderPartialWithType(callerContext, name);
        }

        // 上下文为字典/脚本对象时，把 store 合并进去：**两条通道共存**——
        // partial 内的 `.X` 指向传入上下文（Hugo dot 语义），`page.store`/`scratch`
        // 仍指向调用者的实例。此前是"目标用到 store 就整体退回共享上下文"，
        // 于是同时用两者的 partial 读不到传入的 dict：
        // techdoc 的 pagination.html（`partial "pagination.html" (dict "CurrentNode" …)`
        // 内既读 .CurrentNode 又写 .Scratch）实测 68 处
        // "Cannot get the member $currentNode.scratch for a null object"
        var effective = context;
        if (context is ScriptObject ctxObj)
        {
            if (store is null)
            {
                AddKeyCaseAliases(ctxObj);
                AddPageMethodFamily(ctxObj, pageObject);
                MergePageMembers(ctxObj, ctxObj);
            }
            else
            {
                var merged = new ScriptObject();
                foreach (var key in ctxObj.Keys)
                {
                    merged[key] = ctxObj[key];
                }
                // dict **自带** store/scratch 键时不注入调用者的 store：Hugo 语义下
                // `.Scratch` 指传入 dict 的那个键（hugo-book 的
                // `dict "Scratch" $scratch "Page" $page` 递归收集章节页），覆盖它会让
                // 写入落到调用者的 store 上、传出的集合恒空（调用点 `$pages.Next page`
                // 报 "The function `$pages.Next` was not found" 实测）。大小写一并判定——
                // 迁移产物用 `page.scratch`（小写）访问 `.Scratch`
                var dictOwnsStore = merged.Keys.Any(k =>
                    k.Equals("store", StringComparison.OrdinalIgnoreCase) ||
                    k.Equals("scratch", StringComparison.OrdinalIgnoreCase));
                if (!dictOwnsStore)
                {
                    merged["store"] = store;
                    merged["Store"] = store;
                    merged["scratch"] = store;
                    merged["Scratch"] = store;
                }
                AddKeyCaseAliases(merged);
                AddPageMethodFamily(merged, pageObject);
                MergePageMembers(merged, ctxObj);
                effective = merged;
            }
        }

        var overlay = new ScriptObject
        {
            ["page"] = effective,
            ["Page"] = effective,
            // 显式 dict 上下文只改 dot；`__page` 始终指向调用者的当前页
            //（Hugo：partial 内 `page` 是当前页，`.` 才是传入的 dict）
            ["__page"] = pageObject,
        };
        // 返回值通道的 store 必须随 overlay 一起可见：ResolveStore 读的是
        // CurrentGlobal（最顶层），overlay 一压就把下层 globals 里的
        // `__flint_ret_store` 挡住 → partial 里的 `__partial_ret_set` 写到 null、
        // 调用方 partialValue 读回空（FixIt 所有值返回型 partial 实测）
        if (ResolveStore(callerContext) is { } retStore)
        {
            overlay[RetStoreKey] = retStore;
        }
        callerContext.PushGlobal(overlay);
        try
        {
            return RenderPartialWithType(callerContext, name);
        }
        finally
        {
            callerContext.PopGlobal();
        }
    }

    /// <summary>partial 是否依赖页面 Store（返回值通道 / .Scratch 暂存）</summary>
    private static bool UsesPageStore(string source) =>
        source.Contains("page.store", StringComparison.Ordinal) ||
        source.Contains("page.Store", StringComparison.Ordinal) ||
        source.Contains("page.scratch", StringComparison.Ordinal) ||
        source.Contains("page.Scratch", StringComparison.Ordinal);

    /// <summary>
    /// 页面**方法族**键名（函数值成员）：把调用者页面的这些成员补给 partial 的
    /// dict 上下文（仅补 dict 未定义的键，dict 自己的键优先）。
    ///
    /// 场景：Hugo 惯用 `dict "Page" . "Key" "x" | partial "helper.html"`，
    /// partial 内写 `.Page.Param .Key`——迁移后是 `page.param page?.key`，
    /// 而 partial 的 `page` 是那个 dict（无 param 方法）→ function not found
    ///（FixIt `_partials/function/param.html` 实测）。补上方法族后该调用
    /// 落到调用者页面的 param 上，与 Hugo 语义一致（dict 的 Page 就是调用者页面）
    /// </summary>
    private static readonly string[] PageMethodKeys =
    [
        "param", "Param", "get_page", "GetPage", "get_terms", "GetTerms",
        "has_shortcode", "HasShortcode", "render_string", "RenderString",
        "paginate", "Paginate", "fragments", "Fragments",
        // 祖先判定：hugo-book 的 menu-filetree 以 dict 调用时按这些名字取用
        "is_ancestor", "IsAncestor", "is_descendant", "IsDescendant"
    ];

    /// <summary>
    /// 把 dict 里"装着页面"的键（Hugo 惯用 `dict "Page" . | partial "x"`）的页面成员
    /// 并入绑定对象（dict 自身键优先）。Hugo 语义下 `.Page.X` 取的是该页面的 X，
    /// 而迁移产物里的 `.Page.X` 被写成 `page.x`（page 即 dict 本身）→ 取不到页面成员。
    /// FixIt 的 get-cover.html（`$page := .Page` → `$page = page`）实测：
    /// `$page.resources.getmatch` 报 "for a null object"
    /// </summary>
    /// <summary>
    /// 给 partial 的 dict 上下文补**大小写别名**：主题常用 `dict "Config" …`
    /// 造 PascalCase 键，而迁移产物按页面成员约定写成小写（`page.config`）——
    /// Scriban 成员查找大小写敏感，缺别名时取到 null
    ///（FixIt 的 feed/rss.html `.Config.limit` 实测）
    /// </summary>
    private static void AddKeyCaseAliases(ScriptObject target)
    {
        foreach (var key in target.Keys.ToList())
        {
            if (key.Length == 0)
            {
                continue;
            }
            var lowerFirst = char.ToLowerInvariant(key[0]) + key[1..];
            var upperFirst = char.ToUpperInvariant(key[0]) + key[1..];
            if (lowerFirst != key && !target.ContainsKey(lowerFirst))
            {
                target[lowerFirst] = target[key];
            }
            if (upperFirst != key && !target.ContainsKey(upperFirst))
            {
                target[upperFirst] = target[key];
            }
        }
    }

    private static void MergePageMembers(ScriptObject target, ScriptObject source)
    {
        foreach (var key in new[] { "page", "Page" })
        {
            if (!source.ContainsKey(key) || source[key] is not ScriptObject pageObj ||
                !pageObj.ContainsKey("rel_permalink") || !pageObj.ContainsKey("title"))
            {
                continue;
            }
            foreach (var member in pageObj.Keys)
            {
                if (!target.ContainsKey(member))
                {
                    target[member] = pageObj[member];
                }
            }
        }
    }

    /// <summary>把调用者页面的方法族补给 dict 上下文（缺则补，dict 已有键不覆盖）</summary>
    private static void AddPageMethodFamily(ScriptObject target, ScriptObject? source)
    {
        if (source is null)
        {
            return;
        }
        foreach (var key in PageMethodKeys)
        {
            if (target.ContainsKey(key) || !source.ContainsKey(key))
            {
                continue;
            }
            target[key] = source[key];
        }
    }

    /// <summary>
    /// partialCached 的 variant 签名：页面对象用 permalink 区分（默认 ToString
    /// 只给类型名，会让所有页面共用一条缓存 → 首个页面的输出被复用）
    /// </summary>
    private static string VariantSignature(object? variant) => variant switch
    {
        null => "",
        Scriban.Runtime.ScriptObject o when o.ContainsKey("permalink") =>
            o["permalink"]?.ToString() ?? "",
        Scriban.Runtime.ScriptObject o when o.ContainsKey("rel_permalink") =>
            o["rel_permalink"]?.ToString() ?? "",
        _ => variant.ToString() ?? ""
    };

    /// <summary>渲染结果标量类型还原：布尔/数值从文本还原（partial 返回值的类型语义）</summary>
    private static object RestoreScalarType(string rendered)
    {
        var trimmed = rendered.Trim();
        if (trimmed.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (trimmed.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (long.TryParse(trimmed, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var l))
        {
            return l;
        }
        if (double.TryParse(trimmed, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var d))
        {
            return d;
        }
        return rendered;
    }

    /// <summary>
    /// partial 函数（返回值语义）的 Scriban 包装
    /// </summary>
    private sealed class PartialFunction(ScribanTemplateRenderer renderer, ScriptObject pageObject)
        : Scriban.Runtime.IScriptCustomFunction
    {
        public object? Invoke(
            Scriban.TemplateContext context,
            Scriban.Syntax.ScriptNode? callerContext,
            Scriban.Runtime.ScriptArray arguments,
            Scriban.Syntax.ScriptBlockStatement? blockStatement)
        {
            if (arguments.Count < 1 || arguments[0] is not string name)
            {
                throw new InvalidOperationException("partial 需要至少一个字符串参数（partial 名称）");
            }
            // 调用者的源码位置：partial 名可相对调用者目录解析（见 RenderPartialWithType）
            var callerSpan = callerContext?.Span ?? default;
            // Hugo 的 `partial "x" CONTEXT`：第二参数成为 partial 内的 `.`。
            // 迁移产物的裸 `.X` 被转成 `page.x`，故把 context 临时压成 `page` 全局
            if (arguments.Count > 1)
            {
                return renderer.RenderPartialWithContext(context, name, arguments[1], pageObject, callerSpan);
            }
            return renderer.RenderPartialWithType(context, name, callerSpan);
        }

        public System.Threading.Tasks.ValueTask<object?> InvokeAsync(
            Scriban.TemplateContext context,
            Scriban.Syntax.ScriptNode? callerContext,
            Scriban.Runtime.ScriptArray arguments,
            Scriban.Syntax.ScriptBlockStatement? blockStatement)
        {
            return new System.Threading.Tasks.ValueTask<object?>(
                Invoke(context, callerContext, arguments, blockStatement));
        }

        public int RequiredParameterCount => 1;

        public int ParameterCount => 2;

        public Scriban.Runtime.ScriptVarParamKind VarParamKind =>
            Scriban.Runtime.ScriptVarParamKind.Direct;

        public Type ReturnType => typeof(object);

        public Scriban.Runtime.ScriptParameterInfo GetParameterInfo(int index) =>
            index == 0
                ? new Scriban.Runtime.ScriptParameterInfo(typeof(string), "name")
                : new Scriban.Runtime.ScriptParameterInfo(typeof(object), "context");

        public Scriban.Runtime.ScriptParameterInfo ReturnParameterInfo =>
            new Scriban.Runtime.ScriptParameterInfo(typeof(object), "result");
    }

    /// <summary>
    /// partialValue 的 Scriban 函数包装：Hugo partial 返回值语义。
    ///
    /// 机制（实测验证）：partial 内的 `{{ return X }}` 由转换器改写为
    /// `{{ page.store.set "__partial_ret_NAME" X }}{{ ret }}`；本函数渲染 partial
    /// （共享调用者上下文，故 Store 可见）后从 Store 取回 X —— 真实对象，
    /// 无序列化往返。实测验证：跨 include 传递的对象成员访问与类型均保真。
    ///
    /// 兜底：Store 中无对应键时回退到标量还原（兼容无 return 的 partial）。
    /// </summary>
    /// <summary>
    /// hook 上下文的页面方法占位：返回 null（Hugo 的 render hook 里 .Page 只有
    /// 最小字段集，主题调用站点级方法时应得到空值并走 with 兜底，而非构建失败）
    /// </summary>
    private sealed class StubPageMethodFunction(object? returnValue = null) : Scriban.Runtime.IScriptCustomFunction
    {
        public object? Invoke(Scriban.TemplateContext context, Scriban.Syntax.ScriptNode? callerContext,
            Scriban.Runtime.ScriptArray arguments, Scriban.Syntax.ScriptBlockStatement? blockStatement) => returnValue;

        public ValueTask<object?> InvokeAsync(Scriban.TemplateContext context,
            Scriban.Syntax.ScriptNode? callerContext, Scriban.Runtime.ScriptArray arguments,
            Scriban.Syntax.ScriptBlockStatement? blockStatement) =>
            new(Invoke(context, callerContext, arguments, blockStatement));

        public int RequiredParameterCount => 0;
        public int ParameterCount => 3;
        public Scriban.Runtime.ScriptVarParamKind VarParamKind =>
            Scriban.Runtime.ScriptVarParamKind.Direct;
        public Type ReturnType => typeof(object);
        public Scriban.Runtime.ScriptParameterInfo GetParameterInfo(int index) =>
            new(typeof(object), "arg" + index);
        public Scriban.Runtime.ScriptParameterInfo ReturnParameterInfo =>
            new(typeof(object), "value");
    }

    /// <summary>
    /// templates.Exists（Hugo）：模板是否可解析。此前实现是 `!string.IsNullOrEmpty(name)`
    /// ——**恒真**，使主题的 `{{ if templates.Exists "partials/favicons.html" }}` 守卫失效，
    /// 径直调用不存在的 partial 并在 include 处报 FileNotFound（Blowfish 1574 处实测）。
    /// 此处接 FileTemplateLoader，用与 include 相同的解析规则判断
    /// </summary>
    private sealed class TemplateExistsFunction(FileTemplateLoader loader)
        : Scriban.Runtime.IScriptCustomFunction
    {
        public object? Invoke(Scriban.TemplateContext context, Scriban.Syntax.ScriptNode? callerContext,
            Scriban.Runtime.ScriptArray arguments, Scriban.Syntax.ScriptBlockStatement? blockStatement)
        {
            if (arguments.Count == 0)
            {
                return false;
            }
            var name = arguments[0]?.ToString() ?? "";
            if (name.Length == 0)
            {
                return false;
            }
            try
            {
                var path = loader.GetPath(context, default, name);
                // GetPath 未命中时返回"主根 + 名字"的兜底形态（非真实文件）
                return File.Exists(path);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return false;
            }
        }

        public ValueTask<object?> InvokeAsync(Scriban.TemplateContext context,
            Scriban.Syntax.ScriptNode? callerContext, Scriban.Runtime.ScriptArray arguments,
            Scriban.Syntax.ScriptBlockStatement? blockStatement) =>
            new(Invoke(context, callerContext, arguments, blockStatement));

        public int RequiredParameterCount => 1;
        public int ParameterCount => 1;
        public Scriban.Runtime.ScriptVarParamKind VarParamKind =>
            Scriban.Runtime.ScriptVarParamKind.Direct;
        public Type ReturnType => typeof(bool);
        public Scriban.Runtime.ScriptParameterInfo GetParameterInfo(int index) =>
            new(typeof(string), "name");
        public Scriban.Runtime.ScriptParameterInfo ReturnParameterInfo =>
            new(typeof(bool), "exists");
    }

    /// <summary>
    /// 渲染期返回值 store 的**键名**（挂在渲染上下文 globals 上，每页独立）。
    /// 用它而非 page.Store 的原因：返回值通道必须在**任意渲染上下文**可用——
    /// 短代码/hook/被覆盖 page 的 partial 里 `page` 可能为 null（Blowfish 实测
    /// 1571 处 "Cannot get the member page.store.set for a null object"）
    /// </summary>
    internal const string RetStoreKey = "__flint_ret_store";

    /// <summary>
    /// 返回值通道写入：`__partial_ret_set "name" value`。
    /// 从当前渲染上下文取 store（每页一个）——不依赖 page
    /// </summary>
    private sealed class PartialRetSetFunction : Scriban.Runtime.IScriptCustomFunction
    {
        public object? Invoke(Scriban.TemplateContext context, Scriban.Syntax.ScriptNode? callerContext,
            Scriban.Runtime.ScriptArray arguments, Scriban.Syntax.ScriptBlockStatement? blockStatement)
        {
            if (arguments.Count < 1)
            {
                return "";
            }
            var name = arguments[0]?.ToString() ?? "";
            var value = arguments.Count > 1 ? arguments[1] : null;
            var store = ResolveStore(context);
            // 与读取端同规则归一（含剥掉可能已带的前缀）——见 CanonicalPartialKey
            var canonicalKey = PartialValueFunction.KeyPrefix + PartialValueFunction.CanonicalPartialKey(name);
            store?.Set(canonicalKey, value);
            // 记录到当前 partial 层：文本通道 `{{ partial "x" . }}` 的语义是**输出**
            // 返回值（Hugo 的 partial 语句），RenderPartialWithType 渲染后据此补输出
            RecordRetKey(canonicalKey);
            return "";
        }

        public ValueTask<object?> InvokeAsync(Scriban.TemplateContext context,
            Scriban.Syntax.ScriptNode? callerContext, Scriban.Runtime.ScriptArray arguments,
            Scriban.Syntax.ScriptBlockStatement? blockStatement) =>
            new(Invoke(context, callerContext, arguments, blockStatement));

        public int RequiredParameterCount => 1;
        public int ParameterCount => 2;
        public Scriban.Runtime.ScriptVarParamKind VarParamKind =>
            Scriban.Runtime.ScriptVarParamKind.Direct;
        public Type ReturnType => typeof(string);
        public Scriban.Runtime.ScriptParameterInfo GetParameterInfo(int index) =>
            new(index == 0 ? typeof(string) : typeof(object), index == 0 ? "name" : "value");
        public Scriban.Runtime.ScriptParameterInfo ReturnParameterInfo =>
            new(typeof(string), "empty");
    }

    /// <summary>从渲染上下文取返回值 store（未装配时返回 null）</summary>
    internal static PageStoreObject? ResolveStore(Scriban.TemplateContext context)
    {
        // CurrentGlobal 是 IScriptObject 接口（无 ContainsKey/索引器）→ 用 TryGetValue
        if (context.CurrentGlobal is { } globals &&
            globals.TryGetValue(context, default, RetStoreKey, out var value) &&
            value is PageStoreObject store)
        {
            return store;
        }
        return null;
    }

    private sealed class PartialValueFunction(ScribanTemplateRenderer renderer, ScriptObject pageObject)
        : Scriban.Runtime.IScriptCustomFunction
    {
        /// <summary>Store 键前缀（避免与用户键冲突）</summary>
        internal const string KeyPrefix = "__partial_ret_";

        public object? Invoke(
            Scriban.TemplateContext context,
            Scriban.Syntax.ScriptNode? callerContext,
            Scriban.Runtime.ScriptArray arguments,
            Scriban.Syntax.ScriptBlockStatement? blockStatement)
        {
            if (arguments.Count < 1 || arguments[0] is not string name)
            {
                throw new InvalidOperationException("partialValue 需要至少一个字符串参数（partial 名称）");
            }

            // 优先渲染期 store（任意上下文可用）；回退 page.Store（兼容旧产物）
            PageStoreObject? store = ResolveStore(context) ?? (pageObject as LazyPageObject)?.Store;
            if (store is null)
            {
                return renderer.RenderPartialWithType(context, name);
            }

            // 键规范化：转换器用规范名（无扩展名、无路径前缀）写 Store，
            // 而调用方可能传 "func/X.html" —— 必须同规则归一，否则键不匹配
            // （实测：CALL 返回空，因写入键为 __partial_ret_func/X 而读取键为
            //   __partial_ret_func/X.html）
            var callerSpan = callerContext?.Span ?? default;
            var canonical = CanonicalPartialKey(name);
            var key = KeyPrefix + canonical;
            // 清除上次残留（同一 partial 多次调用时避免读到旧值）
            store.Delete(key);

            // 渲染 partial：其副作用（返回值通道写入渲染上下文 Store）被下方读取。
            // 输出缓冲隔离（关键）：`$x := partial "y"` 在 Hugo 里**只取值不输出文本**，
            // 而 Scriban 的嵌套渲染会把 partial 文本追加/回卷到调用者的输出流——
            // 实测不隔离时调用者已累积的文本丢失、返回值退化为整段渲染文本
            //（FixIt 的 `partialValue "function/camel-case" $key` 取到 "}}}" 之类的碎文本）。
            // 第二参数为 partial 内的 `.`（Hugo dot 语义）——返回值型 partial 同样要传：
            // `partial "function/camel-case-keys.html" $value` 不传会把外层 page 当成 `.`
            object? rendered;
            context.PushOutput();
            try
            {
                rendered = RenderValue();
            }
            finally
            {
                context.PopOutput();
            }

            var value = store.Get(key);
            store.Delete(key);

            // 兜底：partial 无 return（无 store.set）时，回退隔离缓冲里的渲染文本
            return value ?? rendered;

            object? RenderValue() => arguments.Count > 1
                ? renderer.RenderPartialWithContext(context, name, arguments[1], pageObject, callerSpan)
                : renderer.RenderPartialWithType(context, name, callerSpan);
        }

        public System.Threading.Tasks.ValueTask<object?> InvokeAsync(
            Scriban.TemplateContext context,
            Scriban.Syntax.ScriptNode? callerContext,
            Scriban.Runtime.ScriptArray arguments,
            Scriban.Syntax.ScriptBlockStatement? blockStatement)
        {
            return new System.Threading.Tasks.ValueTask<object?>(
                Invoke(context, callerContext, arguments, blockStatement));
        }

        public int RequiredParameterCount => 1;
        public int ParameterCount => 2;
        public Scriban.Runtime.ScriptVarParamKind VarParamKind =>
            Scriban.Runtime.ScriptVarParamKind.Direct;
        public Type ReturnType => typeof(object);
        public Scriban.Runtime.ScriptParameterInfo GetParameterInfo(int index) =>
            index == 0
                ? new Scriban.Runtime.ScriptParameterInfo(typeof(string), "name")
                : new Scriban.Runtime.ScriptParameterInfo(typeof(object), "context");
        public Scriban.Runtime.ScriptParameterInfo ReturnParameterInfo =>
            new(typeof(object), "value");

        /// <summary>
        /// partial 名规范化（与迁移工具 ScribanConverter.CanonicalPartialName 同规则）：
        /// 剥扩展名与路径前缀，使写入键与读取键一致
        /// </summary>
        internal static string CanonicalPartialKey(string raw)
        {
            var n = raw.Replace((char)92, '/').Trim();
            if (n.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            {
                n = n[..^5];
            }
            // 写入端（转换器产出）传的是**已带前缀**的键
            //（`__partial_ret_set "__partial_ret_func/x"`），读取端传 partial 名
            //（`partialValue "_partials/func/x"`）——两端都先剥掉前缀再统一加回，
            // 否则写入为双前缀而读取为单前缀，返回值通道恒读不到值
            //（FixIt 的 camel-case 等所有值返回型 partial 实测）
            if (n.StartsWith(KeyPrefix, StringComparison.Ordinal))
            {
                n = n[KeyPrefix.Length..];
            }
            foreach (var prefix in new[]
                     {
                         "layouts/_partials/", "layouts/partials/", "_partials/", "partials/", "/"
                     })
            {
                if (n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    n = n[prefix.Length..];
                }
            }
            return n;
        }
    }

    /// <summary>
    /// partialCached 的 Scriban 函数包装：实现 IScriptCustomFunction 拿到
    /// 调用者 TemplateContext（不经反射绑定，AOT 安全）
    /// </summary>
    private sealed class PartialCachedFunction(ScribanTemplateRenderer renderer)
        : Scriban.Runtime.IScriptCustomFunction
    {
        public object? Invoke(
            Scriban.TemplateContext context,
            Scriban.Syntax.ScriptNode? callerContext,
            Scriban.Runtime.ScriptArray arguments,
            Scriban.Syntax.ScriptBlockStatement? blockStatement)
        {
            if (arguments.Count < 1 || arguments[0] is not string name)
            {
                throw new InvalidOperationException("partialcached 需要至少一个字符串参数（partial 名称）");
            }

            var variants = new object[arguments.Count - 1];
            for (var i = 1; i < arguments.Count; i++)
            {
                variants[i - 1] = arguments[i] ?? string.Empty;
            }

            return renderer.RenderPartialCached(context, name, variants);
        }

        public System.Threading.Tasks.ValueTask<object?> InvokeAsync(
            Scriban.TemplateContext context,
            Scriban.Syntax.ScriptNode? callerContext,
            Scriban.Runtime.ScriptArray arguments,
            Scriban.Syntax.ScriptBlockStatement? blockStatement)
        {
            return new System.Threading.Tasks.ValueTask<object?>(
                Invoke(context, callerContext, arguments, blockStatement));
        }

        public int RequiredParameterCount => 1;

        public int ParameterCount => 1; // name 必填；variants 为变长尾参

        public Scriban.Runtime.ScriptVarParamKind VarParamKind =>
            Scriban.Runtime.ScriptVarParamKind.Direct;

        public Type ReturnType => typeof(object);

        public Scriban.Runtime.ScriptParameterInfo GetParameterInfo(int index) =>
            index == 0
                ? new Scriban.Runtime.ScriptParameterInfo(typeof(string), "name")
                : new Scriban.Runtime.ScriptParameterInfo(typeof(object), "variant");
    }

    private async ValueTask<Template> GetOrLoadTemplateAsync(
        string templateName,
        CancellationToken cancellationToken)
    {
        if (_templateCache.TryGetValue(templateName, out var cached) && !IsStale(cached))
        {
            return cached.Template;
        }

        string templatePath;
        string content;
        DateTime mtimeUtc;
        try
        {
            templatePath = ResolveTemplatePath(templateName);
            // 读前取 mtime、读后复核：读期间被写会产出"旧内容+新 mtime"的缓存条目，
            // 使 stale 检测永久失效（热重载一直渲染旧模板）——有界重读直至前后一致；
            // 3 轮后仍不一致（文件被持续写）接受最新读，下次 stale 检测会自愈
            for (var attempt = 0; ; attempt++)
            {
                var mtimeBefore = File.GetLastWriteTimeUtc(templatePath);
                content = await File.ReadAllTextAsync(templatePath, cancellationToken);
                mtimeUtc = File.GetLastWriteTimeUtc(templatePath);
                if (mtimeUtc == mtimeBefore || attempt >= 2)
                {
                    break;
                }
            }
        }
        catch (TemplateNotFoundException) when (BuiltinTemplates.ContainsKey(templateName))
        {
            // 内置回退模板（对齐 Hugo embedded templates）：最小站点缺 taxonomy/term/list
            // 等布局时以极简内置模板兜底，而非构建报错
            content = BuiltinTemplates[templateName];
            var template = Template.Parse(content, $"builtin:{templateName}");
            _templateCache[templateName] = new CachedTemplate(template, null, DateTime.MinValue);
            return template;
        }

        var parsed = Template.Parse(content, templatePath);
        if (parsed.HasErrors)
        {
            throw new TemplateParseException(
                templateName,
                parsed.Messages.Select(m => m.ToString()).ToList());
        }

        _templateCache[templateName] = new CachedTemplate(parsed, templatePath, mtimeUtc);
        // 模板文件被重载（mtime stale 或首次载入）说明磁盘模板可能已变化，
        // partialCached 结果缓存必须失效——否则 Serve 长驻渲染下改 partial 源文件
        // 后页面仍渲染旧输出（缓存条目按渲染器生命周期存活，无 mtime 检测）
        _partialResultCache.Clear();
        return parsed;
    }

    private Template GetOrLoadTemplate(string templateName)
    {
        if (_templateCache.TryGetValue(templateName, out var cached) && !IsStale(cached))
        {
            return cached.Template;
        }

        string templatePath;
        string content;
        DateTime mtimeUtc;
        try
        {
            templatePath = ResolveTemplatePath(templateName);
            // 同步版：同上，有界重读直至 mtime 前后一致
            for (var attempt = 0; ; attempt++)
            {
                var mtimeBefore = File.GetLastWriteTimeUtc(templatePath);
                content = File.ReadAllText(templatePath);
                mtimeUtc = File.GetLastWriteTimeUtc(templatePath);
                if (mtimeUtc == mtimeBefore || attempt >= 2)
                {
                    break;
                }
            }
        }
        catch (TemplateNotFoundException) when (BuiltinTemplates.ContainsKey(templateName))
        {
            content = BuiltinTemplates[templateName];
            var template = Template.Parse(content, $"builtin:{templateName}");
            _templateCache[templateName] = new CachedTemplate(template, null, DateTime.MinValue);
            return template;
        }

        var parsed = Template.Parse(content, templatePath);
        if (parsed.HasErrors)
        {
            throw new TemplateParseException(
                templateName,
                parsed.Messages.Select(m => m.ToString()).ToList());
        }

        _templateCache[templateName] = new CachedTemplate(parsed, templatePath, mtimeUtc);
        // 同步重载路径：同上，partialCached 结果缓存随模板重载失效
        _partialResultCache.Clear();
        return parsed;
    }

    /// <summary>
    /// 模板 mtime 查询的短窗缓存：同一路径 TTL 内不重复 stat（NTFS 元数据查询
    /// 每次 20-50μs，每页渲染都查 IsStale 会在大站点放大为几十 ms 的纯文件系统
    /// 等待）。TTL 内的模板修改最多延迟一个窗口被发现——远小于任何真实写入的
    /// 感知间隔，dev server 热重载不受影响
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime CachedAtUtc, DateTime MtimeUtc)> _mtimeCache = new();
    private static readonly TimeSpan MtimeCacheTtl = TimeSpan.FromMilliseconds(50);
    private readonly TimeProvider _timeProvider;

    private DateTime GetMtimeUtc(string path)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (_mtimeCache.TryGetValue(path, out var entry) && now - entry.CachedAtUtc < MtimeCacheTtl)
        {
            return entry.MtimeUtc;
        }
        var mtime = File.GetLastWriteTimeUtc(path);
        _mtimeCache[path] = (now, mtime);
        return mtime;
    }

    /// <summary>
    /// 清空 mtime 短窗缓存：由构建入口（BuildAsync/IncrementalBuildAsync）调用，
    /// 保证"每次构建都能看到最新模板"契约不被 TTL 延迟破坏——缓存只在
    /// 单次构建内部生效（构建内多页渲染共享首轮 stat）。
    /// 同步失效模板查找器的描述符缓存（构建内模板文件可能增删）
    /// </summary>
    public void InvalidateMtimeCache()
    {
        _mtimeCache.Clear();
        _lookup?.Invalidate();
    }

    /// <summary>
    /// 缓存条目是否过期：源文件 mtime 与编译时不一致说明模板已被修改
    /// （Serve 长驻渲染 / 增量构建时模板变化必须刷新，否则渲染的仍是旧模板）
    /// </summary>
    private bool IsStale(CachedTemplate cached)
    {
        return cached.SourcePath is not null &&
               GetMtimeUtc(cached.SourcePath) != cached.ModifiedTimeUtc;
    }

    /// <summary>
    /// 内置回退模板（对齐 Hugo embedded templates 的最小集）：
    /// 最小站点缺 taxonomy/term/list 布局时的极简兜底，避免构建报错；
    /// pagination 为 Hugo 内置分页导航模板的 Scriban 等价（主题 include "pagination" 时命中）
    /// </summary>
    internal static readonly Dictionary<string, string> BuiltinTemplates =
        new(StringComparer.Ordinal)
        {
            ["taxonomy"] = "<ul>{{ for p in site.regular_pages }}<li><a href=\"{{ p.permalink }}\">{{ p.title }}</a></li>{{ end }}</ul>",
            // term 用 pages（词条页面集合，renderer 对未设置 pages 的页面回落全站 regular_pages）
            ["term"] = "<ul>{{ for p in pages }}<li><a href=\"{{ p.permalink }}\">{{ p.title }}</a></li>{{ end }}</ul>",
            ["list"] = "<ul>{{ for p in pages }}<li><a href=\"{{ p.permalink }}\">{{ p.title }}</a></li>{{ end }}</ul>",
            // Hugo 内置 pagination 模板（default 格式）的 Scriban 等价：总分页数 > 1 时
            // 输出首页/上一页/页码槽/下一页/末页。Pager 字段由 PaginatorView 对象提供
            ["pagination"] = BuildPaginationTemplate(),

            // ---- Hugo embedded partials（主题不提供、由 Hugo 内置；主题直接 include）----
            // 最小等价实现：输出合法 HTML 的元信息标签，主题可覆盖同名 partial。
            // site.menus.main 等缺失时为循环空转，不报错
            ["opengraph"] = BuildOpenGraphTemplate(),
            ["schema"] = BuildSchemaTemplate(),
            ["twitter_cards"] = BuildTwitterCardsTemplate(),
            ["google_analytics"] = "",
            // Hugo 内置的图片切片 partial（FixIt 的 twitter-cards 用它取首图）：
            // 从 front matter `images` 构造，写入**值通道**并 ret——用 text 通道的调用点
            // 得到空串（`index "" 0` 为 nil，与"没有图片"等价），用 partialValue 的调用点
            // 拿到切片
            ["_funcs/get-page-images"] = BuildPageImagesTemplate(),
            ["disqus"] = ""
        };

    /// <summary>
    /// Hugo 内置 opengraph.html 的 Scriban 等价：输出 og:* / article:* 元信息。
    /// 逐项对齐 Hugo 内部模板（v0.166 实测产物）：
    /// og:url → og:site_name → og:title → og:description → **og:locale** → og:type，
    /// 常规页再补 article:published_time/modified_time/section/tag。
    /// **空值不输出标签**（此前无条件输出 og:description，Hugo 在描述为空时整条省略
    /// ——跨主题审计实测 96 页多出该标签）
    /// </summary>
    private static string BuildOpenGraphTemplate() =>
        """
        <meta property="og:url" content="{{ page.permalink }}" />
        {{ if site.title }}<meta property="og:site_name" content="{{ site.title }}" />{{ end }}
        {{ if page.title }}<meta property="og:title" content="{{ page.title }}" />{{ end }}
        {{ $__desc = page.description | default page.summary | default site?.params?.description }}{{ if $__desc }}<meta property="og:description" content="{{ $__desc }}" />{{ end }}
        {{ $__locale = page.params?.locale | default site?.language }}{{ if $__locale }}<meta property="og:locale" content="{{ $__locale }}" />{{ end }}
        <meta property="og:type" content="{{ if is_home }}website{{ else }}article{{ end }}" />
        {{ if is_single }}{{ if page.date }}<meta property="article:published_time" content="{{ date.to_string page.date "yyyy-MM-ddTHH:mm:sszzz" }}" />{{ end }}
        {{ if page.lastmod }}<meta property="article:modified_time" content="{{ date.to_string page.lastmod "yyyy-MM-ddTHH:mm:sszzz" }}" />{{ end }}
        {{ if page.section }}<meta property="article:section" content="{{ page.section }}" />{{ end }}
        {{ for $__t in page.tags }}<meta property="article:tag" content="{{ $__t }}" />{{ end }}{{ end }}
        """;

    /// <summary>Hugo 内置 schema.html 的 Scriban 等价（探针 v0.166：Hugo 输出的是
    /// **itemprop meta**，不是 JSON-LD；hugo-book 的 docs/html-head 实测——
    /// name/description/publishDate/lastmod/wordCount，日期布局 -07:00 → +00:00）</summary>
    private static string BuildSchemaTemplate() =>
        """
        <meta itemprop="name" content="{{ page.title }}">
        <meta itemprop="description" content="{{ page.description | default page.summary | default site?.params?.description }}">
        {{ if page.publish_date }}<meta itemprop="datePublished" content="{{ date.to_string page.publish_date "yyyy-MM-ddTHH:mm:sszzz" }}">{{ end }}
        {{ if page.lastmod }}<meta itemprop="dateModified" content="{{ date.to_string page.lastmod "yyyy-MM-ddTHH:mm:sszzz" }}">{{ end }}
        {{ if page.word_count }}<meta itemprop="wordCount" content="{{ page.word_count }}">{{ end }}
        """;

    /// <summary>Hugo 内置 twitter_cards.html 的 Scriban 等价：输出 twitter:* 元信息</summary>
    private static string BuildTwitterCardsTemplate() =>
        """
        {{ if page.title }}<meta name="twitter:title" content="{{ page.title }}" />{{ end }}
        {{ $__tdesc = page.description | default page.summary }}{{ if $__tdesc }}<meta name="twitter:description" content="{{ $__tdesc }}" />{{ end }}
        <meta name="twitter:card" content="summary_large_image" />
        """;

    /// <summary>
    /// Hugo embedded pagination（default 格式）的 Scriban 翻译。
    /// **读 `page.paginator` 而不是全局 `paginator`**（Hugo 的内部模板用的就是页面的
    /// `.Paginator`）：全局 `paginator` 在渲染开始前绑定（那时模板还没调 `.Paginate`），
    /// 会拿到"预绑定的隐式分页器"——与模板实际分页的集合可能不同（clarity 的首页：
    /// 模板传 `where … mainSections` 的 3 条 → 2 页；隐式是站点全部常规页 5 条 → 3 页
    /// → 内置模板渲染出指向未产出页的 /page/3/ 链接）
    /// 5 槽页码窗口居中于当前页，首末页与前后页按条件渲染。
    /// 原始字符串字面量（内含大量 {{ }} 与引号，逐字可读优于拼接/转义）
    /// </summary>
    /// <summary>
    /// Hugo 内置 <c>_funcs/get-page-images</c> 的最小等价：把 front matter
    /// <c>images</c> 映射成图片对象列表（Permalink/RelPermalink），写入值通道
    /// </summary>
    private static string BuildPageImagesTemplate()
    {
        return """
{{- $images = [] -}}
{{- $front = page?.params?.images -}}
{{- if $front -}}
{{- for $img in as_list $front -}}
{{- $images = $images | append (dict "Permalink" (abs_url $img) "RelPermalink" $img) -}}
{{- end -}}
{{- end -}}
{{- __partial_ret_set "__partial_ret__funcs/get-page-images" $images }}{{ ret -}}
""";
    }

    private static string BuildPaginationTemplate()
    {
        return """
        {{ if page.paginator && page.paginator.total_pages > 1 }}
        <ul class="pagination pagination-default">
        {{ if page.paginator.first && page.paginator.page_number != page.paginator.first.page_number }}<li class="page-item"><a href="{{ page.paginator.first.url }}" aria-label="First" class="page-link" role="button"><span aria-hidden="true">&laquo;&laquo;</span></a></li>{{ else }}<li class="page-item disabled"><a aria-disabled="true" aria-label="First" class="page-link" role="button" tabindex="-1"><span aria-hidden="true">&laquo;&laquo;</span></a></li>{{ end }}
        {{ if page.paginator.prev }}<li class="page-item"><a href="{{ page.paginator.prev.url }}" aria-label="Previous" class="page-link" role="button"><span aria-hidden="true">&laquo;</span></a></li>{{ else }}<li class="page-item disabled"><a aria-disabled="true" aria-label="Previous" class="page-link" role="button" tabindex="-1"><span aria-hidden="true">&laquo;</span></a></li>{{ end }}
        {{ slots = 5 }}{{ start = page.paginator.page_number - 2 }}{{ if start < 1 }}{{ start = 1 }}{{ end }}{{ finish = start + slots - 1 }}{{ if finish > page.paginator.total_pages }}{{ finish = page.paginator.total_pages }}{{ end }}{{ if finish - start + 1 < slots }}{{ start = finish - slots + 1 }}{{ if start < 1 }}{{ start = 1 }}{{ end }}{{ end }}
        {{ for k in start..finish }}{{ if page.paginator.page_number == k }}<li class="page-item active"><a aria-current="page" aria-label="Page {{ k }}" class="page-link" role="button">{{ k }}</a></li>{{ else }}<li class="page-item"><a href="{{ page.paginator.pagers[k - 1].url }}" aria-label="Page {{ k }}" class="page-link" role="button">{{ k }}</a></li>{{ end }}{{ end }}
        {{ if page.paginator.next }}<li class="page-item"><a href="{{ page.paginator.next.url }}" aria-label="Next" class="page-link" role="button"><span aria-hidden="true">&raquo;</span></a></li>{{ else }}<li class="page-item disabled"><a aria-disabled="true" aria-label="Next" class="page-link" role="button" tabindex="-1"><span aria-hidden="true">&raquo;</span></a></li>{{ end }}
        {{ if page.paginator.last && page.paginator.page_number != page.paginator.last.page_number }}<li class="page-item"><a href="{{ page.paginator.last.url }}" aria-label="Last" class="page-link" role="button"><span aria-hidden="true">&raquo;&raquo;</span></a></li>{{ else }}<li class="page-item disabled"><a aria-disabled="true" aria-label="Last" class="page-link" role="button" tabindex="-1"><span aria-hidden="true">&raquo;&raquo;</span></a></li>{{ end }}
        </ul>
        {{ end }}
        """;
    }
    // 加权匹配查找器（方案五接线）：替换原 12 形态文件名直查——
    // 描述符集经快照+一致性测试守护，扫描结果按构建缓存（Invalidate 失效）
    private TemplateLookup? _lookup;

    private string ResolveTemplatePath(string templateName)
    {
        var lookup = _lookup ??= new TemplateLookup(_templatesPath, _themeTemplatePaths);
        var resolved = lookup.Resolve(templateName, _templatesPath);
        if (resolved is not null)
        {
            return resolved;
        }

        throw new TemplateNotFoundException(templateName,
            [Path.Combine(_templatesPath, templateName), Path.Combine(_templatesPath, templateName + ".html")]);
    }


    // 缓存内置函数对象（线程安全，只创建一次）
    private ScriptObject? _cachedBuiltinObject;
    private ScriptObject? _cachedDateObject;

    private static readonly IReadOnlyDictionary<string, string> EmptyTranslations =
        new Dictionary<string, string>();

    /// <summary>
    /// 站点翻译表快照（render hook 内的 i18n 用）。站点上下文装配后由
    /// SiteBuilder 设置一次；hook 渲染早于装配时为空表（查不到返回空串）
    /// </summary>
    internal IReadOnlyDictionary<string, string>? Translations { get; set; }

    /// <summary>
    /// 把内置函数对象与日期对象压入上下文。页面渲染（CreateScribanContext）与
    /// render hook 渲染（RenderHookTemplate）共用——hook 模板同样需要 safe_html、
    /// date.to_string 等函数，早期只在页面路径注册导致 `_markup/render-*.html`
    /// 报 "The function `safe_html` was not found"（stack 主题实测）。
    /// </summary>
    private void EnsureFunctionObjects(Scriban.TemplateContext scribanContext)
    {
        // 优化：复用内置函数对象（只创建一次）
        if (_cachedBuiltinObject == null)
        {
            var builtinObject = new ScriptObject();
            _builtinFunctions.RegisterFunctions(builtinObject);
            // partialCached（对齐 Hugo）：IScriptCustomFunction 实现，不经反射绑定（AOT 安全）
            builtinObject.TrySetValue(scribanContext, default, "partialcached",
                new PartialCachedFunction(this), readOnly: true);
            _cachedBuiltinObject = builtinObject;
        }
        scribanContext.PushGlobal(_cachedBuiltinObject);

        // 优化：复用日期对象（只创建一次）
        if (_cachedDateObject == null)
        {
            var dateObject = new ScriptObject();
            RegisterDateObject(dateObject);
            _cachedDateObject = new ScriptObject { ["date"] = dateObject };
        }
        scribanContext.PushGlobal(_cachedDateObject);
    }

    private Scriban.TemplateContext CreateScribanContext(FlintTemplateContext context)
    {
        // 安全契约决策（见 README「HTML 转义契约」）：Scriban 默认不启用
        // HTML 自动转义——Flint 保持该默认，模板输出不做上下文转义矩阵，
        // 内容可信性由内容管线与模板作者负责（对齐 Hugo safe* 恒等语义）
        var scribanContext = new FlintScribanContext
        {
            TemplateLoader = _templateLoader,
            MemberRenamer = member => member.Name, // 保持原始属性名
            StrictVariables = false, // 允许访问未定义的变量
            // Scriban 的函数递归计数在"嵌套渲染 + ret 提前返回"时不递减，
            // 会**跨调用累积**：partial 调用上百次的主题（hugo-book 每页调
            // docs/title.html / icon.html 等）会假性超限（opengraph 的
            // "Exceeding number of recursive depth limit 100 for node: default site.title"）。
            // 与 LoopLimit 同口径关掉引擎侧计数，递归安全由 partial 渲染的
            // 自有深度守卫负责
            RecursiveLimit = 0,
            LoopLimit = 1_000_000 // 万页站点的大列表循环（同数据集对比测试实证）
        };


        // 渲染期依赖收集（T4.1）：按页面初始化依赖快照容器
        RenderDependencyTracker.Initialize(scribanContext, context.Page.SourcePath);

        EnsureFunctionObjects(scribanContext);

        // 创建页面对象
        // 注入站点常规页集合（.RegularPages 在任意页面可用）+ 分页配置
        // （.Paginate 页面方法需要 pageSize/paginatePath）
        var pageObject = CreatePageObject(
            context.Page,
            context.Site.RegularPages,
            context.Site.Config.Paginate,
            context.Site.Config.PaginatePath,
            context.Site.Taxonomies.Taxonomies,
            // .GetPage 需要全量页面（含 section）——常规页集合里没有章节页
            context.Site.Pages);

        // 创建站点对象
        var siteObject = CreateSiteObject(context.Site);

        // 设置全局变量
        var globals = new ScriptObject
        {
            ["page"] = pageObject,
            // 全局当前页（与 dot 解耦）：Hugo 的 `page` 在 partial 以 dict 调用时
            // 仍是当前页，而 `.` 是 dict。迁移产物把源码 `page.X` 转到本名
            ["__page"] = pageObject,
            ["site"] = siteObject,
            // Hugo .Pages 语义：term 页为词条页面列表（per-page 独立包装）；
            // 未设置时回落全站 regular_pages——复用站点级共享包装，同一列表只转换一次
            ["pages"] = context.Pages is not null
                ? new LazyPageList(context.Pages)
                : GetSharedPageList(context.Site.RegularPages),
            ["output_format"] = context.Page.OutputFormat,
            ["params"] = context.Params,
            ["data"] = context.Data,
            ["language"] = context.Language,
            ["is_home"] = context.IsHome,
            ["is_list"] = context.IsList,
            ["is_single"] = context.IsSingle,

            [".Site"] = siteObject,
            [".Params"] = context.Params,
            [".Data"] = context.Data,
        };

        // C1 分页：pager 渲染实例携带逐页绑定的分页器——page.paginator（Hugo 语义）
        // 与全局 paginator 同源；非分页渲染（Paginator 为 null）回落站点级旧值
        var paginatorValue = context.Page.Paginator is not null
            ? BuildPaginatorObject(context.Page.Paginator)
            : siteObject["paginator"];
        globals["paginator"] = paginatorValue;
        globals["Paginator"] = paginatorValue;
        globals[".Paginator"] = paginatorValue;

        // i18n 翻译函数（主题系统 P3）：按站点 Translations 查键，缺键返回空串（对齐 Hugo）。
        // IScriptCustomFunction 显式实现（不经反射，AOT 安全——PartialCachedFunction 同模式）；
        // 捕获本页 siteContext 的翻译表，随 SiteContext 每构建装配
        globals.TrySetValue(scribanContext, default, "i18n",
            new I18nFunction(context.Site.Translations), readOnly: true);

        // 内容视图函数（主题兼容批次二 #7，对齐 Hugo .Render "view"）：
        // 按当前页查找视图模板（{section}/{view} → {view}）并渲染当前页上下文
        globals.TrySetValue(scribanContext, default, "render",
            new RenderViewFunction(this, context.Page, context.Site), readOnly: true);

        // partial 函数（B2）：返回值语义 + 标量类型还原（Hugo partial 可作函数用）；
        // includeCached（B3）：Hugo v0.146 的 partialCached 新名，指向同一实现
        globals.TrySetValue(scribanContext, default, "partial",
            new PartialFunction(this, pageObject), readOnly: true);

        // partialValue（Hugo partial 返回值语义）：渲染 partial 后从页面 Store 取回
        // **真实对象**（非文本还原）。Hugo 的 `{{ $x := partial "Y" . }}` 返回任意
        // 类型（资源对象/字典等），Scriban 的 include 只能文本化——故用 Store 作
        // 对象通道：partial 内 `{{ return X }}` 转换为 `{{ page.store.set K X }}{{ ret }}`，
        // 本函数渲染后取回。Store 是真实容器，完全保真且零序列化成本（实测验证）
        globals.TrySetValue(scribanContext, default, "partialValue",
            new PartialValueFunction(this, pageObject), readOnly: true);
        // 返回值通道：每页独立的 store + 写入函数（转换器的 `{{ return X }}` 改写用）
        globals[RetStoreKey] = new PageStoreObject();
        globals.TrySetValue(scribanContext, default, "__partial_ret_set",
            new PartialRetSetFunction(), readOnly: true);
        // templates.Exists 接真实 loader（覆盖全局的恒真实现）
        globals.TrySetValue(scribanContext, default, "template_exists",
            new TemplateExistsFunction((FileTemplateLoader)_templateLoader), readOnly: true);
        globals.TrySetValue(scribanContext, default, "includeCached",
            new PartialCachedFunction(this), readOnly: true);
        globals.TrySetValue(scribanContext, default, "include_cached",
            new PartialCachedFunction(this), readOnly: true);
        scribanContext.PushGlobal(globals);
        return scribanContext;
    }

    /// <summary>
    /// 内容视图渲染函数包装（C4，对齐 Hugo .Render "view"）：模板候选按页面
    /// <c>.Path</c> 从最深到最浅逐级加目录前缀（<c>docs/api/summary</c> →
    /// <c>docs/summary</c> → <c>summary</c>），用精确路径匹配（目录必须逐段相等）；
    /// 全部未命中返回空串（不是回退到文件名段匹配——那会让任意深度的
    /// 同名视图互相污染，Hugo 无此行为）
    /// </summary>
    private sealed class RenderViewFunction(
        ScribanTemplateRenderer renderer,
        PageContext page,
        SiteContext site)
        : Scriban.Runtime.IScriptCustomFunction
    {
        public object? Invoke(
            Scriban.TemplateContext context,
            Scriban.Syntax.ScriptNode? callerContext,
            Scriban.Runtime.ScriptArray arguments,
            Scriban.Syntax.ScriptBlockStatement? blockStatement)
        {
            var view = arguments.Count > 0 ? arguments[0]?.ToString() : null;
            if (string.IsNullOrWhiteSpace(view))
            {
                return "";
            }

            // 接收者页面（Hugo .Render 是页面方法）：loop 条目/with 上下文作为
            // 第二参数传入时以它为渲染上下文——list 模板 range 里 `.Render "summary"`
            // 的 dot 是当前文章，缺此参数会让每个条目都渲染外层列表页（Ananke 实测：
            // 三张卡片全显示 section 自身）。缺省回落调用页
            var receiver = arguments.Count >= 2 && arguments[1] is LazyPageObject receiverObj
                ? receiverObj.PageContext
                : page;

            foreach (var candidate in ViewCandidates(receiver.SourcePath, view))
            {
                if (renderer.TemplateExistsExact(candidate))
                {
                    var childCtx = new FlintTemplateContext { Page = receiver, Site = site };
                    return renderer.RenderAsync(candidate, childCtx).AsTask().GetAwaiter().GetResult();
                }
            }
            return "";
        }

        public System.Threading.Tasks.ValueTask<object?> InvokeAsync(
            Scriban.TemplateContext context,
            Scriban.Syntax.ScriptNode? callerContext,
            Scriban.Runtime.ScriptArray arguments,
            Scriban.Syntax.ScriptBlockStatement? blockStatement)
        {
            return new System.Threading.Tasks.ValueTask<object?>(
                Invoke(context, callerContext, arguments, blockStatement));
        }

        public int RequiredParameterCount => 1;

        public int ParameterCount => 2;

        public Scriban.Runtime.ScriptVarParamKind VarParamKind =>
            Scriban.Runtime.ScriptVarParamKind.Direct;

        public Type ReturnType => typeof(object);

        public Scriban.Runtime.ScriptParameterInfo GetParameterInfo(int index) => index switch
        {
            0 => new Scriban.Runtime.ScriptParameterInfo(typeof(string), "view"),
            _ => new Scriban.Runtime.ScriptParameterInfo(typeof(object), "context")
        };

        public Scriban.Runtime.ScriptParameterInfo ReturnParameterInfo =>
            new Scriban.Runtime.ScriptParameterInfo(typeof(object), "result");
    }

    /// <summary>
    /// 视图候选路径（深→浅，C4）：页面源路径的 content 相对目录逐级上溯加目录前缀。
    /// 例：/content/docs/api/leaf.md 的 "summary" → docs/api/summary、docs/summary、summary。
    /// 显式 slashed 视图（"_views/summary"）是布局根相对路径——同样逐级上溯前缀
    /// （_views/summary → docs/_views/summary …）
    /// </summary>
    internal static IEnumerable<string> ViewCandidates(string? sourcePath, string view)
    {
        var normalizedView = view.Replace('\\', '/').TrimStart('/');
        var dirSegments = new List<string>();
        if (!string.IsNullOrEmpty(sourcePath))
        {
            var full = sourcePath.Replace('\\', '/');
            var idx = full.LastIndexOf("/content/", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var after = full[(idx + "/content/".Length)..];
                var slash = after.LastIndexOf('/');
                if (slash > 0)
                {
                    // 目录段（不含文件名）；页面路径的目录即视图查找的起点
                    dirSegments = after[..slash]
                        .Split('/', StringSplitOptions.RemoveEmptyEntries)
                        .ToList();
                }
            }
        }

        // 深 → 浅：docs/api → docs → 根
        for (var i = dirSegments.Count; i >= 0; i--)
        {
            var prefix = i > 0 ? string.Join('/', dirSegments.Take(i)) + "/" : "";
            yield return prefix + normalizedView;
        }
    }

    /// <summary>
    /// i18n 翻译函数包装：单参数（翻译 ID），缺键返回空串
    /// </summary>
    /// <summary>
    /// Hugo 的 <c>i18n</c>/<c>lang.Translate</c>（v0.166 探针值）：
    /// <list type="bullet">
    /// <item>键支持**点分嵌套**（<c>article.readingTime</c>）；缺键输出空</item>
    /// <item>值可以是**复数子表**：按计数选形（美式英语 <c>1 → one</c>、其余 <c>other</c>，
    /// 探针：<c>i18n "readingTime" 1</c> → "One minute read"、<c>… 5</c> → "5 minutes read"、
    /// <c>… 0</c> → "0 minutes read"）；**不给计数时选 other 形**</item>
    /// <item>值里的 Go 模板占位（<c>{{ .Count }}</c>）用上下文（计数或 dict）插值——
    /// blowfish 的 <c>i18n "footer.powered_by" (dict "Hugo" … "Theme" …)</c> 即此形态；
    /// 缺失的键渲染为 <c>&lt;no value&gt;</c>（Hugo 同样如此）</item>
    /// </list>
    /// </summary>
    private sealed class I18nFunction(IReadOnlyDictionary<string, string> translations)
        : Scriban.Runtime.IScriptCustomFunction
    {
        private static readonly string[] PluralForms =
            ["zero", "one", "two", "few", "many", "other"];

        public object? Invoke(
            Scriban.TemplateContext context,
            Scriban.Syntax.ScriptNode? callerContext,
            ScriptArray arguments,
            Scriban.Syntax.ScriptBlockStatement? blockStatement)
        {
            // **键/上下文自适应**：迁移产物存在两种形态——
            //   直接调用 `i18n "key" $ctx`（键在前）
            //   管道 `dict … | i18n "key"`（Scriban 把管道左值注入**首参** → 键被挤到第二位）
            // loveit 的 summary.html 用后一形态，按首参是否为字符串判别键的位置
            var keyIndex = -1;
            for (var i = 0; i < arguments.Count; i++)
            {
                if (arguments[i] is string)
                {
                    keyIndex = i;
                    break;
                }
            }

            if (keyIndex < 0)
            {
                return "";
            }

            var key = arguments[keyIndex]?.ToString();
            if (string.IsNullOrEmpty(key))
            {
                return "";
            }

            // 上下文 = 除键之外的首个非空参数（dict / 值）
            object? contextValue = null;
            for (var i = 0; i < arguments.Count; i++)
            {
                if (i == keyIndex || arguments[i] is null)
                {
                    continue;
                }

                contextValue = arguments[i];
                break;
            }

            var value = Resolve(key, contextValue);
            return value is null ? "" : Interpolate(value, contextValue);
        }

        /// <summary>按键取形：有复数子表时按计数选形，否则取主键</summary>
        private string? Resolve(string key, object? contextValue)
        {
            if (PluralForms.Any(form => translations.ContainsKey(key + "." + form)))
            {
                var count = ExtractCount(contextValue);
                var form = count == 1 ? "one" : "other";
                if (translations.TryGetValue(key + "." + form, out var selected))
                {
                    return selected;
                }

                foreach (var candidate in PluralForms)
                {
                    if (translations.TryGetValue(key + "." + candidate, out var fallback))
                    {
                        return fallback;
                    }
                }

                return null;
            }

            return translations.TryGetValue(key, out var value) ? value : null;
        }

        /// <summary>计数：第二参是数字时直接取；是 dict 时取它的 <c>Count</c> 键</summary>
        private static long? ExtractCount(object? contextValue) => contextValue switch
        {
            null => null,
            string s when long.TryParse(s, out var parsed) => parsed,
            int i => i,
            long l => l,
            double d => (long)d,
            ScriptObject obj when obj.ContainsKey("Count") => ExtractCount(obj["Count"]),
            _ => null
        };

        /// <summary>
        /// Go 模板占位插值：<c>{{ .Key }}</c>（含 <c>{{.Key}}</c> 与内部空白）取上下文值。
        /// 上下文是 dict 时按键取；是计数时只有 <c>Count</c> 可用。
        /// 占位但取不到值的渲染为 <c>&lt;no value&gt;</c>（Hugo 探针同）
        /// </summary>
        private static string Interpolate(string value, object? contextValue)
        {
            if (!value.Contains("{{", StringComparison.Ordinal))
            {
                return value;
            }

            return System.Text.RegularExpressions.Regex.Replace(
                value,
                @"\{\{\s*\.([A-Za-z_][A-Za-z0-9_]*)\s*\}\}",
                match =>
                {
                    var field = match.Groups[1].Value;
                    object? replacement = null;
                    if (contextValue is ScriptObject obj)
                    {
                        obj.TryGetValue(null, default, field, out replacement);
                    }
                    else if (string.Equals(field, "Count", StringComparison.OrdinalIgnoreCase))
                    {
                        replacement = contextValue;
                    }

                    return replacement?.ToString() ?? "<no value>";
                });
        }

        public System.Threading.Tasks.ValueTask<object?> InvokeAsync(
            Scriban.TemplateContext context,
            Scriban.Syntax.ScriptNode? callerContext,
            ScriptArray arguments,
            Scriban.Syntax.ScriptBlockStatement? blockStatement)
        {
            return new System.Threading.Tasks.ValueTask<object?>(
                Invoke(context, callerContext, arguments, blockStatement));
        }

        public int RequiredParameterCount => 1;

        public int ParameterCount => 2;

        public Scriban.Runtime.ScriptVarParamKind VarParamKind =>
            Scriban.Runtime.ScriptVarParamKind.Direct;

        public Type ReturnType => typeof(object);

        // **两个参数都声明为 object**：若首参声明 string，Scriban 会对非字符串实参
        //（管道注入的 dict）做 ToString 强转，键变成 "map[Date:X]" 乱码（loveit 实测）
        public Scriban.Runtime.ScriptParameterInfo GetParameterInfo(int index) =>
            new(typeof(object), index == 0 ? "keyOrContext" : "contextOrKey");

        public Scriban.Runtime.ScriptParameterInfo ReturnParameterInfo =>
            new(typeof(string), "result");
    }

    private void CollectDependencies(
        string templateName,
        List<string> dependencies,
        HashSet<string> visited)
    {
        if (!visited.Add(templateName))
        {
            return; // 避免循环依赖
        }

        try
        {
            var templatePath = ResolveTemplatePath(templateName);
            var content = File.ReadAllText(templatePath);

            // 解析模板查找 include/partial 引用
            var template = Template.Parse(content);

            // 简单的正则匹配查找 include 语句
            // {{ include "partial_name" }} 或 {{ partial "partial_name" }}
            var matches = IncludePatternRegex().Matches(content);
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                var depName = match.Groups[1].Value;
                if (!dependencies.Contains(depName))
                {
                    dependencies.Add(depName);
                    CollectDependencies(depName, dependencies, visited);
                }
            }
        }
        catch (TemplateNotFoundException)
        {
            // 模板不存在，忽略
        }
    }

    /// <summary>include/partial 引用匹配（source-gen 正则，替代运行时 RegexOptions.Compiled）</summary>
    [GeneratedRegex(@"\{\{\s*(?:include|partial)\s+[""']([^""']+)[""']")]
    private static partial Regex IncludePatternRegex();
}

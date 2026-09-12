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
    }

    /// <summary>
    /// 本构建内模板函数产生的资源产物（resources.Concat/FromString/Fingerprint 结果），
    /// 由 SiteBuilder 输出阶段写盘——否则模板引用的 RelPermalink 会 404
    /// </summary>
    public IReadOnlyList<TemplateResource> GeneratedResources => _builtinFunctions.GeneratedResources;

    /// <summary>测试专用构造：时间源可注入</summary>
    public ScribanTemplateRenderer(string templatesPath, string baseUrl, TimeProvider timeProvider, params string[] themeTemplatePaths)
    {
        _templatesPath = templatesPath;
        _themeTemplatePaths = themeTemplatePaths.Where(p => !string.IsNullOrEmpty(p)).ToArray();
        _builtinFunctions = new BuiltinTemplateFunctions(baseUrl);
        _templateLoader = new FileTemplateLoader(templatesPath, themeTemplatePaths);
        _timeProvider = timeProvider;
        _builtinFunctions.TemplateExistsProbe = ProbeTemplateExists;
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
        var hookStore = new PageStoreObject();
        pageLike["store"] = hookStore;
        pageLike["Store"] = hookStore;
        pageLike["scratch"] = hookStore;
        pageLike["Scratch"] = hookStore;
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

        var context = new Scriban.TemplateContext
        {
            TemplateLoader = _templateLoader,
            MemberRenamer = member => member.Name,
            StrictVariables = false,
            // 对齐 Hugo：循环迭代数不做 1000 级人为限制——万页站点的列表/
            // taxonomy 页单循环即超默认值（同数据集对比测试实证）
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
            var isolatedContext = new Scriban.TemplateContext
            {
                TemplateLoader = _templateLoader,
                MemberRenamer = member => member.Name,
                StrictVariables = false,
                LoopLimit = 1_000_000
            };
            // 内置函数 + 日期对象都要装：只装前者时 `date.to_string` 会落到
            // Scriban **内置**的 date 对象（要求 DateTime），而 Flint 页面日期是
            // DateTimeOffset → "Unable to convert type `DateTimeOffset` to `DateTime`"
            //（Ananke site-footer.html 经 partialcached 渲染时实测 14 处）
            EnsureFunctionObjects(isolatedContext);
            if (scribanContext.CurrentGlobal is { } callerGlobals)
            {
                isolatedContext.PushGlobal(callerGlobals);
            }

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
    internal object RenderPartialWithType(Scriban.TemplateContext callerContext, string name)
    {
        var path = _templateLoader.GetPath(callerContext, default, name)
            ?? throw new InvalidOperationException($"partial 路径解析失败: {name}");
        var content = _templateLoader.Load(callerContext, default, path)
            ?? throw new InvalidOperationException($"partial 未找到: {name}");
        var partialTemplate = Template.Parse(content, path);
        if (partialTemplate.HasErrors)
        {
            throw new TemplateParseException(
                name,
                partialTemplate.Messages.Select(m => m.ToString()).ToList());
        }

        // 复用调用者上下文：partial 内可见 page/site/内置函数（Hugo partial 的
        // 第二参数语义在 Scriban 中由共享上下文天然满足）
        var rendered = partialTemplate.Render(callerContext);
        return RestoreScalarType(rendered);
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
        Scriban.TemplateContext callerContext, string name, object? context, ScriptObject pageObject)
    {
        // 目标 partial 若读写页面 Store（partial 返回值通道 / 显式暂存），
        // **不能**用裸 context 覆盖 page——Store 挂在原 page 对象上，覆盖即切断通道
        //（Ananke/Stack 实测 "page.store.set / page.Store.get for a null object"）。
        // 该判定按 partial 源文本做确定性检查（含其 include 链上的名字）；
        // 命中时退回共享上下文（dot 偏差换取通道完整）
        var path = _templateLoader.GetPath(callerContext, default, name) ?? "";
        var source = path.Length > 0 ? _templateLoader.Load(callerContext, default, path) : null;
        if (source is null || UsesPageStore(source))
        {
            return RenderPartialWithType(callerContext, name);
        }

        // 上下文为字典时，把调用者的 store/scratch 合并进去：既让 partial 内的
        // `.X` 指向传入上下文（Hugo dot 语义），又保留 page.store 通道可用
        var callerPage = pageObject is { } pg && pg.ContainsKey("store") ? pg["store"] : null;
        object? store = callerPage;
        object? effective = context;
        if (context is ScriptObject ctxObj && store is not null)
        {
            var merged = new ScriptObject();
            foreach (var key in ctxObj.Keys)
            {
                merged[key] = ctxObj[key];
            }
            merged["store"] = store;
            merged["Store"] = store;
            merged["scratch"] = store;
            merged["Scratch"] = store;
            effective = merged;
        }

        var overlay = new ScriptObject
        {
            ["page"] = effective,
            ["Page"] = effective
        };
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
            // Hugo 的 `partial "x" CONTEXT`：第二参数成为 partial 内的 `.`。
            // 迁移产物的裸 `.X` 被转成 `page.x`，故把 context 临时压成 `page` 全局
            if (arguments.Count > 1)
            {
                return renderer.RenderPartialWithContext(context, name, arguments[1], pageObject);
            }
            return renderer.RenderPartialWithType(context, name);
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
            store?.Set(PartialValueFunction.KeyPrefix + name, value);
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
            var canonical = CanonicalPartialKey(name);
            var key = KeyPrefix + canonical;
            // 清除上次残留（同一 partial 多次调用时避免读到旧值）
            store.Delete(key);

            // 渲染 partial：其副作用（page.store.set）写入共享 Store。
            // 返回值（文本还原形式）不使用——我们要的是 Store 中的真实对象
            _ = renderer.RenderPartialWithType(context, name);

            var value = store.Get(key);
            store.Delete(key);

            // 兜底：partial 无 return（无 store.set）时，回退标量还原结果
            return value ?? renderer.RenderPartialWithType(context, name);
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
            ["disqus"] = ""
        };

    /// <summary>
    /// Hugo 内置 opengraph.html 的 Scriban 等价：输出 og:* 元信息。
    /// 变量缺失时用 default 兜底，不产生空标签
    /// </summary>
    private static string BuildOpenGraphTemplate() =>
        """
        <meta property="og:title" content="{{ page.title | default site.title }}" />
        <meta property="og:description" content="{{ page.description | default page.summary | default site.params.description }}" />
        <meta property="og:type" content="{{ if is_home }}website{{ else }}article{{ end }}" />
        <meta property="og:url" content="{{ page.permalink | default site.base_url }}" />
        {{ if site.title }}<meta property="og:site_name" content="{{ site.title }}" />{{ end }}
        """;

    /// <summary>Hugo 内置 schema.html 的 Scriban 等价：输出 JSON-LD 骨架</summary>
    private static string BuildSchemaTemplate() =>
        """
        <script type="application/ld+json">{"@context":"https://schema.org","@type":"{{ if is_home }}WebSite{{ else }}BlogPosting{{ end }}","headline":"{{ page.title }}","url":"{{ page.permalink | default site.base_url }}"}</script>
        """;

    /// <summary>Hugo 内置 twitter_cards.html 的 Scriban 等价：输出 twitter:* 元信息</summary>
    private static string BuildTwitterCardsTemplate() =>
        """
        <meta name="twitter:title" content="{{ page.title | default site.title }}" />
        <meta name="twitter:description" content="{{ page.description | default page.summary }}" />
        <meta name="twitter:card" content="summary_large_image" />
        """;

    /// <summary>
    /// Hugo embedded pagination（default 格式）的 Scriban 翻译：
    /// 5 槽页码窗口居中于当前页，首末页与前后页按条件渲染。
    /// 原始字符串字面量（内含大量 {{ }} 与引号，逐字可读优于拼接/转义）
    /// </summary>
    private static string BuildPaginationTemplate()
    {
        return """
        {{ if paginator && paginator.total_pages > 1 }}
        <ul class="pagination pagination-default">
        {{ if paginator.first && paginator.page_number != paginator.first.page_number }}<li class="page-item"><a href="{{ paginator.first.url }}" aria-label="First" class="page-link" role="button"><span aria-hidden="true">&laquo;&laquo;</span></a></li>{{ else }}<li class="page-item disabled"><a aria-disabled="true" aria-label="First" class="page-link" role="button" tabindex="-1"><span aria-hidden="true">&laquo;&laquo;</span></a></li>{{ end }}
        {{ if paginator.prev }}<li class="page-item"><a href="{{ paginator.prev.url }}" aria-label="Previous" class="page-link" role="button"><span aria-hidden="true">&laquo;</span></a></li>{{ else }}<li class="page-item disabled"><a aria-disabled="true" aria-label="Previous" class="page-link" role="button" tabindex="-1"><span aria-hidden="true">&laquo;</span></a></li>{{ end }}
        {{ slots = 5 }}{{ start = paginator.page_number - 2 }}{{ if start < 1 }}{{ start = 1 }}{{ end }}{{ finish = start + slots - 1 }}{{ if finish > paginator.total_pages }}{{ finish = paginator.total_pages }}{{ end }}{{ if finish - start + 1 < slots }}{{ start = finish - slots + 1 }}{{ if start < 1 }}{{ start = 1 }}{{ end }}{{ end }}
        {{ for k in start..finish }}{{ if paginator.page_number == k }}<li class="page-item active"><a aria-current="page" aria-label="Page {{ k }}" class="page-link" role="button">{{ k }}</a></li>{{ else }}<li class="page-item"><a href="{{ paginator.pagers[k - 1].url }}" aria-label="Page {{ k }}" class="page-link" role="button">{{ k }}</a></li>{{ end }}{{ end }}
        {{ if paginator.next }}<li class="page-item"><a href="{{ paginator.next.url }}" aria-label="Next" class="page-link" role="button"><span aria-hidden="true">&raquo;</span></a></li>{{ else }}<li class="page-item disabled"><a aria-disabled="true" aria-label="Next" class="page-link" role="button" tabindex="-1"><span aria-hidden="true">&raquo;</span></a></li>{{ end }}
        {{ if paginator.last && paginator.page_number != paginator.last.page_number }}<li class="page-item"><a href="{{ paginator.last.url }}" aria-label="Last" class="page-link" role="button"><span aria-hidden="true">&raquo;&raquo;</span></a></li>{{ else }}<li class="page-item disabled"><a aria-disabled="true" aria-label="Last" class="page-link" role="button" tabindex="-1"><span aria-hidden="true">&raquo;&raquo;</span></a></li>{{ end }}
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
        var scribanContext = new Scriban.TemplateContext
        {
            TemplateLoader = _templateLoader,
            MemberRenamer = member => member.Name, // 保持原始属性名
            StrictVariables = false, // 允许访问未定义的变量
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
            context.Site.Taxonomies.Taxonomies);

        // 创建站点对象
        var siteObject = CreateSiteObject(context.Site);

        // 设置全局变量
        var globals = new ScriptObject
        {
            ["page"] = pageObject,
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
    private sealed class I18nFunction(IReadOnlyDictionary<string, string> translations)
        : Scriban.Runtime.IScriptCustomFunction
    {
        public object? Invoke(
            Scriban.TemplateContext context,
            Scriban.Syntax.ScriptNode? callerContext,
            Scriban.Runtime.ScriptArray arguments,
            Scriban.Syntax.ScriptBlockStatement? blockStatement)
        {
            var key = arguments.Count > 0 ? arguments[0]?.ToString() : null;
            return key is not null && translations.TryGetValue(key, out var value) ? value : "";
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

        public int ParameterCount => 1;

        public Scriban.Runtime.ScriptVarParamKind VarParamKind =>
            Scriban.Runtime.ScriptVarParamKind.Direct;

        public Type ReturnType => typeof(object);

        public Scriban.Runtime.ScriptParameterInfo GetParameterInfo(int index) =>
            new Scriban.Runtime.ScriptParameterInfo(typeof(string), "key");

        public Scriban.Runtime.ScriptParameterInfo ReturnParameterInfo =>
            new Scriban.Runtime.ScriptParameterInfo(typeof(object), "result");
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

// Flint 静态站点生成器
// Scriban 模板渲染器实现

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Flint.Core.Abstractions;
using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;
using FlintMenuCollection = Flint.Core.Abstractions.MenuCollection;
using FlintMenuItem = Flint.Core.Abstractions.MenuItem;
using FlintPageContext = Flint.Core.Abstractions.PageContext;
using FlintSiteContext = Flint.Core.Abstractions.SiteContext;
using FlintTaxonomyCollection = Flint.Core.Abstractions.TaxonomyCollection;
// 使用别名避免命名空间冲突
using FlintTemplateContext = Flint.Core.Abstractions.TemplateContext;

namespace Flint.Core.Templates;

/// <summary>
/// 基于 Scriban 的模板渲染器实现
/// 支持模板继承、partial 引用和自定义函数
/// </summary>
public sealed class ScribanTemplateRenderer : ITemplateRenderer
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
    }

    /// <summary>测试专用构造：时间源可注入</summary>
    public ScribanTemplateRenderer(string templatesPath, string baseUrl, TimeProvider timeProvider, params string[] themeTemplatePaths)
    {
        _templatesPath = templatesPath;
        _themeTemplatePaths = themeTemplatePaths.Where(p => !string.IsNullOrEmpty(p)).ToArray();
        _builtinFunctions = new BuiltinTemplateFunctions(baseUrl);
        _templateLoader = new FileTemplateLoader(templatesPath, themeTemplatePaths);
        _timeProvider = timeProvider;
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
        foreach (var kv in vars)
        {
            globals[kv.Key] = kv.Value;
        }

        var context = new Scriban.TemplateContext
        {
            TemplateLoader = _templateLoader,
            MemberRenamer = member => member.Name,
            StrictVariables = false
        };
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
        var cacheKey = name + "\u0001" + string.Join("\u0002", variants.Select(v => v?.ToString() ?? ""))
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

            // 隔离 context：仅内置函数（含 partialcached 递归），无页面输出流；
            // variants 注入为 variants 数组（对齐 Hugo 点参数语义：partial 内
            // 用 {{ variants.0 }} 访问）
            var isolatedContext = new Scriban.TemplateContext
            {
                TemplateLoader = _templateLoader,
                MemberRenamer = member => member.Name,
                StrictVariables = false
            };
            var partialGlobals = new ScriptObject();
            partialGlobals["variants"] = new ScriptArray(variants.Select(v => (object)v));
            isolatedContext.PushGlobal(partialGlobals);
            isolatedContext.PushGlobal(_cachedBuiltinObject!);
            // 同步 Render：隔离 context 的模板加载（FileTemplateLoader）为同步实现，
            // 无需异步——避免 sync-over-async（G17 棘轮）
            return partialTemplate.Render(isolatedContext);
        });
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
    /// 单次构建内部生效（构建内多页渲染共享首轮 stat）
    /// </summary>
    public void InvalidateMtimeCache()
    {
        _mtimeCache.Clear();
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
    /// 最小站点缺 taxonomy/term/list 布局时的极简兜底，避免构建报错
    /// </summary>
    internal static readonly Dictionary<string, string> BuiltinTemplates =
        new(StringComparer.Ordinal)
        {
            ["taxonomy"] = "<ul>{{ for p in site.regular_pages }}<li><a href=\"{{ p.permalink }}\">{{ p.title }}</a></li>{{ end }}</ul>",
            // term 用 pages（词条页面集合，renderer 对未设置 pages 的页面回落全站 regular_pages）
            ["term"] = "<ul>{{ for p in pages }}<li><a href=\"{{ p.permalink }}\">{{ p.title }}</a></li>{{ end }}</ul>",
            ["list"] = "<ul>{{ for p in pages }}<li><a href=\"{{ p.permalink }}\">{{ p.title }}</a></li>{{ end }}</ul>"
        };
    private string ResolveTemplatePath(string templateName)
    {
        // 支持多种模板查找路径：站点 layouts 优先，主题 layouts 按序回退
        //（对齐 Hugo 主题叠加：先挂载者赢；_default 是 legacy 约定保留）
        var searchPaths = new List<string>
        {
            Path.Combine(_templatesPath, templateName),
            Path.Combine(_templatesPath, templateName + ".html"),
            Path.Combine(_templatesPath, "layouts", templateName),
            Path.Combine(_templatesPath, "layouts", templateName + ".html"),
            Path.Combine(_templatesPath, "_default", templateName),
            Path.Combine(_templatesPath, "_default", templateName + ".html"),
        };

        foreach (var themePath in _themeTemplatePaths)
        {
            searchPaths.Add(Path.Combine(themePath, templateName));
            searchPaths.Add(Path.Combine(themePath, templateName + ".html"));
            searchPaths.Add(Path.Combine(themePath, "_default", templateName));
            searchPaths.Add(Path.Combine(themePath, "_default", templateName + ".html"));
        }

        foreach (var path in searchPaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        throw new TemplateNotFoundException(templateName, searchPaths);
    }


    // 缓存内置函数对象（线程安全，只创建一次）
    private ScriptObject? _cachedBuiltinObject;
    private ScriptObject? _cachedDateObject;

    private Scriban.TemplateContext CreateScribanContext(FlintTemplateContext context)
    {
        // 安全契约决策（见 README「HTML 转义契约」）：Scriban 默认不启用
        // HTML 自动转义——Flint 保持该默认，模板输出不做上下文转义矩阵，
        // 内容可信性由内容管线与模板作者负责（对齐 Hugo safe* 恒等语义）
        var scribanContext = new Scriban.TemplateContext
        {
            TemplateLoader = _templateLoader,
            MemberRenamer = member => member.Name, // 保持原始属性名
            StrictVariables = false // 允许访问未定义的变量
        };

        // 渲染期依赖收集（T4.1）：按页面初始化依赖快照容器
        RenderDependencyTracker.Initialize(scribanContext, context.Page.SourcePath);

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

        // 创建页面对象
        var pageObject = CreatePageObject(context.Page);

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

            // Hugo 兼容别名
            [".Page"] = pageObject,
            [".Site"] = siteObject,
            [".Params"] = context.Params,
            [".Data"] = context.Data,
        };

        scribanContext.PushGlobal(globals);
        return scribanContext;
    }

    private static ScriptObject CreatePageObject(FlintPageContext page)
    {
        // term 页 page.pages（词条页面集合）与 taxonomy 页 page.terms（词条列表）
        // 的数据源——渲染器以手工 ScriptObject 暴露成员（不走反射），PageContext
        // 新属性必须在此映射，否则模板拿到 null 渲染空列表（端到端冒烟发现的回归）
        object? pagesValue = page.Pages is not null ? GetSharedPageList(page.Pages) : null;
        object? termsValue = page.Terms is not null
            ? page.Terms.Select(t => (object)new LazyTaxonomyTerm(t)).ToList()
            : null;

        return new ScriptObject
        {
            ["title"] = page.Title,
            ["content"] = page.Content,            ["output_format"] = page.OutputFormat,
            ["permalink"] = page.Permalink,
            ["rel_permalink"] = page.RelPermalink,
            ["date"] = page.Date,
            ["lastmod"] = page.LastMod,
            ["tags"] = page.Tags,
            ["categories"] = page.Categories,
            ["word_count"] = page.WordCount,
            ["reading_time"] = page.ReadingTime,
            ["description"] = page.Description,
            ["summary"] = page.Summary,
            ["prev_page"] = page.PrevPage != null ? CreatePageObject(page.PrevPage) : null,
            ["next_page"] = page.NextPage != null ? CreatePageObject(page.NextPage) : null,
            ["type"] = page.Type,
            ["layout"] = page.Layout,
            ["draft"] = page.Draft,
            ["weight"] = page.Weight,
            ["params"] = page.Params,
            ["resources"] = page.Resources,
            ["pages"] = pagesValue,
            ["terms"] = termsValue,
            ["table_of_contents"] = page.TableOfContents,
            ["plain"] = page.Plain,
            ["raw_content"] = page.RawContent,

            // Hugo 兼容别名（大写开头）
            ["Title"] = page.Title,
            ["Content"] = page.Content,
            ["Permalink"] = page.Permalink,
            ["RelPermalink"] = page.RelPermalink,
            ["Date"] = page.Date,
            ["Lastmod"] = page.LastMod,
            ["Tags"] = page.Tags,
            ["Categories"] = page.Categories,
            ["WordCount"] = page.WordCount,
            ["ReadingTime"] = page.ReadingTime,
            ["Description"] = page.Description,
            ["Summary"] = page.Summary,
            ["PrevPage"] = page.PrevPage != null ? CreatePageObject(page.PrevPage) : null,
            ["NextPage"] = page.NextPage != null ? CreatePageObject(page.NextPage) : null,
            ["Type"] = page.Type,
            ["Layout"] = page.Layout,
            ["Draft"] = page.Draft,
            ["Weight"] = page.Weight,
            ["Params"] = page.Params,
            ["Resources"] = page.Resources,
            ["Pages"] = pagesValue,
            ["Terms"] = termsValue,
            ["TableOfContents"] = page.TableOfContents,
            ["Plain"] = page.Plain,
            ["RawContent"] = page.RawContent,
        };
    }

    // 页面集合的跨渲染包装缓存（T4.3 值缓存的分层落地）：
    // 同一源列表（同一次构建的 SiteContext.Pages 等）复用同一 LazyPageList——
    // PageContext→ScriptObject 转换全构建只做一次（万页站点每次渲染省 O(n) 转换）；
    // 新构建产生新列表实例，按引用自动失效。CWT 防列表驻留导致缓存泄漏
    private static readonly ConditionalWeakTable<IReadOnlyList<FlintPageContext>, LazyPageList> SharedPageLists = new();

    private static ScriptObject CreateSiteObject(FlintSiteContext site)
    {
        // 惰加载包装器跨渲染共享（见 SharedPageLists）；GetConvertedPages 内部有锁保证并发安全
        var lazyPages = GetSharedPageList(site.Pages);
        var lazyRegularPages = GetSharedPageList(site.RegularPages);
        var lazyTaxonomies = new LazyTaxonomies(site.Taxonomies);

        // 依赖跟踪站点对象：site.* 成员访问被记录为 data:site.* 依赖键（T4.1）
        return new DependencyTrackingScriptObject
        {
            ["title"] = site.Title,
            ["base_url"] = site.BaseURL,
            ["language"] = site.Language,
            ["pages"] = lazyPages,
            ["regular_pages"] = lazyRegularPages,
            ["taxonomies"] = lazyTaxonomies,
            ["menus"] = CreateMenusObject(site.Menus),
            ["config"] = site.Config,
            ["data"] = site.Data,
            ["params"] = site.Params,
            ["build_date"] = site.BuildDate,
            ["last_change"] = site.LastChange,
            ["is_multilingual"] = site.IsMultiLingual,
            ["languages"] = site.Languages,

            // Hugo 兼容别名
            ["Title"] = site.Title,
            ["BaseURL"] = site.BaseURL,
            ["Language"] = site.Language,
            ["Pages"] = lazyPages,
            ["RegularPages"] = lazyRegularPages,
            ["Taxonomies"] = lazyTaxonomies,
            ["Menus"] = CreateMenusObject(site.Menus),
            ["Config"] = site.Config,
            ["Data"] = site.Data,
            ["Params"] = site.Params,
            ["BuildDate"] = site.BuildDate,
            ["LastChange"] = site.LastChange,
            ["IsMultiLingual"] = site.IsMultiLingual,
            ["Languages"] = site.Languages,
        };
    }

    /// <summary>
    /// 懒加载页面列表包装器
    /// 只有在实际访问时才转换页面对象，避免 O(n²) 问题
    /// </summary>
    private static LazyPageList GetSharedPageList(IReadOnlyList<FlintPageContext> pages)
    {
        return SharedPageLists.GetValue(pages, static p => new LazyPageList(p));
    }

    private sealed class LazyPageList : ScriptObject, IEnumerable<ScriptObject>, IList<ScriptObject>
    {
        private readonly IReadOnlyList<FlintPageContext> _pages;
        private List<ScriptObject>? _convertedPages;

        public LazyPageList(IReadOnlyList<FlintPageContext> pages)
        {
            _pages = pages;
            // 设置 count/length 属性，这些是常用的且不需要转换所有页面
            SetValue("count", pages.Count, false);
            SetValue("length", pages.Count, false);
            SetValue("size", pages.Count, false);
            SetValue("Count", pages.Count, false);
            SetValue("Length", pages.Count, false);
        }

        // IList<ScriptObject> 实现 - 用于 Scriban 的 len 过滤器
        int ICollection<ScriptObject>.Count => _pages.Count;
        bool ICollection<ScriptObject>.IsReadOnly => true;

        public ScriptObject this[int index]
        {
            get => GetConvertedPages()[index];
            set => throw new NotSupportedException();
        }

        public int IndexOf(ScriptObject item) => GetConvertedPages().IndexOf(item);
        public bool Contains(ScriptObject item) => GetConvertedPages().Contains(item);
        public void CopyTo(ScriptObject[] array, int arrayIndex) => GetConvertedPages().CopyTo(array, arrayIndex);
        void ICollection<ScriptObject>.Add(ScriptObject item) => throw new NotSupportedException();
        void ICollection<ScriptObject>.Clear() => throw new NotSupportedException();
        bool ICollection<ScriptObject>.Remove(ScriptObject item) => throw new NotSupportedException();
        public void Insert(int index, ScriptObject item) => throw new NotSupportedException();
        public void RemoveAt(int index) => throw new NotSupportedException();

        private readonly object _conversionLock = new();

        private List<ScriptObject> GetConvertedPages()
        {
            // 跨渲染共享后的并发保护：并行页面渲染同时枚举同一实例
            if (_convertedPages is not null)
            {
                return _convertedPages;
            }

            lock (_conversionLock)
            {
                return _convertedPages ??= _pages.Select(CreatePageObject).ToList();
            }
        }

        public new IEnumerator<ScriptObject> GetEnumerator()
        {
            return GetConvertedPages().GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetConvertedPages().GetEnumerator();
        }

        public override bool TryGetValue(Scriban.TemplateContext? context, SourceSpan span, string member, out object? value)
        {
            // 处理索引访问
            if (int.TryParse(member, out var index) && index >= 0 && index < _pages.Count)
            {
                value = GetConvertedPages()[index];
                return true;
            }

            // 处理 first/last 等常用属性
            switch (member.ToLowerInvariant())
            {
                case "first":
                    value = _pages.Count > 0 ? CreatePageObject(_pages[0]) : null;
                    return true;
                case "last":
                    value = _pages.Count > 0 ? CreatePageObject(_pages[^1]) : null;
                    return true;
            }

            return base.TryGetValue(context, span, member, out value);
        }
    }

    /// <summary>
    /// 懒加载分类集合包装器
    /// </summary>
    private sealed class LazyTaxonomies : ScriptObject
    {
        private readonly FlintTaxonomyCollection _taxonomies;
        private readonly Dictionary<string, object> _convertedTaxonomies = new();

        public LazyTaxonomies(FlintTaxonomyCollection taxonomies)
        {
            _taxonomies = taxonomies;
        }

        public override bool TryGetValue(Scriban.TemplateContext? context, SourceSpan span, string member, out object? value)
        {
            if (_convertedTaxonomies.TryGetValue(member, out var cached))
            {
                value = cached;
                return true;
            }

            if (_taxonomies.Taxonomies.TryGetValue(member, out var terms))
            {
                // 使用懒加载的术语列表
                var lazyTerms = terms.Select(t => new LazyTaxonomyTerm(t)).ToList();
                _convertedTaxonomies[member] = lazyTerms;
                value = lazyTerms;
                return true;
            }

            return base.TryGetValue(context, span, member, out value);
        }
    }

    /// <summary>
    /// 懒加载分类术语包装器
    /// </summary>
    private sealed class LazyTaxonomyTerm : ScriptObject
    {
        private readonly TaxonomyTerm _term;
        private LazyPageList? _lazyPages;

        public LazyTaxonomyTerm(TaxonomyTerm term)
        {
            _term = term;
            // 设置基本属性（不需要转换页面）
            SetValue("name", term.Name, false);
            SetValue("slug", term.Slug, false);
            SetValue("count", term.Count, false);
            SetValue("permalink", term.Permalink, false);

            // Hugo 兼容
            SetValue("Name", term.Name, false);
            SetValue("Slug", term.Slug, false);
            SetValue("Count", term.Count, false);
            SetValue("Permalink", term.Permalink, false);
        }

        public override bool TryGetValue(Scriban.TemplateContext? context, SourceSpan span, string member, out object? value)
        {
            // 只有访问 pages/Pages 时才懒加载
            if (member.Equals("pages", StringComparison.OrdinalIgnoreCase))
            {
                _lazyPages ??= new LazyPageList(_term.Pages);
                value = _lazyPages;
                return true;
            }

            return base.TryGetValue(context, span, member, out value);
        }
    }


    // CreateTaxonomiesObject 已被 LazyTaxonomies 替代，不再需要

    private static ScriptObject CreateMenusObject(FlintMenuCollection menus)
    {
        var obj = new ScriptObject();
        foreach (var (name, items) in menus.Menus)
        {
            obj[name] = items.Select(CreateMenuItemObject).ToList();
        }
        return obj;
    }

    private static ScriptObject CreateMenuItemObject(FlintMenuItem item)
    {
        return new ScriptObject
        {
            ["name"] = item.Name,
            ["url"] = item.URL,
            ["weight"] = item.Weight,
            ["identifier"] = item.Identifier,
            ["parent"] = item.Parent,
            ["pre"] = item.Pre,
            ["post"] = item.Post,
            ["children"] = item.Children.Select(CreateMenuItemObject).ToList(),
            ["has_children"] = item.HasChildren,
            ["is_active"] = item.IsActive,

            // Hugo 兼容
            ["Name"] = item.Name,
            ["URL"] = item.URL,
            ["Weight"] = item.Weight,
            ["Identifier"] = item.Identifier,
            ["Parent"] = item.Parent,
            ["Pre"] = item.Pre,
            ["Post"] = item.Post,
            ["Children"] = item.Children.Select(CreateMenuItemObject).ToList(),
            ["HasChildren"] = item.HasChildren,
            ["IsActive"] = item.IsActive,
        };
    }

    /// <summary>
    /// 注册自定义日期对象，支持 DateTimeOffset 类型
    /// 覆盖 Scriban 内置的 date 对象
    /// </summary>
    private static void RegisterDateObject(ScriptObject dateObject)
    {
        // Scriban 7 起 Import 标注 RequiresUnreferencedCode（反射创建
        // DynamicCustomFunction）；Import 目标是本方法显式引用的 lambda，
        // linker 不会裁剪被引用成员——运行时安全，方法级压制
#pragma warning disable IL2026
        // to_string - 格式化日期，支持 DateTimeOffset
        dateObject.Import("to_string", (object? date, string? format) =>
        {
            var dt = ConvertToDateTimeOffset(date);
            if (dt == null)
                return "";

            // 使用 .NET 标准格式化
            return dt.Value.ToString(format ?? "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        });

        // now - 当前时间
        dateObject.Import("now", () => DateTimeOffset.Now);

        // parse - 解析日期字符串
        dateObject.Import("parse", (string? s) =>
        {
            if (DateTimeOffset.TryParse(s, out var result))
                return result;
            return DateTimeOffset.MinValue;
        });

        // add_days - 添加天数
        dateObject.Import("add_days", (object? date, int days) =>
        {
            var dt = ConvertToDateTimeOffset(date);
            return dt?.AddDays(days);
        });

        // add_months - 添加月数
        dateObject.Import("add_months", (object? date, int months) =>
        {
            var dt = ConvertToDateTimeOffset(date);
            return dt?.AddMonths(months);
        });

        // add_years - 添加年数
        dateObject.Import("add_years", (object? date, int years) =>
        {
            var dt = ConvertToDateTimeOffset(date);
            return dt?.AddYears(years);
        });
#pragma warning restore IL2026
    }

    /// <summary>
    /// 将各种日期类型转换为 DateTimeOffset
    /// </summary>
    private static DateTimeOffset? ConvertToDateTimeOffset(object? value)
    {
        return value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(dt),
            string s when DateTimeOffset.TryParse(s, out var result) => result,
            long unix => DateTimeOffset.FromUnixTimeSeconds(unix),
            _ => null
        };
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
            var includePattern = new System.Text.RegularExpressions.Regex(
                @"\{\{\s*(?:include|partial)\s+[""']([^""']+)[""']",
                System.Text.RegularExpressions.RegexOptions.Compiled);

            var matches = includePattern.Matches(content);
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
}

/// <summary>
/// 文件系统模板加载器
/// </summary>
internal sealed class FileTemplateLoader : ITemplateLoader
{
    private readonly string _basePath;
    // include 的主题回退目录（与页面模板查找同序：站点优先，主题按序回退）
    private readonly string[] _themeBasePaths;

    public FileTemplateLoader(string basePath, params string[] themeBasePaths)
    {
        _basePath = basePath;
        _themeBasePaths = themeBasePaths.Where(p => !string.IsNullOrEmpty(p)).ToArray();
    }

    private string[] Roots
    {
        get
        {
            var roots = new List<string> { _basePath };
            roots.AddRange(_themeBasePaths);
            return [.. roots];
        }
    }

    /// <summary>
    /// Scriban include 的路径解析入口：按根序（站点→主题）探测候选形态，
    /// 返回实际存在的物理路径——主题回退必须在此完成（Scriban 先 GetPath
    /// 后 Load，Load 拿到的已是探测结果）
    /// </summary>
    public string GetPath(Scriban.TemplateContext context, SourceSpan callerSpan, string templateName)
    {
        var relativeForms = new List<string> { templateName, templateName + ".html" };
        // _default/ 形态（对齐 ResolveTemplatePath 的搜索序）
        relativeForms.Add(Path.Combine("_default", templateName));
        relativeForms.Add(Path.Combine("_default", templateName + ".html"));
        var hasPartialsPrefix = templateName.StartsWith("partials/", StringComparison.OrdinalIgnoreCase) ||
                                templatePathStartsWithBackslash(templateName);
        if (!hasPartialsPrefix)
        {
            relativeForms.Add(Path.Combine("partials", templateName));
            relativeForms.Add(Path.Combine("partials", templateName + ".html"));
        }

        foreach (var root in Roots)
        {
            foreach (var form in relativeForms)
            {
                var candidate = Path.Combine(root, form);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        // 未命中：返回主根形态，让 Load 抛出带上下文的 FileNotFoundException
        return Path.Combine(_basePath, templateName);
    }

    private static bool templatePathStartsWithBackslash(string templateName) =>
        templateName.StartsWith("partials\\", StringComparison.OrdinalIgnoreCase);

    public string Load(Scriban.TemplateContext context, SourceSpan callerSpan, string templatePath)
    {
        // include 路径源自模板内容，读取前校验仍在任一根内——
        // 与 DevServer 静态服务的防穿越标准对齐，"..\" 类路径不再读出模板目录。
        // 无条件 GetFullPath：模板根为相对路径时组合产物非 rooted，
        // 以 IsPathRooted 为前提会让 "{{ include \"../..\" }}" 完全绕过校验
        var fullPath = Path.GetFullPath(templatePath);
        var allowedRoots = Roots.Select(r => Path.GetFullPath(r)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar).ToList();
        if (!allowedRoots.Any(allowed =>
                fullPath.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)))
        {
            throw new FileNotFoundException($"模板 include 路径越出模板目录，已拒绝: {templatePath}");
        }

        // 渲染期依赖收集（T4.1）：include/partial 实际命中的物理路径
        RenderDependencyTracker.Track(context, fullPath);
        return File.ReadAllText(fullPath);
    }

    // Scriban 7 的 ITemplateLoader.LoadAsync 返回注解为 ValueTask<string?>
    public ValueTask<string?> LoadAsync(Scriban.TemplateContext context, SourceSpan callerSpan, string templatePath)
    {
        return new ValueTask<string?>(Load(context, callerSpan, templatePath));
    }
}

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

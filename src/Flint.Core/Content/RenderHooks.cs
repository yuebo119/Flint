// Flint 静态站点生成器
// RenderHooks——layouts/_markup/render-*.html 渲染钩子（对齐 Hugo render hooks 的最小集）

using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.Templates;

namespace Flint.Core.Content;

/// <summary>
/// Markdown 渲染钩子集合：链接/图片/标题的定制渲染模板。
/// 每个委托把钩子变量（destination/text/plain_text/level/id 等）交给
/// <see cref="ScribanTemplateRenderer.RenderHookTemplate"/> 渲染，返回输出 HTML；
/// 模板渲染失败按构建错误 fail-fast。仅存在对应 _markup/ 模板时才非 null。
/// </summary>
public sealed class RenderHooks
{
    /// <summary>render-link.html（同时拦截 <c>&lt;https://…&gt;</c> 自动链接）</summary>
    public Func<IReadOnlyDictionary<string, object>, string>? Link { get; init; }

    /// <summary>render-image.html（优先于 render-link 处理图片）</summary>
    public Func<IReadOnlyDictionary<string, object>, string>? Image { get; init; }

    /// <summary>render-heading.html</summary>
    public Func<IReadOnlyDictionary<string, object>, string>? Heading { get; init; }

    /// <summary>render-codeblock.html（全部代码块的通用钩子；可被语言专属钩子覆盖）</summary>
    public Func<IReadOnlyDictionary<string, object>, string>? CodeBlock { get; init; }

    /// <summary>render-codeblock-&lt;lang&gt;.html（语言专属钩子，优先于通用钩子）</summary>
    public IReadOnlyDictionary<string, Func<IReadOnlyDictionary<string, object>, string>> CodeBlockByLang { get; init; } =
        new Dictionary<string, Func<IReadOnlyDictionary<string, object>, string>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>是否装有任一钩子</summary>
    public bool HasAny => Link is not null || Image is not null || Heading is not null ||
                          CodeBlock is not null || CodeBlockByLang.Count > 0;

    /// <summary>
    /// 从 layouts/_markup/ 探测钩子模板并构建钩子集合；
    /// 目录或模板不存在时返回 null（走 Markdig 默认渲染，零开销）
    /// </summary>
    /// <param name="layoutsDirectory">站点 layouts 目录</param>
    /// <param name="renderer">Scriban 渲染器（.html 钩子）</param>
    /// <param name="themeLayoutDirectories">主题 layouts 回退目录</param>
    /// <param name="fluidRenderer">Fluid 渲染器（.liquid 钩子，FLUID-MIGRATION-PLAN
    /// P5-5）；null = 不探测 .liquid 钩子</param>
    public static RenderHooks? Load(
        string layoutsDirectory,
        ScribanTemplateRenderer renderer,
        IEnumerable<string>? themeLayoutDirectories = null,
        FluidTemplateRenderer? fluidRenderer = null)
        => LoadCore(CollectMarkupDirs(layoutsDirectory, themeLayoutDirectories), renderer, fluidRenderer);

    /// <summary>
    /// **Fluid-only 装配**（裸 FluidTemplateRenderer 用，不经 router 的用法）：
    /// 只探测 .liquid 钩子，.html 钩子不认（与裸 Scriban 渲染器只认 .html 对称）。
    /// 签名不涉 Scriban 类型——调用方 FluidTemplateRenderer 是引擎隔离门禁的
    /// 热路径类型，其 IL 不得出现 Scriban 程序集引用（EngineIsolationTests），
    /// 故不能直接调 <see cref="Load"/>（其签名含 ScribanTemplateRenderer）
    /// </summary>
    public static RenderHooks? LoadFluid(
        string layoutsDirectory,
        FluidTemplateRenderer renderer,
        IEnumerable<string>? themeLayoutDirectories = null)
        => LoadCore(CollectMarkupDirs(layoutsDirectory, themeLayoutDirectories), null, renderer);

    /// <summary>_markup 目录收集：站点优先、主题按序回退（同名钩子站点覆盖主题）</summary>
    private static List<string> CollectMarkupDirs(
        string layoutsDirectory, IEnumerable<string>? themeLayoutDirectories)
    {
        var markupDirs = new List<string> { Path.Combine(layoutsDirectory, "_markup") };
        if (themeLayoutDirectories is not null)
        {
            foreach (var themeDir in themeLayoutDirectories)
            {
                markupDirs.Add(Path.Combine(themeDir, "_markup"));
            }
        }
        return markupDirs;
    }

    /// <summary>钩子装配核心：两渲染器均可空（null = 不探该形态）；全无钩子返回 null</summary>
    private static RenderHooks? LoadCore(
        List<string> markupDirs,
        ScribanTemplateRenderer? renderer,
        FluidTemplateRenderer? fluidRenderer)
    {
        if (markupDirs.All(d => !Directory.Exists(d)))
        {
            return null;
        }

        // 每个钩子位：.liquid（Fluid）优先于 .html（Scriban）——新站点主力
        // 形态覆盖 Hugo 主题形态；Scriban 兼容模式下 fluidRenderer 为 null，
        // 只认 .html；Fluid-only 装配下 renderer 为 null，只认 .liquid
        var link = TryLoadLiquidMulti(markupDirs, "render-link", fluidRenderer)
            ?? (renderer is null ? null : TryLoadMulti(markupDirs, "render-link", renderer));
        var image = TryLoadLiquidMulti(markupDirs, "render-image", fluidRenderer)
            ?? (renderer is null ? null : TryLoadMulti(markupDirs, "render-image", renderer));
        var heading = TryLoadLiquidMulti(markupDirs, "render-heading", fluidRenderer)
            ?? (renderer is null ? null : TryLoadMulti(markupDirs, "render-heading", renderer));
        var codeBlock = TryLoadLiquidMulti(markupDirs, "render-codeblock", fluidRenderer)
            ?? (renderer is null ? null : TryLoadMulti(markupDirs, "render-codeblock", renderer));

        // 语言专属变体：render-codeblock-<lang>（优先于通用 codeblock 钩子）。
        // .liquid 与 .html 双形态，同名时 .liquid 优先
        var byLang = new Dictionary<string, Func<IReadOnlyDictionary<string, object>, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var markupDir in markupDirs)
        {
            if (!Directory.Exists(markupDir))
            {
                continue;
            }
            foreach (var file in Directory.EnumerateFiles(markupDir, "render-codeblock-*"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var lang = name["render-codeblock".Length..].TrimStart('.', '-');
                if (lang.Length == 0 || byLang.ContainsKey(lang))
                {
                    continue;
                }
                if (file.EndsWith(".liquid", StringComparison.OrdinalIgnoreCase) && fluidRenderer is not null)
                {
                    var liquidContent = File.ReadAllText(file);
                    byLang[lang] = vars => RenderLiquidHook(fluidRenderer, liquidContent, "_markup/" + name, vars);
                }
                else if (file.EndsWith(".html", StringComparison.OrdinalIgnoreCase) && renderer is not null)
                {
                    byLang[lang] = vars => renderer.RenderHookTemplate("_markup/" + name, vars)
                        ?? throw new InvalidOperationException($"渲染钩子模板 {name} 未产出内容");
                }
            }
        }

        var hooks = new RenderHooks
        {
            Link = link,
            Image = image,
            Heading = heading,
            CodeBlock = codeBlock,
            CodeBlockByLang = byLang
        };
        return hooks.HasAny ? hooks : null;
    }

    private static Func<IReadOnlyDictionary<string, object>, string>? TryLoadMulti(
        IEnumerable<string> markupDirs, string name, ScribanTemplateRenderer renderer)
    {
        foreach (var dir in markupDirs)
        {
            var result = TryLoad(dir, name, renderer);
            if (result is not null)
            {
                return result; // 站点目录在序列首位，先命中即覆盖主题
            }
        }
        return null;
    }

    /// <summary>
    /// Liquid 版钩子探测（FLUID-MIGRATION-PLAN P5-5）：同目录内
    /// <c>&lt;hookName&gt;.liquid</c> 优先于 <c>&lt;hookName&gt;.html</c>
    /// （新站点主力形态覆盖 Hugo 主题形态）
    /// </summary>
    private static Func<IReadOnlyDictionary<string, object>, string>? TryLoadLiquidMulti(
        IEnumerable<string> markupDirs, string name, FluidTemplateRenderer? renderer)
    {
        if (renderer is null)
        {
            return null;
        }
        foreach (var dir in markupDirs)
        {
            var file = Path.Combine(dir, name + ".liquid");
            if (File.Exists(file))
            {
                var hookFile = new HookTemplateFile(file);
                return vars => RenderLiquidHook(
                    renderer, hookFile.GetContent(Environment.TickCount64), name, vars);
            }
        }
        return null;
    }

    /// <summary>
    /// Liquid 钩子模板文件的内容读取：按 mtime + 50ms 窗口节流。
    /// 此前在加载时一次性读入并闭包捕获，serve 里改 hook 文件永不生效
    /// （渲染器的编译缓存虽有内容失效判据，但拿到的仍是旧文本）；
    /// 每次调用都读盘则每个链接/图片各付一次 IO，故用窗口节流。
    /// </summary>
    internal sealed class HookTemplateFile(string file)
    {
        private const long ReloadWindowMs = 50;

        private readonly string _file = file;
        private string _content = "";
        private DateTime _mtimeUtc;
        private long _length;
        private long _checkedAtMs = long.MinValue;
        private bool _loaded;

        internal string GetContent(long nowMs)
        {
            if (_loaded && nowMs - _checkedAtMs < ReloadWindowMs)
            {
                return _content;
            }

            // (mtime, length) 双字段验效：NTFS 同时间粒度内改写 mtime 可能不前进，
            // 只看 mtime 会把新内容当旧内容（与模板文本缓存同口径）
            var info = new FileInfo(_file);
            var mtime = info.LastWriteTimeUtc;
            var length = info.Exists ? info.Length : 0;
            if (!_loaded || mtime != _mtimeUtc || length != _length)
            {
                _content = info.Exists ? File.ReadAllText(_file) : "";
                _mtimeUtc = mtime;
                _length = length;
            }

            _loaded = true;
            _checkedAtMs = nowMs;
            return _content;
        }
    }

    private static string RenderLiquidHook(
        FluidTemplateRenderer renderer,
        string content,
        string hookName,
        IReadOnlyDictionary<string, object> vars)
    {
        // 空模板与 Scriban 侧同口径返回空串（Hugo 也允许空 hook）。
        // 非空模板渲染出空结果仍报错——那是钩子逻辑写坏，不该静默吞掉整段正文
        if (string.IsNullOrWhiteSpace(content))
        {
            return "";
        }

        // 钩子变量经 ExtraValues 通道合并进 Fluid 模型顶层键
        // （Liquid 无 page 包装，.Text/.Level 等直取）；page/site 用最小
        // 上下文（hook 渲染期无真实页面上下文，与 Scriban 侧同口径）
        var extra = new Dictionary<string, object?>(vars.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in vars)
        {
            extra[key] = value;
        }
        var context = new TemplateContext
        {
            ExtraValues = extra,
            Page = new PageContext
            {
                Title = hookName, Content = "", Permalink = "", RelPermalink = "",
                Date = DateTimeOffset.MinValue, Tags = [], Categories = [],
                WordCount = 0, ReadingTime = TimeSpan.Zero
            },
            Site = new SiteContext
            {
                Title = "", BaseURL = "", Language = "",
                Pages = [], RegularPages = [],
                Taxonomies = new TaxonomyCollection { Taxonomies = new Dictionary<string, IReadOnlyList<TaxonomyTerm>>() },
                Menus = new MenuCollection { Menus = new Dictionary<string, IReadOnlyList<MenuItem>>() },
                Config = new SiteConfig { BaseURL = "", Title = "" }
            }
        };

        var output = renderer.RenderLiquidContentAsync(content, "hook:" + hookName, context)
            .AsTask().GetAwaiter().GetResult();
        return string.IsNullOrEmpty(output)
            ? throw new InvalidOperationException($"Liquid 渲染钩子模板 {hookName} 未产出内容")
            : output;
    }

    private static Func<IReadOnlyDictionary<string, object>, string>? TryLoad(
        string markupDir, string hookName, ScribanTemplateRenderer renderer)
    {
        // 模板名经 renderer 的路径解析命中 <hookName>.html
        if (!File.Exists(Path.Combine(markupDir, hookName + ".html")))
        {
            return null;
        }

        return vars => renderer.RenderHookTemplate("_markup/" + hookName, vars)
            ?? throw new InvalidOperationException($"渲染钩子模板 {hookName} 未产出内容");
    }
}

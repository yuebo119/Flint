// Flint 静态站点生成器
// 模板资源提供者与 resources.*/css.*/js.*/images.* 命名空间（Hugo Pipes 子集）
//
// 覆盖 Hugo 资源管线的高频链路：
//   resources.Get/GetMatch/Match/ByType → 从 assets/ 读取
//   resources.FromString/Concat        → 虚拟资源构造
//   resources.Minify/Fingerprint       → 变换
//   css.Build / css.Sass               → 样式处理（复用 AssetPipeline 的 Sass 能力）
//   js.Build                           → 脚本处理（复用 JavaScriptBundler）
//
// 生成产物（Concat/FromString 的结果）需落盘到 public/，否则 RelPermalink 404——
// 由 ScribanTemplateRenderer.GeneratedResources 收集，SiteBuilder 输出阶段写入。
//
// IL2026/IL3050：同 BuiltinTemplateFunctions——Import 目标是本文件显式 lambda，
// 文件级压制（disable/restore 配对）
#pragma warning disable IL2026, IL3050

using System.Text.RegularExpressions;
using Scriban.Runtime;

namespace Flint.Core.Templates;

/// <summary>模板资源提供者（同步 IO，供 Scriban 函数直接调用）</summary>
public interface ITemplateResourceProvider
{
    /// <summary>按路径取资源（相对 assets/，如 "css/main.css"）；不存在返回 null</summary>
    TemplateResource? Get(string path);

    /// <summary>按路径读**原始字节**（位图图像操作的直通载荷）；不存在返回 null</summary>
    ReadOnlyMemory<byte>? ReadBytes(string path);

    /// <summary>glob 匹配（Hugo resources.Match 语义，支持 ** 与 *）</summary>
    IReadOnlyList<TemplateResource> Match(string pattern);

    /// <summary>按资源类型列出（css/js/image/...）</summary>
    IReadOnlyList<TemplateResource> ByType(string resourceType);

    /// <summary>站点基址（用于 Permalink）</summary>
    string BaseUrl { get; }
}

/// <summary>
/// 文件系统资源提供者：站点 assets/ 优先，主题 assets/ 按序回退
/// （与 SiteBuilder.ProcessAssetsAsync 的站点优先语义一致）。
/// 首次访问时惰性扫描并按扩展名过滤，避免全量读盘。
/// </summary>
public sealed class FileSystemResourceProvider : ITemplateResourceProvider
{
    private readonly string[] _roots;
    private readonly Dictionary<string, string> _index =
        new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _indexed;

    // 索引是**惰性**构建的，而页面渲染是并行的：首个访问 resources.get/Match 的
    // 页面会触发扫描，多线程同时进入会并发写 Dictionary →
    // "Operations that change non-concurrent collections must have exclusive access"
    //（DoIt 主题 22 处实测，触发点 resources.get）。双重检查加锁
    private readonly Lock _indexGate = new();

    public FileSystemResourceProvider(string baseUrl, params string[] assetRoots)
    {
        BaseUrl = baseUrl.TrimEnd('/');
        _roots = assetRoots.Where(r => !string.IsNullOrEmpty(r)).ToArray();
    }

    public string BaseUrl { get; }

    /// <summary>扫描全部资源根建索引（站点根优先，后写不覆盖）</summary>
    private void EnsureIndexed()
    {
        if (_indexed)
        {
            return;
        }

        lock (_indexGate)
        {
            if (_indexed)
            {
                return;
            }
            BuildIndex();
            _indexed = true;
        }
    }

    private void BuildIndex()
    {
        foreach (var root in _roots.AsEnumerable().Reverse())
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                _index[rel] = file;
            }
        }

        _indexed = true;
    }

    public TemplateResource? Get(string path)
    {
        EnsureIndexed();
        var key = path.Replace('\\', '/').TrimStart('/');
        if (!_index.TryGetValue(key, out var full))
        {
            return null;
        }

        return Load(key, full);
    }

    /// <summary>读原始字节（位图直通载荷；站点 assets/ 优先语义与 Get 一致）</summary>
    public ReadOnlyMemory<byte>? ReadBytes(string path)
    {
        EnsureIndexed();
        var key = path.Replace('\\', '/').TrimStart('/');
        if (!_index.TryGetValue(key, out var full))
        {
            return null;
        }

        try
        {
            return File.ReadAllBytes(full);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public IReadOnlyList<TemplateResource> Match(string pattern)
    {
        EnsureIndexed();
        var rx = GlobToRegex(pattern);
        var list = new List<TemplateResource>();
        foreach (var (rel, full) in _index)
        {
            if (rx.IsMatch(rel))
            {
                var r = Load(rel, full);
                if (r is not null)
                {
                    list.Add(r);
                }
            }
        }
        list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return list;
    }

    public IReadOnlyList<TemplateResource> ByType(string resourceType)
    {
        EnsureIndexed();
        var list = new List<TemplateResource>();
        foreach (var (rel, full) in _index)
        {
            var r = Load(rel, full);
            if (r is not null && r.ResourceType.Equals(resourceType, StringComparison.OrdinalIgnoreCase))
            {
                list.Add(r);
            }
        }
        list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return list;
    }

    private TemplateResource? Load(string rel, string full)
    {
        try
        {
            var res = TemplateResource.Create(rel, "", BaseUrl);
            // 位图是二进制：不读文本内容（仅给路径与类型）
            // **SVG 例外**：它是文本资源，主题靠 `.Content` 取符号表做图标内联
            //（monochrome 的 svg/feather.html：`resources.Get "lib/icns/…svg"` +
            //  `findRESubmatch` 抽 `<symbol>` → 内联成 <line>/<path>；此前 SVG 被判为
            //  图像 → Content 恒空 → 全部图标渲染成空 <svg>，实测）
            if (res.IsImage && !rel.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                return res;
            }

            var text = File.ReadAllText(full);
            return TemplateResource.Create(rel, text, BaseUrl);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Hugo glob → 正则（** 跨目录，* 限单段，? 单字符）</summary>
    internal static Regex GlobToRegex(string pattern)
    {
        var p = pattern.Replace('\\', '/').TrimStart('/');
        var sb = new System.Text.StringBuilder("^");
        for (var i = 0; i < p.Length; i++)
        {
            var c = p[i];
            switch (c)
            {
                case '*' when i + 1 < p.Length && p[i + 1] == '*':
                    sb.Append(".*");
                    i++;
                    break;
                case '*':
                    sb.Append("[^/]*");
                    break;
                case '?':
                    sb.Append("[^/]");
                    break;
                default:
                    sb.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}

public sealed partial class BuiltinTemplateFunctions
{
    private readonly ITemplateResourceProvider? _resources;
    private readonly List<TemplateResource> _generated = [];

    // _generated 的写入发生在**并行渲染**期间（各页的模板函数各自 Track 产物），
    // 而 List 非线程安全——并发 RemoveAll/Add 会抛 "Operations that change
    // non-concurrent collections must have exclusive access"（DoIt 主题 22 处实测，
    // 触发点：plugin/fontawesome.html 生成资源）。加锁保护写入；
    // 读取（GeneratedResources）在渲染完成后单线程进行，无需锁
    private readonly Lock _generatedGate = new();

    /// <summary>本实例产生的模板资源产物（Concat/FromString 结果），供输出阶段落盘</summary>
    public IReadOnlyList<TemplateResource> GeneratedResources => _generated;

    /// <summary>
    /// 为命名空间对象补小写/下划线形态键（Scriban 成员查找大小写敏感）。
    /// Hugo 官方写法是 PascalCase（resources.Get），但旧模板/惯用写法是小写（resources.get）
    /// </summary>
    private static void AddLowercaseAliases(ScriptObject obj)
    {
        foreach (var key in obj.Keys.OfType<string>().ToList())
        {
            if (key.Length == 0 || !char.IsUpper(key[0]))
            {
                continue;
            }
            var lowerFirst = char.ToLowerInvariant(key[0]) + key[1..];
            if (!obj.ContainsKey(lowerFirst))
            {
                obj.TrySetValue(null, default, lowerFirst, obj[key], readOnly: true);
            }
            var snake = ToSnakeCase(key);
            if (snake != key && !obj.ContainsKey(snake))
            {
                obj.TrySetValue(null, default, snake, obj[key], readOnly: true);
            }
            // 全小写（GetMatch → getmatch）：迁移器把 Hugo 的 `resources.GetMatch`
            // 统一降为无分隔小写（`getmatch`），只注册 getMatch/get_match 会漏
            //（Stack 的 helper/icon.html 实测 "The function `resources.getmatch` was not found"）
            var flat = key.ToLowerInvariant();
            if (flat != key && flat != lowerFirst && flat != snake && !obj.ContainsKey(flat))
            {
                obj.TrySetValue(null, default, flat, obj[key], readOnly: true);
            }
        }
    }

    private static string ToSnakeCase(string name)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
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

    /// <summary>
    /// 注册 resources.* / css.* / js.* / images.* 命名空间。
    /// 无资源提供者时注册为空对象（模板访问得 null 而非崩溃）。
    /// </summary>
    internal void RegisterResourceNamespaces(ScriptObject root)
    {
        var res = new ScriptObject();
        var css = new ScriptObject();
        var js = new ScriptObject();
        var images = new ScriptObject();

        if (_resources is not null)
        {
            RegisterResourceFunctions(res, images);
        }

        RegisterCssFunctions(css);
        RegisterJsFunctions(js);

        AddLowercaseAliases(res);
        // 全局 minify / toCSS：Hugo 0.128 之前的顶层形态（`$x | minify`、
        // `$scss | toCSS`），Stack 主题仍在使用。语义分别等同 resources.Minify
        // 与 css.Sass（toCSS 只把引用名改为 .css，实际编译由构建期 AssetPipeline
        // 完成——与 css.Sass 同一策略）
        if (res.ContainsKey("Minify"))
        {
            root.TrySetValue(null, default, "minify", res["Minify"], readOnly: true);
        }
        if (css.ContainsKey("Sass"))
        {
            root.TrySetValue(null, default, "toCSS", css["Sass"], readOnly: true);
            root.TrySetValue(null, default, "tocss", css["Sass"], readOnly: true);
        }
        root.TrySetValue(null, default, "resources", res, readOnly: true);
        root.TrySetValue(null, default, "css", css, readOnly: true);
        root.TrySetValue(null, default, "js", js, readOnly: true);
        root.TrySetValue(null, default, "images", images, readOnly: true);
    }

    private void RegisterResourceFunctions(ScriptObject res, ScriptObject images)
    {
        var provider = _resources!;

        res.Import("Get", (string? path) =>
        {
            var r = string.IsNullOrEmpty(path) ? null : provider.Get(path);
            return r?.ToScriptObject();
        });
        res.Import("GetMatch", (string? pattern) =>
        {
            var hits = string.IsNullOrEmpty(pattern) ? [] : provider.Match(pattern);
            return hits.Count > 0 ? hits[0].ToScriptObject() : null;
        });
        res.Import("Match", (string? pattern) =>
        {
            var arr = new ScriptArray();
            foreach (var r in string.IsNullOrEmpty(pattern) ? [] : provider.Match(pattern))
            {
                arr.Add(r.ToScriptObject());
            }
            return arr;
        });
        res.Import("ByType", (string? type) =>
        {
            var arr = new ScriptArray();
            foreach (var r in string.IsNullOrEmpty(type) ? [] : provider.ByType(type))
            {
                arr.Add(r.ToScriptObject());
            }
            return arr;
        });

        // GetRemote URL：Hugo 会联网下载远端资源并缓存；Flint 面向**可复现的离线构建**，
        // 不做网络抓取，返回 nil（主题的 `with $remote` 分支自然跳过）。
        // 已知限制：依赖远端资源的页面在 Flint 侧缺该资源——clarity 的
        // partials/image.html 对 http 开头的图片走此路径（1 处 TEMPLATE001）。
        // 宁可显式返回空值也不去联网：构建结果不应依赖外部服务可达性
        res.Import("GetRemote", (Func<object?[], object?>)(_ => null));

        // FromString NAME CONTENT：虚拟资源
        res.Import("FromString", (string? name, string? content) =>
        {
            // 空白名不是合法资源名：管道左值错位时（`" " | FromString "x.css"`）
            // 会传成 name=" " → RelPermalink "/assets/ " → 输出路径 "assets/ "，
            // 该路径无文件名 → 写盘必失败。此处按"名无效即不产出资源"处理，
            // 避免把模板参数错位升级成构建失败
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }
            var r = TemplateResource.Create(name, content ?? "", provider.BaseUrl);
            Track(r);
            return r.ToScriptObject();
        });

        // Concat NAME RESOURCES...
        // Concat TARGETPATH RESOURCES...：Hugo 的目标路径在前、资源在后，管道形态
        // `X | resources.Concat "p"` 的左值应作末参。若调用方顺序错位（左值落到首参），
        // 首参会是资源对象而非字符串——此时按"无显式目标路径"处理并把它当作资源，
        // 避免产出路径为对象 dump 的非法资源（ananke 实测：rel_permalink 变成
        // `/assets/{name: "a.css", ...}` 这种超长非法路径）
        res.Import("Concat", (params object?[] args) =>
        {
            string target;
            int itemStart;
            if (args.Length > 0 && args[0] is string s0 && s0.Length > 0)
            {
                target = s0;
                itemStart = 1;
            }
            else
            {
                target = "concat.css";
                itemStart = 0;
            }

            var sb = new System.Text.StringBuilder();
            foreach (var it in args.Skip(itemStart))
            {
                var r = TemplateResource.FromScriptObject(it);
                if (r is not null)
                {
                    sb.Append(r.Content);
                }
                else if (it is System.Collections.IEnumerable en and not string)
                {
                    foreach (var inner in en)
                    {
                        sb.Append(TemplateResource.FromScriptObject(inner)?.Content ?? "");
                    }
                }
            }
            var result = TemplateResource.Create(target, sb.ToString(), provider.BaseUrl);
            Track(result);
            return result.ToScriptObject();
        });

        res.Import("Minify", (object? value) =>
        {
            var r = ToResource(value);
            if (r is null)
            {
                return null;
            }
            var minified = r.ResourceType switch
            {
                "css" => MinifyCss(r.Content),
                "js" => MinifyJs(r.Content),
                "html" => MinifyHtml(r.Content),
                _ => r.Content
            };
            var outRes = r.With(minified);
            Track(outRes);
            return outRes.ToScriptObject();
        });

        res.Import("Fingerprint", (object? value, params object[] opts) =>
        {
            var r = ToResource(value);
            if (r is null)
            {
                return null;
            }
            var algorithm = opts.Length > 0 ? opts[0]?.ToString() ?? "sha256" : "sha256";
            var fp = r.WithFingerprint(algorithm);
            Track(fp);
            return fp.ToScriptObject();
        });

        res.Import("Copy", (string? target, object? value) =>
        {
            var r = ToResource(value);
            if (r is null || string.IsNullOrEmpty(target))
            {
                return null;
            }
            var renamed = TemplateResource.Create(target, r.Content, provider.BaseUrl);
            Track(renamed);
            return renamed.ToScriptObject();
        });

        res.Import("ExecuteAsTemplate", (params object?[] args) =>
        {
            // Hugo 的签名是 `ExecuteAsTemplate TARGETPATH DATA RESOURCE`
            //（管道形态把资源注入末位）。早期实现只取**首参**当资源——而首参是
            // 目标路径字符串 → ToResource 得 null → 整条 `| resources.Minify
            // | resources.Fingerprint` 链塌成 null（hugo-book 的 $searchJS 实测：
            // partial 上下文为 null，全站页头报错）。此处按"找资源 + 找路径"解析，
            // 并把资源改名到目标路径使 RelPermalink 与 Hugo 一致。
            //
            // Hugo 语义是**模板执行**：资产内容里的动作（narrow 的 theme-init.js
            // 取 `{{ site.Params.colorScheme | default "shadcn" }}`）会被渲染。
            // 资产文件不经迁移器（assets 不是 layouts），仍是 Go 语法——其中
            // 简单动作（成员访问/管道/default）与 Scriban 同构，直接按 Scriban
            // 渲染；解析或执行失败则回退原文（与 toCSS 的直通策略一致）
            TemplateResource? src = null;
            string? target = null;
            foreach (var a in args)
            {
                // (char)10 = 换行：目标路径是单行字符串（用码点避免源码转义层级）
                if (a is string str && target is null && str.Length > 0 &&
                    !str.Contains((char)10, StringComparison.Ordinal))
                {
                    target = str;
                    continue;
                }
                var r = a switch
                {
                    TemplateResource tr => tr,
                    ScriptObject o when o.ContainsKey("resource_type") => TemplateResource.FromScriptObject(o),
                    _ => null
                };
                if (r is not null)
                {
                    src = r;
                }
            }
            if (src is null)
            {
                return null;
            }

            // S3 反向验证否决"按 Scriban 渲染资产"：narrow 的简单动作渲染成功
            //（colorScheme 取到 shadcn），但 hugo-book 的复杂 Go 模板（range/if
            // 语义与 Scriban 不同）被错误执行——分数 55.6/85.4 → 26.8/67.7。
            // 资产文件不经迁移器转换，保持**直通**；资产内 Go 模板动作不执行
            // 登记为有意差异（正确解法是迁移器扩到 assets，属后续专项）
            var renamed = TemplateResource.Create(target ?? src.Name, src.Content, _resources?.BaseUrl ?? "");
            Track(renamed);
            return renamed.ToScriptObject();
        });

        res.Import("Publish", (object? value) =>
        {
            var r = ToResource(value);
            if (r is null)
            {
                return null;
            }
            Track(r);
            return r.ToScriptObject();
        });

        // 图像：尺寸变换（Flint 的图像变换在构建期由 ImageProcessor 处理，
        // 模板侧提供元数据与 URL；实际像素处理走 assets 管线）
        foreach (var op in ImageOps)
        {
            var opName = op;
            res.Import(opName, (object? value, string? spec) => ApplyImageOp(ToResource(value), opName, spec));
        }

        // 图像变换方法族同时挂到**资源对象自身**上：主题写 `$img.Fill "600x600"`、
        // `$img.Resize "x300"`（Blowfish 的 `$authorImage.Fill` 实测 1580 处报
        // "The function `$authorImage.Fill` was not found"——命名空间级
        // resources.* 之外还需成员级）。Scriban 成员查找大小写敏感，双形态注册
        TemplateResource.AttachImageOps = (obj, resource) =>
        {
            foreach (var op in ImageOps)
            {
                var opName = op;
                obj.Import(opName, (object? spec) => ApplyImageOp(resource, opName, spec as string));
                obj.Import(opName.ToLowerInvariant(),
                    (object? spec) => ApplyImageOp(resource, opName, spec as string));
            }
        };

        images.Import("Config", () => new ScriptObject());
    }

    /// <summary>Hugo 的图像尺寸变换方法名（命名空间级与资源成员级共用）</summary>
    private static readonly string[] ImageOps = ["Resize", "Fit", "Fill", "Crop", "Process"];

    /// <summary>
    /// 图像变换：Flint 的像素处理在构建期由 ImageProcessor 处理，模板侧产出
    /// 确定性命名（{name}_{op}_{spec}.{ext}）的兄弟资源并登记落盘
    /// （命名空间级 <c>resources.Fill</c> 与资源成员级 <c>$img.Fill</c> 共用）
    /// </summary>
    private object? ApplyImageOp(TemplateResource? r, string opName, string? spec)
    {
        if (r is null)
        {
            return null;
        }
        var (w, h) = ParseImageSpec(spec);
        // (char)92 = 反斜杠：用码点写法避免源码里的转义层级（同 ScribanTemplateRenderer）
        var dir = Path.GetDirectoryName(r.Name)?.Replace((char)92, '/') ?? "";
        var file = Path.GetFileNameWithoutExtension(r.Name);
        var ext = Path.GetExtension(r.Name);
        // 规格串会进 URL（Hugo 用 _hu_<hash> 命名；Flint 用 op_spec 确定性命名）——
        // 空格（"70x70 center webp"）进 src 会成为未编码 URL，替换为下划线
        var suffix = string.IsNullOrEmpty(spec)
            ? opName.ToLowerInvariant()
            : opName.ToLowerInvariant() + "_" + spec.Replace(' ', '_');
        var newName = (dir.Length > 0 ? dir + "/" : "") + file + "_" + suffix + ext;
        var baseRes = TemplateResource.Create(newName, r.Content, _resources?.BaseUrl ?? "");
        var outRes = new TemplateResource
        {
            Name = baseRes.Name,
            Title = baseRes.Title,
            ResourceType = "image",
            MediaType = baseRes.MediaType,
            Content = baseRes.Content,
            // 位图在装载期不读文本（Content 为空）——取**原图字节**直通到输出，
            // 否则 SiteBuilder 按"空内容"跳过 → 引用了产物却无文件（blog-awesome 的
            // bio 头像实测断链）；尺寸变换本身为直通（与模板级 toCSS 同策略）
            BinaryContent = r.Content.Length == 0
                ? _resources?.ReadBytes(r.Name)
                : null,
            RelPermalink = baseRes.RelPermalink,
            Permalink = baseRes.Permalink,
            Width = w > 0 ? w : r.Width,
            Height = h > 0 ? h : r.Height
        };
        Track(outRes);
        return outRes.ToScriptObject();
    }

    private void RegisterCssFunctions(ScriptObject css)
    {
        // css.Build：Hugo 的 CSS 构建（@import 内联 + 可选 minify）
        css.Import("Build", (params object?[] args) =>
        {
            var r = FindResourceArg(args);
            if (r is null)
            {
                return null;
            }
            var minify = OptsFlag(args.OfType<object>().ToArray(), "minify", defaultValue: true);
            var content = r.Content;
            if (minify)
            {
                content = MinifyCss(content);
            }
            return r.With(content).ToScriptObject();
        });

        css.Import("Sass", (params object?[] args) =>
        {
            // Hugo 的 toCSS 会真正编译 SCSS；Flint 的 DartSassHost 在 **AOT 发布**下
            // 初始化即抛"Reflection-based serialization has been disabled"（其内部
            // 依赖反射 JSON），无法在模板级编译。此处按 Hugo 的 OPTIONS 语义改写
            // 目标路径（targetPath 优先，否则 .scss/.sass → .css），内容直通：
            // 样式由主题自带编译产物或构建期管线提供（差异登记 §三）
            var r = FindResourceArg(args);
            if (r is null)
            {
                return null;
            }

            ScriptObject? options = args.FirstOrDefault(a =>
                a is ScriptObject so && so.ContainsKey("targetPath")) as ScriptObject;
            var targetPath = options?.TryGetValue(null, default, "targetPath", out var targetValue) == true
                ? targetValue?.ToString()
                : null;

            var css = r.Content;
            var target = targetPath;
            if (string.IsNullOrEmpty(target))
            {
                var dir = Path.GetDirectoryName(r.Name)?.Replace((char)92, '/') ?? "";
                var file = Path.GetFileNameWithoutExtension(r.Name);
                target = (dir.Length > 0 ? dir + "/" : "") + file + ".css";
            }

            // **Track 落盘**：不 Track 则 rel_permalink 指向的文件不会被写出
            //（loveit 的 /css/style.min.css ×15 页面引用 404，实测）
            var produced = TemplateResource.Create(target, css, "");
            Track(produced);
            return produced.ToScriptObject();
        });

        // PostCSS/TailwindCSS 与 js.Babel/Batch 在 Flint 里是**恒等**（无对应工具链），
        // 但要返回**资源实参本身**而非首个实参——否则显式参数序（资源在末位）下
        // 返回的是选项字典，主题拿到 `{{ $css.RelPermalink }}` 就取不到值
        css.Import("PostCSS", (params object?[] args) => FindResourceArg(args, args.FirstOrDefault()));
        css.Import("TailwindCSS", (params object?[] args) => FindResourceArg(args, args.FirstOrDefault()));
        css.Import("Quoted", (string? s) => "\"" + s + "\"");
        css.Import("Unquoted", (string? s) => s ?? "");
        css.Import("ChromaStyles", () => new ScriptObject());
    }

    private void RegisterJsFunctions(ScriptObject js)
    {
        // js.Build：Hugo 的签名是 `js.Build [OPTIONS] INPUT`——**输入在末位**，
        // 故 `$res | js.Build $opts` 展开为 (opts, res)。此前实现声明
        // `(string? path, params object[] opts)`：管道注入的资源对象被 Scriban
        // 转成字符串当成了"路径"，产出 `RelPermalink = "/assets/{name: …整个对象转储…}"`
        // 的畸形资源（fixit 30 处、narrow 54 处、stack 17 处、monochrome 15 处实测
        // 页面里出现 `href="/assets/{name: "js/main.ts", …}"` → JS/CSS 全都加载不到）。
        //
        // Flint 没有 esbuild：语义是**透传**（内容原样、文件名按 targetPath 改名、
        // 可选 MinifyJs），产物在输出阶段写盘
        js.Import("Build", (params object?[] args) =>
        {
            if (args.Length == 0)
            {
                return null;
            }

            // **两种参数序都要认**：Hugo 是"输入在末位"（`$res | js.Build $opts`），
            // 而 Scriban 的管道把左值注入**首参**——同一份主题写法在两条路径下参数序
            // 相反。故不按位置、按**类型**定位输入（资源对象优先，其次字符串路径），
            // 其余实参即选项
            var resource = (TemplateResource?)null;
            var name = (string?)null;
            var content = "";
            var optionArgs = new List<object?>();
            foreach (var a in args)
            {
                if (resource is null && a is not string && TemplateResource.FromScriptObject(a) is { } r)
                {
                    resource = r;
                    name = r.Name;
                    content = r.Content;
                    continue;
                }
                optionArgs.Add(a);
            }
            if (resource is null)
            {
                // 路径形态：取最后一个字符串实参当输入（Hugo 亦接受相对 assets/ 的路径）
                for (var i = optionArgs.Count - 1; i >= 0; i--)
                {
                    if (optionArgs[i] is string p && p.Length > 0)
                    {
                        name = p;
                        content = _resources?.Get(p)?.Content ?? "";
                        optionArgs.RemoveAt(i);
                        break;
                    }
                }
            }
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            // targetPath 优先（Hugo 用它决定产物文件名）；旧式 `js.Build "out.js"` 的
            // 字符串实参同义
            var target = OptsString([.. optionArgs], "targetPath");
            if (string.IsNullOrEmpty(target))
            {
                foreach (var a in optionArgs)
                {
                    if (a is string legacyTarget && legacyTarget.Length > 0)
                    {
                        target = legacyTarget;
                        break;
                    }
                }
            }
            if (!string.IsNullOrEmpty(target))
            {
                name = target;
            }

            if (OptsFlag(optionArgs.OfType<object>().ToArray(), "minify", defaultValue: false))
            {
                content = MinifyJs(content);
            }

            var outRes = TemplateResource.Create(name, content, "");
            Track(outRes);
            return outRes.ToScriptObject();
        });
        js.Import("Babel", (params object?[] args) => (object?)FindResourceArg(args) ?? args.FirstOrDefault());
        js.Import("Batch", (params object?[] args) => (object?)FindResourceArg(args) ?? args.FirstOrDefault());
    }

    /// <summary>
    /// 在实参里按**类型**定位资源（不按位置）：Hugo 的管道语义把输入放末位，
    /// 而 Scriban 把左值注入首参，两条路径的参数序相反，按类型找才能都认
    /// </summary>
    private static TemplateResource? FindResourceArg(object?[] args)
    {
        foreach (var a in args)
        {
            if (a is not string && TemplateResource.FromScriptObject(a) is { } r)
            {
                return r;
            }
        }
        return null;
    }

    private static object? FindResourceArg(object?[] args, object? fallback) =>
        FindResourceArg(args)?.ToScriptObject() ?? fallback;

    /// <summary>选项字典里的字符串值（dict "targetPath" "a.js"）</summary>
    private static string? OptsString(object?[] opts, string key)
    {
        foreach (var o in opts)
        {
            if (o is ScriptObject so && so.ContainsKey(key))
            {
                return so[key]?.ToString();
            }
        }
        return null;
    }

    /// <summary>记录产物（按 RelPermalink 去重，保留最新）</summary>
    private void Track(TemplateResource r)
    {
        // RelPermalink 必须指向**具名文件**（"…/" 这类目录形态无文件名，
        // 落到输出阶段会写盘失败，把模板参数错位升级成构建失败）
        if (string.IsNullOrEmpty(r.RelPermalink) || Path.GetFileName(r.RelPermalink).Length == 0)
        {
            return;
        }
        lock (_generatedGate)
        {
            _generated.RemoveAll(x => x.RelPermalink == r.RelPermalink);
            _generated.Add(r);
        }
    }

    private TemplateResource? ToResource(object? value) => TemplateResource.FromScriptObject(value);

    private static (int Width, int Height) ParseImageSpec(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
        {
            return (0, 0);
        }
        var m = Regex.Match(spec, @"(\d+)?x(\d+)");
        if (!m.Success)
        {
            return int.TryParse(spec, out var w) ? (w, 0) : (0, 0);
        }
        var width = m.Groups[1].Success ? int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
        var height = int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        return (width, height);
    }

    private static bool OptsFlag(object[] opts, string key, bool defaultValue)
    {
        // Hugo 选项字典（dict "minify" true）
        foreach (var o in opts)
        {
            if (o is ScriptObject so && so.ContainsKey(key))
            {
                return so[key] is bool b ? b : so[key]?.ToString() == "true";
            }
        }
        return defaultValue;
    }

    internal static string MinifyCss(string css)
    {
        if (string.IsNullOrEmpty(css))
        {
            return "";
        }
        var s = Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);
        s = Regex.Replace(s, @"\s+", " ");
        s = Regex.Replace(s, @"\s*([{};:,>])\s*", "$1");
        return s.Trim();
    }

    internal static string MinifyJs(string js)
    {
        if (string.IsNullOrEmpty(js))
        {
            return "";
        }
        var s = Regex.Replace(js, @"/\*.*?\*/", "", RegexOptions.Singleline);
        s = Regex.Replace(s, @"^\s*//.*$", "", RegexOptions.Multiline);
        s = Regex.Replace(s, @"\s+", " ");
        return s.Trim();
    }

    internal static string MinifyHtml(string html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return "";
        }
        var s = Regex.Replace(html, @">\s+<", "><");
        s = Regex.Replace(s, @"\s{2,}", " ");
        return s.Trim();
    }
}

#pragma warning restore IL2026, IL3050

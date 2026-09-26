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

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
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

    /// <summary>资源根集合（ES module 打包的路径反查用）</summary>
    internal IReadOnlyList<string> Roots => _roots;

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
                return res.WithSourcePath(full);
            }

            var text = File.ReadAllText(full);
            return TemplateResource.Create(rel, text, BaseUrl).WithSourcePath(full);
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
            var joined = sb.ToString();
            var result = CachedByKey(
                $"concat|{target}|{joined.Length}:{ContentDigest(joined)}",
                () => TemplateResource.Create(target, joined, provider.BaseUrl));
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
            var outRes = CachedTransform("minify", r.ResourceType, r, () =>
                r.With(r.ResourceType switch
                {
                    "css" => MinifyCss(r.Content),
                    "js" => MinifyJs(r.Content),
                    "html" => MinifyHtml(r.Content),
                    _ => r.Content
                }));
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
            var fp = CachedTransform("fingerprint", algorithm, r, () => r.WithFingerprint(algorithm));
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

        res.Import("ExecuteAsTemplate", (Scriban.TemplateContext? ctx, object?[] args) =>
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

            // 渲染语义（S3 复验后的最终形态）：迁移器现已**转换** assets 内含
            // Go 动作的文件（解析干净且有动作才转换，否则原样复制）——此处对
            // **解析干净的 Scriban** 执行渲染，失败回退原文。此前两版：
            // ① 完全直通（资产动作泄漏到产物）；② 对未经转换的 Go 语法直接
            // Scriban 渲染（复杂 range/if 语义不同，hugo-book 崩到 26.8）——均废
            var content = src.Content;
            if (content.Contains("{{", StringComparison.Ordinal) && ctx is not null)
            {
                try
                {
                    var parsed = Scriban.Template.Parse(content, target ?? src.Name);
                    if (!parsed.HasErrors)
                    {
                        // **输出缓冲隔离**（同 RenderPartialWithType）：Scriban 的
                        // Render 会把内容写进 context.Output——不隔离的话整段 JS
                        // 会被复制进调用页的 HTML（narrow 的 TOC 页实测：extra 元素
                        // 激增、结构相似度 41.1 → 23.4）
                        ctx.PushOutput();
                        try
                        {
                            var rendered = parsed.Render(ctx);
                            if (!string.IsNullOrEmpty(rendered))
                            {
                                content = rendered;
                            }
                        }
                        finally
                        {
                            ctx.PopOutput();
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // 渲染失败回退原文
                }
            }

            // **SourcePath 继承**：ExecuteAsTemplate → toCSS 链上，后续 Sass 编译
            // 依赖磁盘源路径解析 @import（m10c/clarity/hugo-coder 的
            // ExecuteAsTemplate→toCSS 链实测：断链后 toCSS 跳过编译，SCSS 原文直出）
            var renamed = TemplateResource.Create(target ?? src.Name, content, _resources?.BaseUrl ?? "")
                .WithSourcePath(src.SourcePath ?? "");
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
            var outRes = CachedTransform("css.build", minify ? "minify" : "raw", r, () =>
            {
                var content = r.Content;
                if (minify)
                {
                    content = MinifyCss(content);
                }
                return r.With(content);
            });
            return outRes.ToScriptObject();
        });

        css.Import("Sass", (params object?[] args) =>
        {
            // Hugo 的 toCSS 会真正编译 SCSS。Flint 的 DartSassHost 在 **裁剪发布**下
            // 初始化即抛"Reflection-based serialization has been disabled"，故改为
            // 调用**外部 Dart Sass 可执行文件**（定位顺序：环境变量 FLINT_SASS →
            // PATH 上的 sass → 进程目录向上找 tools/dart-sass/sass.bat）。
            // 编译必须**按磁盘文件**进行（@import 相对解析依赖文件位置）；
            // 找不到 sass 或编译失败时回退为内容直通（差异登记 §三）。
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

            // **hugo:vars 虚拟导入的变量源**（Hugo css.Sass 的 vars 选项）：主题把
            // 变量字典随 toCSS Options 传入，SCSS 里 `@forward "hugo:vars"` /
            // `"hugo:vars/internal"` 取用（fixit 的 scss-vars.html → _variables.scss）。
            // 键名兼容 vars / vars_internal / varsInternal（迁移器对 Pascal 键产出
            // 逐大写字母 snake 形）
            var varsMap = GetOptionMember(options, "vars") as ScriptObject;
            var varsInternal = GetOptionMember(options, "vars_internal")
                               ?? GetOptionMember(options, "varsInternal");

            var css = r.Content;
            var ext = r.SourcePath is { } sp ? Path.GetExtension(sp) : null;
            var sassCompilable = File.Exists(r.SourcePath) &&
                                 (ext == ".scss" || ext == ".sass" || ext == ".css");
            if (sassCompilable)
            {
                // 编译**渲染后的内容**；load-path 取源文件目录（@import 解析基准）；
                // .sass 走缩进语法（--indented）。**整段入缓存**：外部 Dart Sass
                // 进程单次 ~1-2s，每页重跑时 700 页必然超时
                var loadPath = Path.GetDirectoryName(r.SourcePath!);
                var indented = ext == ".sass";
                // 变量摘要进缓存键：同一 scss 不同 vars 的产出不可互相复用
                css = CachedText(
                    $"css.sass|{loadPath}|{indented}|{VarsDigest(varsMap, varsInternal)}|{css.Length}:{ContentDigest(css)}",
                    () => TryCompileWithSassCli(css, loadPath, indented: indented,
                        vars: varsMap, varsInternal: varsInternal) ?? css);
            }

            var target = targetPath;
            if (string.IsNullOrEmpty(target))
            {
                var dir = Path.GetDirectoryName(r.Name)?.Replace((char)92, '/') ?? "";
                var file = Path.GetFileNameWithoutExtension(r.Name);
                target = (dir.Length > 0 ? dir + "/" : "") + file + ".css";
            }

            // **Track 落盘**：不 Track 则 rel_permalink 指向的文件不会被写出
            //（loveit 的 /css/style.min.css ×15 页面引用 404，实测）
            // baseUrl 同 js.Build 取资源提供者真实值（子路径构建时 RelPermalink
            // 才带 /<主题>/ 前缀）——此前传空串，toCSS 产物全掉回站根
            var produced = TemplateResource.Create(target, css, _resources?.BaseUrl ?? "");
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

    /// <summary>sass 缩进语法的 tab→空格规范化（每个行首 tab 视作一级缩进 = 2 空格）</summary>
    private static string NormalizeSassIndent(string content)
    {
        if (!content.Contains('\t', StringComparison.Ordinal))
        {
            return content;
        }

        var sb = new System.Text.StringBuilder(content.Length);
        var lineStart = true;
        foreach (var c in content)
        {
            if (lineStart && c == '\t')
            {
                sb.Append("  ");
                continue;
            }
            sb.Append(c);
            lineStart = c is '\n' or '\r';
        }
        return sb.ToString();
    }

    /// <summary>Dart Sass 可执行文件的定位缓存（null = 已找过但没找到）</summary>
    private static string? _sassExePath;

    /// <summary>
    /// 定位 Dart Sass：环境变量 FLINT_SASS → 进程目录旁的 dart-sass*/sass.bat
    /// （CLI 发布布局：Flint.exe 与 dart-sass.win-x64/ 同级）→ PATH 上的 sass →
    /// 进程目录向上找 tools/dart-sass/sass.bat（仓库工具布局）
    /// </summary>
    private static string? LocateSassExecutable()
    {
        if (_sassExePath is not null)
        {
            return _sassExePath.Length > 0 ? _sassExePath : null;
        }

        var candidates = new List<string>();
        if (Environment.GetEnvironmentVariable("FLINT_SASS") is { Length: > 0 } env)
        {
            candidates.Add(env);
        }

        // **进程目录同级的 dart-sass*/sass.bat**：CLI 自包含发布把 Dart Sass
        // 运行时放在 Flint.exe 旁边（…/win-x64/dart-sass.win-x64/sass.bat）。
        // 此前只找 tools/dart-sass，发布布局下永远找不到 → toCSS 全部退化为
        // 内容直通（main.css 里剩 `@use "core"` 原样 SCSS，全站无样式——
        // fixit/loveit 实测）
        var procDir = AppContext.BaseDirectory;
        foreach (var sassDir in Directory.Exists(procDir)
                     ? Directory.EnumerateDirectories(procDir, "dart-sass*")
                     : Array.Empty<string>())
        {
            candidates.Add(Path.Combine(sassDir, "sass.bat"));
        }

        // 进程目录向上找仓库工具目录（Flint/src/Flint.Cli/bin/…/win-x64 → 上溯 8 层）
        var dir = new DirectoryInfo(procDir);
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            candidates.Add(Path.Combine(dir.FullName, "tools", "dart-sass", "sass.bat"));
            candidates.Add(Path.Combine(dir.FullName, "dart-sass.win-x64", "sass.bat"));
            dir = dir.Parent;
        }

        foreach (var c in candidates)
        {
            if (File.Exists(c))
            {
                _sassExePath = c;
                return c;
            }
        }

        // PATH 上的 sass（dart-sass 的 .bat/.exe 或类 Unix 的启动脚本）
        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        foreach (var pd in pathDirs)
        {
            foreach (var exe in new[] { "sass.bat", "sass.exe", "sass" })
            {
                var full = Path.Combine(pd.Trim(), exe);
                if (File.Exists(full))
                {
                    _sassExePath = full;
                    return full;
                }
            }
        }

        _sassExePath = "";
        return null;
    }

    /// <summary>
    /// 调用 Dart Sass 编译**内存中的 SCSS/Sass 内容**（stdin 模式 + load-path 解析 @import）。
    /// 必须编内容而不是磁盘文件：ExecuteAsTemplate → toCSS 链上磁盘文件含未渲染的
    /// Scriban 动作，按文件编译必失败（clarity/m10c 实测）。
    /// <paramref name="vars"/>/<paramref name="varsInternal"/> 是 Hugo css.Sass 的
    /// vars 选项：经镜像目录把 `@forward "hugo:vars"`（含 `hugo:vars/<子映射>`）
    /// 替换成生成的 `$k: v;` 声明——Hugo 用自定义 importer 提供该虚拟模块，CLI 版
    /// sass 没有 importer，且 `hugo:` 前缀含冒号不是合法 Windows 文件名，只能在
    /// 源码镜像上做替换（fixit 的 _variables.scss 实测）
    /// </summary>
    private static string? TryCompileWithSassCli(
        string content, string? loadPath, bool indented = false,
        ScriptObject? vars = null, object? varsInternal = null)
    {
        var sass = LocateSassExecutable();
        if (sass is null)
        {
            return null;
        }

        if (indented)
        {
            // **tab 缩进规范化为空格**：sass 缩进语法官方只允许空格，但 Hugo 的
            // Dart Sass 对上游主题的 tab 笔误（clarity 的 _components.sass 771-772
            // 行实测）宽容处理；sass CLI 1.104 直接报 "Expected spaces, was tabs"。
            // 为与 Hugo 产物一致，编译前把行首 tab 转成等宽空格
            content = NormalizeSassIndent(content);
        }

        // hugo:vars 数据（vars 顶层标量 + vars_internal 独立映射 + vars 内的子映射）
        var hugoVars = CollectHugoVars(vars, varsInternal);

        try
        {
            // .sass 缩进语法必须显式 --indented（stdin 模式无扩展名可推断，
            // 默认按 SCSS 解析会报 "expected ;"——clarity 的 main.sass 实测）
            var args = indented
                ? "--no-source-map --quiet --indented --stdin"
                : "--no-source-map --quiet --stdin";

            // **tab 规范化的镜像目录**：被 @import 的磁盘文件（clarity 的
            // _components.sass）里的 tab 同样会让 sass CLI 报错。stdin 只覆盖
            // 主文件，import 的文件必须走 --load-path——把源目录镜像到临时目录
            // 并规范化全部 .sass/.scss 的 tab 后作为 load-path
            string? mirrorDir = null;
            if (indented && !string.IsNullOrEmpty(loadPath))
            {
                mirrorDir = CreateTabNormalizedMirror(loadPath);
                if (mirrorDir is not null)
                {
                    loadPath = mirrorDir;
                }
            }

            // **hugo:vars 镜像**：stdin 主文件也被 @import 的磁盘文件同规则替换，
            // 内存内容里若有 hugo: 导入一并改写（用镜像目录承载替换后的磁盘文件）
            if (hugoVars.Count > 0 && !string.IsNullOrEmpty(loadPath))
            {
                var varsMirror = CreateHugoVarsMirror(loadPath, hugoVars);
                if (varsMirror is not null)
                {
                    if (mirrorDir is not null)
                    {
                        TryDeleteMirror(mirrorDir);
                    }
                    mirrorDir = varsMirror;
                    loadPath = varsMirror;
                }
                content = RewriteHugoVarsImports(content, hugoVars);
            }

            if (!string.IsNullOrEmpty(loadPath))
            {
                args += " --load-path=\"" + loadPath + "\"";
            }

            var psi = new ProcessStartInfo
            {
                FileName = sass,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = loadPath ?? ""
            };
            // .bat 必须经 cmd 解析（直接 spawn .bat 在部分 .NET 版本被禁）
            if (sass.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            {
                psi.FileName = "cmd.exe";
                psi.Arguments = "/c \"\"" + sass + "\" " + args + "\"";
            }

            string stdout, stderr;
            int exitCode;
            try
            {
                using var proc = Process.Start(psi)!;
                proc.StandardInput.Write(content);
                proc.StandardInput.Close();
                stdout = proc.StandardOutput.ReadToEnd();
                stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit(120_000);
                exitCode = proc.ExitCode;
            }
            finally
            {
                if (mirrorDir is not null)
                {
                    TryDeleteMirror(mirrorDir);
                }
            }

            if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            {
                if (Environment.GetEnvironmentVariable("FLINT_SASS_TRACE") == "1")
                {
                    Console.Error.WriteLine($"[sass] compile failed exit={exitCode} err={(stderr.Length > 400 ? stderr[..400] : stderr)}");
                }
                return null;
            }

            return stdout;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 把 sass 源目录镜像到临时目录并把全部 .sass/.scss 的行首 tab 规范化为空格
    /// （Hugo 的 Dart Sass 对上游 tab 笔误宽容，sass CLI 不容——clarity 实测）
    /// </summary>
    private static string? CreateTabNormalizedMirror(string sourceDir)
    {
        try
        {
            var needsMirror = Directory.EnumerateFiles(sourceDir, "*.s*ss", SearchOption.AllDirectories)
                .Any(f => File.ReadAllText(f).Contains('\t', StringComparison.Ordinal));
            if (!needsMirror)
            {
                return null;
            }

            var mirror = Path.Combine(Path.GetTempPath(), "flint-sass-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(mirror);
            foreach (var f in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(sourceDir, f);
                var dest = Path.Combine(mirror, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                if (f.EndsWith(".sass", StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith(".scss", StringComparison.OrdinalIgnoreCase))
                {
                    File.WriteAllText(dest, NormalizeSassIndent(File.ReadAllText(f)));
                }
                else
                {
                    File.Copy(f, dest, overwrite: true);
                }
            }
            return mirror;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void TryDeleteMirror(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 清理失败无碍正确性（临时目录）
        }
    }

    // ---- Hugo css.Sass 的 vars 选项（hugo:vars 虚拟导入）----

    /// <summary>从选项对象取成员（兼容 snake/Pascal 拼写）</summary>
    internal static object? GetOptionMember(ScriptObject? options, string snakeName)
    {
        if (options is null)
        {
            return null;
        }
        foreach (var candidate in new[] { snakeName, char.ToUpperInvariant(snakeName[0]) + snakeName[1..] })
        {
            if (options.TryGetValue(null, default, candidate, out var v) && v is not null)
            {
                return v;
            }
        }
        // 逐大写字母 snake 形（vars_internal 也可能是 varsInternal/VarsInternal）
        var pascal = string.Concat(snakeName.Split('_').Select(p => p.Length > 0
            ? char.ToUpperInvariant(p[0]) + p[1..] : p));
        return options.TryGetValue(null, default, pascal, out var pv) ? pv : null;
    }

    /// <summary>
    /// 汇总 hugo:vars 变量源：<c>""</c>（空键）= vars 的顶层**标量**项；
    /// <c>"internal"</c> 等子键 = vars 内对应**子映射**项；另有独立的
    /// vars_internal 选项合并进 "internal" 命名空间（Hugo 的 VarsInternal）
    /// </summary>
    internal static Dictionary<string, Dictionary<string, string?>> CollectHugoVars(
        ScriptObject? vars, object? varsInternal)
    {
        var result = new Dictionary<string, Dictionary<string, string?>>(StringComparer.OrdinalIgnoreCase);
        if (vars is not null)
        {
            foreach (var key in vars.Keys)
            {
                if (!vars.TryGetValue(null, default, key, out var v) || v is null)
                {
                    continue;
                }
                if (v is ScriptObject nested)
                {
                    result[key] = ScalarEntries(nested);
                }
                else
                {
                    if (!result.TryGetValue("", out var scalars))
                    {
                        scalars = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                        result[""] = scalars;
                    }
                    scalars[key] = v.ToString();
                }
            }
        }
        if (varsInternal is ScriptObject internalObj)
        {
            if (!result.TryGetValue("internal", out var internalScalars))
            {
                internalScalars = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                result["internal"] = internalScalars;
            }
            foreach (var (k, v) in ScalarEntries(internalObj))
            {
                internalScalars[k] = v;
            }
        }
        return result;
    }

    internal static Dictionary<string, string?> ScalarEntries(ScriptObject obj)
    {
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in obj.Keys)
        {
            if (!obj.TryGetValue(null, default, key, out var v) || v is null)
            {
                continue;
            }
            // 嵌套映射不再递归（SCSS 侧只用一层：hugo:vars/<子键>）
            dict[key] = v is ScriptObject ? null : v.ToString();
        }
        return dict;
    }

    /// <summary>变量源的缓存摘要（进 css.sass 缓存键）</summary>
    internal static string VarsDigest(ScriptObject? vars, object? varsInternal)
    {
        if (vars is null && varsInternal is null)
        {
            return "novars";
        }
        var flat = CollectHugoVars(vars, varsInternal);
        var sb = new System.Text.StringBuilder();
        foreach (var (ns, entries) in flat.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            sb.Append(ns).Append('{');
            foreach (var (k, v) in entries.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                sb.Append(k).Append('=').Append(v).Append(';');
            }
            sb.Append('}');
        }
        return sb.Length == 0 ? "novars" : sb.ToString();
    }

    /// <summary>命名空间 → <c>$k: v;</c> 声明块。</summary>
    /// <remarks>
    /// 值格式化对齐 Hugo 的 isTypedCSSValue：hex 颜色（#fff）、CSS 函数
    /// （rgba(…)）、数字+单位（0.875em/60px）、裸关键字（center/bold）原样输出；
    /// 其余（含空格的字体名、路径等）包双引号——直接输出 <c>$f: Noto Sans SC;</c>
    /// 会让 sass 报 "Expected expression"（fixit 的 vars 实测）
    /// </remarks>
    internal static string BuildVarsDeclarations(Dictionary<string, string?> entries)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (k, v) in entries)
        {
            if (v is null)
            {
                continue;
            }
            sb.Append('$').Append(k).Append(": ").Append(FormatSassValue(v)).Append(";\n");
        }
        return sb.ToString();
    }

    /// <summary>SCSS 变量值格式化（见 <see cref="BuildVarsDeclarations"/> 的规则说明）</summary>
    internal static string FormatSassValue(string value)
    {
        var v = value.Trim();
        if (v.Length == 0)
        {
            return "\"\"";
        }
        // 已有引号（单/双）→ 原样
        if ((v.StartsWith('"') && v.EndsWith('"')) || (v.StartsWith('\'') && v.EndsWith('\'')))
        {
            return v;
        }
        // hex 颜色
        if (v.StartsWith('#') && System.Text.RegularExpressions.Regex.IsMatch(v, "^#[0-9a-fA-F]{3,8}$"))
        {
            return v;
        }
        // CSS 函数：rgba(...) / var(...) / calc(...) / linear-gradient(...)
        if (System.Text.RegularExpressions.Regex.IsMatch(v, "^[a-zA-Z-]+\\(.*\\)$"))
        {
            return v;
        }
        // 数字 + 单位 / 纯数字
        if (System.Text.RegularExpressions.Regex.IsMatch(v, "^[+-]?(\\d+\\.?\\d*|\\.\\d+)([a-zA-Z%]*)$"))
        {
            return v;
        }
        // 裸关键字（无空白的标识符序列）
        if (System.Text.RegularExpressions.Regex.IsMatch(v, "^[a-zA-Z_][\\w-]*$"))
        {
            return v;
        }
        // 其余一律引号化（字体名/路径/复合值）；内部双引号转义
        return "\"" + v.Replace("\"", "\\\"") + "\"";
    }

    /// <summary>把源码内容里的 <c>@forward/@use "hugo:vars[/子键]"</c> 替换成变量声明</summary>
    internal static string RewriteHugoVarsImports(
        string content, Dictionary<string, Dictionary<string, string?>> vars)
    {
        if (!content.Contains("hugo:", StringComparison.Ordinal))
        {
            return content;
        }
        return System.Text.RegularExpressions.Regex.Replace(content,
            "@(forward|use)\\s+(\"[^\"]*hugo:vars[^\"]*\"|'[^']*hugo:vars[^']*')",
            m =>
            {
                var spec = m.Groups[2].Value.Trim('"', '\'');
                // "hugo:vars" → 顶层标量；"hugo:vars/internal" → 子映射
                var ns = spec.Length > "hugo:vars".Length && spec["hugo:vars".Length] == '/'
                    ? spec[("hugo:vars/".Length)..]
                    : "";
                vars.TryGetValue(ns, out var entries);
                var decls = entries is null ? "" : BuildVarsDeclarations(entries);
                // 无数据的命名空间留空注释：整块删除会改动 Sass 的模块语义（@use 的
                // 副作用声明），保留一个空注释让文件结构不变
                return decls.Length > 0 ? decls : "/* flint: vars 无数据 */";
            });
    }

    /// <summary>
    /// 把 scss 源目录镜像到临时目录并**替换全部文件里的 hugo: 导入**——CLI 版 sass
    /// 没有自定义 importer，而 <c>hugo:vars</c> 含冒号在 Windows 上不是合法文件名，
    /// 无法落盘成可导入的路径，只能在镜像源码上做文本替换
    /// </summary>
    internal static string? CreateHugoVarsMirror(
        string sourceDir, Dictionary<string, Dictionary<string, string?>> vars)
    {
        try
        {
            var mirror = Path.Combine(Path.GetTempPath(), "flint-hugo-vars-" + Guid.NewGuid().ToString("N"));
            foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(sourceDir, file);
                var target = Path.Combine(mirror, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (rel.EndsWith(".scss", StringComparison.OrdinalIgnoreCase) ||
                    rel.EndsWith(".sass", StringComparison.OrdinalIgnoreCase))
                {
                    File.WriteAllText(target,
                        RewriteHugoVarsImports(File.ReadAllText(file), vars));
                }
                else
                {
                    File.Copy(file, target);
                }
            }
            return mirror;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
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
            else if (TypeScriptEntry(name))
            {
                // **Hugo 语义：targetPath 缺省时按 MIME 改扩展名**——.ts 输入的
                // 产物是 .js（Hugo 文档明示 TypeScript 为例）。stack 的
                // `resources.get "ts/main.ts" | js.Build $opts | fingerprint`
                // 不传 targetPath，此前产物保持 main.<hash>.ts，浏览器当 JS 加载
                // 直接 SyntaxError（实测）
                name = Path.ChangeExtension(name, ".js");
            }

            // **ES module 打包 + TS 剥离**：narrow 的 js.Build 输入是 ES module 入口
            // （import { initX } from "./ui.js"）——不打包则浏览器报
            // "Cannot use import statement outside a module"，全部 JS 失效
            // （narrow 导航/主题切换/dock 实测全死）。fixit/stack 的入口是 .ts，
            // 还需剥类型语法（as 转换/interface/成员注解）。轻量实现：以入口文件
            // 为源做 import 解析 + 拓扑序拼接 + import/export 重写。
            // **必须在 minify 之前**：MinifyJs 把 import 压成单行后逐行解析失效
            var entryPath = resource?.SourcePath;
            if (entryPath is null && !string.IsNullOrEmpty(name))
            {
                entryPath = TryResolveAssetPath(name);
            }
            var minifyJs = OptsFlag(optionArgs.OfType<object>().ToArray(), "minify", defaultValue: false);
            // Hugo 的 params 选项 → @params 虚拟模块（`import * as params from '@params'`）
            var paramsJson = OptsParamsJson(optionArgs);
            // format=esm：产物保持 ES 模块形态（入口导出翻成 export 语句），
            // 供 `<script type="module">` 动态 import 消费（stack 的 photoswipe
            // `import gallery from '/ts/gallery.js'` 实测）
            var esmOutput = string.Equals(
                OptsString([.. optionArgs], "format"), "esm", StringComparison.OrdinalIgnoreCase);

            // **整段入缓存**：ES 打包 + minify 是对"入口路径 + 内容 + params"的纯函数，
            // 每页重跑时 700 页各打包一遍主 bundle（fixit 实测为超时主因之一）
            var outRes = CachedByKey(
                "js.build|" + name + "|" + (entryPath ?? "") + "|" + minifyJs + "|" + esmOutput + "|" + (paramsJson?.Length ?? -1) + "|" + content.Length,
                () =>
                {
                    var out0 = content;
                    var needsBundle = out0.Contains("import ", StringComparison.Ordinal) ||
                                      out0.Contains("export ", StringComparison.Ordinal);
                    var needsStrip = TypeScriptEntry(entryPath);
                    if (entryPath is not null && File.Exists(entryPath) && (needsBundle || needsStrip))
                    {
                        var bundled = EsmBundler.Bundle(entryPath, out0, paramsJson, esmOutput);
                        if (bundled is not null)
                        {
                            out0 = bundled;
                        }
                    }

                    if (minifyJs)
                    {
                        out0 = MinifyJs(out0);
                    }

                    // baseUrl 用资源提供者的真实值（子路径构建时 RelPermalink
                    // 才带 /<主题>/ 前缀）——此前传空串，js.Build 产物全掉回站根
                    return TemplateResource.Create(name, out0, _resources?.BaseUrl ?? "");
                });
            Track(outRes);
            return outRes.ToScriptObject();
        });
        js.Import("Babel", (params object?[] args) => (object?)FindResourceArg(args) ?? args.FirstOrDefault());
        js.Import("Batch", (params object?[] args) => (object?)FindResourceArg(args) ?? args.FirstOrDefault());
    }

    /// <summary>js.Build 的 params 选项序列化为 JSON（@params 虚拟模块注入用）</summary>
    private string? OptsParamsJson(List<object?> optionArgs)
    {
        foreach (var o in optionArgs)
        {
            if (o is ScriptObject so &&
                (so.ContainsKey("params") ? so["params"] : null) is { } p)
            {
                return p switch
                {
                    string s => s,
                    _ => SerializeToJson(p, indented: false),
                };
            }
        }
        return null;
    }

    /// <summary>入口/产物名是否为 TypeScript 源（决定是否剥类型 + 改扩展名）</summary>
    private static bool TypeScriptEntry(string? nameOrPath)
    {
        if (string.IsNullOrEmpty(nameOrPath))
        {
            return false;
        }
        var ext = Path.GetExtension(nameOrPath);
        return ext.Equals(".ts", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".tsx", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".mts", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".cts", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 按资源名反查磁盘路径（遍历资源根）：ES module 打包需要入口文件的
    /// 真实路径来解析相对 import
    /// </summary>
    private string? TryResolveAssetPath(string name)
    {
        if (_resources is not FileSystemResourceProvider fs || string.IsNullOrEmpty(name))
        {
            return null;
        }

        foreach (var root in fs.Roots)
        {
            var candidate = Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    /// <summary>轻量 ES module 打包器（js.Build 的 ES module 入口形态）</summary>
    internal static partial class EsmBundler
    {
        /// <summary>import 无扩展名时的解析候选（Node/esbuild 语义：先精确后补扩展名再目录索引）</summary>
        private static readonly string[] ModuleExtensions =
            [".ts", ".tsx", ".mts", ".cts", ".js", ".mjs", ".jsx"];

        /// <summary>Hugo <c>js.Build</c> 的 <c>params</c> 选项虚拟模块固定 specifier</summary>
        private const string ParamsSpecifier = "@params";

        /// <summary>解析后的模块（虚拟模块 Path 为 null）</summary>
        private sealed class Module
        {
            public required string Key = "";
            public string? Path;
            public required string Content = "";
            public bool IsParams;
        }

        /// <summary>
        /// 从入口文件出发解析 import 图，按**依赖序**（被依赖者在前）拼接为单一经典脚本。
        /// 每模块包 IIFE 独立作用域，导出挂 __flint_exp 对象、经 __flint_mods 注册表
        /// 跨模块引用；import 语句重写为对注册表的 var 声明（支持默认/命名/别名/
        /// 命名空间导入）。TS 入口先剥类型再打包。<c>paramsJson</c> 非空时注入
        /// <c>@params</c> 虚拟模块。解析失败/文件缺失返回 null（调用方回退原文）。
        /// <paramref name="esmOutput"/> 为 true 时按 ES 模块输出：IIFE 包之后
        /// 追加入口模块的 ESM 导出语句（stack 的 photoswipe 用
        /// `format "esm"` + 动态 `import gallery from …` 消费默认导出）
        /// </summary>
        internal static string? Bundle(
            string entryPath, string entryContent, string? paramsJson = null, bool esmOutput = false)
        {
            try
            {
                var modules = new Dictionary<string, Module>(StringComparer.OrdinalIgnoreCase);
                var order = new List<Module>();
                if (!Load(entryPath, entryContent, modules, order, paramsJson))
                {
                    return null;
                }

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("var __flint_mods = {};");
                Module? entryMod = null;
                var entryNamed = new List<string>();
                var entryHasDefault = false;
                foreach (var mod in order)
                {
                    if (mod.Path is not null && Path.GetFullPath(mod.Path) == Path.GetFullPath(entryPath))
                    {
                        entryMod = mod;
                    }
                    sb.Append("/* ===== ").Append(Path.GetFileName(mod.Key)).AppendLine(" ===== */");
                    sb.AppendLine("(function(){");
                    sb.AppendLine("  var __flint_exp = {};");
                    var (body, named, hasDefault) = EmitModuleBody(mod, modules);
                    if (ReferenceEquals(mod, entryMod))
                    {
                        entryNamed = named;
                        entryHasDefault = hasDefault;
                    }
                    sb.Append(body);
                    sb.Append("  __flint_mods[").Append(JsString(mod.Key)).Append("] = __flint_exp;");
                    sb.AppendLine("})();");
                }

                if (esmOutput && entryMod is not null)
                {
                    // ESM 输出：把入口模块的导出翻成真正的 export 语句挂在文件尾
                    var reg = JsString(entryMod.Key);
                    if (entryHasDefault)
                    {
                        sb.Append("export default __flint_mods[").Append(reg).Append("].default;")
                          .AppendLine();
                    }
                    foreach (var n in entryNamed)
                    {
                        sb.Append("const ").Append(n).Append(" = __flint_mods[").Append(reg)
                          .Append("].").Append(n).AppendLine(";");
                        sb.Append("export { ").Append(n).AppendLine(" };");
                    }
                }
                return sb.ToString();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>深度优先加载模块图（后序 = 依赖序）；失败返回 false</summary>
        private static bool Load(
            string path, string content,
            Dictionary<string, Module> modules, List<Module> order, string? paramsJson)
        {
            var key = Path.GetFullPath(path);
            if (modules.ContainsKey(key))
            {
                return true; // 已处理（含循环依赖：直接跳过）
            }

            var mod = new Module { Key = key, Path = key, Content = content };
            modules[key] = mod;

            var dir = Path.GetDirectoryName(key)!;
            // **import 与再导出（export … from）都建依赖边**：barrel 文件
            // （fixit 的 utils/index.ts 全是 `export * from './x'`）不建边则
            // 目标模块不入队、星型复制循环读到 undefined，全部具名导出丢失
            // （createCopyText is not a function 实测）
            foreach (var spec in ExtractDependencySpecifiers(content))
            {
                if (string.Equals(spec, ParamsSpecifier, StringComparison.Ordinal))
                {
                    var pkey = ParamsSpecifier;
                    if (!modules.ContainsKey(pkey))
                    {
                        modules[pkey] = new Module
                        {
                            Key = pkey,
                            Content = BuildParamsModuleContent(paramsJson),
                            IsParams = true,
                        };
                        LoadVirtual(modules[pkey], modules, order);
                    }
                    continue;
                }
                if (!spec.StartsWith("./", StringComparison.Ordinal) &&
                    !spec.StartsWith("../", StringComparison.Ordinal))
                {
                    // 裸模块名（node_modules 依赖）：Flint 无 node 依赖解析，打包失败
                    return false;
                }
                var dep = ResolveRelative(dir, spec);
                if (dep is null)
                {
                    return false;
                }
                if (!Load(dep, File.ReadAllText(dep), modules, order, paramsJson))
                {
                    return false;
                }
            }

            order.Add(mod);
            return true;
        }

        /// <summary>虚拟模块入队（无依赖可解析）</summary>
        private static void LoadVirtual(Module mod, Dictionary<string, Module> modules, List<Module> order)
        {
            order.Add(mod);
        }

        /// <summary>生成 <c>@params</c> 虚拟模块源码：默认导出 + 顶层键的命名导出</summary>
        private static string BuildParamsModuleContent(string? paramsJson)
        {
            var json = string.IsNullOrWhiteSpace(paramsJson) ? "{}" : paramsJson!;
            var sb = new System.Text.StringBuilder();
            sb.Append("var __flint_params = ").Append(json).AppendLine(";");
            sb.AppendLine("export default __flint_params;");
            foreach (var key in TopLevelJsonKeys(json))
            {
                if (IsJsIdentifier(key))
                {
                    sb.Append("export const ").Append(key).Append(" = __flint_params.")
                      .Append(key).AppendLine(";");
                }
            }
            return sb.ToString();
        }

        /// <summary>JSON 对象顶层键（用于 @params 命名导出；解析失败返回空）</summary>
        private static IEnumerable<string> TopLevelJsonKeys(string json)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                {
                    return [];
                }
                return doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
            }
            catch (System.Text.Json.JsonException)
            {
                return [];
            }
        }

        private static bool IsJsIdentifier(string s) =>
            s.Length > 0 && (char.IsAsciiLetter(s[0]) || s[0] == '_' || s[0] == '$') &&
            s.All(c => char.IsAsciiLetterOrDigit(c) || c == '_' || c == '$');

        /// <summary>JS 字符串字面量（含引号与转义）</summary>
        private static string JsString(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length + 2);
            sb.Append('\'');
            foreach (var c in s)
            {
                sb.Append(c switch
                {
                    '\\' => "\\\\",
                    '\'' => "\\'",
                    '\n' => "\\n",
                    '\r' => "\\r",
                    _ => c.ToString(),
                });
            }
            sb.Append('\'');
            return sb.ToString();
        }

        /// <summary>
        /// 相对 specifier 解析（Node/esbuild 语义）：精确命中 → 补扩展名 →
        /// 目录索引文件。stack 的 <c>import menu from './menu'</c>（无扩展名）
        /// 命中 ./menu.ts；fixit 的 <c>'./color-scheme'</c> 命中 ./color-scheme.ts
        /// </summary>
        private static string? ResolveRelative(string fromDir, string spec)
        {
            var combined = Path.GetFullPath(Path.Combine(fromDir, spec));
            if (File.Exists(combined))
            {
                return combined;
            }
            foreach (var ext in ModuleExtensions)
            {
                var candidate = combined + ext;
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            foreach (var ext in ModuleExtensions)
            {
                var index = Path.Combine(combined, "index" + ext);
                if (File.Exists(index))
                {
                    return index;
                }
            }
            return null;
        }

        /// <summary>
        /// 发射单模块体：剥 TS 类型 → import 语句重写为注册表 var 声明 →
        /// export 语句转为 __flint_exp 赋值（默认/命名/别名/再导出全覆盖）
        /// </summary>
        /// <summary>发射单模块体；同时返回该模块的导出清单（ESM 输出用）</summary>
        private static (string Body, List<string> Named, bool HasDefault) EmitModuleBody(
            Module mod, Dictionary<string, Module> modules)
        {
            var js = TypeScriptStripper.Strip(mod.Content);
            var sb = new System.Text.StringBuilder();
            var endAssignments = new List<string>();
            var hasDefaultExport = false;
            var pos = 0;
            foreach (var stmt in ScanModuleStatements(js))
            {
                sb.Append(js, pos, stmt.Start - pos); // 语句间原样输出
                pos = stmt.End;

                if (stmt.Kind == StmtKind.Import)
                {
                    if (stmt.TypeOnly)
                    {
                        continue; // import type：纯类型，整体删除
                    }
                    var depKey = ResolveStatementKey(stmt.Specifier!, mod, modules);
                    if (depKey is null)
                    {
                        continue; // 解析失败（调用方已在加载期拦住，双保险）
                    }
                    var decls = new List<string>();
                    if (stmt.NamespaceBinding is not null)
                    {
                        decls.Add($"var {stmt.NamespaceBinding} = __flint_mods[{JsString(depKey)}];");
                    }
                    if (stmt.DefaultBinding is not null)
                    {
                        decls.Add($"var {stmt.DefaultBinding} = __flint_mods[{JsString(depKey)}].default;");
                    }
                    foreach (var (external, local) in stmt.Named)
                    {
                        decls.Add($"var {local} = __flint_mods[{JsString(depKey)}].{external};");
                    }
                    sb.AppendLine(string.Join(" ", decls));
                    continue;
                }

                if (stmt.Kind == StmtKind.Export)
                {
                    switch (stmt.ExportForm)
                    {
                        case ExportForm.Default:
                            hasDefaultExport = true;
                            if (stmt.DefaultIsDeclaration && stmt.DeclName is not null)
                            {
                                // `export default function NAME(...) {...}` → 保留声明 + 末尾挂 default
                                sb.Append(js, stmt.Start + "export default ".Length,
                                    stmt.End - stmt.Start - "export default ".Length);
                                endAssignments.Add($"__flint_exp.default = {stmt.DeclName};");
                            }
                            else
                            {
                                // 匿名默认导出（`export default function () {…}` /
                                // `export default class {…}`，stack 的 menu.ts 实测）与
                                // 表达式默认导出统一走挂载形态：裸 `function () {}`
                                // 不是合法语句，必须赋给 __flint_exp.default。
                                // 声明形态的 End 不含分号，这里补一个（表达式形态
                                // 源自带分号，多一个空语句无害）
                                sb.Append("__flint_exp.default = ");
                                sb.Append(js, stmt.Start + "export default ".Length,
                                    stmt.End - stmt.Start - "export default ".Length);
                                sb.Append(';');
                            }
                            break;
                        case ExportForm.NamedList:
                            foreach (var (external, local) in stmt.Named)
                            {
                                endAssignments.Add($"__flint_exp.{external} = {local};");
                            }
                            break;
                        case ExportForm.NamedReExport:
                            foreach (var (external, local) in stmt.Named)
                            {
                                var src = ResolveStatementKey(stmt.Specifier!, mod, modules);
                                if (src is not null)
                                {
                                    endAssignments.Add(
                                        $"__flint_exp.{external} = __flint_mods[{JsString(src)}].{local};");
                                }
                            }
                            break;
                        case ExportForm.StarReExport:
                            var starSrc = ResolveStatementKey(stmt.Specifier!, mod, modules);
                            if (starSrc is not null)
                            {
                                endAssignments.Add(
                                    $"for (var __fk in __flint_mods[{JsString(starSrc)}]) {{ __flint_exp[__fk] = __flint_mods[{JsString(starSrc)}][__fk]; }}");
                            }
                            break;
                        case ExportForm.Declaration:
                            // `export function NAME …` → 剥 export 前缀，末尾挂名字
                            sb.Append(js, stmt.Start + "export ".Length,
                                stmt.End - stmt.Start - "export ".Length);
                            if (stmt.DeclName is not null)
                            {
                                endAssignments.Add($"__flint_exp.{stmt.DeclName} = {stmt.DeclName};");
                            }
                            break;
                    }
                    continue;
                }

                // 普通语句：原样
                sb.Append(js, stmt.Start, stmt.End - stmt.Start);
            }
            sb.Append(js, pos, js.Length - pos);
            foreach (var a in endAssignments)
            {
                sb.Append("  ").AppendLine(a);
            }

            // 导出清单：默认导出与命名导出（ESM 输出用）。hasDefaultExport 在
            // Default 语句处理时直接跟踪（默认挂载是内联写的，不在 endAssignments）
            var named = endAssignments
                .Select(a => a.StartsWith("__flint_exp.", StringComparison.Ordinal) &&
                             !a.StartsWith("__flint_exp.default", StringComparison.Ordinal)
                    ? a["__flint_exp.".Length..a.IndexOf(' ', StringComparison.Ordinal)]
                    : null)
                .Where(n => n is not null)
                .Select(n => n!)
                .ToList();
            return (sb.ToString(), named, hasDefaultExport);
        }

        /// <summary>语句 specifier → 模块键（@params 虚拟模块或绝对路径）</summary>
        private static string? ResolveStatementKey(string spec, Module mod, Dictionary<string, Module> modules)
        {
            if (string.Equals(spec, ParamsSpecifier, StringComparison.Ordinal))
            {
                return ParamsSpecifier;
            }
            if (mod.Path is null)
            {
                return null;
            }
            var dir = Path.GetDirectoryName(mod.Path)!;
            var dep = ResolveRelative(dir, spec);
            return dep is null ? null : Path.GetFullPath(dep);
        }

        private enum StmtKind { Other, Import, Export }
        private enum ExportForm { Declaration, Default, NamedList, NamedReExport, StarReExport }

        private sealed class ModuleStatement
        {
            public StmtKind Kind = StmtKind.Other;
            public int Start;
            public int End;
            // import
            public string? Specifier;
            public bool TypeOnly;
            public string? DefaultBinding;
            public string? NamespaceBinding;
            /// <summary>命名子句：External = 源模块/对外的名字，Local = 本地绑定名</summary>
            public List<(string External, string Local)> Named = [];
            // export
            public ExportForm ExportForm;
            public bool DefaultIsDeclaration;
            public string? DeclName;
        }

        /// <summary>
        /// 扫描模块级 import/export 语句（多行安全）：找语句起点（行首或
        /// <c>;{}`</c> 之后的 <c>import</c>/<c>export</c> 关键字），解析子句到
        /// 语句结束（裸导入到分号；带 from 的到分号；默认导出声明到体块末）
        /// </summary>
        private static List<ModuleStatement> ScanModuleStatements(string js)
        {
            var result = new List<ModuleStatement>();
            var i = 0;
            while (i < js.Length)
            {
                var lineStart = i == 0 || js[i - 1] == '\n';
                if (lineStart && TryReadKeyword(js, ref i, out var kw))
                {
                    var start = i - kw.Length;
                    if (kw == "import")
                    {
                        var stmt = ParseImportStatement(js, start);
                        if (stmt is not null)
                        {
                            result.Add(stmt);
                            i = stmt.End;
                            continue;
                        }
                    }
                    else if (kw == "export")
                    {
                        var stmt = ParseExportStatement(js, start);
                        if (stmt is not null)
                        {
                            result.Add(stmt);
                            i = stmt.End;
                            continue;
                        }
                    }
                }
                i++;
            }
            return result;
        }

        /// <summary>行首空白后读关键字（import/export）；命中时 i 推到关键字之后</summary>
        private static bool TryReadKeyword(string s, ref int i, out string keyword)
        {
            var j = i;
            while (j < s.Length && (s[j] == ' ' || s[j] == '\t' || s[j] == '\r'))
            {
                j++;
            }
            foreach (var kw in new[] { "import", "export" })
            {
                if (j + kw.Length <= s.Length && string.CompareOrdinal(s, j, kw, 0, kw.Length) == 0 &&
                    (j + kw.Length >= s.Length || !IsIdentChar(s[j + kw.Length])))
                {
                    i = j + kw.Length;
                    keyword = kw;
                    return true;
                }
            }
            keyword = "";
            return false;
        }

        private static bool IsIdentChar(char c) =>
            char.IsAsciiLetterOrDigit(c) || c == '_' || c == '$';

        /// <summary>解析 import 语句（start 指向 import 关键字）</summary>
        private static ModuleStatement? ParseImportStatement(string js, int start)
        {
            var stmt = new ModuleStatement { Kind = StmtKind.Import, Start = start };
            var i = start + "import".Length;
            i = SkipWs(js, i);
            // import type ...
            if (TryReadWord(js, ref i, "type") && i < js.Length && js[i] != ',')
            {
                // `import type from './x'`：type 是绑定名（后跟 from）——再看一个 token
                var save = i;
                if (TryReadWord(js, ref i, "from") && i < js.Length && (js[i] == '\'' || js[i] == '"'))
                {
                    i = save; // 回退：type 是默认导入名
                    stmt.DefaultBinding = "type";
                }
                else
                {
                    i = save;
                    stmt.TypeOnly = true;
                }
            }
            i = SkipWs(js, i);
            // 三种 import 形态解析到 specifier
            while (i < js.Length && js[i] != '\'' && js[i] != '"')
            {
                if (js[i] == '{')
                {
                    var close = MatchBracket(js, i, '{', '}');
                    if (close < 0)
                    {
                        return null;
                    }
                    ParseNamedClause(js, i, close, stmt.Named);
                    i = close + 1;
                }
                else if (js[i] == '*')
                {
                    i++;
                    i = SkipWs(js, i);
                    if (!TryReadWord(js, ref i, "as"))
                    {
                        return null;
                    }
                    i = SkipWs(js, i);
                    var name = ReadIdentifier(js, ref i);
                    if (name is null)
                    {
                        return null;
                    }
                    stmt.NamespaceBinding = name;
                }
                else if (IsIdentStart(js[i]))
                {
                    var name = ReadIdentifier(js, ref i);
                    if (name is null)
                    {
                        return null;
                    }
                    // `from` 是关键字不是绑定名（`import X from '...'` 的 X 已在前）。
                    // 漏掉这判断会把 from 当默认导入、specifier 前的结构全乱
                    // （`import { a } from './b'` 曾产出 var from = ....default）
                    if (name == "from")
                    {
                        break;
                    }
                    if (stmt.TypeOnly && stmt.DefaultBinding is null && stmt.Named.Count == 0 &&
                        stmt.NamespaceBinding is null && name == "type")
                    {
                        // import type X from ... 的 X
                        stmt.DefaultBinding = name;
                    }
                    else
                    {
                        stmt.DefaultBinding ??= name;
                    }
                }
                else if (js[i] == ',')
                {
                    i++;
                }
                else
                {
                    return null;
                }
                i = SkipWs(js, i);
            }
            if (i >= js.Length)
            {
                return null;
            }
            // from 关键字 break 出循环时 i 停在 from 之后，需再跳过空白
            i = SkipWs(js, i);
            var spec = ReadStringLiteral(js, ref i);
            if (spec is null)
            {
                return null;
            }
            stmt.Specifier = spec;
            // from 关键字（裸导入 `import 'x'` 没有）
            var after = SkipWs(js, i);
            if (TryReadWord(js, ref after, "from"))
            {
                after = SkipWs(js, after);
                if (after >= js.Length || (js[after] != '\'' && js[after] != '"'))
                {
                    return null;
                }
                spec = ReadStringLiteral(js, ref after);
                if (spec is null)
                {
                    return null;
                }
                stmt.Specifier = spec;
                i = after;
            }
            // 语句结束：分号或行尾（无分号结尾）
            var end = SkipWs(js, i);
            if (end < js.Length && js[end] == ';')
            {
                end++;
            }
            stmt.End = end;
            return stmt;
        }

        /// <summary>解析 `{ a, b as c }` 命名导入子句</summary>
        private static void ParseNamedClause(string js, int open, int close, List<(string, string)> into)
        {
            var i = open + 1;
            while (i < close)
            {
                i = SkipWs(js, i);
                if (i >= close)
                {
                    break;
                }
                var imported = ReadIdentifier(js, ref i);
                if (imported is null)
                {
                    return;
                }
                var local = imported;
                i = SkipWs(js, i);
                if (i < close && TryReadWord(js, ref i, "as"))
                {
                    i = SkipWs(js, i);
                    local = ReadIdentifier(js, ref i) ?? imported;
                    i = SkipWs(js, i);
                }
                into.Add((imported, local));
                if (i < close && js[i] == ',')
                {
                    i++;
                }
            }
        }

        /// <summary>解析 export 语句（start 指向 export 关键字）</summary>
        private static ModuleStatement? ParseExportStatement(string js, int start)
        {
            var stmt = new ModuleStatement { Kind = StmtKind.Export, Start = start };
            var i = start + "export".Length;
            i = SkipWs(js, i);
            if (TryReadWord(js, ref i, "default"))
            {
                stmt.ExportForm = ExportForm.Default;
                i = SkipWs(js, i);
                // export default function/class NAME 或匿名声明 → 体块末
                var isDecl = TryReadWord(js, ref i, "function") || TryReadWord(js, ref i, "class");
                if (isDecl)
                {
                    i = SkipWs(js, i);
                    if (TryReadWord(js, ref i, "*"))
                    {
                        i = SkipWs(js, i);
                    }
                    stmt.DeclName = ReadIdentifier(js, ref i);
                    // 跳到体块（{…}）末尾；函数体必有大括号
                    var braceIdx = IndexOfSkippingStrings(js, i, '{');
                    if (braceIdx < 0)
                    {
                        return null;
                    }
                    var close = MatchBracket(js, braceIdx, '{', '}');
                    if (close < 0)
                    {
                        return null;
                    }
                    stmt.DefaultIsDeclaration = true;
                    i = close + 1;
                }
                else
                {
                    // export default <expr>; —— 到行/块层级的分号
                    var end = FindStatementEnd(js, i);
                    if (end < 0)
                    {
                        return null;
                    }
                    i = end;
                }
                var semi = SkipWs(js, i);
                if (semi < js.Length && js[semi] == ';')
                {
                    semi++;
                }
                stmt.End = semi;
                return stmt;
            }

            if (i < js.Length && js[i] == '{')
            {
                var close = MatchBracket(js, i, '{', '}');
                if (close < 0)
                {
                    return null;
                }
                stmt.ExportForm = ExportForm.NamedList;
                var named = new List<(string, string)>();
                ParseNamedClause(js, i, close, named);
                // 导入子句解析给的是 (源名, 别名)；导出语义翻成 (外部名, 本地名)：
                // `export { a, b as c }` → 外部 a/本地 a、外部 c/本地 b
                stmt.Named = named.Select(n => (External: n.Item2, Local: n.Item1)).ToList();
                i = SkipWs(js, close + 1);
                string? reExportFrom = null;
                if (TryReadWord(js, ref i, "from"))
                {
                    i = SkipWs(js, i);
                    reExportFrom = ReadStringLiteral(js, ref i);
                    if (reExportFrom is null)
                    {
                        return null;
                    }
                    stmt.ExportForm = ExportForm.NamedReExport;
                    stmt.Specifier = reExportFrom;
                }
                var end = SkipWs(js, i);
                if (end < js.Length && js[end] == ';')
                {
                    end++;
                }
                stmt.End = end;
                return stmt;
            }

            if (js[i] == '*')
            {
                i++;
                i = SkipWs(js, i);
                if (TryReadWord(js, ref i, "as"))
                {
                    i = SkipWs(js, i);
                    ReadIdentifier(js, ref i); // ns 名（再导出命名空间，暂按通配处理）
                }
                i = SkipWs(js, i);
                if (!TryReadWord(js, ref i, "from"))
                {
                    return null;
                }
                i = SkipWs(js, i);
                var spec = ReadStringLiteral(js, ref i);
                if (spec is null)
                {
                    return null;
                }
                stmt.ExportForm = ExportForm.StarReExport;
                stmt.Specifier = spec;
                var end = SkipWs(js, i);
                if (end < js.Length && js[end] == ';')
                {
                    end++;
                }
                stmt.End = end;
                return stmt;
            }

            // export function/class/const/let/var NAME …
            var declStart = i;
            var declKeyword = TryReadWord(js, ref i, "function") ? "function"
                : TryReadWord(js, ref i, "class") ? "class"
                : TryReadWord(js, ref i, "const") ? "const"
                : TryReadWord(js, ref i, "let") ? "let"
                : TryReadWord(js, ref i, "var") ? "var"
                : null;
            if (declKeyword is not null)
            {
                i = SkipWs(js, i);
                if (TryReadWord(js, ref i, "*"))
                {
                    i = SkipWs(js, i);
                }
                stmt.DeclName = ReadIdentifier(js, ref i);
                stmt.ExportForm = ExportForm.Declaration;
                // 声明体：function/class 到匹配大括号；const/let/var 到分号或行尾
                // （按**关键字**判定而非首字母——'c' 同时是 const 与 class 的首字母，
                // 曾使 `export const v = 1` 走大括号路径找不到 { 而整条语句丢弃）
                if (declKeyword is "function" or "class")
                {
                    var braceIdx = IndexOfSkippingStrings(js, i, '{');
                    if (braceIdx < 0)
                    {
                        return null;
                    }
                    var close = MatchBracket(js, braceIdx, '{', '}');
                    if (close < 0)
                    {
                        return null;
                    }
                    i = close + 1;
                }
                else
                {
                    var end = FindStatementEnd(js, i);
                    if (end < 0)
                    {
                        return null;
                    }
                    i = end;
                }
                stmt.End = i;
                return stmt;
            }

            return null; // export type/interface 等已被 TS 剥离器删除，其余形态不支持
        }

        private static int SkipWs(string s, int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
            {
                i++;
            }
            return i;
        }

        private static bool TryReadWord(string s, ref int i, string word)
        {
            if (i + word.Length <= s.Length && string.CompareOrdinal(s, i, word, 0, word.Length) == 0 &&
                (i + word.Length >= s.Length || !IsIdentChar(s[i + word.Length])))
            {
                i += word.Length;
                return true;
            }
            return false;
        }

        private static string? ReadIdentifier(string s, ref int i)
        {
            if (i >= s.Length || !IsIdentStart(s[i]))
            {
                return null;
            }
            var start = i;
            while (i < s.Length && IsIdentChar(s[i]))
            {
                i++;
            }
            return s[start..i];
        }

        private static bool IsIdentStart(char c) => char.IsAsciiLetter(c) || c == '_' || c == '$';

        private static string? ReadStringLiteral(string s, ref int i)
        {
            if (i >= s.Length || (s[i] != '\'' && s[i] != '"'))
            {
                return null;
            }
            var quote = s[i++];
            var start = i;
            while (i < s.Length && s[i] != quote)
            {
                if (s[i] == '\\')
                {
                    i++;
                }
                i++;
            }
            if (i >= s.Length)
            {
                return null;
            }
            var value = s[start..i];
            i++; // 跳过收尾引号
            return value;
        }

        /// <summary>从 i 起找第一个不在字符串/注释里的指定字符</summary>
        private static int IndexOfSkippingStrings(string s, int i, char target)
        {
            while (i < s.Length)
            {
                var c = s[i];
                if (c == '\'' || c == '"' || c == '`')
                {
                    i = SkipString(s, i);
                    continue;
                }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
                {
                    while (i < s.Length && s[i] != '\n')
                    {
                        i++;
                    }
                    continue;
                }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
                {
                    var close = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = close < 0 ? s.Length : close + 2;
                    continue;
                }
                if (c == '/' && IsRegexStart(s, i))
                {
                    i = SkipRegex(s, i);
                    continue;
                }
                if (c == target)
                {
                    return i;
                }
                i++;
            }
            return -1;
        }

        private static int SkipString(string s, int i)
        {
            var quote = s[i++];
            while (i < s.Length)
            {
                if (s[i] == '\\')
                {
                    i += 2;
                    continue;
                }
                if (s[i] == quote)
                {
                    return i + 1;
                }
                i++;
            }
            return i;
        }

        /// <summary>正则前导关键字（这些标识符后出现 / 是正则字面量而非除号）</summary>
        private static readonly HashSet<string> RegexPrecedingKeywords = new(StringComparer.Ordinal)
        {
            "return", "typeof", "instanceof", "in", "of", "new", "delete", "void",
            "throw", "case", "do", "else", "yield", "await",
        };

        /// <summary>
        /// 位置 i 的 / 是否正则字面量起点（回溯前一 token：运算符/表达式
        /// 关键字/起始位 → 正则；值标识符/) ] 引号/数字 → 除号）。
        /// 不识别会把正则里的引号/括号当字符串或配平符——fixit file.ts 的
        /// `/[\\/:*?"<>|\r\n]+/g` 实测把 MatchBracket 打到文件尾
        /// </summary>
        internal static bool IsRegexStart(string s, int i)
        {
            var j = i - 1;
            while (j >= 0 && char.IsWhiteSpace(s[j]))
            {
                j--;
            }
            if (j < 0)
            {
                return true;
            }
            var c = s[j];
            if (c is '(' or ',' or '=' or ':' or '[' or '!' or '&' or '|' or '?' or
                    '{' or ';' or '>' or '<' or '+' or '-' or '*' or '%' or '~' or '^')
            {
                return true;
            }
            if (char.IsAsciiLetter(c) || c == '_' || c == '$')
            {
                var end = j;
                while (j >= 0 && (char.IsAsciiLetterOrDigit(s[j]) || s[j] == '_' || s[j] == '$'))
                {
                    j--;
                }
                return RegexPrecedingKeywords.Contains(s[(j + 1)..(end + 1)]);
            }
            return false; // ) ] " ' ` 数字 → 除号
        }

        /// <summary>跳过正则字面量（/…/flags）；[...] 类内的 / 不终止；不跨行</summary>
        internal static int SkipRegex(string s, int i)
        {
            i++; // 跳过起始 /
            var inClass = false;
            while (i < s.Length)
            {
                var c = s[i];
                if (c == '\\')
                {
                    i += 2;
                    continue;
                }
                if (c == '\n')
                {
                    return i; // 正则不跨行：失败保护
                }
                if (c == '[')
                {
                    inClass = true;
                }
                else if (c == ']')
                {
                    inClass = false;
                }
                else if (c == '/' && !inClass)
                {
                    i++;
                    while (i < s.Length && char.IsAsciiLetter(s[i]))
                    {
                        i++; // flags
                    }
                    return i;
                }
                i++;
            }
            return i;
        }

        /// <summary>括号/花括号/方括号配平匹配（跳过字符串与注释）</summary>
        private static int MatchBracket(string s, int open, char openCh, char closeCh)
        {
            var depth = 0;
            var i = open;
            while (i < s.Length)
            {
                var c = s[i];
                if (c == '\'' || c == '"' || c == '`')
                {
                    i = SkipString(s, i);
                    continue;
                }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
                {
                    while (i < s.Length && s[i] != '\n')
                    {
                        i++;
                    }
                    continue;
                }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
                {
                    var close = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = close < 0 ? s.Length : close + 2;
                    continue;
                }
                if (c == '/' && IsRegexStart(s, i))
                {
                    i = SkipRegex(s, i);
                    continue;
                }
                if (c == openCh)
                {
                    depth++;
                }
                else if (c == closeCh)
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
                i++;
            }
            return -1;
        }

        /// <summary>语句结束位置（跳过字符串/注释后找层级 0 的分号）</summary>
        private static int FindStatementEnd(string s, int i)
        {
            var paren = 0;
            var brace = 0;
            var brack = 0;
            while (i < s.Length)
            {
                var c = s[i];
                if (c == '\'' || c == '"' || c == '`')
                {
                    i = SkipString(s, i);
                    continue;
                }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
                {
                    while (i < s.Length && s[i] != '\n')
                    {
                        i++;
                    }
                    continue;
                }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
                {
                    var close = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = close < 0 ? s.Length : close + 2;
                    continue;
                }
                if (c == '/' && IsRegexStart(s, i))
                {
                    i = SkipRegex(s, i);
                    continue;
                }
                switch (c)
                {
                    case '(': paren++; break;
                    case ')': paren--; break;
                    case '{': brace++; break;
                    case '}': brace--; break;
                    case '[': brack++; break;
                    case ']': brack--; break;
                    case ';' when paren == 0 && brace == 0 && brack == 0:
                        return i;
                    case '\n' when paren == 0 && brace == 0 && brack == 0:
                        return i; // 无分号的语句到行尾
                }
                i++;
            }
            return s.Length;
        }

        /// <summary>提取全部 import specifier（模块图加载用，含 type-only）</summary>
        private static List<string> ExtractImportSpecifiers(string content)
        {
            var result = new List<string>();
            foreach (var stmt in ScanModuleStatements(content))
            {
                if (stmt.Kind == StmtKind.Import && stmt.Specifier is not null)
                {
                    result.Add(stmt.Specifier);
                }
            }
            return result;
        }

        /// <summary>
        /// 提取全部依赖 specifier：import + **再导出**（`export { a } from './x'` /
        /// `export * from './x'`）。barrel 文件靠再导出聚合模块，漏掉再导出
        /// 依赖边会让目标模块不入队（fixit utils/index.ts 实测）
        /// </summary>
        private static List<string> ExtractDependencySpecifiers(string content)
        {
            var result = new List<string>();
            foreach (var stmt in ScanModuleStatements(content))
            {
                if (stmt.Kind == StmtKind.Import && stmt.Specifier is not null)
                {
                    result.Add(stmt.Specifier);
                }
                else if (stmt.Kind == StmtKind.Export && stmt.Specifier is not null &&
                         (stmt.ExportForm == ExportForm.NamedReExport ||
                          stmt.ExportForm == ExportForm.StarReExport))
                {
                    result.Add(stmt.Specifier);
                }
            }
            return result;
        }
    }

    /// <summary>
    /// TypeScript 类型语法剥离器（js.Build 的 .ts 入口/依赖用）。
    /// Flint 无 esbuild/tsc，用 token 化 + 上下文规则剥掉主题实际用到的 TS 子集：
    /// interface/type 别名/declare 声明、as/satisfies 转换、类成员修饰符与类型
    /// 注解、参数与返回类型注解、泛型形参/实参、非空断言、implements 子句。
    /// 覆盖 fixit/stack 两主题全部 TS 资产；无法识别的新语法按原样保留
    /// （浏览器报错好过静默错位），import/export 语句不受影响
    /// </summary>
    internal static class TypeScriptStripper
    {
        private enum Tok { Ident, Str, Tmpl, Num, Punct }

        private readonly struct Token(Tok kind, string text, int start, int end)
        {
            public readonly Tok Kind = kind;
            public readonly string Text = text;
            public readonly int Start = start;
            public readonly int End = end;
        }

        /// <summary>括号帧（类体/参数组/对象体的上下文判定用）</summary>
        private sealed class Frame
        {
            public char Open;
            public bool IsClassBody;
            public bool IsNamedClause; // import/export 的 { a, b as c } 命名子句
            public bool IsObjectLiteral; // { key: value }（表达式位的大括号）
            public int Questions; // 组内未配对 ? 计数（三元判定）
        }

        private static readonly HashSet<string> TsModifiers = new(StringComparer.Ordinal)
        {
            "private", "protected", "public", "readonly", "abstract", "override", "declare",
        };

        internal static string Strip(string source)
        {
            var tokens = Tokenize(source);
            var sb = new System.Text.StringBuilder(source.Length);
            var frames = new List<Frame>();
            var lastEnd = 0;
            var prevMeaningful = ""; // 前一个有意义 token 的文本（跳过空白）
            var prevMeaningfulWasValue = false; // 前 token 是否为值结尾（标识符/)等）
            var prevWasIdent = false; // 前 token 是否为标识符（排除 case 标签的字符串值）
            var pendingClassBody = false; // 见到 class 关键字后下一个 { 是类体
            var pendingNamedClause = false; // import/export 后下一个 { 是命名子句
            // 剥掉**返回类型**后下一个 { 是函数/类体（此时 prevMeaningful 已是 ":"，
            // 而 ":" 又在对象字面量触发集里，不标记会把函数体误判为对象字面量）
            var pendingBody = false;
            // 已消费的类型表达式区间：三元检测要排除条件类型（A extends B ? C : D）
            // 里的 ?——它长得像三元但属类型语法（fixit event-bus.ts 实测：
            // 漏排除会使后续返回类型 : void 被误判三元而不剥）
            var skippedTypes = new List<(int From, int To)>();
            var i = 0;
            while (i < tokens.Count)
            {
                var t = tokens[i];
                if (t.Kind == Tok.Punct)
                {
                    switch (t.Text)
                    {
                        case "(" or "[":
                            // 命名子句期待在 ( 处失效：import/export 的 { a, b }
                            // 不可能出现在 ( 之后（`export function f(` 是函数声明）
                            pendingNamedClause = false;
                            frames.Add(new Frame { Open = t.Text[0] });
                            break;
                        case "=":
                            // `export const x = { … }`：= 之后不可能是命名子句
                            pendingNamedClause = false;
                            break;
                        case "{":
                            // 对象字面量判定：前 token 在**表达式位**（= ( , [ : return
                            // 及二元/三元/一元运算符）即 { key: value }；否则是代码块
                            // （函数体/if/try/类体/=> 箭头体）。
                            // **=> 不算表达式位**：`=> {` 是箭头函数体（代码块）；
                            // 箭头返回对象字面量必须写 `=> ({...})`（带括号）。
                            // **pendingBody 优先**：返回类型剥掉后 prevMeaningful 是 ":"，
                            // 不靠这个标记会把函数体误判成对象字面量。
                            // 运算符在列是硬需求：三元 true 分支的对象字面量
                            // `cond ? { v: x } : y`（fuse.mjs deepGet 实测）——漏掉
                            // `?` 会把 { 当代码块，对象键 v: 被当类型注解剥掉
                            frames.Add(new Frame
                            {
                                Open = '{',
                                IsClassBody = pendingClassBody,
                                IsNamedClause = pendingNamedClause,
                                IsObjectLiteral = !pendingBody && !pendingClassBody && !pendingNamedClause &&
                                    (prevMeaningful is "=" or "(" or "," or "[" or ":" or "return"
                                        or "?" or "&&" or "||" or "??" or "!" or "+" or "-"
                                        or "*" or "/" or "%" or "==" or "!=" or "===" or "!=="
                                        or "<" or ">" or "<=" or ">=" or "&" or "|" or "^"
                                        or "<<" or ">>" or "typeof" or "void" or "delete"
                                        or "await" or "yield"),
                            });
                            pendingClassBody = false;
                            pendingNamedClause = false;
                            pendingBody = false;
                            break;
                        case ")" or "]" or "}":
                            if (frames.Count > 0)
                            {
                                frames.RemoveAt(frames.Count - 1);
                            }
                            break;
                        case "?":
                            if (frames.Count > 0)
                            {
                                frames[^1].Questions++;
                            }
                            break;
                        case ";":
                            pendingNamedClause = false; // 语句结束，命名子句期待失效
                            break;
                        case "class":
                            break;
                    }
                }

                // import/export 语句起点：记录命名子句期待（{ a, b as c } 内的 as
                // 是**导入/导出别名**而非类型转换，as 规则见到要放行）
                if (t.Kind == Tok.Ident && (t.Text == "import" || t.Text == "export") &&
                    AtStatementStart(prevMeaningful, t, tokens, i, source))
                {
                    pendingNamedClause = true;
                }

                // ---- 语句级删除：interface / type 别名 / declare ----
                if (t.Kind == Tok.Ident && AtStatementStart(prevMeaningful, t, tokens, i, source))
                {
                    if (t.Text == "interface" && i + 1 < tokens.Count &&
                        tokens[i + 1].Kind == Tok.Ident)
                    {
                        i = SkipInterface(tokens, i + 2, ref lastEnd, sb, source);
                        prevMeaningful = "}";
                        prevMeaningfulWasValue = false;
                        continue;
                    }
                    // type X = …（别名）· type * from '…' / type { a } from '…'
                    // （仅类型的再导出）——都整语句删除
                    if (t.Text == "type" && i + 1 < tokens.Count &&
                        (tokens[i + 1].Kind == Tok.Ident ||
                         (tokens[i + 1].Kind == Tok.Punct && tokens[i + 1].Text is "*" or "{")))
                    {
                        i = SkipToStatementEnd(tokens, i + 1, ref lastEnd, source);
                        prevMeaningful = ";";
                        prevMeaningfulWasValue = false;
                        continue;
                    }
                    if (t.Text == "declare")
                    {
                        i = SkipDeclare(tokens, i + 1, ref lastEnd, source);
                        prevMeaningful = ";";
                        prevMeaningfulWasValue = false;
                        continue;
                    }
                }

                // export 前缀 + 类型声明（export interface/type/declare）→ 连 export 一起删
                if (t.Kind == Tok.Ident && t.Text == "export" &&
                    AtStatementStart(prevMeaningful, t, tokens, i, source) &&
                    i + 1 < tokens.Count && tokens[i + 1].Kind == Tok.Ident &&
                    tokens[i + 1].Text is "interface" or "type" or "declare")
                {
                    lastEnd = tokens[i + 1].Start; // 跳过 export（保留前导空白由上一span负责）
                    prevMeaningful = ""; // 让紧随的 interface/type/declare 视为语句起点
                    i++;
                    continue;
                }

                // ---- as / satisfies 转换 ----
                // 命名子句（import/export 的 { a, b as c }）内的 as 是别名，放行
                if (t.Kind == Tok.Ident && (t.Text == "as" || t.Text == "satisfies") &&
                    prevMeaningfulWasValue &&
                    !(frames.Count > 0 && frames[^1].IsNamedClause))
                {
                    var from = i + 1;
                    i = SkipTypeExpression(tokens, i + 1, ref lastEnd);
                    skippedTypes.Add((from, i));
                    prevMeaningful = "as";
                    prevMeaningfulWasValue = true; // 转换后仍是值
                    continue;
                }

                // ---- TS 假 this 参数：`function (this: HTMLElement) {` ----
                // this 不是可传值，仅用于给函数体标注 this 类型，整体删除名字
                // （fixit menu.ts/misc.ts 实测）。后随 : 才判——`foo(this)` 是
                // 合法的 this 实参，不能动
                if (t.Kind == Tok.Ident && t.Text == "this" && frames.Count > 0 &&
                    frames[^1].Open == '(' && i + 1 < tokens.Count &&
                    tokens[i + 1].Kind == Tok.Punct && tokens[i + 1].Text == ":")
                {
                    sb.Append(source, lastEnd, t.Start - lastEnd);
                    lastEnd = t.End;
                    prevMeaningful = "this";
                    prevMeaningfulWasValue = false;
                    prevWasIdent = false;
                    i++;
                    continue;
                }

                // ---- 非空断言 foo!.bar / foo!（行尾）----
                // 后随 . ) ] ; , } 或**换行**（语句尾）时删除；
                // `a !== b` 的 !== 是单 token 不受影响；`!foo`（表达式起始）的
                // 前一 token 不是值结尾，也不受影响。
                // **先 emit 前导 span 再跳过**：否则 token 前的换行/缩进一起丢失，
                // 无分号风格的字段/语句会粘成一行（fixit core.ts 的
                // `readonly config: T` 三字段实测被粘成 `config version isRTL`）
                if (t.Kind == Tok.Punct && t.Text == "!" && prevMeaningfulWasValue &&
                    i + 1 < tokens.Count &&
                    (tokens[i + 1].Kind == Tok.Punct &&
                     tokens[i + 1].Text is "." or "(" or ")" or "]" or ";" or "," or "}" or ":" ||
                     NewlineBetween(tokens, i, source)))
                {
                    sb.Append(source, lastEnd, t.Start - lastEnd);
                    lastEnd = t.End;
                    i++;
                    continue;
                }

                // ---- 类成员修饰符 ----
                if (t.Kind == Tok.Ident && TsModifiers.Contains(t.Text) &&
                    frames.Count > 0 && frames[^1].IsClassBody &&
                    AtStatementStart(prevMeaningful, t, tokens, i, source))
                {
                    sb.Append(source, lastEnd, t.Start - lastEnd);
                    lastEnd = t.End;
                    i++;
                    continue;
                }

                // ---- implements 子句 ----
                if (t.Kind == Tok.Ident && t.Text == "implements" && prevMeaningfulWasValue &&
                    LooksLikeImplementsClause(tokens, i))
                {
                    i = SkipImplements(tokens, i, ref lastEnd);
                    prevMeaningful = "implements";
                    prevMeaningfulWasValue = false;
                    continue;
                }

                // ---- 泛型形参/实参 <...>（function f<T>( / class A<T> / foo<T>( ）----
                // lastEnd 推到 > 的**结束**位：整段 <...>（含 >）从输出中扣除
                if (t.Kind == Tok.Punct && t.Text == "<" && prevMeaningfulWasValue &&
                    TrySkipGenericArgs(tokens, i, out var afterGeneric))
                {
                    lastEnd = tokens[afterGeneric].End;
                    i = afterGeneric + 1;
                    prevMeaningful = ">";
                    prevMeaningfulWasValue = false;
                    continue;
                }

                // ---- 类型注解（参数/字段/返回）----
                if (t.Kind == Tok.Punct && t.Text == ":" &&
                    ShouldStripColon(tokens, i, frames, prevMeaningful, prevMeaningfulWasValue, prevWasIdent, skippedTypes, source))
                {
                    // 返回类型（) 之后）剥掉后，下一个 { 是函数体而非对象字面量
                    if (prevMeaningful == ")")
                    {
                        pendingBody = true;
                    }
                    var from = i + 1;
                    i = SkipTypeExpression(tokens, i + 1, ref lastEnd);
                    skippedTypes.Add((from, i));
                    prevMeaningful = ":";
                    prevMeaningfulWasValue = false;
                    continue;
                }

                // ---- 可选参数标记 b?: T —— ? 后随 : 且在参数组内 ----
                // 删除 ? 的同时必须**撤销**框架的 Questions 计数：这是可选标记
                // 而非三元，否则紧随的 : 被误判三元、参数类型不剥
                // （fixit commentsConsent.ts 的 `consent?: State | null` 实测）
                if (t.Kind == Tok.Punct && t.Text == "?" && frames.Count > 0 &&
                    frames[^1].Open == '(' && i + 1 < tokens.Count &&
                    tokens[i + 1].Kind == Tok.Punct && tokens[i + 1].Text == ":")
                {
                    frames[^1].Questions--;
                    sb.Append(source, lastEnd, t.Start - lastEnd);
                    lastEnd = t.End;
                    i++;
                    continue;
                }

                // ---- class/function 关键字：下一个 { 是类体/函数体 ----
                // 同时清除命名子句期待：`export class Foo {` / `export default
                // function () {` 的 { 不是 import/export 命名子句
                if (t.Kind == Tok.Ident && (t.Text == "class" || t.Text == "function"))
                {
                    pendingClassBody = t.Text == "class";
                    pendingNamedClause = false;
                }

                // 普通 token：输出（含前导空白/注释）
                sb.Append(source, lastEnd, t.Start - lastEnd);
                sb.Append(t.Text);
                lastEnd = t.End;
                prevMeaningful = t.Text;
                prevWasIdent = t.Kind == Tok.Ident;
                prevMeaningfulWasValue = IsValueEnd(t);
                i++;
            }
            sb.Append(source, lastEnd, source.Length - lastEnd);
            return sb.ToString();
        }

        /// <summary>
        /// 语句起点判定：前一有意义 token 是语句边界（空/分号/括号收尾），
        /// 或本 token 前有换行（TS/JS 声明按行起排）。换行检测覆盖
        /// "import … './x' 之后紧跟 export interface" 的衔接
        /// （前一 token 是字符串字面量，不是边界符）
        /// </summary>
        private static bool AtStatementStart(
            string prev, Token t, List<Token> tokens, int i, string source)
        {
            if (prev is "" or ";" or "}" or "{")
            {
                return true;
            }
            var from = i > 0 ? tokens[i - 1].End : 0;
            for (var j = from; j < t.Start && j < source.Length; j++)
            {
                if (source[j] == '\n')
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsValueEnd(Token t) =>
            t.Kind == Tok.Ident || t.Kind == Tok.Str || t.Kind == Tok.Tmpl || t.Kind == Tok.Num ||
            (t.Kind == Tok.Punct && t.Text is ")" or "]" or "}");

        /// <summary>interface NAME [extends X] [{] —— 跳到匹配闭括号之后</summary>
        private static int SkipInterface(
            List<Token> tokens, int i, ref int lastEnd, System.Text.StringBuilder sb, string source)
        {
            // 可延续的 extends 子句与泛型形参
            while (i < tokens.Count)
            {
                var t = tokens[i];
                if (t.Kind == Tok.Punct && t.Text == "{")
                {
                    var close = MatchBraceToken(tokens, i);
                    if (close < 0)
                    {
                        return i;
                    }
                    lastEnd = tokens[close].End;
                    return close + 1;
                }
                if (t.Kind == Tok.Ident || (t.Kind == Tok.Punct &&
                    t.Text is "." or "," or "<" or ">" or "|" or "&" or "(" or ")" or "[" or "]"))
                {
                    i++;
                    continue;
                }
                return i;
            }
            return i;
        }

        /// <summary>
        /// type X = …; —— 跳到层级 0 分号或**行尾**（无分号风格）。
        /// 只认分号会把 `type Mode = 'a' | 'b'`（无分号）后的全部代码吞掉
        /// （fixit tokens.ts 实测：后续常量定义被整段删除）
        /// </summary>
        private static int SkipToStatementEnd(List<Token> tokens, int i, ref int lastEnd, string source)
        {
            var depth = 0;
            while (i < tokens.Count)
            {
                var t = tokens[i];
                if (t.Kind == Tok.Punct)
                {
                    switch (t.Text)
                    {
                        case "(" or "[" or "{" or "<": depth++; break;
                        case ")" or "]" or "}" or ">": depth--; break;
                        case ";" when depth <= 0:
                            lastEnd = t.End;
                            return i + 1;
                    }
                }
                // 行尾且下一 token 不是类型续行（| & . , 等）→ 语句结束
                if (depth <= 0 && i + 1 < tokens.Count &&
                    NewlineBetween(tokens, i, source) &&
                    !(tokens[i + 1].Kind == Tok.Punct &&
                      tokens[i + 1].Text is "|" or "&" or "." or "," or "?" or ":"))
                {
                    lastEnd = t.End;
                    return i + 1;
                }
                i++;
            }
            lastEnd = tokens.Count > 0 ? tokens[^1].End : lastEnd;
            return tokens.Count;
        }

        /// <summary>tokens[i] 与 tokens[i+1] 之间是否有换行</summary>
        private static bool NewlineBetween(List<Token> tokens, int i, string source)
        {
            var from = tokens[i].End;
            var to = i + 1 < tokens.Count ? tokens[i + 1].Start : source.Length;
            for (var j = from; j < to && j < source.Length; j++)
            {
                if (source[j] == '\n')
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>declare …：全局/模块声明跳块，其余跳到分号</summary>
        private static int SkipDeclare(List<Token> tokens, int i, ref int lastEnd, string source)
        {
            // declare global/module/namespace X { … } → 块；declare var/let/const/function/class … → 分号
            var j = i;
            while (j < tokens.Count && tokens[j].Kind == Tok.Ident)
            {
                if (tokens[j].Text is "global" or "module" or "namespace")
                {
                    // 找下一个 { 块
                    while (j < tokens.Count && !(tokens[j].Kind == Tok.Punct && tokens[j].Text == "{"))
                    {
                        j++;
                    }
                    if (j < tokens.Count)
                    {
                        var close = MatchBraceToken(tokens, j);
                        if (close < 0)
                        {
                            return j;
                        }
                        lastEnd = tokens[close].End;
                        return close + 1;
                    }
                    return j;
                }
                j++;
                break;
            }
            return SkipToStatementEnd(tokens, i, ref lastEnd, source);
        }

        /// <summary>implements 子句形态：implements 后是类型表且以 { 收尾（类头）</summary>
        private static bool LooksLikeImplementsClause(List<Token> tokens, int i)
        {
            var j = i + 1;
            var depth = 0;
            while (j < tokens.Count)
            {
                var t = tokens[j];
                if (t.Kind == Tok.Punct)
                {
                    switch (t.Text)
                    {
                        case "(" or "[" or ";":
                            return false; // 参数表/语句边界 → 不是类头
                        case "{":
                            return depth == 0;
                        case "<": depth++; break;
                        case ">": depth--; break;
                    }
                }
                j++;
            }
            return false;
        }

        private static int SkipImplements(List<Token> tokens, int i, ref int lastEnd)
        {
            // 跳到 { 之前（含 extends 后的 implements 列表）
            while (i < tokens.Count && !(tokens[i].Kind == Tok.Punct && tokens[i].Text == "{"))
            {
                i++;
            }
            if (i < tokens.Count)
            {
                lastEnd = tokens[i].Start;
            }
            return i;
        }

        /// <summary>泛型实参/形参：&lt;…&gt; 后紧跟 ( 或 { 才吃（排除 a &lt; b 比较）</summary>
        private static bool TrySkipGenericArgs(List<Token> tokens, int i, out int closeIdx)
        {
            var depth = 0;
            var j = i;
            while (j < tokens.Count)
            {
                var t = tokens[j];
                if (t.Kind == Tok.Punct)
                {
                    if (t.Text == "<")
                    {
                        depth++;
                    }
                    else if (t.Text == ">")
                    {
                        depth--;
                        if (depth == 0)
                        {
                            // > 后必须紧跟 ( 或 { （调用/声明），否则是比较运算
                            if (j + 1 < tokens.Count && tokens[j + 1].Kind == Tok.Punct &&
                                tokens[j + 1].Text is "(" or "{")
                            {
                                closeIdx = j;
                                return true;
                            }
                            closeIdx = -1;
                            return false;
                        }
                    }
                    else if (t.Text is "(" or ")" or "{" or "}" or ";")
                    {
                        closeIdx = -1;
                        return false;
                    }
                }
                if (t.Kind == Tok.Str || t.Kind == Tok.Tmpl)
                {
                    closeIdx = -1;
                    return false;
                }
                j++;
            }
            closeIdx = -1;
            return false;
        }

        /// <summary>冒号是否应剥离（参数类型/字段类型/返回类型；排除三元与对象字面量）</summary>
        private static bool ShouldStripColon(
            List<Token> tokens, int i, List<Frame> frames, string prevMeaningful,
            bool prevWasValue, bool prevWasIdent, List<(int From, int To)> skipped, string source)
        {
            // ) 之后：函数/方法**返回类型**（顶层、类体、嵌套括号内均可）。
            // 三元 `cond ? (a) : b` 也长这样，靠未配对 ? 排除
            if (prevMeaningful == ")")
            {
                return !HasUnmatchedQuestion(tokens, i, skipped, source);
            }
            if (frames.Count == 0)
            {
                // 顶层变量声明的类型注解：`const $tabs: HTMLElement[] = []`
                // （fixit modules/toc.ts 实测）。标签 `foo:` 是唯一误判形态，
                // 主题不用；三元由未配对 ? 排除
                return prevWasIdent && prevMeaningful is not "case" and not "default" &&
                    !HasUnmatchedQuestion(tokens, i, skipped, source);
            }
            var top = frames[^1];
            if (top.Open == '(')
            {
                if (top.Questions > 0)
                {
                    return false; // 三元
                }
                // 参数名后的类型注解（排除对象字面量/续接符位置）
                return prevMeaningful != "{" && prevMeaningful != "," &&
                       prevMeaningful != "(" && prevMeaningful != ";";
            }
            if (top.IsClassBody)
            {
                // 类字段类型：name: T（前 token 是字段名，且本成员无未配对 ?）
                return prevMeaningful != "{" && prevMeaningful != ";" &&
                       prevMeaningful != "(";
            }
            if (top.Open == '{' && !top.IsObjectLiteral)
            {
                // 代码块内的变量声明类型注解：`const $tabs: HTMLElement[] = []`
                // （方法体/箭头函数体内，fixit modules/code.ts 实测）。
                // case 标签的值是字符串字面量，用 prevWasIdent 排除
                return prevWasIdent && prevMeaningful is not "case" and not "default" &&
                    !HasUnmatchedQuestion(tokens, i, skipped, source);
            }
            // 对象体/数组/其他：保留（对象字面量 / case 标签）
            return false;
        }

        /// <summary>从冒号位置向前（同语句）找未配对 ? ——有三元嫌疑。
        /// **后随冒号的 ? 是可选参数标记**（`b?: T`），不是三元，须跳过</summary>
        /// <summary>
        /// 从冒号位置向前（同表达式）找未配对 ? ——有三元嫌疑。
        /// 反向扫描带**括号深度配平**：对象字面量的 } 只是表达式内部的闭括号，
        /// 不能当语句边界（`f(a ? x({k:1}) : y)` 曾因在 } 处误停而漏看 ?，
        /// 把三元的 : 当返回类型剥掉）。深度转负（遇到无配对的开括号）或分号
        /// 才是真正的语句边界。
        /// **两类 ? 不算**：① 后随冒号的可选参数标记（`b?: T`）；
        /// ② 已消费类型表达式内的条件类型问号（`A extends B ? C : D`，
        /// 区间由调用方记录）——它们长得像三元但属类型语法
        /// </summary>
        private static bool HasUnmatchedQuestion(
            List<Token> tokens, int i, List<(int From, int To)> skipped, string source)
        {
            var depth = 0;
            for (var j = i - 1; j >= 0; j--)
            {
                var t = tokens[j];
                // **语句起始关键字边界**：反向扫描遇到 const/let/var/return/if 等
                // 语句关键字（深度 0）说明已跨到另一条语句，必须停。用关键字而非
                // 换行判定：跨行三元（`detail !== undefined\n ? a\n : b`）的 ? 与 :
                // 虽在不同行但属同一表达式，不能被换行切断；而上一行语句的三元 ?
                // 与本行 : 之间必隔着语句关键字（fixit code.ts 实测两种形态）
                if (depth == 0 && t.Kind == Tok.Ident && StatementKeywords.Contains(t.Text))
                {
                    return false;
                }
                if (t.Kind != Tok.Punct)
                {
                    continue;
                }
                switch (t.Text)
                {
                    case ")" or "]" or "}":
                        depth++; // 反向：闭括号表示进入更深的表达式
                        break;
                    case "(" or "[" or "{":
                        if (depth == 0)
                        {
                            return false; // 无配对开括号 → 语句边界
                        }
                        depth--;
                        break;
                    case ";":
                        if (depth == 0)
                        {
                            return false;
                        }
                        break;
                    case "?":
                        if (depth > 0)
                        {
                            break;
                        }
                        // 可选标记（? 后紧跟 :）不计入三元
                        if (j + 1 < tokens.Count && tokens[j + 1].Kind == Tok.Punct &&
                            tokens[j + 1].Text == ":")
                        {
                            break;
                        }
                        // 条件类型内的 ?（已被类型消费器吃掉）不计入三元
                        var inSkipped = false;
                        foreach (var (from, to) in skipped)
                        {
                            if (j >= from && j < to)
                            {
                                inSkipped = true;
                                break;
                            }
                        }
                        if (inSkipped)
                        {
                            break;
                        }
                        return true;
                }
            }
            return false;
        }

        /// <summary>跳过类型表达式 token（返回跳过后的下标；lastEnd 推进到表达式末）</summary>
        private static int SkipTypeExpression(List<Token> tokens, int i, ref int lastEnd)
        {
            var depth = 0; // <> () [] {} 配平
            var expectOperand = false; // 上一 token 是类型运算符（| & . =>），下一 token 必属类型
            var consumedAny = false; // 是否已吃过类型 token（区分"类型开头的 {" 与"类型后的函数体 {"）
            var last = i - 1;
            while (i < tokens.Count)
            {
                var t = tokens[i];
                if (t.Kind == Tok.Ident && TypeOperatorKeywords.Contains(t.Text) && depth == 0)
                {
                    // extends/keyof/typeof/infer/is/in：类型运算符，后必跟类型操作数
                    expectOperand = true;
                }
                if (t.Kind == Tok.Punct)
                {
                    switch (t.Text)
                    {
                        case "(" or "[" or "<":
                            depth++;
                            break;
                        case ")":
                            if (depth == 0)
                            {
                                goto Done;
                            }
                            depth--;
                            // 函数类型 `(…) => T`：类型自有括号闭合后跟 => 要继续吃
                            // （fixit animation.ts 的 `callback?: () => void` 实测）。
                            // 返回类型后的 => 不经过这里（那跟在类型 token 之后），不受影响
                            if (depth == 0 && i + 1 < tokens.Count &&
                                tokens[i + 1].Kind == Tok.Punct && tokens[i + 1].Text == "=>")
                            {
                                expectOperand = true;
                            }
                            break;
                        case "]":
                            if (depth == 0)
                            {
                                goto Done;
                            }
                            depth--;
                            break;
                        case "}":
                            if (depth == 0)
                            {
                                goto Done;
                            }
                            depth--;
                            break;
                        case ">":
                            depth--;
                            break;
                        case "," or ";":
                            if (depth == 0)
                            {
                                goto Done;
                            }
                            break;
                        case "=" when depth == 0:
                            goto Done; // 字段默认值/函数体起点
                        case "{":
                            if (depth == 0 && consumedAny)
                            {
                                goto Done; // 类型已吃过内容，这个 { 是函数/类体
                            }
                            // 类型开头的 { = 对象类型（`let x: { a: string }`，
                            // fixit search 模块实测多行对象类型）；嵌套 { 恒为对象类型
                            depth++;
                            break;
                        case "|" or "&" or "." or "=>":
                            if (depth == 0)
                            {
                                expectOperand = true; // 联合/交叉/限定/函数类型运算符
                            }
                            break;
                    }
                }
                last = i;
                consumedAny = true;
                i++;
                // 括号内或运算符后：无条件继续（类型跨行/链式都在这里活）
                if (depth > 0 || expectOperand)
                {
                    expectOperand = false;
                    continue;
                }
                // 深度 0 且不期待操作数：仅当下一 token 是续行运算符/关键字才继续
                // （否则吃掉下一行语句——`x: Foo` 后的 `bar()` 不能被吞）。
                // `?`/`:` 在列：条件类型 `A extends B ? C : D` 的运算符在深度 0
                if (i < tokens.Count && tokens[i].Kind == Tok.Punct &&
                    tokens[i].Text is "|" or "&" or "." or "[" or "<" or "?" or ":")
                {
                    continue;
                }
                if (i < tokens.Count && tokens[i].Kind == Tok.Ident &&
                    IsTypeContinuation(tokens, i))
                {
                    continue;
                }
                goto Done;
            }
        Done:
            lastEnd = last >= 0 && last < tokens.Count ? tokens[last].End : lastEnd;
            return last + 1;
        }

        private static bool IsTypeContinuation(List<Token> tokens, int i) =>
            TypeOperatorKeywords.Contains(tokens[i].Text);

        /// <summary>类型表达式内的关键字运算符（后必跟类型操作数）</summary>
        private static readonly HashSet<string> TypeOperatorKeywords = new(StringComparer.Ordinal)
        {
            "extends", "keyof", "typeof", "infer", "is", "in",
        };

        /// <summary>语句起始关键字（三元检测的边界：反向扫描遇到即止）。
        /// 只收**语句级**关键字——await/yield/delete/typeof/void/new 是表达式
        /// 运算符，三元分支里会出现（`a ? await f() : b`），收了会误断边界</summary>
        private static readonly HashSet<string> StatementKeywords = new(StringComparer.Ordinal)
        {
            "const", "let", "var", "return", "if", "else", "for", "while", "do",
            "switch", "case", "default", "break", "continue", "throw", "try",
            "catch", "finally", "function", "class", "import", "export",
        };

        /// <summary>token 索引 → 匹配闭括号索引</summary>
        private static int MatchBraceToken(List<Token> tokens, int openIdx)
        {
            var depth = 0;
            for (var i = openIdx; i < tokens.Count; i++)
            {
                if (tokens[i].Kind != Tok.Punct)
                {
                    continue;
                }
                switch (tokens[i].Text)
                {
                    case "{": depth++; break;
                    case "}":
                        depth--;
                        if (depth == 0)
                        {
                            return i;
                        }
                        break;
                }
            }
            return -1;
        }

        private static List<Token> Tokenize(string s)
        {
            var tokens = new List<Token>();
            var i = 0;
            while (i < s.Length)
            {
                var c = s[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
                {
                    while (i < s.Length && s[i] != '\n')
                    {
                        i++;
                    }
                    continue;
                }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
                {
                    var close = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = close < 0 ? s.Length : close + 2;
                    continue;
                }
                if (c == '/' && EsmBundler.IsRegexStart(s, i))
                {
                    i = EsmBundler.SkipRegex(s, i);
                    continue;
                }
                if (c == '\'' || c == '"')
                {
                    var start = i;
                    i = SkipStringLiteral(s, i);
                    tokens.Add(new Token(Tok.Str, s[start..i], start, i));
                    continue;
                }
                if (c == '`')
                {
                    var start = i;
                    i = SkipTemplateLiteral(s, i);
                    tokens.Add(new Token(Tok.Tmpl, s[start..i], start, i));
                    continue;
                }
                if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < s.Length && char.IsAsciiDigit(s[i + 1])))
                {
                    var start = i;
                    while (i < s.Length && (char.IsAsciiLetterOrDigit(s[i]) || s[i] == '.' ||
                               (s[i] == '+' || s[i] == '-') && start < i && (s[i - 1] == 'e' || s[i - 1] == 'E')))
                    {
                        i++;
                    }
                    tokens.Add(new Token(Tok.Num, s[start..i], start, i));
                    continue;
                }
                if (char.IsAsciiLetter(c) || c == '_' || c == '$')
                {
                    var start = i;
                    while (i < s.Length && (char.IsAsciiLetterOrDigit(s[i]) || s[i] == '_' || s[i] == '$'))
                    {
                        i++;
                    }
                    tokens.Add(new Token(Tok.Ident, s[start..i], start, i));
                    continue;
                }
                // 正则字面量：整段按不透明 token（Str 语义）——正则里的
                // 引号/冒号/as 不能进规则（fixit file.ts 的
                // `/[\\/:*?"<>|\r\n]+/g` 实测）
                if (c == '/' && EsmBundler.IsRegexStart(s, i))
                {
                    var rstart = i;
                    i = EsmBundler.SkipRegex(s, i);
                    tokens.Add(new Token(Tok.Str, s[rstart..i], rstart, i));
                    continue;
                }
                // 多字符运算符
                var op = ReadOperator(s, i);
                tokens.Add(new Token(Tok.Punct, op, i, i + op.Length));
                i += op.Length;
            }
            return tokens;
        }

        private static readonly string[] MultiCharOperators =
            ["===", "!==", "=>", "?.", "??", "&&", "||", "...", "==", "!=", "<=", ">="];

        private static string ReadOperator(string s, int i)
        {
            foreach (var op in MultiCharOperators)
            {
                if (string.CompareOrdinal(s, i, op, 0, op.Length) == 0)
                {
                    return op;
                }
            }
            return s[i].ToString();
        }

        private static int SkipStringLiteral(string s, int i)
        {
            var quote = s[i++];
            while (i < s.Length)
            {
                if (s[i] == '\\')
                {
                    i += 2;
                    continue;
                }
                if (s[i] == quote)
                {
                    return i + 1;
                }
                i++;
            }
            return i;
        }

        private static int SkipTemplateLiteral(string s, int i)
        {
            i++; // 跳过 `
            while (i < s.Length)
            {
                if (s[i] == '\\')
                {
                    i += 2;
                    continue;
                }
                if (s[i] == '`')
                {
                    return i + 1;
                }
                if (s[i] == '$' && i + 1 < s.Length && s[i + 1] == '{')
                {
                    // ${...} 表达式：括号配平（嵌套模板字面量简化处理）。
                    // **depth 从 1 起**：开括号 { 已被上面的 i += 2 消费，
                    // 配对 } 应把 depth 减到 0 才收尾——从 0 起会使首个 } 后
                    // depth 变 -1、配平判不上，进而把收尾反引号误当嵌套模板
                    // 起点递归吞掉后续全部代码（实测 `code.${a}` 后的语句
                    // 全被并进模板 token，非空断言等规则静默失效）
                    var depth = 1;
                    i += 2;
                    while (i < s.Length)
                    {
                        if (s[i] == '{')
                        {
                            depth++;
                        }
                        else if (s[i] == '}')
                        {
                            depth--;
                            if (depth == 0)
                            {
                                i++;
                                break;
                            }
                        }
                        else if (s[i] == '`')
                        {
                            i = SkipTemplateLiteral(s, i);
                            continue;
                        }
                        i++;
                    }
                    continue;
                }
                i++;
            }
            return i;
        }
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
    /// <summary>
    /// 纯变换结果缓存（**构建内**生效）：toCSS/js.Build/minify/fingerprint/concat
    /// 对"同一来源 + 同一选项 + 同一内容"的重复调用直接命中。Hugo 的 resources
    /// 管道对变换结果有全局缓存；Flint 此前每页重跑——fixit 的资产分部每页调用
    /// 十几次 toCSS/js.Build，每次都要起外部 Dart Sass 进程或重新打包压缩，
    /// 单页 ~13s、700 页必然超时（实测 timeout 600s 且 0 页产出）。
    /// **ExecuteAsTemplate 不入缓存**（输出依赖页面上下文）
    /// </summary>
    private readonly ConcurrentDictionary<string, TemplateResource> _transformCache =
        new(StringComparer.Ordinal);

    /// <summary>文本变换缓存（css.Sass 的外部 Sass 编译产物）</summary>
    private readonly ConcurrentDictionary<string, string> _textCache =
        new(StringComparer.Ordinal);

    /// <summary>按显式键缓存纯变换（键已含全部输入因子）</summary>
    private TemplateResource CachedByKey(string key, Func<TemplateResource> transform)
    {
        if (_transformCache.TryGetValue(key, out var hit))
        {
            return hit;
        }
        var produced = transform();
        _transformCache[key] = produced;
        return produced;
    }

    /// <summary>文本形态的纯变换缓存（css.Sass 的编译产物是字符串而非资源）</summary>
    private string CachedText(string key, Func<string> transform)
    {
        if (_textCache.TryGetValue(key, out var hit))
        {
            return hit;
        }
        var produced = transform();
        _textCache[key] = produced;
        return produced;
    }

    /// <summary>按"变换类型 + 选项 + 来源路径 + 内容摘要"缓存纯变换</summary>
    private TemplateResource CachedTransform(
        string kind, string optionsKey, TemplateResource source, Func<TemplateResource> transform)
    {
        var content = source.Content ?? "";
        var identity = source.SourcePath is { Length: > 0 } sp ? sp : source.Name ?? "";
        return CachedByKey(
            $"{kind}|{optionsKey}|{identity}|{content.Length}:{ContentDigest(content)}",
            transform);
    }

    /// <summary>内容摘要（SHA-256 前 8 字节十六进制）——摘要碰撞即错配缓存产物，用 SHA-256 保底</summary>
    private static string ContentDigest(string content)
    {
        Span<byte> hash = stackalloc byte[32];
        System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(content), hash);
        return Convert.ToHexString(hash[..8]);
    }

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
        // **行内注释也必须在压平空白之前剥**：压平后整文件只剩一行，
        // `let x = 1; // 注释` 会把后续所有代码吞进注释（narrow 的 dock.js
        // 实测 "Unexpected end of input"——IIFE 收尾被吃掉）。正则无法区分
        // 字符串内的 `//`（URL 等），用逐字符状态机：遇字符串/模板字面量跳过，
        // 遇 `//` 跳到行尾
        s = StripJsLineComments(s);
        // **换行必须保留**：JS 的 ASI（自动分号插入）以换行为触发条件。
        // 主题 TS 多为无分号风格（fixit 的 banner.ts：`const color = '…'`
        // 换行即调 console.log），整段压成空格后 `'…' console.log(` 成为
        // 语法错误（node --check 实测 Unexpected identifier）。保留换行每行
        // 只多 1 字节，ASI 安全；行内多余空白仍压成单空格
        s = Regex.Replace(s, @"\s*\r?\n\s*", "\n");
        s = Regex.Replace(s, @"[^\S\r\n]+", " ");
        return s.Trim();
    }

    /// <summary>逐字符剥 JS 行注释（字符串/模板字面量内的 `//` 不动）</summary>
    private static string StripJsLineComments(string js)
    {
        var sb = new System.Text.StringBuilder(js.Length);
        var i = 0;
        var n = js.Length;
        while (i < n)
        {
            var c = js[i];
            if (c == '"' || c == '\'')
            {
                var quote = c;
                sb.Append(c);
                i++;
                while (i < n)
                {
                    var sc = js[i];
                    sb.Append(sc);
                    if (sc == '\\' && i + 1 < n)
                    {
                        sb.Append(js[i + 1]);
                        i += 2;
                        continue;
                    }
                    i++;
                    if (sc == quote)
                    {
                        break;
                    }
                }
                continue;
            }
            if (c == '`')
            {
                sb.Append(c);
                i++;
                while (i < n)
                {
                    var tc = js[i];
                    sb.Append(tc);
                    if (tc == '\\' && i + 1 < n)
                    {
                        sb.Append(js[i + 1]);
                        i += 2;
                        continue;
                    }
                    i++;
                    if (tc == '`')
                    {
                        break;
                    }
                }
                continue;
            }
            if (c == '/' && i + 1 < n && js[i + 1] == '/')
            {
                while (i < n && js[i] != '\n' && js[i] != '\r')
                {
                    i++;
                }
                continue;
            }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
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

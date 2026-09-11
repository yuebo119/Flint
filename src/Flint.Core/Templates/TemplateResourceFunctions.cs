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
    private bool _indexed;

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
            // 图像为二进制：不读文本内容（仅给路径与类型，宽度高度需解码时另作）
            var res = TemplateResource.Create(rel, "", BaseUrl);
            if (res.IsImage)
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
        res.Import("Concat", (string? name, params object[] items) =>
        {
            var target = string.IsNullOrEmpty(name) ? "concat.css" : name;
            var sb = new System.Text.StringBuilder();
            foreach (var it in items)
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

        res.Import("ExecuteAsTemplate", (object? value, params object[] opts) =>
        {
            // 资源作为模板渲染：Flint 侧不递归渲染（避免与主渲染管线耦合），
            // 原样返回并记录——语义差异由迁移报告标注
            return ToResource(value)?.ToScriptObject();
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
        foreach (var op in new[] { "Resize", "Fit", "Fill", "Crop", "Process" })
        {
            var opName = op;
            res.Import(opName, (object? value, string? spec) =>
            {
                var r = ToResource(value);
                if (r is null)
                {
                    return null;
                }
                var (w, h) = ParseImageSpec(spec);
                // 变换产物路径：{name}_{op}_{spec}.{ext}（确定性，供差分对比）
                var dir = Path.GetDirectoryName(r.Name)?.Replace('\\', '/') ?? "";
                var file = Path.GetFileNameWithoutExtension(r.Name);
                var ext = Path.GetExtension(r.Name);
                var suffix = string.IsNullOrEmpty(spec) ? opName.ToLowerInvariant() : opName.ToLowerInvariant() + "_" + spec.Replace('x', 'x');
                var newName = (dir.Length > 0 ? dir + "/" : "") + file + "_" + suffix + ext;
                var baseRes = TemplateResource.Create(newName, r.Content, provider.BaseUrl);
                var outRes = new TemplateResource
                {
                    Name = baseRes.Name,
                    Title = baseRes.Title,
                    ResourceType = "image",
                    MediaType = baseRes.MediaType,
                    Content = baseRes.Content,
                    RelPermalink = baseRes.RelPermalink,
                    Permalink = baseRes.Permalink,
                    Width = w > 0 ? w : r.Width,
                    Height = h > 0 ? h : r.Height
                };
                Track(outRes);
                return outRes.ToScriptObject();
            });
        }

        images.Import("Config", () => new ScriptObject());
    }

    private static void RegisterCssFunctions(ScriptObject css)
    {
        // css.Build：Hugo 的 CSS 构建（@import 内联 + 可选 minify）
        css.Import("Build", (object? value, params object[] opts) =>
        {
            var r = TemplateResource.FromScriptObject(value);
            if (r is null)
            {
                return null;
            }
            var minify = OptsFlag(opts, "minify", defaultValue: true);
            var content = r.Content;
            if (minify)
            {
                content = MinifyCss(content);
            }
            return r.With(content).ToScriptObject();
        });

        css.Import("Sass", (object? value, params object[] opts) =>
        {
            // Sass 编译由构建期 AssetPipeline 完成；模板侧保留资源引用，
            // 输出扩展名改为 .css（语义：编译后的样式引用）
            var r = TemplateResource.FromScriptObject(value);
            if (r is null)
            {
                return null;
            }
            var dir = Path.GetDirectoryName(r.Name)?.Replace('\\', '/') ?? "";
            var file = Path.GetFileNameWithoutExtension(r.Name);
            var target = (dir.Length > 0 ? dir + "/" : "") + file + ".css";
            return TemplateResource.Create(target, r.Content, "").ToScriptObject();
        });

        css.Import("PostCSS", (object? value, params object[] opts) => value);
        css.Import("TailwindCSS", (object? value, params object[] opts) => value);
        css.Import("Quoted", (string? s) => "\"" + s + "\"");
        css.Import("Unquoted", (string? s) => s ?? "");
        css.Import("ChromaStyles", () => new ScriptObject());
    }

    private static void RegisterJsFunctions(ScriptObject js)
    {
        js.Import("Build", (string? path, params object[] opts) =>
        {
            // 脚本打包由构建期 JavaScriptBundler 完成；模板侧返回资源引用
            var name = string.IsNullOrEmpty(path) ? "bundle.js" : path;
            var r = TemplateResource.Create(name, "", "");
            return r.ToScriptObject();
        });
        js.Import("Babel", (object? value, params object[] opts) => value);
        js.Import("Batch", (object? value, params object[] opts) => value);
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
        _generated.RemoveAll(x => x.RelPermalink == r.RelPermalink);
        _generated.Add(r);
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

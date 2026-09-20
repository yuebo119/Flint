// Flint 静态站点生成器
// 模板资源对象模型（Hugo resources.* 的等价物）
//
// 设计约束：
// 1. 模板函数是同步的（Scriban Import 走同步 lambda），故资源访问为同步 IO；
// 2. 内容不可变——变换返回新实例（Hugo 语义：resources 是值对象）；
// 3. AOT 安全——无反射，全部显式属性。

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Scriban.Runtime;

namespace Flint.Core.Templates;

/// <summary>
/// 模板可访问的资源对象（Hugo Resource 子集）：
/// Name/Title/ResourceType/MediaType/Content/RelPermalink/Permalink/
/// Width/Height/Data（含 Integrity）。
/// </summary>
public sealed class TemplateResource
{
    /// <summary>资源名（含相对路径，如 ananke/css/theme.css）</summary>
    public required string Name { get; init; }

    /// <summary>资源标题（缺省回退 Name）</summary>
    public string? Title { get; init; }

    /// <summary>资源类型（image/css/js/text/html/other）</summary>
    public string ResourceType { get; init; } = "other";

    /// <summary>媒体类型（text/css 等）</summary>
    public string MediaType { get; init; } = "text/plain";

    /// <summary>资源内容</summary>
    public string Content { get; init; } = "";

    /// <summary>二进制载荷（位图图像专用）。非空时站点写出端优先用它，
    /// 不经 <see cref="Content"/> 的 UTF-8 往返（会破坏 PNG/JPEG 字节）；
    /// 位图在装载期不读文本（<see cref="TemplateResourceFunctions.FileSystemResourceProvider"/>），
    /// Fill/Resize 等图像操作的产物靠此通道把**原图字节**带到输出
    /// （尺寸变换为直通——与模板级 toCSS 同策略）</summary>
    public ReadOnlyMemory<byte>? BinaryContent { get; init; }

    /// <summary>输出相对路径（发布后的 URL 路径，以 / 开头）</summary>
    public string RelPermalink { get; init; } = "";

    /// <summary>输出绝对路径</summary>
    public string Permalink { get; init; } = "";

    /// <summary>磁盘源文件路径（文件系统资源装载时设置；Sass 等需要**按文件**
    /// 编译的场景用它解析相对 @import——不存在或非文件资源时为 null）</summary>
    public string? SourcePath { get; init; }

    /// <summary>图像宽度（非图像为 0）</summary>
    public int Width { get; init; }

    /// <summary>图像高度（非图像为 0）</summary>
    public int Height { get; init; }

    /// <summary>是否为图像资源</summary>
    public bool IsImage => ResourceType == "image";

    /// <summary>指纹（Fingerprint 后非空）</summary>
    public string? Fingerprint { get; init; }

    /// <summary>是否已发布（Publish 后置位）</summary>
    public bool Published { get; init; }

    /// <summary>资源级参数（Hugo .Params）</summary>
    public IReadOnlyDictionary<string, object> Params { get; init; } =
        new Dictionary<string, object>();

    /// <summary>按类型构建资源（推断资源类型与媒体类型）</summary>
    public static TemplateResource Create(string name, string content, string baseUrl)
    {
        var ext = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
        var (type, media) = ext switch
        {
            "css" => ("css", "text/css"),
            "js" or "mjs" or "ts" => ("js", "application/javascript"),
            "json" => ("json", "application/json"),
            "html" or "htm" => ("html", "text/html"),
            "svg" or "png" or "jpg" or "jpeg" or "gif" or "webp" or "avif" or "bmp" or "tiff" =>
                ("image", "image/" + (ext == "jpg" ? "jpeg" : ext)),
            "scss" or "sass" => ("scss", "text/x-scss"),
            "xml" => ("xml", "application/xml"),
            "txt" or "md" => ("text", "text/plain"),
            _ => ("other", "application/octet-stream")
        };

        // Hugo v0.166 实测：`assets/x.css` 的 `.RelPermalink` = `/x.css`
        //（资源发布在**站根**，不是 `/assets/` 前缀）——模板链接与 CSS 内相对
        // 引用都依赖该约定（hugo-paper 的 `url(./theme.png)` 实测）
        var rel = "/" + name.TrimStart('/');
        return new TemplateResource
        {
            Name = name,
            Title = name,
            ResourceType = type,
            MediaType = media,
            Content = content,
            RelPermalink = rel,
            Permalink = baseUrl.TrimEnd('/') + rel
        };
    }

    /// <summary>同名同内容的新实例（用于变换链）</summary>
    public TemplateResource With(string? content = null, string? relPermalink = null, string? fingerprint = null) =>
        new()
        {
            Name = Name,
            Title = Title,
            ResourceType = ResourceType,
            MediaType = MediaType,
            Content = content ?? Content,
            RelPermalink = relPermalink ?? RelPermalink,
            Permalink = relPermalink is null ? Permalink : Permalink[..Math.Max(0, Permalink.Length - RelPermalink.Length)] + relPermalink,
            Width = Width,
            Height = Height,
            Fingerprint = fingerprint ?? Fingerprint,
            Published = Published,
            Params = Params,
            SourcePath = SourcePath,
            BinaryContent = BinaryContent
        };

    /// <summary>附带磁盘源路径的拷贝（文件系统资源装载用）</summary>
    public TemplateResource WithSourcePath(string path) =>
        new()
        {
            Name = Name,
            Title = Title,
            ResourceType = ResourceType,
            MediaType = MediaType,
            Content = Content,
            RelPermalink = RelPermalink,
            Permalink = Permalink,
            Width = Width,
            Height = Height,
            Fingerprint = Fingerprint,
            Published = Published,
            Params = Params,
            SourcePath = path,
            BinaryContent = BinaryContent
        };

    /// <summary>
    /// 指纹化（Hugo resources.Fingerprint）：文件名插入内容哈希
    /// （<c>theme.css</c> → <c>theme.abcdef12.css</c>），Integrity 为 sha256- base64
    /// </summary>
    public TemplateResource WithFingerprint(string algorithm = "sha256")
    {
        var bytes = Encoding.UTF8.GetBytes(Content);
        var hash = algorithm.ToLowerInvariant() switch
        {
            "md5" => MD5.HashData(bytes),
            "sha1" => SHA1.HashData(bytes),
            "sha384" => SHA384.HashData(bytes),
            "sha512" => SHA512.HashData(bytes),
            _ => SHA256.HashData(bytes)
        };
        var hex = Convert.ToHexStringLower(hash);
        var shortHash = hex[..8];

        var dir = Path.GetDirectoryName(Name)?.Replace('\\', '/') ?? "";
        var file = Path.GetFileNameWithoutExtension(Name);
        var ext = Path.GetExtension(Name);
        var hashed = (dir.Length > 0 ? dir + "/" : "") + file + "." + shortHash + ext;

        var rel = "/" + hashed;
        return With(
            relPermalink: rel,
            fingerprint: $"{algorithm.ToLowerInvariant()}-{Convert.ToBase64String(hash)}");
    }

    /// <summary>
    /// 媒体类型对象（Hugo .MediaType 是对象，含 Type/SubType/MainType/Suffixes）
    /// ——主题用 .MediaType.SubType 判断（Ananke 实测）
    /// </summary>
    private static ScriptObject BuildMediaTypeObject(string mediaType)
    {
        var parts = (mediaType ?? "").Split('/', 2);
        var main = parts.Length > 0 ? parts[0] : "";
        var sub = parts.Length > 1 ? parts[1] : "";
        var suffix = sub.Contains('+', StringComparison.Ordinal) ? sub[(sub.IndexOf('+') + 1)..] : sub;
        var o = new ScriptObject
        {
            ["type"] = main, ["Type"] = main,
            ["sub_type"] = sub, ["SubType"] = sub,
            ["main_type"] = main, ["MainType"] = main,
            ["suffix"] = suffix, ["Suffix"] = suffix
        };
        var suffixes = new ScriptArray();
        if (suffix.Length > 0)
        {
            suffixes.Add(suffix);
        }
        o["suffixes"] = suffixes;
        o["Suffixes"] = suffixes;
        return o;
    }

    /// <summary>转 ScriptObject 供模板访问（双命名：snake 与 Pascal，与既有约定一致）</summary>
    /// <summary>
    /// 图像变换成员的注入点（由 <c>BuiltinTemplateFunctions.RegisterResourceFunctions</c>
    /// 设置）：把 Resize/Fit/Fill/Crop/Process 挂到资源对象自身，
    /// 使 <c>$img.Fill "600x600"</c> 这类成员调用可用。未设置时资源对象
    /// 只有数据成员（调用变换会报 function not found）
    /// </summary>
    internal static Action<ScriptObject, TemplateResource>? AttachImageOps { get; set; }

    /// <summary>资源 → 模板对象（数据成员 + 图像变换方法族）</summary>
    public ScriptObject ToScriptObject()
    {
        var data = new ScriptObject();
        if (Fingerprint is not null)
        {
            data["Integrity"] = Fingerprint;
            data["integrity"] = Fingerprint;
        }

        var o = new ScriptObject
        {
            ["name"] = Name, ["Name"] = Name,
            ["title"] = Title, ["Title"] = Title,
            ["resource_type"] = ResourceType, ["ResourceType"] = ResourceType,
            ["media_type"] = BuildMediaTypeObject(MediaType), ["MediaType"] = BuildMediaTypeObject(MediaType),
            ["content"] = Content, ["Content"] = Content,
            ["rel_permalink"] = RelPermalink, ["RelPermalink"] = RelPermalink,
            ["permalink"] = Permalink, ["Permalink"] = Permalink,
            ["width"] = Width, ["Width"] = Width,
            ["height"] = Height, ["Height"] = Height,
            ["data"] = data, ["Data"] = data,
            ["params"] = Params, ["Params"] = Params,
            ["source_path"] = SourcePath ?? "",
            ["key"] = RelPermalink, ["Key"] = RelPermalink
        };
        // 图像变换方法族（Resize/Fit/Fill/Crop/Process）由资源命名空间注册方
        // 注入：需要 provider 与产物登记，模板侧的 <c>$img.Fill "600x600"</c>
        // 与 <c>resources.Fill $img …</c> 共用同一实现（Blowfish 实测 1580 处）
        AttachImageOps?.Invoke(o, this);
        return o;
    }

    /// <summary>从模板值还原资源对象（变换链的入参归一）</summary>
    public static TemplateResource? FromScriptObject(object? value)
    {
        if (value is TemplateResource r)
        {
            return r;
        }
        if (value is not ScriptObject o)
        {
            return null;
        }
        var name = o["name"]?.ToString();
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }
        return new TemplateResource
        {
            Name = name,
            Title = o["title"]?.ToString(),
            ResourceType = o["resource_type"]?.ToString() ?? "other",
            MediaType = o["media_type"]?.ToString() ?? "text/plain",
            Content = o["content"]?.ToString() ?? "",
            RelPermalink = o["rel_permalink"]?.ToString() ?? "",
            Permalink = o["permalink"]?.ToString() ?? "",
            Width = int.TryParse(o["width"]?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var w) ? w : 0,
            Height = int.TryParse(o["height"]?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var h) ? h : 0,
            Fingerprint = (o["data"] as ScriptObject)?["integrity"]?.ToString(),
            SourcePath = o["source_path"]?.ToString() is { Length: > 0 } sp ? sp : null
        };
    }
}

// Flint 静态站点生成器
// TemplateResource 的 Scriban 适配器（2026-10-21 架构居中于 Fluid：
// 从共享模型主体拆出，共享模型不再 using Scriban.Runtime）。
// ToScriptObject/FromScriptObject/BuildMediaTypeObject/AttachImageOps
// 只被 Scriban 侧（TemplateResourceFunctions）调用；Fluid 侧经
// FluidValue.Create(resource) + 反射访问 CLR 属性，不碰这些方法。
//
// **独立静态类而非 partial class**：IL 扫描按类型名隔离，partial class
// 会让 Scriban 方法出现在 TemplateResource 类型的 IL 中（隔离门禁误报）

using Scriban.Runtime;

namespace Flint.Core.Templates;

/// <summary>
/// TemplateResource 的 Scriban 适配器：ToScriptObject/FromScriptObject 等
/// Scriban 侧专用方法。独立类而非 partial——IL 隔离门禁按类型名扫描
/// </summary>
internal static class TemplateResourceScribanAdapter
{
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

    /// <summary>
    /// 图像变换成员的注入点（由 <c>BuiltinTemplateFunctions.RegisterResourceFunctions</c>
    /// 设置）：把 Resize/Fit/Fill/Crop/Process 挂到资源对象自身
    /// </summary>
    internal static Action<ScriptObject, TemplateResource>? AttachImageOps { get; set; }

    /// <summary>资源 → 模板对象（数据成员 + 图像变换方法族）</summary>
    public static ScriptObject ToScriptObject(this TemplateResource resource)
    {
        var data = new ScriptObject();
        if (resource.Fingerprint is not null)
        {
            data["Integrity"] = resource.Fingerprint;
            data["integrity"] = resource.Fingerprint;
        }

        var o = new ScriptObject
        {
            ["name"] = resource.Name, ["Name"] = resource.Name,
            ["title"] = resource.Title, ["Title"] = resource.Title,
            ["resource_type"] = resource.ResourceType, ["ResourceType"] = resource.ResourceType,
            ["media_type"] = BuildMediaTypeObject(resource.MediaType), ["MediaType"] = BuildMediaTypeObject(resource.MediaType),
            ["content"] = resource.Content, ["Content"] = resource.Content,
            ["rel_permalink"] = resource.RelPermalink, ["RelPermalink"] = resource.RelPermalink,
            ["permalink"] = resource.Permalink, ["Permalink"] = resource.Permalink,
            ["width"] = resource.Width, ["Width"] = resource.Width,
            ["height"] = resource.Height, ["Height"] = resource.Height,
            ["data"] = data, ["Data"] = data,
            ["params"] = resource.Params, ["Params"] = resource.Params,
            ["source_path"] = resource.SourcePath ?? "",
            ["key"] = resource.RelPermalink, ["Key"] = resource.RelPermalink
        };
        AttachImageOps?.Invoke(o, resource);
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
            Width = int.TryParse(o["width"]?.ToString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var w) ? w : 0,
            Height = int.TryParse(o["height"]?.ToString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var h) ? h : 0,
            Fingerprint = (o["data"] as ScriptObject)?["integrity"]?.ToString(),
            SourcePath = o["source_path"]?.ToString() is { Length: > 0 } sp ? sp : null
        };
    }
}

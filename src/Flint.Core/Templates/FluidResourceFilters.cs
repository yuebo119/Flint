// Flint 静态站点生成器
// Liquid 版 resources 命名空间（FLUID-MIGRATION-PLAN P4-3 全量）：
// Hugo 的 resources.Get/Match/ByType/Concat/Copy/Fingerprint/FromString/
// Minify 与 css.Sass/css.Build/js.Build（P4-4）经 Liquid 过滤器形态暴露。
// 变换核心走 TemplateAssetTransforms 服务（与 Scriban 侧单一事实源），
// 产物经渲染器 GeneratedResources 收集后由 SiteBuilder 输出阶段写盘。
//
// 过滤器形态（Liquid 惯用法，Hugo 命名空间函数的等价）：
//   {{ 'css/main.scss' | resource }}            → resources.Get
//   {{ 'css/*.css' | resources_match }}         → resources.Match
//   {{ 'css' | resources_by_type }}             → resources.ByType
//   {{ $a | resource_concat: $b, target: 'x.css' }}   → resources.Concat
//   {{ $r | resource_copy: 'other.css' }}       → resources.Copy
//   {{ $r | resource_fingerprint }}             → resources.Fingerprint
//   {{ 'name' | resource_from_string: '内容' }} → resources.FromString
//   {{ $r | resource_minify }}                  → resources.Minify
//   {{ $r | css_sass }} / {{ $r | css_build }} / {{ $r | js_build }}

using System.Collections.Concurrent;
using Fluid;
using Fluid.Values;

namespace Flint.Core.Templates;

/// <summary>
/// Liquid 资源与资产变换过滤器。资源提供者为 null 时只读类返回空；
/// 产物型变换（Concat/Fingerprint/Sass/Build 等）总是可用（内容内联或
/// 提供者读取），结果登记进渲染器的 GeneratedResources
/// </summary>
internal static class FluidResourceFilters
{
    /// <summary>AmbientValues 键：站点 baseURL（变换产物 RelPermalink 用）</summary>
    internal const string BaseUrlKey = "__flint_fluid_resource_base_url";

    /// <summary>AmbientValues 键：产物收集队列（渲染器实例的 ConcurrentQueue）</summary>
    internal const string GeneratedKey = "__flint_fluid_generated";

    /// <summary>注册资源与资产变换过滤器（渲染器构造时调用；provider 可为 null）</summary>
    public static void Register(TemplateOptions options, ITemplateResourceProvider? provider)
    {
        // ---- 只读档 ----
        options.Filters.AddFilter("resource", (input, args, ctx) => Get(input, args, ctx, provider));
        options.Filters.AddFilter("resources_get", (input, args, ctx) => Get(input, args, ctx, provider));
        options.Filters.AddFilter("resources_match", (input, args, ctx) => Match(input, args, ctx, provider));
        options.Filters.AddFilter("resources_by_type", (input, args, ctx) => ByType(input, args, ctx, provider));
        options.Filters.AddFilter("resources_bytype", (input, args, ctx) => ByType(input, args, ctx, provider));

        // ---- 产物型（P4-3 全量）----
        options.Filters.AddFilter("resource_concat", Concat);
        options.Filters.AddFilter("resources_concat", Concat);
        options.Filters.AddFilter("resource_copy", Copy);
        options.Filters.AddFilter("resources_copy", Copy);
        options.Filters.AddFilter("resource_fingerprint", Fingerprint);
        options.Filters.AddFilter("resources_fingerprint", Fingerprint);
        options.Filters.AddFilter("resource_from_string", FromString);
        options.Filters.AddFilter("resources_from_string", FromString);
        options.Filters.AddFilter("resource_minify", Minify);

        // ---- 资产变换（P4-4 服务化）----
        options.Filters.AddFilter("css_sass", CssSass);
        options.Filters.AddFilter("sass", CssSass);
        options.Filters.AddFilter("to_css", CssSass);
        options.Filters.AddFilter("css_build", CssBuild);
        options.Filters.AddFilter("js_build", JsBuild);
    }

    // ---- 只读档 ----

    private static ValueTask<FluidValue> Get(
        FluidValue input, FilterArguments args, TemplateContext ctx, ITemplateResourceProvider? provider)
    {
        if (provider is null)
        {
            return new(FluidValue.Create(null, ctx.Options));
        }
        var path = ToString(input, ctx);
        var resource = provider.Get(path);
        return new(FluidValue.Create(resource, ctx.Options));
    }

    private static ValueTask<FluidValue> Match(
        FluidValue input, FilterArguments args, TemplateContext ctx, ITemplateResourceProvider? provider)
    {
        if (provider is null)
        {
            return new(FluidValue.Create(new List<TemplateResource>(), ctx.Options));
        }
        return new(FluidValue.Create(provider.Match(ToString(input, ctx)), ctx.Options));
    }

    private static ValueTask<FluidValue> ByType(
        FluidValue input, FilterArguments args, TemplateContext ctx, ITemplateResourceProvider? provider)
    {
        if (provider is null)
        {
            return new(FluidValue.Create(new List<TemplateResource>(), ctx.Options));
        }
        return new(FluidValue.Create(provider.ByType(ToString(input, ctx)), ctx.Options));
    }

    // ---- 产物型 ----

    /// <summary>{{ $a | resource_concat: $b, target: 'x.css' }}：拼接多资源。
    /// 实参形态：位置实参为待拼接资源（或路径字符串），target 命名实参为产物名</summary>
    private static ValueTask<FluidValue> Concat(FluidValue input, FilterArguments args, TemplateContext ctx)
    {
        var baseUrl = BaseUrlOf(ctx);
        var items = new List<TemplateResource>();
        AddResource(items, input, ctx);
        // 位置实参逐个并入（FilterArguments.Count 为位置参数数，命名参数经
        // HasNamed/Item 取——target 即命名实参）
        for (var i = 0; i < args.Count; i++)
        {
            AddResource(items, args.At(i), ctx);
        }
        var target = "bundle.css";
        if (args.HasNamed("target"))
        {
            target = ToString(args["target"], ctx);
        }
        else if (items.Count > 0)
        {
            // 缺省产物名：首个资源的目录 + bundle.css
            var dir = Path.GetDirectoryName(items[0].Name)?.Replace('\\', '/');
            target = string.IsNullOrEmpty(dir) ? "bundle.css" : dir + "/bundle.css";
        }

        var result = TemplateAssetTransforms.Concat(target, items, baseUrl);
        Register(ctx, result);
        return new(FluidValue.Create(result, ctx.Options));
    }

    /// <summary>{{ $r | resource_copy: 'other.css' }}：改名资源</summary>
    private static ValueTask<FluidValue> Copy(FluidValue input, FilterArguments args, TemplateContext ctx)
    {
        var resource = ToResource(input, ctx);
        if (resource is null)
        {
            return new(FluidValue.Create(null, ctx.Options));
        }
        var target = args.Count > 0 ? ToString(args.At(0), ctx) : resource.Name;
        var result = TemplateAssetTransforms.Copy(resource, target, BaseUrlOf(ctx));
        Register(ctx, result);
        return new(FluidValue.Create(result, ctx.Options));
    }

    /// <summary>{{ $r | resource_fingerprint }}：内容哈希进文件名（可指定算法）</summary>
    private static ValueTask<FluidValue> Fingerprint(FluidValue input, FilterArguments args, TemplateContext ctx)
    {
        var resource = ToResource(input, ctx);
        if (resource is null)
        {
            return new(FluidValue.Create(null, ctx.Options));
        }
        var algorithm = args.Count > 0 ? ToString(args.At(0), ctx) : "sha256";
        var result = TemplateAssetTransforms.Fingerprint(resource, algorithm);
        Register(ctx, result);
        return new(FluidValue.Create(result, ctx.Options));
    }

    /// <summary>{{ 'name.css' | resource_from_string: '内容' }}：虚拟资源构造</summary>
    private static ValueTask<FluidValue> FromString(FluidValue input, FilterArguments args, TemplateContext ctx)
    {
        var name = ToString(input, ctx);
        var content = args.Count > 0 ? ToString(args.At(0), ctx) : "";
        var result = TemplateAssetTransforms.FromString(name, content, BaseUrlOf(ctx));
        Register(ctx, result);
        return new(FluidValue.Create(result, ctx.Options));
    }

    /// <summary>{{ $r | resource_minify }}：按资源类型压缩（css/js）</summary>
    private static ValueTask<FluidValue> Minify(FluidValue input, FilterArguments args, TemplateContext ctx)
    {
        var resource = ToResource(input, ctx);
        if (resource is null)
        {
            return new(FluidValue.Create(null, ctx.Options));
        }
        var content = resource.ResourceType switch
        {
            "css" or "scss" => TemplateAssetTransforms.MinifyCss(resource.Content),
            "js" => TemplateAssetTransforms.MinifyJs(resource.Content),
            _ => resource.Content
        };
        var result = resource.With(content);
        Register(ctx, result);
        return new(FluidValue.Create(result, ctx.Options));
    }

    // ---- 资产变换 ----

    /// <summary>{{ $r | css_sass }}：SCSS 经外部 Dart Sass CLI 编译，产物 .css。
    /// Liquid 无对象字面量，vars/targetPath 选项不可表达（Scriban 侧支持）；
    /// target 命名实参可指定产物名</summary>
    private static ValueTask<FluidValue> CssSass(FluidValue input, FilterArguments args, TemplateContext ctx)
    {
        var resource = ToResource(input, ctx) ?? FromPath(input, ctx);
        if (resource is null)
        {
            return new(FluidValue.Create(null, ctx.Options));
        }
        var target = args.HasNamed("target") ? ToString(args["target"], ctx) : null;
        var result = TemplateAssetTransforms.CompileSass(resource, BaseUrlOf(ctx), target);
        Register(ctx, result);
        return new(FluidValue.Create(result, ctx.Options));
    }

    /// <summary>{{ $r | css_build }}：CSS 构建（内容可选压缩，minify 缺省 true）</summary>
    private static ValueTask<FluidValue> CssBuild(FluidValue input, FilterArguments args, TemplateContext ctx)
    {
        var resource = ToResource(input, ctx) ?? FromPath(input, ctx);
        if (resource is null)
        {
            return new(FluidValue.Create(null, ctx.Options));
        }
        var minify = !args.HasNamed("minify") || ToBool(args["minify"], ctx);
        var result = TemplateAssetTransforms.BuildCss(resource, minify);
        Register(ctx, result);
        return new(FluidValue.Create(result, ctx.Options));
    }

    /// <summary>
    /// {{ $r | js_build }}：ES 打包 + TS 扩展名改写 + 可选压缩。
    /// minify 缺省 false（与 Scriban 侧同口径）；target 命名实参指定产物名
    /// </summary>
    private static ValueTask<FluidValue> JsBuild(FluidValue input, FilterArguments args, TemplateContext ctx)
    {
        var resource = ToResource(input, ctx) ?? FromPath(input, ctx);
        if (resource is null)
        {
            return new(FluidValue.Create(null, ctx.Options));
        }
        var minify = args.HasNamed("minify") && ToBool(args["minify"], ctx);
        var target = args.HasNamed("target") ? ToString(args["target"], ctx) : null;
        var result = TemplateAssetTransforms.BuildJs(
            resource.Name, resource.Content, BaseUrlOf(ctx),
            resource.SourcePath, target, minify);
        Register(ctx, result);
        return new(FluidValue.Create(result, ctx.Options));
    }

    // ---- 辅助 ----

    /// <summary>实参 → 资源列表元素：资源对象直用；字符串路径经提供者解析（无提供者跳过）</summary>
    private static void AddResource(List<TemplateResource> items, FluidValue value, TemplateContext ctx)
    {
        if (value.IsNil())
        {
            return;
        }
        if (value.ToObjectValue(ctx) is TemplateResource direct)
        {
            items.Add(direct);
            return;
        }
        if (value.ToObjectValue(ctx) is IEnumerable<TemplateResource> many)
        {
            items.AddRange(many);
        }
    }

    private static TemplateResource? ToResource(FluidValue value, TemplateContext ctx) =>
        value.IsNil() ? null : value.ToObjectValue(ctx) as TemplateResource;

    /// <summary>字符串路径 → 资源（经 AmbientValues 的提供者；无提供者返回 null）</summary>
    private static TemplateResource? FromPath(FluidValue value, TemplateContext ctx)
    {
        if (value.IsNil())
        {
            return null;
        }
        var path = value.ToStringValue(ctx);
        if (path.Length == 0)
        {
            return null;
        }
        return ctx.AmbientValues.TryGetValue(ProviderKey, out var p) && p is ITemplateResourceProvider provider
            ? provider.Get(path)
            : null;
    }

    /// <summary>AmbientValues 键：资源提供者（路径形态过滤器经它解析）</summary>
    internal const string ProviderKey = "__flint_fluid_resource_provider";

    private static string BaseUrlOf(TemplateContext ctx) =>
        ctx.AmbientValues.TryGetValue(BaseUrlKey, out var v) && v is string s && s.Length > 0
            ? s
            : "http://localhost:1313/";

    /// <summary>产物登记：写进渲染器的 GeneratedResources 收集队列（SiteBuilder 输出阶段落盘）</summary>
    private static void Register(TemplateContext ctx, TemplateResource resource)
    {
        if (ctx.AmbientValues.TryGetValue(GeneratedKey, out var q) && q is ConcurrentQueue<TemplateResource> queue)
        {
            queue.Enqueue(resource);
        }
    }

    private static string ToString(FluidValue value, TemplateContext ctx) =>
        value.IsNil() ? "" : value.ToStringValue(ctx);

    private static bool ToBool(FluidValue value, TemplateContext ctx)
    {
        if (value.IsNil())
        {
            return false;
        }
        var obj = value.ToObjectValue(ctx);
        return obj switch
        {
            bool b => b,
            string s => bool.TryParse(s, out var parsed) && parsed,
            _ => false
        };
    }
}

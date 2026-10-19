// Flint 静态站点生成器
// Fluid 侧 Liquid 过滤器（FLUID-MIGRATION-PLAN P4-2 首批）：abs_url/rel_url
// 先行——Hugo 内置 partial（twitter_cards 的图片 absolutize）依赖它们，
// 语义逐字对齐 Scriban 侧 BuiltinTemplateFunctions.AbsUrl/RelUrl
// （baseURL origin + 子路径前缀 + 路径，绝对 URL 原样返回）。

using System.Globalization;
using Fluid;
using Fluid.Values;

namespace Flint.Core.Templates;

/// <summary>
/// Fluid 过滤器注册：URL 家族（abs_url/absURL/rel_url/relURL）。
/// 过滤器是 Fluid 的扩展点（FilterCollection.AddFilter），不经反射，AOT 安全
/// </summary>
internal static class FluidTemplateFilters
{
    /// <summary>注册全部 Liquid 过滤器（渲染器构造时调用一次）</summary>
    public static void Register(TemplateOptions options, string baseUrl)
    {
        options.Filters.AddFilter("abs_url", (input, args, ctx) => AbsUrl(input, ctx, baseUrl));
        options.Filters.AddFilter("absURL", (input, args, ctx) => AbsUrl(input, ctx, baseUrl));
        options.Filters.AddFilter("absurl", (input, args, ctx) => AbsUrl(input, ctx, baseUrl));
        options.Filters.AddFilter("rel_url", (input, args, ctx) => RelUrl(input, ctx, baseUrl));
        options.Filters.AddFilter("relURL", (input, args, ctx) => RelUrl(input, ctx, baseUrl));
        options.Filters.AddFilter("relurl", (input, args, ctx) => RelUrl(input, ctx, baseUrl));
    }

    private static ValueTask<FluidValue> AbsUrl(FluidValue input, FilterArguments args, TemplateContext ctx) =>
        new(FluidValue.Create(AbsUrl(ToString(input, ctx), BaseUrl(ctx)), ctx.Options));

    private static ValueTask<FluidValue> RelUrl(FluidValue input, FilterArguments args, TemplateContext ctx) =>
        new(FluidValue.Create(RelUrl(ToString(input, ctx), BaseUrl(ctx)), ctx.Options));

    /// <summary>baseUrl 经 AmbientValues 传入（每渲染上下文可覆盖；缺省用构造期站点 baseURL）</summary>
    private static string BaseUrl(TemplateContext ctx) =>
        ctx.AmbientValues.TryGetValue(BaseUrlKey, out var v) && v is string s && s.Length > 0
            ? s
            : FallbackBaseUrl;

    /// <summary>渲染上下文的 baseURL  AmbientValues 键（CreateFluidContext 设置）</summary>
    internal const string BaseUrlKey = "__flint_fluid_base_url";

    /// <summary>静态回落 baseURL（未经 CreateFluidContext 的直调路径，如内置模板单测）</summary>
    internal static string FallbackBaseUrl = "http://localhost:1313/";

    private static string ToString(FluidValue value, TemplateContext ctx) =>
        value.IsNil() ? "" : value.ToStringValue(ctx);

    // ---- 语义逐字对齐 BuiltinTemplateFunctions（Scriban 侧） ----

    /// <summary>绝对 URL：拼到 baseURL（Hugo absURL 语义）；空路径返回 baseURL 本身</summary>
    private static string AbsUrl(string? path, string baseUrl) =>
        string.IsNullOrEmpty(path) ? baseUrl
        : IsAbsoluteUrl(path) ? path
        : OriginOf(baseUrl) + RelUrl(path, baseUrl);

    /// <summary>相对 URL：baseURL 子路径前缀 + 路径（子路径只含一份，避免双前缀）</summary>
    private static string RelUrl(string? path, string baseUrl)
    {
        var prefix = BasePathPrefix(baseUrl);
        if (string.IsNullOrEmpty(path))
        {
            return prefix.Length > 0 ? prefix + "/" : "/";
        }
        if (IsAbsoluteUrl(path))
        {
            return path;
        }
        return prefix + "/" + path.TrimStart('/');
    }

    /// <summary>baseURL 的 origin 部分（scheme://host[:port]）</summary>
    private static string OriginOf(string baseUrl)
    {
        if (!baseUrl.Contains("://", StringComparison.Ordinal))
        {
            return baseUrl;
        }
        var schemeEnd = baseUrl.IndexOf("://", StringComparison.Ordinal) + 3;
        var slash = baseUrl.IndexOf('/', schemeEnd);
        return slash > 0 ? baseUrl[..slash] : baseUrl;
    }

    /// <summary>baseURL 的子路径前缀（无子路径时为空串；如 /fixit）</summary>
    private static string BasePathPrefix(string baseUrl)
    {
        var schemeIdx = baseUrl.IndexOf("://", StringComparison.Ordinal);
        var pathStart = schemeIdx >= 0 ? baseUrl.IndexOf('/', schemeIdx + 3) : 0;
        var path = pathStart >= 0 ? baseUrl[pathStart..] : "";
        return path.TrimEnd('/');
    }

    private static bool IsAbsoluteUrl(string path) =>
        path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}

// Flint 静态站点生成器
// 模板资产变换服务（FLUID-MIGRATION-PLAN P4-3/P4-4 服务化）：
// resources/css/js 的**产物型**变换（Concat/Copy/Fingerprint/FromString/
// Minify/Sass/Build）的引擎无关核心。Scriban 侧（TemplateResourceFunctions
// 的命名空间函数）与 Fluid 侧（FluidResourceFilters 的过滤器）共用本服务，
// 两侧各自只做参数形态解析（Scriban 的 params 数组 vs Liquid 的过滤器实参），
// 变换语义单一事实源在此。
//
// baseUrl 逐调用传入（不用静态）：多站点并行构建（集成测试 in-process）时
// 静态 baseUrl 会串站点——与 Scriban 侧实例级 _baseUrl 同口径。
//
// 产物登记：变换结果经调用方（渲染器）的 GeneratedResources 收集，由
// SiteBuilder 输出阶段写盘——否则模板引用的 RelPermalink 404。
//
// **无 Scriban 依赖**（2026-10-21 架构居中于 Fluid）：CompileSass 去掉
// ScriptObject 参数（Scriban 侧的 vars 形态在调用前转为 varsPrelude 文本），
// MinifyCss 直接内联实现，MinifyJs/BuildJs 经 AssetTransformCore 访问
// （签名干净的纯 string/IO 方法，不涉及 Scriban 类型）

using SixLabors.ImageSharp.Processing;

namespace Flint.Core.Templates;

/// <summary>
/// 资产变换服务：css/js 编译打包压缩与资源派生（指纹/拼接/构造）。
/// 全部静态无状态；外部 Sass CLI 调用与 EsmBundler 均为进程级纯函数
/// </summary>
public static class TemplateAssetTransforms
{
    /// <summary>CSS 压缩（直接内联实现，无 Scriban 依赖——2026-10-21 抽离）</summary>
    public static string MinifyCss(string css)
    {
        if (string.IsNullOrEmpty(css))
        {
            return "";
        }
        var s = System.Text.RegularExpressions.Regex.Replace(css, @"/\*.*?\*/", "", System.Text.RegularExpressions.RegexOptions.Singleline);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s*([{};:,>])\s*", "$1");
        return s.Trim();
    }

    /// <summary>JS 压缩（委派 BuiltinTemplateFunctions——实现含 StripJsLineComments
    /// 等复杂辅助方法，搬移风险大于收益；签名干净不涉及 Scriban 类型）</summary>
    public static string MinifyJs(string js) => BuiltinTemplateFunctions.MinifyJs(js);

    /// <summary>
    /// css.Build 核心：资源内容可选压缩（@import 内联由 AssetPipeline 负责）
    /// </summary>
    public static TemplateResource BuildCss(TemplateResource resource, bool minify) =>
        resource.With(minify ? MinifyCss(resource.Content) : resource.Content);

    /// <summary>
    /// css.Sass/toCSS 核心：SCSS/Sass 经外部 Dart Sass CLI 编译（找不到 sass
    /// 或编译失败时回退内容直通，差异登记）；产物名按 targetPath 或缺省
    /// 同目录 .css。vars 仅 Scriban 侧可表达（ScriptObject 键值映射）；
    /// **Liquid 侧用 varsPrelude**——SCSS 变量定义前导文本（如
    /// <c>"$primary: #333;"</c>），编译前拼接到源前，覆盖同一用例且
    /// Liquid 原生可表达。Scriban 侧调用时先把 ScriptObject vars 转为
    /// varsPrelude 文本再传入（2026-10-21 抽离 ScriptObject 依赖）
    /// </summary>
    public static TemplateResource CompileSass(
        TemplateResource resource,
        string baseUrl,
        string? targetPath = null,
        string? varsPrelude = null)
    {
        var css = resource.Content;
        var ext = resource.SourcePath is { } sp ? Path.GetExtension(sp) : null;
        var compilable = resource.SourcePath is { } source && File.Exists(source) &&
                         (ext == ".scss" || ext == ".sass" || ext == ".css");
        if (compilable)
        {
            // vars 前导文本拼到源前（Hugo 的 hugo:vars 虚拟导入的 Flint 等价；
            // 只在编译路径生效——回退直通时不改变原内容）
            if (!string.IsNullOrEmpty(varsPrelude))
            {
                css = varsPrelude!.TrimEnd() + "\n" + css;
            }
            var loadPath = Path.GetDirectoryName(resource.SourcePath!);
            var indented = ext == ".sass";
            css = BuiltinTemplateFunctions.TryCompileWithSassCli(
                      css, loadPath, indented) ?? css;
        }

        var target = targetPath;
        if (string.IsNullOrEmpty(target))
        {
            var dir = Path.GetDirectoryName(resource.Name)?.Replace('\\', '/') ?? "";
            var file = Path.GetFileNameWithoutExtension(resource.Name);
            target = (dir.Length > 0 ? dir + "/" : "") + file + ".css";
        }

        // 编译产物是新资源（名字变 .css、内容变编译结果），RelPermalink 按
        // 站根约定重算（TemplateResource.Create 的 baseURL 子路径语义）
        return TemplateResource.Create(target, css, baseUrl);
    }

    /// <summary>
    /// js.Build 核心：ES module 打包（EsmBundler）+ TypeScript 入口扩展名改写
    /// （.ts → .js）+ 可选 MinifyJs；targetPath 决定产物名。Flint 无 esbuild，
    /// 打包为轻量 import 解析拼接（语义同 Scriban 侧 js.Build）
    /// </summary>
    public static TemplateResource BuildJs(
        string name,
        string content,
        string baseUrl,
        string? entryPath = null,
        string? targetPath = null,
        bool minify = false,
        bool esmOutput = false,
        string? paramsJson = null)
    {
        var outName = name;
        if (!string.IsNullOrEmpty(targetPath))
        {
            outName = targetPath;
        }
        else if (BuiltinTemplateFunctions.TypeScriptEntry(name) ||
                 BuiltinTemplateFunctions.TypeScriptEntry(entryPath))
        {
            // Hugo 语义：targetPath 缺省时按 MIME 改扩展名（.ts 产物是 .js）
            outName = Path.ChangeExtension(name, ".js");
        }

        var output = content;
        var needsBundle = output.Contains("import ", StringComparison.Ordinal) ||
                          output.Contains("export ", StringComparison.Ordinal);
        var needsStrip = BuiltinTemplateFunctions.TypeScriptEntry(entryPath);
        if (entryPath is not null && File.Exists(entryPath) && (needsBundle || needsStrip))
        {
            var bundled = BuiltinTemplateFunctions.EsmBundler.Bundle(entryPath, output, paramsJson, esmOutput);
            if (bundled is not null)
            {
                output = bundled;
            }
        }

        if (minify)
        {
            output = MinifyJs(output);
        }

        return TemplateResource.Create(outName, output, baseUrl);
    }

    /// <summary>resources.Fingerprint 核心：内容哈希进文件名（theme.css → theme.abcdef12.css）</summary>
    public static TemplateResource Fingerprint(TemplateResource resource, string algorithm = "sha256") =>
        resource.WithFingerprint(algorithm);

    /// <summary>
    /// resources.Concat 核心：多资源内容拼接为新资源（target 为产物名）。
    /// Hugo 的 Concat [TARGET] RESOURCES... 参数序由调用方解析
    /// </summary>
    public static TemplateResource Concat(string target, IReadOnlyList<TemplateResource> resources, string baseUrl)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var r in resources)
        {
            sb.Append(r.Content);
        }
        return TemplateResource.Create(target, sb.ToString(), baseUrl);
    }

    /// <summary>resources.Copy 核心：改名资源（内容不变）</summary>
    public static TemplateResource Copy(TemplateResource resource, string target, string baseUrl) =>
        TemplateResource.Create(target, resource.Content, baseUrl);

    /// <summary>resources.FromString 核心：虚拟资源构造（内容内联）</summary>
    public static TemplateResource FromString(string name, string content, string baseUrl) =>
        TemplateResource.Create(name, content, baseUrl);

    // ---- 图像几何变换（Liquid 侧真像素处理，2026-11-20）----
    //
    // 与 Scriban 侧的"登记直通"（TemplateResourceFunctions.ApplyImageOp）有意分流：
    // 兼容路径保证 Hugo 主题不断链（命名占位 + 原图字节），本路径（Liquid 新站点）
    // 真解码→变换→编码，产物尺寸真实。解码白名单与 ImageProcessor 一致
    //（3.1.12 的 6 个安全公告里 4 个在 TIFF 路径，白名单是攻击面缓解，两处必须同步）。

    /// <summary>变换结果构建内缓存：同一变换（op+spec+源内容指纹）全构建只处理
    /// 一次（同一头像被多页引用的常见形态）；跨构建持久化由 --cache 的
    /// "未变化产物免写盘"承担</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, TemplateResource>
        ImageTransformCache = new();

    /// <summary>
    /// 图像几何变换（resize/fit/fill/crop）：真像素处理，产物内容指纹命名。
    /// 语义：<c>resize</c>/<c>fit</c> 等比缩放（单维按比例，两维 contain 框内）；
    /// <c>fill</c> cover（等比覆盖 + 居中裁剪到精确两维，缺维按原比例补齐）；
    /// <c>crop</c> 从原图中心裁出目标比例的最大矩形（不放大不缩小）。
    /// spec：<c>"300x200"</c> / <c>"300x"</c> / <c>"x200"</c>，尾部可带格式 token
    ///（<c>"300x200 webp"</c>，webp/png/jpg/gif）。SVG 等文本图像返回 null
    /// </summary>
    public static TemplateResource? TransformImage(
        TemplateResource resource,
        string opName,
        string? spec,
        string baseUrl,
        Func<string, ReadOnlyMemory<byte>?>? readBytes)
    {
        // 源字节：内联字节优先，位图资源装载期不读文本（Content 为空）时经
        // 提供者读盘；纯文本资源（svg）无像素可变换
        ReadOnlyMemory<byte> sourceBytes;
        if (resource.BinaryContent is { Length: > 0 } inline)
        {
            sourceBytes = inline;
        }
        else if (resource.Content.Length > 0)
        {
            return null;
        }
        else if (readBytes?.Invoke(resource.Name) is { Length: > 0 } fromDisk)
        {
            sourceBytes = fromDisk;
        }
        else
        {
            return null;
        }

        var parsed = ParseImageSpec(spec);
        if (parsed.Width is null && parsed.Height is null)
        {
            return null;
        }

        // 缓存 key：op + spec + 源内容指纹（前 16 字节 hex）
        Span<byte> hash = stackalloc byte[32];
        System.Security.Cryptography.SHA256.HashData(sourceBytes.Span, hash);
        var key = opName + "|" + spec + "|" + Convert.ToHexString(hash[..16]);
        return ImageTransformCache.GetOrAdd(key,
            _ => ProcessImage(resource, opName, parsed, baseUrl, sourceBytes));
    }

    /// <summary>探测图像尺寸（白名单内位图；文本图像/不可识别返回 null）——srcset 过滤器用它滤掉不小于原宽的档位</summary>
    public static (int Width, int Height)? ProbeImageSize(TemplateResource resource, Func<string, ReadOnlyMemory<byte>?>? readBytes)
    {
        var bytes = resource.BinaryContent is { Length: > 0 } b
            ? b
            : resource.Content.Length > 0 ? default : readBytes?.Invoke(resource.Name) ?? default;
        if (bytes.Length == 0)
        {
            return null;
        }
        try
        {
            var info = SixLabors.ImageSharp.Image.Identify(bytes.Span);
            return info is null ? null : (info.Width, info.Height);
        }
        catch (SixLabors.ImageSharp.UnknownImageFormatException)
        {
            return null;
        }
    }

    /// <summary>spec 解析："300x200 webp" → (300, 200, webp)；未给的维为 null</summary>
    internal static (int? Width, int? Height, string? Format) ParseImageSpec(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
        {
            return (null, null, null);
        }
        var parts = spec.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var sizeToken = parts[0];
        string? format = parts.Length > 1 ? parts[^1].ToLowerInvariant() : null;
        var x = sizeToken.IndexOf('x');
        int? w = null;
        int? h = null;
        if (x < 0)
        {
            w = ParseDim(sizeToken);
        }
        else
        {
            if (x > 0)
            {
                w = ParseDim(sizeToken[..x]);
            }
            if (x < sizeToken.Length - 1)
            {
                h = ParseDim(sizeToken[(x + 1)..]);
            }
        }
        if (format is not ("webp" or "png" or "jpg" or "gif"))
        {
            format = null;
        }
        return (w, h, format);

        static int? ParseDim(string s) =>
            int.TryParse(s, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var n) && n > 0 ? n : null;
    }

    private static TemplateResource ProcessImage(
        TemplateResource source,
        string opName,
        (int? Width, int? Height, string? Format) spec,
        string baseUrl,
        ReadOnlyMemory<byte> bytes)
    {
        using var image = LoadWhitelisted(bytes);
        var (ow, oh) = (image.Width, image.Height);
        var op = opName.ToLowerInvariant();

        int outW;
        int outH;
        if (op is "resize" or "fit")
        {
            // 等比：单维按比例；两维 contain（取较小缩放比）
            double scale;
            if (spec.Width is { } w && spec.Height is { } h)
            {
                scale = Math.Min((double)w / ow, (double)h / oh);
            }
            else if (spec.Width is { } onlyW)
            {
                scale = (double)onlyW / ow;
            }
            else
            {
                scale = (double)spec.Height!.Value / oh;
            }
            outW = Math.Max(1, (int)Math.Round(ow * scale));
            outH = Math.Max(1, (int)Math.Round(oh * scale));
            image.Mutate(ctx => ctx.Resize(outW, outH, SixLabors.ImageSharp.Processing.KnownResamplers.Lanczos3));
        }
        else if (op is "fill")
        {
            // cover：缺维按原比例补齐，等比覆盖后居中裁到精确尺寸
            var targetW = spec.Width ?? (int)Math.Round(ow * (spec.Height!.Value / (double)oh));
            var targetH = spec.Height ?? (int)Math.Round(oh * (spec.Width!.Value / (double)ow));
            var cover = Math.Max((double)targetW / ow, (double)targetH / oh);
            var coverW = Math.Max(targetW, (int)Math.Ceiling(ow * cover));
            var coverH = Math.Max(targetH, (int)Math.Ceiling(oh * cover));
            image.Mutate(ctx => ctx
                .Resize(coverW, coverH, SixLabors.ImageSharp.Processing.KnownResamplers.Lanczos3)
                .Crop(new SixLabors.ImageSharp.Rectangle(
                    (coverW - targetW) / 2, (coverH - targetH) / 2, targetW, targetH)));
            outW = targetW;
            outH = targetH;
        }
        else
        {
            // crop：中心裁出目标比例的最大矩形（不放大不缩小）
            var ratioW = spec.Width ?? ow;
            var ratioH = spec.Height ?? oh;
            var scale = Math.Min(ow / (double)ratioW, oh / (double)ratioH);
            var rectW = Math.Min(ow, (int)Math.Round(ratioW * scale));
            var rectH = Math.Min(oh, (int)Math.Round(ratioH * scale));
            image.Mutate(ctx => ctx.Crop(new SixLabors.ImageSharp.Rectangle(
                (ow - rectW) / 2, (oh - rectH) / 2, rectW, rectH)));
            outW = rectW;
            outH = rectH;
        }

        // 编码：格式 token 或原格式（同格式重编码，质量 85 与构建期管线一致）
        using var ms = new System.IO.MemoryStream();
        var (encoder, mediaType, ext) = GetEncoder(spec.Format, bytes.Span);
        image.Save(ms, encoder);
        var outBytes = ms.ToArray();

        // 指纹命名（同参数同内容 = 同名，--cache 二次构建免写盘）
        var nameHash = System.Security.Cryptography.SHA256.HashData(outBytes);
        var file = System.IO.Path.GetFileNameWithoutExtension(source.Name);
        var dir = System.IO.Path.GetDirectoryName(source.Name)?.Replace((char)92, '/') ?? "";
        var safeSpec = (spec.Width?.ToString() ?? "") + "x" + (spec.Height?.ToString() ?? "");
        var newName = (dir.Length > 0 ? dir + "/" : "")
            + file + "_" + op + "_" + safeSpec.Replace(' ', '_')
            + "_" + Convert.ToHexString(nameHash)[..8].ToLowerInvariant() + ext;
        var created = TemplateResource.Create(newName, "", baseUrl);
        return new TemplateResource
        {
            Name = created.Name,
            Title = created.Title,
            ResourceType = "image",
            MediaType = mediaType,
            Content = "",
            BinaryContent = outBytes,
            RelPermalink = created.RelPermalink,
            Permalink = created.Permalink,
            Width = outW,
            Height = outH
        };
    }

    /// <summary>白名单解码（与 ImageProcessor.LoadImageAsync 同清单：png/jpeg/gif/webp/bmp）</summary>
    private static SixLabors.ImageSharp.Image LoadWhitelisted(ReadOnlyMemory<byte> bytes)
    {
        var span = bytes.Span;
        var format = SixLabors.ImageSharp.Image.DetectFormat(span);
        if (format is not (SixLabors.ImageSharp.Formats.Png.PngFormat
            or SixLabors.ImageSharp.Formats.Jpeg.JpegFormat
            or SixLabors.ImageSharp.Formats.Gif.GifFormat
            or SixLabors.ImageSharp.Formats.Webp.WebpFormat
            or SixLabors.ImageSharp.Formats.Bmp.BmpFormat))
        {
            throw new InvalidOperationException($"不支持的图像格式（仅允许 png/jpeg/gif/webp/bmp）：{format?.Name ?? "未知"}");
        }
        return SixLabors.ImageSharp.Image.Load(span);
    }

    private static (SixLabors.ImageSharp.Formats.IImageEncoder Encoder, string MediaType, string Ext)
        GetEncoder(string? format, ReadOnlySpan<byte> source)
    {
        var effective = format
            ?? (SixLabors.ImageSharp.Image.DetectFormat(source)?.Name?.ToLowerInvariant() switch
            {
                "png" => "png",
                "gif" => "gif",
                "webp" => "webp",
                _ => "jpg",
            });
        return effective switch
        {
            "png" => (new SixLabors.ImageSharp.Formats.Png.PngEncoder
            {
                CompressionLevel = SixLabors.ImageSharp.Formats.Png.PngCompressionLevel.BestCompression
            }, "image/png", ".png"),
            "webp" => (new SixLabors.ImageSharp.Formats.Webp.WebpEncoder
            {
                Quality = 85, FileFormat = SixLabors.ImageSharp.Formats.Webp.WebpFileFormatType.Lossy
            }, "image/webp", ".webp"),
            "gif" => (new SixLabors.ImageSharp.Formats.Gif.GifEncoder(), "image/gif", ".gif"),
            _ => (new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = 85 }, "image/jpeg", ".jpg"),
        };
    }
}

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

using Scriban.Runtime;

namespace Flint.Core.Templates;

/// <summary>
/// 资产变换服务：css/js 编译打包压缩与资源派生（指纹/拼接/构造）。
/// 全部静态无状态；外部 Sass CLI 调用与 EsmBundler 均为进程级纯函数
/// </summary>
public static class TemplateAssetTransforms
{
    /// <summary>CSS 压缩（委派 Scriban 侧既有实现，单一事实源）</summary>
    public static string MinifyCss(string css) => BuiltinTemplateFunctions.MinifyCss(css);

    /// <summary>JS 压缩（同上）</summary>
    public static string MinifyJs(string js) => BuiltinTemplateFunctions.MinifyJs(js);

    /// <summary>
    /// css.Build 核心：资源内容可选压缩（@import 内联由 AssetPipeline 负责）
    /// </summary>
    public static TemplateResource BuildCss(TemplateResource resource, bool minify) =>
        resource.With(minify ? MinifyCss(resource.Content) : resource.Content);

    /// <summary>
    /// css.Sass/toCSS 核心：SCSS/Sass 经外部 Dart Sass CLI 编译（找不到 sass
    /// 或编译失败时回退内容直通，差异登记）；产物名按 targetPath 或缺省
    /// 同目录 .css。vars 仅 Scriban 侧可表达（ScriptObject），Liquid 路径传 null
    /// </summary>
    public static TemplateResource CompileSass(
        TemplateResource resource,
        string baseUrl,
        string? targetPath = null,
        ScriptObject? vars = null,
        object? varsInternal = null)
    {
        var css = resource.Content;
        var ext = resource.SourcePath is { } sp ? Path.GetExtension(sp) : null;
        var compilable = resource.SourcePath is { } source && File.Exists(source) &&
                         (ext == ".scss" || ext == ".sass" || ext == ".css");
        if (compilable)
        {
            var loadPath = Path.GetDirectoryName(resource.SourcePath!);
            var indented = ext == ".sass";
            css = BuiltinTemplateFunctions.TryCompileWithSassCli(
                      css, loadPath, indented, vars, varsInternal) ?? css;
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
}

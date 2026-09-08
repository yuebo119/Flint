// Flint 静态站点生成器
// 文件系统模板加载器（从 ScribanTemplateRenderer.cs 按 partial 拆出的独立类）

using Scriban.Runtime;
using Scriban.Parsing;
using Flint.Core.Abstractions;

namespace Flint.Core.Templates;

/// <summary>
/// 文件系统模板加载器
/// </summary>
internal sealed class FileTemplateLoader : ITemplateLoader
{
    private readonly string _basePath;
    // include 的主题回退目录（与页面模板查找同序：站点优先，主题按序回退）
    private readonly string[] _themeBasePaths;

    public FileTemplateLoader(string basePath, params string[] themeBasePaths)
    {
        _basePath = basePath;
        _themeBasePaths = themeBasePaths.Where(p => !string.IsNullOrEmpty(p)).ToArray();
    }

    private string[] Roots
    {
        get
        {
            var roots = new List<string> { _basePath };
            roots.AddRange(_themeBasePaths);
            return [.. roots];
        }
    }

    /// <summary>
    /// Scriban include 的路径解析入口：按根序（站点→主题）探测候选形态，
    /// 返回实际存在的物理路径——主题回退必须在此完成（Scriban 先 GetPath
    /// 后 Load，Load 拿到的已是探测结果）
    /// </summary>
    public string GetPath(Scriban.TemplateContext context, SourceSpan callerSpan, string templateName)
    {
        var relativeForms = new List<string> { templateName, templateName + ".html" };
        // _default/ 形态（对齐 ResolveTemplatePath 的搜索序）
        relativeForms.Add(Path.Combine("_default", templateName));
        relativeForms.Add(Path.Combine("_default", templateName + ".html"));
        var hasPartialsPrefix = templateName.StartsWith("partials/", StringComparison.OrdinalIgnoreCase) ||
                                templatePathStartsWithBackslash(templateName);
        if (!hasPartialsPrefix)
        {
            relativeForms.Add(Path.Combine("partials", templateName));
            relativeForms.Add(Path.Combine("partials", templateName + ".html"));
        }

        foreach (var root in Roots)
        {
            foreach (var form in relativeForms)
            {
                var candidate = Path.Combine(root, form);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        // 未命中：返回主根形态，让 Load 抛出带上下文的 FileNotFoundException
        return Path.Combine(_basePath, templateName);
    }

    private static bool templatePathStartsWithBackslash(string templateName) =>
        templateName.StartsWith("partials\\", StringComparison.OrdinalIgnoreCase);

    public string Load(Scriban.TemplateContext context, SourceSpan callerSpan, string templatePath)
    {
        // include 路径源自模板内容，读取前校验仍在任一根内——
        // 与 DevServer 静态服务的防穿越标准对齐，"..\" 类路径不再读出模板目录。
        // 无条件 GetFullPath：模板根为相对路径时组合产物非 rooted，
        // 以 IsPathRooted 为前提会让 "{{ include \"../..\" }}" 完全绕过校验
        var fullPath = Path.GetFullPath(templatePath);
        var allowedRoots = Roots.Select(r => Path.GetFullPath(r)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar).ToList();
        if (!allowedRoots.Any(allowed =>
                fullPath.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)))
        {
            throw new FileNotFoundException($"模板 include 路径越出模板目录，已拒绝: {templatePath}");
        }

        // 渲染期依赖收集（T4.1）：include/partial 实际命中的物理路径
        RenderDependencyTracker.Track(context, fullPath);
        return File.ReadAllText(fullPath);
    }

    // Scriban 7 的 ITemplateLoader.LoadAsync 返回注解为 ValueTask<string?>
    public ValueTask<string?> LoadAsync(Scriban.TemplateContext context, SourceSpan callerSpan, string templatePath)
    {
        return new ValueTask<string?>(Load(context, callerSpan, templatePath));
    }
}

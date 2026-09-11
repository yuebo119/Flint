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
    /// 内置模板的 include 哨兵前缀：include "pagination" 等 Hugo 内置模板
    /// 在文件系统中无对应文件，命中时以该前缀回传给 <see cref="Load"/> 取内置内容
    /// </summary>
    internal const string BuiltinPrefix = "\u0001builtin:";

    /// <summary>
    /// Scriban include 的路径解析入口：按根序（站点→主题）探测候选形态，
    /// 返回实际存在的物理路径——主题回退必须在此完成（Scriban 先 GetPath
    /// 后 Load，Load 拿到的已是探测结果）。全部未命中时若为内置模板
    /// （pagination 等）返回内置哨兵
    /// </summary>
    public string GetPath(Scriban.TemplateContext context, SourceSpan callerSpan, string templateName)
    {
        // Hugo 的部分模板名解析**先相对于调用者所在目录**，再回落到根——
        // 例如 `_partials/templates/opengraph.html` 内调用
        // `partial "_funcs/get-page-images"` 命中
        // `_partials/templates/_funcs/get-page-images.html`（PaperMod 实测：
        // 只探根级候选会报 "Unexpected exception while creating template from
        // path .../layouts/_funcs/get-page-images"）。
        // Scriban 的 include 会把调用者物理路径放进 callerSpan.FileName（实测），
        // 由此还原调用者的相对目录
        var callerRelativeDir = TryGetRelativeDirectory(callerSpan.FileName);
        if (callerRelativeDir is not null)
        {
            var called = new[] { templateName, templateName + ".html" };
            foreach (var root in Roots)
            {
                foreach (var form in called)
                {
                    var candidate = Path.Combine(root, callerRelativeDir, form);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }

        var relativeForms = new List<string> { templateName, templateName + ".html" };
        // _default/ 形态（对齐 ResolveTemplatePath 的搜索序）
        relativeForms.Add(Path.Combine("_default", templateName));
        relativeForms.Add(Path.Combine("_default", templateName + ".html"));
        var hasPartialsPrefix = templateName.StartsWith("partials/", StringComparison.OrdinalIgnoreCase) ||
                                templateName.StartsWith("_partials/", StringComparison.OrdinalIgnoreCase) ||
                                templatePathStartsWithBackslash(templateName);
        if (!hasPartialsPrefix)
        {
            // partials/ 与 _partials/ 双形态（后者为 Hugo v0.146+ 新目录约定）
            relativeForms.Add(Path.Combine("partials", templateName));
            relativeForms.Add(Path.Combine("partials", templateName + ".html"));
            relativeForms.Add(Path.Combine("_partials", templateName));
            relativeForms.Add(Path.Combine("_partials", templateName + ".html"));
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

        // 内置模板回退（Hugo embedded templates）：include "pagination" 这类
        // 主题内置依赖在文件系统无文件，顶层解析路径有回退但 include 此前没有——
        // 导致 Ananke 的 {{ include "pagination" }} 必然失败
        // 内置模板名归一：主题可能写 "pagination.html"（Hugo 的 partial 调用带扩展名），
        // 也可能写 Hugo 的 `template "_internal/google_analytics.html"` 形态
        //（`_internal/` 前缀是 Hugo embedded template 的命名空间，须剥除——Stack 实测）
        var builtinKey = templateName;
        if (builtinKey.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            builtinKey = builtinKey[..^5];
        }
        if (builtinKey.StartsWith("_internal/", StringComparison.OrdinalIgnoreCase))
        {
            builtinKey = builtinKey["_internal/".Length..];
        }
        if (ScribanTemplateRenderer.BuiltinTemplates.ContainsKey(builtinKey))
        {
            return BuiltinPrefix + builtinKey;
        }

        // 未命中：返回主根形态，让 Load 抛出带上下文的 FileNotFoundException
        return Path.Combine(_basePath, templateName);
    }

    private static bool templatePathStartsWithBackslash(string templateName) =>
        templateName.StartsWith("partials\\", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 调用者物理路径 → 相对某个模板根的目录（如
    /// <c>C:\site\layouts\_partials\templates\opengraph.html</c> →
    /// <c>_partials/templates</c>）。不在任何根下时返回 null（如内置模板哨兵）。
    /// </summary>
    private string? TryGetRelativeDirectory(string? callerFileName)
    {
        if (string.IsNullOrEmpty(callerFileName) ||
            callerFileName.StartsWith(BuiltinPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        foreach (var root in Roots)
        {
            var fullRoot = Path.GetFullPath(root);
            var fullCaller = Path.GetFullPath(callerFileName);
            var prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
                ? fullRoot
                : fullRoot + Path.DirectorySeparatorChar;
            if (!fullCaller.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var rel = fullCaller[prefix.Length..];
            var dir = Path.GetDirectoryName(rel);
            return string.IsNullOrEmpty(dir) ? "" : dir.Replace('\\', '/');
        }
        return null;
    }

    public string Load(Scriban.TemplateContext context, SourceSpan callerSpan, string templatePath)
    {
        // 内置模板哨兵：直接返回内置模板内容（无物理文件，不做路径校验）
        if (templatePath.StartsWith(BuiltinPrefix, StringComparison.Ordinal))
        {
            var name = templatePath[BuiltinPrefix.Length..];
            if (ScribanTemplateRenderer.BuiltinTemplates.TryGetValue(name, out var builtinContent))
            {
                return builtinContent;
            }
            throw new FileNotFoundException($"内置模板未找到: {name}");
        }

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

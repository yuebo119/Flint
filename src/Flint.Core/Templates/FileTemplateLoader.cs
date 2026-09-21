// Flint 静态站点生成器
// 文件系统模板加载器（从 ScribanTemplateRenderer.cs 按 partial 拆出的独立类）

using System.Collections.Concurrent;
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

    /// <summary>根目录缓存（Roots 每次访问都新建 List——partial 高频调用下是纯浪费）</summary>
    private string[]? _rootsCache;

    /// <summary>根校验用的归一化根前缀缓存（Load 每次调用重建是第二项固定开销）</summary>
    private string[]? _allowedRootsCache;

    /// <summary>
    /// 路径解析缓存：GetPath 每次要做十几次 File.Exists 探测（根 × 候选形态），
    /// partial 递归调用（fixit 的 camel-case-keys 每页数百次）下单次 ~10ms、
    /// 700 页站点必然超时（实测 fixit 单页 ~16s、timeout 600s 零页产出）。
    /// 键 = 调用者文件 + 模板名（解析结果对二者确定，含自解析跳过）；
    /// 构建期模板文件不增删，缓存与渲染器的 mtime 失效检查同口径
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _pathCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>模板文本缓存（mtime 不变即命中；保留 serve 模式热更新语义）</summary>
    private readonly ConcurrentDictionary<string, (string Text, DateTime MtimeUtc)> _textCache =
        new(StringComparer.OrdinalIgnoreCase);

    public FileTemplateLoader(string basePath, params string[] themeBasePaths)
    {
        _basePath = basePath;
        _themeBasePaths = themeBasePaths.Where(p => !string.IsNullOrEmpty(p)).ToArray();
    }

    private string[] Roots
    {
        get
        {
            var cached = _rootsCache;
            if (cached is null)
            {
                var roots = new List<string> { _basePath };
                roots.AddRange(_themeBasePaths);
                cached = _rootsCache = [.. roots];
            }
            return cached;
        }
    }

    /// <summary>
    /// 内置模板的 include 哨兵前缀：include "pagination" 等 Hugo 内置模板
    /// 在文件系统中无对应文件，命中时以该前缀回传给 <see cref="Load"/> 取内置内容
    /// </summary>
    internal const string BuiltinPrefix = "\u0001builtin:";

    /// <summary>
    /// Scriban include 的路径解析入口（带缓存的外壳）：解析结果对
    /// "调用者文件 + 模板名"确定，故按键命中；未命中走 <see cref="ResolvePath"/>
    /// </summary>
    public string GetPath(Scriban.TemplateContext context, SourceSpan callerSpan, string templateName)
    {
        var key = (callerSpan.FileName ?? "") + "\u0001" + templateName;
        if (_pathCache.TryGetValue(key, out var cached))
        {
            return cached;
        }
        var resolved = ResolvePath(context, callerSpan, templateName);
        _pathCache[key] = resolved;
        return resolved;
    }

    private string ResolvePath(Scriban.TemplateContext context, SourceSpan callerSpan, string templateName)
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
        var hasExplicitPartialsPrefix =
            templateName.StartsWith("_partials/", StringComparison.OrdinalIgnoreCase) ||
            templateName.StartsWith("partials/", StringComparison.OrdinalIgnoreCase);
        if (callerRelativeDir is not null)
        {
            // 候选：完整名（同名嵌套场景）与"剥掉 partials 前缀后的相对名"
            //（Hugo 允许 partial 名相对调用者目录解析——PaperMod 的
            // `_partials/templates/opengraph.html` 调用 `partial "_funcs/x"` 命中
            // `_partials/templates/_funcs/x.html`。转换器把短名归一化为
            // `_partials/_funcs/x` 后，必须用"前缀剥离版"才能与调用者目录组合）
            var called = new List<string> { templateName, templateName + ".html" };
            if (hasExplicitPartialsPrefix)
            {
                var relRest = StripPartialsPrefix(templateName);
                // **仅当剥离前缀后仍是"带目录的相对路径"**时才与调用者目录组合。
                // 裸文件名绝不能组合——那会命中调用者自身：stack 的
                // `_partials/comments/provider/disqus.html` 内写
                // `{{ partial "disqus.html" . }}`，Hugo 里这个短名解析到**内置模板**
                // `_internal/disqus.html`，而"调用者目录 + disqus.html"恰好是它自己
                // → 200 层自递归（"partial 嵌套深度超过 200：疑似 partial 互相递归"）
                if (relRest.Contains('/', StringComparison.Ordinal) ||
                    relRest.Contains((char)92, StringComparison.Ordinal))
                {
                    called.Add(relRest);
                    called.Add(relRest + ".html");
                }
            }
            foreach (var root in Roots)
            {
                foreach (var form in called)
                {
                    var candidate = Path.Combine(root, callerRelativeDir, form);
                    // 自解析兜底：候选就是调用者文件本身时跳过（与上一层的裸名保护同理）
                    if (callerSpan.FileName is { Length: > 0 } callerFile &&
                        string.Equals(
                            Path.GetFullPath(candidate), Path.GetFullPath(callerFile),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
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
        else
        {
            // 名字已带 partials 前缀：补**另一个目录形态**的候选。
            // `_partials/x`（Hugo v0.146+ 新约定）与 `partials/x`（旧约定）在主题
            // 生态中并存，转换器统一产出 `_partials/` 前缀，故必须能回退到旧目录；
            // 反向同理（主题原生模板可能写 `partials/x`）。
            // **不再尝试原目录裸名**——那会让 `partial "404.html"` 命中
            // layouts/404.html（页面模板自身）而自递归（hugo-coder 实测）
            var rest = StripPartialsPrefix(templateName);
            if (rest.Length > 0 && rest != templateName)
            {
                relativeForms.Add(Path.Combine("partials", rest));
                relativeForms.Add(Path.Combine("partials", rest + ".html"));
                relativeForms.Add(Path.Combine("_partials", rest));
                relativeForms.Add(Path.Combine("_partials", rest + ".html"));
            }
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
        // `_partials/x` / `partials/x`：Hugo embedded template 不在文件系统里，
        // 转换器产出的显式 partials 前缀须剥除后再查内置表（否则 opengraph /
        // pagination / google_analytics 这类内置模板会解析失败——ananke/papermod/
        // loveit 实测 14 处）
        builtinKey = StripPartialsPrefix(builtinKey);
        if (ScribanTemplateRenderer.BuiltinTemplates.ContainsKey(builtinKey))
        {
            return BuiltinPrefix + builtinKey;
        }

        // 未命中：返回主根形态，让 Load 抛出带上下文的 FileNotFoundException
        return Path.Combine(_basePath, templateName);
    }

    /// <summary>剥除 <c>_partials/</c> 或 <c>partials/</c> 前缀（无前缀时原样返回）</summary>
    private static string StripPartialsPrefix(string name)
    {
        if (name.StartsWith("_partials/", StringComparison.OrdinalIgnoreCase))
        {
            return name["_partials/".Length..];
        }
        if (name.StartsWith("partials/", StringComparison.OrdinalIgnoreCase))
        {
            return name["partials/".Length..];
        }
        return name;
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
        var allowedRoots = _allowedRootsCache ??= Roots
            .Select(r => Path.GetFullPath(r)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar)
            .ToArray();
        if (!Array.Exists(allowedRoots, allowed =>
                fullPath.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)))
        {
            throw new FileNotFoundException($"模板 include 路径越出模板目录，已拒绝: {templatePath}");
        }

        // 渲染期依赖收集（T4.1）：include/partial 实际命中的物理路径
        RenderDependencyTracker.Track(context, fullPath);
        // **文本缓存**：File.ReadAllText + allowedRoots 重建是 partial 高频调用下
        // 的固定开销（每次全量读盘）；mtime 不变即命中，保留 serve 模式热更新语义
        var mtime = File.GetLastWriteTimeUtc(fullPath);
        if (_textCache.TryGetValue(fullPath, out var tc) && tc.MtimeUtc == mtime)
        {
            return tc.Text;
        }
        var text = File.ReadAllText(fullPath);
        _textCache[fullPath] = (text, mtime);
        return text;
    }

    // Scriban 7 的 ITemplateLoader.LoadAsync 返回注解为 ValueTask<string?>
    public ValueTask<string?> LoadAsync(Scriban.TemplateContext context, SourceSpan callerSpan, string templatePath)
    {
        return new ValueTask<string?>(Load(context, callerSpan, templatePath));
    }
}

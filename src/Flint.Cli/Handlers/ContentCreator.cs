// Flint 静态站点生成器
// 内容创建器

using Flint.Core.Configuration;

namespace Flint.Cli;

/// <summary>
/// 内容创建器
/// </summary>
internal static class ContentCreator
{
    /// <summary>
    /// 创建新内容文件
    /// </summary>
    /// <returns>0 表示成功，1 表示失败</returns>
    public static int CreateNewContent(string path, string kind)
    {
        // 验证内容路径
        if (string.IsNullOrWhiteSpace(path))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("错误: 内容路径不能为空");
            Console.ResetColor();
            return 1;
        }

        // 绝对路径/UNC 一律拒绝（Path.Combine 对 rooted 路径短路，会写出 cwd 之外）
        if (Path.IsPathRooted(path))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"错误: 内容路径必须是相对路径: {path}");
            Console.ResetColor();
            return 1;
        }

        // path 允许子目录但拒绝父目录引用（.. 段会写出 cwd 之外）
        if (path.Split('/', '\\').Any(seg => seg is "." or ".."))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"错误: 内容路径不能包含父目录引用: {path}");
            Console.ResetColor();
            return 1;
        }

        var currentDir = Directory.GetCurrentDirectory();

        // 检查是否在 Flint 站点目录中
        if (!ConfigLoader.HasConfigFile(currentDir))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("错误: 当前目录不是有效的 Flint 站点");
            Console.WriteLine("请在站点根目录下运行此命令");
            Console.ResetColor();
            return 1;
        }

        // 规范化路径
        var contentPath = path;
        if (!contentPath.StartsWith("content/", StringComparison.OrdinalIgnoreCase) &&
            !contentPath.StartsWith("content\\", StringComparison.OrdinalIgnoreCase))
        {
            contentPath = Path.Combine("content", contentPath);
        }

        // 确保有 .md 扩展名
        if (!contentPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            contentPath += ".md";
        }

        var fullPath = Path.Combine(currentDir, contentPath);

        // 检查文件是否已存在
        if (File.Exists(fullPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"错误: 文件已存在: {contentPath}");
            Console.ResetColor();
            return 1;
        }

        try
        {
            // 查找 archetype 模板
            var archetypeContent = FindArchetype(currentDir, kind, path);

            // 处理模板变量
            var content = ProcessArchetype(archetypeContent, path);

            // 创建目录
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // 写入文件
            File.WriteAllText(fullPath, content);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"创建内容: {contentPath}");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine("编辑文件后，运行 'Flint serve' 预览");
            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"创建内容失败: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
    }

    private static string FindArchetype(string sitePath, string kind, string contentPath)
    {
        // 搜索顺序：
        // 1. archetypes/{kind}.md
        // 2. archetypes/{section}.md (从路径推断)
        // 3. archetypes/default.md
        // 4. 内置默认模板

        var archetypesDir = Path.Combine(sitePath, "archetypes");

        // 1. 指定的 kind
        var kindPath = Path.Combine(archetypesDir, $"{kind}.md");
        if (File.Exists(kindPath))
        {
            return File.ReadAllText(kindPath);
        }

        // 2. 从路径推断 section
        var section = GetSectionFromPath(contentPath);
        if (!string.IsNullOrEmpty(section))
        {
            var sectionPath = Path.Combine(archetypesDir, $"{section}.md");
            if (File.Exists(sectionPath))
            {
                return File.ReadAllText(sectionPath);
            }
        }

        // 3. 默认 archetype
        var defaultPath = Path.Combine(archetypesDir, "default.md");
        if (File.Exists(defaultPath))
        {
            return File.ReadAllText(defaultPath);
        }

        // 4. 内置默认模板
        return GetBuiltinArchetype();
    }

    private static string? GetSectionFromPath(string path)
    {
        // 从路径中提取 section
        // 例如: content/posts/my-post.md -> posts
        var parts = path.Replace('\\', '/').Split('/');

        // 跳过 "content" 前缀
        var startIndex = 0;
        if (parts.Length > 0 && parts[0].Equals("content", StringComparison.OrdinalIgnoreCase))
        {
            startIndex = 1;
        }

        // 如果还有目录部分，返回第一个目录名
        if (parts.Length > startIndex + 1)
        {
            return parts[startIndex];
        }

        return null;
    }

    private static string GetBuiltinArchetype()
    {
        return """
            +++
            title = "{{ .Name }}"
            date = {{ .Date }}
            draft = true
            tags = []
            categories = []
            description = ""
            +++

            在这里写入内容...
            """;
    }

    private static string ProcessArchetype(string template, string path)
    {
        var now = DateTime.Now;
        var fileName = Path.GetFileNameWithoutExtension(path);
        var title = ConvertToTitle(fileName);

        // 替换模板变量
        var content = template
            .Replace("{{ .Name }}", title)
            .Replace("{{.Name}}", title)
            .Replace("{{ .Date }}", now.ToString("yyyy-MM-ddTHH:mm:sszzz"))
            .Replace("{{.Date}}", now.ToString("yyyy-MM-ddTHH:mm:sszzz"))
            .Replace("{{ .File.ContentBaseName }}", fileName)
            .Replace("{{.File.ContentBaseName}}", fileName);

        // 处理 Hugo 风格的管道函数
        // {{ replace .File.ContentBaseName "-" " " | title }}
        content = ProcessPipeFunctions(content, fileName);

        return content;
    }

    private static string ConvertToTitle(string fileName)
    {
        // 将文件名转换为标题
        // my-first-post -> My First Post
        var words = fileName.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries);
        var titleWords = words.Select(w =>
        {
            if (w.Length == 0)
                return w;
            if (w.Length == 1)
                return char.ToUpperInvariant(w[0]).ToString();
            return char.ToUpperInvariant(w[0]) + w[1..];
        });
        return string.Join(" ", titleWords);
    }

    private static string ProcessPipeFunctions(string content, string fileName)
    {
        // 简单处理一些常见的管道函数
        // {{ replace .Name "-" " " | title }}
        // {{ replace .File.ContentBaseName "-" " " | title }}

        // 处理 .Name 版本
        var patternName = @"\{\{\s*replace\s+\.Name\s+""([^""]*)""\s+""([^""]*)""\s*\|\s*title\s*\}\}";
        content = System.Text.RegularExpressions.Regex.Replace(content, patternName, match =>
        {
            var from = match.Groups[1].Value;
            var to = match.Groups[2].Value;
            var result = fileName.Replace(from, to);
            return ConvertToTitle(result);
        });

        // 处理 .File.ContentBaseName 版本
        var patternFile = @"\{\{\s*replace\s+\.File\.ContentBaseName\s+""([^""]*)""\s+""([^""]*)""\s*\|\s*title\s*\}\}";
        content = System.Text.RegularExpressions.Regex.Replace(content, patternFile, match =>
        {
            var from = match.Groups[1].Value;
            var to = match.Groups[2].Value;
            var result = fileName.Replace(from, to);
            return ConvertToTitle(result);
        });

        return content;
    }
}

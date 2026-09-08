// Flint 静态站点生成器
// permalink 纯函数引擎（从 SiteBuilder 解耦，方法体原样迁移）

using Flint.Core.Configuration;
using Flint.Core.Models;

namespace Flint.Core.Site;

/// <summary>
/// permalink 生成纯函数集：模式选择、token 展开、slug 化
/// </summary>
internal static class PermalinkEngine
{
    internal static string GeneratePermalink(ParsedContent content, SiteConfig config)
    {
        // pattern 分派：front matter type 显式指定（post→Posts、其他→Pages）。
        // 对齐 Hugo 现代语义（用户裁决 2026-09-08）：pattern 未配置时 URL =
        // content 相对路径的目录结构（/posts/page-1/）——旧默认 /:year/:month/:title/
        // 与 /:title/ 曾使全部页面挤在根目录（PermalinkConfig.Posts 声明值形同
        // 虚设，万页对比测试实证）
        var pattern = content.Metadata.Type?.ToLowerInvariant() switch
        {
            "post" => config.Permalinks?.Posts,
            _ => config.Permalinks?.Pages
        };

        var date = content.Metadata.Date ?? DateTimeOffset.Now;
        var fileName = Path.GetFileNameWithoutExtension(content.SourcePath);

        // slug 变量：显式 slug 优先，否则文件名（index.md/_index.md 用父目录名）
        string slug;
        if (fileName.Equals("index", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("_index", StringComparison.OrdinalIgnoreCase))
        {
            var parentDir = Path.GetDirectoryName(content.SourcePath);
            slug = !string.IsNullOrEmpty(parentDir)
                ? GenerateSlug(Path.GetFileName(parentDir))
                : GenerateSlug(content.Metadata.Title);
        }
        else
        {
            slug = GenerateSlug(string.IsNullOrEmpty(content.Metadata.Slug) ? fileName : content.Metadata.Slug);
        }

        if (!string.IsNullOrEmpty(pattern))
        {
            return ExpandPermalinkTokens(
                pattern, date, slug, content.Metadata.Slug ?? "",
                content.SourcePath, config.ContentDir);
        }

        // 目录结构（对齐 Hugo 默认）：URL = content 相对路径去扩展名；
        // index.md 归并为目录 URL；显式 slug 替换文件名段但保留 section 前缀
        var rel = "";
        if (!string.IsNullOrEmpty(content.SourcePath))
        {
            var full = Path.GetFullPath(content.SourcePath).Replace('\\', '/');
            var contentDirName = string.IsNullOrEmpty(config.ContentDir) ? "content" : config.ContentDir;
            var marker = "/" + contentDirName.Trim('/') + "/";
            var idx = full.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
            rel = idx >= 0
                ? full[(idx + marker.Length)..]
                : Path.GetFileName(content.SourcePath).Replace('\\', '/');
        }

        var relNoExt = rel[..^Path.GetExtension(rel).Length].TrimEnd('/');
        if (relNoExt.EndsWith("/index", StringComparison.OrdinalIgnoreCase) ||
            relNoExt.Equals("index", StringComparison.OrdinalIgnoreCase))
        {
            relNoExt = relNoExt[..^"index".Length].TrimEnd('/');
        }

        if (!string.IsNullOrEmpty(content.Metadata.Slug))
        {
            var dirPart = relNoExt.Contains('/')
                ? relNoExt[..(relNoExt.LastIndexOf('/') + 1)]
                : "";
            relNoExt = dirPart + GenerateSlug(content.Metadata.Slug);
        }

        return "/" + relNoExt.Trim('/') + "/";
    }

    /// <summary>
    /// 展开 permalink 模式 token（对齐 Hugo 常用 token 集）。
    /// sections 需要源路径与内容目录推算目录链；title/slug 语义保持历史行为
    /// （slug 化文件名；入口已优先显式 slug）
    /// </summary>
    internal static string ExpandPermalinkTokens(
        string pattern,
        DateTimeOffset date,
        string slug,
        string frontMatterSlug,
        string sourcePath,
        string contentDir)
    {
        // :section/:sections 从源路径目录链推算：取路径中最后一个 "content"
        // 目录段之后的部分（不依赖调用方传内容根）
        var sections = Array.Empty<string>();
        if (!string.IsNullOrEmpty(sourcePath))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(sourcePath));
            if (!string.IsNullOrEmpty(dir))
            {
                var parts = dir.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                var contentIndex = Array.FindLastIndex(
                    parts, p => p.Equals("content", StringComparison.OrdinalIgnoreCase));
                if (contentIndex >= 0 && contentIndex + 1 < parts.Length)
                {
                    sections = parts[(contentIndex + 1)..];
                }
            }
        }

        var sb = new System.Text.StringBuilder(pattern.Length + 32);
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] != ':')
            {
                sb.Append(pattern[i]);
                continue;
            }

            // 读完整 token（字母连续段），识别后追加展开值
            var j = i + 1;
            while (j < pattern.Length && (char.IsLetter(pattern[j]) || pattern[j] == '_'))
            {
                j++;
            }
            var token = pattern[(i + 1)..j].ToLowerInvariant();

            switch (token)
            {
                case "year": sb.Append(date.Year.ToString()); break;
                case "month": sb.Append(date.Month.ToString("D2")); break;
                case "day": sb.Append(date.Day.ToString("D2")); break;
                case "monthname": sb.Append(date.ToString("MMMM", System.Globalization.CultureInfo.InvariantCulture)); break;
                case "dayname": sb.Append(date.ToString("dddd", System.Globalization.CultureInfo.InvariantCulture)); break;
                case "yearday": sb.Append(date.DayOfYear.ToString()); break;
                case "weekdayname": sb.Append(date.ToString("dddd", System.Globalization.CultureInfo.InvariantCulture)); break;
                case "title" or "slug": sb.Append(slug); break;
                case "slugorfilename": sb.Append(string.IsNullOrEmpty(frontMatterSlug) ? slug : frontMatterSlug); break;
                case "filename": sb.Append(slug); break;
                case "contentbasename":
                    var baseName = Path.GetFileNameWithoutExtension(sourcePath ?? "");
                    sb.Append(baseName.Equals("_index", StringComparison.OrdinalIgnoreCase)
                        ? Path.GetFileName(Path.GetDirectoryName(sourcePath) ?? "") ?? ""
                        : baseName);
                    break;
                case "section":
                    sb.Append(sections.Length > 0 ? sections[^1] : "");
                    break;
                case "sections":
                    sb.Append(sections.Length > 0 ? "/" + string.Join("/", sections) : "");
                    break;
                default:
                    // 未知 token 原样保留（含 ':yearXX' 这类非 token 前缀场景）
                    sb.Append(pattern[i..j]);
                    break;
            }
            i = j - 1;
        }

        return sb.ToString();
    }

    /// <summary>
    /// 从标题生成 URL slug
    /// 对于非 ASCII 字符（如中文），保持原样
    /// </summary>
    internal static string GenerateSlug(string title)
    {
        // 将空格和下划线替换为连字符
        var slug = title.Replace(" ", "-").Replace("_", "-");

        // 对于纯 ASCII 字符串，转换为小写
        // 对于包含非 ASCII 字符的字符串（如中文），保持原样
        if (slug.All(c => c < 128))
        {
            slug = slug.ToLowerInvariant();
        }

        return slug;
    }
}

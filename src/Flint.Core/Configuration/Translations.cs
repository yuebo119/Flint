// Flint 静态站点生成器
// i18n 翻译表加载（主题系统 P3）：站点 i18n/<lang>.toml 优先、主题回退，
// 文件形态双支持——扁平键值（hello = "你好"）与 Hugo 形态（[hello] other = "你好"）

using Tomlyn;
using Tomlyn.Model;

namespace Flint.Core.Configuration;

public static class Translations
{
    /// <summary>
    /// 加载语言翻译表：主题 i18n 先入、站点 i18n 覆盖（先入者胜的反向——主题先注册、
    /// 站点后注册覆盖）。回退链：精确语言码 → 语言主段（zh-cn → zh）→ 空表
    /// </summary>
    public static IReadOnlyDictionary<string, string> Load(
        string sourcePath, string? themeName, string? language)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(language))
        {
            return merged;
        }

        // 主题在前（低优先），站点在后（覆盖）
        var roots = new List<string>();
        if (!string.IsNullOrWhiteSpace(themeName))
        {
            roots.Add(Path.Combine(sourcePath, "themes", themeName, "i18n"));
        }
        roots.Add(Path.Combine(sourcePath, "i18n"));

        foreach (var candidate in LanguageFileCandidates(language))
        {
            foreach (var root in roots)
            {
                var file = Path.Combine(root, candidate);
                if (File.Exists(file))
                {
                    MergeFile(merged, file);
                }
            }
            // 一旦主段及以上有翻译，不再向更宽的回退展开（精确语言优先于主段）
            if (merged.Count > 0 && candidate.StartsWith(language, StringComparison.Ordinal))
            {
                break;
            }
        }

        return merged;
    }

    /// <summary>语言文件候选：原样码 → 小写码 → 主段（zh-cn → zh）；去重保序</summary>
    internal static IEnumerable<string> LanguageFileCandidates(string language)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in new[] { language, language.ToLowerInvariant(), language.Split('-')[0] })
        {
            if (!string.IsNullOrWhiteSpace(candidate) && seen.Add(candidate))
            {
                yield return candidate + ".toml";
            }
        }
    }

    private static void MergeFile(Dictionary<string, string> target, string file)
    {
        var table = TomlynCompat.TryParseTable(File.ReadAllText(file));
        if (table is null)
        {
            return;
        }

        foreach (var (key, value) in table)
        {
            switch (value)
            {
                case TomlTable nested when nested.TryGetValue("other", out var other):
                    target[key] = other?.ToString() ?? "";
                    break;
                case TomlTable:
                    break; // 复数形态的其他键（one/two/…）暂不支持，跳过
                default:
                    target[key] = value?.ToString() ?? "";
                    break;
            }
        }
    }
}

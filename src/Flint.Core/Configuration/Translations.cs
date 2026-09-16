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
        string sourcePath, IReadOnlyList<string>? themeNames, string? language)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(language))
        {
            return merged;
        }

        // 主题在前（低优先），站点在后（覆盖）
        var roots = new List<string>();
        // 主题列表按序：后面的主题先入（低优先），前面的主题后入（覆盖），站点最后
        if (themeNames is not null)
        {
            foreach (var themeName in themeNames.Reverse())
            {
                roots.Add(Path.Combine(sourcePath, "themes", themeName, "i18n"));
            }
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

    /// <summary>Hugo 的复数形式键（go-i18n 的 CLDR 分类）</summary>
    private static readonly string[] PluralForms =
        ["zero", "one", "two", "few", "many", "other"];

    private static void MergeFile(Dictionary<string, string> target, string file)
    {
        var table = TomlynCompat.TryParseTable(File.ReadAllText(file));
        if (table is null)
        {
            return;
        }

        Flatten(target, "", table);
    }

    /// <summary>
    /// 递归摊平为**点分键**（`[article.readingTime] one/other` → `article.readingTime.one`
    /// 与 `article.readingTime.other`）。此前只处理**一层**且只认 `other`：
    /// 嵌套子表（主题里普遍存在的 `[section.key]` 形态）整块被丢弃 →
    /// `i18n "article.readingTime" N` 输出空（stack 的阅读时长实测）。
    /// 同时把 `other` 形另存为**主键**，使不带复数的调用（`i18n "key"`）仍能取到值
    /// </summary>
    private static void Flatten(Dictionary<string, string> target, string prefix, TomlTable table)
    {
        foreach (var (key, value) in table)
        {
            var path = prefix.Length == 0 ? key : prefix + "." + key;
            switch (value)
            {
                case TomlTable nested:
                    Flatten(target, path, nested);
                    var hasPlural = PluralForms.Any(nested.ContainsKey);
                    if (hasPlural)
                    {
                        // 复数键另存主键：`i18n "key"`（无计数）用 other 形（Hugo 探针：
                        // 无计数时选 other，`.Count` 缺失渲染为 `<no value>`）
                        // 主键取 **other** 形优先（Hugo 探针：无计数调用走 other）
                        var fallback = PluralForms
                            .OrderBy(form => form == "other" ? 0 : 1)
                            .Select(form => nested.ContainsKey(form) ? nested[form]?.ToString() : null)
                            .FirstOrDefault(v => !string.IsNullOrEmpty(v));
                        if (!string.IsNullOrEmpty(fallback))
                        {
                            target[path] = fallback;
                        }
                    }

                    break;
                default:
                    target[path] = value?.ToString() ?? "";
                    break;
            }
        }
    }
}

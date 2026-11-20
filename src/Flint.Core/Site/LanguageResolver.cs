// Flint 静态站点生成器
// 多语言内容解析（0.4.0 最小切片）：文件名语言后缀分派 + 默认语言判定。
//
// Hugo 语义对齐：content/posts/hello.zh-cn.md 是 hello 的 zh-cn 翻译副本，
// 输出 /zh-cn/posts/hello/；无后缀文件归属默认语言（languages 表中 weight
// 最小者）。后缀只有在命中 [languages] 键时才剥离——"vue.config.md" 这类
// 含点文件名不会被误拆（.config 不是语言）。

namespace Flint.Core.Site;

internal static class LanguageResolver
{
    /// <summary>
    /// 从去扩展名的文件名里剥离语言后缀。<c>hello.zh-cn</c>（languages 含 zh-cn）
    /// → (<c>hello</c>, <c>zh-cn</c>)；不命中返回原名与空语言。
    /// 语言键大小写不敏感归一（配置 <c>[languages.zh-CN]</c> 与文件 <c>.zh-cn.md</c>
    /// 等价，输出统一用配置键的原拼写）
    /// </summary>
    internal static bool TrySplitLanguageSuffix(
        string fileNameNoExt,
        IReadOnlyDictionary<string, Configuration.LanguageConfig> languages,
        out string stem,
        out string language)
    {
        stem = fileNameNoExt;
        language = "";
        var dot = fileNameNoExt.LastIndexOf('.');
        if (dot <= 0 || dot == fileNameNoExt.Length - 1)
        {
            return false;
        }
        var suffix = fileNameNoExt[(dot + 1)..];
        foreach (var (code, _) in languages)
        {
            if (string.Equals(code, suffix, StringComparison.OrdinalIgnoreCase))
            {
                stem = fileNameNoExt[..dot];
                language = code;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 默认语言：languages 表按 weight 升序（同 weight 按键序稳定排序）的
    /// 首个键；无配置时回落站点 LanguageCode（单语言站点的既有行为）
    /// </summary>
    internal static string DefaultLanguage(
        IReadOnlyDictionary<string, Configuration.LanguageConfig> languages,
        string siteLanguageCode) =>
        languages.Count == 0
            ? siteLanguageCode
            : languages.OrderBy(kv => kv.Value.Weight).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .First().Key;

    /// <summary>
    /// 语言前缀：非默认语言返回 <c>/zh-cn</c> 形态（无尾斜杠），默认语言返回
    /// 空串——relPermalink 拼接方用它给翻译副本加语言目录
    /// </summary>
    internal static string LanguagePrefix(string language, string defaultLanguage) =>
        string.IsNullOrEmpty(language) || string.Equals(language, defaultLanguage, StringComparison.OrdinalIgnoreCase)
            ? ""
            : "/" + language;
}

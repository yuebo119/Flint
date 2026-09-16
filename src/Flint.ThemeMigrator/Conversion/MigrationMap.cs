// Flint 主题迁移工具
// AST → Scriban 转换器
//
// 设计要点：
// 1. **结构驱动**：按 AST 结构生成目标，不做字符串切分——这是与正则版
//    的根本区别，也是消除静默损坏的关键。
// 2. **函数名映射表**（数据）：Hugo 名 → Flint 名，表由离线期生成并机器校验。
// 3. **静默损坏检测**：每个表达式转换后做"结构守恒 + 危险模式"检查，
//    失败则降级为 TODO 而非产出可疑代码。
// 4. **上下文栈**：range/with 的裸点映射（对齐 Go 的 dot 语义）。

using System.Globalization;
using System.Text;

namespace Flint.ThemeMigrator.Conversion;

/// <summary>转换结果类型</summary>
internal enum ConversionKind
{
    /// <summary>完全等价转换</summary>
    Equivalent,

    /// <summary>降级（行为有差异但产出合法）</summary>
    Downgraded,

    /// <summary>不支持的 TODO（保留标记）</summary>
    Unsupported,
}

/// <summary>单个表达式的转换结果</summary>
/// <param name="Text">产出的 Scriban 文本</param>
/// <param name="Kind">转换类型</param>
/// <param name="Note">降级/不支持的说明</param>
internal readonly record struct ConversionResult(string Text, ConversionKind Kind, string? Note = null);

/// <summary>迁移映射表（Hugo → Flint），离线生成、机器校验</summary>
internal sealed class MigrationMap
{
    private readonly Dictionary<string, string> _functions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _variables = new(StringComparer.Ordinal);

    public static MigrationMap CreateDefault()
    {
        var m = new MigrationMap();

        // ---- 字段路径（.Title → page.title）----
        // 页面字段
        foreach (var f in new[]
        {
            "Title", "Content", "Summary", "Description", "Date", "Lastmod", "PublishDate",
            "ExpiryDate", "Permalink", "RelPermalink", "Type", "Layout", "Draft", "Weight",
            "Section", "Kind", "Params", "Pages", "RegularPages", "Resources", "File",
            "Site", "WordCount", "ReadingTime", "Plain", "RawContent", "Aliases",
            "TableOfContents", "LinkTitle", "Truncated", "Path", "BundleType", "Keywords",
            "FuzzyWordCount", "PlainWords", "Len", "Slug", "Language", "Store", "Scratch"
        })
        {
            m._variables["." + f] = "page." + ToSnake(f);
        }

        // 站点字段
        foreach (var (hugo, flint) in new[]
        {
            ("Site.Title", "site.title"), ("Site.BaseURL", "site.base_url"),
            ("Site.Language", "site.language"), ("Site.LanguageCode", "site.language"),
            ("Site.Language.LanguageCode", "site.language"), ("Site.Language.Lang", "site.language"),
            ("Site.Copyright", "site.copyright"), ("Site.Params", "site.params"),
            ("Site.Data", "site.data"), ("Site.Menus", "site.menus"),
            ("Site.Pages", "site.pages"), ("Site.RegularPages", "site.regular_pages"),
            ("Site.Taxonomies", "site.taxonomies"), ("Site.Home", "site.home"),
            ("Site.Sections", "site.sections"), ("Site.AllPages", "site.all_pages"),
        })
        {
            m._variables["." + hugo] = flint;
        }

        // ---- 函数名（Hugo → Flint/Scriban）----
        foreach (var (hugo, flint) in new[]
        {
            // 字符串
            ("upper", "upper"), ("lower", "lower"), ("title", "title"), ("humanize", "humanize"),
            ("substr", "substr"), ("truncate", "truncate"), ("replace", "replace"),
            ("replaceRE", "replace_re"), ("split", "split"), ("delimit", "delimit"),
            ("hasPrefix", "has_prefix"), ("hasSuffix", "has_suffix"), ("trim", "trim"),
            ("trimPrefix", "trim_prefix"), ("trimSuffix", "trim_suffix"),
            ("repeat", "repeat"), ("countwords", "count_words"), ("countrunes", "count_runes"),
            ("anchorize", "anchorize"), ("urlize", "urlize"), ("plainify", "plainify"),
            ("emojify", "emojify"), ("markdownify", "markdownify"),
            ("findRE", "find_re"), ("findRESubmatch", "find_re_submatch"),
            ("minify", "minify"), ("resources.Minify", "minify"),
            // 集合
            ("where", "where"), ("sort", "sort"), ("first", "first"), ("last", "last"),
            ("after", "after"), ("uniq", "uniq"), ("shuffle", "shuffle"), ("union", "union"),
            ("intersect", "intersect"), ("append", "append"), ("slice", "slice"),
            ("dict", "dict"), ("index", "index"), ("isset", "isset"), ("in", "in"),
            ("seq", "seq"), ("reverse", "reverse"), ("merge", "merge"),
            // 比较/逻辑（前缀 → 中缀，在转换期特判）
            ("eq", "=="), ("ne", "!="), ("gt", ">"), ("ge", ">="), ("lt", "<"), ("le", "<="),
            ("and", "&&"), ("or", "||"), ("not", "!"),
            ("default", "default"), ("cond", "cond"),
            // 数学
            ("add", "add"), ("sub", "sub"), ("mul", "mul"), ("div", "div"), ("mod", "mod"),
            ("max", "max"), ("min", "min"), ("abs", "abs"), ("ceil", "ceil"), ("floor", "floor"),
            ("round", "round"), ("pow", "pow"), ("sqrt", "sqrt"), ("log", "log"),
            // 日期
            ("dateFormat", "date.to_string"), ("now", "now"), ("time", "time"),
            ("duration", "duration"), ("unix", "unix"),
            // URL / 路径
            ("absURL", "abs_url"), ("relURL", "rel_url"), ("absLangURL", "abs_lang_url"),
            // **命名空间调用 → Flint 全局函数**（语义一致的一族）：命名空间成员在
            // **括号内**用空格调用形态会被 Scriban 误解析——`compare.Ge 5 (math.add 3 1)`
            // 报 "Object must be of type Int32"（`compare.Ge 5 (4)` 与顶层
            // `math.add 3 1` 都正常，故是"括号 + 命名空间 + 空格实参"的组合问题，
            // ananke 的 `compare.Ge $section_count (math.add $n_posts 1)` 实测）。
            // 全局函数形态在括号内正常（`(index $pages 0)` 长期可用）
            ("compare.Eq", "eq"), ("compare.Ne", "ne"), ("compare.Ge", "ge"),
            ("compare.Gt", "gt"), ("compare.Le", "le"), ("compare.Lt", "lt"),
            ("compare.Default", "default"), ("compare.Conditional", "cond"),
            ("math.Add", "add"), ("math.Sub", "sub"), ("math.Mul", "mul"),
            ("math.Div", "div"), ("math.Mod", "mod"), ("math.Max", "max"),
            ("math.Min", "min"), ("math.Floor", "floor"), ("math.Ceil", "ceil"),
            ("math.Round", "round"), ("math.Pow", "pow"), ("math.Sqrt", "sqrt"),
            ("math.Abs", "abs"), ("math.Log", "log"), ("math.Sum", "sum"),
            ("math.Product", "product"), ("math.Rand", "rand"),
            ("relLangURL", "rel_lang_url"), ("path.Join", "path_join"),
            ("path.Base", "path_base"), ("path.Dir", "path_dir"), ("path.Ext", "path_ext"),
            // 编码/哈希
            ("md5", "md5"), ("sha1", "sha1"), ("sha256", "sha256"),
            ("base64Encode", "base64_encode"), ("base64Decode", "base64_decode"),
            ("jsonify", "jsonify"), ("htmlEscape", "html_escape"), ("htmlUnescape", "html_unescape"),
            // 安全
            ("safeHTML", "safe_html"), ("safeCSS", "safe_css"), ("safeJS", "safe_js"),
            ("safeURL", "safe_url"), ("safeHTMLAttr", "safe_html_attr"),
            // 翻译
            ("i18n", "i18n"), ("T", "i18n"), ("lang.Translate", "i18n"),
            // 其他
            ("printf", "printf"), ("print", "print"), ("warnf", "warnf"), ("errorf", "errorf"),
            ("len", "len"), ("isset", "isset"), ("string", "cast_to_string"),
            ("int", "int"), ("float", "float"),
        })
        {
            m._functions[hugo] = flint;
        }

        // ---- 命名空间函数（Hugo 0.146+ → Flint 命名空间）----
        foreach (var (hugo, flint) in new[]
        {
            ("strings.ToUpper", "strings.ToUpper"), ("strings.ToLower", "strings.ToLower"),
            ("strings.TrimPrefix", "strings.TrimPrefix"), ("strings.TrimSuffix", "strings.TrimSuffix"),
            ("strings.HasPrefix", "strings.HasPrefix"), ("strings.HasSuffix", "strings.HasSuffix"),
            ("strings.Contains", "strings.Contains"), ("strings.Split", "strings.Split"),
            ("strings.Replace", "strings.Replace"), ("strings.Repeat", "strings.Repeat"),
            ("strings.TrimSpace", "strings.TrimSpace"), ("strings.FirstUpper", "strings.FirstUpper"),
            ("strings.CountWords", "strings.CountWords"), ("strings.CountRunes", "strings.CountRunes"),
            ("strings.Truncate", "strings.Truncate"), ("strings.Trim", "strings.Trim"),
            ("strings.Chomp", "strings.Chomp"), ("strings.Substr", "strings.Substr"),
            ("collections.Where", "collections.Where"), ("collections.Sort", "collections.Sort"),
            ("collections.Delimit", "collections.Delimit"), ("collections.Dictionary", "collections.Dictionary"),
            ("collections.Slice", "collections.Slice"), ("collections.First", "collections.First"),
            ("collections.Last", "collections.Last"), ("collections.In", "collections.In"),
            ("collections.Index", "collections.Index"), ("collections.Merge", "collections.Merge"),
            ("collections.Union", "collections.Union"), ("collections.Uniq", "collections.Uniq"),
            ("collections.Reverse", "collections.Reverse"), ("collections.Shuffle", "collections.Shuffle"),
            ("collections.Seq", "collections.Seq"), ("collections.KeyVals", "collections.KeyVals"),
            ("collections.IsSet", "collections.IsSet"), ("collections.Append", "collections.Append"),
            ("collections.After", "collections.After"), ("collections.Group", "collections.Group"),
            // **有意覆盖上面那批"命名空间 → 全局"的映射**：Flint 引擎里这些命名空间
            // 与主题写法同名可用（`compare.*`/`collections.*`/`transform.*`/`urls.*`），
            // 保留主题原写法更利于人工比对，也不会踩"成员调用 + 空格实参"的歧义。
            // 引擎探针（2026-09-16）逐条确认可用：`compare.Ge 5 (math.add 3 1)` → true、
            // `if (compare.Ge 5 (math.add 3 1))` → 进入、`math.add 3 (math.mul 2 2)` → 7、
            // `collections.Delimit (slice "a" "b") ", "` → "a, b"。
            // （此前把 `compare.Ge 5 (math.add 3 1)` 报 "Object must be of type Int32"
            //  归因于"括号内的命名空间调用被误解析"，实为比较函数用 IComparable.CompareTo
            //  装箱 int 与 double 相比的缺陷——已在引擎侧修掉，与调用形态无关）
            ("compare.Default", "compare.Default"), ("compare.Conditional", "compare.Conditional"),
            ("compare.Eq", "compare.Eq"), ("compare.Ne", "compare.Ne"),
            ("compare.Gt", "compare.Gt"), ("compare.Ge", "compare.Ge"),
            ("compare.Lt", "compare.Lt"), ("compare.Le", "compare.Le"),
            ("transform.Markdownify", "transform.Markdownify"),
            ("transform.Plainify", "transform.Plainify"),
            ("transform.Emojify", "transform.Emojify"),
            ("transform.HTMLEscape", "transform.HTMLEscape"),
            ("transform.HTMLUnescape", "transform.HTMLUnescape"),
            ("transform.Unmarshal", "transform.Unmarshal"),
            ("transform.Remarshal", "transform.Remarshal"),
            ("transform.XMLEscape", "transform.XMLEscape"),
            ("transform.Highlight", "transform.Highlight"),
            ("urls.AbsURL", "urls.AbsURL"), ("urls.RelURL", "urls.RelURL"),
            ("urls.Anchorize", "urls.Anchorize"), ("urls.URLize", "urls.URLize"),
            ("urls.Ref", "urls.Ref"), ("urls.RelRef", "urls.RelRef"),
            ("urls.Parse", "urls.Parse"), ("urls.PathEscape", "urls.PathEscape"),
            ("inflect.Humanize", "inflect.Humanize"), ("inflect.Pluralize", "inflect.Pluralize"),
            ("inflect.Singularize", "inflect.Singularize"),
            ("safe.HTML", "safe.HTML"), ("safe.CSS", "safe.CSS"), ("safe.JS", "safe.JS"),
            ("safe.URL", "safe.URL"), ("safe.HTMLAttr", "safe.HTMLAttr"),
            ("math.Abs", "math.Abs"), ("math.Add", "math.Add"), ("math.Sub", "math.Sub"),
            ("math.Mul", "math.Mul"), ("math.Div", "math.Div"), ("math.Mod", "math.Mod"),
            ("math.Max", "math.Max"), ("math.Min", "math.Min"), ("math.Ceil", "math.Ceil"),
            ("math.Floor", "math.Floor"), ("math.Round", "math.Round"), ("math.Pow", "math.Pow"),
            ("math.Sqrt", "math.Sqrt"), ("math.Log", "math.Log"),
            ("fmt.Errorf", "fmt.Errorf"), ("fmt.Warnf", "fmt.Warnf"),
            ("fmt.Printf", "fmt.Printf"), ("fmt.Print", "fmt.Print"),
            ("reflect.IsMap", "reflect.IsMap"), ("reflect.IsSlice", "reflect.IsSlice"),
            ("reflect.IsPage", "reflect.IsPage"), ("reflect.IsResource", "reflect.IsResource"),
            ("partials.Include", "include"), ("partials.IncludeCached", "partialcached"),
            ("hugo.Version", "hugo.Version"), ("hugo.Environment", "hugo.Environment"),
            ("hugo.IsProduction", "hugo.IsProduction"), ("hugo.IsDevelopment", "hugo.IsDevelopment"),
            ("hugo.IsExtended", "hugo.IsExtended"), ("hugo.IsMultilingual", "hugo.IsMultilingual"),
            ("hugo.Data", "hugo.Data"), ("hugo.Generator", "hugo.Generator"),
            ("hugo.WorkingDir", "hugo.WorkingDir"), ("hugo.Store", "hugo.Store"),
            ("resources.Get", "resources.Get"), ("resources.GetMatch", "resources.GetMatch"),
            ("resources.Match", "resources.Match"), ("resources.ByType", "resources.ByType"),
            ("resources.FromString", "resources.FromString"), ("resources.Concat", "resources.Concat"),
            ("resources.Minify", "resources.Minify"), ("resources.Fingerprint", "resources.Fingerprint"),
            ("resources.Copy", "resources.Copy"), ("resources.Publish", "resources.Publish"),
            ("resources.ExecuteAsTemplate", "resources.ExecuteAsTemplate"),
            ("css.Build", "css.Build"), ("css.Sass", "css.Sass"), ("css.PostCSS", "css.PostCSS"),
            // Hugo 0.128 之前的顶层 SCSS 编译（`$scss | toCSS`），引擎注册为顶层函数
            ("toCSS", "toCSS"), ("to_css", "toCSS"),
            ("js.Build", "js.Build"), ("js.Babel", "js.Babel"),
            ("lang.Translate", "i18n"),
            ("path.Join", "path.Join"), ("path.Base", "path.Base"), ("path.Dir", "path.Dir"),
            ("path.Ext", "path.Ext"), ("path.Clean", "path.Clean"), ("path.Split", "path.Split"),
            ("os.Getenv", "os.Getenv"), ("os.ReadFile", "os.ReadFile"), ("os.FileExists", "os.FileExists"),
            ("os.Stat", "os.Stat"), ("os.ReadDir", "os.ReadDir"),
            ("crypto.MD5", "crypto.MD5"), ("crypto.SHA1", "crypto.SHA1"), ("crypto.SHA256", "crypto.SHA256"),
            ("crypto.HMAC", "crypto.HMAC"),
            ("encoding.Base64Encode", "encoding.Base64Encode"),
            ("encoding.Base64Decode", "encoding.Base64Decode"),
            ("encoding.Jsonify", "encoding.Jsonify"),
            ("hash.FNV32a", "hash.FNV32a"), ("hash.XxHash", "hash.XxHash"),
            ("templates.Exists", "templates.Exists"),
            ("cast.ToInt", "cast.ToInt"), ("cast.ToFloat", "cast.ToFloat"),
            ("cast.ToString", "cast.ToString"),
        })
        {
            m._functions[hugo] = flint;
        }

        // partial（Hugo 的 partial "x" . → Scriban include "x"）
        m._functions["partial"] = "include";
        m._functions["partialCached"] = "partialcached";

        // cast.ToString 的目标名（避免与 Scriban 内置 string 过滤器冲突）
        m._functions["cast.ToString"] = "cast_to_string";

        return m;
    }

    /// <summary>函数名映射（未命中返回原名）</summary>
    public string MapFunction(string hugoName) =>
        _functions.TryGetValue(hugoName, out var flint) ? flint : hugoName;

    /// <summary>函数是否有映射（用于判断"引擎是否支持"）</summary>
    public bool HasFunction(string hugoName) => _functions.ContainsKey(hugoName);

    /// <summary>点路径映射（.Title → page.title）</summary>
    public string? MapPath(string dottedPath) =>
        _variables.TryGetValue(dottedPath, out var flint) ? flint : null;

    /// <summary>已知的全部目标函数名（供引擎校验）</summary>
    public IReadOnlyCollection<string> TargetFunctions => _functions.Values;

    private static string ToSnake(string name)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                {
                    sb.Append('_');
                }
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}

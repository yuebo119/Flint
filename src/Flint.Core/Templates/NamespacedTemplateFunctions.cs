// Flint 静态站点生成器
// 命名空间函数层（Hugo 0.146+ 官方形态）
//
// 背景：Hugo 0.146 起官方文档全部改用命名空间形式（strings.ToUpper /
// collections.Where / hugo.IsProduction），旧全局名保留为别名。Flint 此前
// 只注册全局扁平名，导致照新文档写的主题全部渲染失败。
//
// 设计：命名空间对象是**别名层**——从已注册的全局函数取引用放入子对象，
// 不重复实现。表为数据，可被运行时枚举校验（ApiSurface 一致性测试守护）。

using System.Text;
using Scriban.Runtime;

namespace Flint.Core.Templates;

public sealed partial class BuiltinTemplateFunctions
{
    /// <summary>
    /// 命名空间别名表：命名空间 → [(命名空间内名, 全局函数名)]。
    /// 全局侧不存在时静默跳过（由 ApiSurfaceConsistencyTests 报告缺失），
    /// 保证注册不因单条缺失而中断。
    /// </summary>
    private static readonly (string Namespace, (string Alias, string Global)[] Map)[] NamespaceTable =
    [
        ("strings",
        [
            ("ToUpper", "upper"), ("ToLower", "lower"), ("Title", "title"),
            ("Trim", "trim"), ("TrimLeft", "trim_left"), ("TrimRight", "trim_right"),
            ("TrimPrefix", "trim_prefix"), ("TrimSuffix", "trim_suffix"), ("TrimSpace", "trim_space"),
            ("HasPrefix", "has_prefix"), ("HasSuffix", "has_suffix"),
            ("Contains", "contains"), ("ContainsAny", "contains_any"),
            ("Split", "split"), ("Substr", "substr"), ("Repeat", "repeat"),
            ("Replace", "replace"), ("ReplaceRE", "replace_re"),
            ("Truncate", "truncate"), ("Chomp", "chomp"),
            ("CountWords", "count_words"), ("CountRunes", "count_runes"), ("RuneCount", "count_runes"),
            ("FirstUpper", "first_upper"), ("SliceString", "slicestr"),
            ("FindRE", "find_re"), ("FindRESubmatch", "find_re_submatch"),
            ("Diff", "diff"), ("ReplacePairs", "replace_pairs"),
        ]),
        ("collections",
        [
            ("After", "after"), ("Append", "append"), ("Apply", "apply"),
            ("Complement", "complement"), ("Delimit", "delimit"), ("Dictionary", "dict"),
            ("First", "first"), ("Group", "group"), ("In", "in"), ("Index", "index"),
            ("Intersect", "intersect"),
            // collections.IsSet 是 (MAP, KEY) 形态，与 isset(value) 不同
            ("IsSet", "collections_is_set"), ("KeyVals", "keyvals"),
            ("Last", "last"), ("Merge", "merge"), ("Querify", "querify"),
            ("Reverse", "reverse"), ("Seq", "seq"), ("Shuffle", "shuffle"),
            // collections.Slice 是 Hugo 的可变参数构造器（非切片），独立实现
            ("Slice", "collections_slice"), ("Sort", "sort"), ("SymDiff", "symdiff"),
            ("Union", "union"), ("Uniq", "uniq"), ("Where", "where"), ("D", "d"),
        ]),
        ("compare",
        [
            ("Conditional", "cond"), ("Default", "default"),
            ("Eq", "eq"), ("Ne", "ne"), ("Ge", "ge"), ("Gt", "gt"), ("Le", "le"), ("Lt", "lt"),
        ]),
        ("cast",
        [
            ("ToFloat", "float"), ("ToInt", "int"), ("ToString", "cast_to_string"),
        ]),
        ("crypto",
        [
            ("MD5", "md5"), ("SHA1", "sha1"), ("SHA256", "sha256"), ("HMAC", "hmac"), ("Hash", "hash"),
        ]),
        ("encoding",
        [
            ("Base64Decode", "base64_decode"), ("Base64Encode", "base64_encode"),
            ("HexDecode", "hex_decode"), ("HexEncode", "hex_encode"), ("Jsonify", "jsonify"),
        ]),
        ("hash",
        [
            ("FNV32a", "fnv32a"), ("XxHash", "xxhash"),
        ]),
        ("transform",
        [
            ("Emojify", "emojify"), ("HTMLEscape", "html_escape"), ("HTMLUnescape", "html_unescape"),
            ("Markdownify", "markdownify"), ("Plainify", "plainify"),
            ("Remarshal", "remarshal"), ("Unmarshal", "unmarshal"),
            ("XMLEscape", "xml_escape"), ("Highlight", "highlight"),
            ("CanHighlight", "can_highlight"), ("HTMLToMarkdown", "html_to_markdown"),
            ("ToMath", "to_math"), ("PortableText", "portable_text"),
        ]),
        ("urls",
        [
            ("AbsURL", "abs_url"), ("RelURL", "rel_url"),
            ("Anchorize", "anchorize"), ("URLize", "urlize"),
            ("PathEscape", "path_escape"), ("PathUnescape", "path_unescape"),
            ("Ref", "ref"), ("RelRef", "relref"),
            ("AbsLangURL", "abs_lang_url"), ("RelLangURL", "rel_lang_url"),
            ("JoinPath", "path_join"), ("Parse", "url_parse"),
        ]),
        ("path",
        [
            ("Base", "path_base"), ("Clean", "path_clean"), ("Dir", "path_dir"),
            ("Ext", "path_ext"), ("Join", "path_join"), ("BaseName", "path_base"),
            ("Split", "path_split"),
        ]),
        ("inflect",
        [
            ("Humanize", "humanize"), ("Pluralize", "pluralize"), ("Singularize", "singularize"),
        ]),
        ("safe",
        [
            ("CSS", "safe_css"), ("HTML", "safe_html"), ("HTMLAttr", "safe_html_attr"),
            ("JS", "safe_js"), ("JSStr", "safe_js_str"), ("URL", "safe_url"),
        ]),
        ("math",
        [
            ("Abs", "abs"), ("Add", "add"), ("Ceil", "ceil"), ("Div", "div"),
            ("Floor", "floor"), ("Log", "log"), ("Max", "max"), ("Min", "min"),
            ("Mod", "mod"), ("ModBool", "mod_bool"), ("Mul", "mul"), ("Pow", "pow"),
            ("Round", "round"), ("Sqrt", "sqrt"), ("Sub", "sub"),
            ("Sum", "sum"), ("Product", "product"), ("Rand", "rand"),
            ("ToDegrees", "to_degrees"), ("ToRadians", "to_radians"),
            ("Cos", "cos"), ("Sin", "sin"), ("Tan", "tan"),
            ("Acos", "acos"), ("Asin", "asin"), ("Atan", "atan"), ("Atan2", "atan2"),
            ("Counter", "counter"), ("MaxInt64", "max_int64"), ("Pi", "pi"),
        ]),
        ("fmt",
        [
            ("Errorf", "errorf"), ("Erroridf", "error_idf"), ("Warnf", "warnf"),
            ("Warnidf", "warn_idf"), ("Print", "print"), ("Printf", "printf"), ("Println", "println"),
        ]),
        ("reflect",
        [
            ("IsMap", "reflect_is_map"), ("IsSlice", "reflect_is_slice"),
            ("IsPage", "reflect_is_page"), ("IsResource", "reflect_is_resource"),
            ("IsSite", "reflect_is_site"),
            ("IsImageResource", "reflect_is_image_resource"),
            ("IsImageResourceProcessable", "reflect_is_image_resource_processable"),
            ("IsImageResourceWithMeta", "reflect_is_image_resource_with_meta"),
        ]),
        ("templates",
        [
            ("Exists", "template_exists"), ("Current", "template_current"),
            ("Inner", "template_inner"), ("Defer", "template_defer"),
        ]),
        ("os",
        [
            ("Getenv", "getenv"), ("FileExists", "file_exists"),
            ("ReadFile", "read_file"), ("ReadDir", "read_dir"), ("Stat", "file_stat"),
        ]),
        ("lang",
        [
            ("Translate", "i18n"), ("FormatNumber", "lang_format_number"),
            ("FormatNumberCustom", "lang_format_number_custom"),
            ("FormatCurrency", "lang_format_currency"),
            ("FormatPercent", "lang_format_percent"),
            ("FormatAccounting", "lang_format_accounting"), ("Merge", "lang_merge"),
        ]),
        ("debug",
        [
            ("Dump", "debug_dump"), ("Timer", "debug_timer"),
            ("VisualizeSpaces", "debug_visualize_spaces"),
        ]),
    ];

    /// <summary>CamelCase → snake_case（CompareDefault → compare_default）</summary>
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

    /// <summary>
    /// 注册命名空间对象：为表中每个命名空间建 ScriptObject，
    /// 把已注册的全局函数按别名放入。全局函数不存在时跳过该条
    /// （缺失清单由 ApiSurfaceConsistencyTests 断言，不在注册期抛错）。
    /// </summary>
    /// <returns>注册的命名空间名列表（诊断用）</returns>
    internal static IReadOnlyList<string> RegisterNamespaceAliases(ScriptObject root)
    {
        var registered = new List<string>();
        foreach (var (ns, map) in NamespaceTable)
        {
            var obj = new ScriptObject();
            foreach (var (alias, global) in map)
            {
                if (root.TryGetValue(null, default, global, out var value) && value is not null)
                {
                    // 双形态注册：Hugo 官方写法是 PascalCase（strings.ToUpper），
                    // 但既有模板/旧文档也用小写（math.round）；Scriban 成员查找
                    // 大小写敏感，只注册一种会导致另一种报 "function not found"
                    // （集成测试实测：本机模板用 math.round 而表里只有 Round）
                    obj.TrySetValue(null, default, alias, value, readOnly: true);
                    // 变体键：首字母小写（math.round）与全小写（strings.toupper）
                    var lowerFirst = char.ToLowerInvariant(alias[0]) + alias[1..];
                    foreach (var variant in new[] { lowerFirst, alias.ToLowerInvariant() })
                    {
                        if (variant != alias && !obj.ContainsKey(variant))
                        {
                            obj.TrySetValue(null, default, variant, value, readOnly: true);
                        }
                    }

                    // 全小写 snake 态（collections.where / compare.default 兼容旧写法）
                    var snake = ToSnake(alias);
                    if (snake != alias && !obj.ContainsKey(snake))
                    {
                        obj.TrySetValue(null, default, snake, value, readOnly: true);
                    }
                }
            }

            // 命名空间注册：Scriban 可能内置同名对象（如 hash 是内置），
            // TrySetValue 遇已存在键会静默失败——此时把别名并入既有对象
            if (root.ContainsKey(ns) && root[ns] is ScriptObject existing)
            {
                foreach (var alias in obj.Keys.OfType<string>())
                {
                    if (!existing.ContainsKey(alias))
                    {
                        existing.TrySetValue(null, default, alias, obj[alias], readOnly: true);
                    }
                }

                if (!existing.ContainsKey("__flint_merged__"))
                {
                    existing.TrySetValue(null, default, "__flint_merged__", true, readOnly: true);
                }
            }
            else
            {
                // 空命名空间也注册（模板访问得空对象而非 null，避免 member-of-null 报错）
                root.TrySetValue(null, default, ns, obj, readOnly: true);
            }

            registered.Add(ns);
        }

        return registered;
    }
}

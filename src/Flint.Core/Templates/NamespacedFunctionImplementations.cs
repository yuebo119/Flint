// Flint 静态站点生成器
// 命名空间层所需的补充函数实现（Hugo 0.146+ 语义）
//
// 参数序按 Hugo 官方文档，与 Flint 既有全局函数**可能不同**——
// 例如 Hugo 的 strings.TrimPrefix 是 (prefix, string)，而 Flint 的
// has_prefix 是 (string, prefix)。别名层只做改名，参数序不同的必须
// 在此重新实现（避免"改名后参数错位"这类静默错误）。
//
// IL2026/IL3050：同 BuiltinTemplateFunctions——Import 目标是本类显式 lambda，
// 文件级压制（disable/restore 配对）
#pragma warning disable IL2026, IL3050

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Scriban.Runtime;

namespace Flint.Core.Templates;

public sealed partial class BuiltinTemplateFunctions
{
    /// <summary>
    /// 注册命名空间层引用的补充函数。仅注册尚不存在的名字
    /// （已由其他 Register* 提供的跳过，避免覆盖既有实现）。
    /// </summary>
    // 实例方法（非 static）：template_exists 需读实例上的 TemplateExistsProbe
    internal void RegisterMissingFunctions(ScriptObject obj)
    {
        void Add(string name, Delegate fn)
        {
            if (!obj.ContainsKey(name))
            {
                obj.Import(name, fn);
            }
        }

        // ---- strings 补充（参数序按 Hugo 文档）----
        Add("trim_prefix", (string? prefix, string? s) =>
            s is null ? "" : prefix is null ? s : (s.StartsWith(prefix, StringComparison.Ordinal) ? s[prefix.Length..] : s));
        Add("trim_suffix", (string? suffix, string? s) =>
            s is null ? "" : suffix is null ? s : (s.EndsWith(suffix, StringComparison.Ordinal) ? s[..^suffix.Length] : s));
        Add("trim_space", (string? s) => s?.Trim() ?? "");
        Add("contains_any", (string? s, string? chars) =>
            !string.IsNullOrEmpty(s) && !string.IsNullOrEmpty(chars) && s.Any(chars.Contains));
        Add("chomp", (string? s) => s?.TrimEnd('\r', '\n') ?? "");
        Add("first_upper", (string? s) =>
            string.IsNullOrEmpty(s) ? "" : char.ToUpperInvariant(s[0]) + s[1..]);
        // slicestr STRING START [END]：end 可省（Hugo 两参形态），
        // 严格三参形参会把 `slicestr $name 2` 打成参数数错误（even 主题 37 处实测）
        Add("slicestr", (params object?[] a) =>
        {
            var s = a.Length > 0 ? a[0]?.ToString() : null;
            if (string.IsNullOrEmpty(s) || a.Length < 2)
            {
                return s ?? "";
            }
            var start = (int)ToDouble(a[1]);
            var from = Math.Clamp(start, 0, s.Length);
            var end = a.Length > 2 && a[2] is not null ? (int?)ToDouble(a[2]) : null;
            var to = end is null or < 0 ? s.Length : Math.Clamp(end.Value, from, s.Length);
            return s[from..to];
        });
        // findRE PATTERN INPUT [LIMIT]：limit 为可选尾参（Hugo 文档）。
        // 缺省仍保留既有 100 条上限（防止病态模式产出爆炸；这是 Flint 的防护，
        // 不是 Hugo 语义），显式给出 limit 时以 limit 为准
        // （monochrome 的 states.html 传 `1`，此前报 "Argument index must be < 2" 77 处）
        Add("find_re", (string? pattern, string? input, params object?[] rest) =>
        {
            if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(input))
            {
                return new ScriptArray();
            }
            var limit = rest.Length > 0 ? ToLimitOrZero(rest[0]) : 0;
            try
            {
                var arr = new ScriptArray();
                foreach (var m in Regex.Matches(input, pattern).Take(limit > 0 ? limit : 100))
                {
                    arr.Add(m.ToString());
                }
                return arr;
            }
            catch (ArgumentException)
            {
                return new ScriptArray();
            }
        });
        Add("find_re_submatch", (string? pattern, string? input, params object?[] rest) =>
        {
            if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(input))
            {
                return new ScriptArray();
            }
            var limit = rest.Length > 0 ? ToLimitOrZero(rest[0]) : 0;
            try
            {
                var arr = new ScriptArray();
                foreach (Match m in Regex.Matches(input, pattern).Take(limit > 0 ? limit : 100))
                {
                    var groups = new ScriptArray();
                    foreach (var g in m.Groups.Cast<Group>())
                    {
                        groups.Add(g.Value);
                    }
                    arr.Add(groups);
                }
                return arr;
            }
            catch (ArgumentException)
            {
                return new ScriptArray();
            }
        });
        Add("diff", (string? a, string? b) =>
        {
            // Hugo strings.Diff：返回 a 中不在 b 的字符（按 a 的顺序、去重）
            if (string.IsNullOrEmpty(a))
            {
                return "";
            }
            var bs = (b ?? "").ToHashSet();
            var seen = new HashSet<char>();
            var sb = new StringBuilder();
            foreach (var c in a.Where(c => !bs.Contains(c) && seen.Add(c)))
            {
                sb.Append(c);
            }
            return sb.ToString();
        });
        Add("replace_pairs", (string? s, params object[] pairs) =>
        {
            if (string.IsNullOrEmpty(s) || pairs.Length < 2)
            {
                return s ?? "";
            }
            var result = s;
            for (var i = 0; i + 1 < pairs.Length; i += 2)
            {
                result = result.Replace(pairs[i]?.ToString() ?? "", pairs[i + 1]?.ToString() ?? "", StringComparison.Ordinal);
            }
            return result;
        });

        // strings.Trim：Hugo 签名是 (CUTSET, STRING) —— 与 Flint 的 trim(s) 一元
        // 语义不同（Ananke 的 `strings.Trim $x "/"` 实测报 "Argument index must be < 1"）
        Add("strings_trim", (object? cutset, object? s) =>
        {
            var text = s?.ToString() ?? "";
            var chars = cutset?.ToString();
            return string.IsNullOrEmpty(chars) ? text : text.Trim(chars.ToCharArray());
        });

        // urls.RelLangURL / AbsLangURL：带语言前缀的 URL（单语言站点等价 relURL/absURL，
        // Hugo 的语言前缀在 hreflang 阶段追加）。旧实现是**恒等桩** →
        // `relLangURL "x1"` 返回 "x1" 而 Hugo 返回 "/x1"，与走真实现的 `relURL`
        // 不一致（apply 探针实测：`apply (slice "x1") "relLangURL" "."` 应为 /x1）
        Add("rel_lang_url", (string? s2) => RelUrl(s2));
        Add("abs_lang_url", (string? s2) => AbsUrl(s2));

        // transform.Unmarshal：把 YAML/JSON/TOML 字符串解析为对象/数组
        // （Hugo 主题用它读内联配置；LoveIt 实测）
        // 入参既可能是**字符串**也可能是**资源对象**（Hugo：
        // `resources.Get "styles/index.yaml" | transform.Unmarshal`，
        // hugo-book 实测）；选项字典在 Go 里前置，故取最后一个「文本位」参数
        Add("transform_unmarshal", (params object?[] args) =>
        {
            var text = LastTextArg(args);
            if (string.IsNullOrWhiteSpace(text))
            {
                return new ScriptObject();
            }
            var t = text.Trim();
            try
            {
                if (t.StartsWith('{') || t.StartsWith('['))
                {
                    var doc = System.Text.Json.JsonDocument.Parse(t);
                    return JsonToScript(doc.RootElement);
                }
                return ParseSimpleYaml(t);
            }
            catch (System.Text.Json.JsonException)
            {
                return new ScriptObject();
            }
        });

        // slice（Hugo 可变参数构造器）：与 Flint 的三参 slice（序列切片）语义冲突。
        // 主题的 `{{ $x := slice }}`（空数组）/ `slice a b c`（构造）经此实现
        Add("hugo_slice", (params object?[] items) =>
        {
            var arr = new ScriptArray();
            foreach (var it in items)
            {
                arr.Add(it);
            }
            return arr;
        });

        // collections.IsSet：Hugo 语义是 (MAP, KEY) 判断键存在，
        // 与 Flint 的 isset(value)（判非空）不同 —— 独立实现
        Add("collections_is_set", (object? map, object? key) =>
        {
            var k = key?.ToString() ?? "";
            return map switch
            {
                ScriptObject so => so.ContainsKey(k),
                IDictionary<string, object> d => d.ContainsKey(k),
                _ => false
            };
        });

        // collections.Slice：Hugo 的可变参数构造器（与 Flint 的 slice 切片语义不同）
        Add("collections_slice", (params object?[] items) =>
        {
            var arr = new ScriptArray();
            foreach (var it in items)
            {
                arr.Add(it);
            }
            return arr;
        });

        // ---- collections 补充 ----
        Add("keyvals", (params object[] args) =>
        {
            // Hugo collections.KeyVals：交替 key/value → 键值序列
            var arr = new ScriptArray();
            for (var i = 0; i + 1 < args.Length; i += 2)
            {
                var kv = new ScriptObject
                {
                    ["Key"] = args[i]?.ToString() ?? "",
                    ["Value"] = args[i + 1]
                };
                arr.Add(kv);
            }
            return arr;
        });
        Add("symdiff", (object? a, object? b) =>
        {
            var sa = ToObjectList(a);
            var sb = ToObjectList(b);
            var setB = sb.ToHashSet();
            var setA = sa.ToHashSet();
            var arr = new ScriptArray();
            foreach (var x in sa.Where(x => !setB.Contains(x)))
            {
                arr.Add(x);
            }
            foreach (var x in sb.Where(x => !setA.Contains(x)))
            {
                arr.Add(x);
            }
            return arr;
        });
        Add("d", (object? fallback, params object[] values) =>
        {
            // Hugo collections.D：返回第一个非空值，全空用 fallback
            foreach (var v in values)
            {
                if (v is not null && !(v is string s && s.Length == 0))
                {
                    return v;
                }
            }
            return fallback;
        });

        // ---- cast ----
        Add("float", (object? v) =>
            v is null ? 0d : double.TryParse(v.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0d);
        Add("int", (object? v) =>
            v is null ? 0L : long.TryParse(v.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : 0L);
        Add("cast_to_string", (object? v) => v?.ToString() ?? "");

        // 不注册全局 "string"——Scriban 内置同名过滤器（{{ x | string }}），
        // 覆盖会使 0 参调用报 "Invalid number of arguments 0 passed to string
        // while expecting 1"（集成测试实测，4 个 fixture 全失败）。
        // cast.ToString 的别名映射在 NamespaceTable 中指向 "cast_to_string"

        // ---- crypto ----
        Add("hmac", (string? hashType, string? key, string? message) =>
            HmacHex(hashType, key ?? "", message ?? ""));
        Add("hash", (string? hashType, string? input) => HashHex(hashType, input ?? ""));

        // ---- encoding ----
        Add("hex_encode", (string? s) => s is null ? "" : Convert.ToHexStringLower(Encoding.UTF8.GetBytes(s)));
        Add("hex_decode", (string? s) =>
        {
            if (string.IsNullOrEmpty(s))
            {
                return "";
            }
            try
            {
                return Encoding.UTF8.GetString(Convert.FromHexString(s));
            }
            catch (FormatException)
            {
                return "";
            }
        });

        // ---- hash ----
        Add("fnv32a", (string? s) => Fnv32a(s ?? "").ToString(CultureInfo.InvariantCulture));
        Add("xxhash", (string? s) => XxHashLike(s ?? ""));

        // ---- math 补充 ----
        Add("mod_bool", (object? a, object? b) => { var bb = (long)ToDouble(b); return bb != 0 && (long)ToDouble(a) % bb == 0; });
        Add("sum", (params object[] args) => args.Sum(ToDouble));
        Add("product", (params object[] args) => args.Aggregate(1d, (acc, v) => acc * ToDouble(v)));
        Add("rand", (params object[] args) =>
            args.Length == 0 ? Random.Shared.NextDouble()
            : args.Length == 1 ? Random.Shared.Next(0, Math.Max(1, (int)ToDouble(args[0])))
            : Random.Shared.Next((int)ToDouble(args[0]), Math.Max((int)ToDouble(args[0]) + 1, (int)ToDouble(args[1]))));
        Add("to_degrees", (object? r) => ToDouble(r) * 180d / Math.PI);
        Add("to_radians", (object? d) => ToDouble(d) * Math.PI / 180d);
        Add("cos", (object? v) => Math.Cos(ToDouble(v)));
        Add("sin", (object? v) => Math.Sin(ToDouble(v)));
        Add("tan", (object? v) => Math.Tan(ToDouble(v)));
        Add("acos", (object? v) => Math.Acos(ToDouble(v)));
        Add("asin", (object? v) => Math.Asin(ToDouble(v)));
        Add("atan", (object? v) => Math.Atan(ToDouble(v)));
        Add("atan2", (object? y, object? x) => Math.Atan2(ToDouble(y), ToDouble(x)));
        Add("max_int64", () => long.MaxValue);
        Add("pi", () => Math.PI);
        var counterObj = new ScriptObject();
        var counterLock = new object();
        var counters = new Dictionary<string, long>(StringComparer.Ordinal);
        obj.TrySetValue(null, default, "counter", new CounterFunction(counters, counterLock), readOnly: true);
        _ = counterObj;

        // ---- fmt 补充 ----
        Add("println", (params object[] args) => string.Join(" ", args.Select(a => a?.ToString() ?? "")) + "\n");
        // erroridf / warnidf：Hugo v0.146+ 的**简洁顶层别名**（主题直接写
        // `warnidf "id" "msg"`，FixIt 的 _partials/init/detection-encryption.html
        // 实测 7 处报 "The function `warnidf` was not found"）。
        // erroridf 与 errorf 同语义：记录后继续渲染（不中止整页）
        // 与 warnf/errorf 同形参：Hugo 的 warnidf/erroridf 是
        // `warnidf ID FORMAT ARGS...`（格式串 + 可变参数），
        // 两参严格形参在 3+ 实参时抛 "Argument index must be < 2"
        //（FixIt 的 _partials/init/detection-encryption.html 实测 7 处）
        Add("error_idf", (string? id, string? format, params object?[] args) => ErrorIdf(id, format, args));
        Add("erroridf", (string? id, string? format, params object?[] args) => ErrorIdf(id, format, args));
        Add("warn_idf", (string? id, string? format, params object?[] args) => WarnIdf(id, format, args));
        Add("warnidf", (string? id, string? format, params object?[] args) => WarnIdf(id, format, args));

        // ---- path 补充 ----
        Add("path_split", (string? p) =>
        {
            if (string.IsNullOrEmpty(p))
            {
                var empty = new ScriptArray();
                empty.Add("");
                empty.Add("");
                return empty;
            }
            var dir = Path.GetDirectoryName(p)?.Replace('\\', '/') ?? "";
            if (dir.Length > 0)
            {
                dir += "/";
            }
            var arr = new ScriptArray();
            arr.Add(dir);
            arr.Add(Path.GetFileName(p));
            return arr;
        });

        // ---- urls 补充 ----
        Add("path_escape", (string? s) => Uri.EscapeDataString(s ?? ""));
        Add("path_unescape", (string? s) => Uri.UnescapeDataString(s ?? ""));
        // 零参宽容：`urls.Parse` 无参时返回空对象（严格单参形参报
        // "Invalid number of arguments 0 ... expecting 1"，doit 实测）
        Add("diagrams_goat", (string? text) => BuildGoatDiagram(text));
        Add("diagrams_ascii_art", (string? text) => BuildGoatDiagram(text));

        Add("url_parse", (params object?[] args) =>
        {
            var s = args.Length > 0 ? args[0]?.ToString() : null;
            var o = new ScriptObject();
            if (Uri.TryCreate(s, UriKind.Absolute, out var u))
            {
                o["scheme"] = u.Scheme;
                o["host"] = u.Host;
                o["path"] = u.AbsolutePath;
                o["query"] = BuildQueryObject(u.Query);
                o["raw_query"] = u.Query.TrimStart('?');
                o["RawQuery"] = u.Query.TrimStart('?');
                o["fragment"] = u.Fragment.TrimStart('#');
                o["opaque"] = "";
                o["user"] = u.UserInfo;
            }
            else if (Uri.TryCreate(s, UriKind.Relative, out var r))
            {
                var raw = s ?? "";
                var q = raw.IndexOf('?', StringComparison.Ordinal);
                o["scheme"] = "";
                o["host"] = "";
                o["path"] = q >= 0 ? raw[..q] : raw;
                o["query"] = BuildQueryObject(q >= 0 ? raw[(q + 1)..] : "");
                o["raw_query"] = q >= 0 ? raw[(q + 1)..] : "";
                o["RawQuery"] = q >= 0 ? raw[(q + 1)..] : "";
                o["fragment"] = "";
                o["opaque"] = "";
                o["user"] = "";
            }
            else
            {
                o["scheme"] = "";
                o["host"] = "";
                o["path"] = "";
                o["query"] = BuildQueryObject("");
                o["raw_query"] = "";
                o["RawQuery"] = "";
                o["fragment"] = "";
                o["opaque"] = s ?? "";
                o["user"] = "";
            }
            return o;
        });

        // ---- safe 补充 ----
        Add("safe_js_str", (string? s) => s ?? "");

        // ---- reflect ----
        // 页面/站点判定用键特征（渲染器里页面对象是 ScriptObject 派生，
        // 无公开标记接口；键组合是稳定特征：页面有 rel_permalink，站点有 base_url）
        // Hugo 的 reflect.IsMap 对 Page/Site 返回 **false**（它们不是映射），
        // 只有 dict / Params / maps.Params 之类的映射才为 true。Flint 的页面
        // 对象同样是 ScriptObject，按类型判定会把页面认成映射——主题里
        // "递归转换 map 键"的辅助函数（FixIt 的 camel-case-keys.html）遍历
        // 页面成员时经 site→pages→page 的对象环无限递归，最终打到 Scriban 的
        // 函数递归上限："Exceeding number of recursive depth limit `100`"
        Add("reflect_is_map", (object? v) => v switch
        {
            null => false,
            // 引擎内部投影（页面片段/store/词条映射/分页器等）对模板是不透明标量：
            // 判成 map 会让"递归转换键名"的辅助函数钻进内部成员（引用图有环）→ 不收敛
            IFlintNonDataObject => false,
            // 页面 / 站点 / 页面集合都不是 map（Hugo：.Scratch 是 struct、.Pages 是
            // slice）；必须逐一排除，否则主题里"递归转换 map 键"的辅助函数会钻进
            // 它们的成员（partial 上下文注入的 store 内部还引用了这些对象，形成环
            // → 无限递归）。FixIt 的 camel-case-keys.html 实测
            ScriptObject page when page.ContainsKey("rel_permalink") && page.ContainsKey("title") => false,
            ScriptObject site when site.ContainsKey("base_url") && !site.ContainsKey("rel_permalink") => false,
            IList<ScriptObject> => false,
            ScriptObject => true,
            IDictionary<string, object> => true,
            _ => false
        });
        // reflect.IsSlice：Hugo 里**只有真集合**为真（map/页面/store 都不是 slice）。
        // 关键：Scriban 的 ScriptObject 全部实现 IEnumerable，用
        // `v is IEnumerable and not string` 会把**页面对象、store、内部投影**也判成切片
        // ——主题据此走"逐元素处理"分支，于是把对象成员当数据一层层下钻，递归不收敛
        //（FixIt 的 camel-case-keys.html 实测：`reflect.IsSlice page` 为真 → 下钻页面成员
        //  到内部投影对象 → 200 层后触发 partial 深度守卫）。
        // 判定顺序：**列表优先于映射**（页面集合是 ScriptObject + IList&lt;ScriptObject&gt;，
        // 按 ScriptObject 判会误判成非切片 → 主题不再遍历 .Pages）
        Add("reflect_is_slice", (object? v) => v switch
        {
            null => false,
            string => false,
            // 真集合优先判定；内部投影都是 ScriptObject，由下面的 ScriptObject 分支回 false
            IList<ScriptObject> => true,
            System.Collections.IList => true,
            ScriptObject => false,
            System.Collections.IDictionary => false,
            System.Collections.IEnumerable => true,
            _ => false
        });
        Add("reflect_is_page", (object? v) =>
            v is ScriptObject o && o.ContainsKey("rel_permalink") && o.ContainsKey("title"));
        Add("reflect_is_resource", (object? v) => v is TemplateResource);
        Add("reflect_is_site", (object? v) =>
            v is ScriptObject s && s.ContainsKey("base_url") && !s.ContainsKey("rel_permalink"));
        Add("reflect_is_image_resource", (object? v) => v is TemplateResource { IsImage: true });
        Add("reflect_is_image_resource_processable", (object? v) => v is TemplateResource { IsImage: true });
        Add("reflect_is_image_resource_with_meta", (object? v) => v is TemplateResource { IsImage: true });

        // ---- debug ----
        Add("debug_dump", (object? v) => DumpValue(v));
        Add("debug_timer", (string? label) => $"[timer] {label}");
        Add("debug_visualize_spaces", (string? s) => s?.Replace(" ", "·") ?? "");

        // ---- os ----
        Add("getenv", (string? name) => Environment.GetEnvironmentVariable(name ?? "") ?? "");
        Add("file_exists", (string? p) =>
            !string.IsNullOrEmpty(p) && (File.Exists(p) || Directory.Exists(p)));
        // Hugo 的全局 camelCase 形态（主题直接写 fileExists，不经 os. 命名空间）
        Add("fileExists", (string? p) =>
            !string.IsNullOrEmpty(p) && (File.Exists(p) || Directory.Exists(p)));
        Add("read_file", (string? p) =>
            !string.IsNullOrEmpty(p) && File.Exists(p) ? File.ReadAllText(p) : "");
        Add("read_dir", (string? p) =>
        {
            var arr = new ScriptArray();
            if (string.IsNullOrEmpty(p) || !Directory.Exists(p))
            {
                return arr;
            }
            foreach (var f in Directory.EnumerateFileSystemEntries(p).OrderBy(x => x, StringComparer.Ordinal))
            {
                arr.Add(Path.GetFileName(f));
            }
            return arr;
        });
        Add("file_stat", (string? p) =>
        {
            var o = new ScriptObject();
            if (string.IsNullOrEmpty(p) || !File.Exists(p))
            {
                o["exists"] = false;
                return o;
            }
            var fi = new FileInfo(p);
            o["exists"] = true;
            o["name"] = fi.Name;
            o["size"] = fi.Length;
            o["is_dir"] = false;
            o["mod_time"] = fi.LastWriteTimeUtc;
            return o;
        });

        // ---- lang 补充 ----
        Add("lang_format_number", (object? v, params object[] opts) => ToDouble(v).ToString("N0", CultureInfo.InvariantCulture));
        Add("lang_format_number_custom", (string? format, object? v) =>
            ToDouble(v).ToString(format ?? "0.##", CultureInfo.InvariantCulture));
        Add("lang_format_currency", (string? currency, object? v) =>
            ToDouble(v).ToString("C", CultureInfo.InvariantCulture) + " " + (currency ?? ""));
        Add("lang_format_percent", (object? v) => ToDouble(v).ToString("P0", CultureInfo.InvariantCulture));
        Add("lang_format_accounting", (object? v) => ToDouble(v).ToString("N2", CultureInfo.InvariantCulture));
        Add("lang_merge", (object? a, object? b) => MergeScriptObjects(a, b));

        // ---- templates ----
        // templates.Exists：真实模板存在性。命名空间对象在注册时**固定引用**本委托，
        // 故用可注入 probe 而非每次重注册——渲染器构造后注入 loader 探测
        // （之前恒真实现使 `{{ if templates.Exists "x" }}` 守卫失效，
        //  Blowfish 的 favicons 守卫径直调用不存在的 partial，1574 处 FileNotFound）
        Add("template_exists", (string? name) =>
            !string.IsNullOrEmpty(name) && (TemplateExistsProbe?.Invoke(name) ?? true));
        Add("template_current", () => new ScriptObject());
        Add("template_inner", (object? v) => v);
        Add("template_defer", (object? v) => v);

        // ---- 代码高亮（Hugo 的 highlight / transform.Highlight / HighlightCodeBlock）----
        // 说明：不实现 chroma 的词法着色，只产出与 Hugo 默认输出**同构**的骨架
        //（highlight div + pre.chroma + code.language-X 与转义后的代码文本），
        // 使主题的 CSS 能挂上、构建不因缺函数而失败。着色差异是已知限制
        //（Hugo 用 chroma 主题生成 token span，Flint 侧留待后续实现）
        Add("highlight", (string? code, string? lang, params object[] opts) =>
            BuildHighlight(code ?? "", lang ?? "", OptsFlag(opts, "noClasses", false)));
        Add("can_highlight", (string? lang) => !string.IsNullOrWhiteSpace(lang));
        Add("highlight_code_block", (object? codeBlock, params object[] opts) =>
            BuildHighlightCodeBlock(codeBlock, OptsFlag(opts, "noClasses", false)));
    }

    /// <summary>
    /// <summary>
    /// erroridf / error_idf（Hugo v0.146+ 带 ID 的错误日志）：与 errorf 同语义——
    /// 记录错误后**继续渲染**，构建收尾按错误计数判失败。
    /// FixIt 的 _partials/init/detection-encryption.html 用 warnidf 报缺少
    /// 加密参数（7 处），早期注册表只有 warn_idf 而无简洁别名 → function not found
    /// </summary>
    /// <summary>warnidf/erroridf 的 ID 前缀包装（Hugo 日志形态：`[ID] 消息`）</summary>
    private static string Prefixed(string? id, string? format, object?[] args) =>
        args.Length > 0
            ? "[" + id + "] " + FormatMessage(format, args)
            : "[" + id + "] " + (format ?? "");

    private static string WarnIdf(string? id, string? format, object?[] args)
    {
        Console.Error.WriteLine(Prefixed(id, format, args));
        return "";
    }

    private string ErrorIdf(string? id, string? format, object?[] args)
    {
        ReportTemplateError(Prefixed(id, format, args));
        return "";
    }

    /// 高亮输出骨架（Hugo chroma 默认 class 形态的近似）：
    /// <c>&lt;div class="highlight"&gt;&lt;pre tabindex="0" class="chroma"&gt;&lt;code …&gt;</c>。
    /// code 内容按 HTML 转义（Hugo 语义：代码文本不当作 HTML）
    /// </summary>
    private static string BuildHighlight(string code, string lang, bool noClasses)
    {
        var escaped = System.Net.WebUtility.HtmlEncode(code);
        var langAttr = string.IsNullOrEmpty(lang)
            ? ""
            : $" class=\"language-{lang}\" data-lang=\"{lang}\"";
        var preClass = noClasses ? "" : " class=\"chroma\"";
        return $"<div class=\"highlight\"><pre tabindex=\"0\"{preClass}><code{langAttr}>{escaped}</code></pre></div>";
    }

    /// <summary>
    /// transform.HighlightCodeBlock：Hugo 0.140+ 的 codeblock hook 用函数，
    /// 返回 <c>{ Inner, Wrapped }</c>（Inner 为 &lt;pre&gt; 内容，Wrapped 含外层 div）
    /// </summary>
    private static ScriptObject BuildHighlightCodeBlock(object? codeBlock, bool noClasses)
    {
        var code = codeBlock switch
        {
            ScriptObject so => so["inner"]?.ToString() ?? so["code"]?.ToString() ?? "",
            null => "",
            _ => codeBlock.ToString() ?? ""
        };
        var lang = codeBlock is ScriptObject s2 ? s2["type"]?.ToString() ?? "" : "";
        var escaped = System.Net.WebUtility.HtmlEncode(code);
        var langAttr = string.IsNullOrEmpty(lang)
            ? ""
            : $" class=\"language-{lang}\" data-lang=\"{lang}\"";
        var preClass = noClasses ? "" : " class=\"chroma\"";
        var inner = $"<pre tabindex=\"0\"{preClass}><code{langAttr}>{escaped}</code></pre>";
        var wrapped = $"<div class=\"highlight\">{inner}</div>";
        var result = new ScriptObject
        {
            ["Inner"] = inner,
            ["Wrapped"] = wrapped
        };
        // snake_case 双形态（迁移产物用小写）
        result["inner"] = inner;
        result["wrapped"] = wrapped;
        return result;
    }

    private static IEnumerable<object?> ToObjectList(object? v) =>
        v is System.Collections.IEnumerable e and not string
            ? e.Cast<object?>()
            : [v];

    private static double ToDouble(object? v) =>
        v switch
        {
            null => 0d,
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            _ => double.TryParse(v.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : 0d
        };

    private static string HmacHex(string? hashType, string key, string message)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var msgBytes = Encoding.UTF8.GetBytes(message);
        byte[] mac = (hashType ?? "sha256").ToLowerInvariant() switch
        {
            "md5" => System.Security.Cryptography.HMACMD5.HashData(keyBytes, msgBytes),
            "sha1" => System.Security.Cryptography.HMACSHA1.HashData(keyBytes, msgBytes),
            _ => System.Security.Cryptography.HMACSHA256.HashData(keyBytes, msgBytes)
        };
        return Convert.ToHexStringLower(mac);
    }

    private static string HashHex(string? hashType, string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        byte[] hash = (hashType ?? "sha256").ToLowerInvariant() switch
        {
            "md5" => System.Security.Cryptography.MD5.HashData(bytes),
            "sha1" => System.Security.Cryptography.SHA1.HashData(bytes),
            "fnv32a" or "fnv32" => System.Security.Cryptography.SHA256.HashData(bytes),
            _ => System.Security.Cryptography.SHA256.HashData(bytes)
        };
        return Convert.ToHexStringLower(hash);
    }

    private static uint Fnv32a(string s)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        var h = offset;
        foreach (var b in Encoding.UTF8.GetBytes(s))
        {
            h ^= b;
            h *= prime;
        }
        return h;
    }

    /// <summary>xxhash 的确定性替身（无外部依赖）：FNV-1a 变体，稳定且分布均匀</summary>
    private static string XxHashLike(string s)
    {
        unchecked
        {
            const ulong prime1 = 11400714785074694791UL;
            const ulong offset = 14695981039346656037UL;
            var h = offset;
            foreach (var b in Encoding.UTF8.GetBytes(s))
            {
                h ^= b;
                h *= prime1;
            }
            return h.ToString("x16", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// debug.Dump 的等价物：结构化文本化（Scriban 7 的 ScriptObject 无 Dump 扩展，
    /// 此处手写递归，避免引入被裁剪的反射路径）
    /// </summary>
    /// <summary>JSON 元素 → Scriban 值（transform.Unmarshal 用）</summary>
    private static object JsonToScript(System.Text.Json.JsonElement el) => el.ValueKind switch
    {
        System.Text.Json.JsonValueKind.Object => BuildJsonObj(el),
        System.Text.Json.JsonValueKind.Array => BuildJsonArr(el),
        System.Text.Json.JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
        System.Text.Json.JsonValueKind.True => true,
        System.Text.Json.JsonValueKind.False => false,
        System.Text.Json.JsonValueKind.Null => null!,
        _ => el.GetString() ?? ""
    };

    private static ScriptObject BuildJsonObj(System.Text.Json.JsonElement el)
    {
        var o = new ScriptObject();
        foreach (var prop in el.EnumerateObject())
        {
            o[prop.Name] = JsonToScript(prop.Value);
        }
        return o;
    }

    private static ScriptArray BuildJsonArr(System.Text.Json.JsonElement el)
    {
        var a = new ScriptArray();
        foreach (var item in el.EnumerateArray())
        {
            a.Add(JsonToScript(item));
        }
        return a;
    }

    private static string DumpValue(object? v)
    {
        var sb = new StringBuilder();
        DumpInto(sb, v, 0);
        return sb.ToString();
    }

    private static void DumpInto(StringBuilder sb, object? v, int depth)
    {
        if (depth > 8)
        {
            sb.Append("...");
            return;
        }

        switch (v)
        {
            case null:
                sb.Append("null");
                break;
            case string s:
                sb.Append('"').Append(s).Append('"');
                break;
            case ScriptObject o:
                sb.Append('{');
                var first = true;
                foreach (var k in o.Keys.OfType<string>())
                {
                    if (!first)
                    {
                        sb.Append(", ");
                    }
                    first = false;
                    sb.Append(k).Append(": ");
                    DumpInto(sb, o[k], depth + 1);
                }
                sb.Append('}');
                break;
            case System.Collections.IEnumerable en:
                sb.Append('[');
                var f2 = true;
                foreach (var item in en)
                {
                    if (!f2)
                    {
                        sb.Append(", ");
                    }
                    f2 = false;
                    DumpInto(sb, item, depth + 1);
                }
                sb.Append(']');
                break;
            default:
                sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture));
                break;
        }
    }

    private static object MergeScriptObjects(object? a, object? b)
    {
        var result = new ScriptObject();
        if (a is ScriptObject sa)
        {
            foreach (var k in sa.Keys.OfType<string>())
            {
                result[k] = sa[k];
            }
        }
        if (b is ScriptObject sb)
        {
            foreach (var k in sb.Keys.OfType<string>())
            {
                result[k] = sb[k];
            }
        }
        return result;
    }

    /// <summary>math.Counter：按 key 递增（Hugo 语义，进程内共享）</summary>
    /// <summary>
    /// 构造 query 映射对象（Hugo <c>url.Values</c> 语义）：同名键取首值，
    /// 并暴露 <c>get</c>/<c>Get</c>（大小写不敏感）。
    /// Congo 的 render-image.html 写 `$params = $url?.query` 后 `$params.get "2x"`，
    /// 暴露成裸字符串时报 "The function `$params.get` was not found"；
    /// 原始串另走 raw_query/RawQuery（Hugo 同名）
    /// </summary>
    /// <summary>
    /// YAML 子集解析：序列（<c>- item</c>）、映射（<c>key: value</c>）、缩进嵌套
    /// （映射值/序列项）与标量（去引号字符串、布尔、数值）。
    /// 主题的 assets 清单多是纯序列文件（hugo-book 的 styles/index.yaml 实测），
    /// 早期实现只认 <c>key: value</c> → 序列被解析成空对象 → 后续 resources.Concat
    /// 拿到空集合 → partial 上下文为 null → 整站只剩 404 页
    /// </summary>
    private static object ParseSimpleYaml(string text)
    {
        var entries = new List<(int Indent, string Content)>();
        foreach (var raw in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var trimmed = raw.TrimEnd();
            if (trimmed.Length == 0)
            {
                continue;
            }
            var content = trimmed.TrimStart();
            if (content.StartsWith('#'))
            {
                continue;
            }
            entries.Add((trimmed.Length - content.Length, content));
        }
        if (entries.Count == 0)
        {
            return new ScriptObject();
        }
        var pos = 0;
        return ParseYamlNode(entries, ref pos, entries[0].Indent);
    }

    private static object ParseYamlNode(List<(int Indent, string Content)> entries, ref int pos, int indent)
    {
        var isSequence = pos < entries.Count && entries[pos].Indent == indent &&
                         entries[pos].Content.StartsWith("- ", StringComparison.Ordinal);
        if (isSequence)
        {
            var arr = new ScriptArray();
            while (pos < entries.Count && entries[pos].Indent == indent &&
                   entries[pos].Content.StartsWith("- ", StringComparison.Ordinal))
            {
                var item = entries[pos].Content[2..].Trim();
                pos++;
                if (item.Length == 0)
                {
                    arr.Add(pos < entries.Count && entries[pos].Indent > indent
                        ? ParseYamlNode(entries, ref pos, entries[pos].Indent)
                        : new ScriptObject());
                    continue;
                }
                var colon = item.IndexOf(':');
                if (colon > 0)
                {
                    var map = new ScriptObject { [item[..colon].Trim()] = YamlScalar(item[(colon + 1)..].Trim()) };
                    while (pos < entries.Count && entries[pos].Indent > indent &&
                           !entries[pos].Content.StartsWith("- ", StringComparison.Ordinal))
                    {
                        var (childIndent, childText) = entries[pos];
                        var c = childText.IndexOf(':');
                        if (c <= 0)
                        {
                            pos++;
                            continue;
                        }
                        var key = childText[..c].Trim();
                        var rest = childText[(c + 1)..].Trim();
                        pos++;
                        map[key] = rest.Length == 0 && pos < entries.Count && entries[pos].Indent > childIndent
                            ? ParseYamlNode(entries, ref pos, entries[pos].Indent)
                            : YamlScalar(rest);
                    }
                    arr.Add(map);
                    continue;
                }
                arr.Add(YamlScalar(item));
            }
            return arr;
        }

        var obj = new ScriptObject();
        while (pos < entries.Count && entries[pos].Indent == indent)
        {
            var lineText = entries[pos].Content;
            var colon = lineText.IndexOf(':');
            if (colon <= 0)
            {
                pos++;
                continue;
            }
            var mapKey = lineText[..colon].Trim();
            var mapRest = lineText[(colon + 1)..].Trim();
            pos++;
            obj[mapKey] = mapRest.Length == 0 && pos < entries.Count && entries[pos].Indent > indent
                ? ParseYamlNode(entries, ref pos, entries[pos].Indent)
                : YamlScalar(mapRest);
        }
        return obj;
    }

    /// <summary>YAML 标量：去引号、识别布尔与数值，其余原样</summary>
    private static object YamlScalar(string v)
    {
        var s = v.Trim();
        if (s.Length >= 2 && ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')))
        {
            s = s[1..^1];
        }
        if (s is "true" or "false")
        {
            return s == "true";
        }
        if (s.Length > 0 && (char.IsDigit(s[0]) || s[0] == '-') &&
            double.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var d))
        {
            return d;
        }
        return s;
    }

    /// <summary>
    /// 取参数表里最后一个「文本位」参数：字符串直接用，资源对象取其内容
    /// （Hugo 的 transform.Unmarshal 接受字符串或 resources.Get 的结果，
    /// 选项字典在 Go 里前置）
    /// </summary>
    private static string? LastTextArg(object?[] args)
    {
        for (var i = args.Length - 1; i >= 0; i--)
        {
            switch (args[i])
            {
                case string s:
                    return s;
                case TemplateResource r:
                    return r.Content;
                case ScriptObject o when o.ContainsKey("content") && o["content"] is string c:
                    return c;
            }
        }
        return null;
    }

    /// <summary>
    /// diagrams.Goat / diagrams.ASCIIArt（Hugo 的字符图）：把文本渲染为 SVG 片段，
    /// 返回含 <c>Inner</c>/<c>Width</c>/<c>Height</c> 的对象（LoveIt 的
    /// plugin/goat.html 用 `{{ with diagrams.Goat .Inner }}` 取这三者）。
    /// 保真度说明：Hugo 用内嵌的 ASCII 艺术字形表逐字绘制，Flint 用等宽 SVG text
    /// 逐字排布（结构等价、视觉是普通等宽文本）——形态差异记录在迁移报告
    /// </summary>
    private static ScriptObject BuildGoatDiagram(string? text)
    {
        const int charWidth = 8;
        const int diagramHeight = 25;
        // (char)10 = 换行：用码点避免源码里的转义层级
        const char nl = (char)10;
        var content = text ?? "";
        var sb = new System.Text.StringBuilder();
        sb.Append("<g transform='translate(8,16)'>").Append(nl);
        var col = 0;
        var maxCols = 0;
        var lines = 1;
        foreach (var ch in content)
        {
            if (ch == nl)
            {
                maxCols = Math.Max(maxCols, col);
                col = 0;
                lines++;
                sb.Append("</g>").Append(nl).Append("<g transform='translate(8,16)'>").Append(nl);
                continue;
            }
            sb.Append("<text text-anchor='middle' x='").Append(col * charWidth)
              .Append("' y='4' fill='currentColor' style='font-size:1em'>")
              .Append(System.Net.WebUtility.HtmlEncode(ch.ToString()))
              .Append("</text>").Append(nl);
            col++;
        }
        sb.Append("</g>");
        maxCols = Math.Max(maxCols, col);

        var o = new ScriptObject
        {
            ["Inner"] = sb.ToString(),
            ["inner"] = sb.ToString(),
            ["Width"] = maxCols * charWidth,
            ["width"] = maxCols * charWidth,
            ["Height"] = lines * diagramHeight,
            ["height"] = lines * diagramHeight
        };
        return o;
    }

    private static ScriptObject BuildQueryObject(string rawQuery)
    {
        var o = new ScriptObject();
        var query = rawQuery.TrimStart('?');
        var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=', StringComparison.Ordinal);
            var k = eq >= 0 ? part[..eq] : part;
            var v = eq >= 0 ? part[(eq + 1)..] : "";
            v = Uri.UnescapeDataString(v.Replace('+', ' '));
            if (k.Length == 0 || pairs.ContainsKey(k))
            {
                continue;
            }
            pairs[k] = v;
            o[k] = v;
        }
        o.Import("get", (string? key) =>
            key is not null && pairs.TryGetValue(key, out var found) ? found : null);
        o.Import("Get", (string? key) =>
            key is not null && pairs.TryGetValue(key, out var found) ? found : null);
        return o;
    }

    private sealed class CounterFunction(Dictionary<string, long> counters, object gate)
        : Scriban.Runtime.IScriptCustomFunction
    {
        public object? Invoke(Scriban.TemplateContext context, Scriban.Syntax.ScriptNode? callerContext,
            ScriptArray arguments, Scriban.Syntax.ScriptBlockStatement? blockStatement)
        {
            if (arguments.Count == 0)
            {
                return 0L;
            }
            var key = arguments[0]?.ToString() ?? "";
            lock (gate)
            {
                counters.TryGetValue(key, out var current);
                counters[key] = current + 1;
                return current;
            }
        }

        public ValueTask<object?> InvokeAsync(Scriban.TemplateContext context, Scriban.Syntax.ScriptNode? callerContext,
            ScriptArray arguments, Scriban.Syntax.ScriptBlockStatement? blockStatement) =>
            new(Invoke(context, callerContext, arguments, blockStatement));

        public int RequiredParameterCount => 0;
        public int ParameterCount => 1;
        public ScriptVarParamKind VarParamKind => ScriptVarParamKind.Direct;
        public Type ReturnType => typeof(object);
        public ScriptParameterInfo GetParameterInfo(int index) => new(typeof(object), "key");
        public ScriptParameterInfo ReturnParameterInfo => new(typeof(object), "value");
    }
}

#pragma warning restore IL2026, IL3050

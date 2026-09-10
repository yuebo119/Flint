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
    internal static void RegisterMissingFunctions(ScriptObject obj)
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
        Add("slicestr", (string? s, object? startRaw, object? endRaw) =>
        {
            if (string.IsNullOrEmpty(s))
            {
                return "";
            }
            var start = (int)ToDouble(startRaw);
            var from = Math.Clamp(start, 0, s.Length);
            var end = endRaw is null ? (int?)null : (int)ToDouble(endRaw);
            var to = end is null or < 0 ? s.Length : Math.Clamp(end.Value, from, s.Length);
            return s[from..to];
        });
        Add("find_re", (string? pattern, string? input) =>
        {
            if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(input))
            {
                return new ScriptArray();
            }
            try
            {
                var arr = new ScriptArray();
                foreach (var m in Regex.Matches(input, pattern).Take(100))
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
        Add("find_re_submatch", (string? pattern, string? input) =>
        {
            if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(input))
            {
                return new ScriptArray();
            }
            try
            {
                var arr = new ScriptArray();
                foreach (Match m in Regex.Matches(input, pattern).Take(100))
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
        Add("error_idf", (string? id, string? message) => $"[{id}] {message}");
        Add("warn_idf", (string? id, string? message) => $"[{id}] {message}");

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
        Add("url_parse", (string? s) =>
        {
            var o = new ScriptObject();
            if (Uri.TryCreate(s, UriKind.Absolute, out var u))
            {
                o["scheme"] = u.Scheme;
                o["host"] = u.Host;
                o["path"] = u.AbsolutePath;
                o["query"] = u.Query.TrimStart('?');
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
                o["query"] = q >= 0 ? raw[(q + 1)..] : "";
                o["fragment"] = "";
                o["opaque"] = "";
                o["user"] = "";
            }
            else
            {
                o["scheme"] = "";
                o["host"] = "";
                o["path"] = "";
                o["query"] = "";
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
        Add("reflect_is_map", (object? v) => v is ScriptObject or IDictionary<string, object>);
        Add("reflect_is_slice", (object? v) => v is System.Collections.IEnumerable and not string);
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
        Add("template_exists", (string? name) => !string.IsNullOrEmpty(name));
        Add("template_current", () => new ScriptObject());
        Add("template_inner", (object? v) => v);
        Add("template_defer", (object? v) => v);
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

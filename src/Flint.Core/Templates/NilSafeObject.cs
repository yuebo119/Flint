// Flint 静态站点生成器
// nil 安全容器（对齐 Go template 的缺失键语义）
//
// 问题：Go template 的 `{{ .Params.missing.deep }}` 对缺失键返回 <no value>
// 而不报错（Go 的 map 索引返回零值）。Scriban 对 null 的成员访问抛
// "Cannot get the member X for a null object"——两者语义冲突。
//
// 实测：Ananke 的 `site.params.ananke.pages.show_date` 在站点未配置 ananke
// 参数时阻断整站构建（Go 下只是空值）。
//
// 方案：Params 用 nil 安全对象包装——缺失键返回**空对象**（可继续链式访问，
// 最终渲染为空字符串），命中键返回真实值。这与 Hugo 的宽容语义一致。

using Scriban.Runtime;

namespace Flint.Core.Templates;

/// <summary>
/// nil 安全的参数容器：缺失键返回空 <see cref="ScriptObject"/>（而非 null），
/// 使深层链式访问（a.b.c.d）不因中间层缺失而报错。
/// </summary>
public sealed class NilSafeObject : ScriptObject, IEnumerable<object>
{
    private readonly IReadOnlyDictionary<string, object>? _source;
    private readonly Dictionary<string, object> _cache = new(StringComparer.OrdinalIgnoreCase);

    public NilSafeObject(IReadOnlyDictionary<string, object>? source)
    {
        _source = source;
    }

    public override bool TryGetValue(Scriban.TemplateContext? context, Scriban.Parsing.SourceSpan span,
        string member, out object? value)
    {
        // 命中：返回真实值（字典递归包装，保证深层链同样 nil 安全）
        if (_source is not null &&
            (_source.TryGetValue(member, out var found) ||
             TryGetIgnoreCase(member, out found)))
        {
            value = found switch
            {
                IReadOnlyDictionary<string, object> d => Wrap(d),
                _ => found
            };
            return true;
        }

        // 未命中：返回空对象（可继续链式访问，渲染为空串）——
        // 不对 null 报错，对齐 Go template 的宽容语义
        value = Wrap(null);
        return true;
    }

    private bool TryGetIgnoreCase(string member, out object value)
    {
        foreach (var kv in _source!)
        {
            if (string.Equals(kv.Key, member, StringComparison.OrdinalIgnoreCase))
            {
                value = kv.Value;
                return true;
            }
        }
        value = null!;
        return false;
    }

    /// <summary>
    /// 空序列语义：缺失参数的链式访问结果在序列函数（Delimit/slice/first/len）
    /// 中表现为空集合，而非"类型转换失败"（Hugo 的 nil 同样被当空集合处理）
    /// </summary>
    public new IEnumerator<object> GetEnumerator() => Enumerable.Empty<object>().GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        Enumerable.Empty<object>().GetEnumerator();

    private object Wrap(IReadOnlyDictionary<string, object>? source)
    {
        var key = source is null ? "\u0000nil" : source.GetHashCode().ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }
        return _cache[key] = new NilSafeObject(source);
    }
}

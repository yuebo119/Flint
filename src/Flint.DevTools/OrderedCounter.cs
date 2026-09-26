// 有序计数器：复刻 Python collections.Counter.most_common() 语义
// （按计数降序，同计数保持首次出现顺序——保证迁移后审计输出与 Python 版对拍一致）

using System.Text;

namespace Flint.DevTools;

/// <summary>
/// 保插入顺序的计数器，most_common 语义与 Python Counter 一致
/// </summary>
internal sealed class OrderedCounter
{
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
    private readonly List<string> _order = new();

    /// <summary>按插入序追加计数</summary>
    public void Add(string key, int amount = 1)
    {
        if (!_counts.ContainsKey(key))
        {
            _order.Add(key);
            _counts[key] = 0;
        }

        _counts[key] += amount;
    }

    /// <summary>读取计数（不存在返回 0）</summary>
    public int this[string key] => _counts.TryGetValue(key, out var n) ? n : 0;

    /// <summary>键数量（多重集种类数）</summary>
    public int KeyCount => _counts.Count;

    /// <summary>按计数降序取前 limit 项（同计数按首次出现顺序，对应 Python Counter.most_common）</summary>
    public List<KeyValuePair<string, int>> MostCommon(int limit)
    {
        return _order
            .Select(k => new KeyValuePair<string, int>(k, _counts[k]))
            .OrderByDescending(p => p.Value)
            .Take(limit)
            .ToList();
    }

    /// <summary>计数差集 this - other 的正值部分（保 this 插入序），对应 Python Counter 减法</summary>
    public List<KeyValuePair<string, int>> Minus(OrderedCounter other)
    {
        var result = new List<KeyValuePair<string, int>>();
        foreach (var key in _order)
        {
            var diff = _counts[key] - other[key];
            if (diff > 0)
            {
                result.Add(new KeyValuePair<string, int>(key, diff));
            }
        }

        return result;
    }
}

/// <summary>
/// Python 文本工具辅助
/// </summary>
internal static class PyCompat
{
    /// <summary>复刻 Python repr(float) 的显示格式（最短往返表示且必含小数点）</summary>
    public static string FormatDouble(double value)
    {
        var s = value.ToString("0.############", System.Globalization.CultureInfo.InvariantCulture);
        return s.Contains('.') ? s : s + ".0";
    }

    /// <summary>按空白切分并丢弃空段（对应 Python str.split()）</summary>
    public static string[] SplitWhitespace(string s)
    {
        return s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    }
}

// Flint 静态站点生成器
// 页面级暂存对象（Hugo .Scratch / .Store 的等价物）
//
// Hugo 语义：页面渲染期内可变的键值容器，用于跨块/跨 partial 传递状态
// （Set/Get/Add/SetInMap/DeleteInMap/GetSortedMapValues/Delete）。
// 实测主题用法：Ananke 短码内 Set+Get 同文件；LoveIt/PaperMod 有跨 partial 用法。
//
// 并发：页面渲染是并行的，但同一页面实例只在一个批次线程内渲染；
// 内部仍加锁，因为同一页面的多个 partial 共享该对象且 Scriban 可能并发求值。

using System.Globalization;
using Scriban.Runtime;

namespace Flint.Core.Templates;

/// <summary>
/// 页面级可变暂存（Hugo .Scratch / .Store）。
/// 以 ScriptObject 派生便于模板用 <c>store.key</c> 直接取值。
/// </summary>
public sealed class PageStoreObject : ScriptObject, IFlintNonDataObject
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, object?>> _maps = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public PageStoreObject()
    {
        // Scriban 只解析注册成员，不调用 C# 公开方法——把 Store API 注册为函数
        // （IScriptCustomFunction 形式，AOT 安全，不经反射绑定）。
        // 同时注册 PascalCase 别名：Hugo 文档写 .Scratch.Set/.Add，主题照抄大小写
        //（PaperMod 用 `$scratch.Add`，缺别名时报 "Cannot get the member Add for a null object"）
        Register("set", "Set", StoreOp.Set);
        Register("get", "Get", StoreOp.Get);
        Register("add", "Add", StoreOp.Add);
        Register("delete", "Delete", StoreOp.Delete);
        Register("setinmap", "SetInMap", StoreOp.SetInMap);
        Register("deleteinmap", "DeleteInMap", StoreOp.DeleteInMap);
        Register("getsortedmapvalues", "GetSortedMapValues", StoreOp.GetSortedMapValues);
        Register("values", "Values", StoreOp.Values);
    }

    private void Register(string lower, string pascal, StoreOp op)
    {
        var fn = new StoreFunction(this, op);
        SetValue(lower, fn, false);
        SetValue(pascal, fn, false);
    }

    /// <summary>Set KEY VALUE：覆盖写入</summary>
    public void Set(string key, object? value)
    {
        lock (_gate)
        {
            _values[key] = value;
        }
    }

    /// <summary>
    /// 缺失键是否返回**空对象**而非 null（render hook 上下文用）。
    /// hook 在内容解析期渲染，此时页面暂存尚未被布局写入——Hugo 里
    /// `.Scratch.Get "params"` 能取到先前布局写入的值，Flint 取不到。
    /// 返回空对象让主题的 `$params.code.copy | default true` 这类链式访问
    /// 得到 null 而不是 "Cannot get the member … for a null object"
    /// 整篇解析失败（LoveIt 的 render-codeblock-goat.html 实测）
    /// </summary>
    internal bool MissingKeyReturnsEmptyObject { get; init; }

    /// <summary>Get KEY：取值，缺失返回 null（或空对象，见上）</summary>
    public object? Get(string key)
    {
        lock (_gate)
        {
            if (_values.TryGetValue(key, out var v))
            {
                // 页面列表回包装：Scratch/Store 里累积的页面集合取出来仍是裸列表，
                // 集合方法族缺失（hugo-book 的 `$scratch.Add "BookPages" (slice .Page)`
                // → `$pages.Next page` 报 "The function `$pages.Next` was not found"）。
                // 与 where/append 等内置同口径：页面序列的派生仍是页面序列
                // ScriptArray / List<object?> 都要覆盖：首次 Add 原样存入的切片是
                // ScriptArray（实现 IList<object>），只认 IReadOnlyList 会漏
                if (v is System.Collections.Generic.IList<object?> list && list.Count > 0)
                {
                    var items = new List<object?>(list.Count);
                    foreach (var item in list)
                    {
                        items.Add(item);
                    }
                    return ScribanTemplateRenderer.RewrapPageSequence(null, items) ?? v;
                }
                return v;
            }
            return MissingKeyReturnsEmptyObject ? new ScriptObject() : null;
        }
    }

    /// <summary>
    /// Add KEY VALUE：序列追加 / 数值累加 / 字符串拼接（Hugo 语义）。
    /// 实测主题用法：<c>$.Scratch.Add "index" (dict ...)</c>（序列追加）
    /// </summary>
    /// <summary>
    /// Add KEY VALUE（内部实现）。命名不用 Add——那会遮蔽 ScriptObject.Add，
    /// Scriban 内部用 Add 构建对象，被劫持会污染暂存（实测 11 个内部键）
    /// </summary>
    public object? AddValue(string key, object? value)
    {
        lock (_gate)
        {
            if (!_values.TryGetValue(key, out var current) || current is null)
            {
                // 首次 Add：**原样存入**。Hugo v0.166 实测：数字 5+7=12、字符串
                // "a"+"b"="ab"、切片 (slice 1)+(slice 2 3)=[1 2 3]——即"首个值直接成为
                // 当前值，后续 Add 按类型累加"。此前包成单元素列表，使数值求和/字符串
                // 拼接分支永不触发（FixIt 的 section.html 用
                // `$localData.Add "totalWordCount" .WordCount` 求总字数，包成列表后
                // `div (list) 1000.0` 报"数学函数收到的参数值类型（不接受）: List<Object>"）
                _values[key] = value;
                return value;
            }
            switch (current)
            {
                case string s when value is string:
                    // 已有值为字符串且新值也是字符串 → 拼接（Hugo 语义）
                    _values[key] = s + value;
                    return _values[key];
                // 追加时同样展平序列实参（Hugo 语义，同上）：页面对象/内部投影/字符串
                // 不是序列，按单值追加
                case List<object?> list:
                    AppendFlattened(list, value);
                    return list;
                case ScriptArray arr:
                    AppendFlattened(arr, value);
                    return arr;
                default:
                    if (IsNumeric(current) && IsNumeric(value))
                    {
                        _values[key] = ToDouble(current) + ToDouble(value);
                        return _values[key];
                    }
                    var nl = new List<object?> { current, value };
                    _values[key] = nl;
                    return nl;
            }
        }
    }

    /// <summary>是否"可展平的序列"：页面对象、内部投影、字符串都不是</summary>
    private static bool IsAppendableSequence(object? value) => value switch
    {
        null or string => false,
        IFlintNonDataObject => false,
        ScribanTemplateRenderer.LazyPageObject => false,
        ScriptArray => true,
        System.Collections.IList => true,
        System.Collections.Generic.IList<Scriban.Runtime.ScriptObject> => true,
        System.Collections.Generic.IEnumerable<object?> => true,
        _ => false
    };

    private static void AppendFlattened(System.Collections.IList target, object? value)
    {
        if (value is not null && IsAppendableSequence(value))
        {
            foreach (var item in (System.Collections.IEnumerable)value)
            {
                target.Add(item);
            }
            return;
        }
        target.Add(value);
    }

    /// <summary>Delete KEY</summary>
    public void Delete(string key)
    {
        lock (_gate)
        {
            _values.Remove(key);
            _maps.Remove(key);
        }
    }

    /// <summary>SetInMap MAP KEY VALUE：嵌套映射写入</summary>
    public void SetInMap(string map, string key, object? value)
    {
        lock (_gate)
        {
            if (!_maps.TryGetValue(map, out var inner))
            {
                inner = new Dictionary<string, object?>(StringComparer.Ordinal);
                _maps[map] = inner;
            }
            inner[key] = value;
        }
    }

    /// <summary>DeleteInMap MAP KEY</summary>
    public void DeleteInMap(string map, string key)
    {
        lock (_gate)
        {
            if (_maps.TryGetValue(map, out var inner))
            {
                inner.Remove(key);
            }
        }
    }

    /// <summary>GetSortedMapValues MAP：按 key 排序的值列表（Hugo 语义）</summary>
    public ScriptArray GetSortedMapValues(string map)
    {
        lock (_gate)
        {
            var arr = new ScriptArray();
            if (_maps.TryGetValue(map, out var inner))
            {
                foreach (var kv in inner.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    arr.Add(kv.Value);
                }
            }
            return arr;
        }
    }

    /// <summary>Values：按 key 排序的全部值（Hugo .Store.Values）</summary>
    public new ScriptArray Values
    {
        get
        {
            lock (_gate)
            {
                var arr = new ScriptArray();
                foreach (var kv in _values.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    arr.Add(kv.Value);
                }
                return arr;
            }
        }
    }

    /// <summary>模板直接取键（store.foo）：先查暂存，再查映射</summary>
    public override bool TryGetValue(Scriban.TemplateContext? context, Scriban.Parsing.SourceSpan span,
        string member, out object? value)
    {
        lock (_gate)
        {
            if (_values.TryGetValue(member, out var v))
            {
                value = v;
                return true;
            }
            if (_maps.TryGetValue(member, out var m))
            {
                var o = new ScriptObject();
                foreach (var kv in m)
                {
                    o[kv.Key] = kv.Value;
                }
                value = o;
                return true;
            }
        }
        return base.TryGetValue(context, span, member, out value);
    }

    private static bool IsNumeric(object? v) =>
        v is int or long or double or float or decimal or short or byte;

    private static double ToDouble(object? v) =>
        v is null ? 0d
        : Convert.ToDouble(v, CultureInfo.InvariantCulture);
}

/// <summary>Store 操作类型</summary>
public enum StoreOp
{
    Set, Get, Add, Delete, SetInMap, DeleteInMap, GetSortedMapValues, Values
}

/// <summary>
/// Store API 的模板函数包装：单一实现按 op 分发（减少 8 个样板类）。
/// IScriptCustomFunction 形式——不经反射绑定，AOT 安全
/// </summary>
public sealed class StoreFunction(PageStoreObject store, StoreOp op) : Scriban.Runtime.IScriptCustomFunction
{
    public object? Invoke(Scriban.TemplateContext context, Scriban.Syntax.ScriptNode? callerContext,
        Scriban.Runtime.ScriptArray arguments, Scriban.Syntax.ScriptBlockStatement? blockStatement)
    {
        string Arg(int i) => arguments.Count > i ? arguments[i]?.ToString() ?? "" : "";
        object? ArgObj(int i) => arguments.Count > i ? arguments[i] : null;

        return op switch
        {
            StoreOp.Set => SetAnd(Arg(0), ArgObj(1)),
            StoreOp.Get => store.Get(Arg(0)),
            // Add 与 Set/Delete 同口径：**返回空串**。Hugo 的 Scratch.Add 是
            // `func (c *Scratch) Add(...)`（无返回值）→ 模板里 `{{ $s.Add "k" v }}`
            // 渲染为空；此前返回存入值，使每个"只调用不接收"的 Add 都往页面里
            // 吐一段文本（`{{ $s.add "n" 5 }}` 实测输出 "5"）
            StoreOp.Add => AddAnd(Arg(0), ArgObj(1)),
            StoreOp.Delete => DeleteAnd(Arg(0)),
            StoreOp.SetInMap => SetInMapAnd(Arg(0), Arg(1), ArgObj(2)),
            StoreOp.DeleteInMap => DeleteInMapAnd(Arg(0), Arg(1)),
            StoreOp.GetSortedMapValues => store.GetSortedMapValues(Arg(0)),
            StoreOp.Values => store.Values,
            _ => null
        };
    }

    private object? AddAnd(string key, object? value)
    {
        store.AddValue(key, value);
        return "";
    }

    private object? SetAnd(string key, object? value)
    {
        store.Set(key, value);
        return "";
    }

    private object? DeleteAnd(string key)
    {
        store.Delete(key);
        return "";
    }

    private object? SetInMapAnd(string map, string key, object? value)
    {
        store.SetInMap(map, key, value);
        return "";
    }

    private object? DeleteInMapAnd(string map, string key)
    {
        store.DeleteInMap(map, key);
        return "";
    }

    public ValueTask<object?> InvokeAsync(Scriban.TemplateContext context, Scriban.Syntax.ScriptNode? callerContext,
        Scriban.Runtime.ScriptArray arguments, Scriban.Syntax.ScriptBlockStatement? blockStatement) =>
        new(Invoke(context, callerContext, arguments, blockStatement));

    public int RequiredParameterCount => 0;
    public int ParameterCount => 3;
    public Scriban.Runtime.ScriptVarParamKind VarParamKind => Scriban.Runtime.ScriptVarParamKind.Direct;
    public Type ReturnType => typeof(object);
    public Scriban.Runtime.ScriptParameterInfo GetParameterInfo(int index) => new(typeof(object), "arg" + index);
    public Scriban.Runtime.ScriptParameterInfo ReturnParameterInfo => new(typeof(object), "result");
}

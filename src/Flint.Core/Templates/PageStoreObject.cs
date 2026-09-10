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
public sealed class PageStoreObject : ScriptObject
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, object?>> _maps = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public PageStoreObject()
    {
        // Scriban 只解析注册成员，不调用 C# 公开方法——把 Store API 注册为函数
        // （IScriptCustomFunction 形式，AOT 安全，不经反射绑定）
        SetValue("set", new StoreFunction(this, StoreOp.Set), false);
        SetValue("get", new StoreFunction(this, StoreOp.Get), false);
        SetValue("add", new StoreFunction(this, StoreOp.Add), false);
        SetValue("delete", new StoreFunction(this, StoreOp.Delete), false);
        SetValue("setinmap", new StoreFunction(this, StoreOp.SetInMap), false);
        SetValue("deleteinmap", new StoreFunction(this, StoreOp.DeleteInMap), false);
        SetValue("getsortedmapvalues", new StoreFunction(this, StoreOp.GetSortedMapValues), false);
        SetValue("values", new StoreFunction(this, StoreOp.Values), false);
    }

    /// <summary>Set KEY VALUE：覆盖写入</summary>
    public void Set(string key, object? value)
    {
        lock (_gate)
        {
            _values[key] = value;
        }
    }

    /// <summary>Get KEY：取值，缺失返回 null</summary>
    public object? Get(string key)
    {
        lock (_gate)
        {
            return _values.TryGetValue(key, out var v) ? v : null;
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
                // 首次 Add：包成单元素序列。Hugo Scratch.Add 语义是"收集"——
                // 序列化初值后，后续 Add 走列表追加；字符串拼接只在
                // 已有值为字符串且新值也为字符串时发生（见下方 string 分支）
                var fresh = new List<object?>();
                if (value is not null)
                {
                    fresh.Add(value);
                }
                _values[key] = fresh;
                return fresh;
            }

            switch (current)
            {
                case string s when value is string:
                    // 已有值为字符串且新值也是字符串 → 拼接（Hugo 语义）
                    _values[key] = s + value;
                    return _values[key];
                case List<object?> list:
                    list.Add(value);
                    return list;
                case ScriptArray arr:
                    arr.Add(value);
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
            StoreOp.Add => store.AddValue(Arg(0), ArgObj(1)),
            StoreOp.Delete => DeleteAnd(Arg(0)),
            StoreOp.SetInMap => SetInMapAnd(Arg(0), Arg(1), ArgObj(2)),
            StoreOp.DeleteInMap => DeleteInMapAnd(Arg(0), Arg(1)),
            StoreOp.GetSortedMapValues => store.GetSortedMapValues(Arg(0)),
            StoreOp.Values => store.Values,
            _ => null
        };
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

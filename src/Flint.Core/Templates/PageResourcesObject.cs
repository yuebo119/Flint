// Flint 静态站点生成器
// 页面资源集合对象（Hugo .Resources 的方法族）
//
// 实测主题用法（Ananke/Stack/LoveIt）：
//   .Resources.ByType "image"    → 按类型筛选
//   .Resources.GetMatch "x*"     → 首个匹配
//   .Resources.Match "x*"        → 全部匹配
//   .Resources.Get "x.jpg"       → 精确取
//
// 此前 page.resources 是裸列表，主题调用方法时报 "function ... not found"。

using System.Text.RegularExpressions;
using Scriban.Runtime;

namespace Flint.Core.Templates;

/// <summary>
/// 页面资源集合（Hugo .Resources）：派生自 ScriptObject 以便模板直接迭代，
/// 同时暴露 ByType/Get/Match/GetMatch 方法。
/// 元素可能是 bundle 资源对象（ScriptObject）或路径字符串，此处统一按名称查询。
/// </summary>
public sealed class PageResourcesObject : ScriptObject
{
    private readonly List<object> _items;

    public PageResourcesObject(IReadOnlyList<object>? items)
    {
        _items = items is null ? [] : [.. items];

        // 集合自身属性
        SetValue("length", _items.Count, false);
        SetValue("count", _items.Count, false);
        SetValue("size", _items.Count, false);

        // 方法族（Scriban 只调用注册成员）
        SetValue("bytype", new ResourcesFilterFunction(this, ResourcesOp.ByType), false);
        SetValue("get", new ResourcesFilterFunction(this, ResourcesOp.Get), false);
        SetValue("match", new ResourcesFilterFunction(this, ResourcesOp.Match), false);
        SetValue("getmatch", new ResourcesFilterFunction(this, ResourcesOp.GetMatch), false);
    }

    /// <summary>原始项列表</summary>
    internal IReadOnlyList<object> Items => _items;

    /// <summary>取资源名（支持字符串路径与资源对象）</summary>
    internal static string? NameOf(object? item) =>
        item switch
        {
            null => null,
            string s => s,
            ScriptObject o => o["name"]?.ToString() ?? o["Name"]?.ToString(),
            _ => item.ToString()
        };

    /// <summary>取资源类型（对象有 resource_type 时用之，否则按扩展名推断）</summary>
    internal static string TypeOf(object? item)
    {
        if (item is ScriptObject o)
        {
            var t = o["resource_type"]?.ToString() ?? o["ResourceType"]?.ToString();
            if (!string.IsNullOrEmpty(t))
            {
                return t;
            }
        }
        var name = NameOf(item) ?? "";
        return Path.GetExtension(name).TrimStart('.').ToLowerInvariant() switch
        {
            "jpg" or "jpeg" or "png" or "gif" or "webp" or "avif" or "svg" or "bmp" => "image",
            "css" or "scss" or "sass" => "css",
            "js" or "mjs" or "ts" => "js",
            "json" => "json",
            "html" or "htm" => "html",
            _ => "other"
        };
    }
}

/// <summary>资源查询操作</summary>
public enum ResourcesOp
{
    ByType,
    Get,
    Match,
    GetMatch
}

/// <summary>资源查询函数（IScriptCustomFunction，AOT 安全）</summary>
public sealed class ResourcesFilterFunction(PageResourcesObject owner, ResourcesOp op)
    : Scriban.Runtime.IScriptCustomFunction
{
    public object? Invoke(Scriban.TemplateContext context, Scriban.Syntax.ScriptNode? callerContext,
        ScriptArray arguments, Scriban.Syntax.ScriptBlockStatement? blockStatement)
    {
        var arg = arguments.Count > 0 ? arguments[0]?.ToString() ?? "" : "";
        return op switch
        {
            ResourcesOp.ByType => Collect(r => PageResourcesObject.TypeOf(r) == arg),
            ResourcesOp.Get => SelectOne(r =>
                (PageResourcesObject.NameOf(r) ?? "").EndsWith(arg, StringComparison.OrdinalIgnoreCase)),
            ResourcesOp.Match => Collect(r => GlobMatch(PageResourcesObject.NameOf(r) ?? "", arg)),
            ResourcesOp.GetMatch => SelectOne(r => GlobMatch(PageResourcesObject.NameOf(r) ?? "", arg)),
            _ => null
        };
    }

    private ScriptArray Collect(Func<object, bool> predicate)
    {
        var arr = new ScriptArray();
        foreach (var item in owner.Items.Where(predicate))
        {
            arr.Add(item);
        }
        return arr;
    }

    private object? SelectOne(Func<object, bool> predicate) => owner.Items.FirstOrDefault(predicate);

    private const char BackslashChar = (char)92;

    /// <summary>Hugo glob（* 单段，** 跨目录，? 单字符）</summary>
    internal static bool GlobMatch(string name, string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return false;
        }
        var p = pattern.Replace(BackslashChar, '/').TrimStart('/');
        var sb = new System.Text.StringBuilder("^");
        for (var i = 0; i < p.Length; i++)
        {
            var c = p[i];
            switch (c)
            {
                case '*' when i + 1 < p.Length && p[i + 1] == '*':
                    sb.Append(".*");
                    i++;
                    break;
                case '*':
                    sb.Append("[^/]*");
                    break;
                case '?':
                    sb.Append("[^/]");
                    break;
                default:
                    sb.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }
        sb.Append('$');
        return Regex.IsMatch(name.Replace(BackslashChar, '/'), sb.ToString(),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public ValueTask<object?> InvokeAsync(Scriban.TemplateContext context, Scriban.Syntax.ScriptNode? callerContext,
        ScriptArray arguments, Scriban.Syntax.ScriptBlockStatement? blockStatement) =>
        new(Invoke(context, callerContext, arguments, blockStatement));

    public int RequiredParameterCount => 1;
    public int ParameterCount => 1;
    public ScriptVarParamKind VarParamKind => ScriptVarParamKind.Direct;
    public Type ReturnType => typeof(object);
    public ScriptParameterInfo GetParameterInfo(int index) => new(typeof(string), "filter");
    public ScriptParameterInfo ReturnParameterInfo => new(typeof(object), "result");
}

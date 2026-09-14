// Flint 静态站点生成器
// 模板驱动的用户短代码

using System.Globalization;
using Flint.Core.Abstractions;
using Flint.Core.Templates;
using Scriban;
using Scriban.Runtime;

namespace Flint.Core.Content.Shortcodes;

/// <summary>
/// 模板驱动的用户短代码
/// 从站点 <c>layouts/shortcodes/&lt;name&gt;.html</c> 加载 Scriban 模板并渲染——
/// 对齐 Hugo"短代码即模板"的语义：站点可用模板覆盖内置短代码。
/// 模板内可用变量（Hugo 短码 API 对齐）：
///   <c>get "key"</c> / <c>get 0</c>（命名优先，数字取位置参数）
///   <c>is_named_params</c>（是否命名参数调用）、<c>params</c>（全参数表）
///   <c>params.foo</c>、<c>positional.0</c>、<c>inner</c>、<c>name</c>
/// </summary>
public sealed class TemplateShortcode : IShortcodeProcessor
{
    private static readonly CultureInfo InvariantCulture = CultureInfo.InvariantCulture;

    private readonly Template _template;

    /// <summary>
    /// 创建模板短代码
    /// </summary>
    /// <param name="name">短代码名（文件名去除扩展名）</param>
    /// <param name="templateContent">模板内容（Scriban 语法）</param>
    /// <exception cref="FormatException">模板语法错误</exception>
    /// <summary>
    /// 上下文补装委托：由渲染器构造时注入（注册内置函数与 partial 等全局）。
    /// 短代码在内容解析期渲染，此时尚无页面上下文，故只补装与页面无关的全局
    /// </summary>
    internal static Action<Scriban.TemplateContext, Scriban.Runtime.ScriptObject>? EnrichContext { get; set; }

    public TemplateShortcode(string name, string templateContent)
    {
        Name = name;
        _template = Template.Parse(templateContent);
        if (_template.HasErrors)
        {
            var messages = string.Join("; ", _template.Messages);
            throw new FormatException(
                string.Format(InvariantCulture, "短代码模板 {0}.html 语法错误: {1}", name, messages));
        }
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Description => $"用户短代码（layouts/shortcodes/{Name}.html）";

    /// <summary>
    /// 父短代码的模板视角对象（Hugo 的 <c>.Parent</c>）：暴露 name/ordinal/params/
    /// inner 以及自身的 parent（链式可追溯）。顶层返回 null —— Scriban 里
    /// <c>{{ if not .Parent }}</c> 对 nil 判真，与 Hugo 语义一致
    /// </summary>
    private static Scriban.Runtime.ScriptObject? BuildParentObject(ShortcodeParent? parent)
    {
        if (parent is not { } p)
        {
            return null;
        }

        var obj = new Scriban.Runtime.ScriptObject
        {
            ["name"] = p.Name,
            ["ordinal"] = p.Ordinal,
            ["inner"] = p.InnerContent ?? string.Empty,
            ["is_named_params"] = p.Parameters.Count > 0
        };

        var paramsObject = new Scriban.Runtime.ScriptObject();
        foreach (var (key, value) in p.Parameters)
        {
            paramsObject[key] = value;
        }
        obj["params"] = paramsObject;
        obj["Params"] = paramsObject;

        var positional = new Scriban.Runtime.ScriptArray();
        foreach (var arg in p.PositionalArgs)
        {
            positional.Add(arg);
        }
        obj["positional"] = positional;

        if (BuildParentObject(p.Parent) is { } grandParent)
        {
            obj["parent"] = grandParent;
        }

        return obj;
    }

    /// <inheritdoc />
    public async ValueTask<string> ProcessAsync(
        ShortcodeContext context,
        CancellationToken cancellationToken = default)
    {
        var globals = new ScriptObject();

        // 全参数表（Hugo .Params）：命名参数 + 位置参数（数字键）
        var paramsObject = new ScriptObject();
        foreach (var (key, value) in context.Parameters)
        {
            paramsObject[key] = value;
        }
        for (var i = 0; i < context.PositionalArgs.Count; i++)
        {
            paramsObject[i.ToString(InvariantCulture)] = context.PositionalArgs[i];
        }
        globals["params"] = paramsObject;

        // 命名参数：小写与 Pascal 双键注册（与渲染器 Page/Site 对象的双键约定一致）
        foreach (var (key, value) in context.Parameters)
        {
            globals[key] = value;
            if (key.Length > 0)
            {
                globals[char.ToUpperInvariant(key[0]) + key[1..]] = value;
            }
        }

        // 位置参数
        var positional = new ScriptArray();
        foreach (var arg in context.PositionalArgs)
        {
            positional.Add(arg);
        }
        globals["positional"] = positional;

        // 内部内容与短代码名
        globals["inner"] = context.InnerContent ?? string.Empty;
        globals["name"] = context.Name;

        // Hugo 嵌套短代码契约：.Ordinal（同级序号）与 .Parent（父级上下文对象）。
        // 顶层短代码的 parent 为 nil —— 这与 Hugo 一致（布局里的短代码 Parent 为 nil），
        // 主题据此判断"是否被正确嵌套"（narrow 的 tab.html）
        globals["ordinal"] = context.Ordinal;
        globals["parent"] = BuildParentObject(context.Parent);

        // Hugo 短码 API 对齐：is_named_params + get（命名优先、数字取位置参数）
        globals["is_named_params"] = context.Parameters.Count > 0;
#pragma warning disable IL2026, IL3050 // Scriban Import 走反射构造 DynamicCustomFunction；lambda 装箱后方法体被 linker 保留，运行时安全（与 BuiltinTemplateFunctions 同口径）
        globals.Import("get", (object? key) =>
        {
            if (key is null)
            {
                return "";
            }
            var name = key.ToString() ?? "";
            // 数字键 → 位置参数（Hugo .Get 0 语义）
            if (int.TryParse(name, NumberStyles.Integer, InvariantCulture, out var index))
            {
                return index >= 0 && index < context.PositionalArgs.Count
                    ? context.PositionalArgs[index]
                    : "";
            }
            return context.Parameters.TryGetValue(name, out var value) ? value : "";
        });
#pragma warning restore IL2026, IL3050

        var templateContext = new FlintScribanContext
        {
            // 与页面渲染同口径：Scriban 的函数递归计数在嵌套渲染时不递减、
            // 会跨调用累积（短代码内调 partial 链会假性超限：
            // "Exceeding recursive depth limit, near to stack overflow"）
            RecursiveLimit = 0,
            LoopLimit = 1_000_000
        };
        templateContext.PushGlobal(globals);
        // 短代码此前用**全新空上下文**渲染：`partial`/`safe_html`/`as_list` 等
        // 全局函数全部不可用（Clarity 的 `partial "sprite"` 报 "function partial
        // was not found"，29 处）。渲染器构造时注入补装委托，使短代码与页面共享
        // 同一套全局函数；`page`/`site` 在解析期尚无值，但 partial 的上下文参数
        //（如 `partial "sprite" (dict "icon" "x")`）可正常传递
        EnrichContext?.Invoke(templateContext, globals);
        cancellationToken.ThrowIfCancellationRequested();
        return await _template.RenderAsync(templateContext);
    }
}

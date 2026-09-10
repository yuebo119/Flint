// Flint 静态站点生成器
// 模板驱动的用户短代码

using System.Globalization;
using Flint.Core.Abstractions;
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

        var templateContext = new Scriban.TemplateContext();
        templateContext.PushGlobal(globals);
        cancellationToken.ThrowIfCancellationRequested();
        return await _template.RenderAsync(templateContext);
    }
}

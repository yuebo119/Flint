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
/// 模板内可用变量：<c>params.src</c> 等命名参数、<c>positional.0</c> 位置参数、<c>inner</c> 内部内容、<c>name</c> 短代码名。
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

        var templateContext = new Scriban.TemplateContext();
        templateContext.PushGlobal(globals);
        cancellationToken.ThrowIfCancellationRequested();
        return await _template.RenderAsync(templateContext);
    }
}

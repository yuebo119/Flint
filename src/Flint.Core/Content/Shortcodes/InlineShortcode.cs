// Flint 静态站点生成器
// Hugo 内联短代码（inline shortcodes）
//
// Hugo 的内容内定义短代码：`enableInlineShortcodes = true` 时，内容里写
//
//   {{< css.inline >}}
//   <style>.foo { color: red }</style>
//   {{< /css.inline >}}
//
// 之间是**模板源码**，`.inline` 后缀标记这是内联定义（成对标签），
// 原位即刻渲染。主题示例内容大量使用这一形式（hugo-paper 的 math/css.inline、
// hugo-clarity 的 x/math/css.inline 实测）。
//
// 缺此特性时短代码注册表查不到名字 → PARSE001 "未注册的短代码"（Hugo 侧因
// enableInlineShortcodes=true 正常构建，Flint 侧整篇内容解析失败）。

using System.Collections.Concurrent;
using Flint.Core.Abstractions;

namespace Flint.Core.Content.Shortcodes;

/// <summary>
/// 内容内定义并即刻渲染的短代码（Hugo inline shortcodes）。
/// 渲染体即短代码的 <c>InnerContent</c>，用与
/// <see cref="TemplateShortcode"/> 相同的变量集合渲染（get/params/positional/…）
/// </summary>
public sealed class InlineShortcode : IShortcodeProcessor
{
    /// <summary>解析缓存：同一内容片段在列表页/单页多次渲染时复用模板（键为模板源）</summary>
    private static readonly ConcurrentDictionary<string, TemplateShortcode> Cache = new(StringComparer.Ordinal);

    private readonly TemplateShortcode _inner;

    private InlineShortcode(string name, TemplateShortcode inner)
    {
        Name = name;
        _inner = inner;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Description => "内联短代码（内容内 {{< name.inline >}} 定义，Hugo enableInlineShortcodes）";

    /// <summary>
    /// 短代码名是否为内联定义形态（Hugo 约定：<c>.inline</c> 后缀）
    /// </summary>
    public static bool IsInlineName(string name) =>
        name.EndsWith(".inline", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 由内容内的成对标签构造内联短代码。<paramref name="innerContent"/> 为 null
    /// （自闭合形态）时返回 null——Hugo 要求内联短代码必须有闭合标签。
    /// 模板语法错误抛 <see cref="FormatException"/>（与 <see cref="TemplateShortcode"/> 一致）
    /// </summary>
    public static InlineShortcode? TryCreate(string name, string? innerContent)
    {
        if (innerContent is null)
        {
            return null;
        }
        var inner = Cache.GetOrAdd(innerContent, body => new TemplateShortcode(name, body));
        return new InlineShortcode(name, inner);
    }

    /// <inheritdoc />
    public ValueTask<string> ProcessAsync(
        ShortcodeContext context,
        CancellationToken cancellationToken = default) =>
        _inner.ProcessAsync(context, cancellationToken);
}

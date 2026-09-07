// Flint 静态站点生成器
// 短代码处理器实现

using System.Globalization;
using System.Text.RegularExpressions;
using Flint.Core.Abstractions;

namespace Flint.Core.Content.Shortcodes;

/// <summary>
/// 短代码处理器
/// 负责解析和执行 Markdown 内容中的短代码
/// </summary>
public sealed partial class ShortcodeProcessor
{
    private static readonly CultureInfo InvariantCulture = CultureInfo.InvariantCulture;

    /// <summary>
    /// Angle 型短代码的 Markdown 管线占位符前缀/后缀（对齐 Hugo 的 HAHAHUGOSHORTCODE…HBHB 防碰撞设计）
    /// </summary>
    public const string DeferredPlaceholderPrefix = "HAHAFLINTSHORTCODE-";
    public const string DeferredPlaceholderSuffix = "-HBHB";

    private readonly IShortcodeRegistry _registry;

    /// <summary>
    /// 遇到未注册短代码时是否抛出异常（fail-fast，对齐 Hugo 语义）。
    /// 默认 true；设为 false 时保持旧行为（输出 HTML 注释占位）
    /// </summary>
    public bool ThrowOnUnregistered { get; init; } = true;

    /// <summary>
    /// 创建短代码处理器实例
    /// </summary>
    /// <param name="registry">短代码注册表</param>
    public ShortcodeProcessor(IShortcodeRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>
    /// 创建使用默认注册表的短代码处理器实例
    /// </summary>
    public ShortcodeProcessor() : this(new ShortcodeRegistry())
    {
    }

    /// <summary>
    /// 获取短代码注册表
    /// </summary>
    public IShortcodeRegistry Registry => _registry;

    /// <summary>
    /// 将 Markdown 渲染产物中的 Angle 型占位符替换回短代码输出（对齐 Hugo expandShortcodeTokens）。
    /// 占位符独占一个段落（&lt;p&gt;占位&lt;/p&gt;）时移除 p 包裹，避免块级 HTML 被包在段落里
    /// </summary>
    public static string ExpandShortcodeTokens(string html, IReadOnlyDictionary<int, string> deferred)
    {
        if (deferred.Count == 0 || string.IsNullOrEmpty(html))
        {
            return html;
        }

        // 先处理独占段落的占位符（去 <p> 包裹），再处理行内出现。
        // 索引用 TryParse + 范围校验：正文恰好含同款占位符文本或超长数字串时
        // 不应替换错误条目或抛 OverflowException 整页崩溃
        html = ParagraphWrappedToken().Replace(html, match => ExpandToken(match, deferred));

        return ShortcodeTokenPattern().Replace(html, match => ExpandToken(match, deferred));
    }

    private static string ExpandToken(System.Text.RegularExpressions.Match match, IReadOnlyDictionary<int, string> deferred)
    {
        // 只做负值防御，不做 deferred.Count 上界：占位符索引按全部短代码编号，
        // 而 deferred 仅收 Angle 条目——Percent 混用时合法 Angle 索引 >= Count，
        // 上界检查会把合法占位符拒展开（TryGetValue 已兜住缺失键，O(1) 无需上界）
        if (!int.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Integer, InvariantCulture, out var index) ||
            index < 0 ||
            !deferred.TryGetValue(index, out var result))
        {
            return match.Value;
        }
        return result;
    }

    /// <summary>
    /// 处理内容中的所有短代码（全展开，所有输出直接进入文本）
    /// </summary>
    /// <param name="content">输入内容</param>
    /// <param name="pageContext">页面上下文（可选）</param>
    /// <param name="siteContext">站点上下文（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>处理后的内容</returns>
    public async ValueTask<string> ProcessAsync(
        string content,
        object? pageContext = null,
        object? siteContext = null,
        CancellationToken cancellationToken = default)
    {
        var (text, deferred) = await ProcessForMarkdownAsync(content, pageContext, siteContext, cancellationToken)
            .ConfigureAwait(false);

        // 全展开语义：Angle 型占位符也替换回结果
        return ExpandShortcodeTokens(text, deferred);
    }

    /// <summary>
    /// 面向 Markdown 管线的短代码处理（双语义，对齐 Hugo）：
    /// <list type="bullet">
    /// <item><c>{{&lt; &gt;}}</c>（Angle）：执行后输出替换为防碰撞占位符，Markdown 渲染完成后
    /// 调用 <see cref="ExpandShortcodeTokens"/> 展开——输出不参与 Markdown 处理，
    /// 避免 figure 等 HTML 被 Markdig 重排/包裹</item>
    /// <item><c>{{% %}}</c>（Percent）：执行后输出直接内联进 Markdown 参与渲染</item>
    /// </list>
    /// </summary>
    /// <returns>处理后的文本与待展开的 Angle 型结果（占位符索引 → HTML）</returns>
    public async ValueTask<(string Text, IReadOnlyDictionary<int, string> Deferred)> ProcessForMarkdownAsync(
        string content,
        object? pageContext = null,
        object? siteContext = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(content) || !ShortcodeParser.ContainsShortcodes(content))
        {
            return (content ?? string.Empty, new Dictionary<int, string>());
        }

        var parseResult = ShortcodeParser.Parse(content);
        if (!parseResult.HasShortcodes)
        {
            return (content, new Dictionary<int, string>());
        }

        // 执行全部短代码
        var results = new Dictionary<int, string>();
        for (var i = 0; i < parseResult.Shortcodes.Count; i++)
        {
            results[i] = await ProcessShortcodeAsync(
                parseResult.Shortcodes[i], pageContext, siteContext, cancellationToken).ConfigureAwait(false);
        }

        // 按 DelimiterType 分派：Angle → 防碰撞占位符后置展开；Percent → 内联。
        // 一级占位符（Parser 产出）已用 HAHAFLINTSHORTCODE 防碰撞格式，
        // 正文恰含 {{SHORTCODE:n}} 字面量不再被误替换
        var deferred = new Dictionary<int, string>();
        var output = parseResult.ProcessedText;
        for (var i = 0; i < parseResult.Shortcodes.Count; i++)
        {
            if (parseResult.Shortcodes[i].DelimiterType == ShortcodeDelimiterType.Angle)
            {
                // 占位符保持原样，由 ExpandShortcodeTokens 按索引展开
                deferred[i] = results[i];
            }
            else
            {
                var placeholder = string.Format(
                    InvariantCulture, "{0}{1}{2}", DeferredPlaceholderPrefix, i, DeferredPlaceholderSuffix);
                output = output.Replace(placeholder, results[i], StringComparison.Ordinal);
            }
        }

        return (output, deferred);
    }

    /// <summary>
    /// 同步处理内容中的所有短代码
    /// </summary>
    /// <param name="content">输入内容</param>
    /// <param name="pageContext">页面上下文（可选）</param>
    /// <param name="siteContext">站点上下文（可选）</param>
    /// <returns>处理后的内容</returns>
    public string Process(
        string content,
        object? pageContext = null,
        object? siteContext = null)
    {
        return ProcessAsync(content, pageContext, siteContext).AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// 提取内容中的所有短代码
    /// </summary>
    /// <param name="content">输入内容</param>
    /// <returns>短代码列表</returns>
    public static IReadOnlyList<ParsedShortcode> ExtractShortcodes(string content)
    {
        return ShortcodeParser.ExtractShortcodes(content);
    }

    /// <summary>
    /// 检查内容是否包含短代码
    /// </summary>
    /// <param name="content">输入内容</param>
    /// <returns>是否包含短代码</returns>
    public static bool ContainsShortcodes(string content)
    {
        return ShortcodeParser.ContainsShortcodes(content);
    }

    /// <summary>
    /// 验证内容中的短代码是否都已注册
    /// </summary>
    /// <param name="content">输入内容</param>
    /// <returns>未注册的短代码名称列表</returns>
    public IReadOnlyList<string> ValidateShortcodes(string content)
    {
        var shortcodes = ShortcodeParser.ExtractShortcodes(content);
        var unregistered = new List<string>();

        foreach (var shortcode in shortcodes)
        {
            if (!_registry.Contains(shortcode.Name))
            {
                unregistered.Add(shortcode.Name);
            }

            // 检查嵌套短代码
            foreach (var nested in shortcode.NestedShortcodes)
            {
                if (!_registry.Contains(nested.Name))
                {
                    unregistered.Add(nested.Name);
                }
            }
        }

        return unregistered.Distinct().ToList();
    }

    /// <summary>
    /// 处理单个短代码
    /// </summary>
    private async ValueTask<string> ProcessShortcodeAsync(
        ParsedShortcode shortcode,
        object? pageContext,
        object? siteContext,
        CancellationToken cancellationToken)
    {
        // 获取处理器
        var processor = _registry.Get(shortcode.Name);

        if (processor == null)
        {
            if (ThrowOnUnregistered)
            {
                // fail-fast：对齐 Hugo"template for shortcode not found"硬错误
                throw new ShortcodeNotFoundException(shortcode.Name, shortcode.StartPosition);
            }

            // 宽容模式（ThrowOnUnregistered=false）：返回原始文本注释
            return $"<!-- 未知短代码: {shortcode.Name} -->";
        }

        // 处理嵌套短代码
        var innerContent = shortcode.InnerContent;
        if (innerContent != null && shortcode.NestedShortcodes.Count > 0)
        {
            innerContent = await ProcessNestedShortcodesAsync(
                innerContent,
                shortcode.NestedShortcodes,
                pageContext,
                siteContext,
                cancellationToken).ConfigureAwait(false);
        }

        // 创建上下文
        var context = new ShortcodeContext
        {
            Name = shortcode.Name,
            Parameters = shortcode.Parameters,
            PositionalArgs = shortcode.PositionalArgs,
            InnerContent = innerContent,
            Page = pageContext,
            Site = siteContext
        };

        try
        {
            return await processor.ProcessAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // 短代码处理错误需要捕获所有异常以返回友好错误信息
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // 处理错误，返回错误信息
            return $"<!-- 短代码 {shortcode.Name} 处理错误: {ex.Message} -->";
        }
    }

    /// <summary>
    /// 处理嵌套短代码
    /// </summary>
    private async ValueTask<string> ProcessNestedShortcodesAsync(
        string content,
        IReadOnlyList<ParsedShortcode> nestedShortcodes,
        object? pageContext,
        object? siteContext,
        CancellationToken cancellationToken)
    {
        var result = content;

        for (var i = 0; i < nestedShortcodes.Count; i++)
        {
            var nested = nestedShortcodes[i];
            var processedNested = await ProcessShortcodeAsync(nested, pageContext, siteContext, cancellationToken).ConfigureAwait(false);
            result = result.Replace(
                string.Format(InvariantCulture, "{{{{NESTED:{0}}}}}", i),
                processedNested,
                StringComparison.Ordinal);
        }

        return result;
    }

    /// <summary>
    /// 移除内容中的所有短代码（保留纯文本）
    /// </summary>
    /// <param name="content">输入内容</param>
    /// <returns>移除短代码后的内容</returns>
    public static string StripShortcodes(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return content ?? string.Empty;
        }

        // 使用正则表达式移除短代码
        // 匹配 {{< ... >}} 和 {{% ... %}} 格式
        var result = ShortcodePattern().Replace(content, string.Empty);

        return result;
    }

    /// <summary>
    /// 短代码匹配正则表达式
    /// </summary>
    [GeneratedRegex(@"\{\{[<%]\s*/?\s*\w+[^}]*[>%]\}\}", RegexOptions.Compiled)]
    private static partial Regex ShortcodePattern();

    /// <summary>
    /// 独占一个段落的 Angle 占位符（&lt;p&gt;HAHAFLINTSHORTCODE-n-HBHB&lt;/p&gt;）
    /// </summary>
    [GeneratedRegex(@"<p>\s*HAHAFLINTSHORTCODE-(\d+)-HBHB\s*</p>")]
    private static partial Regex ParagraphWrappedToken();

    /// <summary>
    /// 行内 Angle 占位符
    /// </summary>
    [GeneratedRegex("HAHAFLINTSHORTCODE-(\\d+)-HBHB")]
    private static partial Regex ShortcodeTokenPattern();
}

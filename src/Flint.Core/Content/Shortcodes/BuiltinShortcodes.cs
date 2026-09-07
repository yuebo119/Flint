// Flint 静态站点生成器
// 内置短代码处理器实现

using System.Globalization;
using System.Text;
using System.Web;
using Flint.Core.Abstractions;

namespace Flint.Core.Content.Shortcodes;

/// <summary>
/// 短代码处理器基类
/// 提供通用的参数获取方法
/// </summary>
public abstract class ShortcodeProcessorBase : IShortcodeProcessor
{
    /// <summary>
    /// 不变区域性
    /// </summary>
    protected static readonly CultureInfo InvariantCulture = CultureInfo.InvariantCulture;

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract string Description { get; }

    /// <inheritdoc />
    public abstract ValueTask<string> ProcessAsync(
        ShortcodeContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取参数值（优先命名参数，其次位置参数）
    /// </summary>
    protected static string? GetParameter(ShortcodeContext context, string name, int positionalIndex = -1)
    {
        // 首先尝试命名参数
        if (context.Parameters.TryGetValue(name, out var value))
        {
            return value;
        }

        // 然后尝试位置参数
        if (positionalIndex >= 0 && positionalIndex < context.PositionalArgs.Count)
        {
            return context.PositionalArgs[positionalIndex];
        }

        return null;
    }

    /// <summary>
    /// 获取参数值，带默认值
    /// </summary>
    protected static string GetParameter(ShortcodeContext context, string name, int positionalIndex, string defaultValue)
    {
        return GetParameter(context, name, positionalIndex) ?? defaultValue;
    }

    /// <summary>
    /// HTML 编码
    /// </summary>
    protected static string HtmlEncode(string? value)
    {
        return HttpUtility.HtmlEncode(value ?? string.Empty);
    }

    /// <summary>
    /// URL 编码
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1055:URI-like return values should not be strings", Justification = "返回编码后的字符串用于构建 URL")]
    protected static string EncodeUrl(string? value)
    {
        return HttpUtility.UrlEncode(value ?? string.Empty);
    }
}

/// <summary>
/// figure 短代码 - 图片展示
/// 用法: {{&lt; figure src="image.jpg" title="标题" caption="说明" alt="替代文本" &gt;}}
/// </summary>
public sealed class FigureShortcode : ShortcodeProcessorBase
{
    /// <inheritdoc />
    public override string Name => "figure";

    /// <inheritdoc />
    public override string Description => "图片展示短代码，支持标题、说明和链接";

    /// <inheritdoc />
    public override ValueTask<string> ProcessAsync(ShortcodeContext context, CancellationToken cancellationToken = default)
    {
        var src = GetParameter(context, "src", 0);
        var title = GetParameter(context, "title", 1);
        var caption = GetParameter(context, "caption", 2);
        var alt = GetParameter(context, "alt", 3) ?? title ?? caption ?? string.Empty;
        var link = GetParameter(context, "link", -1);
        var target = GetParameter(context, "target", -1);
        var rel = GetParameter(context, "rel", -1);
        var width = GetParameter(context, "width", -1);
        var height = GetParameter(context, "height", -1);
        var cssClass = GetParameter(context, "class", -1);
        var attrLink = GetParameter(context, "attr", -1);
        var attrLinkUrl = GetParameter(context, "attrlink", -1);

        if (string.IsNullOrEmpty(src))
        {
            return ValueTask.FromResult("<!-- figure: 缺少 src 参数 -->");
        }

        var sb = new StringBuilder();
        sb.Append("<figure");
        if (!string.IsNullOrEmpty(cssClass))
        {
            sb.Append(InvariantCulture, $" class=\"{HtmlEncode(cssClass)}\"");
        }
        sb.AppendLine(">");

        // 图片（可能带链接）
        if (!string.IsNullOrEmpty(link))
        {
            sb.Append(InvariantCulture, $"<a href=\"{HtmlEncode(link)}\"");
            if (!string.IsNullOrEmpty(target))
            {
                sb.Append(InvariantCulture, $" target=\"{HtmlEncode(target)}\"");
            }
            if (!string.IsNullOrEmpty(rel))
            {
                sb.Append(InvariantCulture, $" rel=\"{HtmlEncode(rel)}\"");
            }
            sb.Append('>');
        }

        sb.Append(InvariantCulture, $"<img src=\"{HtmlEncode(src)}\" alt=\"{HtmlEncode(alt)}\"");
        if (!string.IsNullOrEmpty(title))
        {
            sb.Append(InvariantCulture, $" title=\"{HtmlEncode(title)}\"");
        }
        if (!string.IsNullOrEmpty(width))
        {
            sb.Append(InvariantCulture, $" width=\"{HtmlEncode(width)}\"");
        }
        if (!string.IsNullOrEmpty(height))
        {
            sb.Append(InvariantCulture, $" height=\"{HtmlEncode(height)}\"");
        }
        sb.Append(" />");

        if (!string.IsNullOrEmpty(link))
        {
            sb.Append("</a>");
        }
        sb.AppendLine();

        // 图片说明
        if (!string.IsNullOrEmpty(caption) || !string.IsNullOrEmpty(title) || !string.IsNullOrEmpty(attrLink))
        {
            sb.Append("<figcaption>");
            if (!string.IsNullOrEmpty(title))
            {
                sb.Append(InvariantCulture, $"<h4>{HtmlEncode(title)}</h4>");
            }
            if (!string.IsNullOrEmpty(caption))
            {
                sb.Append(InvariantCulture, $"<p>{HtmlEncode(caption)}</p>");
            }
            if (!string.IsNullOrEmpty(attrLink))
            {
                sb.Append("<p>");
                if (!string.IsNullOrEmpty(attrLinkUrl))
                {
                    sb.Append(InvariantCulture, $"<a href=\"{HtmlEncode(attrLinkUrl)}\">{HtmlEncode(attrLink)}</a>");
                }
                else
                {
                    sb.Append(HtmlEncode(attrLink));
                }
                sb.Append("</p>");
            }
            sb.AppendLine("</figcaption>");
        }

        sb.Append("</figure>");

        return ValueTask.FromResult(sb.ToString());
    }
}

/// <summary>
/// highlight 短代码 - 代码高亮
/// 用法: {{&lt; highlight go "linenos=table,hl_lines=8 15-17" &gt;}}代码{{&lt; /highlight &gt;}}
/// </summary>
public sealed class HighlightShortcode : ShortcodeProcessorBase
{
    /// <inheritdoc />
    public override string Name => "highlight";

    /// <inheritdoc />
    public override string Description => "代码高亮短代码，支持行号和高亮行";

    /// <inheritdoc />
    public override ValueTask<string> ProcessAsync(ShortcodeContext context, CancellationToken cancellationToken = default)
    {
        var language = GetParameter(context, "lang", 0) ?? "text";
        var options = GetParameter(context, "options", 1) ?? string.Empty;
        var code = context.InnerContent ?? string.Empty;

        // 解析选项
        var showLineNumbers = options.Contains("linenos", StringComparison.OrdinalIgnoreCase);
        var highlightLines = ParseHighlightLines(options);

        var sb = new StringBuilder();

        // 使用 pre/code 结构，兼容各种语法高亮库
        sb.Append("<div class=\"highlight\">");
        sb.Append("<pre class=\"chroma\">");
        sb.Append(InvariantCulture, $"<code class=\"language-{HtmlEncode(language)}\" data-lang=\"{HtmlEncode(language)}\"");

        if (highlightLines.Count > 0)
        {
            sb.Append(InvariantCulture, $" data-hl-lines=\"{string.Join(",", highlightLines)}\"");
        }

        if (showLineNumbers)
        {
            sb.Append(" data-linenos=\"true\"");
        }

        sb.Append('>');
        sb.Append(HtmlEncode(code.Trim()));
        sb.Append("</code></pre></div>");

        return ValueTask.FromResult(sb.ToString());
    }

    /// <summary>
    /// 解析高亮行配置
    /// </summary>
    private static List<int> ParseHighlightLines(string options)
    {
        var lines = new List<int>();

        // 查找 hl_lines 参数
        var hlMatch = System.Text.RegularExpressions.Regex.Match(
            options,
            @"hl_lines\s*=\s*[""']?([^""'\s,]+)[""']?",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (!hlMatch.Success)
        {
            return lines;
        }

        var hlValue = hlMatch.Groups[1].Value;
        var parts = hlValue.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            if (part.Contains('-', StringComparison.Ordinal))
            {
                // 范围，如 15-17；无上限展开会被恶意/误写的大范围拖垮构建
                var rangeParts = part.Split('-');
                if (rangeParts.Length == 2 &&
                    int.TryParse(rangeParts[0], out var start) &&
                    int.TryParse(rangeParts[1], out var end))
                {
                    if (start > end)
                    {
                        (start, end) = (end, start);
                    }
                    end = Math.Min(end, start + 10_000);
                    for (var i = start; i <= end; i++)
                    {
                        lines.Add(i);
                    }
                }
            }
            else if (int.TryParse(part, out var line))
            {
                lines.Add(line);
            }
        }

        return lines;
    }
}

/// <summary>
/// ref 短代码 - 内部链接引用（绝对路径）
/// 用法: {{&lt; ref "posts/my-post.md" &gt;}}
/// </summary>
public sealed class RefShortcode : ShortcodeProcessorBase
{
    /// <inheritdoc />
    public override string Name => "ref";

    /// <inheritdoc />
    public override string Description => "内部链接引用短代码，返回绝对 URL";

    /// <inheritdoc />
    public override ValueTask<string> ProcessAsync(ShortcodeContext context, CancellationToken cancellationToken = default)
    {
        var path = GetParameter(context, "path", 0);

        if (string.IsNullOrEmpty(path))
        {
            return ValueTask.FromResult("<!-- ref: 缺少路径参数 -->");
        }

        // 简化实现：仅路径规范化，不做页面查找（改 slug/迁移文件会产生断链）。
        // 完整实现需要页面索引，见任务清单 T2.1 页面树
        var normalizedPath = NormalizePath(path);
        return ValueTask.FromResult($"/{normalizedPath}");
    }

    /// <summary>
    /// 规范化路径
    /// </summary>
    private static string NormalizePath(string path)
    {
        // 移除 .md 扩展名
        if (path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^3];
        }

        // 移除开头的斜杠
        path = path.TrimStart('/');

        // 确保以斜杠结尾（目录风格 URL）
        if (!path.EndsWith('/'))
        {
            path += '/';
        }

        return path;
    }
}

/// <summary>
/// relref 短代码 - 内部链接引用（相对路径）
/// 用法: {{&lt; relref "posts/my-post.md" &gt;}}
/// </summary>
public sealed class RelrefShortcode : ShortcodeProcessorBase
{
    /// <inheritdoc />
    public override string Name => "relref";

    /// <inheritdoc />
    public override string Description => "内部链接引用短代码，返回相对 URL";

    /// <inheritdoc />
    public override ValueTask<string> ProcessAsync(ShortcodeContext context, CancellationToken cancellationToken = default)
    {
        var path = GetParameter(context, "path", 0);

        if (string.IsNullOrEmpty(path))
        {
            return ValueTask.FromResult("<!-- relref: 缺少路径参数 -->");
        }

        // 规范化路径
        var normalizedPath = NormalizePath(path);

        return ValueTask.FromResult(normalizedPath);
    }

    /// <summary>
    /// 规范化路径
    /// </summary>
    private static string NormalizePath(string path)
    {
        // 移除 .md 扩展名
        if (path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^3];
        }

        // 确保以斜杠结尾
        if (!path.EndsWith('/'))
        {
            path += '/';
        }

        return path;
    }
}

/// <summary>
/// gist 短代码 - GitHub Gist 嵌入
/// 用法: {{&lt; gist user gist_id [file] &gt;}}
/// </summary>
public sealed class GistShortcode : ShortcodeProcessorBase
{
    /// <inheritdoc />
    public override string Name => "gist";

    /// <inheritdoc />
    public override string Description => "GitHub Gist 嵌入短代码";

    /// <inheritdoc />
    public override ValueTask<string> ProcessAsync(ShortcodeContext context, CancellationToken cancellationToken = default)
    {
        var user = GetParameter(context, "user", 0);
        var gistId = GetParameter(context, "id", 1);
        var file = GetParameter(context, "file", 2);

        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(gistId))
        {
            return ValueTask.FromResult("<!-- gist: 缺少 user 或 id 参数 -->");
        }

        var scriptUrl = $"https://gist.github.com/{EncodeUrl(user)}/{EncodeUrl(gistId)}.js";
        if (!string.IsNullOrEmpty(file))
        {
            scriptUrl += $"?file={EncodeUrl(file)}";
        }

        var html = $"<script src=\"{HtmlEncode(scriptUrl)}\"></script>";

        return ValueTask.FromResult(html);
    }
}

/// <summary>
/// youtube 短代码 - YouTube 视频嵌入
/// 用法: {{&lt; youtube video_id &gt;}} 或 {{&lt; youtube id="video_id" &gt;}}
/// </summary>
public sealed class YoutubeShortcode : ShortcodeProcessorBase
{
    /// <inheritdoc />
    public override string Name => "youtube";

    /// <inheritdoc />
    public override string Description => "YouTube 视频嵌入短代码";

    /// <inheritdoc />
    public override ValueTask<string> ProcessAsync(ShortcodeContext context, CancellationToken cancellationToken = default)
    {
        var videoId = GetParameter(context, "id", 0);
        var title = GetParameter(context, "title", -1) ?? "YouTube Video";
        var autoplay = GetParameter(context, "autoplay", -1);
        var start = GetParameter(context, "start", -1);

        if (string.IsNullOrEmpty(videoId))
        {
            return ValueTask.FromResult("<!-- youtube: 缺少视频 ID -->");
        }

        var embedUrl = $"https://www.youtube.com/embed/{EncodeUrl(videoId)}";
        var queryParams = new List<string>();

        if (!string.IsNullOrEmpty(autoplay) && autoplay == "1")
        {
            queryParams.Add("autoplay=1");
        }
        if (!string.IsNullOrEmpty(start))
        {
            queryParams.Add($"start={EncodeUrl(start)}");
        }

        if (queryParams.Count > 0)
        {
            embedUrl += "?" + string.Join("&", queryParams);
        }

        var html = $@"<div class=""youtube-container"" style=""position: relative; padding-bottom: 56.25%; height: 0; overflow: hidden;"">
<iframe src=""{HtmlEncode(embedUrl)}"" title=""{HtmlEncode(title)}"" style=""position: absolute; top: 0; left: 0; width: 100%; height: 100%;"" frameborder=""0"" allow=""accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture"" allowfullscreen></iframe>
</div>";

        return ValueTask.FromResult(html);
    }
}

/// <summary>
/// tweet 短代码 - Twitter 推文嵌入
/// 用法: {{&lt; tweet user="username" id="tweet_id" &gt;}}
/// </summary>
public sealed class TweetShortcode : ShortcodeProcessorBase
{
    /// <inheritdoc />
    public override string Name => "tweet";

    /// <inheritdoc />
    public override string Description => "Twitter 推文嵌入短代码";

    /// <inheritdoc />
    public override ValueTask<string> ProcessAsync(ShortcodeContext context, CancellationToken cancellationToken = default)
    {
        var user = GetParameter(context, "user", 0);
        var tweetId = GetParameter(context, "id", 1);

        if (string.IsNullOrEmpty(tweetId))
        {
            return ValueTask.FromResult("<!-- tweet: 缺少推文 ID -->");
        }

        // 使用 Twitter 的 oEmbed 格式
        var tweetUrl = string.IsNullOrEmpty(user)
            ? $"https://twitter.com/i/status/{EncodeUrl(tweetId)}"
            : $"https://twitter.com/{EncodeUrl(user)}/status/{EncodeUrl(tweetId)}";

        var html = $@"<blockquote class=""twitter-tweet"">
<a href=""{HtmlEncode(tweetUrl)}""></a>
</blockquote>
<script async src=""https://platform.twitter.com/widgets.js"" charset=""utf-8""></script>";

        return ValueTask.FromResult(html);
    }
}

/// <summary>
/// vimeo 短代码 - Vimeo 视频嵌入
/// 用法: {{&lt; vimeo video_id &gt;}}
/// </summary>
public sealed class VimeoShortcode : ShortcodeProcessorBase
{
    /// <inheritdoc />
    public override string Name => "vimeo";

    /// <inheritdoc />
    public override string Description => "Vimeo 视频嵌入短代码";

    /// <inheritdoc />
    public override ValueTask<string> ProcessAsync(ShortcodeContext context, CancellationToken cancellationToken = default)
    {
        var videoId = GetParameter(context, "id", 0);
        var title = GetParameter(context, "title", -1) ?? "Vimeo Video";

        if (string.IsNullOrEmpty(videoId))
        {
            return ValueTask.FromResult("<!-- vimeo: 缺少视频 ID -->");
        }

        var embedUrl = $"https://player.vimeo.com/video/{EncodeUrl(videoId)}";

        var html = $@"<div class=""vimeo-container"" style=""position: relative; padding-bottom: 56.25%; height: 0; overflow: hidden;"">
<iframe src=""{HtmlEncode(embedUrl)}"" title=""{HtmlEncode(title)}"" style=""position: absolute; top: 0; left: 0; width: 100%; height: 100%;"" frameborder=""0"" allow=""autoplay; fullscreen; picture-in-picture"" allowfullscreen></iframe>
</div>";

        return ValueTask.FromResult(html);
    }
}

/// <summary>
/// instagram 短代码 - Instagram 帖子嵌入
/// 用法: {{&lt; instagram post_id &gt;}}
/// </summary>
public sealed class InstagramShortcode : ShortcodeProcessorBase
{
    /// <inheritdoc />
    public override string Name => "instagram";

    /// <inheritdoc />
    public override string Description => "Instagram 帖子嵌入短代码";

    /// <inheritdoc />
    public override ValueTask<string> ProcessAsync(ShortcodeContext context, CancellationToken cancellationToken = default)
    {
        var postId = GetParameter(context, "id", 0);
        var hidecaption = GetParameter(context, "hidecaption", -1);

        if (string.IsNullOrEmpty(postId))
        {
            return ValueTask.FromResult("<!-- instagram: 缺少帖子 ID -->");
        }

        var embedUrl = $"https://www.instagram.com/p/{EncodeUrl(postId)}/embed";
        if (hidecaption == "true")
        {
            embedUrl += "?hidecaption=true";
        }

        var html = $@"<div class=""instagram-container"">
<iframe src=""{HtmlEncode(embedUrl)}"" width=""400"" height=""480"" frameborder=""0"" scrolling=""no"" allowtransparency=""true""></iframe>
</div>";

        return ValueTask.FromResult(html);
    }
}

/// <summary>
/// param 短代码 - 获取页面参数
/// 用法: {{&lt; param "paramName" &gt;}}
/// 简化实现：页面参数上下文尚未接入短代码管道（ShortcodeContext.Page 恒为 null），
/// 当前返回空串——此前输出 Go 模板语法字面量 {{.Params.x}} 会污染最终 HTML
/// </summary>
public sealed class ParamShortcode : ShortcodeProcessorBase
{
    /// <inheritdoc />
    public override string Name => "param";

    /// <inheritdoc />
    public override string Description => "获取页面参数短代码（需要页面参数上下文，当前返回空）";

    /// <inheritdoc />
    public override ValueTask<string> ProcessAsync(ShortcodeContext context, CancellationToken cancellationToken = default)
    {
        var paramName = GetParameter(context, "name", 0);

        if (string.IsNullOrEmpty(paramName))
        {
            return ValueTask.FromResult("<!-- param: 缺少参数名 -->");
        }

        // Page 上下文未接线（见任务清单 T2.2），接通后从 context.Page 读取 Params
        return ValueTask.FromResult(string.Empty);
    }
}

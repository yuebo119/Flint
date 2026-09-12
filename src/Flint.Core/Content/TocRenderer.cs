// Flint 静态站点生成器
// 目录（Table of Contents）HTML 渲染
//
// 对齐 Hugo 的产出形状（v0.166 实测，逐字节比对缩进与嵌套）：
//
//   <nav id="TableOfContents">
//     <ul>
//       <li><a href="#a">A</a>
//         <ul>
//           <li><a href="#b">B</a></li>
//         </ul>
//       </li>
//       <li><a href="#d">D</a></li>
//     </ul>
//   </nav>
//
// 主题依赖这一形状做字符串操作：Blowfish 用 `replace … 'id="TableOfContents"'`
// 改写无障碍标签、Congo 用 `in page.table_of_contents "<ul"` 判定"本页是否有目录"。
// 早前把 page.table_of_contents 暴露成标题**列表**，这两类判定全部静默失效。

using System.Text;
using Flint.Core.Abstractions;
using Flint.Core.Models;

namespace Flint.Core.Content;

/// <summary>
/// 标题列表 → 目录 HTML（Hugo <c>.TableOfContents</c> / <c>.Fragments.ToHTML</c> 形状）
/// </summary>
public static class TocRenderer
{
    /// <summary>
    /// 渲染目录 HTML：只保留 <paramref name="startLevel"/>..<paramref name="endLevel"/>
    /// 区间内的标题，按层级嵌套 <c>&lt;ul&gt;</c>（<paramref name="ordered"/> 时用 <c>&lt;ol&gt;</c>）。
    ///
    /// <paramref name="startLevel"/> 小于文档实际最小标题级别时，为每个缺失层级补一层
    /// 无锚点的 <c>&lt;li&gt;</c> 包裹（Hugo <c>.Fragments.ToHTML 1 6</c> 实测行为）。
    /// 区间内无标题时返回空串（对齐 Hugo：空串而非空 nav）
    /// </summary>
    public static string Render(
        IReadOnlyList<MarkdownHeading> headings,
        int startLevel,
        int endLevel,
        bool ordered)
    {
        if (headings.Count == 0)
        {
            return string.Empty;
        }

        var min = Math.Min(startLevel, endLevel);
        var max = Math.Max(startLevel, endLevel);
        var filtered = headings.Where(h => h.Level >= min && h.Level <= max).ToList();
        if (filtered.Count == 0)
        {
            return string.Empty;
        }

        // 缺失层级的空 <li> 包裹链：最内层的 Children 才是真实标题树
        var wrappers = filtered.Min(h => h.Level) - min;
        var roots = BuildTree(filtered);
        for (var i = 0; i < wrappers; i++)
        {
            var wrapper = new TocNode(null);
            wrapper.Children.AddRange(roots);
            roots = [wrapper];
        }

        var sb = new StringBuilder();
        sb.Append("<nav id=\"TableOfContents\">\n");
        RenderLevel(sb, roots, 2, ordered);
        sb.Append("</nav>\n");
        return sb.ToString();
    }

    /// <summary>构建层级树：更深的标题挂到最近的更浅标题下</summary>
    private static List<TocNode> BuildTree(List<MarkdownHeading> headings)
    {
        var roots = new List<TocNode>();
        var stack = new List<TocNode>();
        foreach (var heading in headings)
        {
            var node = new TocNode(heading);
            while (stack.Count > 0 && stack[^1].Heading!.Level >= heading.Level)
            {
                stack.RemoveAt(stack.Count - 1);
            }
            if (stack.Count == 0)
            {
                roots.Add(node);
            }
            else
            {
                stack[^1].Children.Add(node);
            }
            stack.Add(node);
        }
        return roots;
    }

    /// <summary>渲染一层 <c>&lt;ul&gt;</c>（<paramref name="indent"/> 为该 ul 的缩进空格数）</summary>
    private static void RenderLevel(StringBuilder sb, List<TocNode> nodes, int indent, bool ordered)
    {
        var tag = ordered ? "ol" : "ul";
        sb.Append(' ', indent).Append('<').Append(tag).Append(">\n");
        var itemIndent = indent + 2;
        foreach (var node in nodes)
        {
            sb.Append(' ', itemIndent).Append("<li>");
            if (node.Heading is { } heading)
            {
                sb.Append("<a href=\"#").Append(heading.Id).Append("\">")
                  .Append(Escape(heading.Text)).Append("</a>");
            }
            if (node.Children.Count > 0)
            {
                sb.Append('\n');
                RenderLevel(sb, node.Children, itemIndent + 2, ordered);
                sb.Append(' ', itemIndent).Append("</li>\n");
            }
            else
            {
                sb.Append("</li>\n");
            }
        }
        sb.Append(' ', indent).Append("</").Append(tag).Append(">\n");
    }

    private static string Escape(string text) => text
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);

    private sealed class TocNode(MarkdownHeading? heading)
    {
        public MarkdownHeading? Heading { get; } = heading;

        public List<TocNode> Children { get; } = [];
    }
}

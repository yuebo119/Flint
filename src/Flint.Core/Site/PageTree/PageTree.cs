// Flint 静态站点生成器
// 页面树 - 以规范化逻辑路径为 key 的页面集合（对齐 Hugo content map 设计）

using System.Globalization;

namespace Flint.Core.Site.PageTrees;

/// <summary>bundle 类型（判定规则对齐 Hugo pathparser.go）</summary>
public enum PageBundleType
{
    /// <summary>普通单页：posts/hello.md → key /posts/hello</summary>
    Single,

    /// <summary>leaf bundle：posts/gallery/index.md → key /posts/gallery（同目录资源归页面所有）</summary>
    LeafBundle,

    /// <summary>branch 节点：posts/_index.md → key /posts（section/列表页）</summary>
    Branch,

    /// <summary>合成节点（缺失 section/home 补齐，无文件来源）</summary>
    Synthesized
}

/// <summary>树节点</summary>
public sealed class PageTreeNode
{
    /// <summary>规范化树 key（小写、前导 /、无尾 /；home 为空串）</summary>
    public required string Key { get; set; }

    /// <summary>合成节点的显示标题（真实节点取自 Content.Metadata）</summary>
    public string? Title { get; set; }

    public required PageBundleType BundleType { get; set; }

    /// <summary>源内容文件路径（合成节点为空串）</summary>
    public required string SourcePath { get; set; }

    /// <summary>解析后的内容（合成 section/home 为 null）</summary>
    public Models.ParsedContent? Content { get; set; }

    /// <summary>是否为 branch 类节点（section 查询剪枝的依据）</summary>
    public bool IsBranch => BundleType is PageBundleType.Branch or PageBundleType.Synthesized;
}

/// <summary>
/// 页面树
/// 非线程安全：装配阶段单线程使用（与 Hugo 构建期写树持锁的模型一致）；
/// 多语言扩展预留：将来 value 换成按语言维度的节点容器即可，树与遍历代码不变
/// </summary>
public sealed class PageTree
{
    private readonly RadixTree<PageTreeNode> _tree = new();

    /// <summary>节点数</summary>
    public int Count => _tree.Count;

    /// <summary>插入节点；key 冲突（如同 key 的 leaf bundle 与单页）时抛异常（fail-fast）</summary>
    public void Insert(PageTreeNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        // 树内 key 恒为规范化形式（构造方给的原始路径在此归一）
        node.Key = NormalizeKey(node.Key);
        if (!_tree.Insert(node.Key, node))
        {
            return;
        }

        throw new InvalidOperationException(
            string.Create(CultureInfo.InvariantCulture, $"页面树 key 冲突: {node.Key} ({node.BundleType})——同 key 的内容页与 section/bundle 不能共存"));
    }

    public PageTreeNode? Get(string key) =>
        _tree.TryGet(NormalizeKey(key), out var node) ? node : null;

    public bool Has(string key) => _tree.TryGet(NormalizeKey(key), out _);

    /// <summary>删除单节点</summary>
    public bool Delete(string key) => _tree.Remove(NormalizeKey(key));

    /// <summary>
    /// 删除前缀下全部节点（含该 section 自身），返回删除数。增量删除的基础
    /// </summary>
    public int DeletePrefix(string prefix)
    {
        prefix = NormalizeKey(prefix);
        if (prefix.Length == 0)
        {
            // home 前缀 = 全树（防御：不允许一键清空整站）
            throw new InvalidOperationException("DeletePrefix 不允许作用于 home（空 key）");
        }

        var keys = new List<string>();
        _tree.WalkPrefix(prefix, (key, _) =>
        {
            keys.Add(key);
            return false;
        });
        var removed = 0;
        foreach (var key in keys)
        {
            if (_tree.Remove(key))
            {
                removed++;
            }
        }
        return removed;
    }

    /// <summary>
    /// section 页面查询：前缀遍历 + branch 剪枝（对齐 Hugo getPagesInSection）。
    /// 非递归模式命中子 section 节点后跳过其整棵子树（只收直属页面）
    /// </summary>
    /// <param name="sectionKey">section 键（"" 为 home 的直属页面）</param>
    /// <param name="recursive">true 收全部后代，false 只收直属</param>
    public IReadOnlyList<PageTreeNode> GetPagesInSection(string sectionKey, bool recursive = false)
    {
        var result = new List<PageTreeNode>();
        var normalized = NormalizeKey(sectionKey);
        var walker = new PageTreeWalker
        {
            Tree = this,
            Prefix = normalized,
            Handle = (key, node) =>
            {
                // home 的直属查询不含 home 自身（键 "/" 即 "/" 前缀遍历的起点值），但不可剪其子树
                if (normalized.Length == 0 && key == "/")
                {
                    return false;
                }
                result.Add(node);
                // 非递归：进入子 section（含其子树）时剪枝；section 自身不剪
                return !recursive && node.IsBranch && key != normalized;
            }
        };
        Walk(walker);
        return result;
    }

    /// <summary>
    /// 最近祖先查找（按目录段向上）。
    /// <paramref name="includeSelf"/>=true 时含 key 自身；false 时从父级开始（"页面归属 section"语义）
    /// </summary>
    public PageTreeNode? LongestPrefix(string key, bool includeSelf = true)
    {
        var current = NormalizeKey(key);
        if (!includeSelf)
        {
            var parentSlash = current.LastIndexOf('/');
            current = parentSlash <= 0 ? "" : current[..parentSlash];
        }

        while (true)
        {
            if (_tree.TryGet(current, out var node))
            {
                return node;
            }

            if (current.Length == 0)
            {
                return null;
            }

            var slash = current.LastIndexOf('/');
            current = slash <= 0 ? "" : current[..slash];
        }
    }

    /// <summary>声明式前缀遍历（walker 语义对齐 Hugo NodeShiftTreeWalker：剪枝跳过整棵子树）</summary>
    public void Walk(PageTreeWalker walker)
    {
        ArgumentNullException.ThrowIfNull(walker);
        if (walker.Tree != this)
        {
            throw new ArgumentException("walker.Tree 必须是当前树", nameof(walker));
        }

        var prefix = walker.Prefix is null ? "/" : NormalizePrefix(walker.Prefix);

        _tree.WalkPrefix(prefix, (key, node) =>
        {
            if (walker.ShouldSkip(key))
            {
                return true;
            }

            if (walker.IncludeFilter is not null && !walker.IncludeFilter(node))
            {
                return false;
            }

            return walker.Handle is not null && walker.Handle(key, node);
        });
    }

    /// <summary>内部 raw 遍历（DeletePrefix 用）</summary>
    private void WalkPrefixRaw(string prefix, Action<string, PageTreeNode> visit)
    {
        _tree.WalkPrefix(prefix, (key, node) =>
        {
            visit(key, node);
            return false;
        });
    }

    /// <summary>
    /// 规范化树 key：小写、Unix 斜杠、剥内容扩展名、前导 /、无尾 /；
    /// 空串与 "/" 都是 home（对齐 Hugo cleanTreeKey 约定）
    /// </summary>
    public static string NormalizeKey(string path)
    {
        var s = path.Replace('\\', '/').Trim().ToLowerInvariant();

        // 剥首部的 . / 空白（对齐 Hugo trimCutsetDotSlashSpace）
        s = s.TrimStart('.', '/', ' ');

        // 剥内容扩展名（仅已知内容扩展，资源扩展保留语义由调用方处理）
        foreach (var ext in new[] { ".markdown", ".mdown", ".md", ".html" })
        {
            if (s.EndsWith(ext, StringComparison.Ordinal))
            {
                s = s[..^ext.Length];
                break;
            }
        }

        s = s.TrimEnd('/');

        if (s.Length == 0)
        {
            return ""; // home
        }

        return s[0] == '/' ? s : "/" + s;
    }

    /// <summary>前缀遍历的 prefix 规范化：强制尾斜杠，防 /blog 误匹配 /blog2</summary>
    internal static string NormalizePrefix(string prefix)
    {
        var s = NormalizeKey(prefix);
        return s.Length == 0 ? "/" : s + "/";
    }

    /// <summary>
    /// 由内容文件相对路径推导 (key, bundleType)：
    /// 文件名 index → leaf bundle（key=父目录）；_index → branch（key=父目录，站点根 → home=""）；其他 → Single
    /// </summary>
    public static (string Key, PageBundleType Type) KeyForContentFile(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').ToLowerInvariant();
        var fileName = Path.GetFileName(normalized);
        var directory = Path.GetDirectoryName(normalized)?.Replace('\\', '/') ?? "";

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var isMarkdownish = fileName.EndsWith(".md", StringComparison.Ordinal)
                            || fileName.EndsWith(".markdown", StringComparison.Ordinal)
                            || fileName.EndsWith(".html", StringComparison.Ordinal);

        if (!isMarkdownish)
        {
            // 非内容文件不当页面（调用方过滤后才会进来）——按单页处理保持防御
            return (CombineKey(directory, baseName), PageBundleType.Single);
        }

        if (baseName == "index")
        {
            // leaf bundle：key = 父目录；content/index.md → home
            return (NormalizeKey(directory), PageBundleType.LeafBundle);
        }

        if (baseName == "_index")
        {
            // branch：posts/_index.md → /posts；content/_index.md → home（""）
            return (NormalizeKey(directory), PageBundleType.Branch);
        }

        return (CombineKey(directory, baseName), PageBundleType.Single);
    }

    private static string CombineKey(string directory, string baseName)
    {
        var dir = NormalizeKey(directory);
        return dir.Length == 0 ? "/" + baseName : dir + "/" + baseName;
    }
}

/// <summary>
/// 声明式遍历配置（语义对齐 Hugo NodeShiftTreeWalker，去掉语言维度）
/// </summary>
public sealed class PageTreeWalker
{
    public required PageTree Tree { get; init; }

    /// <summary>遍历起点前缀（自动规范化为带尾斜杠形式）</summary>
    public string? Prefix { get; init; }

    /// <summary>节点过滤（作用于每个被访问的节点）</summary>
    public Func<PageTreeNode, bool>? IncludeFilter { get; init; }

    /// <summary>
    /// 访问节点。返回 true = 跳过该节点的整棵子树；
    /// 也可以在回调内调用 <see cref="SkipPrefix"/> 标记其他要跳过的前缀
    /// </summary>
    public Func<string, PageTreeNode, bool>? Handle { get; init; }

    private readonly List<string> _skipPrefixes = new();

    /// <summary>标记跳过指定前缀的整棵子树（对齐 Hugo walker.SkipPrefix）</summary>
    public void SkipPrefix(string prefix)
    {
        _skipPrefixes.Add(PageTree.NormalizePrefix(prefix));
    }

    internal bool ShouldSkip(string key)
    {
        foreach (var skip in _skipPrefixes)
        {
            if (key.StartsWith(skip, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }
}

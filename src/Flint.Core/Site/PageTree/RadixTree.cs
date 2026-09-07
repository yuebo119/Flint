// Flint 静态站点生成器
// 紧凑基数树（radix tree）——页面树的底层存储

namespace Flint.Core.Site.PageTrees;

/// <summary>
/// 紧凑基数树
/// 插入/查询 O(k)（k=key 长度），前缀遍历按字典序，支持遍历中整子树剪枝。
/// 语义参照 Hugo 依赖的 go-radix：节点值先于子节点访问；剪枝跳过该节点的整棵子树。
/// 非线程安全：装配阶段单线程使用（与 Hugo 构建期写树加写锁的模型一致，锁由调用方掌握）。
/// </summary>
public sealed class RadixTree<T>
{
    private readonly Node _root = new() { Prefix = "" };
    private int _count;

    /// <summary>树中带值的节点数</summary>
    public int Count => _count;

    private sealed class Node
    {
        /// <summary>该边的前缀段（相对父节点，非全路径）</summary>
        public string Prefix = "";

        public T? Value;
        public bool HasValue;
        public List<Node>? Children;

        public bool IsLeaf => Children is null || Children.Count == 0;
    }

    /// <summary>
    /// 插入或覆盖。返回 true 表示覆盖了已有值。
    /// 空串与 "/" 等价（home 约定，对齐 Hugo cleanTreeKey）
    /// </summary>
    public bool Insert(string key, T value)
    {
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }
        if (key.Length == 0)
        {
            key = "/";
        }
        else if (key[0] != '/')
        {
            key = "/" + key;
        }

        var node = _root;
        var remaining = key.AsSpan();

        while (remaining.Length > 0)
        {
            var next = FindChild(node, remaining[0]);
            if (next is null)
            {
                // 无匹配子边：新建叶子节点
                var child = new Node { Prefix = remaining.ToString(), HasValue = true, Value = value };
                AddChild(node, child);
                _count++;
                return false;
            }

            // 计算与子边前缀的公共部分
            var common = CommonPrefixLength(remaining, next.Prefix);

            if (common == next.Prefix.Length)
            {
                // 完全消耗子边前缀：下探
                node = next;
                remaining = remaining[common..];
                continue;
            }

            if (common == remaining.Length)
            {
                // key 是子边前缀的前缀：分裂子节点，key 落在新内部节点上
                var split = new Node
                {
                    Prefix = next.Prefix[..common],
                    HasValue = true,
                    Value = value,
                    Children = new List<Node> { new() { Prefix = next.Prefix[common..], HasValue = next.HasValue, Value = next.Value, Children = next.Children } }
                };
                next.Prefix = next.Prefix[common..];
                ReplaceChild(node, next, split);
                _count++;
                return false;
            }

            // 部分公共：分叉——公共部分成为新内部节点，两侧各自成子
            var branch = new Node
            {
                Prefix = next.Prefix[..common],
                // Children 必须保持按边首字符有序（FindChild 依赖该不变量提前返回）
                Children = SortTwoEdges(
                    new Node { Prefix = next.Prefix[common..], HasValue = next.HasValue, Value = next.Value, Children = next.Children },
                    new Node { Prefix = remaining[common..].ToString(), HasValue = true, Value = value })
            };
            ReplaceChild(node, next, branch);
            _count++;
            return false;
        }

        // 恰好走到已有节点
        if (node.HasValue)
        {
            node.Value = value;
            return true;
        }

        node.HasValue = true;
        node.Value = value;
        _count++;
        return false;
    }

    /// <summary>两个子节点按边首字符排序（保持 Children 有序不变量）</summary>
    private static List<Node> SortTwoEdges(Node a, Node b)
    {
        return a.Prefix[0] <= b.Prefix[0] ? new List<Node> { a, b } : new List<Node> { b, a };
    }

    /// <summary>精确查询</summary>
    public bool TryGet(string key, out T value)
    {
        value = default!;
        if (key.Length == 0 || key[0] != '/')
        {
            key = "/" + key;
        }

        var node = _root;
        var remaining = key.AsSpan();

        while (remaining.Length > 0)
        {
            var next = FindChild(node, remaining[0]);
            if (next is null)
            {
                return false;
            }

            if (remaining.Length < next.Prefix.Length ||
                !remaining.StartsWith(next.Prefix, StringComparison.Ordinal))
            {
                return false;
            }

            node = next;
            remaining = remaining[next.Prefix.Length..];
        }

        if (!node.HasValue)
        {
            return false;
        }

        value = node.Value!;
        return true;
    }

    /// <summary>删除单节点值；返回是否删除。叶子空节点会被收缩</summary>
    public bool Remove(string key)
    {
        if (key.Length == 0 || key[0] != '/')
        {
            key = "/" + key;
        }

        var node = _root;
        var parent = default(Node);
        var remaining = key.AsSpan();

        while (remaining.Length > 0)
        {
            var next = FindChild(node, remaining[0]);
            if (next is null ||
                remaining.Length < next.Prefix.Length ||
                !remaining.StartsWith(next.Prefix, StringComparison.Ordinal))
            {
                return false;
            }

            parent = node;
            node = next;
            remaining = remaining[next.Prefix.Length..];
        }

        if (!node.HasValue)
        {
            return false;
        }

        node.HasValue = false;
        node.Value = default;
        _count--;
        Collapse(parent, node);
        return true;
    }

    /// <summary>
    /// 前缀遍历（字典序）。handler 返回 true 表示跳过该节点的整棵子树
    /// </summary>
    /// <param name="prefix">必须以 '/' 开头；遍历命中此前缀开头的全部键</param>
    public void WalkPrefix(string prefix, Func<string, T, bool> handler)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);

        // 下探到 prefix 命中位置（可能落在节点边中间）
        var node = _root;
        var remaining = prefix.AsSpan();
        var walkedKey = new System.Text.StringBuilder();

        while (remaining.Length > 0)
        {
            // prefix 以尾斜杠命中节点边界（如 "/posts/" 下探消耗 "/posts" 后剩 "/"）：
            // 此时遍历该节点的子级中完整键以 prefix 开头的部分——
            // node 自身键（= prefix 去尾斜杠）不以 prefix 开头，排除；
            // 子树中完整键分叉于 prefix 的（如 /sectioned 之于 /section/）整树排除
            if (remaining.Length == 1 && remaining[0] == '/')
            {
                if (node.Children is not null)
                {
                    var baseKey = walkedKey.ToString();
                    foreach (var child in node.Children)
                    {
                        var fullKey = baseKey + child.Prefix;
                        if (fullKey.StartsWith(prefix, StringComparison.Ordinal))
                        {
                            WalkDfs(child, fullKey, handler);
                        }
                    }
                }
                return;
            }

            var next = FindChild(node, remaining[0]);
            if (next is null)
            {
                return; // 无命中子树
            }

            if (remaining.Length < next.Prefix.Length)
            {
                if (!next.Prefix.AsSpan().StartsWith(remaining, StringComparison.Ordinal))
                {
                    return; // 边中途失配
                }

                // prefix 落在边中间：该子树全部键都以 prefix 开头，直接从 next 的子树遍历
                WalkDfs(next, walkedKey.ToString() + next.Prefix, handler);
                return;
            }

            node = next;
            walkedKey.Append(next.Prefix);
            remaining = remaining[next.Prefix.Length..];
        }

        // prefix 恰好命中节点：从该节点开始遍历（该节点值若存在也访问）
        WalkDfs(node, walkedKey.ToString(), handler);
    }

    private void WalkDfs(Node node, string key, Func<string, T, bool> handler)
    {
        if (node.HasValue)
        {
            if (handler(key, node.Value!))
            {
                return; // 剪枝：跳过整棵子树
            }
        }

        if (node.Children is not null)
        {
            foreach (var child in node.Children)
            {
                WalkDfs(child, key + child.Prefix, handler);
            }
        }
    }

    private static int CommonPrefixLength(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        var len = Math.Min(a.Length, b.Length);
        var i = 0;
        while (i < len && a[i] == b[i])
        {
            i++;
        }
        return i;
    }

    private static Node? FindChild(Node node, char first)
    {
        if (node.Children is null)
        {
            return null;
        }

        // Children 按边首字符有序：线性扫描足够（分支因子小）
        foreach (var child in node.Children)
        {
            if (child.Prefix[0] == first)
            {
                return child;
            }
            if (child.Prefix[0] > first)
            {
                return null; // 已越过
            }
        }
        return null;
    }

    private static void AddChild(Node parent, Node child)
    {
        parent.Children ??= new List<Node>();
        var index = parent.Children.FindIndex(c => c.Prefix[0] > child.Prefix[0]);
        if (index < 0)
        {
            parent.Children.Add(child);
        }
        else
        {
            parent.Children.Insert(index, child);
        }
    }

    private static void ReplaceChild(Node parent, Node oldChild, Node newChild)
    {
        if (parent.Children is null)
        {
            parent.Children = new List<Node> { newChild };
            return;
        }

        var index = parent.Children.IndexOf(oldChild);
        parent.Children[index] = newChild;
    }

    /// <summary>
    /// 删除后收缩：无值的叶子节点从父级移除；父级若因此只剩单子且自身无值则合并边（紧凑化）
    /// </summary>
    private static void Collapse(Node? parent, Node node)
    {
        if (parent is null || !node.IsLeaf)
        {
            return;
        }

        parent.Children?.Remove(node);

        // 父节点无值且只剩一个子 → 合并边
        if (!parent.HasValue &&
            parent.Children is { Count: 1 } &&
            parent.Prefix.Length > 0)
        {
            var only = parent.Children[0];
            parent.Prefix += only.Prefix;
            parent.HasValue = only.HasValue;
            parent.Value = only.Value;
            parent.Children = only.Children;
        }
    }
}

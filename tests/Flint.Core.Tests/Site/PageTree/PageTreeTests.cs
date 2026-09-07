// Flint 静态站点生成器
// 页面树测试

using Flint.Core.Site.PageTrees;
using FluentAssertions;
using Xunit;
using PageTree = Flint.Core.Site.PageTrees.PageTree;

namespace Flint.Core.Tests.Site.PageTrees;

public sealed class RadixTreeTests
{
    private static RadixTree<string> NewTree() => new();

    [Fact]
    public void Insert_Get_RoundTrip()
    {
        var tree = NewTree();
        tree.Insert("/a/b", "ab");
        tree.Insert("/a/c", "ac");
        tree.Insert("/a", "a");
        tree.Insert("/b", "b");

        tree.TryGet("/a/b", out var v1).Should().BeTrue();
        v1.Should().Be("ab");
        tree.TryGet("/a", out var v2).Should().BeTrue();
        v2.Should().Be("a");
        tree.TryGet("/b", out var v3).Should().BeTrue();
        v3.Should().Be("b");
        tree.Count.Should().Be(4);
    }

    [Fact]
    public void Insert_OverwriteExistingKey_ReportsTrue()
    {
        var tree = NewTree();
        tree.Insert("/x", "old").Should().BeFalse();
        tree.Insert("/x", "new").Should().BeTrue("覆盖已有键应报告 true");
        tree.TryGet("/x", out var v).Should().BeTrue();
        v.Should().Be("new");
        tree.Count.Should().Be(1, "覆盖不增加计数");
    }

    [Fact]
    public void Insert_MissingKey_ReturnsFalse()
    {
        var tree = NewTree();
        tree.TryGet("/missing", out _).Should().BeFalse();
        tree.TryGet("/a/deeper/missing", out _).Should().BeFalse();
    }

    [Fact]
    public void Insert_PartialEdgeSplit_PreservesSiblings()
    {
        // 经典分裂场景：先插 /team，再插 /technical → /tech 成为分叉内部节点
        var tree = NewTree();
        tree.Insert("/team", "team");
        tree.Insert("/technical", "technical");

        tree.TryGet("/team", out var v1).Should().BeTrue();
        v1.Should().Be("team");
        tree.TryGet("/technical", out var v2).Should().BeTrue();
        v2.Should().Be("technical");
        tree.Count.Should().Be(2);

        // 反向顺序再验
        var tree2 = NewTree();
        tree2.Insert("/technical", "technical");
        tree2.Insert("/team", "team");
        tree2.TryGet("/team", out var v3).Should().BeTrue();
        v3.Should().Be("team");
    }

    [Fact]
    public void WalkPrefix_DictionaryOrder()
    {
        var tree = NewTree();
        tree.Insert("/blog/c", "c");
        tree.Insert("/blog/a", "a");
        tree.Insert("/blog/b", "b");
        tree.Insert("/other", "o");

        var visited = new List<string>();
        tree.WalkPrefix("/blog/", (_, v) =>
        {
            visited.Add(v);
            return false;
        });

        visited.Should().Equal("a", "b", "c");
    }

    [Fact]
    public void WalkPrefix_HandlerReturnTrue_SkipsSubtree()
    {
        var tree = NewTree();
        tree.Insert("/s/a", "a-branch"); // 装配补齐后 section 是有值节点
        tree.Insert("/s/a/1", "deep1");
        tree.Insert("/s/a/2", "deep2");
        tree.Insert("/s/b", "b");

        var visited = new List<string>();
        tree.WalkPrefix("/s/", (key, v) =>
        {
            visited.Add(v);
            return key == "/s/a"; // 剪掉 /s/a 整棵子树
        });

        visited.Should().Equal("a-branch", "b"); // 先访问 /s/a 自身，返回 true 跳过其全部后代
    }

    [Fact]
    public void WalkPrefix_PrefixInsideEdge_MatchesWholeSubtree()
    {
        // prefix 落在节点边中间：/section 前缀应命中 /section/sub/...（边为 /section/sub 的情况）
        var tree = NewTree();
        tree.Insert("/section/sub/page", "p");
        tree.Insert("/sectioned", "e");

        var visited = new List<string>();
        tree.WalkPrefix("/section/", (_, v) =>
        {
            visited.Add(v);
            return false;
        });

        visited.Should().Equal("p"); // 带尾斜杠前缀不命中 /sectioned
    }

    [Fact]
    public void Remove_LeafCollapses()
    {
        var tree = NewTree();
        tree.Insert("/a/b/c", "abc");
        tree.Insert("/a/b", "ab");

        tree.Remove("/a/b/c").Should().BeTrue();
        tree.TryGet("/a/b", out _).Should().BeTrue();
        tree.Count.Should().Be(1);

        tree.Remove("/a/b").Should().BeTrue();
        tree.Count.Should().Be(0);
        tree.TryGet("/a/b", out _).Should().BeFalse();
    }

    [Fact]
    public void Remove_MissingKey_ReturnsFalse()
    {
        var tree = NewTree();
        tree.Insert("/a", "a");
        tree.Remove("/missing").Should().BeFalse();
        tree.Remove("/a/b").Should().BeFalse("内部节点无值不可删");
    }
}

public sealed class PageTreeTests
{
    private static PageTreeNode Node(string key, PageBundleType type = PageBundleType.Single) => new()
    {
        Key = key,
        BundleType = type,
        SourcePath = key.Length == 0 ? "" : $"content{key}.md"
    };

    [Theory]
    [InlineData(@"posts\hello.md", "/posts/hello")]
    [InlineData("POSTS/HELLO.md", "/posts/hello")]
    [InlineData("posts/hello.md", "/posts/hello")]
    [InlineData("posts/gallery/index.md", "/posts/gallery")]
    [InlineData("posts/gallery/index.markdown", "/posts/gallery")]
    [InlineData("index.md", "")]
    [InlineData("_index.md", "")]
    [InlineData("posts/_index.md", "/posts")]
    public void KeyForContentFile_NormalizesKeys(string input, string expected)
    {
        var (key, _) = PageTree.KeyForContentFile(input);
        key.Should().Be(expected);
    }

    [Theory]
    [InlineData("posts/hello.md", PageBundleType.Single)]
    [InlineData("posts/gallery/index.md", PageBundleType.LeafBundle)]
    [InlineData("posts/_index.md", PageBundleType.Branch)]
    [InlineData("index.md", PageBundleType.LeafBundle)]
    [InlineData("_index.md", PageBundleType.Branch)]
    public void KeyForContentFile_DetectsBundleType(string input, PageBundleType expected)
    {
        var (_, type) = PageTree.KeyForContentFile(input);
        ((PageBundleType)type).Should().Be(expected);
    }

    [Fact]
    public void Get_NormalizesKey()
    {
        var tree = new PageTree();
        tree.Insert(Node("/posts/hello"));

        tree.Get("POSTS/HELLO.md").Should().NotBeNull("查询键经同样的规范化");
        tree.Get("/posts/hello").Should().NotBeNull();
        tree.Get("posts/hello").Should().NotBeNull();
    }

    [Fact]
    public void Insert_DuplicateKey_Throws()
    {
        var tree = new PageTree();
        tree.Insert(Node("/posts/hello"));
        var act = () => tree.Insert(Node("/posts/hello", PageBundleType.Branch));
        act.Should().Throw<InvalidOperationException>().WithMessage("*冲突*");
    }

    [Fact]
    public void GetPagesInSection_NonRecursive_PrunesNestedBranches()
    {
        var tree = new PageTree();
        tree.Insert(Node("/posts/a", PageBundleType.Single));
        tree.Insert(Node("/posts/b", PageBundleType.Single));
        tree.Insert(Node("/posts/sub", PageBundleType.Branch)); // _index 归一化为父目录
        tree.Insert(Node("/posts/sub/c", PageBundleType.Single));
        tree.Insert(Node("/about", PageBundleType.Single));

        var section = GetDirectChildren(tree, "/posts");
        section.Should().Contain(new[] { "/posts/a", "/posts/b", "/posts/sub" });
        section.Should().NotContain("/posts/sub/c", "子 section 的页面不应出现在直属列表");
        section.Should().NotContain("/about");
    }

    [Fact]
    public void GetPagesInSection_Recursive_IncludesNested()
    {
        var tree = new PageTree();
        tree.Insert(Node("/posts/a"));
        tree.Insert(Node("/posts/sub", PageBundleType.Branch)); // _index 归一化为父目录
        tree.Insert(Node("/posts/sub/c"));

        var section = GetAllDescendants(tree, "/posts");
        section.Should().Contain(new[] { "/posts/a", "/posts/sub", "/posts/sub/c" });
    }

    [Fact]
    public void LongestPrefix_FindsSectionAncestor()
    {
        var tree = new PageTree();
        tree.Insert(Node("/posts", PageBundleType.Branch)); // _index 归一化为父目录
        tree.Insert(Node("/posts/2024/deep"));

        tree.LongestPrefix("/posts/2024/deep", includeSelf: false)!.Key.Should().Be("/posts", "向上找到最近的 section 祖先");
        tree.LongestPrefix("/nonexistent/x").Should().BeNull();
    }

    [Fact]
    public void DeletePrefix_RemovesSectionSubtree()
    {
        var tree = new PageTree();
        tree.Insert(Node("/posts/a"));
        tree.Insert(Node("/posts/sub/c"));
        tree.Insert(Node("/posts/sub", PageBundleType.Branch)); // _index 归一化为父目录
        tree.Insert(Node("/about"));

        var removed = tree.DeletePrefix("/posts");
        removed.Should().Be(3);
        tree.Has("/posts/a").Should().BeFalse();
        tree.Has("/posts/sub/c").Should().BeFalse();
        tree.Has("/posts/sub").Should().BeFalse();
        tree.Has("/about").Should().BeTrue();
    }

    [Fact]
    public void GetPagesInSection_NonRecursive_PublicApi_PrunesNestedBranches()
    {
        var tree = new PageTree();
        tree.Insert(Node("/posts/a"));
        tree.Insert(Node("/posts/b"));
        tree.Insert(Node("/posts/sub", PageBundleType.Branch));
        tree.Insert(Node("/posts/sub/c"));

        var direct = tree.GetPagesInSection("/posts", recursive: false);
        direct.Select(n => n.Key).Should().Equal("/posts/a", "/posts/b", "/posts/sub");
    }

    [Fact]
    public void GetPagesInSection_Recursive_PublicApi_IncludesAllDescendants()
    {
        var tree = new PageTree();
        tree.Insert(Node("/posts/a"));
        tree.Insert(Node("/posts/sub", PageBundleType.Branch));
        tree.Insert(Node("/posts/sub/c"));

        var all = tree.GetPagesInSection("/posts", recursive: true);
        all.Select(n => n.Key).Should().Equal("/posts/a", "/posts/sub", "/posts/sub/c");
    }

    [Fact]
    public void GetPagesInSection_Home_ReturnsTopLevelPages()
    {
        var tree = new PageTree();
        tree.Insert(Node("/posts", PageBundleType.Branch));
        tree.Insert(Node("/about"));
        tree.Insert(new PageTreeNode { Key = "", BundleType = PageBundleType.Synthesized, SourcePath = "" });

        var topLevel = tree.GetPagesInSection("", recursive: false);
        topLevel.Select(n => n.Key).Should().Equal("/about", "/posts"); // home 的直属页面不含 home 自身
    }

    [Fact]
    public void DeletePrefix_OnHome_Throws()
    {
        var tree = new PageTree();
        var act = () => tree.DeletePrefix("/");
        act.Should().Throw<InvalidOperationException>("防御：不允许一键清空整站");
    }

    [Fact]
    public void Walk_TailSlashPreventsSiblingCollision()
    {
        var tree = new PageTree();
        tree.Insert(Node("/blog"));
        tree.Insert(Node("/blog2"));
        tree.Insert(Node("/blog/a"));

        var visited = new List<string>();
        tree.Walk(new PageTreeWalker
        {
            Tree = tree,
            Prefix = "/blog",
            Handle = (_, node) =>
            {
                visited.Add(node.Key);
                return false;
            }
        });

        visited.Should().Contain("/blog/a");
        visited.Should().NotContain("/blog2", "尾斜杠规范化防止 /blog 前缀误匹配 /blog2");
    }

    /// <summary>section 直属子节点（对齐 Hugo getPagesInSection 非递归：branch 剪枝）</summary>
    private static List<string> GetDirectChildren(PageTree tree, string sectionKey)
    {
        var result = new List<string>();
        var walker = new PageTreeWalker
        {
            Tree = tree,
            Prefix = sectionKey,
            Handle = (key, node) =>
            {
                result.Add(key);
                return node.IsBranch && key != PageTree.NormalizeKey(sectionKey);
            }
        };
        tree.Walk(walker);
        return result;
    }

    /// <summary>递归收集全部后代</summary>
    private static List<string> GetAllDescendants(PageTree tree, string sectionKey)
    {
        var result = new List<string>();
        tree.Walk(new PageTreeWalker
        {
            Tree = tree,
            Prefix = sectionKey,
            Handle = (_, node) =>
            {
                result.Add(node.Key);
                return false;
            }
        });
        return result;
    }
}

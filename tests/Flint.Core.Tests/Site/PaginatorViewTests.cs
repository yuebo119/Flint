// Flint 静态站点生成器
// PaginatorView 分页视图单元测试（C1 分页多页产出）

using Flint.Core.Abstractions;
using Xunit;

namespace Flint.Core.Tests.Site;

/// <summary>
/// PaginatorView 单元测试：URL 生成、切片、pager 导航、边界钳制。
/// 对齐 Hugo Pager 语义（第 1 页 URL = 列表页基准，第 N 页 = {基准}{path}/{N}/）
/// </summary>
public class PaginatorViewTests
{
    private static List<PageContext> Pages(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new PageContext
            {
                Title = $"P{i}",
                Content = "",
                Permalink = $"https://example.org/p/{i}/",
                RelPermalink = $"/p/{i}/",
                Date = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(-i),
                Tags = [],
                Categories = [],
                WordCount = 0,
                ReadingTime = TimeSpan.Zero,
                Type = "page"
            })
            .ToList();

    [Fact]
    public void Create_第一页URL等于列表页基准()
    {
        var view = PaginatorView.Create(Pages(8), 1, 3, "/posts/", "page");

        Assert.Equal("/posts/", view.URL);
        Assert.Equal(3, view.Pages.Count);
        Assert.Equal("P1", view.Pages[0].Title);
        Assert.Equal(3, view.TotalPages);
        Assert.Equal(8, view.TotalItems);
    }

    [Fact]
    public void Create_第N页URL为基准加paginatePath加页码()
    {
        var view = PaginatorView.Create(Pages(8), 2, 3, "/posts/", "page");

        Assert.Equal("/posts/page/2/", view.URL);
        Assert.Equal("P4", view.Pages[0].Title);
        Assert.Equal(3, view.NumberOfElements);
        Assert.True(view.HasPrev);
        Assert.True(view.HasNext);
        Assert.False(view.IsFirst);
        Assert.False(view.IsLast);
    }

    [Fact]
    public void Create_末页切片不满且无下一页()
    {
        var view = PaginatorView.Create(Pages(8), 3, 3, "/posts/", "page");

        Assert.Equal(2, view.Pages.Count);
        Assert.Equal("P7", view.Pages[0].Title);
        Assert.False(view.HasNext);
        Assert.True(view.IsLast);
    }

    [Fact]
    public void Create_页码超界钳制到总页数()
    {
        var view = PaginatorView.Create(Pages(8), 99, 3, "/posts/", "page");

        Assert.Equal(3, view.PageNumber);
        Assert.Equal("/posts/page/3/", view.URL);
    }

    [Fact]
    public void Create_空集合恒一页且切片为空()
    {
        var view = PaginatorView.Create([], 1, 3, "/posts/", "page");

        Assert.Equal(1, view.TotalPages);
        Assert.Empty(view.Pages);
        Assert.Equal(0, view.TotalItems);
        Assert.False(view.HasPrev);
        Assert.False(view.HasNext);
    }

    [Fact]
    public void Create_异常页大小视为1不除零()
    {
        var view = PaginatorView.Create(Pages(3), 1, 0, "/posts/", "page");

        Assert.Equal(1, view.PageSize);
        Assert.Equal(3, view.TotalPages);
        Assert.Single(view.Pages);
    }

    [Fact]
    public void Create_基准URL缺前导与尾斜杠时归一化()
    {
        var view = PaginatorView.Create(Pages(4), 2, 2, "posts", "page");

        Assert.Equal("/posts/page/2/", view.URL);
        Assert.Equal("/posts/", view.First.URL);
    }

    [Fact]
    public void 导航访问器_首末前后均指向正确页()
    {
        var view = PaginatorView.Create(Pages(10), 2, 3, "/posts/", "page");

        Assert.Equal(1, view.First.PageNumber);
        Assert.Equal("/posts/", view.First.URL);
        Assert.Equal(4, view.Last.PageNumber);
        Assert.Equal("/posts/page/4/", view.Last.URL);
        Assert.Equal(1, view.Prev!.PageNumber);
        Assert.Equal(3, view.Next!.PageNumber);
    }

    [Fact]
    public void 导航访问器_首页无上一页末页无下一页()
    {
        var first = PaginatorView.Create(Pages(10), 1, 3, "/posts/", "page");
        Assert.Null(first.Prev);

        var last = PaginatorView.Create(Pages(10), 4, 3, "/posts/", "page");
        Assert.Null(last.Next);
    }

    [Fact]
    public void Pagers_枚举全部分页且URL递增()
    {
        var view = PaginatorView.Create(Pages(10), 1, 3, "/posts/", "page");

        Assert.Equal(4, view.Pagers.Count);
        Assert.Equal("/posts/", view.Pagers[0].URL);
        Assert.Equal("/posts/page/2/", view.Pagers[1].URL);
        Assert.Equal("/posts/page/4/", view.Pagers[3].URL);
    }

    [Fact]
    public void 切片完整性_各页并集等于原集合()
    {
        var pages = Pages(10);
        var view = PaginatorView.Create(pages, 1, 3, "/posts/", "page");

        var collected = new List<PageContext>();
        for (var i = 1; i <= view.TotalPages; i++)
        {
            collected.AddRange(view.ForPage(i).Pages);
        }

        Assert.Equal(pages.Count, collected.Count);
        Assert.Equal(
            pages.Select(p => p.Title).OrderBy(t => t),
            collected.Select(p => p.Title).OrderBy(t => t));
    }
}

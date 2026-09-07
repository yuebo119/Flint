// Flint 静态站点生成器
// fast render mode 测试（T4.4）——最近访问 URL 队列 + 模板变化时只重渲染访问中的页面

using Flint.Core.Server;
using Xunit;

namespace Flint.Core.Tests.Server;

public class RecentUrlQueueTests
{
    [Fact]
    public void Record_FewerThanCapacity_AllKept()
    {
        var queue = new RecentUrlQueue(20);
        queue.Record("/a/");
        queue.Record("/b/");

        var snapshot = queue.Snapshot();

        Assert.Equal(2, snapshot.Count);
        Assert.Contains("/a/", snapshot);
        Assert.Contains("/b/", snapshot);
    }

    [Fact]
    public void Record_OverCapacity_EvictsOldest()
    {
        var queue = new RecentUrlQueue(3);
        queue.Record("/1/");
        queue.Record("/2/");
        queue.Record("/3/");
        queue.Record("/4/");

        var snapshot = queue.Snapshot();

        Assert.DoesNotContain("/1/", snapshot); // 最旧被挤出
        Assert.Contains("/2/", snapshot);
        Assert.Contains("/4/", snapshot);
    }

    [Fact]
    public void Record_Duplicates_CountOnce()
    {
        var queue = new RecentUrlQueue(20);
        queue.Record("/a/");
        queue.Record("/a/");

        Assert.Single(queue.Snapshot());
    }

    [Fact]
    public void Record_EmptyUrl_Ignored()
    {
        var queue = new RecentUrlQueue(5);
        queue.Record("");

        Assert.Empty(queue.Snapshot());
    }

    [Fact]
    public void Snapshot_EmptyQueue_ReturnsEmpty()
    {
        Assert.Empty(new RecentUrlQueue().Snapshot());
    }
}

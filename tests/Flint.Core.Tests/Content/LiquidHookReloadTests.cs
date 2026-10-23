// Flint 静态站点生成器
// Liquid 渲染钩子热更新契约测试
//
// 回归防护：钩子模板此前在加载时一次性读入并闭包捕获，serve 长驻进程里
// 改 `.liquid` 钩子永不生效；钩子文件的读取必须按 (mtime, length) 窗口验效。
// 另一个回归点：空 `.liquid` 钩子曾直接抛异常（Scriban 空模板返回空串），
// 两引擎对同一主题行为不一致。

using Flint.Core.Content;
using Xunit;

namespace Flint.Core.Tests.Content;

public sealed class LiquidHookReloadTests : IDisposable
{
    private readonly string _dir;

    public LiquidHookReloadTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"Flint_hook_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }

        GC.SuppressFinalize(this);
    }

    private string WriteHook(string content)
    {
        var path = Path.Combine(_dir, "render-link.liquid");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void 首次读取返回文件内容()
    {
        var path = WriteHook("<a href=\"{{ .Destination }}\">x</a>");
        var hook = new RenderHooks.HookTemplateFile(path);

        Assert.Contains("Destination", hook.GetContent(nowMs: 1_000));
    }

    [Fact]
    public void 窗口内不重复读盘()
    {
        var path = WriteHook("A");
        var hook = new RenderHooks.HookTemplateFile(path);
        Assert.Equal("A", hook.GetContent(nowMs: 1_000));

        // 窗口内改写：仍返回缓存内容（节流语义，最多延迟一个窗口生效）
        File.WriteAllText(path, "changed-after-window-test");
        Assert.Equal("A", hook.GetContent(nowMs: 1_020));
    }

    [Fact]
    public void 过窗口且长度变化时重读()
    {
        var path = WriteHook("A");
        var hook = new RenderHooks.HookTemplateFile(path);
        Assert.Equal("A", hook.GetContent(nowMs: 1_000));

        // 换成长度不同的内容：即便 mtime 粒度未前进，长度判据也能识别
        File.WriteAllText(path, "BB");
        Assert.Equal("BB", hook.GetContent(nowMs: 1_200));
    }

    [Fact]
    public void 文件被删除后返回空串而非抛异常()
    {
        var path = WriteHook("A");
        var hook = new RenderHooks.HookTemplateFile(path);
        Assert.Equal("A", hook.GetContent(nowMs: 1_000));

        File.Delete(path);
        Assert.Equal("", hook.GetContent(nowMs: 1_200));
    }
}
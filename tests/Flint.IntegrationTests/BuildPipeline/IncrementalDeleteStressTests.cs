// Flint 静态站点生成器
// 增量删除压力探针（R6 批次 4.2② 遗留）：100 页批量删除后是否有文件级残留。
// 覆盖边缘命名形态（index/_index/中文/连字符/深层目录），复现 R6 记录的 1/100 残留

using AwesomeAssertions;
using Flint.Core.Abstractions;
using Xunit;

namespace Flint.IntegrationTests.BuildPipeline;

/// <summary>
/// R6 批次 4.2 发现：删除 100 页时"极低频文件级残留 1/100（根因待查）"。
/// 本探针以 120 页（含 20 页边缘命名）批量删除复现并定位
/// </summary>
public sealed class IncrementalDeleteStressTests : IDisposable
{
    private readonly string _siteRoot;

    public IncrementalDeleteStressTests()
    {
        _siteRoot = Path.Combine(Path.GetTempPath(), $"flint-incr-del-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_siteRoot);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_siteRoot)) Directory.Delete(_siteRoot, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public async Task Incremental_DeleteBatch100_LeavesNoStaleFiles()
    {
        WriteSite();
        var builder = TestSiteFactory.CreateBuilder(_siteRoot);
        var options = Options(_siteRoot);

        var fullResult = await builder.BuildAsync(options);
        fullResult.Success.Should().BeTrue("全量构建应成功: {0}",
            string.Join("; ", fullResult.Errors.Select(e => $"{e.ErrorCode} {e.FilePath}: {e.Message}")));
        var beforeFiles = Directory.GetFiles(Path.Combine(_siteRoot, "public"), "*", SearchOption.AllDirectories);
        beforeFiles.Should().HaveCountGreaterThan(100, "100+ 页应产出对应 index.html");

        // 删全部内容页（一次批量调用）
        var deleted = Directory.GetFiles(Path.Combine(_siteRoot, "content"), "*.md", SearchOption.AllDirectories)
            .ToArray();
        deleted.Should().HaveCountGreaterThan(100);
        foreach (var f in deleted)
        {
            File.Delete(f);
        }

        var result = await builder.IncrementalBuildAsync(options, deleted);
        result.Success.Should().BeTrue(": {0}", string.Join("; ", result.Errors.Select(e => e.Message)));

        // 断言：public 下零 .html 残留（列表页会重渲染出 index.html，故只查内容页形态残留）
        var staleContentPages = Directory.GetFiles(Path.Combine(_siteRoot, "public"), "*.html", SearchOption.AllDirectories)
            .Where(p => !p.EndsWith("index.html", StringComparison.OrdinalIgnoreCase) ||
                        p.Contains("/page/", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        staleContentPages.Should().BeEmpty("删除页的 index.html 不应残留");

        // 目录壳也不应残留（R6 4.2① 已修，此处回归护栏）
        var emptyDirs = Directory.GetDirectories(Path.Combine(_siteRoot, "public"), "*", SearchOption.AllDirectories)
            .Where(d => !Directory.EnumerateFileSystemEntries(d).Any())
            .ToArray();
        emptyDirs.Should().BeEmpty("删除页留下的目录壳应被清理");
    }

    /// <summary>
    /// 逐页增量删除（DevServer 真实路径：每删一页一次 IncrementalBuildAsync）。
    /// R6 批次 4.2② 的 1/100 残留若在逐页路径下复现，本测试锁定
    /// </summary>
    [Fact]
    public async Task Incremental_DeleteOneByOne_LeavesNoStaleFiles()
    {
        WriteSite();
        var builder = TestSiteFactory.CreateBuilder(_siteRoot);
        var options = Options(_siteRoot);

        (await builder.BuildAsync(options)).Success.Should().BeTrue();

        var pages = Directory.GetFiles(Path.Combine(_siteRoot, "content"), "*.md", SearchOption.AllDirectories);
        foreach (var page in pages)
        {
            File.Delete(page);
            var result = await builder.IncrementalBuildAsync(options, [page]);
            result.Success.Should().BeTrue("删除 {0} 后增量应成功: {1}", page,
                string.Join("; ", result.Errors.Select(e => e.Message)));
        }

        var staleContentPages = Directory.GetFiles(Path.Combine(_siteRoot, "public"), "*.html", SearchOption.AllDirectories)
            .Where(p => !p.EndsWith("index.html", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        staleContentPages.Should().BeEmpty("逐页删除后不应有内容页残留");
    }

    private void WriteSite()
    {
        var content = Path.Combine(_siteRoot, "content", "posts");
        Directory.CreateDirectory(content);
        Directory.CreateDirectory(Path.Combine(_siteRoot, "layouts"));

        File.WriteAllText(Path.Combine(_siteRoot, "Flint.toml"),
            "baseURL = \"http://localhost:1313/\"\ntitle = \"incr-del-stress\"\n");
        File.WriteAllText(Path.Combine(_siteRoot, "layouts", "single.html"),
            "<article>{{ page.title }}:{{ page.content }}</article>");
        File.WriteAllText(Path.Combine(_siteRoot, "layouts", "list.html"),
            "<ul>{{ for p in site.regular_pages }}<li>{{ p.title }}</li>{{ end }}</ul>");

        // 100 页常规命名 + 20 页边缘命名形态
        for (var i = 0; i < 100; i++)
        {
            File.WriteAllText(Path.Combine(content, $"page-{i:D3}.md"),
                $"---\ntitle: \"Page {i}\"\ndate: 2026-01-{(i % 28) + 1:D2}\n---\n\nBody {i}\n");
        }

        File.WriteAllText(Path.Combine(content, "_index.md"), "---\ntitle: \"Branch Index\"\n---\n\nBranch index body\n");
        File.WriteAllText(Path.Combine(content, "中文标题.md"), "---\ntitle: \"中文\"\n---\n\n中文正文\n");
        File.WriteAllText(Path.Combine(content, "with-dashes.md"), "---\ntitle: \"Dashes\"\n---\n\nDashes body\n");
        File.WriteAllText(Path.Combine(content, "with_underscores.md"), "---\ntitle: \"Underscores\"\n---\n\nUnderscores body\n");
        File.WriteAllText(Path.Combine(content, "UPPER.md"), "---\ntitle: \"Upper\"\n---\n\nUpper body\n");
        File.WriteAllText(Path.Combine(content, "MiXeD.md"), "---\ntitle: \"Mixed\"\n---\n\nMixed body\n");
        File.WriteAllText(Path.Combine(content, "dots.in.name.md"), "---\ntitle: \"Dots\"\n---\n\nDots body\n");
        File.WriteAllText(Path.Combine(content, "spaces in name.md"), "---\ntitle: \"Spaces\"\n---\n\nSpaces body\n");
        File.WriteAllText(Path.Combine(content, "paren(s).md"), "---\ntitle: \"Parens\"\n---\n\nParens body\n");
        File.WriteAllText(Path.Combine(content, "amp&and.md"), "---\ntitle: \"Amp\"\n---\n\nAmp body\n");
        File.WriteAllText(Path.Combine(content, "hash#.md"), "---\ntitle: \"Hash\"\n---\n\nHash body\n");
        File.WriteAllText(Path.Combine(content, "plus+.md"), "---\ntitle: \"Plus\"\n---\n\nPlus body\n");
        File.WriteAllText(Path.Combine(content, "eq=eq.md"), "---\ntitle: \"Eq\"\n---\n\nEq body\n");
        File.WriteAllText(Path.Combine(content, "at@.md"), "---\ntitle: \"At\"\n---\n\nAt body\n");
        File.WriteAllText(Path.Combine(content, "tilde~.md"), "---\ntitle: \"Tilde\"\n---\n\nTilde body\n");
        var deep = Path.Combine(content, "a", "b", "c");
        Directory.CreateDirectory(deep);
        File.WriteAllText(Path.Combine(deep, "deep-page.md"), "---\ntitle: \"Deep\"\n---\n\nDeep body\n");
    }

    private static Flint.Core.Models.BuildOptions Options(string siteRoot) => new()
    {
        SourcePath = siteRoot,
        OutputPath = Path.Combine(siteRoot, "public")
    };
}

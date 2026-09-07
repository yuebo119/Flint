// 临时诊断：b/index.json 的写入来源
using Xunit;

namespace Flint.IntegrationTests.BuildPipeline;

public sealed class TempDiagTests : IDisposable
{
    private readonly string _siteRoot = Path.Combine(
        Path.GetTempPath(), $"flint-diag-{Guid.NewGuid():N}");
    private readonly string _outputRoot;

    public TempDiagTests()
    {
        _outputRoot = Path.Combine(_siteRoot, "public");
        Directory.CreateDirectory(Path.Combine(_siteRoot, "content", "posts"));
        Directory.CreateDirectory(Path.Combine(_siteRoot, "layouts"));
        File.WriteAllText(Path.Combine(_siteRoot, "Flint.toml"),
            "baseURL = \"http://localhost:1313/\"\ntitle = \"t\"\n");
        File.WriteAllText(Path.Combine(_siteRoot, "layouts", "single.html"),
            "<article>{{ page.title }}</article>");
        File.WriteAllText(Path.Combine(_siteRoot, "layouts", "list.html"), "<ul></ul>");
        File.WriteAllText(Path.Combine(_siteRoot, "content", "posts", "b.md"),
            "+++\ntitle = \"Post B\"\noutputs = [\"html\", \"json\"]\n+++\n\nBody B.\n");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_siteRoot)) Directory.Delete(_siteRoot, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public async Task Diag_Json写入来源()
    {
        var renderer = new Flint.Core.Templates.ScribanTemplateRenderer(
            Path.Combine(_siteRoot, "layouts"));
        Console.WriteLine($"TemplateExists(single)={renderer.TemplateExists("single")}");
        Console.WriteLine($"TemplateExists(single.json)={renderer.TemplateExists("single.json")}");

        var configLoader = new Flint.Core.Configuration.ConfigLoader();
        var contentParser = new Flint.Core.Content.ContentParser();
        var assetPipeline = new Flint.Core.Assets.AssetPipeline();
        var builder = new Flint.Core.Site.SiteBuilder(
            contentParser, renderer, assetPipeline, configLoader);
        var result = await builder.BuildAsync(new Flint.Core.Models.BuildOptions
        {
            SourcePath = _siteRoot,
            OutputPath = _outputRoot
        });
        foreach (var e in result.Errors) Console.WriteLine($"ERR: {e.Message} @{e.FilePath}");

        var jsonPath = Path.Combine(_outputRoot, "b", "index.json");
        Console.WriteLine($"json exists={File.Exists(jsonPath)}");
        if (File.Exists(jsonPath))
            Console.WriteLine($"json content={File.ReadAllText(jsonPath)}");
        Assert.True(true);
    }
}

// Flint 静态站点生成器
// Liquid 内容智能过滤器锁定测试（2026-11-20 批次）：
// resize/fit/fill/crop 真像素变换、srcset 一键响应式图像、related 相关内容、
// toc 文章目录。产物断言读真实字节（解码验证尺寸，不是名字断言）。

using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.Templates;
using Xunit;

namespace Flint.Core.Tests.Templates;

public sealed class FluidContentIntelligenceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FluidTemplateRenderer _renderer;
    private readonly FileSystemResourceProvider _provider;

    public FluidContentIntelligenceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"Flint_cintel_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, "assets", "img"));
        // 100×60 测试图（纯色 PNG）
        WritePng("img/photo.png", 100, 60);
        _provider = new FileSystemResourceProvider(
            "https://example.com", Path.Combine(_tempDir, "assets"));
        _renderer = new FluidTemplateRenderer(_tempDir, "https://example.com", null, _provider);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
        GC.SuppressFinalize(this);
    }

    // ---- 图像几何变换（真像素处理）----

    [Fact]
    public async Task resize_等比缩放_产物尺寸真实()
    {
        var o = await RenderAsync(
            "{% assign img = 'img/photo.png' | resource %}" +
            "{% assign out = img | resize: '50x' %}" +
            "{{ out.width }}x{{ out.height }}|{{ out.name }}");
        Assert.StartsWith("50x30|", o);
        // 产物字节真实缩放：从登记队列反查并解码验证（不是只有名字）
        var generated = DrainGenerated();
        var resized = generated.FirstOrDefault(g => g.Name.Contains("resize_50x"));
        Assert.NotNull(resized);
        using var decoded = SixLabors.ImageSharp.Image.Load(resized!.BinaryContent!.Value.Span);
        Assert.Equal(50, decoded.Width);
        Assert.Equal(30, decoded.Height);
    }

    [Fact]
    public async Task fill_cover裁剪_输出精确尺寸()
    {
        var o = await RenderAsync(
            "{% assign img = 'img/photo.png' | resource %}" +
            "{% assign out = img | fill: '40x40' %}" +
            "{{ out.width }}x{{ out.height }}");
        Assert.Equal("40x40", o);
        var generated = DrainGenerated();
        var filled = generated.First(g => g.Name.Contains("fill_40x40"));
        using var decoded = SixLabors.ImageSharp.Image.Load(filled.BinaryContent!.Value.Span);
        Assert.Equal(40, decoded.Width);
        Assert.Equal(40, decoded.Height);
    }

    [Fact]
    public async Task crop_中心比例裁剪_不放大()
    {
        // 100×60 原图裁 1:1 → 中心 60×60（等比最大矩形，不放大）
        var o = await RenderAsync(
            "{% assign img = 'img/photo.png' | resource %}" +
            "{% assign out = img | crop: '300x300' %}" +
            "{{ out.width }}x{{ out.height }}");
        Assert.Equal("60x60", o);
    }

    [Fact]
    public async Task resize_格式转码_webp()
    {
        var o = await RenderAsync(
            "{% assign img = 'img/photo.png' | resource %}" +
            "{% assign out = img | resize: '50x webp' %}" +
            "{{ out.name }}");
        Assert.EndsWith(".webp", o);
        var generated = DrainGenerated();
        var webp = generated.First(g => g.Name.EndsWith(".webp", StringComparison.Ordinal));
        Assert.Equal("image/webp", webp.MediaType);
    }

    // ---- srcset ----

    [Fact]
    public async Task srcset_直出img_档位过滤超原图宽()
    {
        // 原图 100 宽：480/800 档全被滤（>100），剩原图档
        var o = await RenderAsync(
            "{% assign img = 'img/photo.png' | resource %}" +
            "{{ img | srcset: '480,800', sizes: '(max-width:600px) 100vw', alt: 'A \" B' }}");
        Assert.StartsWith("<img src=\"/img/photo.png\"", o);
        // 100 宽档位存在（原图自身档）
        Assert.Contains(" 100w", o);
        // 超原图宽的档位不生成
        Assert.DoesNotContain("480w", o);
        Assert.DoesNotContain("800w", o);
        Assert.Contains(" sizes=\"(max-width:600px) 100vw\"", o);
        // alt 转义
        Assert.Contains("alt=\"A &quot; B\"", o);
        Assert.EndsWith(" loading=\"lazy\" decoding=\"async\">", o);
    }

    [Fact]
    public async Task srcset_小档位真实生成并登记()
    {
        // 造 500 宽图，请求 100/200 档 → 两档真生成
        WritePng("img/wide.png", 500, 300);
        var o = await RenderAsync(
            "{% assign img = 'img/wide.png' | resource %}" +
            "{{ img | srcset: '100,200' }}");
        Assert.Contains(" 100w", o);
        Assert.Contains(" 200w", o);
        var generated = DrainGenerated();
        var v100 = generated.FirstOrDefault(g => g.Name.Contains("resize_100x"));
        var v200 = generated.FirstOrDefault(g => g.Name.Contains("resize_200x"));
        Assert.NotNull(v100);
        Assert.NotNull(v200);
        using var d100 = SixLabors.ImageSharp.Image.Load(v100!.BinaryContent!.Value.Span);
        Assert.Equal(100, d100.Width);
    }

    // ---- related ----

    [Fact]
    public async Task related_按共享标签排序并可过滤()
    {
        var ctx = MakeContext();
        var o = await RenderAsync(
            "{% assign rel = page | related: 2 %}" +
            "{% for r in rel %}{{ r.title }};{% endfor %}", ctx);
        // B 与 A 共享 tags go,rust（160 分）> C 共享 go（80 分）；D 无共享被滤
        Assert.Equal("B;C;", o);

        // min_score 过滤：≥100 只剩 B
        var ctx2 = MakeContext();
        var o2 = await RenderAsync(
            "{% assign rel = page | related: 5, min_score: 100 %}" +
            "{% for r in rel %}{{ r.title }};{% endfor %}", ctx2);
        Assert.Equal("B;", o2);
    }

    // ---- toc ----

    [Fact]
    public async Task toc_锚点与正文一致_class可定制()
    {
        var ctx = MakeContext();
        var o = await RenderAsync("{{ page | toc: 2, 3, class: 'my-toc' }}", ctx);
        Assert.Contains("<nav id=\"TableOfContents\" class=\"my-toc\">", o);
        // 锚点 id 来自 Headings 单源
        Assert.Contains("href=\"#sec-one\"", o);
        Assert.Contains("href=\"#sec-two-deep\"", o);
        // level 1/4 被区间裁掉
        Assert.DoesNotContain("href=\"#top\"", o);
        Assert.DoesNotContain("href=\"#deep4\"", o);

        // 默认无 class（id 形态，Scriban 侧 ToHTML 同款）
        var ctx2 = MakeContext();
        var o2 = await RenderAsync("{{ page | toc }}", ctx2);
        Assert.Contains("<nav id=\"TableOfContents\">", o2);
    }

    // ---- 帮助 ----

    private async Task<string> RenderAsync(string template, TemplateContext? context = null)
    {
        var file = Path.Combine(_tempDir, "t.liquid");
        await File.WriteAllTextAsync(file, template).ConfigureAwait(true);
        return await _renderer.RenderTemplateFileAsync(file, context ?? MakeContext()).ConfigureAwait(true);
    }

    private List<TemplateResource> DrainGenerated() =>
        _renderer.GeneratedResources.ToList();

    private static TemplateContext MakeContext()
    {
        var a = new PageContext
        {
            Title = "A", Content = "c",
            Permalink = "https://example.com/a/", RelPermalink = "/a/",
            Date = DateTimeOffset(2026, 1, 1), Tags = ["go", "rust"], Categories = [],
            WordCount = 1, ReadingTime = TimeSpan.FromMinutes(1),
            Headings =
            [
                new MarkdownHeading { Level = 1, Text = "top", Id = "top" },
                new MarkdownHeading { Level = 2, Text = "Sec One", Id = "sec-one" },
                new MarkdownHeading { Level = 3, Text = "Sec Two Deep", Id = "sec-two-deep" },
                new MarkdownHeading { Level = 4, Text = "Deep4", Id = "deep4" },
            ]
        };
        var b = MakePage("B", ["go", "rust"], DateTimeOffset(2026, 1, 2));
        var c = MakePage("C", ["go"], DateTimeOffset(2026, 1, 3));
        var d = MakePage("D", ["python"], DateTimeOffset(2026, 1, 4));
        var site = new SiteContext
        {
            Title = "S", BaseURL = "https://example.com", Language = "en",
            Pages = [a, b, c, d], RegularPages = [a, b, c, d],
            Taxonomies = new TaxonomyCollection { Taxonomies = new Dictionary<string, IReadOnlyList<TaxonomyTerm>>() },
            Menus = new MenuCollection { Menus = new Dictionary<string, IReadOnlyList<MenuItem>>() },
            Config = new SiteConfig { BaseURL = "https://example.com", Title = "S" }
        };
        return new TemplateContext { Page = a, Site = site };
    }

    private static PageContext MakePage(string title, string[] tags, DateTimeOffset date) => new()
    {
        Title = title, Content = "c",
        Permalink = $"https://example.com/{title.ToLowerInvariant()}/",
        RelPermalink = $"/{title.ToLowerInvariant()}/",
        Date = date, Tags = tags, Categories = [],
        WordCount = 1, ReadingTime = TimeSpan.FromMinutes(1)
    };

    private static DateTimeOffset DateTimeOffset(int y, int m, int d) =>
        new(y, m, d, 0, 0, 0, TimeSpan.Zero);

    private void WritePng(string name, int width, int height)
    {
        using var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(width, height);
        img[0, 0] = new SixLabors.ImageSharp.PixelFormats.Rgba32(255, 0, 0, 255);
        using var ms = new MemoryStream();
        img.Save(ms, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        File.WriteAllBytes(Path.Combine(_tempDir, "assets", name.Replace('/', Path.DirectorySeparatorChar)),
            ms.ToArray());
    }
}

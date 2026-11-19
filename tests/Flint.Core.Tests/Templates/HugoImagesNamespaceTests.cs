// Flint 静态站点生成器
// images.* 命名空间（Hugo 0.170.1 对齐批次）的锁定测试：
// - QrCodeGenerator：结构断言 + 矩阵哈希锁定（哈希样本全部经 ZXing.Net
//   0.16.20 解码回原文验证，2026-11，tmp/qr-verify 一次性验证轮）
// - images.QR / Filter / Process / Config / 滤镜工厂 / resources.PostProcess

using System.Security.Cryptography;
using System.Text;
using Flint.Core.Templates;
using Scriban;
using Scriban.Runtime;
using Xunit;

namespace Flint.Core.Tests.Templates;

public sealed class HugoImagesNamespaceTests : IDisposable
{
    private readonly string _assetsDir =
        Path.Combine(Path.GetTempPath(), $"Flint_images_{Guid.NewGuid():N}");

    public HugoImagesNamespaceTests()
    {
        Directory.CreateDirectory(_assetsDir);
        // 滤镜/管道测试的公共资产（直通策略只要求文件存在；Config 断言 3×2）
        WritePng("logo.png", 3, 2);
        File.WriteAllText(Path.Combine(_assetsDir, "a.css"), "a{}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_assetsDir))
        {
            Directory.Delete(_assetsDir, true);
        }
    }

    // ---- QR 编码器：结构与哈希 ----

    [Fact]
    public void QR_v1_结构断言_finder_timing_dark()
    {
        var m = Encode("1234567890", "medium");
        Assert.Equal(21, m.Length);

        // 左上 finder：外环暗 + 3×3 核心暗 + 中环亮
        Assert.True(m[0][0]);
        Assert.True(m[0][6]);
        Assert.True(m[6][0]);
        Assert.True(m[3][3]);
        Assert.False(m[1][1]); // 中环亮
        Assert.False(m[5][5]);
        // 右上 / 左下 finder 存在
        Assert.True(m[0][20]);
        Assert.True(m[20][0]);

        // timing（row 6，col 8..13）：明暗交替且 col 8 为暗
        for (var c = 8; c <= 13; c++)
        {
            Assert.Equal(c % 2 == 0, m[6][c]);
        }

        // dark module：(size-8, 8) 恒暗
        Assert.True(m[13][8]);
    }

    // 哈希锁定样本：全部经 ZXing 解码验证（掩码择优与位流正确性一起锁住）
    [Theory]
    [InlineData("https://gohugo.io/", "medium", 25,
        "3ee472747e0190fe6ae6be79bde9c2e16de958ddf0fbe41fea43f14c507ad2f2")]
    [InlineData("HELLO WORLD 1234567890", "high", 29,
        "df6311c45367ba460307a567bd9cfad358906bbfc805f88cb4483a91b9f2b0df")]
    [InlineData("Flint 静态站点生成器 — 中文与 UTF-8 多字节内容验证", "medium", 37,
        "b730c027a0b690f17a485b333bf5d657d692acfc853f02035c1bed3117c80199")]
    [InlineData("https://example.com/very-long-path/with/many/segments?a=1&b=2&c=3#fragment", "quartile", 45,
        "18ce907827ffaa8bb62399f6fcb2676dc9fb987e0428ce8506558271f668a6c5")]
    [InlineData("0123456789", "quartile", 21,
        "15993e2e0561a1e46845323c36e20a5a11b53c3d9a3ff6e9c22b5ac4c9f5f02c")]
    public void QR_矩阵哈希锁定(string text, string level, int size, string sha256)
    {
        var m = Encode(text, level);
        Assert.Equal(size, m.Length);
        Assert.Equal(sha256, MatrixSha(m));
    }

    [Theory]
    [InlineData("low")]
    [InlineData("medium")]
    [InlineData("quartile")]
    [InlineData("high")]
    public void QR_四级均可编码且格式区自洽(string level)
    {
        var m = Encode("level probe 42", level);
        // 两份 format 信息必须一致（col 8 的 rows 0-5/7-8 vs 右上/左下副本）
        var first = new bool[15];
        for (var i = 0; i <= 5; i++)
        {
            first[i] = m[i][8];
        }
        first[6] = m[7][8];
        first[7] = m[8][8];
        first[8] = m[8][7];
        for (var i = 9; i < 15; i++)
        {
            first[i] = m[8][14 - i];
        }
        for (var i = 0; i < 8; i++)
        {
            Assert.Equal(first[i], m[8][m.Length - 1 - i]);
        }
        for (var i = 8; i < 15; i++)
        {
            Assert.Equal(first[i], m[m.Length - 15 + i][8]);
        }
    }

    [Fact]
    public void QR_容量对齐_ZXing_口径()
    {
        // ZXing 家族实测分界：同文本产出一致版本（M 级 v1=13 / v7=121 / v8=122）
        Assert.Equal(21, Encode(new string('a', 13), "medium").Length);
        Assert.Equal(25, Encode(new string('a', 14), "medium").Length);
        Assert.Equal(45, Encode(new string('a', 121), "medium").Length);
        Assert.Equal(49, Encode(new string('a', 122), "medium").Length);
        // v10 上限 212（规范 213，保守 -1 换 ZXing 互操作）
        Assert.Equal(57, Encode(new string('a', 212), "medium").Length);
        Assert.Throws<InvalidOperationException>(() => Encode(new string('a', 213), "medium"));
    }

    [Fact]
    public void QR_空文本抛出对齐_Hugo_构建失败语义()
    {
        Assert.Throws<InvalidOperationException>(() => Encode("", "medium"));
    }

    [Fact]
    public void QR_SVG_默认颜色与透明背景()
    {
        var m = Encode("svg probe", "medium");
        var svg = QrCodeGenerator.ToSvg(m, 4, "#000000", "#ffffff");
        Assert.StartsWith("<svg ", svg);
        Assert.Contains("viewBox=\"0 0 29 29\"", svg);
        Assert.Contains("width=\"116\"", svg); // (21+8)*4 —— 29 模块 * 4
        Assert.Contains("fill=\"#ffffff\"", svg); // 背景
        // 透明背景（alpha 00）：不画背景 rect
        var transparent = QrCodeGenerator.ToSvg(m, 4, "#000000", "");
        Assert.DoesNotContain("100%\", \"100%", transparent);
        Assert.DoesNotContain("rect", transparent);
    }

    [Fact]
    public void QR_SVG_确定性输出()
    {
        var m = Encode("determinism", "medium");
        Assert.Equal(
            QrCodeGenerator.ToSvg(m, 4, "#000000", "#ffffff"),
            QrCodeGenerator.ToSvg(Encode("determinism", "medium"), 4, "#000000", "#ffffff"));
    }

    // ---- 模板级：images.* 注册 ----

    [Fact]
    public void 模板_QR_产出_SVG_资源与宽高()
    {
        var o = Render("{{ q = images.QR \"https://example.com\" }}{{ q.rel_permalink }}|{{ q.width }}x{{ q.height }}");
        Assert.EndsWith(".svg", o.Split('|')[0]);
        Assert.Matches(@"^\d+x\d+$", o.Split('|')[1]);
        // 19 字节 → v2-M（25 模块）+ 静区 8：33 × scale 4 = 132
        Assert.Equal("132x132", o.Split('|')[1]);
    }

    [Fact]
    public void 模板_QR_颜色选项进资源内容()
    {
        var colored = Render(
            "{{ (images.QR \"x\" (dict \"fgColor\" \"#123456\" \"bgColor\" \"#abcdef\")).content }}");
        Assert.Contains("#123456", colored);
        Assert.Contains("#abcdef", colored);
        // 透明背景：无背景 rect
        var transp = Render(
            "{{ (images.QR \"x\" (dict \"bgColor\" \"#ffffff00\")).content }}");
        Assert.DoesNotContain("rect", transp);
    }

    [Fact]
    public void 模板_QR_targetDir_前缀进资源名()
    {
        var o = Render("{{ (images.QR \"x\" (dict \"targetDir\" \"codes/qrs\")).name }}");
        Assert.StartsWith("codes/qrs/qr_", o);
    }

    [Fact]
    public void 模板_滤镜工厂与_Filter_组合()
    {
        // 21 个工厂存在且返回 filter 描述
        var factories = new[]
        {
            "AutoOrient", "Brightness", "ColorBalance", "Colorize", "Contrast", "Dither",
            "Gamma", "GaussianBlur", "Grayscale", "Hue", "Invert", "Mask", "Opacity",
            "Overlay", "Padding", "Pixelate", "Saturation", "Sepia", "Sigmoid", "Text",
            "UnsharpMask"
        };
        foreach (var f in factories)
        {
            var o = Render($"{{{{ (images.{f} 6).__flint_filter_op__ }}}}");
            Assert.Equal(f, o);
        }

        // images.Filter FILTERS IMAGE：登记 filter_ 命名资源（原图直通策略）
        var o2 = Render(
            "{{ img = resources.Get \"logo.png\" }}" +
            "{{ out = images.Filter (slice (images.GaussianBlur 6) (images.Pixelate 8)) img }}" +
            "{{ out.name }}");
        Assert.StartsWith("logo_filter_", o2);
        Assert.Contains("GaussianBlur_6", o2);
        Assert.Contains("Pixelate_8", o2);
        Assert.EndsWith(".png", o2);
    }

    [Fact]
    public void 模板_资源成员级_Filter_与_Process()
    {
        var o = Render(
            "{{ img = resources.Get \"logo.png\" }}" +
            "{{ f = img.Filter (images.Sepia 3) }}" +
            "{{ f.name }}|{{ (img.Process \"resize 100x\").name }}");
        Assert.StartsWith("logo_filter_Sepia_3.png|", o);
        Assert.Contains("process_resize_100x", o);
    }

    [Fact]
    public void 模板_images_Process_命名空间级()
    {
        var o = Render(
            "{{ img = resources.Get \"logo.png\" }}{{ (images.Process img \"webp\").name }}");
        Assert.Equal("logo_process_webp.png", o);
    }

    [Fact]
    public void 模板_images_Config_读真实位图尺寸()
    {
        var o = Render("{{ c = images.Config (resources.Get \"logo.png\") }}{{ c.Width }}x{{ c.Height }}");
        Assert.Equal("3x2", o);
    }

    [Fact]
    public void 模板_resources_PostProcess_恒等()
    {
        var o = Render(
            "{{ a = resources.Get \"a.css\" }}" +
            "{{ b = a | resources.PostProcess }}" +
            "{{ b.name }}");
        Assert.Equal("a.css", o);
    }

    // ---- 帮助 ----

    private static bool[][] Encode(string text, string level) =>
        QrCodeGenerator.Encode(Encoding.UTF8.GetBytes(text), QrCodeGenerator.LevelIndex(level));

    private static string MatrixSha(bool[][] m)
    {
        var bytes = new byte[m.Length * m.Length];
        var i = 0;
        foreach (var row in m)
        {
            foreach (var v in row)
            {
                bytes[i++] = v ? (byte)1 : (byte)0;
            }
        }
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private string Render(string template)
    {
        var provider = new FileSystemResourceProvider("https://example.com", _assetsDir);
        var fns = new BuiltinTemplateFunctions("https://example.com", provider);
        var globals = new ScriptObject();
        fns.RegisterFunctions(globals);
        var ctx = new TemplateContext();
        ctx.PushGlobal(globals);
        var parsed = Template.Parse(template);
        Assert.False(parsed.HasErrors, string.Join("; ", parsed.Messages));
        return parsed.Render(ctx);
    }

    private void WritePng(string name, int width, int height)
    {
        using var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(
            width, height);
        using var ms = new MemoryStream();
        img.Save(ms, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        File.WriteAllBytes(Path.Combine(_assetsDir, name), ms.ToArray());
    }
}

// bench asset：L4 资源管线画像（scripts/asset-bench.py 的 C# 移植）
// 1000 页 × L3 主题 + 图片资产（噪声 PNG 解码）+ Sass 编译；
// 手写 LCG 噪声 PNG（与 Python 版同种子同像素公式；压缩流由 .NET zlib 产生，
// 文件字节与 Python 版可不同但像素内容一致，对解码负载等价）。

using System.Buffers.Binary;
using System.CommandLine;
using System.IO.Compression;

namespace Flint.DevTools.Bench;

/// <summary>
/// 资源管线基准
/// </summary>
internal static class AssetBench
{
    private const string Scss = @"$primary: #2b6cb0;
$radius: 4px;
@mixin card($pad: 12px) {
  padding: $pad;
  border-radius: $radius;
  box-shadow: 0 1px 3px rgba(0,0,0,.12);
}
@each $name, $color in (a: #e53e3e, b: #38a169, c: #d69e2e, d: #805ad5, e: #319795) {
  .badge-#{$name} { color: $color; border: 1px solid darken($color, 10%); @include card(6px); }
}
nav.site {
  ul { display: flex; gap: 8px; li { a { color: $primary; &:hover { text-decoration: underline; } } } }
}
@for $i from 1 through 40 {
  .col-#{$i} { width: 100% / $i; @if $i % 5 == 0 { border-left: 1px solid #eee; } }
}
article { @include card; h1 { font-size: 1.6rem; color: $primary; } p code { background: #f7fafc; } }
";

    internal static Command BuildCommand()
    {
        var pagesOpt = new Option<int>("--pages") { Description = "页数", DefaultValueFactory = _ => 1000 };
        var imagesOpt = new Option<int>("--images") { Description = "图片数（640x480 噪声 PNG）", DefaultValueFactory = _ => 100 };
        var runsOpt = new Option<int>("--runs") { Description = "测量轮数（不含预热）", DefaultValueFactory = _ => 3 };

        var cmd = new Command("asset", "L4 资源管线画像（图片解码/重编码 + Sass 编译）");
        cmd.Options.Add(pagesOpt);
        cmd.Options.Add(imagesOpt);
        cmd.Options.Add(runsOpt);

        cmd.SetAction(async parseResult =>
        {
            var pages = parseResult.GetValue(pagesOpt);
            var images = parseResult.GetValue(imagesOpt);
            var runs = parseResult.GetValue(runsOpt);

            var root = Path.GetFullPath(DefaultPaths.AssetBenchRoot);
            var site = Path.Combine(root, "site");
            var pub = Path.Combine(root, "site-pub");

            RepoGuard.DeleteTree(site);
            GenCorpus(site, pages, images);
            Console.Error.WriteLine($"L4 语料就绪：{pages} 页 × L3 主题 + {images} 图 + Sass");

            ProcessRunner.RequireExe(DefaultPaths.FlintExe);

            var times = new List<double>(runs);
            var peaks = new List<double>(runs);
            var flintArgs = new[] { "build", "-s", site, "-o", pub };
            var env = new Dictionary<string, string> { ["FLINT_TRACE_PHASES"] = "1" };

            for (var i = 0; i < runs + 1; i++) // 首轮预热（含磁盘缓存建立）
            {
                RepoGuard.DeleteTree(pub);
                var result = await ProcessRunner.RunTimedAsync(DefaultPaths.FlintExe, flintArgs, env).ConfigureAwait(false);
                if (result.ExitCode != 0)
                {
                    throw new InvalidOperationException($"构建失败 rc={result.ExitCode}: {result.Stderr}");
                }

                peaks.Add(result.PeakRssBytes / 1048576.0);

                if (i == 0)
                {
                    Console.Error.WriteLine("--- 预热轮阶段明细 ---");
                    Console.Error.WriteLine(result.Stderr);
                }
                else
                {
                    times.Add(result.ElapsedMs);
                }
            }

            Console.WriteLine();
            Console.WriteLine($"=== L4 资源管线画像（{runs} 轮中位）===");
            Console.WriteLine($"构建时间中位: {ProcessRunner.Median(times):F0}ms");
            Console.WriteLine($"内存峰值中位: {ProcessRunner.Median(peaks):F0}MB");
            Console.WriteLine($"产出文件数: {CountFiles(pub)}");
            return 0;
        });

        return cmd;
    }

    private static int CountFiles(string dir)
    {
        return Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Count()
            : 0;
    }

    private static void GenCorpus(string site, int pages, int images)
    {
        var content = Path.Combine(site, "content", "posts");
        var layouts = Path.Combine(site, "layouts", "_default");
        var partials = Path.Combine(site, "layouts", "partials");
        var imgdir = Path.Combine(site, "assets", "images");
        var styledir = Path.Combine(site, "assets", "styles");
        foreach (var d in new[] { content, layouts, partials, imgdir, styledir })
        {
            Directory.CreateDirectory(d);
        }

        File.WriteAllText(Path.Combine(site, "Flint.toml"),
            "baseURL = \"http://localhost:1313/\"\ntitle = \"Asset Bench\"\n", Utf8.NoBom);
        File.WriteAllText(Path.Combine(layouts, "single.html"),
            "<nav>{{ include \"nav\" }}</nav>{{ include \"sidebar\" }}<article><h1>{{ page.title }}</h1>{{ page.content }}</article>{{ include \"related\" }}{{ partialcached \"footer\" }}",
            Utf8.NoBom);
        File.WriteAllText(Path.Combine(partials, "sidebar.html"),
            "<aside>{{ for p in site.regular_pages }}<li><a href=\"{{ p.rel_permalink }}\">{{ p.title }}</a></li>{{ end }}</aside>", Utf8.NoBom);
        File.WriteAllText(Path.Combine(partials, "nav.html"),
            "<nav>{{ for p in site.regular_pages }}<a href=\"{{ p.rel_permalink }}\">{{ p.title }}</a>{{ end }}</nav>", Utf8.NoBom);
        File.WriteAllText(Path.Combine(partials, "related.html"),
            "<div>{{ for p in site.regular_pages }}{{ if p.type == \"page\" }}<span>{{ p.title }}</span>{{ end }}{{ end }}</div>", Utf8.NoBom);
        File.WriteAllText(Path.Combine(partials, "footer.html"), "<footer>C</footer>", Utf8.NoBom);
        File.WriteAllText(Path.Combine(site, "layouts", "index.html"), "<h1>home</h1>", Utf8.NoBom);

        for (var i = 0; i < pages; i++)
        {
            var fm = $"---\ntitle: \"Post {i}\"\ndate: 2026-01-01\ntags: [\"t{i % 20}\"]\n---\n\nBody {i} lorem ipsum.";
            File.WriteAllText(Path.Combine(content, $"p{i:D4}.md"), fm, Utf8.NoBom);
        }

        Console.Error.WriteLine($"生成 {images} 张 PNG（640x480 噪声）...");
        for (var i = 0; i < images; i++)
        {
            NoisePng.Write(Path.Combine(imgdir, $"img_{i:D3}.png"), 640, 480, i + 7);
        }

        File.WriteAllText(Path.Combine(styledir, "main.scss"), Scss, Utf8.NoBom);
    }

    /// <summary>手写噪声 PNG：LCG 像素（与 Python 版同种子同公式，像素内容逐字节一致）；
    /// zlib 压缩流由 .NET 实现产生，文件字节与 Python 版可不同，对解码负载等价。</summary>
    private static class NoisePng
    {
        private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        private static readonly uint[] CrcTable = BuildCrcTable();

        public static void Write(string path, int w, int h, int seed)
        {
            var rng = (uint)seed;
            var raw = new byte[h * (1 + (w * 3))];
            var pos = 0;
            for (var y = 0; y < h; y++)
            {
                raw[pos++] = 0; // filter: none
                for (var x = 0; x < w; x++)
                {
                    rng = ((1103515245 * rng) + 12345) & 0x7FFFFFFF;
                    raw[pos++] = (byte)(rng & 0xFF);
                    raw[pos++] = (byte)((rng >> 8) & 0xFF);
                    raw[pos++] = (byte)((rng >> 16) & 0xFF);
                }
            }

            using var fs = File.Create(path);
            fs.Write(PngSignature);

            var ihdr = new byte[13];
            BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0, 4), (uint)w);
            BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4, 4), (uint)h);
            ihdr[8] = 8;  // bit depth
            ihdr[9] = 2;  // color type: truecolor
            ihdr[10] = 0; // compression
            ihdr[11] = 0; // filter
            ihdr[12] = 0; // interlace
            WriteChunk(fs, "IHDR", ihdr);

            using var idat = new MemoryStream();
            using (var zlib = new ZLibStream(idat, CompressionLevel.Fastest, leaveOpen: true))
            {
                zlib.Write(raw);
            }

            WriteChunk(fs, "IDAT", idat.ToArray());
            WriteChunk(fs, "IEND", Array.Empty<byte>());
        }

        private static void WriteChunk(Stream stream, string tag, byte[] data)
        {
            var tagBytes = System.Text.Encoding.ASCII.GetBytes(tag);

            uint crc = 0xFFFFFFFF;
            foreach (var b in tagBytes)
            {
                crc = (crc >> 8) ^ CrcTable[(crc ^ b) & 0xFF];
            }

            foreach (var b in data)
            {
                crc = (crc >> 8) ^ CrcTable[(crc ^ b) & 0xFF];
            }

            crc ^= 0xFFFFFFFF;

            Span<byte> header = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(header, (uint)data.Length);
            stream.Write(header);
            stream.Write(tagBytes);
            stream.Write(data);

            Span<byte> crcSpan = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(crcSpan, crc);
            stream.Write(crcSpan);
        }

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                var c = n;
                for (var k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? (0xEDB88320 ^ (c >> 1)) : (c >> 1);
                }

                table[n] = c;
            }

            return table;
        }
    }
}

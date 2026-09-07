// Flint 静态站点生成器
// Build 命令处理器

using System.Diagnostics;
using Flint.Core.Assets;
using Flint.Core.Configuration;
using Flint.Core.Content;
using Flint.Core.Content.Shortcodes;
using Flint.Core.Models;
using Flint.Core.Site;
using Flint.Core.Templates;

namespace Flint.Cli;

/// <summary>
/// Build 命令处理器
/// </summary>
internal static class BuildHandler
{
    /// <summary>
    /// 执行构建命令
    /// </summary>
    public static async Task<int> ExecuteAsync(
        bool minify,
        bool drafts,
        bool future,
        string output,
        string source,
        bool clean,
        bool verbose,
        CancellationToken cancellationToken = default)
    {
        var sourcePath = Path.GetFullPath(source);
        var outputPath = Path.GetFullPath(output);

        // 清理守卫：--clean 会递归删除输出目录（无回收站），
        // 输出目录等于源目录或包含源目录时必须拒绝，否则源内容不可逆丢失。
        // GetFullPath 保留尾分隔符（如 "C:\site\"），直接拼接分隔符会产生
        // 双分隔符使前缀匹配恒失效（守卫被绕过、源目录被删）——先归一化再比较
        var sourceNorm = sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var outputNorm = outputPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (clean && (string.Equals(outputNorm, sourceNorm, StringComparison.OrdinalIgnoreCase)
            || sourceNorm.StartsWith(outputNorm + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"错误: 输出目录 ({outputPath}) 等于或包含源目录 ({sourcePath})，--clean 将删除源内容，已拒绝");
            Console.ResetColor();
            return 1;
        }

        // 检查是否是有效的 Flint 站点
        var configPath = ConfigLoader.FindConfigFile(sourcePath);
        if (configPath == null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("错误: 当前目录不是有效的 Flint 站点");
            Console.WriteLine("请在站点根目录下运行此命令，或使用 'Flint new site <name>' 创建新站点");
            Console.ResetColor();
            return 1;
        }

        if (verbose)
        {
            Console.WriteLine($"源目录: {sourcePath}");
            Console.WriteLine($"输出目录: {outputPath}");
            Console.WriteLine($"配置文件: {configPath}");
            Console.WriteLine($"压缩: {minify}");
            Console.WriteLine($"包含草稿: {drafts}");
            Console.WriteLine($"包含未来内容: {future}");
            Console.WriteLine();
        }

        try
        {
            // 创建核心组件
            var configLoader = new ConfigLoader();
            var siteConfig = await configLoader.LoadAsync(configPath, cancellationToken);
            // 主题布局叠加（对齐 Hugo 主题语义）：站点 layouts 优先，theme 配置的
            // 主题目录按序回退——mod 下载的主题其 layouts 由此生效
            var themeLayoutDirs = siteConfig.Theme
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(t => Path.Combine(sourcePath, "themes", t, "layouts"))
                .ToArray();
            var templateRenderer = new ScribanTemplateRenderer(
                Path.Combine(sourcePath, "layouts"),
                siteConfig.BaseURL,
                themeLayoutDirs);

            // render hooks（T5.1）：layouts/_markup/render-*.html 存在时定制链接/图片/标题渲染
            var renderHooks = RenderHooks.Load(Path.Combine(sourcePath, "layouts"), templateRenderer);
            var markdownParser = renderHooks is not null ? new MarkdownParser(renderHooks) : new MarkdownParser();
            var contentParser = new ContentParser(new FrontMatterParser(), markdownParser, new ShortcodeProcessor());
            ContentParserDateSetup.Apply(contentParser, siteConfig,
                Path.Combine(sourcePath, siteConfig.ContentDir));
            var assetPipeline = new AssetPipeline(new AssetPipelineOptions
            {
                MinifyCss = minify,
                SourceDirectory = sourcePath,
                OutputDirectory = outputPath
            },
            imageProcessor: new ImageProcessor(),
            sassCompiler: new SassCompiler(),
            jsBundler: new JavaScriptBundler());

            // 创建站点构建器
            var siteBuilder = new SiteBuilder(
                contentParser,
                templateRenderer,
                assetPipeline,
                configLoader);

            // 清理输出目录
            if (clean && Directory.Exists(outputPath))
            {
                if (verbose)
                    Console.WriteLine("清理输出目录...");
                await siteBuilder.CleanAsync(outputPath, cancellationToken);
            }

            // 执行构建
            Console.WriteLine("开始构建站点...");
            var stopwatch = Stopwatch.StartNew();

            var buildOptions = new BuildOptions
            {
                SourcePath = sourcePath,
                OutputPath = outputPath,
                Minify = minify,
                IncludeDrafts = drafts,
                IncludeFuture = future,
                Parallelism = Environment.ProcessorCount
            };

            var result = await siteBuilder.BuildAsync(buildOptions, cancellationToken);
            stopwatch.Stop();

            // 输出结果
            if (result.Success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine();
                Console.WriteLine($"构建成功！");
                Console.ResetColor();
                Console.WriteLine($"  页面: {result.PagesBuilt}");
                Console.WriteLine($"  资源: {result.AssetsProcessed}");
                Console.WriteLine($"  耗时: {result.Duration.TotalMilliseconds:F0}ms");
                Console.WriteLine($"  内存: {result.MemoryUsed / 1024 / 1024:F1}MB");
                Console.WriteLine($"  输出: {outputPath}");

                if (result.Warnings.Count > 0)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"\n警告 ({result.Warnings.Count}):");
                    foreach (var warning in result.Warnings.Take(10))
                    {
                        Console.WriteLine($"  - {warning.Message}");
                    }
                    if (result.Warnings.Count > 10)
                    {
                        Console.WriteLine($"  ... 还有 {result.Warnings.Count - 10} 个警告");
                    }
                    Console.ResetColor();
                }

                return 0;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine();
                Console.WriteLine($"构建失败！");
                Console.WriteLine($"\n错误 ({result.Errors.Count}):");
                foreach (var error in result.Errors)
                {
                    Console.WriteLine($"  [{error.ErrorCode}] {error.FilePath}:{error.Line}:{error.Column}");
                    Console.WriteLine($"    {error.Message}");
                }
                Console.ResetColor();
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"构建异常: {ex.Message}");
            if (verbose)
            {
                Console.WriteLine(ex.StackTrace);
            }
            Console.ResetColor();
            return 1;
        }
    }
}

// Flint 静态站点生成器
// Serve 命令处理器 - 使用完整的 DevServer 实现

using Flint.Core.Abstractions;
using Flint.Core.Assets;
using Flint.Core.Configuration;
using Flint.Core.Content;
using Flint.Core.Content.Shortcodes;
using Flint.Core.Server;
using Flint.Core.Site;
using Flint.Core.Templates;

namespace Flint.Cli;

/// <summary>
/// Serve 命令处理器
/// </summary>
internal static class ServeHandler
{
    /// <summary>
    /// 执行 serve 命令
    /// </summary>
    public static async Task<int> ExecuteAsync(
        int port,
        bool openBrowser,
        bool liveReload,
        bool drafts,
        string source,
        bool verbose,
        string host = "localhost")
    {
        var sourcePath = Path.GetFullPath(source);
        var outputPath = Path.Combine(sourcePath, "public");

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

        try
        {
            // 创建核心组件
            var configLoader = new ConfigLoader();
            var config = await configLoader.LoadAsync(configPath);
            // 主题布局叠加（同 BuildHandler）：站点优先、主题回退，dev server 一致生效
            var themeLayoutDirs = config.Theme
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(t => Path.Combine(sourcePath, "themes", t, "layouts"))
                .ToArray();
            var templateRenderer = new ScribanTemplateRenderer(
                Path.Combine(sourcePath, "layouts"),
                config.BaseURL,
                themeLayoutDirs);

            // render hooks（T5.1）：layouts/_markup/render-*.html 存在时定制链接/图片/标题渲染
            var renderHooks = RenderHooks.Load(Path.Combine(sourcePath, "layouts"), templateRenderer);
            var markdownParser = renderHooks is not null ? new MarkdownParser(renderHooks) : new MarkdownParser();
            var contentParser = new ContentParser(new FrontMatterParser(), markdownParser, new ShortcodeProcessor());
            ContentParserDateSetup.Apply(contentParser, config,
                Path.Combine(sourcePath, config.ContentDir));
            var assetPipeline = new AssetPipeline(new AssetPipelineOptions
            {
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

            // 创建开发服务器
            using var devServer = new DevServer(siteBuilder);

            var options = new DevServerOptions
            {
                SourcePath = sourcePath,
                OutputPath = outputPath,
                Port = port,
                BindAddress = host,
                LiveReload = liveReload,
                OpenBrowser = openBrowser,
                IncludeDrafts = drafts,
                IncludeFuture = true,
                Verbose = verbose
            };

            // 设置取消处理
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            Console.WriteLine($"启动开发服务器...");
            Console.WriteLine($"  站点目录: {sourcePath}");
            Console.WriteLine($"  输出目录: {outputPath}");
            Console.WriteLine();

            // 启动服务器
            await devServer.StartAsync(options, cts.Token);

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"开发服务器已启动: {devServer.ServerUrl}");
            Console.ResetColor();
            Console.WriteLine("按 Ctrl+C 停止服务器");
            Console.WriteLine();

            // 等待取消信号
            try
            {
                await Task.Delay(Timeout.Infinite, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // 正常退出
            }

            Console.WriteLine("\n正在停止服务器...");
            await devServer.StopAsync();
            Console.WriteLine("服务器已停止");

            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"服务器错误: {ex.Message}");
            if (verbose)
            {
                Console.WriteLine(ex.StackTrace);
            }
            Console.ResetColor();
            return 1;
        }
    }
}

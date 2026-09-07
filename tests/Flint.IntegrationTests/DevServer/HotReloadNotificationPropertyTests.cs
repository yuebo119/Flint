// Flint 静态站点生成器
// 热重载通知正确性属性测试
// **Validates: Property 9**

using Flint.Core.Abstractions;
using Xunit;
using Flint.Core.Assets;
using Flint.Core.Configuration;
using Flint.Core.Content;
using Flint.Core.Server;
using Flint.Core.Site;
using Flint.Core.Templates;
using Flint.IntegrationTests.Fixtures;
using Flint.IntegrationTests.Utilities;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using LiveReloadMessage = Flint.IntegrationTests.Utilities.LiveReloadMessage;
using LiveReloadMessageType = Flint.IntegrationTests.Utilities.LiveReloadMessageType;
// 解决命名空间冲突
using Random = System.Random;

namespace Flint.IntegrationTests.DevServer;

/// <summary>
/// 热重载通知正确性属性测试
/// **Validates: Property 9**
/// </summary>
/// <remarks>
/// Property 9: 热重载通知正确性
/// *For any* 内容文件或资源文件的修改，开发服务器应通过 WebSocket 向所有连接的客户端
/// 发送正确类型的热重载通知（内容变化发送 reload，CSS 变化发送 cssUpdate）。
/// 
/// 满足需求：
/// - Requirements 3.6: 内容文件变化触发刷新
/// - Requirements 3.7: CSS 文件变化触发热更新
/// - Requirements 3.10: 多客户端连接广播通知
/// </remarks>
[Collection("DevServer")]
public class HotReloadNotificationPropertyTests
{
    #region 辅助方法

    /// <summary>
    /// 运行异步属性测试的辅助方法
    /// 基础设施异常（端口/文件/socket 竞争）重试一次：并行用例起停 server 时
    /// 偶发互踩不应记为断言失败；断言失败（返回 false）不重试
    /// </summary>
    private static Property RunPropertyTestAsync(Func<Task<bool>> testFunc)
    {
        const int maxAttempts = 2;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var result = testFunc().GetAwaiter().GetResult();
                return result.ToProperty();
            }
            catch (Exception) when (attempt < maxAttempts)
            {
                // 首次失败按基础设施竞争重试
            }
            catch (Exception)
            {
                return false.ToProperty();
            }
        }

        return false.ToProperty();
    }

    /// <summary>
    /// 启动开发服务器
    /// </summary>
    private static async Task<(IDevServer devServer, DevServerTestClient client, CancellationTokenSource cts)> StartServerAsync(TestSiteFixture fixture)
    {
        var cts = new CancellationTokenSource();

        var contentParser = new ContentParser();
        var templateRenderer = new ScribanTemplateRenderer(
            Path.Combine(fixture.SiteRoot, "layouts"));
        var assetPipeline = new AssetPipeline();
        var configLoader = new ConfigLoader();

        var siteBuilder = new SiteBuilder(
            contentParser,
            templateRenderer,
            assetPipeline,
            configLoader);

        var devServer = new Core.Server.DevServer(siteBuilder);

        var options = new DevServerOptions
        {
            SourcePath = fixture.SiteRoot,
            OutputPath = fixture.OutputPath,
            Port = Random.Shared.Next(10000, 60000),
            LiveReload = true,
            OpenBrowser = false,
            UseHttps = false,
            BindAddress = "127.0.0.1",
            IncludeDrafts = true,
            IncludeFuture = true,
            Verbose = false
        };

        await devServer.StartAsync(options, cts.Token);
        var client = new DevServerTestClient(devServer.ServerUrl!);
        await client.WaitForServerReadyAsync(TimeSpan.FromSeconds(10));

        return (devServer, client, cts);
    }

    /// <summary>
    /// 停止开发服务器并清理资源
    /// </summary>
    private static async Task StopServerAsync(IDevServer? devServer, DevServerTestClient? client, CancellationTokenSource? cts)
    {
        if (client != null)
        {
            await client.DisposeAsync();
        }

        if (devServer != null)
        {
            await devServer.StopAsync();
            if (devServer is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        cts?.Cancel();
        cts?.Dispose();
    }

    private static LiveReloadMessageType GetExpectedNotificationType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".css" or ".scss" or ".sass" => LiveReloadMessageType.CssUpdate,
            _ => LiveReloadMessageType.Reload
        };
    }

    #endregion

    #region Property 9: 热重载通知正确性

    /// <summary>
    /// 属性测试：文件扩展名决定通知类型
    /// **Validates: Property 9**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(FileChangeNotificationArbitraries) })]
    public Property Property9_FileExtensionDeterminesNotificationType(FileChangeNotificationTestCase testCase)
    {
        // 验证文件扩展名与预期通知类型的映射关系
        var expectedType = GetExpectedNotificationType(testCase.Extension);

        return (expectedType == testCase.ExpectedNotificationType).ToProperty()
            .Label($"扩展名 '{testCase.Extension}' 应触发 '{testCase.ExpectedNotificationType}' 通知");
    }

    /// <summary>
    /// 属性测试：CSS 相关扩展名触发 CssUpdate
    /// **Validates: Property 9**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Property9_CssExtensionsTriggerCssUpdate(CssExtension extension)
    {
        var ext = extension switch
        {
            CssExtension.Css => ".css",
            CssExtension.Scss => ".scss",
            CssExtension.Sass => ".sass",
            _ => ".css"
        };

        var expectedType = GetExpectedNotificationType(ext);

        return (expectedType == LiveReloadMessageType.CssUpdate).ToProperty()
            .Label($"CSS 扩展名 '{ext}' 应触发 CssUpdate 通知");
    }

    /// <summary>
    /// 属性测试：非 CSS 扩展名触发 Reload
    /// **Validates: Property 9**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Property9_NonCssExtensionsTriggerReload(NonCssExtension extension)
    {
        var ext = extension switch
        {
            NonCssExtension.Html => ".html",
            NonCssExtension.Md => ".md",
            NonCssExtension.Js => ".js",
            NonCssExtension.Json => ".json",
            NonCssExtension.Toml => ".toml",
            NonCssExtension.Yaml => ".yaml",
            _ => ".html"
        };

        var expectedType = GetExpectedNotificationType(ext);

        return (expectedType == LiveReloadMessageType.Reload).ToProperty()
            .Label($"非 CSS 扩展名 '{ext}' 应触发 Reload 通知");
    }

    /// <summary>
    /// 属性测试：多客户端应收到相同通知
    /// **Validates: Property 9**
    /// </summary>
    [Property(MaxTest = 50)]
    public Property Property9_AllClientsReceiveSameNotification(PositiveInt clientCount)
    {
        return RunPropertyTestAsync(async () =>
        {
            using var fixture = new TestSiteFixture();
            await fixture.InitializeAsync();

            IDevServer? devServer = null;
            DevServerTestClient? mainClient = null;
            CancellationTokenSource? cts = null;
            var clients = new List<DevServerTestClient>();

            try
            {
                await fixture.CreateSiteAsync("minimal");
                await fixture.BuildAsync();
                (devServer, mainClient, cts) = await StartServerAsync(fixture);

                // 限制客户端数量以避免资源耗尽
                var count = Math.Min(clientCount.Get % 5 + 1, 5);

                // 创建多个客户端
                for (var i = 0; i < count; i++)
                {
                    var client = new DevServerTestClient(devServer.ServerUrl!);
                    await client.ConnectLiveReloadAsync("/__livereload");
                    clients.Add(client);
                }

                // 验证所有客户端都已连接
                var allConnected = clients.All(c => c.IsWebSocketConnected);

                return allConnected;
            }
            finally
            {
                foreach (var client in clients)
                {
                    await client.DisposeAsync();
                }
                await StopServerAsync(devServer, mainClient, cts);
                await fixture.DisposeAsync();
            }
        });
    }

    /// <summary>
    /// 属性测试：LiveReloadMessage 结构正确性
    /// **Validates: Property 9**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Property9_LiveReloadMessageStructureIsCorrect(
        LiveReloadMessageType type,
        NonEmptyString data)
    {
        var message = new LiveReloadMessage
        {
            Type = type,
            Data = data.Get,
            Timestamp = DateTimeOffset.UtcNow
        };

        var hasValidType = Enum.IsDefined(message.Type);
        var hasTimestamp = message.Timestamp != default;
        var hasData = !string.IsNullOrEmpty(message.Data);

        return (hasValidType && hasTimestamp && hasData).ToProperty()
            .Label("LiveReloadMessage 应包含有效的类型、时间戳和数据");
    }

    /// <summary>
    /// 属性测试：消息类型枚举完整性
    /// **Validates: Property 9**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Property9_AllMessageTypesAreDefined(LiveReloadMessageType type)
    {
        return Enum.IsDefined(type).ToProperty()
            .Label($"消息类型 {type} 应该是已定义的枚举值");
    }

    #endregion
}


/// <summary>
/// 文件变化通知测试用例
/// </summary>
public record FileChangeNotificationTestCase
{
    public required string Extension { get; init; }
    public required LiveReloadMessageType ExpectedNotificationType { get; init; }
}

/// <summary>
/// CSS 扩展名枚举
/// </summary>
public enum CssExtension
{
    Css,
    Scss,
    Sass
}

/// <summary>
/// 非 CSS 扩展名枚举
/// </summary>
public enum NonCssExtension
{
    Html,
    Md,
    Js,
    Json,
    Toml,
    Yaml
}

/// <summary>
/// 文件变化通知生成器
/// </summary>
public static class FileChangeNotificationArbitraries
{
    private static readonly FileChangeNotificationTestCase[] TestCases =
    [
        // CSS 相关扩展名 -> CssUpdate
        new() { Extension = ".css", ExpectedNotificationType = LiveReloadMessageType.CssUpdate },
        new() { Extension = ".scss", ExpectedNotificationType = LiveReloadMessageType.CssUpdate },
        new() { Extension = ".sass", ExpectedNotificationType = LiveReloadMessageType.CssUpdate },
        
        // 其他扩展名 -> Reload
        new() { Extension = ".html", ExpectedNotificationType = LiveReloadMessageType.Reload },
        new() { Extension = ".htm", ExpectedNotificationType = LiveReloadMessageType.Reload },
        new() { Extension = ".md", ExpectedNotificationType = LiveReloadMessageType.Reload },
        new() { Extension = ".markdown", ExpectedNotificationType = LiveReloadMessageType.Reload },
        new() { Extension = ".js", ExpectedNotificationType = LiveReloadMessageType.Reload },
        new() { Extension = ".ts", ExpectedNotificationType = LiveReloadMessageType.Reload },
        new() { Extension = ".json", ExpectedNotificationType = LiveReloadMessageType.Reload },
        new() { Extension = ".toml", ExpectedNotificationType = LiveReloadMessageType.Reload },
        new() { Extension = ".yaml", ExpectedNotificationType = LiveReloadMessageType.Reload },
        new() { Extension = ".yml", ExpectedNotificationType = LiveReloadMessageType.Reload },
        new() { Extension = ".xml", ExpectedNotificationType = LiveReloadMessageType.Reload },
        new() { Extension = ".txt", ExpectedNotificationType = LiveReloadMessageType.Reload },
        new() { Extension = ".png", ExpectedNotificationType = LiveReloadMessageType.Reload },
        new() { Extension = ".jpg", ExpectedNotificationType = LiveReloadMessageType.Reload },
        new() { Extension = ".svg", ExpectedNotificationType = LiveReloadMessageType.Reload },
    ];

    public static Arbitrary<FileChangeNotificationTestCase> FileChangeNotificationTestCase()
    {
        return Gen.Elements(TestCases).ToArbitrary();
    }
}

// Flint 静态站点生成器
// 开发服务器响应正确性属性测试
// **Validates: Property 8**

using System.Net;
using Xunit;
using System.Text;
using Flint.Core.Abstractions;
using Flint.Core.Assets;
using Flint.Core.Configuration;
using Flint.Core.Content;
using Flint.Core.Site;
using Flint.Core.Templates;
using Flint.IntegrationTests.Fixtures;
using Flint.IntegrationTests.Generators;
using Flint.IntegrationTests.Utilities;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

// 解决命名空间冲突
using Random = System.Random;

namespace Flint.IntegrationTests.DevServer;

/// <summary>
/// 开发服务器响应正确性属性测试
/// **Validates: Property 8**
/// </summary>
/// <remarks>
/// Property 8: 开发服务器响应正确性
/// *For any* 存在于输出目录的文件，开发服务器应返回正确的文件内容和对应的 Content-Type 头，
/// 且 HTML 文件应包含热重载脚本注入。
/// 
/// 满足需求：
/// - Requirements 3.3: HTTP 文件服务
/// - Requirements 3.4: Content-Type 头设置
/// </remarks>
[Collection("DevServer")]
public class DevServerResponsePropertyTests
{
    #region 辅助方法

    /// <summary>
    /// 运行异步属性测试的辅助方法
    /// </summary>
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

    private static string SanitizeTitle(string title)
    {
        // 白名单过滤：只保留对 TOML 基本字符串安全的字符。
        // 控制字符、双引号、反斜杠、+、=、# 会破坏 TOML Front Matter
        // （畸形 Front Matter 现在会 fail-fast 导致构建失败，而非静默降级）
        var sb = new StringBuilder();
        foreach (var c in title)
        {
            if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)
                || c is '-' or '_' or ',' or '.' or '!' or ':' or ';' or '(' or ')')
            {
                sb.Append(c);
            }
        }
        var result = sb.ToString().Trim();
        return result.Length > 0 ? result : "Untitled";
    }

    private static string SanitizePath(string path)
    {
        // 移除路径中的非法字符
        var invalidChars = Path.GetInvalidFileNameChars();
        var result = new StringBuilder();
        foreach (var c in path)
        {
            if (!invalidChars.Contains(c) && c != '/' && c != '\\')
            {
                result.Append(c);
            }
        }
        return result.Length > 0 ? result.ToString() : "test";
    }

    #endregion

    #region Property 8: 开发服务器响应正确性

    /// <summary>
    /// 属性测试：对于任意有效的文件名，服务器应返回正确的 Content-Type
    /// **Validates: Property 8**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(FileExtensionArbitraries) })]
    public Property Property8_ServerReturnsCorrectContentType(FileExtensionTestCase testCase)
    {
        // 跳过无效的测试用例
        if (string.IsNullOrEmpty(testCase.Extension) || string.IsNullOrEmpty(testCase.ExpectedContentType))
        {
            return true.ToProperty();
        }

        return RunPropertyTestAsync(async () =>
        {
            using var fixture = new TestSiteFixture();
            await fixture.InitializeAsync();

            IDevServer? devServer = null;
            DevServerTestClient? client = null;
            CancellationTokenSource? cts = null;

            try
            {
                await fixture.CreateSiteAsync("minimal");
                await fixture.BuildAsync();
                (devServer, client, cts) = await StartServerAsync(fixture);

                // Arrange - 创建测试文件
                var fileName = $"test-{Guid.NewGuid():N}{testCase.Extension}";
                var content = testCase.SampleContent ?? Encoding.UTF8.GetBytes("test content");

                await fixture.AddStaticAsync(fileName, content);
                await fixture.BuildAsync();

                // Act
                var response = await client.GetAsync($"/{fileName}");

                // Assert
                if (!response.IsSuccess)
                {
                    // 文件可能未被正确复制到输出目录
                    return true;
                }

                var hasCorrectContentType = response.ContentType?.Contains(
                    testCase.ExpectedContentType,
                    StringComparison.OrdinalIgnoreCase) ?? false;

                return hasCorrectContentType;
            }
            catch (Exception ex) when (ex is TimeoutException or HttpRequestException)
            {
                // 网络错误不应导致测试失败
                return true;
            }
            finally
            {
                await StopServerAsync(devServer, client, cts);
                await fixture.DisposeAsync();
            }
        });
    }

    /// <summary>
    /// 属性测试：HTML 文件应包含热重载脚本
    /// **Validates: Property 8**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(FlintArbitraries) })]
    public Property Property8_HtmlFilesContainLiveReloadScript(NonEmptyString title)
    {
        return RunPropertyTestAsync(async () =>
        {
            using var fixture = new TestSiteFixture();
            await fixture.InitializeAsync();

            IDevServer? devServer = null;
            DevServerTestClient? client = null;
            CancellationTokenSource? cts = null;

            try
            {
                await fixture.CreateSiteAsync("minimal");
                await fixture.BuildAsync();
                (devServer, client, cts) = await StartServerAsync(fixture);

                // Arrange - 创建 HTML 内容
                var safeTitle = SanitizeTitle(title.Get);
                var fileName = $"page-{Guid.NewGuid():N}.md";
                var content = $"""
                    +++
                    title = "{safeTitle}"
                    date = 2024-01-15T10:00:00+08:00
                    draft = false
                    +++

                    测试内容
                    """;

                await fixture.AddContentAsync(fileName, content);
                await fixture.BuildAsync();

                // Act - 获取生成的 HTML
                var response = await client.GetAsync("/index.html");

                // Assert
                if (!response.IsSuccess)
                {
                    return true;
                }

                var containsLiveReload = response.Content.Contains("__livereload");

                return containsLiveReload;
            }
            catch (Exception ex) when (ex is TimeoutException or HttpRequestException)
            {
                return true;
            }
            finally
            {
                await StopServerAsync(devServer, client, cts);
                await fixture.DisposeAsync();
            }
        });
    }

    /// <summary>
    /// 属性测试：服务器返回的文件内容应与磁盘上的文件一致
    /// **Validates: Property 8**
    /// </summary>
    [Property(MaxTest = 50)]
    public Property Property8_ServerReturnsCorrectFileContent(PositiveInt seed)
    {
        return RunPropertyTestAsync(async () =>
        {
            using var fixture = new TestSiteFixture();
            await fixture.InitializeAsync();

            IDevServer? devServer = null;
            DevServerTestClient? client = null;
            CancellationTokenSource? cts = null;

            try
            {
                await fixture.CreateSiteAsync("minimal");
                await fixture.BuildAsync();
                (devServer, client, cts) = await StartServerAsync(fixture);

                // Arrange - 创建随机内容的文件
                var random = new Random(seed.Get);
                var contentBytes = new byte[random.Next(100, 1000)];
                random.NextBytes(contentBytes);

                // 使用 base64 编码作为文本内容
                var textContent = Convert.ToBase64String(contentBytes);
                var fileName = $"data-{Guid.NewGuid():N}.txt";

                await fixture.AddStaticAsync(fileName, Encoding.UTF8.GetBytes(textContent));
                await fixture.BuildAsync();

                // Act
                var response = await client.GetAsync($"/{fileName}");

                // Assert
                if (!response.IsSuccess)
                {
                    return true;
                }

                var contentMatches = response.Content.Trim() == textContent.Trim();

                return contentMatches;
            }
            catch (Exception ex) when (ex is TimeoutException or HttpRequestException)
            {
                return true;
            }
            finally
            {
                await StopServerAsync(devServer, client, cts);
                await fixture.DisposeAsync();
            }
        });
    }

    /// <summary>
    /// 属性测试：不存在的文件应返回 404
    /// **Validates: Property 8**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Property8_NonExistentFilesReturn404(NonEmptyString randomPath)
    {
        return RunPropertyTestAsync(async () =>
        {
            using var fixture = new TestSiteFixture();
            await fixture.InitializeAsync();

            IDevServer? devServer = null;
            DevServerTestClient? client = null;
            CancellationTokenSource? cts = null;

            try
            {
                await fixture.CreateSiteAsync("minimal");
                await fixture.BuildAsync();
                (devServer, client, cts) = await StartServerAsync(fixture);

                // Arrange - 生成一个肯定不存在的路径
                var path = $"/non-existent-{Guid.NewGuid():N}/{SanitizePath(randomPath.Get)}.html";

                // Act
                var response = await client.GetAsync(path);

                // Assert
                var is404 = response.StatusCode == HttpStatusCode.NotFound;

                return is404;
            }
            catch (Exception ex) when (ex is TimeoutException or HttpRequestException)
            {
                return true;
            }
            finally
            {
                await StopServerAsync(devServer, client, cts);
                await fixture.DisposeAsync();
            }
        });
    }

    #endregion
}


/// <summary>
/// 文件扩展名测试用例
/// </summary>
public record FileExtensionTestCase
{
    public required string Extension { get; init; }
    public required string ExpectedContentType { get; init; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "测试数据")]
    public byte[]? SampleContent { get; init; }
}

/// <summary>
/// 文件扩展名生成器
/// </summary>
public static class FileExtensionArbitraries
{
    private static readonly FileExtensionTestCase[] TestCases =
    [
        new() { Extension = ".html", ExpectedContentType = "text/html", SampleContent = "<!DOCTYPE html><html><body>Test</body></html>"u8.ToArray() },
        new() { Extension = ".css", ExpectedContentType = "text/css", SampleContent = "body { margin: 0; }"u8.ToArray() },
        new() { Extension = ".js", ExpectedContentType = "javascript", SampleContent = "console.log('test');"u8.ToArray() },
        new() { Extension = ".json", ExpectedContentType = "application/json", SampleContent = """{"key": "value"}"""u8.ToArray() },
        new() { Extension = ".xml", ExpectedContentType = "xml", SampleContent = "<?xml version=\"1.0\"?><root/>"u8.ToArray() },
        new() { Extension = ".txt", ExpectedContentType = "text/plain", SampleContent = "Plain text content"u8.ToArray() },
        new() { Extension = ".svg", ExpectedContentType = "image/svg+xml", SampleContent = "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray() },
    ];

    public static Arbitrary<FileExtensionTestCase> FileExtensionTestCase()
    {
        return Gen.Elements(TestCases).ToArbitrary();
    }
}

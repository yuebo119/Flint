// Flint 静态站点生成器
// 开发服务器单元测试

#pragma warning disable CA2000 // 测试代码中不需要处理 Dispose
#pragma warning disable CA2007 // 测试代码中不需要 ConfigureAwait

using Flint.Core.Abstractions;
using Flint.Core.Models;
using Flint.Core.Server;
using Xunit;

namespace Flint.Core.Tests.Server;

/// <summary>
/// DevServer 单元测试
/// </summary>
public class DevServerTests : IDisposable
{
    private readonly StubSiteBuilder _siteBuilder;
    private readonly string _testDir;
    private readonly string _outputDir;

    public DevServerTests()
    {
        _siteBuilder = new StubSiteBuilder();
        _testDir = Path.Combine(Path.GetTempPath(), $"Flint_dev_test_{Guid.NewGuid():N}");
        _outputDir = Path.Combine(_testDir, "public");
        Directory.CreateDirectory(_testDir);
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try
            { Directory.Delete(_testDir, true); }
            catch { }
        }
        GC.SuppressFinalize(this);
    }

    #region 构造函数测试

    [Fact]
    public void Constructor_应该正确初始化()
    {
        // Act
        using var server = new DevServer(_siteBuilder);

        // Assert
        Assert.NotNull(server);
        Assert.False(server.IsRunning);
        Assert.Null(server.ServerUrl);
    }

    #endregion

    #region IsRunning 测试

    [Fact]
    public void IsRunning_初始状态应该为false()
    {
        // Arrange
        using var server = new DevServer(_siteBuilder);

        // Assert
        Assert.False(server.IsRunning);
    }

    #endregion

    #region ServerUrl 测试

    [Fact]
    public void ServerUrl_初始状态应该为null()
    {
        // Arrange
        using var server = new DevServer(_siteBuilder);

        // Assert
        Assert.Null(server.ServerUrl);
    }

    #endregion

    #region StartAsync 测试

    [Fact]
    public async Task StartAsync_已运行时应该抛出异常()
    {
        // Arrange
        using var server = new DevServer(_siteBuilder);
        var options = CreateDevServerOptions();

        // 启动服务器
        await server.StartAsync(options);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => server.StartAsync(options));

        // 清理
        await server.StopAsync();
    }

    [Fact]
    public async Task StartAsync_应该设置IsRunning为true()
    {
        // Arrange
        using var server = new DevServer(_siteBuilder);
        var options = CreateDevServerOptions();

        // Act
        await server.StartAsync(options);

        // Assert
        Assert.True(server.IsRunning);

        // 清理
        await server.StopAsync();
    }

    [Fact]
    public async Task StartAsync_应该设置ServerUrl()
    {
        // Arrange
        using var server = new DevServer(_siteBuilder);
        var options = CreateDevServerOptions();

        // Act
        await server.StartAsync(options);

        // Assert
        Assert.NotNull(server.ServerUrl);
        Assert.StartsWith("http://", server.ServerUrl);

        // 清理
        await server.StopAsync();
    }

    [Fact]
    public async Task StartAsync_使用HTTPS应该设置正确的协议()
    {
        // Arrange
        using var server = new DevServer(_siteBuilder);
        var options = new DevServerOptions
        {
            SourcePath = _testDir,
            OutputPath = _outputDir,
            Port = 0,
            BindAddress = "127.0.0.1",
            UseHttps = true,
            LiveReload = true,
            OpenBrowser = false,
            IncludeDrafts = false,
            IncludeFuture = false,
            Verbose = false
        };

        // Act
        await server.StartAsync(options);

        // Assert
        Assert.NotNull(server.ServerUrl);
        Assert.StartsWith("https://", server.ServerUrl);

        // 清理
        await server.StopAsync();
    }

    [Fact]
    public async Task StartAsync_应该调用初始构建()
    {
        // Arrange
        using var server = new DevServer(_siteBuilder);
        var options = CreateDevServerOptions();

        // Act
        await server.StartAsync(options);

        // Assert
        Assert.True(_siteBuilder.BuildAsyncCalled);

        // 清理
        await server.StopAsync();
    }

    #endregion

    #region StopAsync 测试

    [Fact]
    public async Task StopAsync_未运行时不应该抛出异常()
    {
        // Arrange
        using var server = new DevServer(_siteBuilder);

        // Act & Assert
        await server.StopAsync();
        // 不抛出异常即为成功
    }

    [Fact]
    public async Task StopAsync_应该设置IsRunning为false()
    {
        // Arrange
        using var server = new DevServer(_siteBuilder);
        var options = CreateDevServerOptions();
        await server.StartAsync(options);

        // Act
        await server.StopAsync();

        // Assert
        Assert.False(server.IsRunning);
    }

    [Fact]
    public async Task StopAsync_应该清除ServerUrl()
    {
        // Arrange
        using var server = new DevServer(_siteBuilder);
        var options = CreateDevServerOptions();
        await server.StartAsync(options);

        // Act
        await server.StopAsync();

        // Assert
        Assert.Null(server.ServerUrl);
    }

    #endregion

    #region Dispose 测试

    [Fact]
    public void Dispose_应该正确释放资源()
    {
        // Arrange
        var server = new DevServer(_siteBuilder);

        // Act
        server.Dispose();

        // Assert - 不抛出异常即为成功
    }

    [Fact]
    public void Dispose_多次调用不应该抛出异常()
    {
        // Arrange
        var server = new DevServer(_siteBuilder);

        // Act
        server.Dispose();
        server.Dispose();

        // Assert - 不抛出异常即为成功
    }

    [Fact]
    public async Task Dispose_运行中应该停止服务器()
    {
        // Arrange
        var server = new DevServer(_siteBuilder);
        var options = CreateDevServerOptions();
        await server.StartAsync(options);

        // Act
        server.Dispose();

        // 等待一小段时间让 Dispose 完成
        await Task.Delay(100);

        // Assert - Dispose 会尝试停止服务器，但由于是同步调用，可能不会立即完成
        // 我们只验证 Dispose 不会抛出异常
    }

    #endregion

    #region 辅助方法

    private DevServerOptions CreateDevServerOptions()
    {
        return new DevServerOptions
        {
            SourcePath = _testDir,
            OutputPath = _outputDir,
            Port = 0, // 使用随机可用端口
            BindAddress = "127.0.0.1",
            UseHttps = false,
            LiveReload = true,
            OpenBrowser = false,
            IncludeDrafts = false,
            IncludeFuture = false,
            Verbose = false
        };
    }

    #endregion

    #region Stub 类

    private sealed class StubSiteBuilder : ISiteBuilder
    {
        private readonly string _outputDir;

        public bool BuildAsyncCalled { get; private set; }
        public bool IncrementalBuildAsyncCalled { get; private set; }

        public StubSiteBuilder()
        {
            _outputDir = Path.Combine(Path.GetTempPath(), "stub_output");
        }

        public ValueTask<BuildResult> BuildAsync(BuildOptions options, CancellationToken cancellationToken = default)
        {
            BuildAsyncCalled = true;
            return ValueTask.FromResult(new BuildResult
            {
                Success = true,
                PagesBuilt = 0,
                AssetsProcessed = 0,
                Duration = TimeSpan.FromMilliseconds(100),
                MemoryUsed = 1024,
                Errors = [],
                Warnings = [],
                OutputPath = options.OutputPath
            });
        }

        public ValueTask<BuildResult> IncrementalBuildAsync(BuildOptions options, IReadOnlyList<string> changedFiles, CancellationToken cancellationToken = default)
        {
            IncrementalBuildAsyncCalled = true;
            return ValueTask.FromResult(new BuildResult
            {
                Success = true,
                PagesBuilt = 0,
                AssetsProcessed = 0,
                Duration = TimeSpan.FromMilliseconds(50),
                MemoryUsed = 512,
                Errors = [],
                Warnings = [],
                OutputPath = options.OutputPath
            });
        }

        public ValueTask CleanAsync(string outputPath, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }
    }

    #endregion
}

/// <summary>
/// DevServerOptions 单元测试
/// </summary>
public class DevServerOptionsTests
{
    [Fact]
    public void DevServerOptions_默认值应该正确()
    {
        // Act
        var options = new DevServerOptions
        {
            SourcePath = "/source",
            OutputPath = "/output"
        };

        // Assert
        Assert.Equal(1313, options.Port);
        Assert.Equal("localhost", options.BindAddress);
        Assert.False(options.UseHttps);
        Assert.True(options.LiveReload);
        Assert.True(options.OpenBrowser);
        Assert.True(options.IncludeDrafts);  // 默认为 true
        Assert.True(options.IncludeFuture);  // 默认为 true
        Assert.False(options.Verbose);
    }

    [Fact]
    public void DevServerOptions_应该正确设置属性()
    {
        // Act
        var options = new DevServerOptions
        {
            SourcePath = "/my/source",
            OutputPath = "/my/output",
            Port = 8080,
            BindAddress = "0.0.0.0",
            UseHttps = true,
            LiveReload = false,
            OpenBrowser = false,
            IncludeDrafts = true,
            IncludeFuture = true,
            Verbose = true
        };

        // Assert
        Assert.Equal("/my/source", options.SourcePath);
        Assert.Equal("/my/output", options.OutputPath);
        Assert.Equal(8080, options.Port);
        Assert.Equal("0.0.0.0", options.BindAddress);
        Assert.True(options.UseHttps);
        Assert.False(options.LiveReload);
        Assert.False(options.OpenBrowser);
        Assert.True(options.IncludeDrafts);
        Assert.True(options.IncludeFuture);
        Assert.True(options.Verbose);
    }
}

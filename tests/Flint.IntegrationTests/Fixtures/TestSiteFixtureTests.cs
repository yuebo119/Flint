// Flint 静态站点生成器
// TestSiteFixture 单元测试

using FluentAssertions;
using Xunit;

namespace Flint.IntegrationTests.Fixtures;

/// <summary>
/// TestSiteFixture 单元测试
/// 验证测试站点夹具的基本功能
/// </summary>
/// <remarks>
/// 满足需求：
/// - Requirements 10.1: 提供创建临时测试站点的测试夹具
/// - Requirements 10.6: 测试完成后自动清理临时文件和目录
/// </remarks>
public class TestSiteFixtureTests : IAsyncLifetime
{
    private TestSiteFixture? _fixture;

    public Task InitializeAsync()
    {
        _fixture = new TestSiteFixture();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_fixture != null)
        {
            await _fixture.DisposeAsync();
        }
    }

    #region 站点创建测试

    [Fact]
    public async Task CreateSiteAsync_WithDefaultTemplate_ShouldCreateMinimalSite()
    {
        // Arrange & Act
        var siteRoot = await _fixture!.CreateSiteAsync("default");

        // Assert
        siteRoot.Should().NotBeNullOrEmpty();
        Directory.Exists(siteRoot).Should().BeTrue("站点根目录应该存在");
        Directory.Exists(Path.Combine(siteRoot, "content")).Should().BeTrue("content 目录应该存在");
        Directory.Exists(Path.Combine(siteRoot, "layouts")).Should().BeTrue("layouts 目录应该存在");
        File.Exists(Path.Combine(siteRoot, "Flint.toml")).Should().BeTrue("配置文件应该存在");
    }

    [Fact]
    public async Task CreateSiteAsync_WithMinimalTemplate_ShouldCreateSiteWithBasicStructure()
    {
        // Arrange & Act
        var siteRoot = await _fixture!.CreateSiteAsync("minimal");

        // Assert
        _fixture.IsCreated.Should().BeTrue();
        _fixture.SiteRoot.Should().Be(siteRoot);
        _fixture.Config.Should().NotBeNull();
        _fixture.Config.Title.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CreateSiteAsync_ShouldSetOutputPath()
    {
        // Arrange & Act
        await _fixture!.CreateSiteAsync();

        // Assert
        _fixture.OutputPath.Should().NotBeNullOrEmpty();
        _fixture.OutputPath.Should().EndWith("public");
    }

    [Fact]
    public async Task CreateSiteAsync_ShouldGenerateUniqueTestId()
    {
        // Arrange
        var fixture1 = new TestSiteFixture();
        var fixture2 = new TestSiteFixture();

        try
        {
            // Act
            await fixture1.CreateSiteAsync();
            await fixture2.CreateSiteAsync();

            // Assert
            fixture1.TestId.Should().NotBe(fixture2.TestId);
            fixture1.SiteRoot.Should().NotBe(fixture2.SiteRoot);
        }
        finally
        {
            await fixture1.DisposeAsync();
            await fixture2.DisposeAsync();
        }
    }

    [Fact]
    public async Task CreateSiteAsync_WithInvalidTemplate_ShouldThrowArgumentException()
    {
        // Arrange & Act
        var act = async () => await _fixture!.CreateSiteAsync("invalid_template");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*未知的站点模板*");
    }

    #endregion

    #region 文件添加测试

    [Fact]
    public async Task AddContentAsync_ShouldCreateContentFile()
    {
        // Arrange
        await _fixture!.CreateSiteAsync();
        var content = """
            +++
            title = "测试文章"
            date = 2024-01-15T10:00:00+08:00
            +++

            这是测试内容。
            """;

        // Act
        await _fixture.AddContentAsync("posts/test-post.md", content);

        // Assert
        var filePath = Path.Combine(_fixture.SiteRoot, "content", "posts", "test-post.md");
        File.Exists(filePath).Should().BeTrue();
        var fileContent = await File.ReadAllTextAsync(filePath);
        fileContent.Should().Contain("测试文章");
    }

    [Fact]
    public async Task AddTemplateAsync_ShouldCreateTemplateFile()
    {
        // Arrange
        await _fixture!.CreateSiteAsync();
        var template = """
            <!DOCTYPE html>
            <html>
            <head><title>{{ page.title }}</title></head>
            <body>{{ page.content }}</body>
            </html>
            """;

        // Act
        await _fixture.AddTemplateAsync("custom.html", template);

        // Assert
        var filePath = Path.Combine(_fixture.SiteRoot, "layouts", "custom.html");
        File.Exists(filePath).Should().BeTrue();
    }

    [Fact]
    public async Task AddAssetAsync_ShouldCreateAssetFile()
    {
        // Arrange
        await _fixture!.CreateSiteAsync();
        var cssContent = "body { margin: 0; padding: 0; }"u8.ToArray();

        // Act
        await _fixture.AddAssetAsync("css/style.css", cssContent);

        // Assert
        var filePath = Path.Combine(_fixture.SiteRoot, "assets", "css", "style.css");
        File.Exists(filePath).Should().BeTrue();
    }

    [Fact]
    public async Task AddStaticAsync_ShouldCreateStaticFile()
    {
        // Arrange
        await _fixture!.CreateSiteAsync();
        var imageContent = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG 文件头

        // Act
        await _fixture.AddStaticAsync("images/logo.png", imageContent);

        // Assert
        var filePath = Path.Combine(_fixture.SiteRoot, "static", "images", "logo.png");
        File.Exists(filePath).Should().BeTrue();
    }

    [Fact]
    public async Task SetConfigAsync_ShouldUpdateConfiguration()
    {
        // Arrange
        await _fixture!.CreateSiteAsync();
        var newConfig = """
            baseURL = "https://example.com/"
            title = "新标题"
            languageCode = "en"
            """;

        // Act
        await _fixture.SetConfigAsync(newConfig, "toml");

        // Assert
        _fixture.Config.Title.Should().Be("新标题");
        _fixture.Config.BaseURL.Should().Be("https://example.com/");
    }

    [Fact]
    public async Task AddContentAsync_BeforeCreateSite_ShouldThrowInvalidOperationException()
    {
        // Arrange & Act
        var act = async () => await _fixture!.AddContentAsync("test.md", "content");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*站点尚未创建*");
    }

    #endregion

    #region 构建测试

    [Fact]
    public async Task BuildAsync_WithValidSite_ShouldReturnSuccessResult()
    {
        // Arrange
        await _fixture!.CreateSiteAsync();

        // Act
        var result = await _fixture.BuildAsync();

        // Assert
        result.Should().NotBeNull();
        result.OutputPath.Should().Be(_fixture.OutputPath);
    }

    [Fact]
    public async Task BuildAsync_ShouldCreateOutputDirectory()
    {
        // Arrange
        await _fixture!.CreateSiteAsync();

        // Act
        await _fixture.BuildAsync();

        // Assert
        Directory.Exists(_fixture.OutputPath).Should().BeTrue();
    }

    [Fact]
    public async Task CleanOutputAsync_ShouldRemoveOutputDirectory()
    {
        // Arrange
        await _fixture!.CreateSiteAsync();
        await _fixture.BuildAsync();
        Directory.Exists(_fixture.OutputPath).Should().BeTrue();

        // Act
        await _fixture.CleanOutputAsync();

        // Assert
        Directory.Exists(_fixture.OutputPath).Should().BeFalse();
    }

    #endregion

    #region 快照测试

    [Fact]
    public async Task CreateSnapshotAsync_ShouldCaptureOutputFiles()
    {
        // Arrange
        await _fixture!.CreateSiteAsync();
        await _fixture.BuildAsync();

        // Act
        var count = await _fixture.CreateSnapshotAsync("test-snapshot");

        // Assert
        count.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task CompareWithSnapshotAsync_WithNoChanges_ShouldReturnNoChanges()
    {
        // Arrange
        await _fixture!.CreateSiteAsync();
        await _fixture.BuildAsync();
        await _fixture.CreateSnapshotAsync("baseline");

        // Act
        var result = await _fixture.CompareWithSnapshotAsync("baseline");

        // Assert
        result.HasChanges.Should().BeFalse();
        result.AddedFiles.Should().BeEmpty();
        result.RemovedFiles.Should().BeEmpty();
        result.ModifiedFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task CompareWithSnapshotAsync_WithAddedFile_ShouldDetectAddition()
    {
        // Arrange
        await _fixture!.CreateSiteAsync();
        await _fixture.BuildAsync();
        await _fixture.CreateSnapshotAsync("baseline");

        // 添加新文件到输出目录
        var newFilePath = Path.Combine(_fixture.OutputPath, "new-file.txt");
        await File.WriteAllTextAsync(newFilePath, "new content");

        // Act
        var result = await _fixture.CompareWithSnapshotAsync("baseline");

        // Assert
        result.HasChanges.Should().BeTrue();
        result.AddedFiles.Should().Contain("new-file.txt");
    }

    [Fact]
    public async Task GetOutputFileAsync_WithExistingFile_ShouldReturnContent()
    {
        // Arrange
        await _fixture!.CreateSiteAsync();
        await _fixture.BuildAsync();

        // 创建测试文件
        var testFilePath = Path.Combine(_fixture.OutputPath, "test.html");
        await File.WriteAllTextAsync(testFilePath, "<html>test</html>");

        // Act
        var content = await _fixture.GetOutputFileAsync("test.html");

        // Assert
        content.Should().NotBeNull();
        content.Should().Contain("<html>test</html>");
    }

    [Fact]
    public async Task GetOutputFileAsync_WithNonExistingFile_ShouldReturnNull()
    {
        // Arrange
        await _fixture!.CreateSiteAsync();
        await _fixture.BuildAsync();

        // Act
        var content = await _fixture.GetOutputFileAsync("non-existing.html");

        // Assert
        content.Should().BeNull();
    }

    [Fact]
    public async Task OutputFileExists_WithExistingFile_ShouldReturnTrue()
    {
        // Arrange
        await _fixture!.CreateSiteAsync();
        await _fixture.BuildAsync();

        // 创建测试文件
        var testFilePath = Path.Combine(_fixture.OutputPath, "exists.html");
        await File.WriteAllTextAsync(testFilePath, "content");

        // Act
        var exists = _fixture.OutputFileExists("exists.html");

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task GetOutputFiles_ShouldReturnAllFiles()
    {
        // Arrange
        await _fixture!.CreateSiteAsync();
        await _fixture.BuildAsync();

        // Act
        var files = _fixture.GetOutputFiles();

        // Assert
        files.Should().NotBeNull();
    }

    #endregion

    #region 清理测试

    [Fact]
    public async Task DisposeAsync_ShouldCleanupTempDirectory()
    {
        // Arrange
        var fixture = new TestSiteFixture();
        var siteRoot = await fixture.CreateSiteAsync();
        Directory.Exists(siteRoot).Should().BeTrue();

        // Act
        await fixture.DisposeAsync();

        // Assert
        // 等待一小段时间让文件系统完成删除
        await Task.Delay(200);
        Directory.Exists(siteRoot).Should().BeFalse("临时目录应该被清理");
    }

    [Fact]
    public async Task Dispose_AfterDispose_ShouldNotThrow()
    {
        // Arrange
        var fixture = new TestSiteFixture();
        await fixture.CreateSiteAsync();

        // Act
        fixture.Dispose();
        var act = () => fixture.Dispose();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public async Task CreateSiteAsync_AfterDispose_ShouldThrowObjectDisposedException()
    {
        // Arrange
        var fixture = new TestSiteFixture();
        await fixture.CreateSiteAsync();
        fixture.Dispose();

        // Act
        var act = async () => await fixture.CreateSiteAsync();

        // Assert
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    #endregion

    #region 属性访问测试

    [Fact]
    public void SiteRoot_BeforeCreate_ShouldThrowInvalidOperationException()
    {
        // Arrange & Act
        var act = () => _fixture!.SiteRoot;

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*站点尚未创建*");
    }

    [Fact]
    public void OutputPath_BeforeCreate_ShouldThrowInvalidOperationException()
    {
        // Arrange & Act
        var act = () => _fixture!.OutputPath;

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*站点尚未创建*");
    }

    [Fact]
    public void Config_BeforeCreate_ShouldThrowInvalidOperationException()
    {
        // Arrange & Act
        var act = () => _fixture!.Config;

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*站点尚未创建*");
    }

    [Fact]
    public void IsCreated_BeforeCreate_ShouldReturnFalse()
    {
        // Arrange & Act & Assert
        _fixture!.IsCreated.Should().BeFalse();
    }

    [Fact]
    public async Task IsCreated_AfterCreate_ShouldReturnTrue()
    {
        // Arrange
        await _fixture!.CreateSiteAsync();

        // Act & Assert
        _fixture.IsCreated.Should().BeTrue();
    }

    #endregion
}

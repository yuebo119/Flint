// Flint 静态站点生成器
// ErrorInjector 单元测试

using FluentAssertions;
using Xunit;

namespace Flint.IntegrationTests.ErrorInjection;

/// <summary>
/// ErrorInjector 单元测试
/// 验证错误注入的正确性和恢复功能
/// </summary>
public class ErrorInjectorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ErrorInjector _injector;

    public ErrorInjectorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"Flint-error-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _injector = new ErrorInjector();
    }

    public void Dispose()
    {
        _injector.Dispose();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                // 确保所有文件可写
                foreach (var file in Directory.GetFiles(_tempDir, "*", SearchOption.AllDirectories))
                {
                    var fileInfo = new FileInfo(file);
                    fileInfo.IsReadOnly = false;
                }
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // 忽略清理错误
        }
        GC.SuppressFinalize(this);
    }

    #region 无效 UTF-8 注入测试

    [Fact]
    public async Task InjectInvalidUtf8Async_ShouldCreateFileWithInvalidUtf8()
    {
        // Arrange
        var filePath = Path.Combine(_tempDir, "invalid-utf8.txt");

        // Act
        var result = await _injector.InjectInvalidUtf8Async(filePath);

        // Assert
        result.Success.Should().BeTrue();
        result.Type.Should().Be(ErrorInjectionType.Encoding);
        File.Exists(filePath).Should().BeTrue();

        // 验证文件包含无效 UTF-8
        var bytes = await File.ReadAllBytesAsync(filePath);
        bytes.Should().Contain(0xFF);
    }

    [Fact]
    public async Task InjectInvalidUtf8Async_WithExistingFile_ShouldPreserveOriginalContent()
    {
        // Arrange
        var filePath = Path.Combine(_tempDir, "existing.txt");
        var originalContent = "Original content";
        await File.WriteAllTextAsync(filePath, originalContent);

        // Act
        var result = await _injector.InjectInvalidUtf8Async(filePath);

        // Assert
        result.Success.Should().BeTrue();
        result.OriginalContent.Should().NotBeNull();
    }

    #endregion

    #region 截断文件注入测试

    [Fact]
    public async Task InjectTruncatedFileAsync_ShouldTruncateFile()
    {
        // Arrange
        var filePath = Path.Combine(_tempDir, "truncate.txt");
        var originalContent = "This is a test file with some content that will be truncated.";
        await File.WriteAllTextAsync(filePath, originalContent);
        var originalLength = new FileInfo(filePath).Length;

        // Act
        var result = await _injector.InjectTruncatedFileAsync(filePath, 10);

        // Assert
        result.Success.Should().BeTrue();
        result.Type.Should().Be(ErrorInjectionType.DataCorruption);
        var newLength = new FileInfo(filePath).Length;
        newLength.Should().BeLessThan(originalLength);
    }

    [Fact]
    public async Task InjectTruncatedFileAsync_WithNonExistentFile_ShouldReturnFailure()
    {
        // Arrange
        var filePath = Path.Combine(_tempDir, "nonexistent.txt");

        // Act
        var result = await _injector.InjectTruncatedFileAsync(filePath);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("不存在");
    }

    #endregion

    #region 损坏的配置文件注入测试

    [Fact]
    public async Task InjectCorruptedJsonAsync_ShouldCreateInvalidJson()
    {
        // Arrange
        var filePath = Path.Combine(_tempDir, "config.json");

        // Act
        var result = await _injector.InjectCorruptedJsonAsync(filePath);

        // Assert
        result.Success.Should().BeTrue();
        result.Type.Should().Be(ErrorInjectionType.DataCorruption);

        var content = await File.ReadAllTextAsync(filePath);
        content.Should().Contain("invalid");
    }

    [Fact]
    public async Task InjectCorruptedYamlAsync_ShouldCreateInvalidYaml()
    {
        // Arrange
        var filePath = Path.Combine(_tempDir, "config.yaml");

        // Act
        var result = await _injector.InjectCorruptedYamlAsync(filePath);

        // Assert
        result.Success.Should().BeTrue();
        result.Type.Should().Be(ErrorInjectionType.DataCorruption);

        var content = await File.ReadAllTextAsync(filePath);
        content.Should().Contain("invalid");
    }

    [Fact]
    public async Task InjectCorruptedTomlAsync_ShouldCreateInvalidToml()
    {
        // Arrange
        var filePath = Path.Combine(_tempDir, "config.toml");

        // Act
        var result = await _injector.InjectCorruptedTomlAsync(filePath);

        // Assert
        result.Success.Should().BeTrue();
        result.Type.Should().Be(ErrorInjectionType.DataCorruption);

        var content = await File.ReadAllTextAsync(filePath);
        content.Should().Contain("invalid");
    }

    #endregion

    #region 内容错误注入测试

    [Fact]
    public async Task InjectInvalidFrontMatterAsync_ShouldCreateInvalidFrontMatter()
    {
        // Arrange
        var filePath = Path.Combine(_tempDir, "post.md");

        // Act
        var result = await _injector.InjectInvalidFrontMatterAsync(filePath);

        // Assert
        result.Success.Should().BeTrue();

        var content = await File.ReadAllTextAsync(filePath);
        content.Should().StartWith("---");
        content.Should().NotContain("---\n---"); // 不应有闭合的 front matter
    }

    [Fact]
    public async Task InjectInvalidTemplateSyntaxAsync_ShouldCreateInvalidTemplate()
    {
        // Arrange
        var filePath = Path.Combine(_tempDir, "template.html");

        // Act
        var result = await _injector.InjectInvalidTemplateSyntaxAsync(filePath);

        // Assert
        result.Success.Should().BeTrue();

        var content = await File.ReadAllTextAsync(filePath);
        content.Should().Contain("{{");
        content.Should().Contain("缺少");
    }

    [Fact]
    public async Task InjectInvalidScssAsync_ShouldCreateInvalidScss()
    {
        // Arrange
        var filePath = Path.Combine(_tempDir, "style.scss");

        // Act
        var result = await _injector.InjectInvalidScssAsync(filePath);

        // Assert
        result.Success.Should().BeTrue();

        var content = await File.ReadAllTextAsync(filePath);
        content.Should().Contain("$");
        content.Should().Contain("undefined");
    }

    #endregion


    #region 文件系统状态注入测试

    [Fact]
    public async Task InjectEmptyFileAsync_ShouldCreateEmptyFile()
    {
        // Arrange
        var filePath = Path.Combine(_tempDir, "empty.txt");
        await File.WriteAllTextAsync(filePath, "Some content");

        // Act
        var result = await _injector.InjectEmptyFileAsync(filePath);

        // Assert
        result.Success.Should().BeTrue();
        var fileInfo = new FileInfo(filePath);
        fileInfo.Length.Should().Be(0);
    }

    [Fact]
    public void InjectReadOnlyFile_ShouldMakeFileReadOnly()
    {
        // Arrange
        var filePath = Path.Combine(_tempDir, "readonly.txt");
        File.WriteAllText(filePath, "Some content");

        // Act
        var result = _injector.InjectReadOnlyFile(filePath);

        // Assert
        result.Success.Should().BeTrue();
        result.Type.Should().Be(ErrorInjectionType.Permission);
        var fileInfo = new FileInfo(filePath);
        fileInfo.IsReadOnly.Should().BeTrue();
    }

    [Fact]
    public void InjectReadOnlyFile_WithNonExistentFile_ShouldReturnFailure()
    {
        // Arrange
        var filePath = Path.Combine(_tempDir, "nonexistent.txt");

        // Act
        var result = _injector.InjectReadOnlyFile(filePath);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("不存在");
    }

    [Fact]
    public async Task InjectMissingFileAsync_ShouldDeleteFile()
    {
        // Arrange
        var filePath = Path.Combine(_tempDir, "todelete.txt");
        await File.WriteAllTextAsync(filePath, "Some content");

        // Act
        var result = await _injector.InjectMissingFileAsync(filePath);

        // Assert
        result.Success.Should().BeTrue();
        result.Type.Should().Be(ErrorInjectionType.FileSystem);
        File.Exists(filePath).Should().BeFalse();
        result.OriginalContent.Should().NotBeNull();
    }

    [Fact]
    public async Task InjectDirectoryInsteadOfFileAsync_ShouldReplaceFileWithDirectory()
    {
        // Arrange
        var filePath = Path.Combine(_tempDir, "file-to-dir.txt");
        await File.WriteAllTextAsync(filePath, "Some content");

        // Act
        var result = await _injector.InjectDirectoryInsteadOfFileAsync(filePath);

        // Assert
        result.Success.Should().BeTrue();
        result.Type.Should().Be(ErrorInjectionType.FileSystem);
        File.Exists(filePath).Should().BeFalse();
        Directory.Exists(filePath).Should().BeTrue();
    }

    #endregion

    #region 恢复测试

    [Fact]
    public async Task RestoreAsync_ShouldRestoreOriginalContent()
    {
        // Arrange
        var filePath = Path.Combine(_tempDir, "restore.txt");
        var originalContent = "Original content to restore";
        await File.WriteAllTextAsync(filePath, originalContent);

        var injection = await _injector.InjectInvalidUtf8Async(filePath);

        // Act
        var restored = await _injector.RestoreAsync(injection);

        // Assert
        restored.Should().BeTrue();
        var restoredContent = await File.ReadAllTextAsync(filePath);
        restoredContent.Should().Be(originalContent);
    }

    [Fact]
    public async Task RestoreAsync_WithReadOnlyFile_ShouldRestorePermissions()
    {
        // Arrange
        var filePath = Path.Combine(_tempDir, "readonly-restore.txt");
        await File.WriteAllTextAsync(filePath, "Some content");

        var injection = _injector.InjectReadOnlyFile(filePath);

        // Act
        var restored = await _injector.RestoreAsync(injection);

        // Assert
        restored.Should().BeTrue();
        var fileInfo = new FileInfo(filePath);
        fileInfo.IsReadOnly.Should().BeFalse();
    }

    [Fact]
    public async Task RestoreAsync_WithMissingFile_ShouldRestoreFile()
    {
        // Arrange
        var filePath = Path.Combine(_tempDir, "missing-restore.txt");
        var originalContent = "Content to restore";
        await File.WriteAllTextAsync(filePath, originalContent);

        var injection = await _injector.InjectMissingFileAsync(filePath);

        // Act
        var restored = await _injector.RestoreAsync(injection);

        // Assert
        restored.Should().BeTrue();
        File.Exists(filePath).Should().BeTrue();
        var restoredContent = await File.ReadAllTextAsync(filePath);
        restoredContent.Should().Be(originalContent);
    }

    [Fact]
    public async Task RestoreAllAsync_ShouldRestoreAllInjections()
    {
        // Arrange
        var file1 = Path.Combine(_tempDir, "file1.txt");
        var file2 = Path.Combine(_tempDir, "file2.txt");
        await File.WriteAllTextAsync(file1, "Content 1");
        await File.WriteAllTextAsync(file2, "Content 2");

        await _injector.InjectInvalidUtf8Async(file1);
        await _injector.InjectEmptyFileAsync(file2);

        // Act
        var restoredCount = await _injector.RestoreAllAsync();

        // Assert
        restoredCount.Should().Be(2);
        (await File.ReadAllTextAsync(file1)).Should().Be("Content 1");
        (await File.ReadAllTextAsync(file2)).Should().Be("Content 2");
    }

    #endregion

    #region 辅助方法测试

    [Fact]
    public void CorruptBinaryData_ShouldModifyData()
    {
        // Arrange
        var original = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

        // Act
        var corrupted = ErrorInjector.CorruptBinaryData(original, 0.5, seed: 12345);

        // Assert
        corrupted.Should().HaveCount(original.Length);
        corrupted.Should().NotEqual(original);
    }

    [Fact]
    public void CorruptBinaryData_WithSeed_ShouldBeDeterministic()
    {
        // Arrange
        var original = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

        // Act
        var corrupted1 = ErrorInjector.CorruptBinaryData(original, 0.5, seed: 12345);
        var corrupted2 = ErrorInjector.CorruptBinaryData(original, 0.5, seed: 12345);

        // Assert
        corrupted1.Should().Equal(corrupted2);
    }

    [Fact]
    public void CorruptTextData_ShouldModifyText()
    {
        // Arrange
        var original = "This is a test string for corruption testing.";

        // Act
        var corrupted = ErrorInjector.CorruptTextData(original, 0.2, seed: 12345);

        // Assert
        corrupted.Should().HaveLength(original.Length);
        corrupted.Should().NotBe(original);
    }

    [Fact]
    public void CorruptTextData_WithSeed_ShouldBeDeterministic()
    {
        // Arrange
        var original = "This is a test string for corruption testing.";

        // Act
        var corrupted1 = ErrorInjector.CorruptTextData(original, 0.2, seed: 12345);
        var corrupted2 = ErrorInjector.CorruptTextData(original, 0.2, seed: 12345);

        // Assert
        corrupted1.Should().Be(corrupted2);
    }

    #endregion

    #region 注入列表测试

    [Fact]
    public async Task Injections_ShouldTrackAllInjections()
    {
        // Arrange
        var file1 = Path.Combine(_tempDir, "track1.txt");
        var file2 = Path.Combine(_tempDir, "track2.txt");

        // Act
        await _injector.InjectInvalidUtf8Async(file1);
        await _injector.InjectCorruptedJsonAsync(file2);

        // Assert
        _injector.Injections.Should().HaveCount(2);
        _injector.Injections.Should().Contain(i => i.TargetPath == file1);
        _injector.Injections.Should().Contain(i => i.TargetPath == file2);
    }

    #endregion

    #region 枚举测试

    [Fact]
    public void ErrorInjectionType_ShouldHaveAllExpectedValues()
    {
        // Assert
        Enum.GetValues<ErrorInjectionType>().Should().HaveCount(4);
        Enum.IsDefined(ErrorInjectionType.FileSystem).Should().BeTrue();
        Enum.IsDefined(ErrorInjectionType.DataCorruption).Should().BeTrue();
        Enum.IsDefined(ErrorInjectionType.Permission).Should().BeTrue();
        Enum.IsDefined(ErrorInjectionType.Encoding).Should().BeTrue();
    }

    #endregion

    #region ErrorInjectionResult 测试

    [Fact]
    public void ErrorInjectionResult_ShouldHaveDefaultValues()
    {
        // Arrange & Act
        var result = new ErrorInjectionResult();

        // Assert
        result.Success.Should().BeFalse();
        result.Type.Should().Be(ErrorInjectionType.FileSystem);
        result.TargetPath.Should().BeEmpty();
        result.ErrorMessage.Should().BeNull();
        result.OriginalContent.Should().BeNull();
    }

    #endregion
}

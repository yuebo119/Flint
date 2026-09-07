// Flint 静态站点生成器
// 数据文件加载器单元测试

using Flint.Core.Site;
using Xunit;

namespace Flint.Core.Tests.Site;

/// <summary>
/// DataFileLoader 单元测试
/// </summary>
public class DataFileLoaderTests : IDisposable
{
    private readonly string _testDir;
    private readonly DataFileLoader _loader;

    public DataFileLoaderTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"Flint_data_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _loader = new DataFileLoader();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    #region YAML 文件测试

    [Fact]
    public async Task LoadAsync_应该加载YAML文件()
    {
        // Arrange
        var yamlContent = """
            name: "测试数据"
            version: 1
            items:
              - item1
              - item2
            """;
        await File.WriteAllTextAsync(Path.Combine(_testDir, "test.yaml"), yamlContent);

        // Act
        var result = await _loader.LoadAsync(_testDir);

        // Assert
        Assert.True(result.ContainsKey("test"));
        var data = result["test"] as Dictionary<object, object>;
        Assert.NotNull(data);
        Assert.Equal("测试数据", data["name"]);
    }

    [Fact]
    public async Task LoadAsync_应该加载YML文件()
    {
        // Arrange
        var ymlContent = """
            title: "YML测试"
            enabled: true
            """;
        await File.WriteAllTextAsync(Path.Combine(_testDir, "config.yml"), ymlContent);

        // Act
        var result = await _loader.LoadAsync(_testDir);

        // Assert
        Assert.True(result.ContainsKey("config"));
    }

    #endregion

    #region TOML 文件测试

    [Fact]
    public async Task LoadAsync_应该加载TOML文件()
    {
        // Arrange
        var tomlContent = """
            name = "TOML数据"
            version = 2
            
            [settings]
            debug = true
            """;
        await File.WriteAllTextAsync(Path.Combine(_testDir, "settings.toml"), tomlContent);

        // Act
        var result = await _loader.LoadAsync(_testDir);

        // Assert
        Assert.True(result.ContainsKey("settings"));
        var data = result["settings"] as Dictionary<string, object>;
        Assert.NotNull(data);
        Assert.Equal("TOML数据", data["name"]);
    }

    #endregion

    #region JSON 文件测试

    [Fact]
    public async Task LoadAsync_应该加载JSON文件()
    {
        // Arrange
        var jsonContent = """
            {
                "name": "JSON数据",
                "count": 42,
                "active": true,
                "tags": ["tag1", "tag2"]
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(_testDir, "data.json"), jsonContent);

        // Act
        var result = await _loader.LoadAsync(_testDir);

        // Assert
        Assert.True(result.ContainsKey("data"));
        var data = result["data"] as Dictionary<string, object?>;
        Assert.NotNull(data);
        Assert.Equal("JSON数据", data["name"]);
        Assert.Equal(42, Convert.ToInt64(data["count"]));
        Assert.Equal(true, data["active"]);
    }

    [Fact]
    public async Task LoadAsync_应该正确处理JSON数组()
    {
        // Arrange
        var jsonContent = """
            [
                {"id": 1, "name": "Item 1"},
                {"id": 2, "name": "Item 2"}
            ]
            """;
        await File.WriteAllTextAsync(Path.Combine(_testDir, "items.json"), jsonContent);

        // Act
        var result = await _loader.LoadAsync(_testDir);

        // Assert
        Assert.True(result.ContainsKey("items"));
        var items = result["items"] as List<object?>;
        Assert.NotNull(items);
        Assert.Equal(2, items.Count);
    }

    #endregion

    #region 目录结构测试

    [Fact]
    public async Task LoadAsync_应该递归加载子目录()
    {
        // Arrange
        var subDir = Path.Combine(_testDir, "subdir");
        Directory.CreateDirectory(subDir);
        await File.WriteAllTextAsync(Path.Combine(subDir, "nested.yaml"), "value: nested");

        // Act
        var result = await _loader.LoadAsync(_testDir);

        // Assert
        Assert.True(result.ContainsKey("subdir.nested"));
    }

    [Fact]
    public async Task LoadAsync_应该处理多级嵌套目录()
    {
        // Arrange
        var deepDir = Path.Combine(_testDir, "level1", "level2");
        Directory.CreateDirectory(deepDir);
        await File.WriteAllTextAsync(Path.Combine(deepDir, "deep.json"), """{"deep": true}""");

        // Act
        var result = await _loader.LoadAsync(_testDir);

        // Assert
        Assert.True(result.ContainsKey("level1.level2.deep"));
    }

    #endregion

    #region 边界情况测试

    [Fact]
    public async Task LoadAsync_目录不存在时应该返回空字典()
    {
        // Arrange
        var nonExistentDir = Path.Combine(_testDir, "nonexistent");

        // Act
        var result = await _loader.LoadAsync(nonExistentDir);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task LoadAsync_应该忽略不支持的文件格式()
    {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_testDir, "readme.txt"), "This is a text file");
        await File.WriteAllTextAsync(Path.Combine(_testDir, "data.yaml"), "valid: true");

        // Act
        var result = await _loader.LoadAsync(_testDir);

        // Assert
        Assert.Single(result);
        Assert.True(result.ContainsKey("data"));
        Assert.False(result.ContainsKey("readme"));
    }

    [Fact]
    public async Task LoadAsync_应该忽略无效的文件内容()
    {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_testDir, "invalid.json"), "not valid json {{{");
        await File.WriteAllTextAsync(Path.Combine(_testDir, "valid.json"), """{"valid": true}""");

        // Act
        var result = await _loader.LoadAsync(_testDir);

        // Assert
        Assert.Single(result);
        Assert.True(result.ContainsKey("valid"));
    }

    [Fact]
    public async Task LoadAsync_应该处理空文件()
    {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_testDir, "empty.yaml"), "");
        await File.WriteAllTextAsync(Path.Combine(_testDir, "valid.yaml"), "key: value");

        // Act
        var result = await _loader.LoadAsync(_testDir);

        // Assert
        // 空文件可能被忽略或返回 null
        Assert.True(result.ContainsKey("valid"));
    }

    #endregion

    #region 数据类型测试

    [Fact]
    public async Task LoadAsync_应该正确处理JSON数值类型()
    {
        // Arrange
        var jsonContent = """
            {
                "integer": 42,
                "float": 3.14,
                "negative": -10,
                "large": 9999999999
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(_testDir, "numbers.json"), jsonContent);

        // Act
        var result = await _loader.LoadAsync(_testDir);

        // Assert
        var data = result["numbers"] as Dictionary<string, object?>;
        Assert.NotNull(data);
        Assert.Equal(42, Convert.ToInt64(data["integer"]));
        Assert.Equal(3.14, data["float"]);
        Assert.Equal(-10, Convert.ToInt64(data["negative"]));
    }

    [Fact]
    public async Task LoadAsync_应该正确处理JSON布尔和null()
    {
        // Arrange
        var jsonContent = """
            {
                "trueValue": true,
                "falseValue": false,
                "nullValue": null
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(_testDir, "booleans.json"), jsonContent);

        // Act
        var result = await _loader.LoadAsync(_testDir);

        // Assert
        var data = result["booleans"] as Dictionary<string, object?>;
        Assert.NotNull(data);
        Assert.Equal(true, data["trueValue"]);
        Assert.Equal(false, data["falseValue"]);
        Assert.Null(data["nullValue"]);
    }

    #endregion
}

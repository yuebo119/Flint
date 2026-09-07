// Flint 静态站点生成器
// 配置验证错误测试
// 测试无效值、缺少必需字段、格式错误的处理
// _Requirements: 4.6, 4.7_

using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using FluentAssertions;
using Xunit;

namespace Flint.IntegrationTests.Configuration;

/// <summary>
/// 配置验证错误测试
/// 验证配置系统对无效输入的错误处理
/// </summary>
public class ConfigValidationErrorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ConfigLoader _loader;
    private readonly ConfigValidator _validator;

    public ConfigValidationErrorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"Flint-config-validation-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _loader = new ConfigLoader();
        _validator = new ConfigValidator();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // 忽略清理错误
        }
        GC.SuppressFinalize(this);
    }

    #region 无效值错误处理（类型错误、范围错误）

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com")]
    [InlineData("example.com")]
    [InlineData("://missing-protocol.com")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "测试需要验证无效 URL 字符串")]
    public void Validate_WithInvalidBaseUrl_ShouldReturnErrorWithFieldName(string invalidUrl)
    {
        // Arrange
        var config = new SiteConfig { BaseURL = invalidUrl, Title = "Test Site" };

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyPath == "baseURL" && e.ErrorCode == "CFG003");
        var error = result.Errors.First(e => e.PropertyPath == "baseURL");
        error.ActualValue.Should().Be(invalidUrl);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Validate_WithPaginateBelowMinimum_ShouldReturnRangeError(int invalidPaginate)
    {
        // Arrange
        var config = CreateValidConfigWithPaginate(invalidPaginate);

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyPath == "paginate" && e.ErrorCode == "CFG004");
    }

    [Theory]
    [InlineData(1001)]
    [InlineData(5000)]
    public void Validate_WithPaginateAboveMaximum_ShouldReturnRangeError(int invalidPaginate)
    {
        // Arrange
        var config = CreateValidConfigWithPaginate(invalidPaginate);

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyPath == "paginate" && e.ErrorCode == "CFG004");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1001)]
    public void Validate_WithInvalidSummaryLength_ShouldReturnRangeError(int invalidLength)
    {
        // Arrange
        var config = CreateValidConfigWithSummaryLength(invalidLength);

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyPath == "summaryLength" && e.ErrorCode == "CFG004");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(301)]
    public void Validate_WithInvalidHttpTimeout_ShouldReturnRangeError(int invalidTimeout)
    {
        // Arrange
        var config = CreateValidConfigWithSecurity(new SecurityConfig { HttpTimeout = invalidTimeout });

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyPath == "security.httpTimeout" && e.ErrorCode == "CFG004");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10001)]
    public void Validate_WithInvalidCacheMaxSize_ShouldReturnRangeError(int invalidSize)
    {
        // Arrange
        var config = CreateValidConfigWithCaches(new CacheConfig { MaxSize = invalidSize });

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyPath == "caches.maxSize" && e.ErrorCode == "CFG004");
    }

    [Theory]
    [InlineData("english")]
    [InlineData("e")]
    [InlineData("en_US")]
    [InlineData("123")]
    public void Validate_WithInvalidLanguageCode_ShouldReturnError(string invalidCode)
    {
        // Arrange
        var config = CreateValidConfigWithLanguageCode(invalidCode);

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyPath == "languageCode" && e.ErrorCode == "CFG009");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void Validate_WithInvalidTocStartLevel_ShouldReturnRangeError(int invalidLevel)
    {
        // Arrange
        var config = CreateValidConfigWithMarkup(new MarkupConfig
        {
            TableOfContents = new TableOfContentsConfig { StartLevel = invalidLevel, EndLevel = 4 }
        });

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyPath == "markup.tableOfContents.startLevel" && e.ErrorCode == "CFG004");
    }

    [Theory]
    [InlineData(4, 2)]
    [InlineData(5, 1)]
    public void Validate_WithStartLevelGreaterThanEndLevel_ShouldReturnLogicError(int startLevel, int endLevel)
    {
        // Arrange
        var config = CreateValidConfigWithMarkup(new MarkupConfig
        {
            TableOfContents = new TableOfContentsConfig { StartLevel = startLevel, EndLevel = endLevel }
        });

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyPath == "markup.tableOfContents" && e.ErrorCode == "CFG005");
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://author.com")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "测试需要验证无效 URL 字符串")]
    public void Validate_WithInvalidAuthorUrl_ShouldReturnError(string invalidUrl)
    {
        // Arrange
        var config = CreateValidConfigWithAuthor(new AuthorConfig { Name = "Test Author", URL = invalidUrl });

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyPath == "author.url" && e.ErrorCode == "CFG003");
    }

    #endregion

    #region 缺少必需字段错误处理

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "测试需要验证缺失 URL 字符串")]
    public void Validate_WithMissingBaseUrl_ShouldReturnRequiredFieldError(string? missingUrl)
    {
        // Arrange
        var config = new SiteConfig { BaseURL = missingUrl!, Title = "Test Site" };

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyPath == "baseURL" && e.ErrorCode == "CFG001");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithMissingTitle_ShouldReturnRequiredFieldError(string? missingTitle)
    {
        // Arrange
        var config = new SiteConfig { BaseURL = "https://example.com/", Title = missingTitle! };

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyPath == "title" && e.ErrorCode == "CFG002");
    }

    [Fact]
    public void Validate_WithMultipleMissingFields_ShouldReturnAllErrors()
    {
        // Arrange
        var config = new SiteConfig { BaseURL = "", Title = "" };

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCountGreaterThanOrEqualTo(2);
        result.Errors.Should().Contain(e => e.PropertyPath == "baseURL");
        result.Errors.Should().Contain(e => e.PropertyPath == "title");
    }

    [Fact]
    public void Validate_WithEmptyContentDir_ShouldReturnError()
    {
        // Arrange
        var config = CreateValidConfigWithContentDir("");

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyPath == "contentDir" && e.ErrorCode == "CFG006");
    }

    [Fact]
    public void Validate_WithEmptyLayoutDir_ShouldReturnError()
    {
        // Arrange
        var config = CreateValidConfigWithLayoutDir("");

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyPath == "layoutDir" && e.ErrorCode == "CFG006");
    }

    [Fact]
    public void Validate_WithEmptyStaticDir_ShouldReturnError()
    {
        // Arrange
        var config = CreateValidConfigWithStaticDir("");

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyPath == "staticDir" && e.ErrorCode == "CFG006");
    }

    #endregion

    #region 格式错误处理（无效 TOML/YAML/JSON）

    [Fact]
    public async Task LoadAsync_WithUnclosedTomlString_ShouldThrowParseException()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "invalid.toml");
        await File.WriteAllTextAsync(configPath, "baseURL = \"https://example.com/\ntitle = \"Test");

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(() => _loader.LoadAsync(configPath).AsTask());
    }

    [Fact]
    public async Task LoadAsync_WithInvalidTomlKeyValue_ShouldThrowParseException()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "invalid.toml");
        await File.WriteAllTextAsync(configPath, "baseURL = \ntitle = \"Test\"");

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(() => _loader.LoadAsync(configPath).AsTask());
    }

    [Fact]
    public async Task LoadAsync_WithInvalidTomlTable_ShouldThrowParseException()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "invalid.toml");
        await File.WriteAllTextAsync(configPath, "baseURL = \"https://example.com/\"\n[invalid table\nkey = \"value\"");

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(() => _loader.LoadAsync(configPath).AsTask());
    }

    [Fact]
    public async Task LoadAsync_WithUnclosedJsonBrace_ShouldThrowParseException()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "invalid.json");
        await File.WriteAllTextAsync(configPath, "{\"baseURL\": \"https://example.com/\", \"title\": \"Test\"");

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(() => _loader.LoadAsync(configPath).AsTask());
    }

    [Fact]
    public async Task LoadAsync_WithMissingJsonQuotes_ShouldThrowParseException()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "invalid.json");
        await File.WriteAllTextAsync(configPath, "{baseURL: \"https://example.com/\", \"title\": \"Test\"}");

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(() => _loader.LoadAsync(configPath).AsTask());
    }

    [Fact]
    public async Task LoadAsync_WithJsonTrailingComma_ShouldHandleGracefully()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "trailing.json");
        await File.WriteAllTextAsync(configPath, "{\"baseURL\": \"https://example.com/\", \"title\": \"Test\",}");

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.BaseURL.Should().Be("https://example.com/");
        config.Title.Should().Be("Test");
    }

    [Fact]
    public async Task LoadAsync_WithInvalidJsonNumber_ShouldThrowParseException()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "invalid.json");
        await File.WriteAllTextAsync(configPath, "{\"baseURL\": \"https://example.com/\", \"title\": \"Test\", \"paginate\": 10abc}");

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(() => _loader.LoadAsync(configPath).AsTask());
    }

    [Theory]
    [InlineData("invalid.toml", "这不是有效的配置文件内容 !!!")]
    [InlineData("invalid.json", "not json at all")]
    public async Task LoadAsync_WithCompletelyInvalidContent_ShouldThrowException(string fileName, string content)
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, fileName);
        await File.WriteAllTextAsync(configPath, content);

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(() => _loader.LoadAsync(configPath).AsTask());
    }

    [Theory]
    [InlineData("empty.toml")]
    [InlineData("empty.yaml")]
    public async Task LoadAsync_WithEmptyFile_ShouldHandleGracefully(string fileName)
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, fileName);
        await File.WriteAllTextAsync(configPath, "");

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Should().NotBeNull();
    }

    [Fact]
    public async Task LoadAsync_WithEmptyJsonFile_ShouldHandleGracefully()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "empty.json");
        await File.WriteAllTextAsync(configPath, "{}");

        // Act
        var config = await _loader.LoadAsync(configPath);

        // Assert
        config.Should().NotBeNull();
    }

    [Fact]
    public async Task LoadAsync_WithInvalidYamlIndentation_ShouldThrowParseException()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "invalid.yaml");
        await File.WriteAllTextAsync(configPath, "baseURL: \"https://example.com/\"\n  title: \"Test\"\n invalid: indentation");

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(() => _loader.LoadAsync(configPath).AsTask());
    }

    [Fact]
    public async Task LoadAsync_WithInvalidYamlColon_ShouldThrowParseException()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "invalid.yaml");
        await File.WriteAllTextAsync(configPath, "baseURL https://example.com/\ntitle: \"Test\"");

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(() => _loader.LoadAsync(configPath).AsTask());
    }

    [Fact]
    public async Task LoadAsync_WithInvalidTomlArraySyntax_ShouldThrowParseException()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "invalid.toml");
        await File.WriteAllTextAsync(configPath, "baseURL = \"https://example.com/\"\ntitle = \"Test\"\nitems = [1, 2, 3");

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(() => _loader.LoadAsync(configPath).AsTask());
    }

    [Fact]
    public async Task LoadAsync_WithInvalidJsonArraySyntax_ShouldThrowParseException()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, "invalid.json");
        await File.WriteAllTextAsync(configPath, "{\"baseURL\": \"https://example.com/\", \"title\": \"Test\", \"items\": [1, 2, 3}");

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(() => _loader.LoadAsync(configPath).AsTask());
    }

    #endregion

    #region 验证错误信息包含字段名和位置

    [Fact]
    public void Validate_ErrorsShouldContainPropertyPath()
    {
        // Arrange
        var config = new SiteConfig
        {
            BaseURL = "invalid-url",
            Title = "Test",
            Markup = new MarkupConfig
            {
                TableOfContents = new TableOfContentsConfig { StartLevel = 0, EndLevel = 7 }
            }
        };

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        foreach (var error in result.Errors)
        {
            error.PropertyPath.Should().NotBeNullOrEmpty();
        }
        result.Errors.Should().Contain(e => e.PropertyPath.Contains("markup.tableOfContents", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ErrorsShouldContainErrorCode()
    {
        // Arrange
        var config = new SiteConfig { BaseURL = "", Title = "", LanguageCode = "invalid" };

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        foreach (var error in result.Errors)
        {
            error.ErrorCode.Should().NotBeNullOrEmpty();
            error.ErrorCode.Should().StartWith("CFG");
        }
    }

    [Fact]
    public void Validate_ErrorsShouldContainExpectedAndActualValues()
    {
        // Arrange
        var config = CreateValidConfigWithPaginate(-5);

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        var paginateError = result.Errors.First(e => e.PropertyPath == "paginate");
        paginateError.ExpectedValue.Should().NotBeNullOrEmpty();
        paginateError.ActualValue.Should().NotBeNullOrEmpty();
        paginateError.ActualValue.Should().Contain("-5");
    }

    [Fact]
    public void Validate_ErrorsShouldContainDescriptiveMessage()
    {
        // Arrange
        var config = new SiteConfig
        {
            BaseURL = "not-a-valid-url",
            Title = "Test Site"
        };

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        var urlError = result.Errors.First(e => e.PropertyPath == "baseURL");
        urlError.Message.Should().NotBeNullOrEmpty();
        urlError.Message.Length.Should().BeGreaterThan(10);
    }

    [Fact]
    public void Validate_WithInvalidLanguageInLanguages_ShouldIncludeLanguageKeyInPath()
    {
        // Arrange
        var config = CreateValidConfigWithLanguages(new Dictionary<string, LanguageConfig>
        {
            ["invalid_lang"] = new LanguageConfig { LanguageName = "Invalid" }
        });

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyPath.Contains("languages.invalid_lang", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ErrorMessageShouldBeHumanReadable()
    {
        // Arrange
        var config = new SiteConfig
        {
            BaseURL = "invalid",
            Title = "",
            Paginate = -1
        };

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        foreach (var error in result.Errors)
        {
            // 错误消息应该是人类可读的，不应该包含技术术语或代码
            error.Message.Should().NotContain("null");
            error.Message.Should().NotContain("exception");
            error.Message.Length.Should().BeGreaterThan(5);
        }
    }

    #endregion

    #region 路径安全性验证

    [Theory]
    [InlineData("/absolute/path")]
    [InlineData("C:\\Windows\\System32")]
    public void Validate_WithAbsolutePath_ShouldReturnSecurityError(string absolutePath)
    {
        // Arrange
        var config = CreateValidConfigWithContentDir(absolutePath);

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyPath == "contentDir" && e.ErrorCode == "CFG007");
    }

    [Theory]
    [InlineData("../parent")]
    [InlineData("path/../other")]
    [InlineData("..")]
    public void Validate_WithParentDirectoryReference_ShouldReturnSecurityError(string pathWithParentRef)
    {
        // Arrange
        var config = CreateValidConfigWithContentDir(pathWithParentRef);

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyPath == "contentDir" && e.ErrorCode == "CFG008");
    }

    #endregion

    #region 综合错误场景测试

    [Fact]
    public void Validate_WithMultipleErrorTypes_ShouldReportAllErrors()
    {
        // Arrange
        var config = new SiteConfig
        {
            BaseURL = "invalid-url",
            Title = "",
            LanguageCode = "invalid_code",
            Paginate = -1,
            ContentDir = "../escape",
            Markup = new MarkupConfig
            {
                TableOfContents = new TableOfContentsConfig { StartLevel = 5, EndLevel = 2 }
            }
        };

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCountGreaterThanOrEqualTo(5);
        result.Errors.Should().Contain(e => e.ErrorCode == "CFG002"); // 缺少标题
        result.Errors.Should().Contain(e => e.ErrorCode == "CFG003"); // 无效 URL
        result.Errors.Should().Contain(e => e.ErrorCode == "CFG004"); // 范围错误
        result.Errors.Should().Contain(e => e.ErrorCode == "CFG005"); // 逻辑错误
        result.Errors.Should().Contain(e => e.ErrorCode == "CFG008"); // 路径安全
    }

    [Fact]
    public void Validate_WithValidConfig_ShouldPass()
    {
        // Arrange
        var config = CreateValidConfig();

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(10, 500)]
    [InlineData(1000, 1000)]
    public void Validate_WithBoundaryPaginateValues_ShouldPass(int paginate, int summaryLength)
    {
        // Arrange
        var config = new SiteConfig
        {
            BaseURL = "https://example.com/",
            Title = "Test Site",
            LanguageCode = "en",
            ContentDir = "content",
            LayoutDir = "layouts",
            StaticDir = "static",
            AssetDir = "assets",
            DataDir = "data",
            PublishDir = "public",
            ArchetypeDir = "archetypes",
            Paginate = paginate,
            SummaryLength = summaryLength,
            Markup = new MarkupConfig
            {
                TableOfContents = new TableOfContentsConfig { StartLevel = 2, EndLevel = 3 }
            },
            Security = new SecurityConfig { HttpTimeout = 30 },
            Caches = new CacheConfig { MaxSize = 100 }
        };

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region 辅助方法

    private static SiteConfig CreateValidConfig()
    {
        return new SiteConfig
        {
            BaseURL = "https://example.com/",
            Title = "Test Site",
            LanguageCode = "en",
            ContentDir = "content",
            LayoutDir = "layouts",
            StaticDir = "static",
            AssetDir = "assets",
            DataDir = "data",
            PublishDir = "public",
            ArchetypeDir = "archetypes",
            Paginate = 10,
            SummaryLength = 70,
            Markup = new MarkupConfig
            {
                TableOfContents = new TableOfContentsConfig { StartLevel = 2, EndLevel = 3 }
            },
            Security = new SecurityConfig { HttpTimeout = 30 },
            Caches = new CacheConfig { MaxSize = 100 }
        };
    }

    private static SiteConfig CreateValidConfigWithPaginate(int paginate)
    {
        return new SiteConfig
        {
            BaseURL = "https://example.com/",
            Title = "Test Site",
            LanguageCode = "en",
            ContentDir = "content",
            LayoutDir = "layouts",
            StaticDir = "static",
            AssetDir = "assets",
            DataDir = "data",
            PublishDir = "public",
            ArchetypeDir = "archetypes",
            Paginate = paginate,
            SummaryLength = 70,
            Markup = new MarkupConfig
            {
                TableOfContents = new TableOfContentsConfig { StartLevel = 2, EndLevel = 3 }
            },
            Security = new SecurityConfig { HttpTimeout = 30 },
            Caches = new CacheConfig { MaxSize = 100 }
        };
    }

    private static SiteConfig CreateValidConfigWithSummaryLength(int summaryLength)
    {
        return new SiteConfig
        {
            BaseURL = "https://example.com/",
            Title = "Test Site",
            LanguageCode = "en",
            ContentDir = "content",
            LayoutDir = "layouts",
            StaticDir = "static",
            AssetDir = "assets",
            DataDir = "data",
            PublishDir = "public",
            ArchetypeDir = "archetypes",
            Paginate = 10,
            SummaryLength = summaryLength,
            Markup = new MarkupConfig
            {
                TableOfContents = new TableOfContentsConfig { StartLevel = 2, EndLevel = 3 }
            },
            Security = new SecurityConfig { HttpTimeout = 30 },
            Caches = new CacheConfig { MaxSize = 100 }
        };
    }

    private static SiteConfig CreateValidConfigWithSecurity(SecurityConfig security)
    {
        return new SiteConfig
        {
            BaseURL = "https://example.com/",
            Title = "Test Site",
            LanguageCode = "en",
            ContentDir = "content",
            LayoutDir = "layouts",
            StaticDir = "static",
            AssetDir = "assets",
            DataDir = "data",
            PublishDir = "public",
            ArchetypeDir = "archetypes",
            Paginate = 10,
            SummaryLength = 70,
            Markup = new MarkupConfig
            {
                TableOfContents = new TableOfContentsConfig { StartLevel = 2, EndLevel = 3 }
            },
            Security = security,
            Caches = new CacheConfig { MaxSize = 100 }
        };
    }

    private static SiteConfig CreateValidConfigWithCaches(CacheConfig caches)
    {
        return new SiteConfig
        {
            BaseURL = "https://example.com/",
            Title = "Test Site",
            LanguageCode = "en",
            ContentDir = "content",
            LayoutDir = "layouts",
            StaticDir = "static",
            AssetDir = "assets",
            DataDir = "data",
            PublishDir = "public",
            ArchetypeDir = "archetypes",
            Paginate = 10,
            SummaryLength = 70,
            Markup = new MarkupConfig
            {
                TableOfContents = new TableOfContentsConfig { StartLevel = 2, EndLevel = 3 }
            },
            Security = new SecurityConfig { HttpTimeout = 30 },
            Caches = caches
        };
    }

    private static SiteConfig CreateValidConfigWithLanguageCode(string languageCode)
    {
        return new SiteConfig
        {
            BaseURL = "https://example.com/",
            Title = "Test Site",
            LanguageCode = languageCode,
            ContentDir = "content",
            LayoutDir = "layouts",
            StaticDir = "static",
            AssetDir = "assets",
            DataDir = "data",
            PublishDir = "public",
            ArchetypeDir = "archetypes",
            Paginate = 10,
            SummaryLength = 70,
            Markup = new MarkupConfig
            {
                TableOfContents = new TableOfContentsConfig { StartLevel = 2, EndLevel = 3 }
            },
            Security = new SecurityConfig { HttpTimeout = 30 },
            Caches = new CacheConfig { MaxSize = 100 }
        };
    }

    private static SiteConfig CreateValidConfigWithMarkup(MarkupConfig markup)
    {
        return new SiteConfig
        {
            BaseURL = "https://example.com/",
            Title = "Test Site",
            LanguageCode = "en",
            ContentDir = "content",
            LayoutDir = "layouts",
            StaticDir = "static",
            AssetDir = "assets",
            DataDir = "data",
            PublishDir = "public",
            ArchetypeDir = "archetypes",
            Paginate = 10,
            SummaryLength = 70,
            Markup = markup,
            Security = new SecurityConfig { HttpTimeout = 30 },
            Caches = new CacheConfig { MaxSize = 100 }
        };
    }

    private static SiteConfig CreateValidConfigWithAuthor(AuthorConfig author)
    {
        return new SiteConfig
        {
            BaseURL = "https://example.com/",
            Title = "Test Site",
            LanguageCode = "en",
            ContentDir = "content",
            LayoutDir = "layouts",
            StaticDir = "static",
            AssetDir = "assets",
            DataDir = "data",
            PublishDir = "public",
            ArchetypeDir = "archetypes",
            Paginate = 10,
            SummaryLength = 70,
            Markup = new MarkupConfig
            {
                TableOfContents = new TableOfContentsConfig { StartLevel = 2, EndLevel = 3 }
            },
            Security = new SecurityConfig { HttpTimeout = 30 },
            Caches = new CacheConfig { MaxSize = 100 },
            Author = author
        };
    }

    private static SiteConfig CreateValidConfigWithContentDir(string contentDir)
    {
        return new SiteConfig
        {
            BaseURL = "https://example.com/",
            Title = "Test Site",
            LanguageCode = "en",
            ContentDir = contentDir,
            LayoutDir = "layouts",
            StaticDir = "static",
            AssetDir = "assets",
            DataDir = "data",
            PublishDir = "public",
            ArchetypeDir = "archetypes",
            Paginate = 10,
            SummaryLength = 70,
            Markup = new MarkupConfig
            {
                TableOfContents = new TableOfContentsConfig { StartLevel = 2, EndLevel = 3 }
            },
            Security = new SecurityConfig { HttpTimeout = 30 },
            Caches = new CacheConfig { MaxSize = 100 }
        };
    }

    private static SiteConfig CreateValidConfigWithLayoutDir(string layoutDir)
    {
        return new SiteConfig
        {
            BaseURL = "https://example.com/",
            Title = "Test Site",
            LanguageCode = "en",
            ContentDir = "content",
            LayoutDir = layoutDir,
            StaticDir = "static",
            AssetDir = "assets",
            DataDir = "data",
            PublishDir = "public",
            ArchetypeDir = "archetypes",
            Paginate = 10,
            SummaryLength = 70,
            Markup = new MarkupConfig
            {
                TableOfContents = new TableOfContentsConfig { StartLevel = 2, EndLevel = 3 }
            },
            Security = new SecurityConfig { HttpTimeout = 30 },
            Caches = new CacheConfig { MaxSize = 100 }
        };
    }

    private static SiteConfig CreateValidConfigWithStaticDir(string staticDir)
    {
        return new SiteConfig
        {
            BaseURL = "https://example.com/",
            Title = "Test Site",
            LanguageCode = "en",
            ContentDir = "content",
            LayoutDir = "layouts",
            StaticDir = staticDir,
            AssetDir = "assets",
            DataDir = "data",
            PublishDir = "public",
            ArchetypeDir = "archetypes",
            Paginate = 10,
            SummaryLength = 70,
            Markup = new MarkupConfig
            {
                TableOfContents = new TableOfContentsConfig { StartLevel = 2, EndLevel = 3 }
            },
            Security = new SecurityConfig { HttpTimeout = 30 },
            Caches = new CacheConfig { MaxSize = 100 }
        };
    }

    private static SiteConfig CreateValidConfigWithLanguages(IReadOnlyDictionary<string, LanguageConfig> languages)
    {
        return new SiteConfig
        {
            BaseURL = "https://example.com/",
            Title = "Test Site",
            LanguageCode = "en",
            ContentDir = "content",
            LayoutDir = "layouts",
            StaticDir = "static",
            AssetDir = "assets",
            DataDir = "data",
            PublishDir = "public",
            ArchetypeDir = "archetypes",
            Paginate = 10,
            SummaryLength = 70,
            Markup = new MarkupConfig
            {
                TableOfContents = new TableOfContentsConfig { StartLevel = 2, EndLevel = 3 }
            },
            Security = new SecurityConfig { HttpTimeout = 30 },
            Caches = new CacheConfig { MaxSize = 100 },
            Languages = languages
        };
    }

    #endregion
}

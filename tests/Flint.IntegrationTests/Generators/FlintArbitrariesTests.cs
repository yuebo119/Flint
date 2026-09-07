// Flint 静态站点生成器
// FsCheck 生成器验证测试
// 验证生成的数据符合约束和分布合理性

using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Xunit;

namespace Flint.IntegrationTests.Generators;

/// <summary>
/// FlintArbitraries 生成器验证测试
/// 验证生成的数据符合约束、分布合理性和边界条件覆盖率
/// </summary>
public class FlintArbitrariesTests
{
    #region SiteConfig 生成器测试

    [Property(MaxTest = 100)]
    public Property SiteConfig_ShouldHaveValidBaseUrl()
    {
        return Prop.ForAll(
            FlintArbitraries.SiteConfigArb(),
            config =>
            {
                config.BaseURL.Should().NotBeNullOrEmpty();
                config.BaseURL.Should().StartWith("http");
                return true;
            });
    }

    [Property(MaxTest = 100)]
    public Property SiteConfig_ShouldHaveValidTitle()
    {
        return Prop.ForAll(
            FlintArbitraries.SiteConfigArb(),
            config =>
            {
                config.Title.Should().NotBeNullOrEmpty();
                return true;
            });
    }

    [Property(MaxTest = 100)]
    public Property SiteConfig_ShouldHaveValidLanguageCode()
    {
        return Prop.ForAll(
            FlintArbitraries.SiteConfigArb(),
            config =>
            {
                config.LanguageCode.Should().NotBeNullOrEmpty();
                config.LanguageCode.Length.Should().BeGreaterThanOrEqualTo(2);
                return true;
            });
    }

    [Property(MaxTest = 100)]
    public Property SiteConfig_ShouldHaveValidPaginate()
    {
        return Prop.ForAll(
            FlintArbitraries.SiteConfigArb(),
            config =>
            {
                config.Paginate.Should().BeGreaterThanOrEqualTo(5);
                config.Paginate.Should().BeLessThanOrEqualTo(50);
                return true;
            });
    }

    [Property(MaxTest = 100)]
    public Property MinimalSiteConfig_ShouldOnlyHaveRequiredFields()
    {
        return Prop.ForAll(
            FlintArbitraries.MinimalSiteConfigArb(),
            config =>
            {
                config.BaseURL.Should().NotBeNullOrEmpty();
                config.Title.Should().NotBeNullOrEmpty();
                // 默认值应该被使用
                config.LanguageCode.Should().Be("en");
                config.Theme.Should().BeEmpty();
                return true;
            });
    }

    [Property(MaxTest = 100)]
    public Property FullSiteConfig_ShouldHaveAllOptionalFields()
    {
        return Prop.ForAll(
            FlintArbitraries.FullSiteConfigArb(),
            config =>
            {
                config.BaseURL.Should().NotBeNullOrEmpty();
                config.Title.Should().NotBeNullOrEmpty();
                config.Copyright.Should().NotBeNullOrEmpty();
                config.Author.Should().NotBeNull();
                config.Author!.Name.Should().NotBeNullOrEmpty();
                return true;
            });
    }

    #endregion

    #region FrontMatter 生成器测试

    [Property(MaxTest = 100)]
    public Property FrontMatter_ShouldHaveValidTitle()
    {
        return Prop.ForAll(
            FlintArbitraries.FrontMatterArb(),
            fm =>
            {
                fm.Title.Should().NotBeNullOrEmpty();
                return true;
            });
    }

    [Property(MaxTest = 100)]
    public Property FrontMatter_ShouldHaveValidWeight()
    {
        return Prop.ForAll(
            FlintArbitraries.FrontMatterArb(),
            fm =>
            {
                fm.Weight.Should().BeGreaterThanOrEqualTo(0);
                fm.Weight.Should().BeLessThanOrEqualTo(100);
                return true;
            });
    }

    [Property(MaxTest = 100)]
    public Property FrontMatter_TagsShouldBeValid()
    {
        return Prop.ForAll(
            FlintArbitraries.FrontMatterArb(),
            fm =>
            {
                fm.Tags.Should().NotBeNull();
                // Tags 可以为空，如果不为空则每个 tag 都应该有效
                if (fm.Tags.Count > 0)
                {
                    fm.Tags.Should().AllSatisfy(tag => tag.Should().NotBeNullOrEmpty());
                }
                return true;
            });
    }

    [Property(MaxTest = 100)]
    public Property FrontMatter_CategoriesShouldBeValid()
    {
        return Prop.ForAll(
            FlintArbitraries.FrontMatterArb(),
            fm =>
            {
                fm.Categories.Should().NotBeNull();
                // Categories 可以为空，如果不为空则每个 category 都应该有效
                if (fm.Categories.Count > 0)
                {
                    fm.Categories.Should().AllSatisfy(cat => cat.Should().NotBeNullOrEmpty());
                }
                return true;
            });
    }

    [Property(MaxTest = 100)]
    public Property DraftFrontMatter_ShouldAlwaysBeDraft()
    {
        return Prop.ForAll(
            FlintArbitraries.DraftFrontMatterArb(),
            fm =>
            {
                fm.Draft.Should().BeTrue();
                return true;
            });
    }

    [Property(MaxTest = 100)]
    public Property FutureFrontMatter_ShouldHaveFutureDate()
    {
        return Prop.ForAll(
            FlintArbitraries.FutureFrontMatterArb(),
            fm =>
            {
                fm.Date.Should().NotBeNull();
                fm.Date!.Value.Should().BeAfter(DateTimeOffset.UtcNow);
                return true;
            });
    }

    #endregion

    #region ContentFile 生成器测试

    [Property(MaxTest = 100)]
    public Property ContentFile_ShouldHaveValidPath()
    {
        return Prop.ForAll(
            FlintArbitraries.ContentFileArb(),
            cf =>
            {
                cf.Path.Should().NotBeNullOrEmpty();
                cf.Path.Should().EndWith(".md");
                return true;
            });
    }

    [Property(MaxTest = 100)]
    public Property ContentFile_ShouldHaveContent()
    {
        return Prop.ForAll(
            FlintArbitraries.ContentFileArb(),
            cf =>
            {
                cf.RawContent.Length.Should().BeGreaterThan(0);
                return true;
            });
    }

    [Property(MaxTest = 100)]
    public Property ContentFile_ContentShouldBeValidUtf8()
    {
        return Prop.ForAll(
            FlintArbitraries.ContentFileArb(),
            cf =>
            {
                var action = () => cf.GetContentAsString();
                action.Should().NotThrow();
                return true;
            });
    }

    #endregion

    #region BuildOptions 生成器测试

    [Property(MaxTest = 100)]
    public Property BuildOptions_ShouldHaveValidPaths()
    {
        return Prop.ForAll(
            FlintArbitraries.BuildOptionsArb(),
            opts =>
            {
                opts.SourcePath.Should().NotBeNullOrEmpty();
                opts.OutputPath.Should().NotBeNullOrEmpty();
                return true;
            });
    }

    [Property(MaxTest = 100)]
    public Property BuildOptions_ShouldHaveValidParallelism()
    {
        return Prop.ForAll(
            FlintArbitraries.BuildOptionsArb(),
            opts =>
            {
                opts.Parallelism.Should().BeGreaterThanOrEqualTo(1);
                opts.Parallelism.Should().BeLessThanOrEqualTo(8);
                return true;
            });
    }

    [Property(MaxTest = 100)]
    public Property ProductionBuildOptions_ShouldHaveProductionSettings()
    {
        return Prop.ForAll(
            FlintArbitraries.ProductionBuildOptionsArb(),
            opts =>
            {
                opts.Environment.Should().Be("production");
                opts.Minify.Should().BeTrue();
                opts.IncludeDrafts.Should().BeFalse();
                opts.IncludeFuture.Should().BeFalse();
                opts.CleanOutput.Should().BeTrue();
                return true;
            });
    }

    [Property(MaxTest = 100)]
    public Property DevelopmentBuildOptions_ShouldHaveDevelopmentSettings()
    {
        return Prop.ForAll(
            FlintArbitraries.DevelopmentBuildOptionsArb(),
            opts =>
            {
                opts.Environment.Should().Be("development");
                opts.Minify.Should().BeFalse();
                opts.IncludeDrafts.Should().BeTrue();
                opts.IncludeFuture.Should().BeTrue();
                opts.GenerateSourceMaps.Should().BeTrue();
                return true;
            });
    }

    #endregion

    #region AssetFile 生成器测试

    [Property(MaxTest = 100)]
    public Property AssetFile_ShouldHaveValidSourcePath()
    {
        return Prop.ForAll(
            FlintArbitraries.AssetFileArb(),
            asset =>
            {
                asset.SourcePath.Should().NotBeNullOrEmpty();
                asset.SourcePath.Should().Contain("/");
                return true;
            });
    }

    [Property(MaxTest = 100)]
    public Property AssetFile_ShouldHaveValidMediaType()
    {
        return Prop.ForAll(
            FlintArbitraries.AssetFileArb(),
            asset =>
            {
                asset.MediaType.Should().NotBeNullOrEmpty();
                asset.MediaType.Should().Contain("/");
                return true;
            });
    }

    [Property(MaxTest = 100)]
    public Property AssetFile_ShouldHaveContent()
    {
        return Prop.ForAll(
            FlintArbitraries.AssetFileArb(),
            asset =>
            {
                asset.Content.Length.Should().BeGreaterThan(0);
                return true;
            });
    }

    [Property(MaxTest = 100)]
    public Property ImageAsset_ShouldHaveImageMediaType()
    {
        return Prop.ForAll(
            FlintArbitraries.ImageAssetArb(),
            asset =>
            {
                asset.MediaType.Should().StartWith("image/");
                asset.SourcePath.Should().StartWith("images/");
                return true;
            });
    }

    [Property(MaxTest = 100)]
    public Property StyleAsset_ShouldHaveStyleMediaType()
    {
        return Prop.ForAll(
            FlintArbitraries.StyleAssetArb(),
            asset =>
            {
                asset.MediaType.Should().StartWith("text/");
                asset.SourcePath.Should().StartWith("styles/");
                return true;
            });
    }

    [Property(MaxTest = 100)]
    public Property ScriptAsset_ShouldHaveScriptMediaType()
    {
        return Prop.ForAll(
            FlintArbitraries.ScriptAssetArb(),
            asset =>
            {
                asset.MediaType.Should().StartWith("application/");
                asset.SourcePath.Should().StartWith("scripts/");
                return true;
            });
    }

    [Property(MaxTest = 100)]
    public Property FontAsset_ShouldHaveFontMediaType()
    {
        return Prop.ForAll(
            FlintArbitraries.FontAssetArb(),
            asset =>
            {
                (asset.MediaType.StartsWith("font/") || asset.MediaType.StartsWith("application/")).Should().BeTrue();
                asset.SourcePath.Should().StartWith("fonts/");
                return true;
            });
    }

    #endregion

    #region ConfigFormat 生成器测试

    [Property(MaxTest = 100)]
    public Property ConfigFormat_ShouldBeValidEnum()
    {
        return Prop.ForAll(
            FlintArbitraries.ConfigFormatArb(),
            format =>
            {
                Enum.IsDefined(format).Should().BeTrue();
                return true;
            });
    }

    [Property(MaxTest = 100)]
    public Property FrontMatterFormat_ShouldBeValidEnum()
    {
        return Prop.ForAll(
            FlintArbitraries.FrontMatterFormatArb(),
            format =>
            {
                Enum.IsDefined(format).Should().BeTrue();
                return true;
            });
    }

    #endregion

    #region 分布测试

    [Fact]
    public void SiteConfig_ShouldGenerateDiverseConfigs()
    {
        // 生成多个配置，验证分布合理性
        var configs = FlintArbitraries.SiteConfigArb().Generator.Sample(50, 50).ToList();

        // 验证有不同的 BaseURL
        configs.Select(c => c.BaseURL).Distinct().Count().Should().BeGreaterThan(1);

        // 验证有不同的 Title
        configs.Select(c => c.Title).Distinct().Count().Should().BeGreaterThan(1);

        // 验证有不同的 LanguageCode
        configs.Select(c => c.LanguageCode).Distinct().Count().Should().BeGreaterThan(1);

        // 验证布尔值有 true 和 false
        configs.Select(c => c.BuildDrafts).Distinct().Count().Should().Be(2);
    }

    [Fact]
    public void FrontMatter_ShouldGenerateDiverseFrontMatters()
    {
        var frontMatters = FlintArbitraries.FrontMatterArb().Generator.Sample(50, 50).ToList();

        // 验证有不同的 Title
        frontMatters.Select(fm => fm.Title).Distinct().Count().Should().BeGreaterThan(1);

        // 验证有不同的 Format
        frontMatters.Select(fm => fm.Format).Distinct().Count().Should().BeGreaterThan(1);

        // 验证有草稿和非草稿
        frontMatters.Select(fm => fm.Draft).Distinct().Count().Should().Be(2);
    }

    [Fact]
    public void AssetFile_ShouldGenerateDiverseAssets()
    {
        var assets = FlintArbitraries.AssetFileArb().Generator.Sample(50, 50).ToList();

        // 验证有不同类型的资源
        var types = assets.Select(a => a.Type).Distinct().ToList();
        types.Count.Should().BeGreaterThan(1);
    }

    #endregion
}

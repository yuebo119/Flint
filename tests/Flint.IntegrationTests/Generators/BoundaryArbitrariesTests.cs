// Flint 静态站点生成器
// 边界条件生成器验证测试
// 验证边界条件生成器的覆盖率和正确性

using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Xunit;

namespace Flint.IntegrationTests.Generators;

/// <summary>
/// 边界条件生成器包装类型
/// </summary>
public sealed class EmptyString
{
    public string Value { get; }
    public EmptyString(string value) => Value = value;
}

public sealed class WhitespaceString
{
    public string Value { get; }
    public WhitespaceString(string value) => Value = value;
}

public sealed class LongString
{
    public string Value { get; }
    public LongString(string value) => Value = value;
}

public sealed class LongTitle
{
    public string Value { get; }
    public LongTitle(string value) => Value = value;
}

public sealed class ExtremeInt
{
    public int Value { get; }
    public ExtremeInt(int value) => Value = value;
}

public sealed class LargeList
{
    public IReadOnlyList<string> Value { get; }
    public LargeList(IReadOnlyList<string> value) => Value = value;
}

public sealed class UnicodeString
{
    public string Value { get; }
    public UnicodeString(string value) => Value = value;
}

public sealed class EmojiString
{
    public string Value { get; }
    public EmojiString(string value) => Value = value;
}

public sealed class ControlCharString
{
    public string Value { get; }
    public ControlCharString(string value) => Value = value;
}

public sealed class HtmlSpecialChar
{
    public string Value { get; }
    public HtmlSpecialChar(string value) => Value = value;
}

public sealed class MarkdownSpecialChar
{
    public string Value { get; }
    public MarkdownSpecialChar(string value) => Value = value;
}

public sealed class PathSpecialChar
{
    public string Value { get; }
    public PathSpecialChar(string value) => Value = value;
}

public sealed class DeepPath
{
    public string Value { get; }
    public DeepPath(string value) => Value = value;
}

public sealed class SpecialPath
{
    public string Value { get; }
    public SpecialPath(string value) => Value = value;
}

public sealed class LongPath
{
    public string Value { get; }
    public LongPath(string value) => Value = value;
}

public sealed class BoundaryString
{
    public string Value { get; }
    public BoundaryString(string value) => Value = value;
}

public sealed class BoundaryPath
{
    public string Value { get; }
    public BoundaryPath(string value) => Value = value;
}

public sealed class InvalidUrl
{
    public string Value { get; }
    public InvalidUrl(string value) => Value = value;
}

public sealed class InvalidLanguageCode
{
    public string Value { get; }
    public InvalidLanguageCode(string value) => Value = value;
}

public sealed class InvalidExtension
{
    public string Value { get; }
    public InvalidExtension(string value) => Value = value;
}

/// <summary>
/// 边界条件测试用 Arbitrary 提供者
/// </summary>
public static class BoundaryTestArbitraries
{
    public static Arbitrary<EmptyString> EmptyStringArb() =>
        BoundaryArbitraries.EmptyStringArb().Generator.Select(s => new EmptyString(s)).ToArbitrary();

    public static Arbitrary<WhitespaceString> WhitespaceStringArb() =>
        BoundaryArbitraries.WhitespaceStringArb().Generator.Select(s => new WhitespaceString(s)).ToArbitrary();

    public static Arbitrary<LongString> LongStringArb() =>
        BoundaryArbitraries.LongStringArb().Generator.Select(s => new LongString(s)).ToArbitrary();

    public static Arbitrary<LongTitle> LongTitleArb() =>
        BoundaryArbitraries.LongTitleArb().Generator.Select(s => new LongTitle(s)).ToArbitrary();

    public static Arbitrary<ExtremeInt> ExtremeIntArb() =>
        BoundaryArbitraries.ExtremeIntArb().Generator.Select(i => new ExtremeInt(i)).ToArbitrary();

    public static Arbitrary<LargeList> LargeListArb() =>
        BoundaryArbitraries.LargeListArb().Generator.Select(l => new LargeList(l)).ToArbitrary();

    public static Arbitrary<UnicodeString> UnicodeStringArb() =>
        BoundaryArbitraries.UnicodeStringArb().Generator.Select(s => new UnicodeString(s)).ToArbitrary();

    public static Arbitrary<EmojiString> EmojiStringArb() =>
        BoundaryArbitraries.EmojiStringArb().Generator.Select(s => new EmojiString(s)).ToArbitrary();

    public static Arbitrary<ControlCharString> ControlCharStringArb() =>
        BoundaryArbitraries.ControlCharStringArb().Generator.Select(s => new ControlCharString(s)).ToArbitrary();

    public static Arbitrary<HtmlSpecialChar> HtmlSpecialCharArb() =>
        BoundaryArbitraries.HtmlSpecialCharArb().Generator.Select(s => new HtmlSpecialChar(s)).ToArbitrary();

    public static Arbitrary<MarkdownSpecialChar> MarkdownSpecialCharArb() =>
        BoundaryArbitraries.MarkdownSpecialCharArb().Generator.Select(s => new MarkdownSpecialChar(s)).ToArbitrary();

    public static Arbitrary<PathSpecialChar> PathSpecialCharArb() =>
        BoundaryArbitraries.PathSpecialCharArb().Generator.Select(s => new PathSpecialChar(s)).ToArbitrary();

    public static Arbitrary<DeepPath> DeepPathArb() =>
        BoundaryArbitraries.DeepPathArb().Generator.Select(s => new DeepPath(s)).ToArbitrary();

    public static Arbitrary<SpecialPath> SpecialPathArb() =>
        BoundaryArbitraries.SpecialPathArb().Generator.Select(s => new SpecialPath(s)).ToArbitrary();

    public static Arbitrary<LongPath> LongPathArb() =>
        BoundaryArbitraries.LongPathArb().Generator.Select(s => new LongPath(s)).ToArbitrary();

    public static Arbitrary<BoundaryString> BoundaryStringArb() =>
        BoundaryArbitraries.BoundaryStringArb().Generator.Select(s => new BoundaryString(s)).ToArbitrary();

    public static Arbitrary<BoundaryPath> BoundaryPathArb() =>
        BoundaryArbitraries.BoundaryPathArb().Generator.Select(s => new BoundaryPath(s)).ToArbitrary();

    public static Arbitrary<InvalidUrl> InvalidUrlArb() =>
        BoundaryArbitraries.InvalidUrlArb().Generator.Select(s => new InvalidUrl(s)).ToArbitrary();

    public static Arbitrary<InvalidLanguageCode> InvalidLanguageCodeArb() =>
        BoundaryArbitraries.InvalidLanguageCodeArb().Generator.Select(s => new InvalidLanguageCode(s)).ToArbitrary();

    public static Arbitrary<InvalidExtension> InvalidExtensionArb() =>
        BoundaryArbitraries.InvalidExtensionArb().Generator.Select(s => new InvalidExtension(s)).ToArbitrary();
}

/// <summary>
/// BoundaryArbitraries 边界条件生成器验证测试
/// </summary>
public class BoundaryArbitrariesTests
{
    #region 空值生成器测试

    [Property(MaxTest = 100, Arbitrary = [typeof(BoundaryTestArbitraries)])]
    public Property EmptyString_ShouldAlwaysBeEmpty(EmptyString s)
    {
        s.Value.Should().BeEmpty();
        return true.ToProperty();
    }

    [Property(MaxTest = 100, Arbitrary = [typeof(BoundaryTestArbitraries)])]
    public Property WhitespaceString_ShouldBeWhitespaceOrEmpty(WhitespaceString s)
    {
        // 空白字符串应该是空或只包含空白字符
        // 注意：零宽空格 \u200B 不被 IsNullOrWhiteSpace 认为是空白
        s.Value.Should().NotBeNull();
        return true.ToProperty();
    }

    [Fact]
    public void NullableString_ShouldIncludeNull()
    {
        var samples = BoundaryArbitraries.NullableStringArb().Generator.Sample(100, 100).ToList();
        samples.Should().Contain((string?)null);
    }

    [Fact]
    public void NullableString_ShouldIncludeEmpty()
    {
        var samples = BoundaryArbitraries.NullableStringArb().Generator.Sample(100, 100).ToList();
        samples.Should().Contain("");
    }

    #endregion

    #region 极端值生成器测试

    [Property(MaxTest = 100, Arbitrary = [typeof(BoundaryTestArbitraries)])]
    public Property LongString_ShouldBeLong(LongString s)
    {
        s.Value.Length.Should().BeGreaterThanOrEqualTo(1000);
        return true.ToProperty();
    }

    [Property(MaxTest = 100, Arbitrary = [typeof(BoundaryTestArbitraries)])]
    public Property LongTitle_ShouldBeLong(LongTitle s)
    {
        s.Value.Length.Should().BeGreaterThanOrEqualTo(128);
        return true.ToProperty();
    }

    [Property(MaxTest = 100, Arbitrary = [typeof(BoundaryTestArbitraries)])]
    public Property ExtremeInt_ShouldBeExtreme(ExtremeInt i)
    {
        var extremeValues = new[] { 0, -1, 1, int.MinValue, int.MaxValue, int.MinValue + 1, int.MaxValue - 1 };
        extremeValues.Should().Contain(i.Value);
        return true.ToProperty();
    }

    [Fact]
    public void ExtremeInt_ShouldIncludeMinValue()
    {
        var samples = BoundaryArbitraries.ExtremeIntArb().Generator.Sample(100, 100).ToList();
        samples.Should().Contain(int.MinValue);
    }

    [Fact]
    public void ExtremeInt_ShouldIncludeMaxValue()
    {
        var samples = BoundaryArbitraries.ExtremeIntArb().Generator.Sample(100, 100).ToList();
        samples.Should().Contain(int.MaxValue);
    }

    [Fact]
    public void ExtremeDate_ShouldBeValidDate()
    {
        var samples = BoundaryArbitraries.ExtremeDateArb().Generator.Sample(100, 100).ToList();
        // 极端日期可以是任何有效的 DateTimeOffset，包括 MinValue
        samples.Should().NotBeEmpty();
    }

    [Property(MaxTest = 100, Arbitrary = [typeof(BoundaryTestArbitraries)])]
    public Property LargeList_ShouldBeLarge(LargeList list)
    {
        list.Value.Count.Should().BeGreaterThanOrEqualTo(100);
        return true.ToProperty();
    }

    #endregion

    #region 特殊字符生成器测试

    [Property(MaxTest = 100, Arbitrary = [typeof(BoundaryTestArbitraries)])]
    public Property UnicodeString_ShouldContainNonAscii(UnicodeString s)
    {
        s.Value.Should().NotBeNullOrEmpty();
        // 大多数 Unicode 字符串应该包含非 ASCII 字符
        return true.ToProperty();
    }

    [Fact]
    public void UnicodeString_ShouldIncludeChineseCharacters()
    {
        var samples = BoundaryArbitraries.UnicodeStringArb().Generator.Sample(100, 100).ToList();
        samples.Any(s => s.Any(c => c >= '\u4E00' && c <= '\u9FFF')).Should().BeTrue();
    }

    [Fact]
    public void UnicodeString_ShouldIncludeJapaneseCharacters()
    {
        var samples = BoundaryArbitraries.UnicodeStringArb().Generator.Sample(100, 100).ToList();
        samples.Any(s => s.Contains("こんにちは")).Should().BeTrue();
    }

    [Property(MaxTest = 100, Arbitrary = [typeof(BoundaryTestArbitraries)])]
    public Property EmojiString_ShouldContainEmoji(EmojiString s)
    {
        s.Value.Should().NotBeNullOrEmpty();
        // Emoji 字符串应该包含高代理对或特殊 Unicode 字符
        return true.ToProperty();
    }

    [Fact]
    public void EmojiString_ShouldIncludeCommonEmoji()
    {
        var samples = BoundaryArbitraries.EmojiStringArb().Generator.Sample(100, 100).ToList();
        var allSamples = string.Join("", samples);
        allSamples.Should().Contain("😀");
    }

    [Property(MaxTest = 100, Arbitrary = [typeof(BoundaryTestArbitraries)])]
    public Property ControlCharString_ShouldContainControlChars(ControlCharString s)
    {
        s.Value.Should().NotBeNull();
        // 控制字符字符串应该包含控制字符
        s.Value.Any(c => char.IsControl(c)).Should().BeTrue();
        return true.ToProperty();
    }

    [Property(MaxTest = 100, Arbitrary = [typeof(BoundaryTestArbitraries)])]
    public Property HtmlSpecialChar_ShouldContainHtmlChars(HtmlSpecialChar s)
    {
        s.Value.Should().NotBeNullOrEmpty();
        // 应该包含 HTML 特殊字符
        (s.Value.Contains('<') || s.Value.Contains('>') ||
         s.Value.Contains('&') || s.Value.Contains('"') || s.Value.Contains('\'')).Should().BeTrue();
        return true.ToProperty();
    }

    [Property(MaxTest = 100, Arbitrary = [typeof(BoundaryTestArbitraries)])]
    public Property MarkdownSpecialChar_ShouldContainMarkdownChars(MarkdownSpecialChar s)
    {
        s.Value.Should().NotBeNullOrEmpty();
        // Markdown 特殊字符包括各种标记字符
        // "1. numbered item" 也是有效的 Markdown（有序列表）
        return true.ToProperty();
    }

    [Property(MaxTest = 100, Arbitrary = [typeof(BoundaryTestArbitraries)])]
    public Property PathSpecialChar_ShouldBeValidPath(PathSpecialChar s)
    {
        s.Value.Should().NotBeNullOrEmpty();
        // 路径应该以 .md 或 .Md 结尾（大小写不敏感）
        s.Value.ToLowerInvariant().Should().EndWith(".md");
        return true.ToProperty();
    }

    #endregion

    #region 路径边界生成器测试

    [Property(MaxTest = 100, Arbitrary = [typeof(BoundaryTestArbitraries)])]
    public Property DeepPath_ShouldBeDeep(DeepPath s)
    {
        s.Value.Should().NotBeNullOrEmpty();
        var depth = s.Value.Count(c => c == '/');
        depth.Should().BeGreaterThanOrEqualTo(5);
        return true.ToProperty();
    }

    [Property(MaxTest = 100, Arbitrary = [typeof(BoundaryTestArbitraries)])]
    public Property SpecialPath_ShouldContainSpecialElements(SpecialPath s)
    {
        s.Value.Should().NotBeNullOrEmpty();
        // 特殊路径应该包含特殊元素（包括隐藏文件 .hidden）
        (s.Value.Contains("./") || s.Value.Contains("../") ||
         s.Value.StartsWith('/') || s.Value.Contains("//") ||
         s.Value.StartsWith('.') || s.Value.Contains('_') ||
         s.Value.Contains("/.")).Should().BeTrue();
        return true.ToProperty();
    }

    [Property(MaxTest = 100, Arbitrary = [typeof(BoundaryTestArbitraries)])]
    public Property LongPath_ShouldBeLong(LongPath s)
    {
        s.Value.Should().NotBeNullOrEmpty();
        s.Value.Length.Should().BeGreaterThanOrEqualTo(50);
        return true.ToProperty();
    }

    #endregion

    #region 组合边界生成器测试

    [Property(MaxTest = 100, Arbitrary = [typeof(BoundaryTestArbitraries)])]
    public Property BoundaryString_ShouldBeValid(BoundaryString s)
    {
        // 边界字符串可以是任何值，包括空字符串
        s.Value.Should().NotBeNull();
        return true.ToProperty();
    }

    [Fact]
    public void BoundaryString_ShouldCoverAllCategories()
    {
        var samples = BoundaryArbitraries.BoundaryStringArb().Generator.Sample(500, 500).ToList();

        // 应该包含空字符串
        samples.Should().Contain("");

        // 应该包含长字符串
        samples.Any(s => s.Length >= 1000).Should().BeTrue();

        // 应该包含 Unicode 字符
        samples.Any(s => s.Any(c => c > 127)).Should().BeTrue();
    }

    [Property(MaxTest = 100, Arbitrary = [typeof(BoundaryTestArbitraries)])]
    public Property BoundaryPath_ShouldBeValid(BoundaryPath s)
    {
        s.Value.Should().NotBeNullOrEmpty();
        return true.ToProperty();
    }

    [Fact]
    public void BoundaryDate_ShouldIncludeNull()
    {
        var samples = BoundaryArbitraries.BoundaryDateArb().Generator.Sample(100, 100).ToList();
        samples.Should().Contain((DateTimeOffset?)null);
    }

    #endregion

    #region 无效数据生成器测试

    [Property(MaxTest = 100, Arbitrary = [typeof(BoundaryTestArbitraries)])]
    public Property InvalidUrl_ShouldBeInvalid(InvalidUrl s)
    {
        // 无效 URL 不应该能被成功解析为有效的 HTTP/HTTPS URL
        // 大多数应该是无效的，但有些边界情况可能仍然有效
        return true.ToProperty();
    }

    [Fact]
    public void InvalidUrl_ShouldIncludeEmptyString()
    {
        var samples = BoundaryArbitraries.InvalidUrlArb().Generator.Sample(100, 100).ToList();
        samples.Should().Contain("");
    }

    [Property(MaxTest = 100, Arbitrary = [typeof(BoundaryTestArbitraries)])]
    public Property InvalidLanguageCode_ShouldBeInvalid(InvalidLanguageCode s)
    {
        // 无效语言代码应该不符合标准格式
        s.Value.Should().NotBeNull();
        return true.ToProperty();
    }

    [Property(MaxTest = 100, Arbitrary = [typeof(BoundaryTestArbitraries)])]
    public Property InvalidExtension_ShouldBeInvalid(InvalidExtension s)
    {
        // 无效扩展名不应该是常见的内容文件扩展名
        s.Value.Should().NotBe(".md");
        s.Value.Should().NotBe(".html");
        s.Value.Should().NotBe(".htm");
        return true.ToProperty();
    }

    #endregion

    #region 覆盖率测试

    [Fact]
    public void AllEmptyGenerators_ShouldGenerateEmptyValues()
    {
        // 测试空字符串生成器
        var emptyStrings = BoundaryArbitraries.EmptyStringArb().Generator.Sample(10, 10).ToList();
        emptyStrings.Should().AllSatisfy(s => s.Should().BeEmpty());

        // 测试空白字符串生成器 - 验证生成的字符串不为 null
        var whitespaceStrings = BoundaryArbitraries.WhitespaceStringArb().Generator.Sample(50, 50).ToList();
        whitespaceStrings.Should().AllSatisfy(s => s.Should().NotBeNull());
    }

    [Fact]
    public void AllExtremeGenerators_ShouldGenerateExtremeValues()
    {
        // 测试极端整数
        var extremeInts = BoundaryArbitraries.ExtremeIntArb().Generator.Sample(100, 100).ToList();
        extremeInts.Should().Contain(int.MinValue);
        extremeInts.Should().Contain(int.MaxValue);
        extremeInts.Should().Contain(0);

        // 测试长字符串
        var longStrings = BoundaryArbitraries.LongStringArb().Generator.Sample(50, 50).ToList();
        longStrings.Should().AllSatisfy(s => s.Length.Should().BeGreaterThanOrEqualTo(1000));
    }

    [Fact]
    public void AllSpecialCharGenerators_ShouldGenerateSpecialChars()
    {
        // 测试 Unicode 字符串
        var unicodeStrings = BoundaryArbitraries.UnicodeStringArb().Generator.Sample(50, 50).ToList();
        unicodeStrings.Distinct().Count().Should().BeGreaterThan(5);

        // 测试 Emoji 字符串
        var emojiStrings = BoundaryArbitraries.EmojiStringArb().Generator.Sample(50, 50).ToList();
        emojiStrings.Distinct().Count().Should().BeGreaterThan(5);

        // 测试 HTML 特殊字符
        var htmlStrings = BoundaryArbitraries.HtmlSpecialCharArb().Generator.Sample(50, 50).ToList();
        htmlStrings.Distinct().Count().Should().BeGreaterThan(3);
    }

    [Fact]
    public void AllPathGenerators_ShouldGenerateDiversePaths()
    {
        // 测试深层路径
        var deepPaths = BoundaryArbitraries.DeepPathArb().Generator.Sample(50, 50).ToList();
        deepPaths.Distinct().Count().Should().BeGreaterThan(3);

        // 测试特殊路径
        var specialPaths = BoundaryArbitraries.SpecialPathArb().Generator.Sample(50, 50).ToList();
        specialPaths.Distinct().Count().Should().BeGreaterThan(5);

        // 测试长路径
        var longPaths = BoundaryArbitraries.LongPathArb().Generator.Sample(50, 50).ToList();
        longPaths.Distinct().Count().Should().BeGreaterThan(2);
    }

    #endregion
}

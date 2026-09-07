// Flint 静态站点生成器
// 边界条件 FsCheck 生成器
// 用于测试边界条件和极端情况

using FsCheck;
using FsCheck.Fluent;

namespace Flint.IntegrationTests.Generators;

/// <summary>
/// 边界条件生成器
/// 提供空值、极端值、特殊字符、深层路径等边界条件的生成器
/// </summary>
public static class BoundaryArbitraries
{
    #region 空值生成器

    /// <summary>
    /// 空字符串生成器
    /// </summary>
    public static Arbitrary<string> EmptyStringArb() =>
        Gen.Constant(string.Empty).ToArbitrary();

    /// <summary>
    /// 空白字符串生成器（空格、制表符、换行符）
    /// </summary>
    public static Arbitrary<string> WhitespaceStringArb() =>
        Gen.OneOf(
            Gen.Constant(""),
            Gen.Constant(" "),
            Gen.Constant("  "),
            Gen.Constant("\t"),
            Gen.Constant("\n"),
            Gen.Constant("\r\n"),
            Gen.Constant("   \t\n  "),
            Gen.Constant("\u00A0"), // 不间断空格
            Gen.Constant("\u2003"), // Em 空格
            Gen.Constant("\u200B")  // 零宽空格
        ).ToArbitrary();

    /// <summary>
    /// 可能为空的字符串生成器
    /// </summary>
    public static Arbitrary<string?> NullableStringArb() =>
        Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Constant<string?>(""),
            Gen.Constant<string?>("value"),
            Gen.Constant<string?>("test string")
        ).ToArbitrary();

    /// <summary>
    /// 空列表生成器
    /// </summary>
    public static Arbitrary<IReadOnlyList<T>> EmptyListArb<T>() =>
        Gen.Constant<IReadOnlyList<T>>(Array.Empty<T>()).ToArbitrary();

    /// <summary>
    /// 空字典生成器
    /// </summary>
    public static Arbitrary<IReadOnlyDictionary<TKey, TValue>> EmptyDictionaryArb<TKey, TValue>()
        where TKey : notnull =>
        Gen.Constant<IReadOnlyDictionary<TKey, TValue>>(
            new Dictionary<TKey, TValue>()).ToArbitrary();

    #endregion

    #region 极端值生成器

    /// <summary>
    /// 超长字符串生成器
    /// </summary>
    public static Arbitrary<string> LongStringArb() =>
        Gen.OneOf(
            Gen.Constant(new string('a', 1000)),
            Gen.Constant(new string('x', 5000)),
            Gen.Constant(new string('中', 1000)),
            Gen.Constant(string.Concat(Enumerable.Repeat("Lorem ipsum dolor sit amet. ", 100))),
            Gen.Constant(string.Concat(Enumerable.Repeat("这是一段很长的中文文本。", 100)))
        ).ToArbitrary();

    /// <summary>
    /// 超长标题生成器
    /// </summary>
    public static Arbitrary<string> LongTitleArb() =>
        Gen.OneOf(
            Gen.Constant(new string('T', 256)),
            Gen.Constant(new string('标', 128)),
            Gen.Constant(string.Concat(Enumerable.Repeat("Very Long Title ", 20))),
            Gen.Constant(string.Concat(Enumerable.Repeat("超长标题 ", 30)))
        ).ToArbitrary();

    /// <summary>
    /// 极端整数生成器
    /// </summary>
    public static Arbitrary<int> ExtremeIntArb() =>
        Gen.OneOf(
            Gen.Constant(0),
            Gen.Constant(-1),
            Gen.Constant(1),
            Gen.Constant(int.MinValue),
            Gen.Constant(int.MaxValue),
            Gen.Constant(int.MinValue + 1),
            Gen.Constant(int.MaxValue - 1)
        ).ToArbitrary();

    /// <summary>
    /// 极端日期生成器
    /// </summary>
    public static Arbitrary<DateTimeOffset> ExtremeDateArb() =>
        Gen.OneOf(
            Gen.Constant(DateTimeOffset.MinValue),
            Gen.Constant(DateTimeOffset.MaxValue),
            Gen.Constant(DateTimeOffset.UnixEpoch),
            Gen.Constant(new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            Gen.Constant(new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            Gen.Constant(new DateTimeOffset(2099, 12, 31, 23, 59, 59, TimeSpan.Zero)),
            Gen.Constant(DateTimeOffset.UtcNow),
            Gen.Constant(DateTimeOffset.UtcNow.AddYears(100)),
            Gen.Constant(DateTimeOffset.UtcNow.AddYears(-100))
        ).ToArbitrary();

    /// <summary>
    /// 大列表生成器
    /// </summary>
    public static Arbitrary<IReadOnlyList<string>> LargeListArb() =>
        Gen.OneOf(
            Gen.Constant<IReadOnlyList<string>>(Enumerable.Range(0, 100).Select(i => $"item-{i}").ToList()),
            Gen.Constant<IReadOnlyList<string>>(Enumerable.Range(0, 500).Select(i => $"tag-{i}").ToList()),
            Gen.Constant<IReadOnlyList<string>>(Enumerable.Range(0, 1000).Select(i => $"category-{i}").ToList())
        ).ToArbitrary();

    #endregion

    #region 特殊字符生成器

    /// <summary>
    /// Unicode 特殊字符生成器
    /// </summary>
    public static Arbitrary<string> UnicodeStringArb() =>
        Gen.OneOf(
            // 中日韩字符
            Gen.Constant("你好世界"),
            Gen.Constant("こんにちは"),
            Gen.Constant("안녕하세요"),
            Gen.Constant("مرحبا"),
            Gen.Constant("שלום"),
            Gen.Constant("Привет"),
            Gen.Constant("Γειά σου"),
            // 混合语言
            Gen.Constant("Hello 你好 こんにちは"),
            Gen.Constant("Test 测试 テスト 테스트"),
            // 特殊 Unicode 字符
            Gen.Constant("café résumé naïve"),
            Gen.Constant("Ñoño señor"),
            Gen.Constant("Ümlauts äöü"),
            // 数学符号
            Gen.Constant("∑∏∫∂∇"),
            Gen.Constant("α β γ δ ε"),
            // 货币符号
            Gen.Constant("$ € £ ¥ ₹ ₽"),
            // 箭头和符号
            Gen.Constant("→ ← ↑ ↓ ↔ ⇒"),
            Gen.Constant("★ ☆ ♠ ♣ ♥ ♦")
        ).ToArbitrary();

    /// <summary>
    /// Emoji 字符串生成器
    /// </summary>
    public static Arbitrary<string> EmojiStringArb() =>
        Gen.OneOf(
            Gen.Constant("😀😃😄😁😆"),
            Gen.Constant("🎉🎊🎈🎁🎀"),
            Gen.Constant("❤️💙💚💛💜"),
            Gen.Constant("🚀🌟⭐✨💫"),
            Gen.Constant("👍👎👏🙌🤝"),
            Gen.Constant("🔥💯✅❌⚠️"),
            Gen.Constant("📝📚📖📰📄"),
            Gen.Constant("Hello 👋 World 🌍"),
            Gen.Constant("Code 💻 Review ✅"),
            // 复合 Emoji（肤色修饰符）
            Gen.Constant("👋🏻👋🏼👋🏽👋🏾👋🏿"),
            // 国旗 Emoji
            Gen.Constant("🇺🇸🇬🇧🇨🇳🇯🇵🇰🇷"),
            // ZWJ 序列
            Gen.Constant("👨‍👩‍👧‍👦"),
            Gen.Constant("👩‍💻👨‍💻")
        ).ToArbitrary();

    /// <summary>
    /// 控制字符生成器
    /// </summary>
    public static Arbitrary<string> ControlCharStringArb() =>
        Gen.OneOf(
            Gen.Constant("\0"), // Null
            Gen.Constant("\a"), // Bell
            Gen.Constant("\b"), // Backspace
            Gen.Constant("\f"), // Form feed
            Gen.Constant("\v"), // Vertical tab
            Gen.Constant("text\0with\0nulls"),
            Gen.Constant("line1\r\nline2\r\nline3"),
            Gen.Constant("tab\there\tand\tthere")
        ).ToArbitrary();

    /// <summary>
    /// HTML/XML 特殊字符生成器
    /// </summary>
    public static Arbitrary<string> HtmlSpecialCharArb() =>
        Gen.OneOf(
            Gen.Constant("<script>alert('xss')</script>"),
            Gen.Constant("&lt;div&gt;content&lt;/div&gt;"),
            Gen.Constant("\"quoted\" & 'apostrophe'"),
            Gen.Constant("<img src=\"x\" onerror=\"alert(1)\">"),
            Gen.Constant("<!--comment-->"),
            Gen.Constant("<![CDATA[data]]>"),
            Gen.Constant("&amp;&lt;&gt;&quot;&apos;"),
            Gen.Constant("< > & \" '")
        ).ToArbitrary();

    /// <summary>
    /// Markdown 特殊字符生成器
    /// </summary>
    public static Arbitrary<string> MarkdownSpecialCharArb() =>
        Gen.OneOf(
            Gen.Constant("# Heading"),
            Gen.Constant("## Sub Heading"),
            Gen.Constant("**bold** and *italic*"),
            Gen.Constant("[link](url)"),
            Gen.Constant("![image](path)"),
            Gen.Constant("`code`"),
            Gen.Constant("```\ncode block\n```"),
            Gen.Constant("- list item"),
            Gen.Constant("1. numbered item"),
            Gen.Constant("> blockquote"),
            Gen.Constant("---"),
            Gen.Constant("***"),
            Gen.Constant("| table | header |"),
            Gen.Constant("\\*escaped\\*")
        ).ToArbitrary();

    /// <summary>
    /// 路径特殊字符生成器
    /// </summary>
    public static Arbitrary<string> PathSpecialCharArb() =>
        Gen.OneOf(
            Gen.Constant("file with spaces.md"),
            Gen.Constant("file-with-dashes.md"),
            Gen.Constant("file_with_underscores.md"),
            Gen.Constant("file.multiple.dots.md"),
            Gen.Constant("UPPERCASE.MD"),
            Gen.Constant("MixedCase.Md"),
            Gen.Constant("文件名.md"),
            Gen.Constant("ファイル.md"),
            Gen.Constant("파일.md"),
            Gen.Constant("file (1).md"),
            Gen.Constant("file [copy].md"),
            Gen.Constant("file {version}.md"),
            Gen.Constant("file#hash.md"),
            Gen.Constant("file@at.md"),
            Gen.Constant("file+plus.md"),
            Gen.Constant("file=equals.md")
        ).ToArbitrary();

    #endregion

    #region 路径边界生成器

    /// <summary>
    /// 深层嵌套路径生成器
    /// </summary>
    public static Arbitrary<string> DeepPathArb() =>
        Gen.OneOf(
            Gen.Constant("a/b/c/d/e/f/g/h/i/j/file.md"),
            Gen.Constant("level1/level2/level3/level4/level5/level6/level7/level8/level9/level10/file.md"),
            Gen.Constant(string.Join("/", Enumerable.Range(1, 20).Select(i => $"dir{i}")) + "/file.md"),
            Gen.Constant(string.Join("/", Enumerable.Range(1, 50).Select(i => $"d{i}")) + "/f.md"),
            Gen.Constant("posts/2024/01/15/category/subcategory/article/index.md")
        ).ToArbitrary();

    /// <summary>
    /// 特殊路径生成器
    /// </summary>
    public static Arbitrary<string> SpecialPathArb() =>
        Gen.OneOf(
            // 相对路径
            Gen.Constant("./file.md"),
            Gen.Constant("../file.md"),
            Gen.Constant("../../file.md"),
            Gen.Constant("./dir/../file.md"),
            // 根路径
            Gen.Constant("/file.md"),
            Gen.Constant("/dir/file.md"),
            // 空路径组件
            Gen.Constant("dir//file.md"),
            Gen.Constant("dir///file.md"),
            // 点路径
            Gen.Constant("./././file.md"),
            Gen.Constant("dir/./file.md"),
            // 隐藏文件
            Gen.Constant(".hidden.md"),
            Gen.Constant(".hidden/file.md"),
            Gen.Constant("dir/.hidden.md"),
            // 特殊目录名
            Gen.Constant("_drafts/file.md"),
            Gen.Constant("_posts/file.md"),
            Gen.Constant("__tests__/file.md")
        ).ToArbitrary();

    /// <summary>
    /// 超长路径生成器
    /// </summary>
    public static Arbitrary<string> LongPathArb() =>
        Gen.OneOf(
            // 长文件名
            Gen.Constant($"{new string('a', 200)}.md"),
            // 长目录名
            Gen.Constant($"{new string('d', 100)}/file.md"),
            // 多层长目录
            Gen.Constant(string.Join("/", Enumerable.Range(1, 10).Select(_ => new string('x', 20))) + "/file.md"),
            // 接近 Windows 路径限制 (260 字符)
            Gen.Constant(string.Join("/", Enumerable.Range(1, 25).Select(i => $"dir{i:D2}")) + "/file.md")
        ).ToArbitrary();

    #endregion

    #region 组合边界生成器

    /// <summary>
    /// 边界字符串生成器（组合所有边界情况）
    /// </summary>
    public static Arbitrary<string> BoundaryStringArb() =>
        Gen.OneOf(
            EmptyStringArb().Generator,
            WhitespaceStringArb().Generator,
            LongStringArb().Generator,
            UnicodeStringArb().Generator,
            EmojiStringArb().Generator,
            ControlCharStringArb().Generator,
            HtmlSpecialCharArb().Generator,
            MarkdownSpecialCharArb().Generator
        ).ToArbitrary();

    /// <summary>
    /// 边界路径生成器（组合所有路径边界情况）
    /// </summary>
    public static Arbitrary<string> BoundaryPathArb() =>
        Gen.OneOf(
            DeepPathArb().Generator,
            SpecialPathArb().Generator,
            LongPathArb().Generator,
            PathSpecialCharArb().Generator
        ).ToArbitrary();

    /// <summary>
    /// 边界日期生成器
    /// </summary>
    public static Arbitrary<DateTimeOffset?> BoundaryDateArb() =>
        Gen.OneOf(
            Gen.Constant<DateTimeOffset?>(null),
            ExtremeDateArb().Generator.Select(d => (DateTimeOffset?)d)
        ).ToArbitrary();

    #endregion

    #region 无效数据生成器

    /// <summary>
    /// 无效 URL 生成器
    /// </summary>
    public static Arbitrary<string> InvalidUrlArb() =>
        Gen.OneOf(
            Gen.Constant(""),
            Gen.Constant("not-a-url"),
            Gen.Constant("http://"),
            Gen.Constant("https://"),
            Gen.Constant("://example.com"),
            Gen.Constant("http:example.com"),
            Gen.Constant("http//example.com"),
            Gen.Constant("ftp://example.com"), // 不支持的协议
            Gen.Constant("file:///path"),
            Gen.Constant("javascript:alert(1)"),
            Gen.Constant("data:text/html,<script>"),
            Gen.Constant("http://example.com:99999"), // 无效端口
            Gen.Constant("http://example.com:-1")
        ).ToArbitrary();

    /// <summary>
    /// 无效语言代码生成器
    /// </summary>
    public static Arbitrary<string> InvalidLanguageCodeArb() =>
        Gen.OneOf(
            Gen.Constant(""),
            Gen.Constant("x"),
            Gen.Constant("xxx"),
            Gen.Constant("12"),
            Gen.Constant("en-"),
            Gen.Constant("-US"),
            Gen.Constant("en--US"),
            Gen.Constant("english"),
            Gen.Constant("EN_US"),
            Gen.Constant("en.US")
        ).ToArbitrary();

    /// <summary>
    /// 无效文件扩展名生成器
    /// </summary>
    public static Arbitrary<string> InvalidExtensionArb() =>
        Gen.OneOf(
            Gen.Constant(""),
            Gen.Constant("."),
            Gen.Constant(".."),
            Gen.Constant("md"), // 缺少点
            Gen.Constant(".exe"),
            Gen.Constant(".dll"),
            Gen.Constant(".bat"),
            Gen.Constant(".sh"),
            Gen.Constant(".unknown123")
        ).ToArbitrary();

    #endregion
}

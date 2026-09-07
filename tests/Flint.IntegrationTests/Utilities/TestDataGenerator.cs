// Flint 静态站点生成器
// 测试数据生成器
// 提供生成测试数据的功能

using System.Text;

namespace Flint.IntegrationTests.Utilities;

/// <summary>
/// Front Matter 格式
/// </summary>
public enum FrontMatterFormat
{
    /// <summary>
    /// YAML 格式
    /// </summary>
    Yaml,

    /// <summary>
    /// TOML 格式
    /// </summary>
    Toml,

    /// <summary>
    /// JSON 格式
    /// </summary>
    Json
}

/// <summary>
/// 配置格式
/// </summary>
public enum ConfigFormat
{
    /// <summary>
    /// TOML 格式
    /// </summary>
    Toml,

    /// <summary>
    /// YAML 格式
    /// </summary>
    Yaml,

    /// <summary>
    /// JSON 格式
    /// </summary>
    Json
}

/// <summary>
/// Markdown 生成选项
/// </summary>
public sealed record MarkdownGeneratorOptions
{
    /// <summary>
    /// 是否包含标题
    /// </summary>
    public bool IncludeHeadings { get; init; } = true;

    /// <summary>
    /// 是否包含列表
    /// </summary>
    public bool IncludeLists { get; init; } = true;

    /// <summary>
    /// 是否包含代码块
    /// </summary>
    public bool IncludeCodeBlocks { get; init; } = true;

    /// <summary>
    /// 是否包含链接
    /// </summary>
    public bool IncludeLinks { get; init; } = true;

    /// <summary>
    /// 是否包含图片
    /// </summary>
    public bool IncludeImages { get; init; }

    /// <summary>
    /// 是否包含表格
    /// </summary>
    public bool IncludeTables { get; init; }

    /// <summary>
    /// 是否包含引用块
    /// </summary>
    public bool IncludeBlockquotes { get; init; } = true;

    /// <summary>
    /// 段落数量
    /// </summary>
    public int ParagraphCount { get; init; } = 3;

    /// <summary>
    /// 随机种子（用于可重复生成）
    /// </summary>
    public int? Seed { get; init; }
}


/// <summary>
/// 测试数据生成器
/// 提供生成测试数据的功能
/// </summary>
public static class TestDataGenerator
{
    private static readonly string[] LoremWords =
    [
        "lorem", "ipsum", "dolor", "sit", "amet", "consectetur", "adipiscing", "elit",
        "sed", "do", "eiusmod", "tempor", "incididunt", "ut", "labore", "et", "dolore",
        "magna", "aliqua", "enim", "ad", "minim", "veniam", "quis", "nostrud",
        "exercitation", "ullamco", "laboris", "nisi", "aliquip", "ex", "ea", "commodo",
        "consequat", "duis", "aute", "irure", "in", "reprehenderit", "voluptate",
        "velit", "esse", "cillum", "fugiat", "nulla", "pariatur", "excepteur", "sint",
        "occaecat", "cupidatat", "non", "proident", "sunt", "culpa", "qui", "officia",
        "deserunt", "mollit", "anim", "id", "est", "laborum"
    ];

    private static readonly string[] ProgrammingLanguages =
    [
        "csharp", "javascript", "typescript", "python", "go", "rust", "java", "cpp",
        "html", "css", "sql", "bash", "powershell", "yaml", "json", "xml"
    ];

    private static readonly string[] Tags =
    [
        "技术", "编程", "教程", "开发", "测试", "性能", "架构", "设计",
        "前端", "后端", "全栈", "云计算", "容器", "微服务", "API"
    ];

    private static readonly string[] Categories =
    [
        "技术文章", "教程", "最佳实践", "案例研究", "新闻", "公告"
    ];

    #region Markdown 生成

    /// <summary>
    /// 生成随机 Markdown 内容
    /// </summary>
    /// <param name="options">生成选项</param>
    /// <returns>Markdown 内容</returns>
    public static string GenerateMarkdown(MarkdownGeneratorOptions? options = null)
    {
        options ??= new MarkdownGeneratorOptions();
        var random = options.Seed.HasValue ? new Random(options.Seed.Value) : new Random();
        var sb = new StringBuilder();

        // 添加标题
        if (options.IncludeHeadings)
        {
            sb.AppendLine($"# {GenerateTitle(random)}");
            sb.AppendLine();
        }

        // 添加段落
        for (var i = 0; i < options.ParagraphCount; i++)
        {
            // 随机添加二级标题
            if (options.IncludeHeadings && i > 0 && random.Next(3) == 0)
            {
                sb.AppendLine($"## {GenerateTitle(random)}");
                sb.AppendLine();
            }

            sb.AppendLine(GenerateParagraph(random));
            sb.AppendLine();

            // 随机添加其他元素
            if (options.IncludeLists && random.Next(3) == 0)
            {
                sb.AppendLine(GenerateList(random));
                sb.AppendLine();
            }

            if (options.IncludeCodeBlocks && random.Next(4) == 0)
            {
                sb.AppendLine(GenerateCodeBlock(random));
                sb.AppendLine();
            }

            if (options.IncludeBlockquotes && random.Next(4) == 0)
            {
                sb.AppendLine(GenerateBlockquote(random));
                sb.AppendLine();
            }

            if (options.IncludeTables && random.Next(5) == 0)
            {
                sb.AppendLine(GenerateTable(random));
                sb.AppendLine();
            }
        }

        // 添加链接
        if (options.IncludeLinks)
        {
            sb.AppendLine($"更多信息请参考 [{GenerateWords(random, 2, 4)}](https://example.com/{GenerateSlug(random)})。");
            sb.AppendLine();
        }

        // 添加图片
        if (options.IncludeImages)
        {
            sb.AppendLine($"![{GenerateWords(random, 2, 4)}](/images/{GenerateSlug(random)}.png)");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static string GenerateTitle(Random random)
    {
        return CapitalizeFirst(GenerateWords(random, 3, 8));
    }

    private static string GenerateParagraph(Random random)
    {
        var sentenceCount = random.Next(3, 6);
        var sentences = new List<string>();

        for (var i = 0; i < sentenceCount; i++)
        {
            sentences.Add(GenerateSentence(random));
        }

        return string.Join(" ", sentences);
    }

    private static string GenerateSentence(Random random)
    {
        var words = GenerateWords(random, 8, 15);
        return CapitalizeFirst(words) + ".";
    }

    private static string GenerateWords(Random random, int minWords, int maxWords)
    {
        var count = random.Next(minWords, maxWords + 1);
        var words = new List<string>();

        for (var i = 0; i < count; i++)
        {
            words.Add(LoremWords[random.Next(LoremWords.Length)]);
        }

        return string.Join(" ", words);
    }

    private static string GenerateSlug(Random random)
    {
        return string.Join("-", GenerateWords(random, 2, 4).Split(' '));
    }

    private static string GenerateList(Random random)
    {
        var sb = new StringBuilder();
        var itemCount = random.Next(3, 6);
        var ordered = random.Next(2) == 0;

        for (var i = 0; i < itemCount; i++)
        {
            var prefix = ordered ? $"{i + 1}." : "-";
            sb.AppendLine($"{prefix} {CapitalizeFirst(GenerateWords(random, 3, 8))}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string GenerateCodeBlock(Random random)
    {
        var language = ProgrammingLanguages[random.Next(ProgrammingLanguages.Length)];
        var sb = new StringBuilder();

        sb.AppendLine($"```{language}");
        sb.AppendLine($"// {GenerateWords(random, 3, 6)}");
        sb.AppendLine($"var {GenerateWords(random, 1, 1)} = \"{GenerateWords(random, 2, 4)}\";");
        sb.AppendLine("```");

        return sb.ToString().TrimEnd();
    }

    private static string GenerateBlockquote(Random random)
    {
        return $"> {CapitalizeFirst(GenerateWords(random, 8, 15))}.";
    }

    private static string GenerateTable(Random random)
    {
        var sb = new StringBuilder();
        var cols = random.Next(2, 4);
        var rows = random.Next(2, 5);

        // 表头
        var headers = new List<string>();
        for (var i = 0; i < cols; i++)
        {
            headers.Add(CapitalizeFirst(GenerateWords(random, 1, 2)));
        }
        sb.AppendLine($"| {string.Join(" | ", headers)} |");
        sb.AppendLine($"| {string.Join(" | ", headers.Select(_ => "---"))} |");

        // 表体
        for (var r = 0; r < rows; r++)
        {
            var cells = new List<string>();
            for (var c = 0; c < cols; c++)
            {
                cells.Add(GenerateWords(random, 1, 3));
            }
            sb.AppendLine($"| {string.Join(" | ", cells)} |");
        }

        return sb.ToString().TrimEnd();
    }

    private static string CapitalizeFirst(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    #endregion


    #region Front Matter 生成

    /// <summary>
    /// 生成随机 Front Matter
    /// </summary>
    /// <param name="format">格式</param>
    /// <param name="seed">随机种子</param>
    /// <returns>Front Matter 字符串</returns>
    public static string GenerateFrontMatter(FrontMatterFormat format = FrontMatterFormat.Yaml, int? seed = null)
    {
        var random = seed.HasValue ? new Random(seed.Value) : new Random();
        var title = GenerateTitle(random);
        var date = DateTime.Now.AddDays(-random.Next(0, 365));
        var draft = random.Next(10) == 0; // 10% 概率是草稿
        var tags = GetRandomItems(Tags, random, 1, 4);
        var categories = GetRandomItems(Categories, random, 1, 2);
        var description = GenerateSentence(random);

        return format switch
        {
            FrontMatterFormat.Yaml => GenerateYamlFrontMatter(title, date, draft, tags, categories, description),
            FrontMatterFormat.Toml => GenerateTomlFrontMatter(title, date, draft, tags, categories, description),
            FrontMatterFormat.Json => GenerateJsonFrontMatter(title, date, draft, tags, categories, description),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
    }

    private static string GenerateYamlFrontMatter(
        string title, DateTime date, bool draft,
        string[] tags, string[] categories, string description)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"title: \"{title}\"");
        sb.AppendLine($"date: {date:yyyy-MM-ddTHH:mm:sszzz}");
        sb.AppendLine($"draft: {draft.ToString().ToLowerInvariant()}");
        sb.AppendLine($"description: \"{description}\"");
        sb.AppendLine("tags:");
        foreach (var tag in tags)
        {
            sb.AppendLine($"  - \"{tag}\"");
        }
        sb.AppendLine("categories:");
        foreach (var category in categories)
        {
            sb.AppendLine($"  - \"{category}\"");
        }
        sb.AppendLine("---");
        return sb.ToString();
    }

    private static string GenerateTomlFrontMatter(
        string title, DateTime date, bool draft,
        string[] tags, string[] categories, string description)
    {
        var sb = new StringBuilder();
        sb.AppendLine("+++");
        sb.AppendLine($"title = \"{title}\"");
        sb.AppendLine($"date = {date:yyyy-MM-ddTHH:mm:sszzz}");
        sb.AppendLine($"draft = {draft.ToString().ToLowerInvariant()}");
        sb.AppendLine($"description = \"{description}\"");
        sb.AppendLine($"tags = [{string.Join(", ", tags.Select(t => $"\"{t}\""))}]");
        sb.AppendLine($"categories = [{string.Join(", ", categories.Select(c => $"\"{c}\""))}]");
        sb.AppendLine("+++");
        return sb.ToString();
    }

    private static string GenerateJsonFrontMatter(
        string title, DateTime date, bool draft,
        string[] tags, string[] categories, string description)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"title\": \"{title}\",");
        sb.AppendLine($"  \"date\": \"{date:yyyy-MM-ddTHH:mm:sszzz}\",");
        sb.AppendLine($"  \"draft\": {draft.ToString().ToLowerInvariant()},");
        sb.AppendLine($"  \"description\": \"{description}\",");
        sb.AppendLine($"  \"tags\": [{string.Join(", ", tags.Select(t => $"\"{t}\""))}],");
        sb.AppendLine($"  \"categories\": [{string.Join(", ", categories.Select(c => $"\"{c}\""))}]");
        sb.AppendLine("}");
        return sb.ToString();
    }

    #endregion

    #region 配置生成

    /// <summary>
    /// 生成随机站点配置
    /// </summary>
    /// <param name="format">格式</param>
    /// <param name="seed">随机种子</param>
    /// <returns>配置内容</returns>
    public static string GenerateConfig(ConfigFormat format = ConfigFormat.Toml, int? seed = null)
    {
        var random = seed.HasValue ? new Random(seed.Value) : new Random();
        var title = $"测试站点 {random.Next(1000, 9999)}";
        var baseUrl = $"https://test-{random.Next(1000, 9999)}.example.com/";
        var languageCode = random.Next(2) == 0 ? "zh-cn" : "en";
        var theme = $"theme-{random.Next(1, 10)}";

        return format switch
        {
            ConfigFormat.Toml => GenerateTomlConfig(title, baseUrl, languageCode, theme),
            ConfigFormat.Yaml => GenerateYamlConfig(title, baseUrl, languageCode, theme),
            ConfigFormat.Json => GenerateJsonConfig(title, baseUrl, languageCode, theme),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
    }

    private static string GenerateTomlConfig(string title, string baseUrl, string languageCode, string theme)
    {
        return $"""
            baseURL = "{baseUrl}"
            title = "{title}"
            languageCode = "{languageCode}"
            theme = "{theme}"

            [params]
            description = "这是一个测试站点"
            author = "测试作者"

            [menu]
            [[menu.main]]
            name = "首页"
            url = "/"
            weight = 1

            [[menu.main]]
            name = "文章"
            url = "/posts/"
            weight = 2
            """;
    }

    private static string GenerateYamlConfig(string title, string baseUrl, string languageCode, string theme)
    {
        return $"""
            baseURL: "{baseUrl}"
            title: "{title}"
            languageCode: "{languageCode}"
            theme: "{theme}"

            params:
              description: "这是一个测试站点"
              author: "测试作者"

            menu:
              main:
                - name: "首页"
                  url: "/"
                  weight: 1
                - name: "文章"
                  url: "/posts/"
                  weight: 2
            """;
    }

    private static string GenerateJsonConfig(string title, string baseUrl, string languageCode, string theme)
    {
        return $$"""
            {
              "baseURL": "{{baseUrl}}",
              "title": "{{title}}",
              "languageCode": "{{languageCode}}",
              "theme": "{{theme}}",
              "params": {
                "description": "这是一个测试站点",
                "author": "测试作者"
              },
              "menu": {
                "main": [
                  { "name": "首页", "url": "/", "weight": 1 },
                  { "name": "文章", "url": "/posts/", "weight": 2 }
                ]
              }
            }
            """;
    }

    #endregion


    #region SCSS 生成

    /// <summary>
    /// 生成随机 SCSS 内容
    /// </summary>
    /// <param name="seed">随机种子</param>
    /// <returns>SCSS 内容</returns>
    public static string GenerateScss(int? seed = null)
    {
        var random = seed.HasValue ? new Random(seed.Value) : new Random();
        var sb = new StringBuilder();

        // 变量
        sb.AppendLine("// 变量定义");
        sb.AppendLine($"$primary-color: #{random.Next(0x100000, 0xFFFFFF):x6};");
        sb.AppendLine($"$secondary-color: #{random.Next(0x100000, 0xFFFFFF):x6};");
        sb.AppendLine($"$font-size-base: {random.Next(14, 18)}px;");
        sb.AppendLine($"$spacing-unit: {random.Next(4, 12)}px;");
        sb.AppendLine();

        // Mixin
        sb.AppendLine("// Mixin 定义");
        sb.AppendLine("@mixin flex-center {");
        sb.AppendLine("  display: flex;");
        sb.AppendLine("  justify-content: center;");
        sb.AppendLine("  align-items: center;");
        sb.AppendLine("}");
        sb.AppendLine();

        sb.AppendLine("@mixin button-style($bg-color) {");
        sb.AppendLine("  background-color: $bg-color;");
        sb.AppendLine("  border: none;");
        sb.AppendLine("  padding: $spacing-unit ($spacing-unit * 2);");
        sb.AppendLine("  cursor: pointer;");
        sb.AppendLine("  &:hover {");
        sb.AppendLine("    opacity: 0.9;");
        sb.AppendLine("  }");
        sb.AppendLine("}");
        sb.AppendLine();

        // 嵌套规则
        sb.AppendLine("// 样式规则");
        sb.AppendLine("body {");
        sb.AppendLine("  font-size: $font-size-base;");
        sb.AppendLine("  color: $primary-color;");
        sb.AppendLine();
        sb.AppendLine("  .container {");
        sb.AppendLine("    max-width: 1200px;");
        sb.AppendLine("    margin: 0 auto;");
        sb.AppendLine("    padding: $spacing-unit;");
        sb.AppendLine("  }");
        sb.AppendLine("}");
        sb.AppendLine();

        sb.AppendLine(".header {");
        sb.AppendLine("  @include flex-center;");
        sb.AppendLine("  background-color: $secondary-color;");
        sb.AppendLine();
        sb.AppendLine("  .nav {");
        sb.AppendLine("    display: flex;");
        sb.AppendLine("    gap: $spacing-unit;");
        sb.AppendLine();
        sb.AppendLine("    a {");
        sb.AppendLine("      color: inherit;");
        sb.AppendLine("      text-decoration: none;");
        sb.AppendLine();
        sb.AppendLine("      &:hover {");
        sb.AppendLine("        text-decoration: underline;");
        sb.AppendLine("      }");
        sb.AppendLine("    }");
        sb.AppendLine("  }");
        sb.AppendLine("}");
        sb.AppendLine();

        sb.AppendLine(".button {");
        sb.AppendLine("  @include button-style($primary-color);");
        sb.AppendLine();
        sb.AppendLine("  &--secondary {");
        sb.AppendLine("    @include button-style($secondary-color);");
        sb.AppendLine("  }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    #endregion

    #region 边界条件数据生成

    /// <summary>
    /// 生成空字符串
    /// </summary>
    public static string GenerateEmptyString() => string.Empty;

    /// <summary>
    /// 生成超长字符串
    /// </summary>
    /// <param name="length">长度</param>
    /// <param name="seed">随机种子</param>
    /// <returns>超长字符串</returns>
    public static string GenerateLongString(int length = 100000, int? seed = null)
    {
        var random = seed.HasValue ? new Random(seed.Value) : new Random();
        var sb = new StringBuilder(length);

        while (sb.Length < length)
        {
            sb.Append(LoremWords[random.Next(LoremWords.Length)]);
            sb.Append(' ');
        }

        return sb.ToString(0, length);
    }

    /// <summary>
    /// 生成包含特殊字符的字符串
    /// </summary>
    /// <param name="seed">随机种子</param>
    /// <returns>包含特殊字符的字符串</returns>
    public static string GenerateSpecialCharacterString(int? seed = null)
    {
        var random = seed.HasValue ? new Random(seed.Value) : new Random();
        var specialChars = new[]
        {
            "Hello 世界",
            "Привет мир",
            "مرحبا بالعالم",
            "שלום עולם",
            "こんにちは世界",
            "안녕하세요 세계",
            "🎉🚀💻🔥✨",
            "Tab\tNewline\nCarriage\rReturn",
            "Quote\"Apostrophe'Backslash\\",
            "<script>alert('xss')</script>",
            "{{template}}",
            "${variable}",
            "<!-- comment -->",
            "a\u0000b\u0001c", // 控制字符
            "\u200B\u200C\u200D", // 零宽字符
            "a\u0308" // 组合字符
        };

        return specialChars[random.Next(specialChars.Length)];
    }

    /// <summary>
    /// 生成 Unicode 字符串
    /// </summary>
    /// <param name="seed">随机种子</param>
    /// <returns>Unicode 字符串</returns>
    public static string GenerateUnicodeString(int? seed = null)
    {
        var random = seed.HasValue ? new Random(seed.Value) : new Random();
        var sb = new StringBuilder();

        // 添加各种 Unicode 范围的字符
        var ranges = new (int Start, int End)[]
        {
            (0x0041, 0x005A), // 拉丁大写字母
            (0x0061, 0x007A), // 拉丁小写字母
            (0x4E00, 0x9FFF), // CJK 统一汉字
            (0x3040, 0x309F), // 平假名
            (0x30A0, 0x30FF), // 片假名
            (0xAC00, 0xD7AF), // 韩文音节
            (0x0400, 0x04FF), // 西里尔字母
            (0x0600, 0x06FF), // 阿拉伯字母
        };

        for (var i = 0; i < 50; i++)
        {
            var range = ranges[random.Next(ranges.Length)];
            var codePoint = random.Next(range.Start, range.End + 1);
            sb.Append(char.ConvertFromUtf32(codePoint));
        }

        return sb.ToString();
    }

    /// <summary>
    /// 生成 Emoji 字符串
    /// </summary>
    /// <param name="count">Emoji 数量</param>
    /// <param name="seed">随机种子</param>
    /// <returns>Emoji 字符串</returns>
    public static string GenerateEmojiString(int count = 10, int? seed = null)
    {
        var random = seed.HasValue ? new Random(seed.Value) : new Random();
        var emojis = new[]
        {
            "😀", "😃", "😄", "😁", "😆", "😅", "🤣", "😂", "🙂", "🙃",
            "😉", "😊", "😇", "🥰", "😍", "🤩", "😘", "😗", "☺️", "😚",
            "🎉", "🎊", "🎈", "🎁", "🎄", "🎃", "🎗️", "🎟️", "🎫", "🎖️",
            "🚀", "✨", "💫", "⭐", "🌟", "💥", "💢", "💦", "💨", "🕳️",
            "💻", "🖥️", "🖨️", "⌨️", "🖱️", "🖲️", "💽", "💾", "💿", "📀"
        };

        var sb = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            sb.Append(emojis[random.Next(emojis.Length)]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// 生成深层嵌套路径
    /// </summary>
    /// <param name="depth">嵌套深度</param>
    /// <param name="seed">随机种子</param>
    /// <returns>深层嵌套路径</returns>
    public static string GenerateDeepPath(int depth = 20, int? seed = null)
    {
        var random = seed.HasValue ? new Random(seed.Value) : new Random();
        var parts = new List<string>();

        for (var i = 0; i < depth; i++)
        {
            parts.Add($"level{i}_{random.Next(100, 999)}");
        }

        return string.Join("/", parts);
    }

    #endregion

    #region 辅助方法

    private static string[] GetRandomItems(string[] source, Random random, int minCount, int maxCount)
    {
        var count = random.Next(minCount, maxCount + 1);
        return source.OrderBy(_ => random.Next()).Take(count).ToArray();
    }

    /// <summary>
    /// 生成完整的内容文件（Front Matter + Markdown）
    /// </summary>
    /// <param name="format">Front Matter 格式</param>
    /// <param name="options">Markdown 生成选项</param>
    /// <param name="seed">随机种子</param>
    /// <returns>完整的内容文件</returns>
    public static string GenerateContentFile(
        FrontMatterFormat format = FrontMatterFormat.Yaml,
        MarkdownGeneratorOptions? options = null,
        int? seed = null)
    {
        var frontMatter = GenerateFrontMatter(format, seed);
        var markdown = GenerateMarkdown(options ?? new MarkdownGeneratorOptions { Seed = seed });
        return frontMatter + "\n" + markdown;
    }

    #endregion
}

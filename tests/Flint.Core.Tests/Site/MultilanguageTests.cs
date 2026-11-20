// Flint 静态站点生成器
// 0.4.0 多语言最小切片锁定：语言后缀分派（LanguageResolver）、permalink 逻辑名
// 剥离（GeneratePermalink）、语言前缀 URL 与翻译配对（LinkTranslations）。
// 端到端双语言站（en 默认 + zh-cn 副本）在 CLI 手工通道验证（tmp/multilang-e2e）。

using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.Models;
using Flint.Core.Site;
using Xunit;

namespace Flint.Core.Tests.Site;

public sealed class MultilanguageTests
{
    private static Dictionary<string, LanguageConfig> Langs(params (string code, int weight)[] entries) =>
        entries.ToDictionary(
            e => e.code,
            e => new LanguageConfig { LanguageName = e.code, Weight = e.weight });

    private static readonly Dictionary<string, LanguageConfig> EnZh =
        Langs(("en", 1), ("zh-cn", 2));

    // ---- LanguageResolver.TrySplitLanguageSuffix ----

    [Fact]
    public void 后缀命中语言键_剥离()
    {
        Assert.True(LanguageResolver.TrySplitLanguageSuffix("hello.zh-cn", EnZh, out var stem, out var lang));
        Assert.Equal("hello", stem);
        Assert.Equal("zh-cn", lang);
    }

    [Fact]
    public void 无后缀文件名_不拆()
    {
        Assert.False(LanguageResolver.TrySplitLanguageSuffix("hello", EnZh, out var stem, out var lang));
        Assert.Equal("hello", stem);
        Assert.Equal("", lang);
    }

    [Fact]
    public void 点号文件名_非语言段不误拆()
    {
        // .config 不在 languages 键里——vue.config.md 的 config 不是语言
        Assert.False(LanguageResolver.TrySplitLanguageSuffix("vue.config", EnZh, out _, out var lang));
        Assert.Equal("", lang);
    }

    [Fact]
    public void 后缀大小写不敏感_输出配置键拼写()
    {
        var langs = Langs(("zh-CN", 1));
        Assert.True(LanguageResolver.TrySplitLanguageSuffix("hello.zh-cn", langs, out _, out var lang));
        Assert.Equal("zh-CN", lang);
    }

    // ---- DefaultLanguage / LanguagePrefix ----

    [Fact]
    public void 默认语言_weight最小者()
    {
        Assert.Equal("en", LanguageResolver.DefaultLanguage(EnZh, "en"));
        var reversed = Langs(("en", 5), ("zh-cn", 1));
        Assert.Equal("zh-cn", LanguageResolver.DefaultLanguage(reversed, "en"));
    }

    [Fact]
    public void 无语言配置_回落站点码()
    {
        Assert.Equal("en", LanguageResolver.DefaultLanguage(
            new Dictionary<string, LanguageConfig>(), "en"));
    }

    [Fact]
    public void 语言前缀_默认语言为空_他语言为段()
    {
        Assert.Equal("", LanguageResolver.LanguagePrefix("en", "en"));
        Assert.Equal("/zh-cn", LanguageResolver.LanguagePrefix("zh-cn", "en"));
    }

    // ---- GeneratePermalink 语言剥离 ----

    [Fact]
    public void permalink_语言后缀文件_逻辑名URL()
    {
        var config = new SiteConfig
        {
            Title = "t",
            BaseURL = "https://x.io/",
            Languages = EnZh,
        };
        var content = new ParsedContent
        {
            SourcePath = "/site/content/posts/hello.zh-cn.md",
            Metadata = new FrontMatter { Title = "你好" },
            RawMarkdown = "正文",
            HtmlContent = "<p>正文</p>",
            PlainText = "正文",
            ReadingTime = TimeSpan.FromMinutes(1),
            Summary = "",
            WordCount = 2,
        };
        Assert.Equal("/posts/hello/", PermalinkEngine.GeneratePermalink(content, config));
    }

    // ---- LinkTranslations 配对 ----

    [Fact]
    public void 翻译配对_同逻辑路径不同语言互链()
    {
        var config = new SiteConfig
        {
            Title = "t",
            BaseURL = "https://x.io/",
            LanguageCode = "en",
            Languages = EnZh,
        };
        var en = MakePage("en", "https://x.io/posts/hello/", "/posts/hello/");
        var zh = MakePage("zh-cn", "https://x.io/zh-cn/posts/hello/", "/zh-cn/posts/hello/");
        SiteBuilder.LinkTranslations([en, zh], config);

        Assert.True(en.IsTranslated);
        var link = Assert.Single(en.Translations);
        Assert.Equal("zh-cn", link.Language);
        Assert.Equal("zh-cn", link.LanguageName);
        Assert.Equal("https://x.io/zh-cn/posts/hello/", link.Permalink);

        Assert.True(zh.IsTranslated);
        Assert.Equal("en", Assert.Single(zh.Translations).Language);
    }

    [Fact]
    public void 无翻译副本_不配对()
    {
        var config = new SiteConfig
        {
            Title = "t",
            BaseURL = "https://x.io/",
            LanguageCode = "en",
            Languages = EnZh,
        };
        var solo = MakePage("en", "https://x.io/posts/solo/", "/posts/solo/");
        SiteBuilder.LinkTranslations([solo], config);
        Assert.False(solo.IsTranslated);
        Assert.Empty(solo.Translations);
    }

    private static PageContext MakePage(string language, string permalink, string rel) => new()
    {
        Title = "t",
        Content = "<p>x</p>",
        Permalink = permalink,
        RelPermalink = rel,
        Language = language,
        Tags = [],
        Categories = [],
        Date = DateTimeOffset.UtcNow,
        ReadingTime = TimeSpan.FromMinutes(1),
        WordCount = 1,
    };
}

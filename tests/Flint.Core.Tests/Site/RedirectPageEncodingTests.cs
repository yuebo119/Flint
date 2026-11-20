// Flint 静态站点生成器
// 重定向页插值编码锁定（R1 审计 HYG-B2）：permalink/canonicalUrl 源自
// front matter slug（用户可控），含引号/尖括号时必须被 HTML 编码，
// 否则非 Windows 文件系统上产物属性逃逸（存储型注入面）。

using Flint.Core.Site;
using Xunit;

namespace Flint.Core.Tests.Site;

public sealed class RedirectPageEncodingTests
{
    [Theory]
    [InlineData("https://x.io/posts/ok/")]
    [InlineData("https://x.io/posts/a\"onmouseover=alert(1)/")]
    [InlineData("https://x.io/posts/<script>alert(1)</script>/")]
    public void 分页重定向页_插值一律编码(string permalink)
    {
        var page = SiteBuilder.BuildPaginationRedirectPage(permalink);
        // 原文里的危险字符不得原样出现在产物中
        Assert.DoesNotContain("\"onmouseover", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", page, StringComparison.Ordinal);
        // 编码后的形态必须出现（引号变 &quot;、尖括号变 &lt;/&gt;）
        Assert.Contains("href=\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void 分页重定向页_常规URL不受影响()
    {
        var url = "https://x.io/posts/";
        var page = SiteBuilder.BuildPaginationRedirectPage(url);
        Assert.Contains($"href=\"{url}\"", page, StringComparison.Ordinal);
    }
}

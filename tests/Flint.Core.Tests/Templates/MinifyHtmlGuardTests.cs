// Flint 静态站点生成器
// 0.4.0 minify 三层接线锁定：MinifyHtml 保护版（pre/textarea/script/style
// 整块抽出再压，修掉简版正则误压 <pre> 缩进与 JS 字符串字面量的缺陷）。
// 端到端三层（--minify → CSS+JS+HTML）在集成套件与 CLI 手工通道覆盖。

using Flint.Core.Templates;
using Xunit;

namespace Flint.Core.Tests.Templates;

public sealed class MinifyHtmlGuardTests
{
    [Fact]
    public void 普通空白_标签间与折叠压缩()
    {
        var html = "<div>  <p>hello</p>  </div>\n\n<span>  x  </span>";
        var out_ = BuiltinTemplateFunctions.MinifyHtml(html);
        Assert.Equal("<div><p>hello</p></div><span> x </span>", out_);
    }

    [Fact]
    public void pre内容_逐字节保持不压缩()
    {
        var pre = "<pre>  line1\n    line2\ttab  </pre>";
        var html = $"<div>  </div>{pre}<p>  tail  </p>";
        var out_ = BuiltinTemplateFunctions.MinifyHtml(html);
        Assert.Contains(pre, out_);
        Assert.DoesNotContain("  </div>", out_);
    }

    [Fact]
    public void textarea内容_保持()
    {
        var html = "<textarea>  keep  me  </textarea>";
        Assert.Equal(html, BuiltinTemplateFunctions.MinifyHtml(html));
    }

    [Fact]
    public void script字符串字面量_连续空格保持()
    {
        var script = "<script>var s = \"a   b\";  var t = 1;</script>";
        var html = $"<head>  </head>{script}";
        var out_ = BuiltinTemplateFunctions.MinifyHtml(html);
        Assert.Contains("\"a   b\"", out_);
    }

    [Fact]
    public void style内容_保持()
    {
        var style = "<style>.x {  margin: 0;  }</style>";
        var html = $"<div> </div>{style}";
        Assert.Contains(style, BuiltinTemplateFunctions.MinifyHtml(html));
    }

    [Fact]
    public void 多个保护块_顺序还原不串位()
    {
        var pre1 = "<pre>first  block</pre>";
        var pre2 = "<pre id=\"b\">second   block</pre>";
        var html = $"  {pre1}  <p>x</p>  {pre2}  ";
        var out_ = BuiltinTemplateFunctions.MinifyHtml(html);
        Assert.Contains(pre1, out_);
        Assert.Contains(pre2, out_);
        Assert.DoesNotContain("  <p>", out_);
    }

    [Fact]
    public void 空串_返回空串()
    {
        Assert.Equal("", BuiltinTemplateFunctions.MinifyHtml(""));
    }
}

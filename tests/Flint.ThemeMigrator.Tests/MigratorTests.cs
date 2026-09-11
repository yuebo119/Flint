// Flint 主题迁移工具测试
// 回归防护对象（本轮实测确证，均为正则版无法正确处理的情形）：
// 1. 引号内含 }} / {{ 时正确切分（正则版会静默损坏）
// 2. 嵌套结构产出正确语义（正则版产出 `(len < page.pages) 10` 这类垃圾）
// 3. Scriban 的 for 不接受裸参数列表（须括号化）
// 4. 注释必须完全丢弃（保留会污染产物致解析失败）
// 5. 双变量 range 展开为单变量 + .key/.value 解构
// 6. dict 空值/非标识符键的特殊处理

using Flint.ThemeMigrator.Conversion;
using Flint.ThemeMigrator.Migration;
using Flint.ThemeMigrator.Parsing;
using Xunit;

namespace Flint.ThemeMigrator.Tests;

/// <summary>词法器：正确的引号/注释/嵌套处理</summary>
public sealed class LexerTests
{
    [Fact]
    public void 引号内含右定界符不被切开()
    {
        // 正则版的静默损坏点：{{ printf "}}" }} 曾被切在引号内，残渣泄漏成正文
        var tokens = new GoTemplateLexer("A{{ printf \"}}\" }}B").Tokenize();

        var strings = tokens.Where(t => t.Type == TokenType.String).Select(t => t.Value).ToList();
        Assert.Contains("\"}}\"", strings);
        Assert.Equal(2, tokens.Count(t => t.Type == TokenType.Text));
    }

    [Fact]
    public void 引号内含左定界符不被切开()
    {
        var tokens = new GoTemplateLexer("{{ printf \"{{\" }}").Tokenize();
        Assert.Contains(tokens, t => t.Type == TokenType.String && t.Value == "\"{{\"");
    }

    [Fact]
    public void 注释识别含裁剪标记形态()
    {
        var tokens = new GoTemplateLexer("{{- /* note */ -}}").Tokenize();
        Assert.Contains(tokens, t => t.Type == TokenType.Comment);
    }

    [Fact]
    public void 字段与变量区分()
    {
        var tokens = new GoTemplateLexer("{{ .Params.Title $x }}").Tokenize();
        Assert.Contains(tokens, t => t.Type == TokenType.Field && t.Value == ".Params.Title");
        Assert.Contains(tokens, t => t.Type == TokenType.Variable && t.Value == "$x");
    }

    [Fact]
    public void 反引号原始串不被转义干扰()
    {
        var tokens = new GoTemplateLexer("{{ printf `a\"b` }}").Tokenize();
        Assert.Contains(tokens, t => t.Type == TokenType.RawString);
    }
}

/// <summary>解析器 + 转换器：结构驱动转换的正确性</summary>
public sealed class ParserConverterTests
{
    private static string Convert(string template)
    {
        var tokens = new GoTemplateLexer(template).Tokenize();
        var parts = new GoTemplateParser(tokens).Parse();
        return new TemplateConverter(MigrationMap.CreateDefault()).Convert(parts);
    }

    [Fact]
    public void 嵌套括号结构正确()
    {
        // 正则版的静默损坏点：lt (len .Pages) 10 → (len < page.pages) 10
        var result = Convert("{{ lt (len .Pages) 10 }}");

        // Scriban 的函数是前缀形态 len <arg>（不是 C 风格 len(...)），
        // 关键是结构与运算符正确
        Assert.Contains("len page.pages", result, StringComparison.Ordinal);
        Assert.Contains("< 10", result, StringComparison.Ordinal);
        Assert.DoesNotContain("(len < page.pages) 10", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 管道逐段转换()
    {
        var result = Convert("{{ .Title | markdownify }}");
        Assert.Contains("page.title", result, StringComparison.Ordinal);
        Assert.Contains("markdownify", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 命名空间调用保持完整()
    {
        // 曾被错误拆成 "strings page.ToUpper"（Field 当参数）
        var result = Convert("{{ strings.ToUpper \"a\" }}");
        Assert.Contains("strings.ToUpper", result, StringComparison.Ordinal);
        Assert.DoesNotContain("strings page", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 比较函数转中缀()
    {
        var result = Convert("{{ eq .Kind \"home\" }}");
        Assert.Contains("page.kind == \"home\"", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 逻辑函数转中缀()
    {
        var result = Convert("{{ and .Draft (not .Title) }}");
        Assert.Contains("&&", result, StringComparison.Ordinal);
        Assert.Contains("!(page.title)", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 双变量range展开为解构()
    {
        // Scriban 不支持 for k, v in（实测 PARSE-ERR）
        var result = Convert("{{ range $k, $v := .Params }}{{ $k }}{{ end }}");
        Assert.Contains("for ", result, StringComparison.Ordinal);
        Assert.Contains(".key", result, StringComparison.Ordinal);
        Assert.Contains(".value", result, StringComparison.Ordinal);
        Assert.DoesNotContain("for $k, $v", result, StringComparison.Ordinal);
    }

    [Fact]
    public void range集合加括号()
    {
        // Scriban 的 for 不接受裸参数列表（实测 PARSE-ERR）
        var result = Convert("{{ range split . \",\" }}X{{ end }}");
        Assert.Contains("for ", result, StringComparison.Ordinal);
        Assert.Contains("(split ", result, StringComparison.Ordinal);
    }

    [Fact]
    public void partial参数内容记入诊断()
    {
        // Scriban 的 include 不接收上下文参数：内容记入降级说明（不写入产物——
        // Scriban 注释在动作内会被误判为对象初始化器，实测致预检失败）
        var tokens = new GoTemplateLexer("{{ partial \"x\" (dict \"k\" 1) }}").Tokenize();
        var parts = new GoTemplateParser(tokens).Parse();
        var converter = new TemplateConverter(MigrationMap.CreateDefault());
        var output = converter.Convert(parts);

        Assert.Contains("include \"x\"", output, StringComparison.Ordinal);
        Assert.Contains(converter.Stats.Notes, n => n.Contains("k: 1", StringComparison.Ordinal));
    }

    [Fact]
    public void dict直接构造为对象字面量()
    {
        var result = Convert("{{ $d := dict \"k\" 1 }}");
        Assert.Contains("k: 1", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 空dict用函数形态()
    {
        // Scriban 无法解析空对象字面量 { }
        var result = Convert("{{ $d := dict }}");
        Assert.Contains("$d = dict", result, StringComparison.Ordinal);
        Assert.DoesNotContain("{ }", result, StringComparison.Ordinal);
    }

    [Fact]
    public void dict非标识符键加引号()
    {
        var result = Convert("{{ dict \"a=1\" \"v\" }}");
        Assert.Contains("\"a=1\":", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 注释完全丢弃()
    {
        var result = Convert("A{{/* note */}}B");
        Assert.Equal("AB", result);
    }

    [Fact]
    public void define转capture并生成include()
    {
        // Scriban 无 extends/block 语句，用 capture + 命名参数 include
        var result = Convert("{{ define \"main\" }}X{{ end }}");
        Assert.Contains("capture blk_main", result, StringComparison.Ordinal);
        Assert.Contains("include \"baseof.html\" blk_main: blk_main", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 参数式Param转paramLookup()
    {
        // $.Param 是页面参数查询；Scriban 的 $ 是函数参数数组，直接改名会失败
        var result = Convert("{{ .Param \"x\" }}");
        Assert.Contains("paramLookup", result, StringComparison.Ordinal);
        Assert.Contains("\"x\"", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 日期格式化方法转换()
    {
        var result = Convert("{{ .Date.Format \"2006-01-02\" }}");
        Assert.Contains("date.to_string", result, StringComparison.Ordinal);
        Assert.Contains("yyyy-MM-dd", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 资源方法在资源上下文补前缀()
    {
        // with .Resources.ByType 块内的裸 .GetMatch：隐式接收者是资源对象
        var result = Convert(
            "{{ with .Resources.ByType \"image\" }}{{ with .GetMatch \"x*\" }}Y{{ end }}{{ end }}");
        Assert.Contains("page.resources.bytype", result, StringComparison.Ordinal);
        Assert.Contains("page.resources.getmatch", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 翻译函数映射为i18n()
    {
        var result = Convert("{{ lang.Translate \"readMore\" }}");
        Assert.Contains("i18n", result, StringComparison.Ordinal);
        Assert.DoesNotContain("lang.Translate", result, StringComparison.Ordinal);
    }

    [Fact]
    public void partial上下文参数省略()
    {
        // Scriban 的 include 共享调用者上下文，Hugo 的第二参数无需显式传递
        var result = Convert("{{ partial \"cover\" . }}");
        Assert.Contains("include \"cover\"", result, StringComparison.Ordinal);
    }
}

/// <summary>结构守卫：静默损坏防线</summary>
public sealed class StructuralGuardTests
{
    [Theory]
    [InlineData("(a < b) c", true)]   // 括号后跟操作数 → 可疑
    [InlineData("(a == b)", false)]   // 正常括号表达式
    [InlineData("(a", true)]          // 括号不平衡
    [InlineData("", true)]            // 空产出
    [InlineData("f(page.title)", false)]
    public void 结构检查识别可疑产出(string text, bool shouldFlag)
    {
        var problem = StructuralGuard.Check(text);
        Assert.Equal(shouldFlag, problem is not null);
    }

    [Fact]
    public void 引号不平衡被识别()
    {
        Assert.NotNull(StructuralGuard.Check("f \"unclosed"));
    }
}

/// <summary>Go 日期布局转换（确定性映射）</summary>
public sealed class DateFormatConverterTests
{
    [Theory]
    [InlineData("2006-01-02", "yyyy-MM-dd")]
    [InlineData("2006-01-02T15:04:05Z07:00", "yyyy-MM-ddTHH:mm:sszzz")]
    [InlineData("January 2, 2006", "MMMM d, yyyy")]
    [InlineData("15:04:05", "HH:mm:ss")]
    public void Go布局转Net格式(string go, string net)
    {
        Assert.Equal(net, GoDateFormatConverter.Convert(go));
    }

    [Fact]
    public void 长模式优先于短模式()
    {
        // "2006-01-02" 必须整体匹配，不能被 "2006" 抢先替换
        var result = GoDateFormatConverter.Convert("2006-01-02");
        Assert.Equal("yyyy-MM-dd", result);
        Assert.DoesNotContain("MM-dd", result.Replace("yyyy-MM-dd", "", StringComparison.Ordinal),
            StringComparison.Ordinal);
    }
}

/// <summary>partial 返回值语义（Hugo 返回对象 vs Scriban 文本化）</summary>
public sealed class PartialReturnValueTests
{
    private static string Convert(string template, string? selfPartial = null,
        IReadOnlySet<string>? valueReturning = null)
    {
        var tokens = new GoTemplateLexer(template).Tokenize();
        var parts = new GoTemplateParser(tokens).Parse();
        return new TemplateConverter(MigrationMap.CreateDefault(), valueReturning, selfPartial).Convert(parts);
    }

    [Fact]
    public void return在partial内改写为store通道()
    {
        // Hugo 的 return 返回任意对象；Scriban 的 include 只能文本化，
        // 故用页面 Store 作对象通道（实测：跨 include 保真且零序列化）
        var result = Convert("{{ return $x }}", selfPartial: "func/maker");
        Assert.Contains("page.store.set", result, StringComparison.Ordinal);
        Assert.Contains("__partial_ret_func/maker", result, StringComparison.Ordinal);
        Assert.Contains("ret", result, StringComparison.Ordinal);
    }

    [Fact]
    public void return在非partial内保持ret()
    {
        var result = Convert("{{ return $x }}");
        Assert.Contains("ret $x", result, StringComparison.Ordinal);
        Assert.DoesNotContain("page.store.set", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 无参return提前退出()
    {
        var result = Convert("{{ return }}", selfPartial: "func/maker");
        Assert.Contains("ret", result, StringComparison.Ordinal);
        Assert.DoesNotContain("page.store.set", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 返回值型partial的调用点改为partialValue()
    {
        var vr = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "func/maker" };
        var result = Convert("{{ $v := partial \"func/maker.html\" . }}", valueReturning: vr);
        Assert.Contains("partialValue", result, StringComparison.Ordinal);
        Assert.DoesNotContain("include", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 非返回值型partial保持include()
    {
        var vr = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "func/other" };
        var result = Convert("{{ partial \"func/maker.html\" . }}", valueReturning: vr);
        Assert.Contains("include", result, StringComparison.Ordinal);
        Assert.DoesNotContain("partialValue", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 返回值型partial上下文非dot时保持include()
    {
        // 传其他页面对象时语义不等价（partialValue 共享调用者上下文）
        var vr = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "func/maker" };
        var result = Convert("{{ partial \"func/maker.html\" $otherPage }}", valueReturning: vr);
        Assert.Contains("include", result, StringComparison.Ordinal);
    }

    [Fact]
    public void partial名规范化对齐()
    {
        Assert.Equal("func/x", ScribanConverter.CanonicalPartialName("func/x.html"));
        Assert.Equal("func/x", ScribanConverter.CanonicalPartialName("layouts/_partials/func/x.html"));
        Assert.Equal("func/x", ScribanConverter.CanonicalPartialName("_partials/func/x.html"));
        Assert.Equal("x", ScribanConverter.CanonicalPartialName("x.html"));
    }

    [Fact]
    public void return被识别为关键字而非标识符()
    {
        // 回归防护：return 不在 Go 标准库 key 表中（Hugo 扩展），
        // 未识别会产出 "IdentifierExpr { Raw = return }" 这类 TODO
        var result = Convert("{{ return 1 }}");
        Assert.DoesNotContain("TODO-HUGO", result, StringComparison.Ordinal);
    }
}

/// <summary>块内裸点映射（range/with 作用域）</summary>
public sealed class BlockScopeMappingTests
{
    private static string Convert(string template)
    {
        var tokens = new GoTemplateLexer(template).Tokenize();
        var parts = new GoTemplateParser(tokens).Parse();
        return new TemplateConverter(MigrationMap.CreateDefault()).Convert(parts);
    }

    [Fact]
    public void range内裸点映射到循环变量()
    {
        // 回归防护：`{{ range .Pages }}{{ .Title }}` 曾产出 page.title
        // （循环变量被忽略，渲染错误页面的标题）
        var result = Convert("{{ range .Pages }}{{ .Title }}{{ end }}");
        Assert.Contains("for $__it", result, StringComparison.Ordinal);
        Assert.Contains(".title", result, StringComparison.Ordinal);
        // 关键：不得出现裸 page.title（应为循环变量.title）
        Assert.DoesNotContain("{{ page.title }}", result, StringComparison.Ordinal);
    }

    [Fact]
    public void range内PageNumber不被Page前缀误伤()
    {
        // 回归防护：`.PageNumber` 曾因以 `.Page` 开头被排除条件误伤，
        // 产出裸 page_number（应映射为循环变量的 .page_number）
        var result = Convert("{{ range .Pagers }}{{ .PageNumber }}{{ end }}");
        Assert.Contains("$__it", result, StringComparison.Ordinal);
        Assert.Contains("page_number", result, StringComparison.Ordinal);
        Assert.DoesNotContain("{{ page_number }}", result, StringComparison.Ordinal);
    }

    [Fact]
    public void range内Pages不被Page前缀误伤()
    {
        var result = Convert("{{ range .Sections }}{{ range .Pages }}{{ .Title }}{{ end }}{{ end }}");
        Assert.DoesNotContain("{{ page.pages }}", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 显式根不受作用域影响()
    {
        // .Site.* / $.* 是显式根，即使在 range 内也不应映射到循环变量
        var result = Convert("{{ range .Pages }}{{ .Site.Title }}{{ end }}");
        Assert.Contains("site.title", result, StringComparison.Ordinal);
    }

    [Fact]
    public void with内裸点映射到上下文变量()
    {
        var result = Convert("{{ with .Params.author }}{{ . }}{{ end }}");
        Assert.Contains("if $__w", result, StringComparison.Ordinal);
    }

    [Fact]
    public void range外层裸点仍映射到page()
    {
        // 无作用域时保持原语义
        var result = Convert("{{ .Title }}");
        Assert.Contains("page.title", result, StringComparison.Ordinal);
    }
}

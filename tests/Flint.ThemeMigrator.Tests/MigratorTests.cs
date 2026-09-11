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
        // Scriban 迭代**映射**产出 {Key, Value}（PascalCase——`x.key` 取不到，实测）；
        // 迭代**序列**产出元素本身，故用 `?? for.index` / `?? pair` 统一两种形态
        Assert.Contains(".Key", result, StringComparison.Ordinal);
        Assert.Contains(".Value", result, StringComparison.Ordinal);
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
    public void partial上下文参数写入产物()
    {
        // Hugo 的 `partial "x" CONTEXT` 把 CONTEXT 作为 partial 内的 `.`；
        // Flint 的 partial 函数接收第二参数并临时绑成 page（Hugo dot 语义等价），
        // 故内容**写入产物**而非仅记诊断（旧行为丢弃参数，致 partial 内 `.` 取不到值）
        var tokens = new GoTemplateLexer("{{ partial \"x\" (dict \"k\" 1) }}").Tokenize();
        var parts = new GoTemplateParser(tokens).Parse();
        var converter = new TemplateConverter(MigrationMap.CreateDefault());
        var output = converter.Convert(parts);

        Assert.Contains("partial \"_partials/x\"", output, StringComparison.Ordinal);
        Assert.Contains("k: 1", output, StringComparison.Ordinal);
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
        // nil 安全化后形态为 `page?.resources?.bytype`（行为等价，仅点号安全化）
        Assert.Contains("resources", result, StringComparison.Ordinal);
        Assert.Contains("bytype", result, StringComparison.Ordinal);
        Assert.Contains("getmatch", result, StringComparison.Ordinal);
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
        Assert.Contains("include \"_partials/cover\"", result, StringComparison.Ordinal);
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

/// <summary>
/// 四主题收敛轮（2026-09-11）转换层回归：以下形态都曾让真实主题构建失败
/// </summary>
public sealed class PipeAndParserRegressionTests
{
    private static string Convert(string template)
    {
        var tokens = new GoTemplateLexer(template).Tokenize();
        var parts = new GoTemplateParser(tokens).Parse();
        return new TemplateConverter(MigrationMap.CreateDefault()).Convert(parts);
    }

    [Fact]
    public void 管道左值作末参改写为显式调用()
    {
        // Go/Hugo：`X | f A` == `f A X`；Scriban 注入**首参**。
        // PaperMod 实测：`" " | resources.FromString "a.css"` 曾产出名为 " " 的资源
        var result = Convert("{{ \"\" | resources.FromString \"a.css\" }}");
        Assert.Contains("resources.FromString \"a.css\" \"\"", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 管道左值为多token表达式时加括号()
    {
        // 不加括号会与前参连成一串（FromString 收到 4 个实参）
        var result = Convert("{{ delimit .Params.x \", \" | resources.FromString \"a.css\" }}");
        Assert.Contains("resources.FromString \"a.css\" (", result, StringComparison.Ordinal);
    }

    [Fact]
    public void printf管道形态参数顺序正确()
    {
        // PaperMod：`X | printf "content=%q"` → printf "content=%q" X
        var result = Convert("{{ .Title | printf \"%s\" }}");
        Assert.Contains("printf \"%s\" page.title", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 括号内管道不被外层切分()
    {
        // ParsePipeline 曾不跟踪括号深度，`slice "a" (X | default "y") $z`
        // 会从内层 | 断开 → 数组提前闭合（Ananke baseof 的 body_classes 实测）
        var result = Convert("{{ slice \"ma0\" (.Param \"cls\" | default \"d\") .Kind }}");
        Assert.Contains("[\"ma0\"", result, StringComparison.Ordinal);
        Assert.Contains("default", result, StringComparison.Ordinal);
        Assert.Contains("page.kind", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 块名含连字符归一为合法标识符()
    {
        // Stack：`block "body-class"` → Scriban 变量名不接受连字符
        //（曾产出 `__def_body-class` → "Unsupported target expression for assignment"）
        var result = Convert("{{ block \"body-class\" }}x{{ end }}");
        Assert.Contains("__def_body_class", result, StringComparison.Ordinal);
        Assert.DoesNotContain("__def_body-class", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 局部变量接收者不被换根()
    {
        // `$scratch.Add` 的 head 是局部变量本身（曾产出 `pagescratch.add`）；
        // 裸 `$.GetTerms` 仍需换到 page 根
        Assert.Contains("$scratch.add", Convert("{{ $scratch.Add \"k\" 1 }}"), StringComparison.Ordinal);
        Assert.Contains("page.get_terms", Convert("{{ $.GetTerms \"tags\" }}"), StringComparison.Ordinal);
    }

    [Fact]
    public void 局部变量成员访问用nil安全()
    {
        // `$posts.paginate` 在变量为 nil 时 Hugo 返回 nil、Scriban 抛异常
        Assert.Contains("$posts?.paginate", Convert("{{ $posts.paginate }}"), StringComparison.Ordinal);
    }

    [Fact]
    public void partial调用点产出带前缀路径()
    {
        // Hugo 的 partial 只在 partials/_partials 目录查找，不会命中原目录同名页面模板。
        // 裸名会让 `partial "404.html"` 解析到 layouts/404.html（自身）→ 自递归
        //（hugo-coder 实测 "Exceeding number of recursive depth limit"）
        var result = Convert("{{ partial \"404.html\" . }}");
        Assert.Contains("_partials/404", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 命名模板的template调用改走partial()
    {
        // Go 的 `template "X" CTX` 调用 define 出来的命名模板；
        // 简单名 → 提取为 _partials/X.html，调用点走 partial 路径
        var result = Convert("{{ template \"integrity\" $styles }}");
        Assert.Contains("_partials/integrity", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 内置模板的template调用保持include()
    {
        // `_internal/...` 是 Hugo embedded template（无物理文件），走 include 命中引擎内置表
        var result = Convert("{{ template \"_internal/opengraph.html\" . }}");
        Assert.Contains("include \"_internal/opengraph.html\"", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 非管道default重排参数顺序()
    {
        // Hugo 的 default 是 `default DEFAULT GIVEN`，而 Flint 侧按 Scriban 语义取首参
        //（管道注入位），故非管道形态需重排为 (GIVEN, DEFAULT)
        var nonPiped = Convert("{{ default \"x\" page.title }}");
        Assert.Contains("default page?.title \"x\"", nonPiped, StringComparison.Ordinal);
        // 管道形态已是 (左值, 默认值)，不得重排
        var piped = Convert("{{ page.title | default \"x\" }}");
        Assert.Contains("default \"x\"", piped, StringComparison.Ordinal);
        Assert.DoesNotContain("default page.title", piped, StringComparison.Ordinal);
    }

    [Fact]
    public void 数据路径段名不被snake化()
    {
        // Stack：`hugo.Data.external.PhotoSwipe.Style` 的键是作者定义的驼峰
        var result = Convert("{{ hugo.Data.external.PhotoSwipe.Style }}");
        Assert.Contains("site?.data?.external?.PhotoSwipe?.Style", result, StringComparison.Ordinal);
    }
}

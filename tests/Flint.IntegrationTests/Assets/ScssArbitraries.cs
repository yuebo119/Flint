// Flint 静态站点生成器
// SCSS 属性测试的 FsCheck 生成器
// 为 SCSS 编译正确性属性测试提供随机数据生成

using System.Text;
using FsCheck;
using FsCheck.Fluent;

namespace Flint.IntegrationTests.Assets;

/// <summary>
/// SCSS 属性测试的 FsCheck 生成器
/// </summary>
public static class ScssArbitraries
{
    // CSS 颜色值
    private static readonly string[] Colors =
    [
        "#007bff", "#6c757d", "#28a745", "#dc3545", "#ffc107",
        "#17a2b8", "#f8f9fa", "#343a40", "#ffffff", "#000000",
        "red", "blue", "green", "yellow", "orange", "purple",
        "rgb(255, 0, 0)", "rgba(0, 0, 255, 0.5)"
    ];

    // CSS 尺寸值
    private static readonly string[] Sizes =
    [
        "10px", "20px", "1rem", "2em", "100%", "50vh", "auto",
        "12px", "16px", "24px", "32px", "48px", "64px"
    ];

    // CSS 属性名
    private static readonly string[] Properties =
    [
        "color", "background-color", "font-size", "margin", "padding",
        "border", "width", "height", "display", "position"
    ];

    // CSS 选择器
    private static readonly string[] Selectors =
    [
        ".container", ".header", ".footer", ".content", ".sidebar",
        ".button", ".link", ".card", ".nav", ".menu",
        "body", "div", "span", "p", "h1", "h2", "a"
    ];

    #region 基础生成器

    /// <summary>
    /// 生成有效的 CSS 颜色值
    /// </summary>
    public static Arbitrary<string> ColorArb() =>
        Gen.Elements(Colors).ToArbitrary();

    /// <summary>
    /// 生成有效的 CSS 尺寸值
    /// </summary>
    public static Arbitrary<string> SizeArb() =>
        Gen.Elements(Sizes).ToArbitrary();

    /// <summary>
    /// 生成有效的 CSS 属性名
    /// </summary>
    public static Arbitrary<string> PropertyArb() =>
        Gen.Elements(Properties).ToArbitrary();

    /// <summary>
    /// 生成有效的 CSS 选择器
    /// </summary>
    public static Arbitrary<string> SelectorArb() =>
        Gen.Elements(Selectors).ToArbitrary();

    /// <summary>
    /// 生成有效的 SCSS 变量名
    /// </summary>
    public static Arbitrary<string> VariableNameArb() =>
        Gen.Elements(
            "$primary", "$secondary", "$accent", "$text-color",
            "$bg-color", "$font-size", "$spacing", "$border-radius",
            "$shadow", "$transition"
        ).ToArbitrary();

    #endregion

    #region SCSS 生成器

    /// <summary>
    /// 生成有效的 SCSS 内容
    /// </summary>
    public static Arbitrary<string> ValidScssArb()
    {
        return Gen.OneOf(
            SimpleScssGen(),
            ScssWithVariablesGen(),
            ScssWithNestingGen(),
            ScssWithMixinGen()
        ).ToArbitrary();
    }

    /// <summary>
    /// 生成简单的 SCSS（无特殊语法）
    /// </summary>
    private static Gen<string> SimpleScssGen()
    {
        return from selector in SelectorArb().Generator
               from propCount in Gen.Choose(1, 3)
               from props in Gen.ListOf<string>(PropertyValuePairGen()).Select(p => p.Take(propCount).ToList())
               let propsStr = string.Join("\n    ", props)
               select selector + " {\n    " + propsStr + "\n}";
    }

    /// <summary>
    /// 生成属性-值对
    /// </summary>
    private static Gen<string> PropertyValuePairGen()
    {
        return Gen.OneOf(
            from prop in Gen.Elements("color", "background-color")
            from value in ColorArb().Generator
            select prop + ": " + value + ";",

            from prop in Gen.Elements("font-size", "margin", "padding", "width", "height")
            from value in SizeArb().Generator
            select prop + ": " + value + ";",

            from prop in Gen.Constant("display")
            from value in Gen.Elements("flex", "block", "inline", "none", "grid")
            select prop + ": " + value + ";"
        );
    }

    /// <summary>
    /// 生成包含变量的 SCSS
    /// </summary>
    private static Gen<string> ScssWithVariablesGen()
    {
        return from varName in Gen.Elements("primary", "secondary", "accent")
               from color in ColorArb().Generator
               from selector in SelectorArb().Generator
               select BuildScssWithVariables(varName, color, selector);
    }

    private static string BuildScssWithVariables(string varName, string color, string selector)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"${varName}-color: {color};");
        sb.AppendLine();
        sb.AppendLine($"{selector} {{");
        sb.AppendLine($"    color: ${varName}-color;");
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// 生成包含嵌套的 SCSS
    /// </summary>
    private static Gen<string> ScssWithNestingGen()
    {
        return from parent in Gen.Elements(".container", ".wrapper", ".section")
               from child in Gen.Elements(".header", ".content", ".footer")
               from color in ColorArb().Generator
               from size in SizeArb().Generator
               select BuildScssWithNesting(parent, child, color, size);
    }

    private static string BuildScssWithNesting(string parent, string child, string color, string size)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{parent} {{");
        sb.AppendLine($"    padding: {size};");
        sb.AppendLine();
        sb.AppendLine($"    {child} {{");
        sb.AppendLine($"        color: {color};");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// 生成包含 Mixin 的 SCSS
    /// </summary>
    private static Gen<string> ScssWithMixinGen()
    {
        return from mixinName in Gen.Elements("flex-center", "button-style", "card-shadow")
               from selector in SelectorArb().Generator
               from color in ColorArb().Generator
               select BuildScssWithMixin(mixinName, selector, color);
    }

    private static string BuildScssWithMixin(string mixinName, string selector, string color)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"@mixin {mixinName} {{");
        sb.AppendLine("    display: flex;");
        sb.AppendLine("    justify-content: center;");
        sb.AppendLine("    align-items: center;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine($"{selector} {{");
        sb.AppendLine($"    @include {mixinName};");
        sb.AppendLine($"    color: {color};");
        sb.AppendLine("}");
        return sb.ToString();
    }

    #endregion

    #region 带期望值的 SCSS 生成器

    /// <summary>
    /// 生成包含变量的 SCSS（带期望值）
    /// </summary>
    public static Arbitrary<ScssWithVariables> ScssWithVariablesArb()
    {
        return (from varName in Gen.Elements("primary", "secondary", "accent")
                from color in ColorArb().Generator
                from selector in SelectorArb().Generator
                select new ScssWithVariables
                {
                    Scss = BuildScssWithVariables(varName, color, selector),
                    ExpectedValues = new List<string> { color }
                }).ToArbitrary();
    }

    /// <summary>
    /// 生成包含嵌套的 SCSS（带期望选择器）
    /// </summary>
    public static Arbitrary<ScssWithNesting> ScssWithNestingArb()
    {
        return (from parent in Gen.Elements(".container", ".wrapper", ".section")
                from child in Gen.Elements(".header", ".content", ".footer")
                from color in ColorArb().Generator
                from size in SizeArb().Generator
                let expectedSelector = parent + " " + child
                select new ScssWithNesting
                {
                    Scss = BuildScssWithNesting(parent, child, color, size),
                    ExpectedSelectors = new List<string> { parent, expectedSelector }
                }).ToArbitrary();
    }

    /// <summary>
    /// 生成包含 Mixin 的 SCSS（带期望属性）
    /// </summary>
    public static Arbitrary<ScssWithMixin> ScssWithMixinArb()
    {
        return (from mixinName in Gen.Elements("flex-center", "button-style", "card-shadow")
                from selector in SelectorArb().Generator
                from color in ColorArb().Generator
                select new ScssWithMixin
                {
                    Scss = BuildScssWithMixin(mixinName, selector, color),
                    ExpectedProperties = new List<string> { "display", "flex", "justify-content", "center" }
                }).ToArbitrary();
    }

    #endregion
}

#region 数据类型

/// <summary>
/// 包含变量的 SCSS 及其期望值
/// </summary>
public sealed record ScssWithVariables
{
    /// <summary>
    /// SCSS 内容
    /// </summary>
    public required string Scss { get; init; }

    /// <summary>
    /// 期望在输出中出现的值
    /// </summary>
    public required IReadOnlyList<string> ExpectedValues { get; init; }
}

/// <summary>
/// 包含嵌套的 SCSS 及其期望选择器
/// </summary>
public sealed record ScssWithNesting
{
    /// <summary>
    /// SCSS 内容
    /// </summary>
    public required string Scss { get; init; }

    /// <summary>
    /// 期望在输出中出现的选择器
    /// </summary>
    public required IReadOnlyList<string> ExpectedSelectors { get; init; }
}

/// <summary>
/// 包含 Mixin 的 SCSS 及其期望属性
/// </summary>
public sealed record ScssWithMixin
{
    /// <summary>
    /// SCSS 内容
    /// </summary>
    public required string Scss { get; init; }

    /// <summary>
    /// 期望在输出中出现的属性
    /// </summary>
    public required IReadOnlyList<string> ExpectedProperties { get; init; }
}

#endregion

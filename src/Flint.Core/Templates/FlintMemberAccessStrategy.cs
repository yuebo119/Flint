// Flint 静态站点生成器
// Fluid 成员访问策略：snake_case → PascalCase 映射（FLUID-MIGRATION-PLAN P1-4）。
// Fluid 默认成员比较器是 OrdinalIgnoreCase（page.title 命中 Title），但 Hugo 侧
// 模板惯用 snake_case（page.word_count / page.rel_permalink），默认策略不映射
// 下划线形态——探针实测 page.word_count 取空。本策略在委派默认反射查找前把
// snake_case 名归一为 PascalCase，保持默认比较器的大小写不敏感语义。

using Fluid;
using Fluid.Values;

namespace Flint.Core.Templates;

/// <summary>
/// 带 snake_case 映射的 Fluid 成员访问策略：<c>word_count</c> → <c>WordCount</c>。
/// 其余名字原样委派 <see cref="DefaultMemberAccessStrategy"/>（大小写不敏感）
/// </summary>
public sealed class FlintMemberAccessStrategy : DefaultMemberAccessStrategy
{
    /// <summary>
    /// 取成员访问器：snake_case 名先归一为 PascalCase 再走默认反射查找。
    /// 无下划线的名（含已是 PascalCase 的）原样透传——默认比较器已覆盖大小写。
    /// 未命中成员时基类返回 null（Fluid 的"未定义成员"信号，模板取空值）
    /// </summary>
    public override MemberAccessor GetAccessor(Type type, string name, StringComparer stringComparer)
    {
        var normalized = ToPascalCase(name);
        return base.GetAccessor(type, normalized, stringComparer);
    }

    /// <summary>
    /// snake_case → PascalCase：<c>word_count</c> → <c>WordCount</c>，
    /// <c>rel_permalink</c> → <c>RelPermalink</c>。无下划线原样返回
    /// （大小写由默认比较器处理，此处不介入）
    /// </summary>
    internal static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name) || !name.Contains('_'))
        {
            return name;
        }

        Span<char> buffer = name.Length <= 256 ? stackalloc char[name.Length] : new char[name.Length];
        var write = 0;
        var upperNext = true;
        foreach (var c in name)
        {
            if (c == '_')
            {
                upperNext = true;
                continue;
            }
            buffer[write++] = upperNext && char.IsLower(c) ? char.ToUpperInvariant(c) : c;
            upperNext = false;
        }
        return new string(buffer[..write]);
    }
}

/// <summary>
/// <c>page.reading_time</c> 的 Liquid 视图。Fluid 3 的 <c>FluidValue.Create</c>
/// 会把 <see cref="TimeSpan"/> 硬转换为 <c>DateTimeValue</c>（纪元 + 时长，
/// 探针实证），使 <c>page.reading_time.total_minutes</c> 在转换后的
/// DateTimeOffset 上取空。经 <see cref="PageContext.ReadingTime"/> 的注册访问器
/// 返回本视图，暴露 <see cref="TotalMinutes"/>（Flint 口径）与
/// <see cref="Minutes"/>（Hugo .ReadingTime.Minutes 口径）双形态；
/// <see cref="ToString"/> 保持 TimeSpan 字符串形态（"00:01:00"）
/// </summary>
public sealed class ReadingTimeView
{
    private readonly TimeSpan _value;

    public ReadingTimeView(TimeSpan value) => _value = value;

    /// <summary>总分钟数（Flint 的 reading_time.total_minutes 口径）</summary>
    public double TotalMinutes => _value.TotalMinutes;

    /// <summary>整分钟数（Hugo 的 .ReadingTime.Minutes 口径，向下取整）</summary>
    public int Minutes => (int)_value.TotalMinutes;

    /// <summary>TimeSpan 字符串形态（与 Scriban 侧 {{ page.reading_time }} 一致）</summary>
    public override string ToString() => _value.ToString();
}

/// <summary>
/// C# 索引器的成员访问适配（<c>site.menus.main</c> / <c>site.taxonomies.categories</c>）：
/// Fluid 的成员语法不走 C# 索引器，MenuCollection/TaxonomyCollection 经
/// <see cref="MemberAccessStrategy.Register(Type, MemberAccessor)"/> 注册本访问器后，
/// 任意成员名路由到索引器——缺失键返回空集合，与 Scriban 侧 ScriptObject
/// 键查找同语义（2026-10-19 P2-7 产物对比实证：未注册时菜单/taxonomy
/// 在 Liquid 路径整体取空）
/// </summary>
public sealed class IndexerMemberAccessor : MemberAccessor
{
    private readonly Func<object, string, object?> _lookup;

    public IndexerMemberAccessor(Func<object, string, object?> lookup) => _lookup = lookup;

    public override ValueTask<FluidValue> GetAsync(object obj, string name, TemplateContext context)
    {
        var value = _lookup(obj, name);
        return new ValueTask<FluidValue>(FluidValue.Create(value, context.Options));
    }
}

/// <summary>
/// Hugo 常量信息视图（<c>hugo.Version</c> / <c>hugo.IsProduction</c> 等）：
/// Scriban 侧有完整的 hugo 对象（EngineCapabilityTests 门禁），Fluid 侧
/// 2026-10-21 补入——主题模板写 hugo.IsProduction 得空是静默失败
/// </summary>
public sealed class HugoInfoView
{
    /// <summary>共享单例（值全部编译期常量）</summary>
    public static HugoInfoView Instance { get; } = new();

    /// <summary>Hugo 兼容版本号（Flint 声明兼容的 Hugo 版本）</summary>
    public string Version => "0.146.0";

    /// <summary>是否生产环境（Flint build 命令恒 true；serve 为 false）</summary>
    public bool IsProduction => true;

    /// <summary>是否开发服务器（Flint build 命令恒 false；serve 为 true）</summary>
    public bool IsServer => false;

    /// <summary>环境名称</summary>
    public string Environment => "production";
}

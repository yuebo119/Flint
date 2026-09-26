// Flint 静态站点生成器
// 带蛇形别名的 dict：`dict "displayName" …` 之类**驼峰键**在迁移产物里常被
// 以 `display_name`（蛇形）读取——迁移器把模板成员名归一为 snake_case（与
// BuildParamsObject 对参数表的做法一致），故 dict 值的读取需要同样的别名兜底

using Scriban.Parsing;
using Scriban.Runtime;

namespace Flint.Core.Templates;

/// <summary>
/// dict 的产物：写入时保留**原键**，同时提供**蛇形别名**兜底（camelCase/PascalCase →
/// snake_case；`displayName` → `display_name`）。narrow 的 post-license.html：
/// `dict "displayName" …` 由迁移产物以 `$license.display_name` 读取，此前取到空 →
/// 许可证类型链接的文本整段消失（实测）
/// </summary>
internal sealed class ScriptDictWithSnakeAliases : ScriptObject
{
    public override bool TryGetValue(Scriban.TemplateContext? context, SourceSpan span, string member, out object? value)
    {
        if (base.TryGetValue(context, span, member, out value))
        {
            return true;
        }

        // 蛇形/大小写归一兜底：`display_name`/`displayname`/`displayName` 都命中同一条
        // （ScriptObject 的字典访问是大小写敏感的，去下划线后还需忽略大小写）
        var target = member.Replace("_", "");
        foreach (var key in Keys)
        {
            if (string.Equals(key.Replace("_", ""), target, StringComparison.OrdinalIgnoreCase))
            {
                value = this[key];
                return true;
            }
        }

        value = null;
        return false;
    }
}

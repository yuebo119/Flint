// Flint 静态站点生成器
// 配置深合并工具：config/_default/ 目录式配置按段合并用

namespace Flint.Core.Configuration;

/// <summary>
/// 配置字典深合并（对齐 Hugo 的 config 目录合并语义）：
/// 源覆盖目标；两侧同为字典时递归合并，否则源整体替换目标。
/// 合并后源字典不被修改（调用方可复用）
/// </summary>
internal static class ConfigMerge
{
    /// <summary>把 <paramref name="source"/> 深合并进 <paramref name="target"/>（源优先）</summary>
    public static void DeepMergeInto(Dictionary<string, object> target, Dictionary<string, object> source)
    {
        foreach (var (key, value) in source)
        {
            // 键大小写不敏感匹配：Hugo 的配置键按不敏感处理
            //（params.Author 与 params.author 是同一键）
            var existingKey = target.Keys.FirstOrDefault(
                k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));

            if (existingKey is not null &&
                target[existingKey] is Dictionary<string, object> targetDict &&
                value is Dictionary<string, object> sourceDict)
            {
                DeepMergeInto(targetDict, sourceDict);
                continue;
            }

            target[existingKey ?? key] = value;
        }
    }
}

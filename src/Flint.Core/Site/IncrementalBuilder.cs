// Flint 静态站点生成器
// 增量构建器实现

using System.Collections.Concurrent;

namespace Flint.Core.Site;

/// <summary>
/// 增量构建器
/// 追踪文件依赖关系，实现增量构建
/// </summary>
public sealed class IncrementalBuilder
{
    private readonly ConcurrentDictionary<string, HashSet<string>> _dependencies = new();
    // 值用并发字典当 set：注册方在 Parallel.ForEachAsync 内并发到达，
    // HashSet.Add 在扩容期并发会丢条目甚至损坏桶结构
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _reverseDependencies = new();

    /// <summary>
    /// 注册文件依赖关系
    /// </summary>
    /// <param name="file">文件路径</param>
    /// <param name="dependsOn">依赖的文件列表</param>
    public void RegisterDependency(string file, IEnumerable<string> dependsOn)
    {
        var deps = dependsOn.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _dependencies[file] = deps;

        // 更新反向依赖
        foreach (var dep in deps)
        {
            _reverseDependencies.AddOrUpdate(
                dep,
                _ => new ConcurrentDictionary<string, byte>(
                    new[] { new KeyValuePair<string, byte>(file, 0) }),
                (_, existing) =>
                {
                    existing.TryAdd(file, 0);
                    return existing;
                });
        }
    }

    /// <summary>
    /// 获取需要重新构建的文件列表
    /// </summary>
    /// <param name="changedFiles">变化的文件</param>
    /// <returns>需要重新构建的文件列表</returns>
    public IReadOnlySet<string> GetAffectedFiles(IEnumerable<string> changedFiles)
    {
        var affected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(changedFiles);

        while (queue.Count > 0)
        {
            var file = queue.Dequeue();
            if (!affected.Add(file))
                continue;

            // 查找依赖此文件的其他文件（并发字典枚举线程安全，弱一致）
            if (_reverseDependencies.TryGetValue(file, out var dependents))
            {
                foreach (var dependent in dependents.Keys)
                {
                    if (!affected.Contains(dependent))
                    {
                        queue.Enqueue(dependent);
                    }
                }
            }
        }

        return affected;
    }
}

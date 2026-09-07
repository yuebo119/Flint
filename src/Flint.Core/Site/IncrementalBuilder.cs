// Flint 静态站点生成器
// 增量构建器实现

using System.Collections.Concurrent;
using Flint.Core.Abstractions;

namespace Flint.Core.Site;

/// <summary>
/// 增量构建器
/// 追踪文件依赖关系，实现增量构建
/// </summary>
public sealed class IncrementalBuilder
{
    private readonly ConcurrentDictionary<string, FileInfo> _fileCache = new();
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


    /// <summary>
    /// 检查文件是否需要重新构建
    /// </summary>
    /// <param name="file">文件路径</param>
    /// <returns>是否需要重新构建</returns>
    public bool NeedsRebuild(string file)
    {
        if (!File.Exists(file))
            return true;

        var currentInfo = new System.IO.FileInfo(file);

        if (_fileCache.TryGetValue(file, out var cached))
        {
            return currentInfo.LastWriteTimeUtc > cached.LastWriteTimeUtc
                || currentInfo.Length != cached.Length;
        }

        return true;
    }

    /// <summary>
    /// 更新文件缓存
    /// </summary>
    /// <param name="file">文件路径</param>
    public void UpdateCache(string file)
    {
        if (File.Exists(file))
        {
            var info = new System.IO.FileInfo(file);
            _fileCache[file] = new FileInfo
            {
                Path = file,
                LastWriteTimeUtc = info.LastWriteTimeUtc,
                Length = info.Length
            };
        }
        else
        {
            _fileCache.TryRemove(file, out _);
        }
    }

    /// <summary>
    /// 清除所有缓存
    /// </summary>
    public void ClearCache()
    {
        _fileCache.Clear();
        _dependencies.Clear();
        _reverseDependencies.Clear();
    }

    /// <summary>
    /// 获取文件的所有依赖
    /// </summary>
    public IReadOnlySet<string> GetDependencies(string file)
    {
        return _dependencies.TryGetValue(file, out var deps)
            ? deps
            : new HashSet<string>();
    }

    /// <summary>
    /// 获取依赖指定文件的所有文件（返回快照，调用方可安全枚举）
    /// </summary>
    public IReadOnlySet<string> GetDependents(string file)
    {
        return _reverseDependencies.TryGetValue(file, out var deps)
            ? new HashSet<string>(deps.Keys, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class FileInfo
    {
        public required string Path { get; init; }
        public required DateTimeOffset LastWriteTimeUtc { get; init; }
        public required long Length { get; init; }
    }
}

/// <summary>
/// 模板依赖分析器
/// </summary>
public sealed class TemplateDependencyAnalyzer
{
    private readonly ITemplateRenderer _templateRenderer;

    public TemplateDependencyAnalyzer(ITemplateRenderer templateRenderer)
    {
        _templateRenderer = templateRenderer;
    }

    /// <summary>
    /// 分析模板依赖
    /// </summary>
    /// <param name="templateName">模板名称</param>
    /// <returns>依赖的模板列表</returns>
    public IReadOnlyList<string> AnalyzeDependencies(string templateName)
    {
        return _templateRenderer.GetDependencies(templateName);
    }

    /// <summary>
    /// 获取使用指定模板的所有内容文件
    /// </summary>
    /// <param name="templateName">模板名称</param>
    /// <param name="contentFiles">内容文件列表</param>
    /// <param name="getLayout">获取内容文件布局的函数</param>
    /// <returns>使用该模板的内容文件列表</returns>
    public IReadOnlyList<string> GetContentUsingTemplate(
        string templateName,
        IEnumerable<string> contentFiles,
        Func<string, string?> getLayout)
    {
        var result = new List<string>();
        var templateDeps = new HashSet<string>(_templateRenderer.GetDependencies(templateName))
        {
            templateName
        };

        foreach (var contentFile in contentFiles)
        {
            var layout = getLayout(contentFile) ?? "single";
            if (templateDeps.Contains(layout))
            {
                result.Add(contentFile);
            }
        }

        return result;
    }
}

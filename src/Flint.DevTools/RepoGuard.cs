// 语料删除守卫：bench 类命令会整树重建 benchmarks/corpus 下的可再生语料
// （对应 Python 版 shutil.rmtree）。白名单化，绝不触及仓库源码/文档/测试树。

namespace Flint.DevTools;

/// <summary>
/// benchmarks/corpus 语料删除守卫
/// </summary>
internal static class RepoGuard
{
    private static readonly Lazy<string> LazyCorpusRoot = new(() =>
        Path.Combine(RepoPaths.RepoRoot, "benchmarks", "corpus"));

    /// <summary>语料根目录（gitignore，可再生）</summary>
    public static string CorpusRoot => LazyCorpusRoot.Value;

    /// <summary>
    /// 删除许可判定：仓外路径（用户显式指定的临时工作区）放行；仓内只允许
    /// benchmarks/corpus 语料根（可再生）；仓库其余位置（源码/文档/测试）一律拒绝
    /// </summary>
    public static void EnsureDeletable(string path)
    {
        var full = Path.GetFullPath(path);
        var repoRoot = Path.GetFullPath(RepoPaths.RepoRoot);

        if (!full.StartsWith(repoRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return;
        }

        var corpusRoot = Path.GetFullPath(CorpusRoot);
        var allowed = full.Equals(corpusRoot, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(corpusRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        if (!allowed)
        {
            throw new InvalidOperationException($"拒绝删除：仓库内目标不在 benchmarks/corpus 内（{full}）");
        }
    }

    /// <summary>删除语料目录树（不存在则跳过）</summary>
    public static void DeleteTree(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        EnsureDeletable(path);
        Directory.Delete(path, recursive: true);
    }
}

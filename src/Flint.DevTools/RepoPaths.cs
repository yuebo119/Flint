// 仓库路径定位：从当前目录逐级向上查找 Flint.slnx 锚点
// （与 .ai 脚本 _ai_root_find 同策略，替代原 Python 脚本的 os.path.dirname 逆推）

namespace Flint.DevTools;

/// <summary>
/// 仓库路径定位与关键目录约定
/// </summary>
internal static class RepoPaths
{
    private static readonly Lazy<string> LazyRoot = new(FindRepoRoot);

    /// <summary>仓库根（含 Flint.slnx 的目录）</summary>
    public static string RepoRoot => LazyRoot.Value;

    /// <summary>matrix20 主题矩阵产物根（gitignore 本地语料，{主题}/public-flint 与 {主题}/public）</summary>
    public static string Matrix20Root => Path.Combine(RepoRoot, "matrix20");

    /// <summary>演示语料根（demo-sites/fixtures/corpus）</summary>
    public static string FixturesCorpus => Path.Combine(RepoRoot, "demo-sites", "fixtures", "corpus");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Flint.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("无法定位仓库根（Flint.slnx）：请在 Flint 仓库目录内运行本工具");
    }
}

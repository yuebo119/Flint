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

    private static readonly Lazy<string> LazyTfm = new(ReadTargetFramework);

    /// <summary>
    /// 目标框架（net11.0 这类）——取自 Directory.Build.props 的 TargetFramework。
    /// 这里必须读而不是硬编码：TFM 升级后硬编码的产物路径会指向不存在的目录，
    /// 演示/编排命令会静默跑旧二进制（2026-10-22 审计实测 CLI 产物停留在上月）
    /// </summary>
    public static string TargetFrameworkMoniker => LazyTfm.Value;

    private static string ReadTargetFramework()
    {
        var propsPath = Path.Combine(RepoRoot, "Directory.Build.props");
        if (File.Exists(propsPath))
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                File.ReadAllText(propsPath), @"<TargetFramework>\s*([^<\s]+)\s*</TargetFramework>");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        throw new InvalidOperationException(
            "无法从 Directory.Build.props 读取 TargetFramework，拒绝用猜测的框架名定位产物");
    }

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

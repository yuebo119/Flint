// 基准路径默认值：与 Python 版 scripts/*.py 的默认参数逐字对应
// （Python: os.path.dirname(os.path.dirname(abspath(__file__))) 逆推仓库根，此处走 RepoPaths 锚点）

namespace Flint.DevTools.Bench;

/// <summary>
/// 基准默认路径
/// </summary>
internal static class DefaultPaths
{
    /// <summary>AOT 版 Flint.exe</summary>
    public static string FlintExe => Path.Combine(RepoPaths.RepoRoot, "benchmarks", "tools", "flint-aot", "Flint.exe");

    /// <summary>hugo.exe（对比引擎）</summary>
    public static string HugoExe => Path.Combine(RepoPaths.RepoRoot, "benchmarks", "tools", "hugo.exe");

    /// <summary>ssg-bench 语料根</summary>
    public static string SsgBenchRoot => Path.Combine(RepoGuard.CorpusRoot, "ssg-bench");

    /// <summary>complexity 语料根</summary>
    public static string ComplexityRoot => Path.Combine(RepoGuard.CorpusRoot, "complexity");

    /// <summary>asset-bench 语料根</summary>
    public static string AssetBenchRoot => Path.Combine(RepoGuard.CorpusRoot, "asset-bench");

    /// <summary>MDN 合并语料根（corpus-merged）</summary>
    public static string CorpusMergedRoot => Path.Combine(RepoGuard.CorpusRoot, "corpus-merged");
}

/// <summary>
/// UTF-8 无 BOM 编码（与 Python 版 open(..., encoding="utf-8") 写入行为一致）
/// </summary>
internal static class Utf8
{
    /// <summary>无 BOM 的 UTF-8</summary>
    public static readonly System.Text.Encoding NoBom = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
}

// Flint 静态站点生成器
// Git 日期源抽象——Hugo :git 特殊日期源的数据提供方

namespace Flint.Core.Abstractions;

/// <summary>
/// 内容文件 git 最后提交时间提供方（Hugo <c>:git</c> 日期源）。
/// 实现应在站点级缓存全量 git log 映射（一次进程调用构建），查询 O(1)。
/// 非 git 仓库 / git 不可用时返回 null，调用方按"该源缺省"降级，不致命。
/// </summary>
public interface IGitDateProvider
{
    /// <summary>
    /// 获取文件的最后一次提交修改时间
    /// </summary>
    /// <param name="filePath">文件绝对路径（须位于站点仓库内）</param>
    /// <returns>最后提交时间（commit 的 committer date）；无记录或 git 不可用时 null</returns>
    DateTimeOffset? GetLastCommitTime(string filePath);
}

// Flint 静态站点生成器
// 模块供应链安全契约测试
//
// 回归防护（审计 P1-10 四项）：
//   1. git 参数注入：refName/gitUrl 曾拼成字符串传给 git，以 '-' 开头的值会被
//      git 当成选项（如 --upload-pack=...）
//   2. zip slip / zip bomb：解压前不校验条目路径与展开体积
//   3. lockfile 的 sha256 只写不读，完整性校验是死功能
//   4. 子进程双流顺序读取的死锁形态（以并发读取 + 超时实现，无法在单测里直接断言，
//      由 CliTestRunner/既有 ModuleManager 测试的运行时不挂死间接覆盖）

using System.IO.Compression;
using Flint.Core.Modules;
using Xunit;

namespace Flint.Core.Tests.Modules;

public sealed class ModuleManagerSupplyChainTests : IDisposable
{
    private readonly string _siteDir;

    public ModuleManagerSupplyChainTests()
    {
        _siteDir = Path.Combine(Path.GetTempPath(), $"Flint_supply_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_siteDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_siteDir))
        {
            Directory.Delete(_siteDir, true);
        }

        GC.SuppressFinalize(this);
    }

    private static byte[] BuildZip(params (string Name, string Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }

        return ms.ToArray();
    }

    #region git 参数注入

    [Fact]
    public async Task ref以连字符开头时拒绝而非透传给git()
    {
        using var manager = new ModuleManager(_siteDir);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            async () => await manager.GetAsync("owner/repo@--upload-pack=evil").ConfigureAwait(false));

        Assert.Contains("git ref", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ref含空白时拒绝()
    {
        using var manager = new ModuleManager(_siteDir);

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await manager.GetAsync("owner/repo@feature branch").ConfigureAwait(false));
    }

    [Fact]
    public void git克隆地址由owner与repo拼装()
    {
        // 回归锁定：ParseRepository 返回元组，曾直接插值拼出
        // "https://github.com/(owner, repo).git" 这种必然失败的畸形地址
        Assert.Equal(
            "https://github.com/owner/repo.git",
            ModuleManager.BuildGitUrl("owner", "repo"));
    }

    [Fact]
    public async Task 合法ref不被参数校验拒绝()
    {
        using var manager = new ModuleManager(_siteDir);

        // 本用例只关心不是"参数校验类"拒绝：真克隆必然失败（本机/CI 无该仓库），
        // 失败形态是 InvalidOperationException，不是 ArgumentException
        var ex = await Record.ExceptionAsync(
            async () => await manager.GetAsync("owner/repo@flint-nonexistent-ref-xyz").ConfigureAwait(false));

        Assert.IsNotType<ArgumentException>(ex);
    }

    #endregion

    #region zip 解压防护

    [Fact]
    public void zip条目越出目标目录时拒绝()
    {
        var zip = BuildZip(("../escaped.txt", "pwned"));
        var target = Path.Combine(_siteDir, "themes", "evil");

        var ex = Assert.Throws<InvalidOperationException>(
            () => ModuleManager.ExtractModule(zip, target, CancellationToken.None));

        Assert.Contains("zip slip", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void zip展开体积超限时拒绝()
    {
        var zip = BuildZip(("a.txt", new string('x', 100)), ("b.txt", new string('y', 100)));
        var target = Path.Combine(_siteDir, "themes", "bomb");

        var ex = Assert.Throws<InvalidOperationException>(
            () => ModuleManager.ExtractModule(zip, target, maxBytes: 50, maxEntries: 100, CancellationToken.None));

        Assert.Contains("上限", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void zip条目数超限时拒绝()
    {
        var zip = BuildZip(("a.txt", "a"), ("b.txt", "b"), ("c.txt", "c"));
        var target = Path.Combine(_siteDir, "themes", "many");

        var ex = Assert.Throws<InvalidOperationException>(
            () => ModuleManager.ExtractModule(zip, target, maxBytes: long.MaxValue, maxEntries: 2, CancellationToken.None));

        Assert.Contains("条目数", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 正常压缩包按上限内解压并换入目标目录()
    {
        // GitHub 归档形态：单层 <repo>-<tag>/ 前缀目录被整体换入（解压逻辑按
        // "唯一顶层目录即模块根"取件，这是 GitHub archive 的固定形态）
        var zip = BuildZip(("repo-1.0.0/README.md", "hello"), ("repo-1.0.0/sub/file.txt", "nested"));
        var target = Path.Combine(_siteDir, "themes", "good");

        ModuleManager.ExtractModule(zip, target, CancellationToken.None);

        var files = Directory.GetFiles(target, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(target, f).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["README.md", "sub/file.txt"], files);
    }

    [Fact]
    public void 解压失败时不留下临时目录()
    {
        var zip = BuildZip(("../escape.txt", "pwned"));
        var target = Path.Combine(_siteDir, "themes", "tmpcheck");

        Assert.Throws<InvalidOperationException>(
            () => ModuleManager.ExtractModule(zip, target, CancellationToken.None));

        var leftovers = Directory.GetDirectories(Path.Combine(_siteDir, "themes"), "tmpcheck.*");
        Assert.Empty(leftovers);
    }

    #endregion

    #region lockfile 完整性校验

    private void WriteLock(string moduleName, string version, string sha256)
    {
        File.WriteAllText(
            Path.Combine(_siteDir, "Flint.lock"),
            $"[\"{moduleName}\"]\n\"version\" = \"{version}\"\n\"repository\" = \"x/y\"\n\"sha256\" = \"{sha256}\"\n");
    }

    [Fact]
    public async Task 记录版本一致但哈希不符时拒绝()
    {
        WriteLock("repo", "v1.0.0", "aabbccddeeff00112233445566778899");
        using var manager = new ModuleManager(_siteDir);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await manager.VerifyAgainstLockAsync("repo", "v1.0.0", "ffffffffffffffff", CancellationToken.None).ConfigureAwait(false));

        Assert.Contains("lockfile", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 哈希一致时通过()
    {
        const string sha = "aabbccddeeff00112233445566778899";
        WriteLock("repo", "v1.0.0", sha);
        using var manager = new ModuleManager(_siteDir);

        await manager.VerifyAgainstLockAsync("repo", "v1.0.0", sha, CancellationToken.None);
    }

    [Fact]
    public async Task 版本不同或无记录时跳过校验()
    {
        WriteLock("repo", "v1.0.0", "aabbccddeeff00112233445566778899");
        using var manager = new ModuleManager(_siteDir);

        // 换版本安装（记录的是 v1.0.0，要装 v2.0.0）与全新模块都无对照基准
        await manager.VerifyAgainstLockAsync("repo", "v2.0.0", "ffffffffffffffff", CancellationToken.None);
        await manager.VerifyAgainstLockAsync("other", "v1.0.0", "ffffffffffffffff", CancellationToken.None);
    }

    [Fact]
    public async Task git与本地安装的占位哈希不参与比对()
    {
        WriteLock("repo", "main", "unknown");
        using var manager = new ModuleManager(_siteDir);

        await manager.VerifyAgainstLockAsync("repo", "main", "ffffffffffffffff", CancellationToken.None);
    }

    #endregion
}
// Flint 静态站点生成器
// 输出目录确保器：段级缓存 + Windows 单段创建原语，消除逐次全路径回溯的存在性检查
//
// 为什么（benchmarks/HOTSPOTS.md 批次 0 实测 + .NET 实现行为）：
// - 目录创建占"写入输出"相约 60%（10000 目录裸基准 mkdir = 1947ms）
// - Directory.CreateDirectory(全路径) 在托管层逐段做存在性检查：万页站点
//   每页一次全路径调用 ≈ 3~4 万次系统调用，其中 ~75% 是对已存在祖先段的
//   重复检查（页面目录彼此唯一，但 /posts、/tags 等父段被重复验证上万次）
// - 本类改法：① 段级缓存跳过已知存在的前缀（0 系统调用）② Windows 对
//   首个缺失段起逐段调用 kernel32 CreateDirectoryW（父段已知存在 → 每个
//   新目录层仅 1 次系统调用；LibraryImport 直连，AOT 下无反射）
//
// 约束：
// - 缓存前缀封闭（入缓存的一定是整条前缀链都已创建），故"前缀未命中 ⇒
//   更深前缀必未命中"，扫描可在首个未命中处停止
// - 缓存只保证单进程内"命中 ⇒ 目录仍在"；CleanOutput 会在构建开始时删除
//   输出目录，故 BuildAsync/IncrementalBuildAsync 入口必须 Reset()（已接线）
// - UNC 路径与相对路径不做段级手术（逐段语义易错），回退托管实现但仍受
//   全路径缓存保护（重复目录零调用）

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Flint.Core.IO;

/// <summary>输出目录确保器（构建级缓存，见类型头注释）</summary>
public static partial class OutputDirectoryEnsurer
{
    /// <summary>
    /// 已知存在的目录集。Windows 下大小写不敏感（NTFS 默认语义），其他平台区分大小写
    /// </summary>
    private static readonly ConcurrentDictionary<string, byte> KnownExisting =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private static readonly bool IsWindows = OperatingSystem.IsWindows();

    /// <summary>
    /// 重置缓存。必须在每次构建入口（CleanOutput 删目录之前）调用——
    /// 否则清理后的目录会被缓存命中跳过重建，写入阶段报目录不存在
    /// </summary>
    public static void Reset() => KnownExisting.Clear();

    /// <summary>确保目录存在（不存在则创建整条链；已知存在则零系统调用返回）</summary>
    public static void Ensure(string? directory)
    {
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        var dir = Path.TrimEndingDirectorySeparator(directory);
        if (dir.Length == 0)
        {
            return;
        }

        if (KnownExisting.ContainsKey(dir))
        {
            return;
        }

        if (!IsWindows || !Path.IsPathRooted(dir) || dir.StartsWith(@"\\", StringComparison.Ordinal))
        {
            EnsureManaged(dir);
            return;
        }

        EnsureSegmentedWindows(dir);
    }

    /// <summary>托管回退：全路径创建（内部逐段检查），入缓存仅整路径（不维持前缀封闭）</summary>
    private static void EnsureManaged(string dir)
    {
        if (KnownExisting.ContainsKey(dir))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        KnownExisting.TryAdd(dir, 0);
    }

    /// <summary>
    /// Windows 段级创建：从最深已缓存前缀之后的首个缺失段起，逐段单建。
    /// 缓存前缀封闭 ⇒ 首个未命中段之后必无已缓存段，一次扫描即可定位起点
    /// </summary>
    private static void EnsureSegmentedWindows(string dir)
    {
        // 1) 扫描各段结束位置，找最深已缓存前缀（保留原始字符串表示：兼容盘符）
        var deepestCachedEnd = -1;
        var segStart = 0;
        for (var i = 0; i <= dir.Length; i++)
        {
            if (i < dir.Length && dir[i] != '\\' && dir[i] != '/')
            {
                continue;
            }

            if (i > segStart)
            {
                var prefix = dir.Substring(0, i);
                if (KnownExisting.ContainsKey(prefix))
                {
                    deepestCachedEnd = i;
                }
                else
                {
                    break; // 前缀封闭：此段未缓存 ⇒ 更深段必未缓存
                }
            }

            segStart = i + 1;
        }

        // 2) 升序逐段推进：已缓存段（结束位 <= deepestCachedEnd）跳过，其后逐段单建
        segStart = 0;
        for (var i = 0; i <= dir.Length; i++)
        {
            if (i < dir.Length && dir[i] != '\\' && dir[i] != '/')
            {
                continue;
            }

            if (i > segStart && i > deepestCachedEnd)
            {
                var prefix = dir.Substring(0, i);
                if (prefix.EndsWith(':'))
                {
                    // 盘符段（"C:"）天然存在，入缓存即可，勿对其调用创建原语
                    KnownExisting.TryAdd(prefix, 0);
                }
                else
                {
                    CreateSingleLevel(prefix);
                }
            }

            segStart = i + 1;
        }
    }

    /// <summary>创建单层目录（调用方保证父段已存在或为盘符根）；已存在视为成功</summary>
    private static void CreateSingleLevel(string prefix)
    {
        if (KnownExisting.ContainsKey(prefix))
        {
            return;
        }

        if (!CreateDirectoryW(prefix, IntPtr.Zero))
        {
            var err = Marshal.GetLastPInvokeError();
            if (err != 183) // ERROR_ALREADY_EXISTS：并行兄弟同时创建同层
            {
                throw new IOException(
                    $"无法创建目录 \"{prefix}\"：{new Win32Exception(err).Message} (0x{err:X8})");
            }
        }

        KnownExisting.TryAdd(prefix, 0);
    }

    // 经典 DllImport 而非 LibraryImport：简单签名（LPWStr+IntPtr+BOOL）在
    // NativeAOT 下走内置封送零反射；且绕开本机 SDK 上 LibraryImportGenerator
    // 的 IndexOutOfRange 崩溃（CS8785）。CA5392 要求的搜索路径钉死已加
    [DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    [DllImport(
        "kernel32.dll",
        EntryPoint = "CreateDirectoryW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern bool CreateDirectoryW(
        string lpFileName,
        IntPtr lpSecurityAttributes);
}

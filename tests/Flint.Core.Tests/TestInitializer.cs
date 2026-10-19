// Flint 静态站点生成器
// 测试环境初始化与集合定义

using System.Runtime.CompilerServices;
using Xunit;

namespace Flint.Core.Tests;

/// <summary>
/// 测试进程环境初始化。产品已改为外部 Dart Sass CLI，Windows 本地测试
/// 复用仓库内置的 benchmark dart-sass，避免强依赖全局 sass。
/// </summary>
public static class TestEnvironment
{
    [ModuleInitializer]
    public static void Initialize()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FLINT_SASS")))
        {
            return;
        }

        var repoRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var sass = Path.Combine(repoRoot, "benchmarks", "tools", "flint-aot", "dart-sass.win-x64", "sass.bat");
        if (File.Exists(sass))
        {
            Environment.SetEnvironmentVariable("FLINT_SASS", sass);
        }
    }
}

/// <summary>
/// xUnit 测试集合定义
/// 环境变量敏感测试串行化：进程级环境变量（Environment.SetEnvironmentVariable）
/// 是整个测试进程共享的全局状态，设置 Flint_* 变量的测试与经
/// ConfigLoader.LoadAsync（末尾 ApplyOverrides 读进程 env）消费配置的测试
/// 并行运行时构成竞态——曾致 ConfigLoaderTests 偶发断言失败（单跑必过）。
/// 同集合内测试串行执行，隔离这一共享状态
/// </summary>
[CollectionDefinition("EnvironmentSensitive")]
public class EnvironmentSensitiveTestGroup
{
}

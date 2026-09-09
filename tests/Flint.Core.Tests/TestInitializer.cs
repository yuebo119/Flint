// Flint 静态站点生成器
// 测试初始化器 - 配置 JavaScript 引擎

using JavaScriptEngineSwitcher.Core;
using JavaScriptEngineSwitcher.V8;
using Xunit;

namespace Flint.Core.Tests;

/// <summary>
/// 测试初始化器
/// 在测试运行前配置必要的全局设置
/// </summary>
public static class TestInitializer
{
    private static bool _initialized;
    private static readonly object _lock = new();

    /// <summary>
    /// 初始化 JavaScript 引擎
    /// DartSassHost 需要一个 JavaScript 引擎来运行 Dart Sass
    /// </summary>
    public static void InitializeJavaScriptEngine()
    {
        if (_initialized)
            return;

        lock (_lock)
        {
            if (_initialized)
                return;

            var engineSwitcher = JsEngineSwitcher.Current;
            engineSwitcher.EngineFactories.Add(new V8JsEngineFactory());
            engineSwitcher.DefaultEngineName = V8JsEngine.EngineName;

            _initialized = true;
        }
    }
}

/// <summary>
/// xUnit 测试集合定义
/// 用于 SassCompiler 测试，确保 JavaScript 引擎已初始化
/// </summary>
[CollectionDefinition("SassCompiler")]
public class SassCompilerTestGroup : ICollectionFixture<SassCompilerFixture>
{
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

/// <summary>
/// SassCompiler 测试夹具
/// 在测试集合开始时初始化 JavaScript 引擎
/// </summary>
public class SassCompilerFixture : IDisposable
{
    public SassCompilerFixture()
    {
        TestInitializer.InitializeJavaScriptEngine();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}

// Flint 静态站点生成器
// 测试初始化器 - 配置 JavaScript 引擎

using JavaScriptEngineSwitcher.Core;
using JavaScriptEngineSwitcher.V8;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = false)]

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

// -----------------------------------------------------------------------
// <copyright file="TestInitializer.cs" company="Flint">
//     Copyright (c) Flint. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Runtime.CompilerServices;
using JavaScriptEngineSwitcher.Core;
using JavaScriptEngineSwitcher.V8;

namespace Flint.IntegrationTests;

/// <summary>
/// 测试初始化器
/// 用于在测试运行前进行全局初始化
/// </summary>
public static class TestInitializer
{
    private static bool _initialized;
    private static readonly object _lock = new();

    /// <summary>
    /// 模块初始化方法
    /// 在测试程序集加载时自动执行
    /// </summary>
    [ModuleInitializer]
    public static void Initialize()
    {
        InitializeJavaScriptEngine();
    }

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
            if (!engineSwitcher.EngineFactories.Any(f => f.EngineName == V8JsEngine.EngineName))
            {
                engineSwitcher.EngineFactories.Add(new V8JsEngineFactory());
            }
            engineSwitcher.DefaultEngineName = V8JsEngine.EngineName;

            _initialized = true;
        }
    }
}

/// <summary>
/// xUnit 测试集合定义 - 资源管道测试
/// 确保 JavaScript 引擎已初始化
/// </summary>
[Xunit.CollectionDefinition("AssetPipeline")]
public class AssetPipelineTestGroup : Xunit.ICollectionFixture<AssetPipelineFixture>
{
}

/// <summary>
/// DevServer 测试集合：起真实 Kestrel 实例（端口探测/绑定、HTTP 时序）。
/// DisableParallelization 使其与其他 collection 也不并行——并行负载下探测到的
/// 端口可能被并行的起服测试抢占、HTTP 时序断言被 CPU 争抢翻转
/// （全量三轮实测出现过 StartWhileRunning_ThrowsException 与
/// GetAsync_NonExistentFile_Returns404 的环境性失败，单跑均绿）
/// </summary>
[Xunit.CollectionDefinition("DevServer", DisableParallelization = true)]
public sealed class DevServerTestGroup
{
}

/// <summary>
/// 性能测量集合：墙钟/内存分配速率断言在并行负载下被噪声主导
/// （GC.GetTotalAllocatedBytes 是进程级计数，并行测试的分配全部计入）。
/// DisableParallelization 使测量期独占进程
/// </summary>
[Xunit.CollectionDefinition("Performance", DisableParallelization = true)]
public sealed class PerformanceTestGroup
{
}

/// <summary>
/// 资源管道测试夹具
/// 在测试集合开始时初始化 JavaScript 引擎
/// </summary>
public class AssetPipelineFixture : IDisposable
{
    public AssetPipelineFixture()
    {
        TestInitializer.InitializeJavaScriptEngine();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}

// -----------------------------------------------------------------------
// <copyright file="TestInitializer.cs" company="Flint">
//     Copyright (c) Flint. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Flint.IntegrationTests;

/// <summary>
/// xUnit 测试集合定义 - 资源管道测试
/// </summary>
[Xunit.CollectionDefinition("AssetPipeline")]
public class AssetPipelineTestGroup
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

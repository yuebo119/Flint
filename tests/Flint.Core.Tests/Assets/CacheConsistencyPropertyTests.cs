// Flint 静态站点生成器
// 缓存一致性属性测试
// **Feature: Flint, Property 11: 缓存一致性**

using System.Collections.Concurrent;
using System.Text;
using Flint.Core.Abstractions;
using Flint.Core.Assets;
using Flint.Core.Models;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Flint.Core.Tests.Assets;

/// <summary>
/// 缓存一致性属性测试
/// **Validates: Requirements 4.11, 9.10**
/// 
/// Property 11: 缓存一致性
/// 对于任意已处理的资源，缓存命中时返回的结果应该与重新处理的结果完全一致。
/// </summary>
public class CacheConsistencyPropertyTests
{
    #region Property 11.1: 内存缓存一致性

    /// <summary>
    /// **Property 11: 缓存一致性 - 内存缓存命中返回相同结果**
    /// 相同内容的资源，缓存命中时应该返回与首次处理完全一致的结果
    /// **Validates: Requirements 4.11**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property MemoryCache_HitReturnsIdenticalResult()
    {
        return Prop.ForAll(
            ArbMap.Default.ArbFor<NonEmptyString>(),
            content =>
            {
                var pipeline = new AssetPipeline(new AssetPipelineOptions
                {
                    EnableMemoryCache = true,
                    EnablePersistentCache = false
                });

                var asset = CreateCssAsset(content.Get);

                // 首次处理
                var result1 = pipeline.ProcessAsync(asset).AsTask().Result;
                // 缓存命中
                var result2 = pipeline.ProcessAsync(asset).AsTask().Result;

                // 验证结果完全一致
                return result1.ContentHash == result2.ContentHash &&
                       result1.Integrity == result2.Integrity &&
                       result1.OutputPath == result2.OutputPath &&
                       result1.MediaType == result2.MediaType &&
                       result1.Content.Span.SequenceEqual(result2.Content.Span);
            });
    }

    /// <summary>
    /// **Property 11: 缓存一致性 - 缓存清除后重新处理结果一致**
    /// 清除缓存后重新处理，应该产生与之前相同的结果
    /// **Validates: Requirements 4.11**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CacheClear_ReprocessProducesSameResult()
    {
        return Prop.ForAll(
            ArbMap.Default.ArbFor<NonEmptyString>(),
            content =>
            {
                var pipeline = new AssetPipeline(new AssetPipelineOptions
                {
                    EnableMemoryCache = true
                });

                var asset = CreateCssAsset(content.Get);

                // 首次处理
                var result1 = pipeline.ProcessAsync(asset).AsTask().Result;

                // 清除缓存
                pipeline.ClearMemoryCache();

                // 重新处理
                var result2 = pipeline.ProcessAsync(asset).AsTask().Result;

                // 验证结果一致
                return result1.ContentHash == result2.ContentHash &&
                       result1.Integrity == result2.Integrity;
            });
    }

    #endregion

    #region Property 11.2: 持久化缓存一致性

    /// <summary>
    /// **Property 11: 缓存一致性 - 持久化缓存命中返回相同结果**
    /// 使用持久化缓存时，缓存命中应该返回与首次处理完全一致的结果
    /// **Validates: Requirements 9.10**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property PersistentCache_HitReturnsIdenticalResult()
    {
        return Prop.ForAll(
            ArbMap.Default.ArbFor<NonEmptyString>(),
            content =>
            {
                var cache = new InMemoryCache();
                var pipeline = new AssetPipeline(
                    new AssetPipelineOptions
                    {
                        EnableMemoryCache = false,
                        EnablePersistentCache = true
                    },
                    cache: cache);

                var asset = CreateCssAsset(content.Get);

                // 首次处理（写入缓存）
                var result1 = pipeline.ProcessAsync(asset).AsTask().Result;

                // 创建新管道实例（模拟重启）
                var pipeline2 = new AssetPipeline(
                    new AssetPipelineOptions
                    {
                        EnableMemoryCache = false,
                        EnablePersistentCache = true
                    },
                    cache: cache);

                // 缓存命中
                var result2 = pipeline2.ProcessAsync(asset).AsTask().Result;

                // 验证结果一致
                return result1.ContentHash == result2.ContentHash &&
                       result1.Integrity == result2.Integrity;
            });
    }

    #endregion

    #region Property 11.3: 批量处理缓存一致性

    /// <summary>
    /// **Property 11: 缓存一致性 - 批量处理与单独处理结果一致**
    /// 批量处理的结果应该与单独处理每个资源的结果一致
    /// **Validates: Requirements 4.11**
    /// </summary>
    [Property(MaxTest = 50)]
    public Property BatchProcess_ProducesSameResultsAsIndividual()
    {
        return Prop.ForAll(
            Gen.Choose(1, 5)
                .SelectMany(count => Gen.ListOf<string>(Gen.Elements("a", "b", "c", "d", "e")
                    .Select(s => s + Guid.NewGuid().ToString("N")[..8]), count))
                .ToArbitrary(),
            contents =>
            {
                var contentList = contents?.ToList();
                if (contentList == null || contentList.Count == 0)
                    return true;

                var pipeline = new AssetPipeline(new AssetPipelineOptions
                {
                    EnableMemoryCache = false
                });

                var assets = contentList.Select((c, i) => CreateCssAsset(c, $"file{i}.css")).ToList();

                // 单独处理
                var individualResults = assets
                    .Select(a => pipeline.ProcessAsync(a).AsTask().Result)
                    .ToList();

                // 批量处理
                var batchResults = pipeline.ProcessBatchAsync(assets).AsTask().Result;

                // 验证结果一致（按内容哈希匹配）
                var individualHashes = individualResults.Select(r => r.ContentHash).OrderBy(h => h).ToList();
                var batchHashes = batchResults.Select(r => r.ContentHash).OrderBy(h => h).ToList();

                return individualHashes.SequenceEqual(batchHashes);
            });
    }

    #endregion

    #region Property 11.4: 并发处理缓存一致性

    /// <summary>
    /// **Property 11: 缓存一致性 - 并发处理相同资源结果一致**
    /// 多个线程同时处理相同资源时，所有结果应该一致
    /// **Validates: Requirements 4.11**
    /// </summary>
    [Property(MaxTest = 50)]
    public Property ConcurrentProcess_ProducesConsistentResults()
    {
        return Prop.ForAll(
            ArbMap.Default.ArbFor<NonEmptyString>(),
            Gen.Choose(2, 8).ToArbitrary(),
            (content, threadCount) =>
            {
                var pipeline = new AssetPipeline(new AssetPipelineOptions
                {
                    EnableMemoryCache = true,
                    MaxParallelism = threadCount
                });

                var asset = CreateCssAsset(content.Get);
                var results = new ConcurrentBag<ProcessedAsset>();

                // 并发处理
                Parallel.For(0, threadCount, _ =>
                {
                    var result = pipeline.ProcessAsync(asset).AsTask().Result;
                    results.Add(result);
                });

                // 验证所有结果一致
                var hashes = results.Select(r => r.ContentHash).Distinct().ToList();
                var integrities = results.Select(r => r.Integrity).Distinct().ToList();

                return hashes.Count == 1 && integrities.Count == 1;
            });
    }

    #endregion

    #region Property 11.5: 内容哈希作为缓存键的正确性

    /// <summary>
    /// **Property 11: 缓存一致性 - 相同内容产生相同哈希和完整性**
    /// 相同内容的资源应该产生相同的内容哈希和 SRI 完整性哈希
    /// **Validates: Requirements 4.11, 9.10**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property SameContent_ProducesSameHashAndIntegrity()
    {
        return Prop.ForAll(
            ArbMap.Default.ArbFor<NonEmptyString>(),
            content =>
            {
                // 使用两个独立的管道实例，禁用缓存
                var pipeline1 = new AssetPipeline(new AssetPipelineOptions
                {
                    EnableMemoryCache = false,
                    EnablePersistentCache = false
                });
                var pipeline2 = new AssetPipeline(new AssetPipelineOptions
                {
                    EnableMemoryCache = false,
                    EnablePersistentCache = false
                });

                var asset1 = CreateCssAsset(content.Get, "path1/style.css");
                var asset2 = CreateCssAsset(content.Get, "path2/style.css");

                // 分别处理
                var result1 = pipeline1.ProcessAsync(asset1).AsTask().Result;
                var result2 = pipeline2.ProcessAsync(asset2).AsTask().Result;

                // 验证内容哈希和完整性相同（因为内容相同）
                return result1.ContentHash == result2.ContentHash &&
                       result1.Integrity == result2.Integrity;
            });
    }

    /// <summary>
    /// **Property 11: 缓存一致性 - 不同内容使用不同缓存**
    /// 不同内容的资源应该有不同的缓存条目
    /// **Validates: Requirements 4.11**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property DifferentContent_UsesDifferentCache()
    {
        return Prop.ForAll(
            ArbMap.Default.ArbFor<NonEmptyString>(),
            ArbMap.Default.ArbFor<NonEmptyString>(),
            (content1, content2) =>
            {
                // 确保内容不同
                if (content1.Get == content2.Get)
                    return true;

                var pipeline = new AssetPipeline(new AssetPipelineOptions
                {
                    EnableMemoryCache = true
                });

                var asset1 = CreateCssAsset(content1.Get);
                var asset2 = CreateCssAsset(content2.Get);

                var result1 = pipeline.ProcessAsync(asset1).AsTask().Result;
                var result2 = pipeline.ProcessAsync(asset2).AsTask().Result;

                // 验证缓存条目数量为 2
                return pipeline.MemoryCacheCount == 2 &&
                       result1.ContentHash != result2.ContentHash;
            });
    }

    #endregion

    #region Property 11.6: 流式处理缓存一致性

    /// <summary>
    /// **Property 11: 缓存一致性 - 流式处理结果与批量处理一致**
    /// 流式处理的结果应该与批量处理的结果一致
    /// **Validates: Requirements 4.11**
    /// </summary>
    [Property(MaxTest = 30)]
    public Property StreamProcess_ProducesSameResultsAsBatch()
    {
        return Prop.ForAll(
            Gen.ListOf<NonEmptyString>(ArbMap.Default.GeneratorFor<NonEmptyString>(), 3)
                .ToArbitrary(),
            contents =>
            {
                var contentList = contents?.ToList();
                if (contentList == null || contentList.Count == 0)
                    return true;

                var pipeline1 = new AssetPipeline(new AssetPipelineOptions { EnableMemoryCache = false });
                var pipeline2 = new AssetPipeline(new AssetPipelineOptions { EnableMemoryCache = false });

                var assets = contentList.Select((c, i) => CreateCssAsset(c.Get, $"file{i}.css")).ToList();

                // 批量处理
                var batchResults = pipeline1.ProcessBatchAsync(assets).AsTask().Result;

                // 流式处理
                var streamResults = new List<ProcessedAsset>();
                var asyncAssets = ToAsyncEnumerable(assets);
                var enumerator = pipeline2.ProcessStreamAsync(asyncAssets).GetAsyncEnumerator();
                while (enumerator.MoveNextAsync().AsTask().Result)
                {
                    streamResults.Add(enumerator.Current);
                }

                // 验证结果一致（按哈希排序比较）
                var batchHashes = batchResults.Select(r => r.ContentHash).OrderBy(h => h).ToList();
                var streamHashes = streamResults.Select(r => r.ContentHash).OrderBy(h => h).ToList();

                return batchHashes.SequenceEqual(streamHashes);
            });
    }

    private static async IAsyncEnumerable<AssetFile> ToAsyncEnumerable(IEnumerable<AssetFile> assets)
    {
        foreach (var asset in assets)
        {
            yield return asset;
            await Task.Yield();
        }
    }

    #endregion

    #region 辅助方法和类

    private static AssetFile CreateCssAsset(string content, string path = "styles/main.css")
    {
        return new AssetFile
        {
            SourcePath = path,
            MediaType = "text/css",
            Content = Encoding.UTF8.GetBytes(content),
            ModifiedTime = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// 内存缓存实现（用于测试）
    /// </summary>
    private sealed class InMemoryCache : IContentAddressableCache
    {
        private readonly ConcurrentDictionary<string, object> _cache = new();
        private long _hitCount;
        private long _missCount;

        public ValueTask<T?> GetAsync<T>(string contentHash, CancellationToken cancellationToken = default)
            where T : class
        {
            if (_cache.TryGetValue(contentHash, out var value))
            {
                Interlocked.Increment(ref _hitCount);
                return ValueTask.FromResult((T?)value);
            }
            Interlocked.Increment(ref _missCount);
            return ValueTask.FromResult<T?>(null);
        }

        public ValueTask SetAsync<T>(string contentHash, T value, CancellationToken cancellationToken = default)
            where T : class
        {
            _cache[contentHash] = value;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> ContainsAsync(string contentHash, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(_cache.ContainsKey(contentHash));
        }

        public ValueTask<bool> RemoveAsync(string contentHash, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(_cache.TryRemove(contentHash, out _));
        }

        public ValueTask ClearAsync(CancellationToken cancellationToken = default)
        {
            _cache.Clear();
            return ValueTask.CompletedTask;
        }

        public CacheStatistics GetStatistics()
        {
            return new CacheStatistics
            {
                ItemCount = _cache.Count,
                TotalSize = 0,
                HitCount = _hitCount,
                MissCount = _missCount
            };
        }
    }

    #endregion
}

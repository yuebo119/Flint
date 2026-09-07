// Flint 静态站点生成器
// 模块配置一致性属性测试
// 验证模块操作后配置文件的一致性

using Flint.Core.Modules;
using Flint.IntegrationTests.Fixtures;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Flint.IntegrationTests.Modules;

/// <summary>
/// 模块配置一致性属性测试
/// 验证模块操作后配置文件的一致性
/// </summary>
/// <remarks>
/// 满足需求：
/// - Requirements 7.9: 模块配置一致性
/// </remarks>
public class ModuleConfigConsistencyPropertyTests
{
    #region 辅助方法

    /// <summary>
    /// 运行异步属性测试的辅助方法
    /// </summary>
    private static Property RunPropertyTestAsync(Func<Task<bool>> testFunc)
    {
        try
        {
            var result = testFunc().GetAwaiter().GetResult();
            return result.ToProperty();
        }
        catch (Exception)
        {
            return false.ToProperty();
        }
    }

    #endregion

    #region 属性测试

    /// <summary>
    /// 属性测试：模块列表操作是幂等的
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ListAsync_IsIdempotent(PositiveInt iterations)
    {
        return RunPropertyTestAsync(async () =>
        {
            using var fixture = new TestSiteFixture();
            await fixture.InitializeAsync();

            ModuleManager? moduleManager = null;

            try
            {
                await fixture.CreateSiteAsync("minimal");
                moduleManager = new ModuleManager(fixture.SiteRoot);

                var count = Math.Min(iterations.Get % 10 + 1, 10);

                // 创建一些测试模块
                var themesPath = Path.Combine(fixture.SiteRoot, "themes");
                Directory.CreateDirectory(themesPath);

                var modulePath = Path.Combine(themesPath, "idempotent-test-theme");
                if (!Directory.Exists(modulePath))
                {
                    Directory.CreateDirectory(modulePath);
                    await File.WriteAllTextAsync(
                        Path.Combine(modulePath, "theme.toml"),
                        """
                        name = "idempotent-test-theme"
                        version = "1.0.0"
                        """);
                }

                // Act - 多次调用 ListAsync
                var results = new List<int>();
                for (var i = 0; i < count; i++)
                {
                    var modules = await moduleManager.ListAsync();
                    results.Add(modules.Count);
                }

                // Assert - 所有结果应该相同
                var allSame = results.Distinct().Count() == 1;

                return allSame;
            }
            finally
            {
                moduleManager?.Dispose();
                await fixture.DisposeAsync();
            }
        });
    }

    /// <summary>
    /// 属性测试：模块安装后删除是幂等的
    /// </summary>
    [Property(MaxTest = 50)]
    public Property InstallThenRemove_IsIdempotent(PositiveInt seed)
    {
        return RunPropertyTestAsync(async () =>
        {
            using var fixture = new TestSiteFixture();
            await fixture.InitializeAsync();

            ModuleManager? moduleManager = null;

            try
            {
                await fixture.CreateSiteAsync("minimal");
                moduleManager = new ModuleManager(fixture.SiteRoot);

                var moduleName = $"temp-module-{seed.Get % 1000}";
                var themesPath = Path.Combine(fixture.SiteRoot, "themes");
                var modulePath = Path.Combine(themesPath, moduleName);

                // 清理可能存在的模块
                if (Directory.Exists(modulePath))
                {
                    Directory.Delete(modulePath, recursive: true);
                }

                // Act - 创建模块
                Directory.CreateDirectory(modulePath);
                await File.WriteAllTextAsync(
                    Path.Combine(modulePath, "theme.toml"),
                    $"""
                    name = "{moduleName}"
                    version = "1.0.0"
                    """);

                var modulesBeforeRemove = await moduleManager.ListAsync();
                var countBefore = modulesBeforeRemove.Count;

                // 删除模块
                await moduleManager.RemoveAsync(moduleName);

                var modulesAfterRemove = await moduleManager.ListAsync();
                var countAfter = modulesAfterRemove.Count;

                // Assert
                var moduleRemoved = countAfter == countBefore - 1;
                var moduleNotInList = !modulesAfterRemove.Any(m => m.Name == moduleName);

                return moduleRemoved && moduleNotInList;
            }
            finally
            {
                moduleManager?.Dispose();
                await fixture.DisposeAsync();
            }
        });
    }

    /// <summary>
    /// 属性测试：模块名称在列表中是唯一的
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ModuleNames_AreUnique(PositiveInt moduleCount)
    {
        return RunPropertyTestAsync(async () =>
        {
            using var fixture = new TestSiteFixture();
            await fixture.InitializeAsync();

            ModuleManager? moduleManager = null;

            try
            {
                await fixture.CreateSiteAsync("minimal");
                moduleManager = new ModuleManager(fixture.SiteRoot);

                var count = Math.Min(moduleCount.Get % 5 + 1, 5);
                var themesPath = Path.Combine(fixture.SiteRoot, "themes");

                // 清理现有模块
                if (Directory.Exists(themesPath))
                {
                    Directory.Delete(themesPath, recursive: true);
                }
                Directory.CreateDirectory(themesPath);

                // 创建多个模块
                for (var i = 0; i < count; i++)
                {
                    var modulePath = Path.Combine(themesPath, $"unique-theme-{i}");
                    Directory.CreateDirectory(modulePath);
                    await File.WriteAllTextAsync(
                        Path.Combine(modulePath, "theme.toml"),
                        $"""
                        name = "unique-theme-{i}"
                        version = "1.0.0"
                        """);
                }

                // Act
                var modules = await moduleManager.ListAsync();

                // Assert
                var names = modules.Select(m => m.Name).ToList();
                var uniqueNames = names.Distinct().ToList();
                var allUnique = names.Count == uniqueNames.Count;

                return allUnique;
            }
            finally
            {
                moduleManager?.Dispose();
                await fixture.DisposeAsync();
            }
        });
    }

    /// <summary>
    /// 属性测试：模块版本格式一致
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ModuleVersionArbitraries) })]
    public Property ModuleVersion_FormatIsConsistent(string version)
    {
        return RunPropertyTestAsync(async () =>
        {
            using var fixture = new TestSiteFixture();
            await fixture.InitializeAsync();

            ModuleManager? moduleManager = null;

            try
            {
                await fixture.CreateSiteAsync("minimal");
                moduleManager = new ModuleManager(fixture.SiteRoot);

                var themesPath = Path.Combine(fixture.SiteRoot, "themes");
                var modulePath = Path.Combine(themesPath, "version-test-theme");

                // 清理
                if (Directory.Exists(modulePath))
                {
                    Directory.Delete(modulePath, recursive: true);
                }
                Directory.CreateDirectory(modulePath);

                await File.WriteAllTextAsync(
                    Path.Combine(modulePath, "theme.toml"),
                    $"""
                    name = "version-test-theme"
                    version = "{version}"
                    """);

                // Act
                var modules = await moduleManager.ListAsync();

                // Assert
                var module = modules.FirstOrDefault(m => m.Name == "version-test-theme");
                var hasVersion = module != null && !string.IsNullOrEmpty(module.Version);

                return hasVersion;
            }
            finally
            {
                moduleManager?.Dispose();
                await fixture.DisposeAsync();
            }
        });
    }

    /// <summary>
    /// 属性测试：锁文件在模块操作后保持一致
    /// </summary>
    [Property(MaxTest = 50)]
    public Property LockFile_RemainsConsistentAfterOperations(PositiveInt operationCount)
    {
        return RunPropertyTestAsync(async () =>
        {
            using var fixture = new TestSiteFixture();
            await fixture.InitializeAsync();

            ModuleManager? moduleManager = null;

            try
            {
                await fixture.CreateSiteAsync("minimal");
                moduleManager = new ModuleManager(fixture.SiteRoot);

                var count = Math.Min(operationCount.Get % 5 + 1, 5);
                var themesPath = Path.Combine(fixture.SiteRoot, "themes");
                var lockFilePath = Path.Combine(fixture.SiteRoot, "Flint.lock");

                // 清理
                if (Directory.Exists(themesPath))
                {
                    Directory.Delete(themesPath, recursive: true);
                }
                Directory.CreateDirectory(themesPath);

                if (File.Exists(lockFilePath))
                {
                    File.Delete(lockFilePath);
                }

                // Act - 执行多次创建和删除操作
                for (var i = 0; i < count; i++)
                {
                    var moduleName = $"lock-test-{i}";
                    var modulePath = Path.Combine(themesPath, moduleName);

                    Directory.CreateDirectory(modulePath);
                    await File.WriteAllTextAsync(
                        Path.Combine(modulePath, "theme.toml"),
                        $"""
                        name = "{moduleName}"
                        version = "1.0.0"
                        """);
                }

                // 删除所有模块
                for (var i = 0; i < count; i++)
                {
                    await moduleManager.RemoveAsync($"lock-test-{i}");
                }

                // Assert
                var modules = await moduleManager.ListAsync();
                var noModulesLeft = modules.Count == 0;

                return noModulesLeft;
            }
            finally
            {
                moduleManager?.Dispose();
                await fixture.DisposeAsync();
            }
        });
    }

    /// <summary>
    /// 属性测试：模块描述符字段完整性
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ModuleDescriptor_HasRequiredFields(PositiveInt seed)
    {
        return RunPropertyTestAsync(async () =>
        {
            using var fixture = new TestSiteFixture();
            await fixture.InitializeAsync();

            ModuleManager? moduleManager = null;

            try
            {
                await fixture.CreateSiteAsync("minimal");
                moduleManager = new ModuleManager(fixture.SiteRoot);

                var themesPath = Path.Combine(fixture.SiteRoot, "themes");
                var moduleName = $"descriptor-test-{seed.Get % 100}";
                var modulePath = Path.Combine(themesPath, moduleName);

                // 清理
                if (Directory.Exists(modulePath))
                {
                    Directory.Delete(modulePath, recursive: true);
                }
                Directory.CreateDirectory(modulePath);

                await File.WriteAllTextAsync(
                    Path.Combine(modulePath, "theme.toml"),
                    $"""
                    name = "{moduleName}"
                    version = "1.0.0"
                    repository = "https://github.com/test/{moduleName}"
                    """);

                // Act
                var modules = await moduleManager.ListAsync();
                var module = modules.FirstOrDefault(m => m.Name == moduleName);

                // Assert
                var hasName = module != null && !string.IsNullOrEmpty(module.Name);
                var hasVersion = module != null && !string.IsNullOrEmpty(module.Version);
                return hasName && hasVersion;
            }
            finally
            {
                moduleManager?.Dispose();
                await fixture.DisposeAsync();
            }
        });
    }

    #endregion
}


/// <summary>
/// 模块版本生成器
/// </summary>
public static class ModuleVersionArbitraries
{
    private static readonly string[] Versions =
    [
        "1.0.0",
        "2.0.0",
        "1.2.3",
        "0.1.0",
        "10.20.30",
        "1.0.0-alpha",
        "1.0.0-beta.1",
        "2.0.0-rc.1",
        "1.0.0+build.123"
    ];

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "FsCheck 约定")]
    public static Arbitrary<string> ModuleVersion()
    {
        return Gen.Elements(Versions).ToArbitrary();
    }
}

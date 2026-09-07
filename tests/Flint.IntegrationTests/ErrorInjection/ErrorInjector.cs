// Flint 静态站点生成器
// 错误注入工具
// 提供各种错误注入功能用于测试错误处理逻辑

namespace Flint.IntegrationTests.ErrorInjection;

/// <summary>
/// 错误注入类型
/// </summary>
public enum ErrorInjectionType
{
    /// <summary>
    /// 文件系统错误
    /// </summary>
    FileSystem,

    /// <summary>
    /// 数据损坏
    /// </summary>
    DataCorruption,

    /// <summary>
    /// 权限错误
    /// </summary>
    Permission,

    /// <summary>
    /// 编码错误
    /// </summary>
    Encoding
}

/// <summary>
/// 错误注入结果
/// </summary>
public sealed record ErrorInjectionResult
{
    /// <summary>
    /// 是否成功注入
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// 注入类型
    /// </summary>
    public ErrorInjectionType Type { get; init; }

    /// <summary>
    /// 目标路径
    /// </summary>
    public string TargetPath { get; init; } = "";

    /// <summary>
    /// 错误消息（如果注入失败）
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 原始内容（用于恢复）
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "用于存储原始文件内容以便恢复")]
    public byte[]? OriginalContent { get; init; }
}


/// <summary>
/// 错误注入工具
/// 提供各种错误注入功能用于测试错误处理逻辑
/// </summary>
public sealed class ErrorInjector : IDisposable
{
    private readonly List<ErrorInjectionResult> _injections = [];
    private bool _disposed;

    /// <summary>
    /// 已注入的错误列表
    /// </summary>
    public IReadOnlyList<ErrorInjectionResult> Injections => _injections;

    #region 文件系统错误注入

    /// <summary>
    /// 创建无效的 UTF-8 文件
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>注入结果</returns>
    public async Task<ErrorInjectionResult> InjectInvalidUtf8Async(string filePath)
    {
        try
        {
            // 保存原始内容
            byte[]? originalContent = null;
            if (File.Exists(filePath))
            {
                originalContent = await File.ReadAllBytesAsync(filePath);
            }

            // 创建包含无效 UTF-8 序列的内容
            var invalidUtf8 = new byte[]
            {
                0x48, 0x65, 0x6C, 0x6C, 0x6F, // "Hello"
                0x20,                          // 空格
                0xFF, 0xFE,                    // 无效的 UTF-8 序列
                0x57, 0x6F, 0x72, 0x6C, 0x64  // "World"
            };

            await File.WriteAllBytesAsync(filePath, invalidUtf8);

            var result = new ErrorInjectionResult
            {
                Success = true,
                Type = ErrorInjectionType.Encoding,
                TargetPath = filePath,
                OriginalContent = originalContent
            };
            _injections.Add(result);
            return result;
        }
        catch (Exception ex)
        {
            return new ErrorInjectionResult
            {
                Success = false,
                Type = ErrorInjectionType.Encoding,
                TargetPath = filePath,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// 创建截断的文件
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="truncateBytes">截断的字节数（从末尾）</param>
    /// <returns>注入结果</returns>
    public async Task<ErrorInjectionResult> InjectTruncatedFileAsync(string filePath, int truncateBytes = 10)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return new ErrorInjectionResult
                {
                    Success = false,
                    Type = ErrorInjectionType.DataCorruption,
                    TargetPath = filePath,
                    ErrorMessage = "文件不存在"
                };
            }

            var originalContent = await File.ReadAllBytesAsync(filePath);

            if (originalContent.Length <= truncateBytes)
            {
                // 文件太小，清空它
                await File.WriteAllBytesAsync(filePath, []);
            }
            else
            {
                var truncatedContent = originalContent[..^truncateBytes];
                await File.WriteAllBytesAsync(filePath, truncatedContent);
            }

            var result = new ErrorInjectionResult
            {
                Success = true,
                Type = ErrorInjectionType.DataCorruption,
                TargetPath = filePath,
                OriginalContent = originalContent
            };
            _injections.Add(result);
            return result;
        }
        catch (Exception ex)
        {
            return new ErrorInjectionResult
            {
                Success = false,
                Type = ErrorInjectionType.DataCorruption,
                TargetPath = filePath,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// 创建损坏的 JSON 文件
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>注入结果</returns>
    public async Task<ErrorInjectionResult> InjectCorruptedJsonAsync(string filePath)
    {
        try
        {
            byte[]? originalContent = null;
            if (File.Exists(filePath))
            {
                originalContent = await File.ReadAllBytesAsync(filePath);
            }

            // 创建无效的 JSON
            var corruptedJson = "{ \"title\": \"Test\", \"invalid\": }";
            await File.WriteAllTextAsync(filePath, corruptedJson);

            var result = new ErrorInjectionResult
            {
                Success = true,
                Type = ErrorInjectionType.DataCorruption,
                TargetPath = filePath,
                OriginalContent = originalContent
            };
            _injections.Add(result);
            return result;
        }
        catch (Exception ex)
        {
            return new ErrorInjectionResult
            {
                Success = false,
                Type = ErrorInjectionType.DataCorruption,
                TargetPath = filePath,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// 创建损坏的 YAML 文件
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>注入结果</returns>
    public async Task<ErrorInjectionResult> InjectCorruptedYamlAsync(string filePath)
    {
        try
        {
            byte[]? originalContent = null;
            if (File.Exists(filePath))
            {
                originalContent = await File.ReadAllBytesAsync(filePath);
            }

            // 创建无效的 YAML（缩进错误）
            var corruptedYaml = """
                title: Test
                  invalid: indentation
                    nested:
                  wrong: level
                """;
            await File.WriteAllTextAsync(filePath, corruptedYaml);

            var result = new ErrorInjectionResult
            {
                Success = true,
                Type = ErrorInjectionType.DataCorruption,
                TargetPath = filePath,
                OriginalContent = originalContent
            };
            _injections.Add(result);
            return result;
        }
        catch (Exception ex)
        {
            return new ErrorInjectionResult
            {
                Success = false,
                Type = ErrorInjectionType.DataCorruption,
                TargetPath = filePath,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// 创建损坏的 TOML 文件
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>注入结果</returns>
    public async Task<ErrorInjectionResult> InjectCorruptedTomlAsync(string filePath)
    {
        try
        {
            byte[]? originalContent = null;
            if (File.Exists(filePath))
            {
                originalContent = await File.ReadAllBytesAsync(filePath);
            }

            // 创建无效的 TOML
            var corruptedToml = """
                title = "Test"
                invalid = 
                [section
                key = "missing bracket"
                """;
            await File.WriteAllTextAsync(filePath, corruptedToml);

            var result = new ErrorInjectionResult
            {
                Success = true,
                Type = ErrorInjectionType.DataCorruption,
                TargetPath = filePath,
                OriginalContent = originalContent
            };
            _injections.Add(result);
            return result;
        }
        catch (Exception ex)
        {
            return new ErrorInjectionResult
            {
                Success = false,
                Type = ErrorInjectionType.DataCorruption,
                TargetPath = filePath,
                ErrorMessage = ex.Message
            };
        }
    }

    #endregion


    #region 内容错误注入

    /// <summary>
    /// 创建无效的 Front Matter
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>注入结果</returns>
    public async Task<ErrorInjectionResult> InjectInvalidFrontMatterAsync(string filePath)
    {
        try
        {
            byte[]? originalContent = null;
            if (File.Exists(filePath))
            {
                originalContent = await File.ReadAllBytesAsync(filePath);
            }

            // 创建无效的 Front Matter（未闭合）
            var invalidContent = """
                ---
                title: "Test"
                date: invalid-date-format
                
                This is content without closing front matter.
                """;
            await File.WriteAllTextAsync(filePath, invalidContent);

            var result = new ErrorInjectionResult
            {
                Success = true,
                Type = ErrorInjectionType.DataCorruption,
                TargetPath = filePath,
                OriginalContent = originalContent
            };
            _injections.Add(result);
            return result;
        }
        catch (Exception ex)
        {
            return new ErrorInjectionResult
            {
                Success = false,
                Type = ErrorInjectionType.DataCorruption,
                TargetPath = filePath,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// 创建无效的模板语法
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>注入结果</returns>
    public async Task<ErrorInjectionResult> InjectInvalidTemplateSyntaxAsync(string filePath)
    {
        try
        {
            byte[]? originalContent = null;
            if (File.Exists(filePath))
            {
                originalContent = await File.ReadAllBytesAsync(filePath);
            }

            // 创建无效的模板语法
            var invalidTemplate = """
                <!DOCTYPE html>
                <html>
                <head>
                    <title>{{ .Title }}</title>
                </head>
                <body>
                    {{ if .Content }}
                    <div>{{ .Content }}</div>
                    <!-- 缺少 end -->
                    {{ range .Pages
                    <!-- 缺少闭合括号 -->
                    <p>{{ .Title }}</p>
                    {{ end }}
                </body>
                </html>
                """;
            await File.WriteAllTextAsync(filePath, invalidTemplate);

            var result = new ErrorInjectionResult
            {
                Success = true,
                Type = ErrorInjectionType.DataCorruption,
                TargetPath = filePath,
                OriginalContent = originalContent
            };
            _injections.Add(result);
            return result;
        }
        catch (Exception ex)
        {
            return new ErrorInjectionResult
            {
                Success = false,
                Type = ErrorInjectionType.DataCorruption,
                TargetPath = filePath,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// 创建无效的 SCSS 语法
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>注入结果</returns>
    public async Task<ErrorInjectionResult> InjectInvalidScssAsync(string filePath)
    {
        try
        {
            byte[]? originalContent = null;
            if (File.Exists(filePath))
            {
                originalContent = await File.ReadAllBytesAsync(filePath);
            }

            // 创建无效的 SCSS
            var invalidScss = """
                $primary-color: #333
                
                .container {
                    color: $undefined-variable;
                    
                    .nested {
                        /* 缺少闭合括号 */
                        padding: 10px
                    
                @mixin undefined-mixin {
                    display: flex;
                }
                """;
            await File.WriteAllTextAsync(filePath, invalidScss);

            var result = new ErrorInjectionResult
            {
                Success = true,
                Type = ErrorInjectionType.DataCorruption,
                TargetPath = filePath,
                OriginalContent = originalContent
            };
            _injections.Add(result);
            return result;
        }
        catch (Exception ex)
        {
            return new ErrorInjectionResult
            {
                Success = false,
                Type = ErrorInjectionType.DataCorruption,
                TargetPath = filePath,
                ErrorMessage = ex.Message
            };
        }
    }

    #endregion

    #region 文件系统状态注入

    /// <summary>
    /// 创建空文件
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>注入结果</returns>
    public async Task<ErrorInjectionResult> InjectEmptyFileAsync(string filePath)
    {
        try
        {
            byte[]? originalContent = null;
            if (File.Exists(filePath))
            {
                originalContent = await File.ReadAllBytesAsync(filePath);
            }

            await File.WriteAllBytesAsync(filePath, []);

            var result = new ErrorInjectionResult
            {
                Success = true,
                Type = ErrorInjectionType.DataCorruption,
                TargetPath = filePath,
                OriginalContent = originalContent
            };
            _injections.Add(result);
            return result;
        }
        catch (Exception ex)
        {
            return new ErrorInjectionResult
            {
                Success = false,
                Type = ErrorInjectionType.DataCorruption,
                TargetPath = filePath,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// 创建只读文件
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>注入结果</returns>
    public ErrorInjectionResult InjectReadOnlyFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return new ErrorInjectionResult
                {
                    Success = false,
                    Type = ErrorInjectionType.Permission,
                    TargetPath = filePath,
                    ErrorMessage = "文件不存在"
                };
            }

            var fileInfo = new FileInfo(filePath);
            fileInfo.IsReadOnly = true;

            var result = new ErrorInjectionResult
            {
                Success = true,
                Type = ErrorInjectionType.Permission,
                TargetPath = filePath
            };
            _injections.Add(result);
            return result;
        }
        catch (Exception ex)
        {
            return new ErrorInjectionResult
            {
                Success = false,
                Type = ErrorInjectionType.Permission,
                TargetPath = filePath,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// 删除文件（模拟文件丢失）
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>注入结果</returns>
    public async Task<ErrorInjectionResult> InjectMissingFileAsync(string filePath)
    {
        try
        {
            byte[]? originalContent = null;
            if (File.Exists(filePath))
            {
                originalContent = await File.ReadAllBytesAsync(filePath);
                File.Delete(filePath);
            }

            var result = new ErrorInjectionResult
            {
                Success = true,
                Type = ErrorInjectionType.FileSystem,
                TargetPath = filePath,
                OriginalContent = originalContent
            };
            _injections.Add(result);
            return result;
        }
        catch (Exception ex)
        {
            return new ErrorInjectionResult
            {
                Success = false,
                Type = ErrorInjectionType.FileSystem,
                TargetPath = filePath,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// 用目录替换文件
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>注入结果</returns>
    public async Task<ErrorInjectionResult> InjectDirectoryInsteadOfFileAsync(string filePath)
    {
        try
        {
            byte[]? originalContent = null;
            if (File.Exists(filePath))
            {
                originalContent = await File.ReadAllBytesAsync(filePath);
                File.Delete(filePath);
            }

            Directory.CreateDirectory(filePath);

            var result = new ErrorInjectionResult
            {
                Success = true,
                Type = ErrorInjectionType.FileSystem,
                TargetPath = filePath,
                OriginalContent = originalContent
            };
            _injections.Add(result);
            return result;
        }
        catch (Exception ex)
        {
            return new ErrorInjectionResult
            {
                Success = false,
                Type = ErrorInjectionType.FileSystem,
                TargetPath = filePath,
                ErrorMessage = ex.Message
            };
        }
    }

    #endregion


    #region 恢复方法

    /// <summary>
    /// 恢复指定的注入
    /// </summary>
    /// <param name="injection">注入结果</param>
    /// <returns>是否成功恢复</returns>
    public async Task<bool> RestoreAsync(ErrorInjectionResult injection)
    {
        try
        {
            if (injection.Type == ErrorInjectionType.Permission)
            {
                // 恢复文件权限
                if (File.Exists(injection.TargetPath))
                {
                    var fileInfo = new FileInfo(injection.TargetPath);
                    fileInfo.IsReadOnly = false;
                }
                return true;
            }

            if (injection.Type == ErrorInjectionType.FileSystem && Directory.Exists(injection.TargetPath))
            {
                // 如果是目录，先删除
                Directory.Delete(injection.TargetPath, recursive: true);
            }

            if (injection.OriginalContent != null)
            {
                // 确保目录存在
                var directory = Path.GetDirectoryName(injection.TargetPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllBytesAsync(injection.TargetPath, injection.OriginalContent);
            }
            else if (File.Exists(injection.TargetPath))
            {
                // 如果没有原始内容，删除文件
                File.Delete(injection.TargetPath);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 恢复所有注入
    /// </summary>
    /// <returns>成功恢复的数量</returns>
    public async Task<int> RestoreAllAsync()
    {
        var successCount = 0;

        // 反向恢复，以处理依赖关系
        for (var i = _injections.Count - 1; i >= 0; i--)
        {
            if (await RestoreAsync(_injections[i]))
            {
                successCount++;
            }
        }

        _injections.Clear();
        return successCount;
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 创建随机损坏的二进制数据
    /// </summary>
    /// <param name="originalData">原始数据</param>
    /// <param name="corruptionRate">损坏率（0-1）</param>
    /// <param name="seed">随机种子</param>
    /// <returns>损坏的数据</returns>
    public static byte[] CorruptBinaryData(byte[] originalData, double corruptionRate = 0.1, int? seed = null)
    {
        var random = seed.HasValue ? new Random(seed.Value) : new Random();
        var result = new byte[originalData.Length];
        Array.Copy(originalData, result, originalData.Length);

        var bytesToCorrupt = (int)(originalData.Length * corruptionRate);
        for (var i = 0; i < bytesToCorrupt; i++)
        {
            var index = random.Next(result.Length);
            result[index] = (byte)random.Next(256);
        }

        return result;
    }

    /// <summary>
    /// 创建随机损坏的文本数据
    /// </summary>
    /// <param name="originalText">原始文本</param>
    /// <param name="corruptionRate">损坏率（0-1）</param>
    /// <param name="seed">随机种子</param>
    /// <returns>损坏的文本</returns>
    public static string CorruptTextData(string originalText, double corruptionRate = 0.1, int? seed = null)
    {
        var random = seed.HasValue ? new Random(seed.Value) : new Random();
        var chars = originalText.ToCharArray();

        var charsToCorrupt = (int)(chars.Length * corruptionRate);
        for (var i = 0; i < charsToCorrupt; i++)
        {
            var index = random.Next(chars.Length);
            chars[index] = (char)random.Next(32, 127); // 可打印 ASCII 字符
        }

        return new string(chars);
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// 释放资源并恢复所有注入
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        // 同步恢复所有注入
        foreach (var injection in _injections.AsEnumerable().Reverse())
        {
            try
            {
                RestoreAsync(injection).GetAwaiter().GetResult();
            }
            catch
            {
                // 忽略恢复错误
            }
        }

        _injections.Clear();
        GC.SuppressFinalize(this);
    }

    #endregion
}

// Flint 静态站点生成器
// 资源文件数据类型

namespace Flint.Core.Models;

/// <summary>
/// 资源文件的只读记录结构
/// </summary>
public readonly record struct AssetFile
{
    /// <summary>
    /// 源文件路径
    /// </summary>
    public required string SourcePath { get; init; }

    /// <summary>
    /// 媒体类型（MIME 类型）
    /// </summary>
    public required string MediaType { get; init; }

    /// <summary>
    /// 文件内容
    /// </summary>
    public required ReadOnlyMemory<byte> Content { get; init; }

    /// <summary>
    /// 最后修改时间
    /// </summary>
    public required DateTimeOffset ModifiedTime { get; init; }

    /// <summary>
    /// 内容长度（字节数）
    /// </summary>
    public int Length => Content.Length;

    /// <summary>
    /// 文件扩展名（小写）
    /// </summary>
    public string Extension => Path.GetExtension(SourcePath).ToLowerInvariant();

    /// <summary>
    /// 文件名（不含路径）
    /// </summary>
    public string FileName => Path.GetFileName(SourcePath);

    /// <summary>
    /// 获取资源类型
    /// </summary>
    public AssetType Type => GetAssetType(Extension);

    /// <summary>
    /// 根据扩展名获取资源类型
    /// </summary>
    private static AssetType GetAssetType(string extension) => extension switch
    {
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".avif" or ".svg" or ".ico" => AssetType.Image,
        ".css" or ".scss" or ".sass" or ".less" => AssetType.Style,
        ".js" or ".mjs" or ".ts" or ".tsx" or ".jsx" => AssetType.Script,
        ".woff" or ".woff2" or ".ttf" or ".otf" or ".eot" => AssetType.Font,
        ".mp4" or ".webm" or ".ogg" or ".mov" => AssetType.Video,
        ".mp3" or ".wav" or ".flac" or ".aac" => AssetType.Audio,
        ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" => AssetType.Document,
        ".json" or ".xml" or ".yaml" or ".yml" or ".toml" => AssetType.Data,
        _ => AssetType.Other
    };
}

/// <summary>
/// 资源类型枚举
/// </summary>
public enum AssetType
{
    /// <summary>
    /// 图片资源
    /// </summary>
    Image,

    /// <summary>
    /// 样式资源（CSS、Sass 等）
    /// </summary>
    Style,

    /// <summary>
    /// 脚本资源（JavaScript、TypeScript 等）
    /// </summary>
    Script,

    /// <summary>
    /// 字体资源
    /// </summary>
    Font,

    /// <summary>
    /// 视频资源
    /// </summary>
    Video,

    /// <summary>
    /// 音频资源
    /// </summary>
    Audio,

    /// <summary>
    /// 文档资源
    /// </summary>
    Document,

    /// <summary>
    /// 数据文件
    /// </summary>
    Data,

    /// <summary>
    /// 其他资源
    /// </summary>
    Other
}

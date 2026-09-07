// Flint 静态站点生成器
// 资源哈希属性测试
// **Feature: Flint, Property 7: 资源哈希确定性**

using System.Text;
using Flint.Core.Abstractions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Flint.Core.Tests.Abstractions;

/// <summary>
/// 资源哈希属性测试
/// **Validates: Requirements 4.7, 4.8**
/// 
/// Property 7: 资源哈希确定性
/// 对于任意资源文件，相同的内容应该产生相同的内容哈希和 SRI 哈希。
/// 不同的内容应该产生不同的哈希（碰撞概率可忽略）。
/// </summary>
public class ContentHasherPropertyTests
{
    #region Property 7.1: 内容哈希确定性

    /// <summary>
    /// **Property 7: 资源哈希确定性 - 相同内容产生相同哈希**
    /// 相同的字节内容应该始终产生相同的 SHA256 哈希
    /// **Validates: Requirements 4.7**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property SameContent_ProducesSameHash()
    {
        return Prop.ForAll<byte[]>(content =>
        {
            if (content == null)
                return true;

            // Act - 计算两次哈希
            var hash1 = ContentHasher.ComputeHash(content.AsSpan());
            var hash2 = ContentHasher.ComputeHash(content.AsSpan());

            // Assert - 哈希应该相同
            return hash1 == hash2;
        });
    }

    /// <summary>
    /// **Property 7: 资源哈希确定性 - 哈希长度固定**
    /// SHA256 哈希应该始终是 64 个十六进制字符
    /// **Validates: Requirements 4.7**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Hash_HasFixedLength()
    {
        return Prop.ForAll<byte[]>(content =>
        {
            if (content == null)
                return true;

            var hash = ContentHasher.ComputeHash(content.AsSpan());

            // SHA256 = 32 bytes = 64 hex chars
            return hash.Length == 64;
        });
    }


    /// <summary>
    /// **Property 7: 资源哈希确定性 - 哈希是有效的十六进制**
    /// 哈希字符串应该只包含有效的十六进制字符
    /// **Validates: Requirements 4.7**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Hash_IsValidHexadecimal()
    {
        return Prop.ForAll<byte[]>(content =>
        {
            if (content == null)
                return true;

            var hash = ContentHasher.ComputeHash(content.AsSpan());

            // 检查所有字符都是有效的十六进制字符
            return hash.All(c => char.IsAsciiHexDigitLower(c) || char.IsDigit(c));
        });
    }

    #endregion

    #region Property 7.2: SRI 哈希确定性

    /// <summary>
    /// **Property 7: 资源哈希确定性 - 相同内容产生相同 SRI 哈希**
    /// 相同的内容应该产生相同的 SRI 完整性哈希
    /// **Validates: Requirements 4.8**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property SameContent_ProducesSameSriHash()
    {
        return Prop.ForAll<byte[]>(content =>
        {
            if (content == null)
                return true;

            var sri1 = ContentHasher.ComputeSriHash(content.AsSpan());
            var sri2 = ContentHasher.ComputeSriHash(content.AsSpan());

            return sri1 == sri2;
        });
    }

    /// <summary>
    /// **Property 7: 资源哈希确定性 - SRI 哈希格式正确**
    /// SRI 哈希应该以 "sha256-" 开头，后跟 Base64 编码
    /// **Validates: Requirements 4.8**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property SriHash_HasCorrectFormat()
    {
        return Prop.ForAll<byte[]>(content =>
        {
            if (content == null)
                return true;

            var sri = ContentHasher.ComputeSriHash(content.AsSpan());

            // 检查格式
            if (!sri.StartsWith("sha256-", StringComparison.Ordinal))
                return false;

            // 检查 Base64 部分
            var base64Part = sri["sha256-".Length..];
            try
            {
                var decoded = Convert.FromBase64String(base64Part);
                // SHA256 = 32 bytes
                return decoded.Length == 32;
            }
            catch (FormatException)
            {
                return false;
            }
        });
    }

    #endregion

    #region Property 7.3: 不同内容产生不同哈希

    /// <summary>
    /// **Property 7: 资源哈希确定性 - 不同内容产生不同哈希**
    /// 不同的内容应该产生不同的哈希（碰撞概率可忽略）
    /// **Validates: Requirements 4.7**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property DifferentContent_ProducesDifferentHash()
    {
        return Prop.ForAll<byte[], byte[]>((content1, content2) =>
        {
            if (content1 == null || content2 == null)
                return true;

            // 如果内容相同，跳过
            if (content1.SequenceEqual(content2))
                return true;

            var hash1 = ContentHasher.ComputeHash(content1.AsSpan());
            var hash2 = ContentHasher.ComputeHash(content2.AsSpan());

            // 不同内容应该产生不同哈希
            return hash1 != hash2;
        });
    }

    /// <summary>
    /// **Property 7: 资源哈希确定性 - 单字节差异产生不同哈希**
    /// 即使只有一个字节不同，哈希也应该不同
    /// **Validates: Requirements 4.7**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property SingleByteDifference_ProducesDifferentHash()
    {
        var arbNonEmptyBytes = ArbMap.Default.ArbFor<NonEmptyArray<byte>>();
        var arbByte = Gen.Choose(0, 255).ToArbitrary();

        return Prop.ForAll(arbNonEmptyBytes, arbByte, (content, newByte) =>
        {
            var original = content.Get;
            if (original.Length == 0)
                return true;

            // 创建修改后的副本
            var modified = (byte[])original.Clone();
            var index = original.Length / 2;

            // 确保修改后的字节不同
            if (modified[index] == (byte)newByte)
            {
                modified[index] = (byte)((newByte + 1) % 256);
            }
            else
            {
                modified[index] = (byte)newByte;
            }

            var hash1 = ContentHasher.ComputeHash(original.AsSpan());
            var hash2 = ContentHasher.ComputeHash(modified.AsSpan());

            return hash1 != hash2;
        });
    }

    #endregion


    #region Property 7.4: 短哈希确定性

    /// <summary>
    /// **Property 7: 资源哈希确定性 - 短哈希确定性**
    /// 相同内容应该产生相同的短哈希
    /// **Validates: Requirements 4.7**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ShortHash_IsDeterministic()
    {
        var arbBytes = ArbMap.Default.ArbFor<byte[]>();
        var arbLength = Gen.Choose(1, 64).ToArbitrary();

        return Prop.ForAll(arbBytes, arbLength, (content, length) =>
        {
            if (content == null)
                return true;

            var shortHash1 = ContentHasher.ComputeShortHash(content.AsSpan(), length);
            var shortHash2 = ContentHasher.ComputeShortHash(content.AsSpan(), length);

            return shortHash1 == shortHash2 && shortHash1.Length == Math.Min(length, 64);
        });
    }

    /// <summary>
    /// **Property 7: 资源哈希确定性 - 短哈希是完整哈希的前缀**
    /// 短哈希应该是完整哈希的前缀
    /// **Validates: Requirements 4.7**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ShortHash_IsPrefixOfFullHash()
    {
        var arbBytes = ArbMap.Default.ArbFor<byte[]>();
        var arbLength = Gen.Choose(1, 64).ToArbitrary();

        return Prop.ForAll(arbBytes, arbLength, (content, length) =>
        {
            if (content == null)
                return true;

            var fullHash = ContentHasher.ComputeHash(content.AsSpan());
            var shortHash = ContentHasher.ComputeShortHash(content.AsSpan(), length);

            return fullHash.StartsWith(shortHash, StringComparison.Ordinal);
        });
    }

    #endregion

    #region Property 7.5: 指纹路径确定性

    /// <summary>
    /// **Property 7: 资源哈希确定性 - 指纹路径确定性**
    /// 相同内容和路径应该产生相同的指纹路径
    /// **Validates: Requirements 4.7**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property FingerprintedPath_IsDeterministic()
    {
        return Prop.ForAll<byte[]>(content =>
        {
            if (content == null)
                return true;

            var path = "assets/style.css";
            var fingerprinted1 = ContentHasher.GenerateFingerprintedPath(path, content.AsSpan());
            var fingerprinted2 = ContentHasher.GenerateFingerprintedPath(path, content.AsSpan());

            return fingerprinted1 == fingerprinted2;
        });
    }

    /// <summary>
    /// **Property 7: 资源哈希确定性 - 指纹路径保留扩展名**
    /// 指纹路径应该保留原始文件的扩展名
    /// **Validates: Requirements 4.7**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property FingerprintedPath_PreservesExtension()
    {
        var arbBytes = ArbMap.Default.ArbFor<byte[]>();
        var arbExtension = Gen.Elements(".css", ".js", ".png", ".jpg", ".svg").ToArbitrary();

        return Prop.ForAll(arbBytes, arbExtension, (content, extension) =>
        {
            if (content == null)
                return true;

            var path = $"assets/file{extension}";
            var fingerprinted = ContentHasher.GenerateFingerprintedPath(path, content.AsSpan());

            return fingerprinted.EndsWith(extension, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// **Property 7: 资源哈希确定性 - 不同内容产生不同指纹路径**
    /// 不同内容应该产生不同的指纹路径
    /// **Validates: Requirements 4.7**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property DifferentContent_ProducesDifferentFingerprintedPath()
    {
        return Prop.ForAll<byte[], byte[]>((content1, content2) =>
        {
            if (content1 == null || content2 == null)
                return true;
            if (content1.SequenceEqual(content2))
                return true;

            var path = "assets/style.css";
            var fingerprinted1 = ContentHasher.GenerateFingerprintedPath(path, content1.AsSpan());
            var fingerprinted2 = ContentHasher.GenerateFingerprintedPath(path, content2.AsSpan());

            return fingerprinted1 != fingerprinted2;
        });
    }

    #endregion

    #region Property 7.6: 字符串哈希一致性

    /// <summary>
    /// **Property 7: 资源哈希确定性 - 字符串和字节哈希一致**
    /// 字符串的 UTF-8 编码和对应字节数组应该产生相同哈希
    /// **Validates: Requirements 4.7**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property StringAndBytes_ProduceSameHash()
    {
        return Prop.ForAll<NonEmptyString>(str =>
        {
            var stringContent = str.Get;
            var byteContent = Encoding.UTF8.GetBytes(stringContent);

            var hashFromString = ContentHasher.ComputeHash(stringContent);
            var hashFromBytes = ContentHasher.ComputeHash(byteContent.AsSpan());

            return hashFromString == hashFromBytes;
        });
    }

    #endregion
}

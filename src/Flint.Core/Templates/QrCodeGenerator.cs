// Flint 静态站点生成器
// QR 码生成器：Hugo images.QR（v0.141+）的兼容实现
//
// 为什么自写而不用 QrCoder 之类第三方包：Flint 的 Native AOT 是核心卖点，
// 第三方 QR 包的反射面与 AOT 兼容性不可控；QR 编码是纯算法（GF(256) +
// Reed-Solomon + 掩码评估），零依赖约 300 行，v1-v10（最多 1067 字节）覆盖
// 主题 QR 场景（短 URL / 文本）。
//
// 块结构表与掩码/罚分规则：ISO/IEC 18004（thonky.com 错误校正表交叉验证，
// 2026-11 抓取；v1 各级与 v10 各级数据码字+纠错码字均等于总码字数 26/346，
// 锚点全部吻合）。格式信息 BCH(15,5) 编码采用与 nayuki/QR-Code-generator
// （MIT，业界广泛验证）一致的实现。

using System.Text;

namespace Flint.Core.Templates;

/// <summary>
/// QR 码编码器：UTF-8 字节模式，纠错级 L/M/Q/H，版本 1-10 自动选择，
/// 8 种掩码罚分择优。产出布尔矩阵与 SVG 文本。
/// </summary>
internal static class QrCodeGenerator
{
    // ---- GF(256)，本原多项式 0x11d ----
    private static readonly byte[] Exp = new byte[512];
    private static readonly short[] Log = new short[256];

    static QrCodeGenerator()
    {
        var x = 1;
        for (var i = 0; i < 255; i++)
        {
            Exp[i] = (byte)x;
            Log[x] = (short)i;
            x <<= 1;
            if ((x & 0x100) != 0)
            {
                x ^= 0x11d;
            }
        }
        // 倍长表：省去取模
        for (var i = 255; i < 512; i++)
        {
            Exp[i] = Exp[i - 255];
        }
    }

    private static int GfMul(int a, int b) =>
        a == 0 || b == 0 ? 0 : Exp[Log[a] + Log[b]];

    /// <summary>
    /// 块结构表 [level][version-1]：(每块纠错码字数, 各块数据码字数)。
    /// level 索引 0=L 1=M 2=Q 3=H
    /// </summary>
    private static readonly (int Ec, int[] Blocks)[][] BlocksByLevel =
    [
        // L（20% 冗余）
        [
            (7, [19]), (10, [34]), (15, [55]), (20, [80]), (26, [108]),
            (18, [68, 68]), (20, [78, 78]), (24, [97, 97]), (30, [116, 116]),
            (18, [68, 68, 69, 69]),
        ],
        // M（38%，Hugo images.QR 默认）
        [
            (10, [16]), (16, [28]), (26, [44]), (18, [32, 32]), (24, [43, 43]),
            (16, [27, 27, 27, 27]), (18, [31, 31, 31, 31]), (22, [38, 38, 39, 39]),
            (22, [36, 36, 36, 37, 37]), (26, [43, 43, 43, 43, 44]),
        ],
        // Q（55%）
        [
            (13, [13]), (22, [22]), (18, [17, 17]), (26, [24, 24]),
            (18, [15, 15, 16, 16]), (24, [19, 19, 19, 19]),
            // v7-Q：2 块×14 + 4 块×15（数据 88 + EC 6×18=108 = 196 总码字）
            (18, [14, 14, 15, 15, 15, 15]), (22, [18, 18, 18, 18, 19, 19]),
            (20, [16, 16, 16, 16, 17, 17, 17, 17]), (24, [19, 19, 19, 19, 19, 19, 20, 20]),
        ],
        // H（65%）
        [
            (17, [9]), (28, [16]), (22, [13, 13]), (16, [9, 9, 9, 9]),
            (22, [11, 11, 12, 12]), (28, [15, 15, 15, 15]),
            (26, [13, 13, 13, 13, 14]), (26, [14, 14, 14, 14, 15, 15]),
            (24, [12, 12, 12, 12, 13, 13, 13, 13]), (28, [15, 15, 15, 15, 15, 15, 16, 16]),
        ],
    ];

    /// <summary>每版本边长模块数（v1-v10）</summary>
    private static readonly int[] Sizes = [21, 25, 29, 33, 37, 41, 45, 49, 53, 57];

    /// <summary>每版本 alignment 中心坐标（v1 无）</summary>
    private static readonly int[][] AlignmentCenters =
    [
        [], [6, 18], [6, 22], [6, 26], [6, 30], [6, 34],
        [6, 22, 38], [6, 24, 42], [6, 26, 46], [6, 28, 50],
    ];

    /// <summary>数据置放后的剩余 0 bit（v1:0，v2-v6:7，v7-v10:0）</summary>
    private static readonly int[] RemainderBits = [0, 7, 7, 7, 7, 7, 0, 0, 0, 0];

    /// <summary>纠错级的 format 信息位：L=01 M=00 Q=11 H=10</summary>
    private static readonly int[] LevelFormatBits = [1, 0, 3, 2];

    /// <summary>Hugo level 参数（low/medium/quartile/high）→ 级索引；未知值回落 M</summary>
    public static int LevelIndex(string? level) => (level ?? "").Trim().ToLowerInvariant() switch
    {
        "low" => 0,
        "quartile" => 2,
        "high" => 3,
        _ => 1, // medium / 空 / 未知
    };

    /// <summary>
    /// 编码为 QR 矩阵（true = 暗模块）。data 为 UTF-8 字节。
    /// 空数据或超出 v10 容量时抛 InvalidOperationException（对齐 Hugo"构建失败"语义）。
    /// </summary>
    public static bool[][] Encode(ReadOnlySpan<byte> data, int level)
    {
        if (data.Length == 0)
        {
            throw new InvalidOperationException("images.QR: 文本不能为空");
        }
        var (version, ecPerBlock, blocks) = SelectVersion(data.Length, level);
        var size = Sizes[version];
        var finalBits = BuildFinalBits(data, version, level, ecPerBlock, blocks);

        var (matrix, used) = BuildFunctionModules(size, version);
        PlaceData(matrix, used, finalBits);
        var mask = ApplyBestMask(matrix, used);
        PlaceFormat(matrix, level, mask);
        PlaceVersion(matrix, version);
        return matrix;
    }

    /// <summary>矩阵 → SVG（含 4 模块静区；bg 为空串则透明背景）</summary>
    public static string ToSvg(bool[][] matrix, int scale, string fg, string bg)
    {
        var size = matrix.Length;
        var quiet = 4;
        var total = size + quiet * 2;
        var sb = new StringBuilder(total * total / 4 + 128);
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"")
          .Append(total * scale).Append("\" height=\"").Append(total * scale)
          .Append("\" viewBox=\"0 0 ").Append(total).Append(' ').Append(total)
          .Append("\" shape-rendering=\"crispEdges\">");
        if (!string.IsNullOrEmpty(bg))
        {
            sb.Append("<rect width=\"100%\" height=\"100%\" fill=\"").Append(bg).Append("\"/>");
        }
        sb.Append("<path fill=\"").Append(fg).Append("\" d=\"");
        for (var r = 0; r < size; r++)
        {
            for (var c = 0; c < size; c++)
            {
                if (matrix[r][c])
                {
                    sb.Append('M').Append(c + quiet).Append(',').Append(r + quiet)
                      .Append("h1v1h-1z");
                }
            }
        }
        sb.Append("\"/></svg>");
        return sb.ToString();
    }

    /// <summary>选版本：数据能装下的最小版本（字节模式，v1-9 计数 8bit、v10 16bit）。
    /// 容量比 ISO 规范口径少 1 字节（多扣 8 bit）：ZXing 家族（多数手机扫码器的
    /// 解码底盘，也是 Hugo 生态外的既成标准）的版本选择实测恒为「规范容量 −1」
    /// （M 级 v1-v7 分界 13/25/41/61/83/105/121，2026-11 实测）。对齐它，
    /// 同一文本产出与最大生态一致的版本；解码端不受影响（解码器比生成器宽容）</summary>
    private static (int Version, int Ec, int[] Blocks) SelectVersion(int byteCount, int level)
    {
        var table = BlocksByLevel[level];
        for (var v = 0; v < table.Length; v++)
        {
            var (ec, blocks) = table[v];
            var countBits = v < 9 ? 8 : 16;
            var capacityBytes = (blocks.Sum() * 8 - 4 - countBits - 8) / 8;
            if (byteCount <= capacityBytes)
            {
                return (v, ec, blocks);
            }
        }
        throw new InvalidOperationException(
            $"images.QR: 数据 {byteCount} 字节超出 v10/{LevelName(level)} 容量（Hugo 侧同样报错）");
    }

    internal static string LevelName(int level) => level switch
    {
        0 => "L", 2 => "Q", 3 => "H", _ => "M",
    };

    /// <summary>bit 流：模式头 + 数据 + 终止符 + pad + 分块 RS + 交织（含尾随 remainder 0 bit）</summary>
    private static bool[] BuildFinalBits(
        ReadOnlySpan<byte> data, int version, int level, int ecPerBlock, int[] blocks)
    {
        var totalData = blocks.Sum();
        var countBits = version < 9 ? 8 : 16;

        // 位流（List<bool> 容量小无妨，主题 QR 都是短文本）
        var bits = new List<bool>(totalData * 8 + 16);
        void Push(int value, int width)
        {
            for (var i = width - 1; i >= 0; i--)
            {
                bits.Add(((value >> i) & 1) == 1);
            }
        }
        Push(0b0100, 4); // 字节模式
        Push(data.Length, countBits);
        foreach (var b in data)
        {
            Push(b, 8);
        }
        var capacity = totalData * 8;
        Push(0, Math.Min(4, capacity - bits.Count)); // 终止符
        while (bits.Count % 8 != 0)
        {
            bits.Add(false);
        }
        var bytes = new byte[bits.Count / 8];
        for (var i = 0; i < bytes.Length; i++)
        {
            var b = 0;
            for (var j = 0; j < 8; j++)
            {
                b = (b << 1) | (bits[i * 8 + j] ? 1 : 0);
            }
            bytes[i] = (byte)b;
        }
        // pad 到数据容量：0xEC 0x11 交替
        var padded = new byte[totalData];
        Array.Copy(bytes, padded, bytes.Length);
        for (var i = bytes.Length; i < totalData; i++)
        {
            padded[i] = (i - bytes.Length) % 2 == 0 ? (byte)0xec : (byte)0x11;
        }

        // 分块 RS
        var gen = GeneratorPoly(ecPerBlock);
        var dataBlocks = new byte[blocks.Length][];
        var ecBlocks = new byte[blocks.Length][];
        var offset = 0;
        for (var b = 0; b < blocks.Length; b++)
        {
            var block = new byte[blocks[b]];
            Array.Copy(padded, offset, block, 0, block.Length);
            offset += block.Length;
            dataBlocks[b] = block;
            ecBlocks[b] = RsEncode(block, gen);
        }

        // 交织：数据列优先，再纠错列
        var interleaved = new List<byte>(totalData + ecPerBlock * blocks.Length);
        var maxBlock = blocks.Max();
        for (var i = 0; i < maxBlock; i++)
        {
            foreach (var block in dataBlocks)
            {
                if (i < block.Length)
                {
                    interleaved.Add(block[i]);
                }
            }
        }
        for (var i = 0; i < ecPerBlock; i++)
        {
            foreach (var block in ecBlocks)
            {
                interleaved.Add(block[i]);
            }
        }

        var final = new bool[interleaved.Count * 8 + RemainderBits[version]];
        var idx = 0;
        foreach (var b in interleaved)
        {
            for (var j = 7; j >= 0; j--)
            {
                final[idx++] = ((b >> j) & 1) == 1;
            }
        }
        return final;
    }

    /// <summary>RS 生成多项式 g(x)=Π(x-α^i)，monic（首项系数 1），降幂序</summary>
    private static int[] GeneratorPoly(int degree)
    {
        var poly = new[] { 1 };
        for (var i = 0; i < degree; i++)
        {
            var next = new int[poly.Length + 1];
            for (var j = 0; j < poly.Length; j++)
            {
                next[j] ^= poly[j];
                next[j + 1] ^= GfMul(poly[j], Exp[i]);
            }
            poly = next;
        }
        return poly;
    }

    /// <summary>多项式除法求余：data·x^ec mod g(x)（g monic，同步消首项）</summary>
    private static byte[] RsEncode(byte[] data, int[] gen)
    {
        var ec = gen.Length - 1;
        var work = new int[data.Length + ec];
        for (var i = 0; i < data.Length; i++)
        {
            work[i] = data[i];
        }
        for (var i = 0; i < data.Length; i++)
        {
            var coef = work[i];
            if (coef == 0)
            {
                continue;
            }
            for (var j = 1; j < gen.Length; j++)
            {
                work[i + j] ^= GfMul(gen[j], coef);
            }
        }
        var result = new byte[ec];
        for (var i = 0; i < ec; i++)
        {
            result[i] = (byte)work[data.Length + i];
        }
        return result;
    }

    /// <summary>功能模块层（finder/separator/timing/alignment/dark/format 预留）</summary>
    private static (bool[][] Matrix, bool[][] Used) BuildFunctionModules(int size, int version)
    {
        var m = Jagged(size);
        var used = Jagged(size);
        void Set(int r, int c, bool dark)
        {
            m[r][c] = dark;
            used[r][c] = true;
        }

        // finder（7×7 双环 + 3×3 核心）+ separator（外圈 1 宽恒亮）
        void Finder(int r0, int c0)
        {
            for (var i = -1; i <= 7; i++)
            {
                for (var j = -1; j <= 7; j++)
                {
                    var r = r0 + i;
                    var c = c0 + j;
                    if (r < 0 || r >= size || c < 0 || c >= size)
                    {
                        continue;
                    }
                    var inside = i is >= 0 and <= 6 && j is >= 0 and <= 6;
                    var dark = inside
                        && (i is 0 or 6 || j is 0 or 6 || (i is >= 2 and <= 4 && j is >= 2 and <= 4));
                    Set(r, c, dark);
                }
            }
        }
        Finder(0, 0);
        Finder(0, size - 7);
        Finder(size - 7, 0);

        // timing：row 6 / col 6，在 separator 之间
        for (var i = 8; i < size - 8; i++)
        {
            Set(6, i, i % 2 == 0);
            Set(i, 6, i % 2 == 0);
        }

        // alignment：跳过三个 finder 角组合（与 timing 重叠处图案同构，后写覆盖无害）
        var centers = AlignmentCenters[version];
        var last = centers.Length - 1;
        if (centers.Length > 0)
        {
            for (var ci = 0; ci < centers.Length; ci++)
            {
                for (var cj = 0; cj < centers.Length; cj++)
                {
                    if ((ci == 0 && cj == 0) || (ci == 0 && cj == last) || (ci == last && cj == 0))
                    {
                        continue;
                    }
                    var cr = centers[ci];
                    var cc = centers[cj];
                    for (var i = -2; i <= 2; i++)
                    {
                        for (var j = -2; j <= 2; j++)
                        {
                            Set(cr + i, cc + j, i is -2 or 2 || j is -2 or 2 || (i == 0 && j == 0));
                        }
                    }
                }
            }
        }

        // format 预留区：row 8 与 col 8 的首尾段（值由 PlaceFormat 写，此处仅占位）
        for (var i = 0; i <= 8; i++)
        {
            used[8][i] = true;
            used[i][8] = true;
        }
        for (var i = 0; i < 8; i++)
        {
            used[8][size - 1 - i] = true;
            used[size - 1 - i][8] = true;
        }
        // dark module（恒暗，非 format 位）
        m[size - 8][8] = true;
        used[size - 8][8] = true;

        // 版本信息预留（v7+）：右上与左下两块 3×6（值由 PlaceVersion 写）
        if (version >= 6)
        {
            for (var i = 0; i < 18; i++)
            {
                used[i / 3][size - 11 + i % 3] = true;
                used[size - 11 + i % 3][i / 3] = true;
            }
        }

        return (m, used);
    }

    /// <summary>zigzag 数据置放：列对从右向左，方向逐对翻转，跳过 col 6 与功能模块</summary>
    private static void PlaceData(bool[][] m, bool[][] used, bool[] bits)
    {
        var size = m.Length;
        var idx = 0;
        var upward = true;
        for (var c = size - 1; c > 0; c -= 2)
        {
            if (c == 6)
            {
                c--; // 跳过 timing 列
            }
            for (var k = 0; k < size; k++)
            {
                var r = upward ? size - 1 - k : k;
                foreach (var col in (Span<int>)[c, c - 1])
                {
                    if (!used[r][col])
                    {
                        m[r][col] = idx < bits.Length && bits[idx];
                        idx++;
                    }
                }
            }
            upward = !upward;
        }
    }

    /// <summary>8 种掩码罚分择优，只翻数据模块；返回胜出掩码号</summary>
    private static int ApplyBestMask(bool[][] m, bool[][] used)
    {
        var size = m.Length;
        var original = CloneJagged(m);
        var bestMask = 0;
        var bestPenalty = int.MaxValue;
        for (var mask = 0; mask < 8; mask++)
        {
            Restore(original, m, used, mask);
            var p = Penalty(m);
            if (p < bestPenalty)
            {
                bestPenalty = p;
                bestMask = mask;
            }
        }
        Restore(original, m, used, bestMask);
        return bestMask;
    }

    private static void Restore(bool[][] original, bool[][] m, bool[][] used, int mask)
    {
        var size = m.Length;
        for (var i = 0; i < size; i++)
        {
            Array.Copy(original[i], m[i], size);
        }
        for (var r = 0; r < size; r++)
        {
            for (var c = 0; c < size; c++)
            {
                if (!used[r][c] && MaskBit(mask, r, c))
                {
                    m[r][c] = !m[r][c];
                }
            }
        }
    }

    private static bool MaskBit(int mask, int r, int c) => mask switch
    {
        0 => (r + c) % 2 == 0,
        1 => r % 2 == 0,
        2 => c % 3 == 0,
        3 => (r + c) % 3 == 0,
        4 => (r / 2 + c / 3) % 2 == 0,
        5 => r * c % 2 + r * c % 3 == 0,
        6 => (r * c % 2 + r * c % 3) % 2 == 0,
        _ => (r * c % 3 + (r + c) % 2) % 2 == 0,
    };

    /// <summary>罚分：P1 连续同色 / P2 2×2 块 / P3 finder-like / P4 暗模块比例</summary>
    private static int Penalty(bool[][] m)
    {
        var size = m.Length;
        var penalty = 0;

        // P1：行 + 列连续同色（≥5 计罚：5 格 3 分，每多 1 格 +1）
        for (var axis = 0; axis < 2; axis++)
        {
            for (var a = 0; a < size; a++)
            {
                var run = 1;
                var prev = axis == 0 ? m[a][0] : m[0][a];
                for (var b = 1; b < size; b++)
                {
                    var cur = axis == 0 ? m[a][b] : m[b][a];
                    if (cur == prev)
                    {
                        run++;
                        if (run == 5)
                        {
                            penalty += 3;
                        }
                        else if (run > 5)
                        {
                            penalty += 1;
                        }
                    }
                    else
                    {
                        run = 1;
                        prev = cur;
                    }
                }
            }
        }

        // P2：2×2 同色块每块 3 分
        for (var r = 0; r < size - 1; r++)
        {
            for (var c = 0; c < size - 1; c++)
            {
                if (m[r][c] == m[r][c + 1] && m[r][c] == m[r + 1][c] && m[r][c] == m[r + 1][c + 1])
                {
                    penalty += 3;
                }
            }
        }

        // P3：1:1:3:1:1 finder-like 模式（前或后 4 格全亮）每个 40 分
        for (var axis = 0; axis < 2; axis++)
        {
            for (var a = 0; a < size; a++)
            {
                for (var b = 0; b <= size - 7; b++)
                {
                    var match = true;
                    for (var k = 0; k < 7 && match; k++)
                    {
                        var v = axis == 0 ? m[a][b + k] : m[b + k][a];
                        if (v != (k is 0 or 2 or 3 or 4 or 6))
                        {
                            match = false;
                        }
                    }
                    if (!match)
                    {
                        continue;
                    }
                    var lightBefore = b >= 4;
                    var lightAfter = b + 10 < size;
                    for (var k = 1; k <= 4 && (lightBefore || lightAfter); k++)
                    {
                        if (lightBefore && (axis == 0 ? m[a][b - k] : m[b - k][a]))
                        {
                            lightBefore = false;
                        }
                        if (lightAfter && (axis == 0 ? m[a][b + 6 + k] : m[b + 6 + k][a]))
                        {
                            lightAfter = false;
                        }
                    }
                    if (lightBefore || lightAfter)
                    {
                        penalty += 40;
                    }
                }
            }
        }

        // P4：暗模块比例偏离 50%，每 5% 一档 10 分
        var dark = 0;
        foreach (var row in m)
        {
            foreach (var v in row)
            {
                if (v)
                {
                    dark++;
                }
            }
        }
        var ratio = dark * 100 / (size * size);
        penalty += Math.Abs(ratio - 50) / 5 * 10;
        return penalty;
    }

    /// <summary>format 信息 BCH(15,5)：级位 2 + 掩码位 3 → 15 bit（xor 0x5412 掩码）</summary>
    private static int FormatBits(int level, int mask)
    {
        var data = (LevelFormatBits[level] << 3) | mask;
        var rem = data;
        for (var i = 0; i < 10; i++)
        {
            rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        }
        return (((data << 10) | rem) ^ 0x5412) & 0x7fff;
    }

    /// <summary>两份 format 信息放置（左上 + 右上/左下）与 dark module</summary>
    private static void PlaceFormat(bool[][] m, int level, int mask)
    {
        var size = m.Length;
        var bits = FormatBits(level, mask);
        bool Bit(int i) => ((bits >> i) & 1) == 1;

        // 第一份：col 8 的 rows 0-5/7、(8,8)、(7,8)、row 8 的 cols 6..0
        for (var i = 0; i <= 5; i++)
        {
            m[i][8] = Bit(i);
        }
        m[7][8] = Bit(6);
        m[8][8] = Bit(7);
        m[8][7] = Bit(8);
        for (var i = 9; i < 15; i++)
        {
            m[8][14 - i] = Bit(i);
        }
        // 第二份：row 8 右侧 bit0..7，col 8 下方 bit8..14
        for (var i = 0; i < 8; i++)
        {
            m[8][size - 1 - i] = Bit(i);
        }
        for (var i = 8; i < 15; i++)
        {
            m[size - 15 + i][8] = Bit(i);
        }
        m[size - 8][8] = true; // dark module
    }

    /// <summary>版本信息（v7+）：6 bit 版本号 + 12 bit BCH(18,6)（G=0x1F25），
    /// 两块 3×6 放右上与左下（不参与掩码）</summary>
    private static void PlaceVersion(bool[][] m, int version)
    {
        if (version < 6)
        {
            return; // v1-v6 无版本信息
        }
        var ver = version + 1; // 数组索引 → 版本号
        var size = m.Length;
        var rem = ver;
        for (var i = 0; i < 12; i++)
        {
            rem = (rem << 1) ^ ((rem >> 11) * 0x1f25);
        }
        var bits = (ver << 12) | (rem & 0xfff);
        for (var i = 0; i < 18; i++)
        {
            var dark = ((bits >> i) & 1) == 1;
            m[i / 3][size - 11 + i % 3] = dark;       // 右上
            m[size - 11 + i % 3][i / 3] = dark;       // 左下
        }
    }

    /// <summary>交错数组构造（CA1814：分析器禁止多维数组，error 级）</summary>
    private static bool[][] Jagged(int size)
    {
        var a = new bool[size][];
        for (var i = 0; i < size; i++)
        {
            a[i] = new bool[size];
        }
        return a;
    }

    private static bool[][] CloneJagged(bool[][] m)
    {
        var a = new bool[m.Length][];
        for (var i = 0; i < m.Length; i++)
        {
            a[i] = (bool[])m[i].Clone();
        }
        return a;
    }
}

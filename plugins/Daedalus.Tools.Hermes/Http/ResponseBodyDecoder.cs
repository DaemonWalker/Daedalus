using System.IO.Compression;
using System.Text;

namespace Daedalus.Tools.Hermes.Http;

/// <summary>
/// 响应体解码（hermes.md §5.1）：先按 Content-Encoding 解压（gzip / deflate / br，多层按施加逆序），
/// 再按 Content-Type charset 转字符串（缺省按 BOM 嗅探，否则 UTF-8；未知字符集回退 UTF-8）。
/// 解压放在引擎侧而非 HttpClientHandler.AutomaticDecompression：后者会向用户指定的 Accept-Encoding
/// 并集 handler 自己的值，破坏"请求头原样发出"的语义。
/// </summary>
internal static class ResponseBodyDecoder
{
    /// <summary>读取并解码响应体文本。</summary>
    public static async Task<string> DecodeAsync(HttpContent content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        byte[] bytes = await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        bytes = Decompress(bytes, [.. content.Headers.ContentEncoding]);
        return DecodeText(bytes, content.Headers.ContentType?.CharSet);
    }

    private static byte[] Decompress(byte[] bytes, IReadOnlyList<string> encodings)
    {
        // Content-Encoding 按施加顺序排列（如 "gzip, br" 表示先 gzip 后 br），解码按逆序逐层还原
        byte[] current = bytes;
        for (int i = encodings.Count - 1; i >= 0; i--)
        {
            current = encodings[i].Trim().ToLowerInvariant() switch
            {
                "gzip" => Inflate(current, s => new GZipStream(s, CompressionMode.Decompress)),
                "deflate" => InflateDeflate(current),
                "br" => Inflate(current, s => new BrotliStream(s, CompressionMode.Decompress)),
                _ => current, // identity 或未知编码：原样保留
            };
        }

        return current;
    }

    // deflate 有 raw（RFC 1951）与 zlib 封装（RFC 1950，0x78 魔数）两种现实形态，按首字节区分
    private static byte[] InflateDeflate(byte[] bytes) => bytes.Length > 0 && bytes[0] == 0x78
        ? Inflate(bytes, s => new ZLibStream(s, CompressionMode.Decompress))
        : Inflate(bytes, s => new DeflateStream(s, CompressionMode.Decompress));

    private static byte[] Inflate(byte[] bytes, Func<Stream, Stream> wrap)
    {
        try
        {
            using var input = new MemoryStream(bytes);
            using Stream decompressed = wrap(input);
            using var output = new MemoryStream();
            decompressed.CopyTo(output);
            return output.ToArray();
        }
        catch (InvalidDataException)
        {
            // 服务器标错 Content-Encoding 时尽力而为：保留原始字节，不让发送流程整体失败
            return bytes;
        }
    }

    private static string DecodeText(byte[] bytes, string? charSet)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(charSet))
        {
            try
            {
                // charset 可能带引号（如 charset="utf-8"）；CodePagesEncodingProvider 未注册，非 Unicode 代码页会回落 UTF-8
                return Encoding.GetEncoding(charSet.Trim().Trim('"')).GetString(bytes);
            }
            catch (ArgumentException)
            {
                // 未知字符集回退 UTF-8
            }
        }

        return DecodeWithBomSniff(bytes);
    }

    private static string DecodeWithBomSniff(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE }))
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF }))
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        return Encoding.UTF8.GetString(bytes);
    }
}

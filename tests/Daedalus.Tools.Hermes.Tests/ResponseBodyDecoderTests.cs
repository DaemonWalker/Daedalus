using System.IO.Compression;
using System.Text;

using Daedalus.Tools.Hermes.Http;

namespace Daedalus.Tools.Hermes.Tests;

/// <summary>ResponseBodyDecoder：Content-Encoding 解压与字符集解码（hermes.md §5.1）。</summary>
public sealed class ResponseBodyDecoderTests
{
    private static ByteArrayContent Content(byte[] bytes, string? contentEncoding = null, string? contentType = null)
    {
        var content = new ByteArrayContent(bytes);
        if (contentEncoding is not null)
        {
            content.Headers.TryAddWithoutValidation("Content-Encoding", contentEncoding);
        }

        if (contentType is not null)
        {
            content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        }

        return content;
    }

    private static byte[] Compress(byte[] data, Func<Stream, Stream> wrap)
    {
        using var output = new MemoryStream();
        using (Stream compressed = wrap(output))
        {
            compressed.Write(data);
        }

        return output.ToArray();
    }

    private static byte[] Gzip(byte[] data) => Compress(data, s => new GZipStream(s, CompressionMode.Compress, leaveOpen: true));

    private static byte[] Brotli(byte[] data) => Compress(data, s => new BrotliStream(s, CompressionMode.Compress, leaveOpen: true));

    [Fact]
    public async Task DecodeAsync_无压缩_按原样解码()
    {
        Assert.Equal("hello", await ResponseBodyDecoder.DecodeAsync(Content("hello"u8.ToArray()), CancellationToken.None));
    }

    [Fact]
    public async Task DecodeAsync_gzip_解压()
    {
        ByteArrayContent content = Content(Gzip("{\"a\":1}"u8.ToArray()), "gzip");

        Assert.Equal("{\"a\":1}", await ResponseBodyDecoder.DecodeAsync(content, CancellationToken.None));
    }

    [Fact]
    public async Task DecodeAsync_br_解压()
    {
        ByteArrayContent content = Content(Brotli("布鲁提"u8.ToArray()), "br");

        Assert.Equal("布鲁提", await ResponseBodyDecoder.DecodeAsync(content, CancellationToken.None));
    }

    [Fact]
    public async Task DecodeAsync_deflate_zlib封装与raw均解压()
    {
        byte[] raw = "deflate-body"u8.ToArray();
        ByteArrayContent zlib = Content(Compress(raw, s => new ZLibStream(s, CompressionMode.Compress, leaveOpen: true)), "deflate");
        ByteArrayContent rawDeflate = Content(Compress(raw, s => new DeflateStream(s, CompressionMode.Compress, leaveOpen: true)), "deflate");

        Assert.Equal("deflate-body", await ResponseBodyDecoder.DecodeAsync(zlib, CancellationToken.None));
        Assert.Equal("deflate-body", await ResponseBodyDecoder.DecodeAsync(rawDeflate, CancellationToken.None));
    }

    [Fact]
    public async Task DecodeAsync_多层编码_按施加逆序解压()
    {
        // 先 gzip 后 br（Content-Encoding: gzip, br），解码先 br 后 gzip
        byte[] bytes = Brotli(Gzip("multi"u8.ToArray()));
        ByteArrayContent content = Content(bytes, "gzip, br");

        Assert.Equal("multi", await ResponseBodyDecoder.DecodeAsync(content, CancellationToken.None));
    }

    [Fact]
    public async Task DecodeAsync_服务器标错编码_保留原始字节不抛异常()
    {
        ByteArrayContent content = Content("not-really-gzip"u8.ToArray(), "gzip");

        Assert.Equal("not-really-gzip", await ResponseBodyDecoder.DecodeAsync(content, CancellationToken.None));
    }

    [Fact]
    public async Task DecodeAsync_指定charset_按字符集解码()
    {
        byte[] bytes = Encoding.Unicode.GetBytes("中文");
        ByteArrayContent content = Content(bytes, contentType: "text/plain; charset=utf-16");

        Assert.Equal("中文", await ResponseBodyDecoder.DecodeAsync(content, CancellationToken.None));
    }

    [Fact]
    public async Task DecodeAsync_未知charset_回退UTF8()
    {
        ByteArrayContent content = Content("abc"u8.ToArray(), contentType: "text/plain; charset=x-unknown");

        Assert.Equal("abc", await ResponseBodyDecoder.DecodeAsync(content, CancellationToken.None));
    }

    [Fact]
    public async Task DecodeAsync_无charset带UTF8BOM_按BOM嗅探并去除()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. "bom"u8];

        Assert.Equal("bom", await ResponseBodyDecoder.DecodeAsync(Content(bytes), CancellationToken.None));
    }
}

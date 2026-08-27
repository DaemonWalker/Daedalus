namespace Daedalus.Tools.Cadmus.Tests;

/// <summary>CadmusOperations 测试：编码方式清单、已知编码向量、错误收敛、初始方式解析。</summary>
public class CadmusOperationsTests
{
    private static CadmusEncoding Base64 => CadmusOperations.Encodings.Single(e => e.Id == CadmusOperations.Base64Id);

    private static CadmusEncoding Url => CadmusOperations.Encodings.Single(e => e.Id == CadmusOperations.UrlId);

    [Fact]
    public void Encodings_方式清单_含Base64与Url且Base64为首项()
    {
        Assert.Equal(2, CadmusOperations.Encodings.Count);
        Assert.Equal(CadmusOperations.Base64Id, CadmusOperations.Encodings[0].Id);
        Assert.Equal(CadmusOperations.UrlId, CadmusOperations.Encodings[1].Id);
        Assert.False(string.IsNullOrWhiteSpace(CadmusOperations.Encodings[0].DisplayName));
        Assert.False(string.IsNullOrWhiteSpace(CadmusOperations.Encodings[1].DisplayName));
    }

    [Fact]
    public void Encode_Base64Ascii输入_返回已知向量()
    {
        CadmusOperationResult result = CadmusOperations.Encode(Base64, "hello");

        Assert.True(result.Success);
        Assert.Equal("aGVsbG8=", result.Output);
        Assert.Contains("编码完成", result.StatusText);
    }

    [Fact]
    public void Encode_Base64中文输入_按UTF8字节编码()
    {
        CadmusOperationResult result = CadmusOperations.Encode(Base64, "中文");

        Assert.True(result.Success);
        Assert.Equal("5Lit5paH", result.Output);
    }

    [Fact]
    public void Encode_Base64空串_返回空串()
    {
        CadmusOperationResult result = CadmusOperations.Encode(Base64, string.Empty);

        Assert.True(result.Success);
        Assert.Equal(string.Empty, result.Output);
    }

    [Fact]
    public void Encode_Url特殊字符_按RFC3986转义()
    {
        CadmusOperationResult result = CadmusOperations.Encode(Url, "a b&c=中");

        Assert.True(result.Success);
        Assert.Equal("a%20b%26c%3D%E4%B8%AD", result.Output);
    }

    [Fact]
    public void Encode_Url空串_返回空串()
    {
        CadmusOperationResult result = CadmusOperations.Encode(Url, string.Empty);

        Assert.True(result.Success);
        Assert.Equal(string.Empty, result.Output);
    }

    [Fact]
    public void Encode_Url孤立代理项_按替换字符编码不抛异常()
    {
        // 孤立高代理项不是合法 Unicode 标量：现行 BCL 按 U+FFFD 替换后编码，操作仍算成功
        CadmusOperationResult result = CadmusOperations.Encode(Url, "\uD800");

        Assert.True(result.Success);
        Assert.Equal("%EF%BF%BD", result.Output);
    }

    [Fact]
    public void Encode_未知方式id_抛InvalidOperationException()
    {
        var unknown = new CadmusEncoding("rot13", "ROT13");

        Assert.Throws<InvalidOperationException>(() => CadmusOperations.Encode(unknown, "input"));
    }

    [Fact]
    public void ResolveInitialEncoding_匹配上次方式_返回对应方式()
    {
        CadmusEncoding result = CadmusOperations.ResolveInitialEncoding("URL");

        Assert.Equal(CadmusOperations.UrlId, result.Id);
    }

    [Fact]
    public void ResolveInitialEncoding_上次方式未知_回落列表第一个()
    {
        CadmusEncoding result = CadmusOperations.ResolveInitialEncoding("rot13");

        Assert.Equal(CadmusOperations.Base64Id, result.Id);
    }

    [Fact]
    public void ResolveInitialEncoding_首次启动_返回列表第一个()
    {
        CadmusEncoding result = CadmusOperations.ResolveInitialEncoding(null);

        Assert.Equal(CadmusOperations.Base64Id, result.Id);
    }
}

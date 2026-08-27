using System.Text;

namespace Daedalus.Tools.Oedipus.Tests;

/// <summary>OedipusOperations 测试：解码方式清单、已知解码向量、错误收敛、JWT、初始方式解析。</summary>
public class OedipusOperationsTests
{
    private static OedipusDecoding Base64 => OedipusOperations.Decodings.Single(d => d.Id == OedipusOperations.Base64Id);

    private static OedipusDecoding Url => OedipusOperations.Decodings.Single(d => d.Id == OedipusOperations.UrlId);

    private static OedipusDecoding Xml => OedipusOperations.Decodings.Single(d => d.Id == OedipusOperations.XmlId);

    private static OedipusDecoding Jwt => OedipusOperations.Decodings.Single(d => d.Id == OedipusOperations.JwtId);

    /// <summary>测试用 Base64Url 编码（与实现无关的独立计算）。</summary>
    private static string Base64UrlEncode(string text)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(text))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static string BuildJwt(string headerJson, string payloadJson, string signature = "sig")
    {
        return Base64UrlEncode(headerJson) + "." + Base64UrlEncode(payloadJson) + "." + signature;
    }

    [Fact]
    public void Decodings_方式清单_含四种方式且Base64为首项()
    {
        Assert.Equal(4, OedipusOperations.Decodings.Count);
        Assert.Equal(OedipusOperations.Base64Id, OedipusOperations.Decodings[0].Id);
        Assert.Equal(OedipusOperations.UrlId, OedipusOperations.Decodings[1].Id);
        Assert.Equal(OedipusOperations.XmlId, OedipusOperations.Decodings[2].Id);
        Assert.Equal(OedipusOperations.JwtId, OedipusOperations.Decodings[3].Id);
        Assert.All(OedipusOperations.Decodings, d => Assert.False(string.IsNullOrWhiteSpace(d.DisplayName)));
    }

    [Fact]
    public void Decode_Base64Ascii输入_返回已知向量()
    {
        OedipusOperationResult result = OedipusOperations.Decode(Base64, "aGVsbG8=");

        Assert.True(result.Success);
        Assert.Equal("hello", result.Output);
        Assert.Contains("解码完成", result.StatusText);
    }

    [Fact]
    public void Decode_Base64中文输入_按UTF8还原()
    {
        OedipusOperationResult result = OedipusOperations.Decode(Base64, "5Lit5paH");

        Assert.True(result.Success);
        Assert.Equal("中文", result.Output);
    }

    [Fact]
    public void Decode_Base64空串_返回空串()
    {
        OedipusOperationResult result = OedipusOperations.Decode(Base64, string.Empty);

        Assert.True(result.Success);
        Assert.Equal(string.Empty, result.Output);
    }

    [Fact]
    public void Decode_Base64非法格式_收敛为错误状态()
    {
        OedipusOperationResult result = OedipusOperations.Decode(Base64, "!!!这不是Base64!!!");

        Assert.False(result.Success);
        Assert.Null(result.Output);
        Assert.Contains("解码失败", result.StatusText);
    }

    [Fact]
    public void Decode_Base64字节非合法UTF8_收敛为错误状态()
    {
        // 0xFF 0xFE 不是合法 UTF-8 序列
        OedipusOperationResult result = OedipusOperations.Decode(Base64, Convert.ToBase64String([0xFF, 0xFE]));

        Assert.False(result.Success);
        Assert.Null(result.Output);
        Assert.Contains("UTF-8", result.StatusText);
    }

    [Fact]
    public void Decode_Url特殊字符_按百分号还原()
    {
        OedipusOperationResult result = OedipusOperations.Decode(Url, "a%20b%26c%3D%E4%B8%AD");

        Assert.True(result.Success);
        Assert.Equal("a b&c=中", result.Output);
    }

    [Fact]
    public void Decode_Url空串_返回空串()
    {
        OedipusOperationResult result = OedipusOperations.Decode(Url, string.Empty);

        Assert.True(result.Success);
        Assert.Equal(string.Empty, result.Output);
    }

    [Fact]
    public void Decode_Xml预定义实体_全部还原()
    {
        OedipusOperationResult result = OedipusOperations.Decode(Xml, "&amp;&lt;&gt;&quot;&apos;");

        Assert.True(result.Success);
        Assert.Equal("&<>\"'", result.Output);
    }

    [Fact]
    public void Decode_Xml数字实体_十进制与十六进制均还原()
    {
        OedipusOperationResult result = OedipusOperations.Decode(Xml, "&#65;&#x42;&#20013;");

        Assert.True(result.Success);
        Assert.Equal("AB中", result.Output);
    }

    [Fact]
    public void Decode_Xml无实体输入_原样返回()
    {
        OedipusOperationResult result = OedipusOperations.Decode(Xml, "普通文本");

        Assert.True(result.Success);
        Assert.Equal("普通文本", result.Output);
    }

    [Fact]
    public void Decode_Jwt合法令牌_Header与Payload美化输出且签名段原样标注()
    {
        string token = BuildJwt("{\"alg\":\"HS256\",\"typ\":\"JWT\"}", "{\"sub\":\"1234567890\",\"name\":\"张三\"}", "SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c");

        OedipusOperationResult result = OedipusOperations.Decode(Jwt, token);

        Assert.True(result.Success);
        Assert.NotNull(result.Output);
        Assert.Contains("--- Header ---", result.Output);
        Assert.Contains("\"alg\": \"HS256\"", result.Output);
        Assert.Contains("--- Payload ---", result.Output);
        // 中文 claim 按 UnsafeRelaxedJsonEscaping 不转义输出
        Assert.Contains("\"name\": \"张三\"", result.Output);
        Assert.Contains("--- 签名 (Base64Url, 未解码) ---", result.Output);
        Assert.EndsWith("SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c", result.Output);
    }

    [Fact]
    public void Decode_Jwt段数不足_收敛为错误状态()
    {
        OedipusOperationResult result = OedipusOperations.Decode(Jwt, "aaa.bbb");

        Assert.False(result.Success);
        Assert.Null(result.Output);
        Assert.Contains("3 段", result.StatusText);
    }

    [Fact]
    public void Decode_Jwt段数过多_收敛为错误状态()
    {
        OedipusOperationResult result = OedipusOperations.Decode(Jwt, "a.b.c.d");

        Assert.False(result.Success);
        Assert.Null(result.Output);
        Assert.Contains("3 段", result.StatusText);
    }

    [Fact]
    public void Decode_JwtPayload非法Json_收敛为错误状态()
    {
        string token = BuildJwt("{\"alg\":\"HS256\"}", "这不是 JSON");

        OedipusOperationResult result = OedipusOperations.Decode(Jwt, token);

        Assert.False(result.Success);
        Assert.Null(result.Output);
        Assert.Contains("payload", result.StatusText);
    }

    [Fact]
    public void Decode_Jwt段含非法Base64Url字符_收敛为错误状态()
    {
        OedipusOperationResult result = OedipusOperations.Decode(Jwt, "ab!.bbb.sig");

        Assert.False(result.Success);
        Assert.Null(result.Output);
        Assert.Contains("解码失败", result.StatusText);
    }

    [Fact]
    public void Decode_未知方式id_抛InvalidOperationException()
    {
        var unknown = new OedipusDecoding("rot13", "ROT13");

        Assert.Throws<InvalidOperationException>(() => OedipusOperations.Decode(unknown, "input"));
    }

    [Fact]
    public void ResolveInitialDecoding_匹配上次方式_返回对应方式()
    {
        OedipusDecoding result = OedipusOperations.ResolveInitialDecoding("JWT");

        Assert.Equal(OedipusOperations.JwtId, result.Id);
    }

    [Fact]
    public void ResolveInitialDecoding_上次方式未知_回落列表第一个()
    {
        OedipusDecoding result = OedipusOperations.ResolveInitialDecoding("rot13");

        Assert.Equal(OedipusOperations.Base64Id, result.Id);
    }

    [Fact]
    public void ResolveInitialDecoding_首次启动_返回列表第一个()
    {
        OedipusDecoding result = OedipusOperations.ResolveInitialDecoding(null);

        Assert.Equal(OedipusOperations.Base64Id, result.Id);
    }
}

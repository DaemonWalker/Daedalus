using System.Text;

namespace Daedalus.Tools.Iris.Tests;

/// <summary>IrisOperations 测试：方式清单、编码/解码已知向量（移植自 Cadmus/Oedipus 测试）、错误收敛、初始方式解析。</summary>
public class IrisOperationsTests
{
    private static IrisMethod Method(string id)
    {
        return IrisOperations.Methods.Single(m => m.Id == id);
    }

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
    public void Methods_方式清单_十一种方式且顺序固定()
    {
        Assert.Equal(11, IrisOperations.Methods.Count);
        Assert.Equal(
            [
                IrisOperations.Base64EncodeId, IrisOperations.UrlEncodeId,
                IrisOperations.Base64DecodeId, IrisOperations.UrlDecodeId, IrisOperations.XmlDecodeId, IrisOperations.JwtDecodeId,
                IrisOperations.AesEncryptId, IrisOperations.AesDecryptId,
                IrisOperations.RsaKeygenId, IrisOperations.RsaEncryptId, IrisOperations.RsaDecryptId,
            ],
            IrisOperations.Methods.Select(m => m.Id).ToArray());
        Assert.All(IrisOperations.Methods, m => Assert.False(string.IsNullOrWhiteSpace(m.DisplayName)));
        // 类别归属：前两项编码、随后四项解码、AES/RSA 加解密、密钥对生成
        Assert.Equal(IrisMethodCategory.Encode, Method(IrisOperations.Base64EncodeId).Category);
        Assert.Equal(IrisMethodCategory.Decode, Method(IrisOperations.JwtDecodeId).Category);
        Assert.Equal(IrisMethodCategory.Encrypt, Method(IrisOperations.AesEncryptId).Category);
        Assert.Equal(IrisMethodCategory.Decrypt, Method(IrisOperations.AesDecryptId).Category);
        Assert.Equal(IrisMethodCategory.Generate, Method(IrisOperations.RsaKeygenId).Category);
    }

    // ---- 编码（移植自 CadmusOperationsTests） ----

    [Fact]
    public void Encode_Base64Ascii输入_返回已知向量()
    {
        IrisOperationResult result = IrisOperations.Encode(Method(IrisOperations.Base64EncodeId), "hello");

        Assert.True(result.Success);
        Assert.Equal("aGVsbG8=", result.Output);
        Assert.Contains("编码完成", result.StatusText);
    }

    [Fact]
    public void Encode_Base64中文输入_按UTF8字节编码()
    {
        IrisOperationResult result = IrisOperations.Encode(Method(IrisOperations.Base64EncodeId), "中文");

        Assert.True(result.Success);
        Assert.Equal("5Lit5paH", result.Output);
    }

    [Fact]
    public void Encode_Base64空串_返回空串()
    {
        IrisOperationResult result = IrisOperations.Encode(Method(IrisOperations.Base64EncodeId), string.Empty);

        Assert.True(result.Success);
        Assert.Equal(string.Empty, result.Output);
    }

    [Fact]
    public void Encode_Url特殊字符_按RFC3986转义()
    {
        IrisOperationResult result = IrisOperations.Encode(Method(IrisOperations.UrlEncodeId), "a b&c=中");

        Assert.True(result.Success);
        Assert.Equal("a%20b%26c%3D%E4%B8%AD", result.Output);
    }

    [Fact]
    public void Encode_Url空串_返回空串()
    {
        IrisOperationResult result = IrisOperations.Encode(Method(IrisOperations.UrlEncodeId), string.Empty);

        Assert.True(result.Success);
        Assert.Equal(string.Empty, result.Output);
    }

    [Fact]
    public void Encode_Url孤立代理项_按替换字符编码不抛异常()
    {
        // 孤立高代理项不是合法 Unicode 标量：现行 BCL 按 U+FFFD 替换后编码，操作仍算成功
        IrisOperationResult result = IrisOperations.Encode(Method(IrisOperations.UrlEncodeId), "\uD800");

        Assert.True(result.Success);
        Assert.Equal("%EF%BF%BD", result.Output);
    }

    [Fact]
    public void Encode_未知方式id_抛InvalidOperationException()
    {
        var unknown = new IrisMethod("rot13", "ROT13", IrisMethodCategory.Encode);

        Assert.Throws<InvalidOperationException>(() => IrisOperations.Encode(unknown, "input"));
    }

    // ---- 解码（移植自 OedipusOperationsTests） ----

    [Fact]
    public void Decode_Base64Ascii输入_返回已知向量()
    {
        IrisOperationResult result = IrisOperations.Decode(Method(IrisOperations.Base64DecodeId), "aGVsbG8=");

        Assert.True(result.Success);
        Assert.Equal("hello", result.Output);
        Assert.Contains("解码完成", result.StatusText);
    }

    [Fact]
    public void Decode_Base64中文输入_按UTF8还原()
    {
        IrisOperationResult result = IrisOperations.Decode(Method(IrisOperations.Base64DecodeId), "5Lit5paH");

        Assert.True(result.Success);
        Assert.Equal("中文", result.Output);
    }

    [Fact]
    public void Decode_Base64非法格式_收敛为错误状态()
    {
        IrisOperationResult result = IrisOperations.Decode(Method(IrisOperations.Base64DecodeId), "!!!这不是Base64!!!");

        Assert.False(result.Success);
        Assert.Null(result.Output);
        Assert.Contains("解码失败", result.StatusText);
    }

    [Fact]
    public void Decode_Base64字节非合法UTF8_收敛为错误状态()
    {
        // 0xFF 0xFE 不是合法 UTF-8 序列
        IrisOperationResult result = IrisOperations.Decode(Method(IrisOperations.Base64DecodeId), Convert.ToBase64String([0xFF, 0xFE]));

        Assert.False(result.Success);
        Assert.Null(result.Output);
        Assert.Contains("UTF-8", result.StatusText);
    }

    [Fact]
    public void Decode_Url特殊字符_按百分号还原()
    {
        IrisOperationResult result = IrisOperations.Decode(Method(IrisOperations.UrlDecodeId), "a%20b%26c%3D%E4%B8%AD");

        Assert.True(result.Success);
        Assert.Equal("a b&c=中", result.Output);
    }

    [Fact]
    public void Decode_Xml预定义实体_全部还原()
    {
        IrisOperationResult result = IrisOperations.Decode(Method(IrisOperations.XmlDecodeId), "&amp;&lt;&gt;&quot;&apos;");

        Assert.True(result.Success);
        Assert.Equal("&<>\"'", result.Output);
    }

    [Fact]
    public void Decode_Xml数字实体_十进制与十六进制均还原()
    {
        IrisOperationResult result = IrisOperations.Decode(Method(IrisOperations.XmlDecodeId), "&#65;&#x42;&#20013;");

        Assert.True(result.Success);
        Assert.Equal("AB中", result.Output);
    }

    [Fact]
    public void Decode_Xml无实体输入_原样返回()
    {
        IrisOperationResult result = IrisOperations.Decode(Method(IrisOperations.XmlDecodeId), "普通文本");

        Assert.True(result.Success);
        Assert.Equal("普通文本", result.Output);
    }

    [Fact]
    public void Decode_Xml解码结果是合法Xml_美化排版并换行()
    {
        IrisOperationResult result = IrisOperations.Decode(Method(IrisOperations.XmlDecodeId), "&lt;root&gt;&lt;a&gt;1&lt;/a&gt;&lt;/root&gt;");

        Assert.True(result.Success);
        Assert.Equal("<root>\r\n  <a>1</a>\r\n</root>", result.Output);
    }

    [Fact]
    public void Decode_Xml解码结果带声明头_声明头保留且不被改写编码()
    {
        IrisOperationResult result = IrisOperations.Decode(
            Method(IrisOperations.XmlDecodeId), "&lt;?xml version=&quot;1.0&quot; encoding=&quot;utf-8&quot;?&gt;&lt;root/&gt;");

        Assert.True(result.Success);
        Assert.Equal("<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<root />", result.Output);
    }

    [Fact]
    public void Decode_Jwt合法令牌_Header与Payload美化输出且签名段原样标注()
    {
        string token = BuildJwt("{\"alg\":\"HS256\",\"typ\":\"JWT\"}", "{\"sub\":\"1234567890\",\"name\":\"张三\"}", "SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c");

        IrisOperationResult result = IrisOperations.Decode(Method(IrisOperations.JwtDecodeId), token);

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
        IrisOperationResult result = IrisOperations.Decode(Method(IrisOperations.JwtDecodeId), "aaa.bbb");

        Assert.False(result.Success);
        Assert.Null(result.Output);
        Assert.Contains("3 段", result.StatusText);
    }

    [Fact]
    public void Decode_Jwt段数过多_收敛为错误状态()
    {
        IrisOperationResult result = IrisOperations.Decode(Method(IrisOperations.JwtDecodeId), "a.b.c.d");

        Assert.False(result.Success);
        Assert.Null(result.Output);
        Assert.Contains("3 段", result.StatusText);
    }

    [Fact]
    public void Decode_JwtPayload非法Json_收敛为错误状态()
    {
        string token = BuildJwt("{\"alg\":\"HS256\"}", "这不是 JSON");

        IrisOperationResult result = IrisOperations.Decode(Method(IrisOperations.JwtDecodeId), token);

        Assert.False(result.Success);
        Assert.Null(result.Output);
        Assert.Contains("payload", result.StatusText);
    }

    [Fact]
    public void Decode_Jwt段含非法Base64Url字符_收敛为错误状态()
    {
        IrisOperationResult result = IrisOperations.Decode(Method(IrisOperations.JwtDecodeId), "ab!.bbb.sig");

        Assert.False(result.Success);
        Assert.Null(result.Output);
        Assert.Contains("解码失败", result.StatusText);
    }

    [Fact]
    public void Decode_未知方式id_抛InvalidOperationException()
    {
        var unknown = new IrisMethod("rot13", "ROT13", IrisMethodCategory.Decode);

        Assert.Throws<InvalidOperationException>(() => IrisOperations.Decode(unknown, "input"));
    }

    // ---- 初始方式解析 ----

    [Fact]
    public void ResolveInitialMethod_匹配上次方式_返回对应方式()
    {
        IrisMethod result = IrisOperations.ResolveInitialMethod("JWT-DEC");

        Assert.Equal(IrisOperations.JwtDecodeId, result.Id);
    }

    [Fact]
    public void ResolveInitialMethod_上次方式未知_回落列表第一个()
    {
        IrisMethod result = IrisOperations.ResolveInitialMethod("rot13");

        Assert.Equal(IrisOperations.Base64EncodeId, result.Id);
    }

    [Fact]
    public void ResolveInitialMethod_首次启动_返回列表第一个()
    {
        IrisMethod result = IrisOperations.ResolveInitialMethod(null);

        Assert.Equal(IrisOperations.Base64EncodeId, result.Id);
    }
}

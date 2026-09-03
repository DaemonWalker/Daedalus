using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace Daedalus.Tools.Iris;

/// <summary>方式类别：决定执行按钮文案与动态参数区显隐（iris.md §4）。</summary>
public enum IrisMethodCategory
{
    /// <summary>编码（Base64 / URL）。</summary>
    Encode,

    /// <summary>解码（Base64 / URL / XML 实体 / JWT）。</summary>
    Decode,

    /// <summary>加密（AES / RSA）。</summary>
    Encrypt,

    /// <summary>解密（AES / RSA）。</summary>
    Decrypt,

    /// <summary>生成（RSA 密钥对）。</summary>
    Generate,
}

/// <summary>处理方式。</summary>
/// <param name="Id">方式 id，如 "base64-enc"、"aes-enc"，全小写（设置文件以此持久化）。</param>
/// <param name="DisplayName">界面显示名。</param>
/// <param name="Category">方式类别。</param>
public sealed record IrisMethod(string Id, string DisplayName, IrisMethodCategory Category);

/// <summary>一次操作的结果。</summary>
/// <param name="Success">操作是否成功。</param>
/// <param name="StatusText">状态栏展示文本：成功为结果摘要，失败为错误信息。</param>
/// <param name="Output">成功时的输出文本；失败时为 null（输出区保持不变）。</param>
public sealed record IrisOperationResult(bool Success, string StatusText, string? Output);

/// <summary>
/// Iris 的操作编排（iris.md §5，非 UI 可测）：方式清单与 Base64 / URL 编码、
/// Base64 / URL / XML 实体 / JWT 解码实现（移植自 Cadmus / Oedipus），
/// 输入非法收敛为错误状态文本，不向界面抛异常。AES / RSA 见
/// <see cref="IrisAesOperations"/> / <see cref="IrisRsaOperations"/>。
/// </summary>
public static class IrisOperations
{
    /// <summary>Base64 编码方式 id。</summary>
    public const string Base64EncodeId = "base64-enc";

    /// <summary>URL 编码方式 id。</summary>
    public const string UrlEncodeId = "url-enc";

    /// <summary>Base64 解码方式 id。</summary>
    public const string Base64DecodeId = "base64-dec";

    /// <summary>URL 解码方式 id。</summary>
    public const string UrlDecodeId = "url-dec";

    /// <summary>XML 实体解码方式 id。</summary>
    public const string XmlDecodeId = "xml-dec";

    /// <summary>JWT 解码方式 id。</summary>
    public const string JwtDecodeId = "jwt-dec";

    /// <summary>AES 加密方式 id。</summary>
    public const string AesEncryptId = "aes-enc";

    /// <summary>AES 解密方式 id。</summary>
    public const string AesDecryptId = "aes-dec";

    /// <summary>RSA 生成密钥对方式 id。</summary>
    public const string RsaKeygenId = "rsa-keygen";

    /// <summary>RSA 加密方式 id。</summary>
    public const string RsaEncryptId = "rsa-enc";

    /// <summary>RSA 解密方式 id。</summary>
    public const string RsaDecryptId = "rsa-dec";

    /// <summary>全部方式（顺序即界面下拉顺序，首项为默认选中）。</summary>
    public static IReadOnlyList<IrisMethod> Methods { get; } =
    [
        new IrisMethod(Base64EncodeId, "Base64 编码", IrisMethodCategory.Encode),
        new IrisMethod(UrlEncodeId, "URL 编码", IrisMethodCategory.Encode),
        new IrisMethod(Base64DecodeId, "Base64 解码", IrisMethodCategory.Decode),
        new IrisMethod(UrlDecodeId, "URL 解码", IrisMethodCategory.Decode),
        new IrisMethod(XmlDecodeId, "XML 实体解码", IrisMethodCategory.Decode),
        new IrisMethod(JwtDecodeId, "JWT 解码", IrisMethodCategory.Decode),
        new IrisMethod(AesEncryptId, "AES 加密", IrisMethodCategory.Encrypt),
        new IrisMethod(AesDecryptId, "AES 解密", IrisMethodCategory.Decrypt),
        new IrisMethod(RsaKeygenId, "RSA 生成密钥对", IrisMethodCategory.Generate),
        new IrisMethod(RsaEncryptId, "RSA 加密", IrisMethodCategory.Encrypt),
        new IrisMethod(RsaDecryptId, "RSA 解密", IrisMethodCategory.Decrypt),
    ];

    // 严格 UTF-8：非法字节序列抛 DecoderFallbackException，按输入错误收敛而非静默替换出乱码
    internal static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    // JWT 段的美化输出：缩进 2；中文不转义（UnsafeRelaxedJsonEscaping），claim 值可读性优先
    private static readonly JsonSerializerOptions JwtJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // XML 解码后美化的读取设置：与 Proteus 同一安全基线——禁 DTD、禁外部实体（防 XXE）；
    // 忽略元素间纯空白文本节点，否则单行 XML 残留的空白会被当作内容保留
    private static readonly XmlReaderSettings XmlBeautifyReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreWhitespace = true,
    };

    // XML 美化输出固定缩进 2（与 JWT 的 JSON 美化一致）；声明头手动前置，
    // 避免 XmlWriter 面向 StringBuilder 输出时把 encoding 改写成 utf-16
    private static readonly XmlWriterSettings XmlBeautifyWriterSettings = new()
    {
        Indent = true,
        IndentChars = "  ",
        OmitXmlDeclaration = true,
    };

    /// <summary>按指定方式编码：Base64 为 UTF-8 字节序列的 Base64；URL 为 <see cref="Uri.EscapeDataString"/>。</summary>
    public static IrisOperationResult Encode(IrisMethod method, string input)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(input);

        try
        {
            string output = method.Id switch
            {
                Base64EncodeId => Convert.ToBase64String(Encoding.UTF8.GetBytes(input)),
                UrlEncodeId => Uri.EscapeDataString(input),
                // 未知 id 属编程错误（方式清单固定），抛出让 App 兜底，不伪装成输入错误
                _ => throw new InvalidOperationException($"未知编码方式：{method.Id}"),
            };
            return new IrisOperationResult(true, $"编码完成（{method.DisplayName}）", output);
        }
        catch (UriFormatException ex)
        {
            // 防御性兜底：.NET 现行行为下孤立代理项会按替换字符编码、不抛异常，
            // 但 EscapeDataString 的契约仍声明 UriFormatException，按输入错误收敛而不上抛
            return new IrisOperationResult(false, $"编码失败：{ex.Message}", null);
        }
    }

    /// <summary>按指定方式解码：Base64 为 UTF-8 文本还原；URL 为 <see cref="Uri.UnescapeDataString"/>；XML 实体为 <see cref="WebUtility.HtmlDecode"/>；JWT 见 <see cref="DecodeJwt"/>。</summary>
    public static IrisOperationResult Decode(IrisMethod method, string input)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(input);

        try
        {
            string output = method.Id switch
            {
                Base64DecodeId => DecodeBase64(input),
                UrlDecodeId => Uri.UnescapeDataString(input),
                XmlDecodeId => DecodeXmlEntities(input),
                JwtDecodeId => DecodeJwt(input),
                // 未知 id 属编程错误（方式清单固定），抛出让 App 兜底，不伪装成输入错误
                _ => throw new InvalidOperationException($"未知解码方式：{method.Id}"),
            };
            return new IrisOperationResult(true, $"解码完成（{method.DisplayName}）", output);
        }
        catch (UriFormatException ex)
        {
            // UriFormatException 派生自 FormatException，必须先于它捕获；
            // 防御性兜底：UnescapeDataString 对非法输入通常原样返回，但其契约声明该异常
            return new IrisOperationResult(false, $"解码失败：{ex.Message}", null);
        }
        catch (FormatException ex)
        {
            // Base64 / Base64Url 格式非法（含 JWT 段含非法字符、长度余数为 1）
            return new IrisOperationResult(false, $"解码失败：输入不是合法的 Base64（{ex.Message}）", null);
        }
        catch (DecoderFallbackException)
        {
            return new IrisOperationResult(false, "解码失败：解码结果不是合法的 UTF-8 文本", null);
        }
        catch (InvalidOperationException ex) when (method.Id == JwtDecodeId)
        {
            // JWT 结构性错误（段数不符、段内 JSON 非法）由 DecodeJwt 以 InvalidOperationException 给出可读信息；
            // 未知方式 id 的 InvalidOperationException 因 id 过滤不上钩，照常上抛让 App 兜底
            return new IrisOperationResult(false, $"解码失败：{ex.Message}", null);
        }
    }

    /// <summary>
    /// 解析启动时的初始方式：优先上次选择的方式 id（大小写不敏感），否则取列表第一个（Base64 编码）。
    /// </summary>
    public static IrisMethod ResolveInitialMethod(string? lastMethodId)
    {
        if (!string.IsNullOrEmpty(lastMethodId))
        {
            IrisMethod? remembered = Methods.FirstOrDefault(
                m => string.Equals(m.Id, lastMethodId, StringComparison.OrdinalIgnoreCase));
            if (remembered is not null)
            {
                return remembered;
            }
        }

        return Methods[0];
    }

    /// <summary>按所选格式编码字节序列（密文输出等）。</summary>
    internal static string EncodeBytes(byte[] bytes, IrisBytesEncoding format)
    {
        return format == IrisBytesEncoding.Hex ? Convert.ToHexString(bytes) : Convert.ToBase64String(bytes);
    }

    /// <summary>按所选格式解码用户输入的字段文本（密文、密钥、IV 等），空值与格式非法均给可读错误。</summary>
    internal static byte[] DecodeField(string? text, IrisBytesEncoding format, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException($"{fieldName}不能为空");
        }

        string trimmed = text.Trim();
        try
        {
            return format == IrisBytesEncoding.Hex ? Convert.FromHexString(trimmed) : Convert.FromBase64String(trimmed);
        }
        catch (FormatException)
        {
            throw new FormatException($"{fieldName}不是合法的 {format} 文本");
        }
    }

    /// <summary>Base64 解码：字节序列按严格 UTF-8 还原为文本，非法字节抛 DecoderFallbackException。</summary>
    private static string DecodeBase64(string input)
    {
        byte[] bytes = Convert.FromBase64String(input);
        return StrictUtf8.GetString(bytes);
    }

    /// <summary>
    /// XML 实体解码：选用 <see cref="WebUtility.HtmlDecode"/> 的理由——XML 的五个预定义实体与
    /// 数字实体（&amp;#NN; / &amp;#xHH;）都是 HTML 实体的子集，BCL 没有独立的 XML 实体解码 API。
    /// 解码结果若为合法 XML 则美化排版（缩进 2、自动换行），否则原样返回。
    /// </summary>
    private static string DecodeXmlEntities(string input)
    {
        string decoded = WebUtility.HtmlDecode(input);
        return TryBeautifyXml(decoded, out string? beautified) ? beautified : decoded;
    }

    /// <summary>尝试按 XML 美化排版；非法 XML（含 DOCTYPE，按安全基线拒绝）返回 false，由调用方原样输出。</summary>
    private static bool TryBeautifyXml(string input, [NotNullWhen(true)] out string? beautified)
    {
        try
        {
            XDocument document;
            using (XmlReader reader = XmlReader.Create(new StringReader(input), XmlBeautifyReaderSettings))
            {
                document = XDocument.Load(reader);
            }

            var builder = new StringBuilder();
            if (document.Declaration is not null)
            {
                builder.Append(document.Declaration.ToString()).Append("\r\n");
            }

            using (XmlWriter writer = XmlWriter.Create(builder, XmlBeautifyWriterSettings))
            {
                document.Save(writer);
            }

            beautified = builder.ToString();
            return true;
        }
        catch (XmlException)
        {
            // 解码结果不是 XML 属正常输入（如纯文本），按原样输出处理而非错误
            beautified = null;
            return false;
        }
    }

    /// <summary>
    /// JWT 解码（iris.md §3）：按 '.' 拆三段（header.payload.signature），header 与 payload
    /// 经 Base64Url 解码后按 UTF-8 JSON 解析并美化（缩进 2、中文不转义）；签名段不解码，原样附在末尾。
    /// </summary>
    private static string DecodeJwt(string input)
    {
        string[] segments = input.Split('.');
        if (segments.Length != 3)
        {
            throw new InvalidOperationException($"JWT 应由 3 段组成（header.payload.signature），实际 {segments.Length} 段");
        }

        string header = DecodeJwtJsonSegment(segments[0], "header");
        string payload = DecodeJwtJsonSegment(segments[1], "payload");

        var builder = new StringBuilder();
        builder.AppendLine("--- Header ---");
        builder.AppendLine(header);
        builder.AppendLine();
        builder.AppendLine("--- Payload ---");
        builder.AppendLine(payload);
        builder.AppendLine();
        builder.AppendLine("--- 签名 (Base64Url, 未解码) ---");
        builder.Append(segments[2]);
        return builder.ToString();
    }

    /// <summary>解码 JWT 的 header/payload 段：Base64Url → UTF-8 → JSON 美化；非法 JSON 给明确错误。</summary>
    private static string DecodeJwtJsonSegment(string segment, string segmentName)
    {
        byte[] bytes = DecodeBase64Url(segment);
        string json = StrictUtf8.GetString(bytes);
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document.RootElement, JwtJsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"JWT 的 {segmentName} 段不是合法 JSON：{ex.Message}");
        }
    }

    /// <summary>Base64Url 解码：'-'→'+'、'_'→'/'，按长度补 '='；余数为 1 属非法（Convert 抛 FormatException）。</summary>
    private static byte[] DecodeBase64Url(string input)
    {
        string base64 = input.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 0:
                break;
            case 2:
                base64 += "==";
                break;
            case 3:
                base64 += "=";
                break;
            default:
                throw new FormatException("Base64Url 长度非法（余数为 1）");
        }

        return Convert.FromBase64String(base64);
    }
}

using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Daedalus.Tools.Oedipus;

/// <summary>解码方式。</summary>
/// <param name="Id">方式 id，如 "base64"、"jwt"，全小写（设置文件以此持久化）。</param>
/// <param name="DisplayName">界面显示名。</param>
public sealed record OedipusDecoding(string Id, string DisplayName);

/// <summary>一次解码操作的结果。</summary>
/// <param name="Success">操作是否成功。</param>
/// <param name="StatusText">状态栏展示文本：成功为结果摘要，失败为错误信息。</param>
/// <param name="Output">成功时的输出文本；失败时为 null（输出区保持不变）。</param>
public sealed record OedipusOperationResult(bool Success, string StatusText, string? Output);

/// <summary>
/// Oedipus 的操作编排（oedipus.md §5，非 UI 可测）：解码方式清单与 Base64 / URL / XML 实体 / JWT
/// 解码实现，输入非法（格式错误、非合法 UTF-8、JWT 段数/JSON 不符）收敛为错误状态文本，不向界面抛异常。
/// </summary>
public static class OedipusOperations
{
    /// <summary>Base64 解码方式 id。</summary>
    public const string Base64Id = "base64";

    /// <summary>URL 解码方式 id。</summary>
    public const string UrlId = "url";

    /// <summary>XML 实体解码方式 id。</summary>
    public const string XmlId = "xml";

    /// <summary>JWT 解码方式 id。</summary>
    public const string JwtId = "jwt";

    /// <summary>全部解码方式（顺序即界面下拉顺序，首项为默认选中）。</summary>
    public static IReadOnlyList<OedipusDecoding> Decodings { get; } =
    [
        new OedipusDecoding(Base64Id, "Base64"),
        new OedipusDecoding(UrlId, "URL"),
        new OedipusDecoding(XmlId, "XML 实体"),
        new OedipusDecoding(JwtId, "JWT"),
    ];

    // 严格 UTF-8：非法字节序列抛 DecoderFallbackException，按输入错误收敛而非静默替换出乱码
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    // JWT 段的美化输出：缩进 2；中文不转义（UnsafeRelaxedJsonEscaping），claim 值可读性优先
    private static readonly JsonSerializerOptions JwtJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>按指定方式解码：Base64 为 UTF-8 文本还原；URL 为 <see cref="Uri.UnescapeDataString"/>；XML 实体为 <see cref="WebUtility.HtmlDecode"/>；JWT 见 <see cref="DecodeJwt"/>。</summary>
    public static OedipusOperationResult Decode(OedipusDecoding decoding, string input)
    {
        ArgumentNullException.ThrowIfNull(decoding);
        ArgumentNullException.ThrowIfNull(input);

        try
        {
            string output = decoding.Id switch
            {
                Base64Id => DecodeBase64(input),
                UrlId => Uri.UnescapeDataString(input),
                // 选用 HtmlDecode 的理由：XML 的五个预定义实体与数字实体（&#NN; / &#xHH;）
                // 都是 HTML 实体的子集，BCL 没有独立的 XML 实体解码 API
                XmlId => WebUtility.HtmlDecode(input),
                JwtId => DecodeJwt(input),
                // 未知 id 属编程错误（方式清单固定），抛出让 App 兜底，不伪装成输入错误
                _ => throw new InvalidOperationException($"未知解码方式：{decoding.Id}"),
            };
            return new OedipusOperationResult(true, $"解码完成（{decoding.DisplayName}）", output);
        }
        catch (UriFormatException ex)
        {
            // UriFormatException 派生自 FormatException，必须先于它捕获；
            // 防御性兜底：UnescapeDataString 对非法输入通常原样返回，但其契约声明该异常
            return new OedipusOperationResult(false, $"解码失败：{ex.Message}", null);
        }
        catch (FormatException ex)
        {
            // Base64 / Base64Url 格式非法（含 JWT 段含非法字符、长度余数为 1）
            return new OedipusOperationResult(false, $"解码失败：输入不是合法的 Base64（{ex.Message}）", null);
        }
        catch (DecoderFallbackException)
        {
            return new OedipusOperationResult(false, "解码失败：解码结果不是合法的 UTF-8 文本", null);
        }
        catch (InvalidOperationException ex) when (decoding.Id == JwtId)
        {
            // JWT 结构性错误（段数不符、段内 JSON 非法）由 DecodeJwt 以 InvalidOperationException 给出可读信息；
            // 未知方式 id 的 InvalidOperationException 因 id 过滤不上钩，照常上抛让 App 兜底
            return new OedipusOperationResult(false, $"解码失败：{ex.Message}", null);
        }
    }

    /// <summary>
    /// 解析启动时的初始解码方式：优先上次选择的方式 id（大小写不敏感），否则取列表第一个（Base64）。
    /// </summary>
    public static OedipusDecoding ResolveInitialDecoding(string? lastDecodingId)
    {
        if (!string.IsNullOrEmpty(lastDecodingId))
        {
            OedipusDecoding? remembered = Decodings.FirstOrDefault(
                d => string.Equals(d.Id, lastDecodingId, StringComparison.OrdinalIgnoreCase));
            if (remembered is not null)
            {
                return remembered;
            }
        }

        return Decodings[0];
    }

    /// <summary>Base64 解码：字节序列按严格 UTF-8 还原为文本，非法字节抛 DecoderFallbackException。</summary>
    private static string DecodeBase64(string input)
    {
        byte[] bytes = Convert.FromBase64String(input);
        return StrictUtf8.GetString(bytes);
    }

    /// <summary>
    /// JWT 解码（oedipus.md §5）：按 '.' 拆三段（header.payload.signature），header 与 payload
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

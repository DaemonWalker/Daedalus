using System.Text;

namespace Daedalus.Tools.Cadmus;

/// <summary>编码方式。</summary>
/// <param name="Id">方式 id，如 "base64"、"url"，全小写（设置文件以此持久化）。</param>
/// <param name="DisplayName">界面显示名。</param>
public sealed record CadmusEncoding(string Id, string DisplayName);

/// <summary>一次编码操作的结果。</summary>
/// <param name="Success">操作是否成功。</param>
/// <param name="StatusText">状态栏展示文本：成功为结果摘要，失败为错误信息。</param>
/// <param name="Output">成功时的输出文本；失败时为 null（输出区保持不变）。</param>
public sealed record CadmusOperationResult(bool Success, string StatusText, string? Output);

/// <summary>
/// Cadmus 的操作编排（cadmus.md §5，非 UI 可测）：编码方式清单与 Base64 / URL 编码实现，
/// 输入非法（孤立代理项等）收敛为错误状态文本，不向界面抛异常。
/// </summary>
public static class CadmusOperations
{
    /// <summary>Base64 编码方式 id。</summary>
    public const string Base64Id = "base64";

    /// <summary>URL 编码方式 id。</summary>
    public const string UrlId = "url";

    /// <summary>全部编码方式（顺序即界面下拉顺序，首项为默认选中）。</summary>
    public static IReadOnlyList<CadmusEncoding> Encodings { get; } =
    [
        new CadmusEncoding(Base64Id, "Base64"),
        new CadmusEncoding(UrlId, "URL"),
    ];

    /// <summary>按指定方式编码：Base64 为 UTF-8 字节序列的 Base64；URL 为 <see cref="Uri.EscapeDataString"/>。</summary>
    public static CadmusOperationResult Encode(CadmusEncoding encoding, string input)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        ArgumentNullException.ThrowIfNull(input);

        try
        {
            string output = encoding.Id switch
            {
                Base64Id => Convert.ToBase64String(Encoding.UTF8.GetBytes(input)),
                UrlId => Uri.EscapeDataString(input),
                // 未知 id 属编程错误（方式清单固定），抛出让 App 兜底，不伪装成输入错误
                _ => throw new InvalidOperationException($"未知编码方式：{encoding.Id}"),
            };
            return new CadmusOperationResult(true, $"编码完成（{encoding.DisplayName}）", output);
        }
        catch (UriFormatException ex)
        {
            // 防御性兜底：.NET 现行行为下孤立代理项会按替换字符编码、不抛异常，
            // 但 EscapeDataString 的契约仍声明 UriFormatException，按输入错误收敛而不上抛
            return new CadmusOperationResult(false, $"编码失败：{ex.Message}", null);
        }
    }

    /// <summary>
    /// 解析启动时的初始编码方式：优先上次选择的方式 id（大小写不敏感），否则取列表第一个（Base64）。
    /// </summary>
    public static CadmusEncoding ResolveInitialEncoding(string? lastEncodingId)
    {
        if (!string.IsNullOrEmpty(lastEncodingId))
        {
            CadmusEncoding? remembered = Encodings.FirstOrDefault(
                e => string.Equals(e.Id, lastEncodingId, StringComparison.OrdinalIgnoreCase));
            if (remembered is not null)
            {
                return remembered;
            }
        }

        return Encodings[0];
    }
}

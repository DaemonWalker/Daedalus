using Daedalus.Abstractions;

namespace Daedalus.Tools.Hermes.Response;

/// <summary>响应体美化结果。</summary>
/// <param name="Text">展示文本（美化后的或原文）。</param>
/// <param name="FormatId">匹配到的格式 id；未匹配到映射时为 null。</param>
/// <param name="Beautified">true 表示实际完成了美化；格式化器缺失或内容非法时为 false（退化为纯文本，不报错，hermes.md §8）。</param>
public sealed record BeautifyResult(string Text, string? FormatId, bool Beautified);

/// <summary>
/// 响应体美化（hermes.md §8，FR-HERMES-004）：按 Content-Type 映射格式 id，经
/// <see cref="IToolHost.FindFormatter"/> 取格式化器美化；格式化器未安装或内容非法时退化为纯文本。
/// </summary>
public sealed class ResponseBeautifier(IToolHost host)
{
    // 响应美化的缩进宽度固定为 2，不占用设置项
    private static readonly FormatOptions Options = new(Minify: false, IndentSize: 2);

    /// <summary>美化响应体；<paramref name="contentType"/> 为响应的 Content-Type 头值（可为 null）。</summary>
    public BeautifyResult Beautify(string body, string? contentType)
    {
        ArgumentNullException.ThrowIfNull(body);
        string? formatId = ContentTypeFormatMapper.Map(contentType);
        if (formatId is null)
        {
            return new BeautifyResult(body, null, false);
        }

        IFormatter? formatter = host.FindFormatter(formatId);
        if (formatter is null)
        {
            return new BeautifyResult(body, formatId, false);
        }

        try
        {
            return new BeautifyResult(formatter.Format(body, Options), formatId, true);
        }
        catch (FormatException)
        {
            // 响应体内容非法（如服务端声称 json 实际不是）：退化为纯文本展示，不报错
            return new BeautifyResult(body, formatId, false);
        }
    }
}

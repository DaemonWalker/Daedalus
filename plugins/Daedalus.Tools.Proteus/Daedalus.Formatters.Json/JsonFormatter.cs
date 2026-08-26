using System.Buffers;
using System.Text;
using System.Text.Json;

using Daedalus.Abstractions;

namespace Daedalus.Formatters.Json;

/// <summary>
/// JSON 格式化器插件（设计文档：docs/plugins/proteus/json.md）。
/// 基于 <see cref="JsonDocument"/> 实现校验（含行列）、美化（缩进可配）与压缩；
/// 严格 JSON：不接受注释与尾随逗号。
/// </summary>
public sealed class JsonFormatter : IFormatter
{
    // 注释/尾随逗号保持默认严格拒绝；深度上限放宽为不限制
    // （默认 64 层对工具场景偏小；JsonDocument 是迭代式解析，深嵌套不会栈溢出）
    // 注意：JsonDocumentOptions.MaxDepth 与 JsonSerializerOptions 不同，0 不代表无限制，需显式给大值
    private static readonly JsonDocumentOptions DocumentOptions = new() { MaxDepth = int.MaxValue };
    /// <inheritdoc />
    public string FormatId => "json";

    /// <inheritdoc />
    public string DisplayName => "JSON";

    /// <inheritdoc />
    public bool TryValidate(string input, out string? error)
    {
        try
        {
            using (JsonDocument.Parse(input, DocumentOptions))
            {
            }

            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            error = BuildErrorMessage(input, ex);
            return false;
        }
    }

    /// <inheritdoc />
    /// <exception cref="FormatException">输入不是合法 JSON，消息中含行列信息。</exception>
    public string Format(string input, FormatOptions options)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(input, DocumentOptions);
        }
        catch (JsonException ex)
        {
            throw new FormatException(BuildErrorMessage(input, ex), ex);
        }

        using (document)
        {
            // 直接用 Utf8JsonWriter 重写：无序列化器的深度限制，缩进宽度可直接配置（.NET 9+）
            var writerOptions = new JsonWriterOptions
            {
                Indented = !options.Minify,
                IndentSize = options.IndentSize,
            };
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer, writerOptions))
            {
                document.RootElement.WriteTo(writer);
            }

            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }
    }

    private static string BuildErrorMessage(string input, JsonException ex)
    {
        (int line, int column) = GetPosition(input, ex);
        return $"第 {line} 行第 {column} 列：{ex.Message}";
    }

    // JsonException 的行列均为 0 起始，且列是行内 UTF-8 字节位置；
    // 对用户展示时换算为 1 起始的行号与字符列
    private static (int Line, int Column) GetPosition(string input, JsonException ex)
    {
        int lineIndex = (int)(ex.LineNumber ?? 0);
        long bytePosition = ex.BytePositionInLine ?? 0;

        string lineText = GetLine(input, lineIndex);
        int column = 1;
        int byteCount = 0;
        foreach (Rune rune in lineText.EnumerateRunes())
        {
            if (byteCount >= bytePosition)
            {
                break;
            }

            byteCount += rune.Utf8SequenceLength;
            column++;
        }

        return (lineIndex + 1, column);
    }

    private static string GetLine(string input, int lineIndex)
    {
        int current = 0;
        foreach (ReadOnlySpan<char> lineSpan in input.AsSpan().EnumerateLines())
        {
            if (current == lineIndex)
            {
                return lineSpan.ToString();
            }

            current++;
        }

        return string.Empty;
    }
}

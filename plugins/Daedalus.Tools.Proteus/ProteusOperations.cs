using Daedalus.Abstractions;

namespace Daedalus.Tools.Proteus;

/// <summary>一次格式化/压缩/校验操作的结果。</summary>
/// <param name="Success">操作是否成功。</param>
/// <param name="StatusText">状态栏展示文本：成功为结果摘要，失败为错误信息（含行列，由格式化器给出）。</param>
/// <param name="Output">成功时的新输出文本；失败或校验操作时为 null（输出区保持不变，proteus.md §5）。</param>
public sealed record ProteusOperationResult(bool Success, string StatusText, string? Output);

/// <summary>
/// Proteus 的操作编排（proteus.md §5/§7，非 UI 可测）：把界面动作翻译为对
/// <see cref="IFormatter"/> 的调用，并统一错误处理——Format 抛 <see cref="FormatException"/>
/// 时按校验失败处理，不产生输出。
/// </summary>
public static class ProteusOperations
{
    /// <summary>格式化（美化）：缩进宽度为 <paramref name="indentSize"/>。</summary>
    public static ProteusOperationResult Format(IFormatter formatter, string input, int indentSize)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        return FormatCore(formatter, input, new FormatOptions(Minify: false, IndentSize: indentSize), "格式化");
    }

    /// <summary>压缩。</summary>
    public static ProteusOperationResult Minify(IFormatter formatter, string input)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        return FormatCore(formatter, input, new FormatOptions(Minify: true, IndentSize: 0), "压缩");
    }

    /// <summary>校验：不改输出区，结果只体现在状态栏（FR-PROTEUS-002）。</summary>
    public static ProteusOperationResult Validate(IFormatter formatter, string input)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        return formatter.TryValidate(input, out string? error)
            ? new ProteusOperationResult(true, $"校验通过（{formatter.DisplayName}）", null)
            : new ProteusOperationResult(false, $"校验失败：{error}", null);
    }

    /// <summary>
    /// 解析启动时的初始格式：优先上次选择的格式 id（大小写不敏感），否则取列表第一个；
    /// 未安装任何格式化器时返回 null（界面据此禁用操作按钮）。
    /// </summary>
    public static IFormatter? ResolveInitialFormatter(IReadOnlyList<IFormatter> formatters, string? lastFormatId)
    {
        ArgumentNullException.ThrowIfNull(formatters);
        if (formatters.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(lastFormatId))
        {
            IFormatter? remembered = formatters.FirstOrDefault(
                f => string.Equals(f.FormatId, lastFormatId, StringComparison.OrdinalIgnoreCase));
            if (remembered is not null)
            {
                return remembered;
            }
        }

        return formatters[0];
    }

    private static ProteusOperationResult FormatCore(
        IFormatter formatter, string input, FormatOptions options, string operationName)
    {
        try
        {
            string output = formatter.Format(input, options);
            return new ProteusOperationResult(true, $"{operationName}完成（{formatter.DisplayName}）", output);
        }
        catch (FormatException ex)
        {
            // 契约约定输入非法只抛 FormatException；其余异常视为插件 bug，向上抛给 App 兜底
            return new ProteusOperationResult(false, $"{operationName}失败：{ex.Message}", null);
        }
    }
}

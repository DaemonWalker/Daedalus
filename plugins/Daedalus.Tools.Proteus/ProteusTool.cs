using System.Windows.Forms;

using Daedalus.Abstractions;

namespace Daedalus.Tools.Proteus;

/// <summary>
/// Proteus（变形之神）工具插件（docs/plugins/proteus.md）：文本格式化/压缩/校验。
/// 支持的格式完全由 <see cref="IFormatter"/> 插件提供，工具本体不内置任何格式（FR-PROTEUS-003）。
/// </summary>
public sealed class ProteusTool : ITool
{
    /// <summary>工具 id（数据目录、日志上下文均以此标识）。</summary>
    internal const string ToolId = "daedalus.tools.proteus";

    /// <inheritdoc />
    public ToolMetadata Metadata { get; } = new(
        ToolId,
        "Proteus 格式化",
        "文本格式化工具：美化、压缩、校验，格式由格式化器插件提供",
        new Version(1, 0, 0));

    /// <inheritdoc />
    public Control CreateView(IToolHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return new ProteusPanel(host);
    }
}

using System.Windows.Forms;

using Daedalus.Abstractions;

using Microsoft.Extensions.DependencyInjection;

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

    /// <summary>注册约定（proteus.md §4.1）：视图树为 transient，每次开标签页在 scope 内解析新实例。</summary>
    public void RegisterServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // 数据目录经预置的 IToolHost 解析
        services.AddTransient(sp => new ProteusSettingsStore(
            sp.GetRequiredService<IToolHost>().GetDataDirectory(ToolId)));
        services.AddTransient<ProteusPanel>();
    }

    /// <inheritdoc />
    public Control CreateView(IToolHost host, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(services);

        // scope 与标签页同生灭：disposable transient 由容器持有至 scope 释放（ITool 契约约定）
        IServiceScope scope = services.CreateScope();
        try
        {
            var panel = scope.ServiceProvider.GetRequiredService<ProteusPanel>();
            panel.Disposed += (_, _) => scope.Dispose();
            return panel;
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }
}

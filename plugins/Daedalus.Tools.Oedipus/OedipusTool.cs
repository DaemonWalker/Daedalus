using System.Windows.Forms;

using Daedalus.Abstractions;

using Microsoft.Extensions.DependencyInjection;

namespace Daedalus.Tools.Oedipus;

/// <summary>
/// Oedipus（俄狄浦斯，解开斯芬克斯之谜——解码/解读）工具插件（docs/plugins/oedipus.md）：
/// Base64 / URL / XML 实体 / JWT 解码。解码方式内置于工具本体，不依赖格式化器插件。
/// </summary>
public sealed class OedipusTool : ITool
{
    /// <summary>工具 id（数据目录、日志上下文均以此标识）。</summary>
    internal const string ToolId = "daedalus.tools.oedipus";

    /// <inheritdoc />
    public ToolMetadata Metadata { get; } = new(
        ToolId,
        "Oedipus 解码",
        "解码工具：Base64 / URL / XML 实体 / JWT 解码",
        new Version(1, 0, 0));

    /// <summary>注册约定（oedipus.md §4.1）：视图树为 transient，每次开标签页在 scope 内解析新实例。</summary>
    public void RegisterServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // 数据目录经预置的 IToolHost 解析
        services.AddTransient(sp => new OedipusSettingsStore(
            sp.GetRequiredService<IToolHost>().GetDataDirectory(ToolId)));
        services.AddTransient<OedipusPanel>();
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
            var panel = scope.ServiceProvider.GetRequiredService<OedipusPanel>();
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

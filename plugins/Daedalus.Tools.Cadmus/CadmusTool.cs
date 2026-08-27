using System.Windows.Forms;

using Daedalus.Abstractions;

using Microsoft.Extensions.DependencyInjection;

namespace Daedalus.Tools.Cadmus;

/// <summary>
/// Cadmus（卡德摩斯，将腓尼基字母传入希腊——编码/书写之神）工具插件（docs/plugins/cadmus.md）：
/// Base64 / URL 编码。编码方式内置于工具本体，不依赖格式化器插件。
/// </summary>
public sealed class CadmusTool : ITool
{
    /// <summary>工具 id（数据目录、日志上下文均以此标识）。</summary>
    internal const string ToolId = "daedalus.tools.cadmus";

    /// <inheritdoc />
    public ToolMetadata Metadata { get; } = new(
        ToolId,
        "Cadmus 编码",
        "编码工具：Base64 / URL 编码",
        new Version(1, 0, 0));

    /// <summary>注册约定（cadmus.md §4.1）：视图树为 transient，每次开标签页在 scope 内解析新实例。</summary>
    public void RegisterServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // 数据目录经预置的 IToolHost 解析
        services.AddTransient(sp => new CadmusSettingsStore(
            sp.GetRequiredService<IToolHost>().GetDataDirectory(ToolId)));
        services.AddTransient<CadmusPanel>();
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
            var panel = scope.ServiceProvider.GetRequiredService<CadmusPanel>();
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

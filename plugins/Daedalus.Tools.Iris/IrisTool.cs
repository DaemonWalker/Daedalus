using System.Windows.Forms;

using Daedalus.Abstractions;

using Microsoft.Extensions.DependencyInjection;

namespace Daedalus.Tools.Iris;

/// <summary>
/// Iris（伊里斯，众神信使——承载信息的编码与密文传递）工具插件（docs/plugins/iris.md）：
/// Base64 / URL 编码，Base64 / URL / XML 实体 / JWT 解码，AES / RSA 加解密。
/// 方式内置于工具本体，不依赖格式化器插件。承继并取代 Cadmus / Oedipus。
/// </summary>
public sealed class IrisTool : ITool
{
    /// <summary>工具 id（数据目录、日志上下文均以此标识）。</summary>
    internal const string ToolId = "daedalus.tools.iris";

    /// <inheritdoc />
    public ToolMetadata Metadata { get; } = new(
        ToolId,
        "Iris 编码与加密",
        "编码/解码/加解密一体工具：Base64 / URL 编码，Base64 / URL / XML 实体 / JWT 解码，AES / RSA 加解密",
        new Version(1, 0, 0));

    /// <summary>注册约定（iris.md §4.1）：视图树为 transient，每次开标签页在 scope 内解析新实例。</summary>
    public void RegisterServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // 数据目录经预置的 IToolHost 解析
        services.AddTransient(sp => new IrisSettingsStore(
            sp.GetRequiredService<IToolHost>().GetDataDirectory(ToolId)));
        services.AddTransient<IrisPanel>();
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
            var panel = scope.ServiceProvider.GetRequiredService<IrisPanel>();
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

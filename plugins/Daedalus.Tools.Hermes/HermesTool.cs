using System.Windows.Forms;

using Daedalus.Abstractions;
using Daedalus.Tools.Hermes.Collections;
using Daedalus.Tools.Hermes.Editing;
using Daedalus.Tools.Hermes.History;
using Daedalus.Tools.Hermes.Http;
using Daedalus.Tools.Hermes.Response;
using Daedalus.Tools.Hermes.Scripting;
using Daedalus.Tools.Hermes.Settings;
using Daedalus.Tools.Hermes.Variables;
using Daedalus.Tools.Hermes.View;

using Microsoft.Extensions.DependencyInjection;

using Serilog;

namespace Daedalus.Tools.Hermes;

/// <summary>
/// Hermes（神使）工具插件（docs/plugins/hermes.md）：HTTP 客户端——请求编辑与发送、
/// 重定向跳转链、集合管理、环境变量、历史记录。
/// </summary>
public sealed class HermesTool : ITool
{
    /// <summary>工具 id（数据目录、日志上下文均以此标识）。</summary>
    internal const string ToolId = "daedalus.tools.hermes";

    /// <inheritdoc />
    public ToolMetadata Metadata { get; } = new(
        ToolId,
        "Hermes HTTP 客户端",
        "HTTP 调试工具：请求编辑与发送、重定向跳转链、集合、环境变量、历史记录",
        new Version(1, 0, 0));

    /// <summary>
    /// 注册约定（hermes.md §4.1）：引擎/编排/工厂与各 Store 为 singleton——工具容器随进程存活，
    /// singleton 即跨标签页共享（Cookie 会话与双 client 缓存的"浏览器会话"语义，hermes.md §5.2）；
    /// 视图树为 transient，每次开标签页在 scope 内解析新实例。
    /// </summary>
    public void RegisterServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // 跨标签共享：HttpEngine/SendOrchestrator 的 ILogger 由容器注入宿主预置实例（带 hermes 上下文）
        services.AddSingleton<HttpClientFactory>();
        services.AddSingleton<HttpEngine>();
        services.AddSingleton<SendOrchestrator>();

        // 数据目录经预置的 IToolHost 解析；各 Store 同为跨标签共享（同一目录下的读写由 Store 自身容错）
        services.AddSingleton(sp => new CollectionStore(GetDataDirectory(sp)));
        services.AddSingleton(sp => new EnvironmentStore(GetDataDirectory(sp)));
        services.AddSingleton(sp => new HistoryStore(GetDataDirectory(sp)));
        services.AddSingleton(sp => new HermesSettingsStore(GetDataDirectory(sp), sp.GetRequiredService<ILogger>()));
        services.AddSingleton(sp => new HistoryArchive(GetDataDirectory(sp), sp.GetRequiredService<ILogger>()));
        services.AddSingleton(sp => new HistorySearch(GetDataDirectory(sp), sp.GetRequiredService<ILogger>()));
        services.AddSingleton<RecentHistoryReader>();

        // 视图树（transient）：每次开标签页解析新实例；RequestEditorPanel 依赖运行时委托，由 HermesPanel 手工构造
        services.AddTransient<ScriptHost>();
        services.AddTransient<ResponseBeautifier>();
        services.AddTransient<CollectionPanel>();
        services.AddTransient<HistoryPanel>();
        services.AddTransient<ResponsePanel>();
        services.AddTransient<HermesPanel>();
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
            var panel = scope.ServiceProvider.GetRequiredService<HermesPanel>();
            panel.Disposed += (_, _) => scope.Dispose();
            return panel;
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    private static string GetDataDirectory(IServiceProvider services) =>
        services.GetRequiredService<IToolHost>().GetDataDirectory(ToolId);
}

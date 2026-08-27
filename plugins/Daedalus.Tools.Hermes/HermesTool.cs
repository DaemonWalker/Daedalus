using System.Windows.Forms;

using Daedalus.Abstractions;
using Daedalus.Tools.Hermes.Editing;
using Daedalus.Tools.Hermes.Http;
using Daedalus.Tools.Hermes.View;

namespace Daedalus.Tools.Hermes;

/// <summary>
/// Hermes（神使）工具插件（docs/plugins/hermes.md）：HTTP 客户端——请求编辑与发送、
/// 重定向跳转链、集合管理、环境变量、历史记录。
/// </summary>
public sealed class HermesTool : ITool
{
    /// <summary>工具 id（数据目录、日志上下文均以此标识）。</summary>
    internal const string ToolId = "daedalus.tools.hermes";

    // Cookie 会话与双 client 缓存为工具生命周期共享（hermes.md §5.2"浏览器会话"），
    // 进程退出时随宿主回收，ITool 契约无卸载钩子，不主动 Dispose
    private readonly HttpClientFactory _clientFactory = new();
    private HttpEngine? _engine;
    private SendOrchestrator? _orchestrator;

    /// <inheritdoc />
    public ToolMetadata Metadata { get; } = new(
        ToolId,
        "Hermes HTTP 客户端",
        "HTTP 调试工具：请求编辑与发送、重定向跳转链、集合、环境变量、历史记录",
        new Version(1, 0, 0));

    /// <summary>共享 HTTP 引擎（双 client 缓存 + 共享 CookieContainer）。</summary>
    internal HttpEngine Engine => _engine ??= new HttpEngine(_clientFactory);

    /// <summary>共享发送编排（变量替换 → 引擎 → 历史组装）。</summary>
    internal SendOrchestrator Orchestrator => _orchestrator ??= new SendOrchestrator(Engine);

    /// <summary>共享 HTTP client 工厂（设置面板的"忽略证书校验"开关经它重建 client）。</summary>
    internal HttpClientFactory ClientFactory => _clientFactory;

    /// <inheritdoc />
    public Control CreateView(IToolHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        // 引擎与编排为工具生命周期共享：首次开标签页时注入插件日志器（Debug 逐跳/变量替换日志）
        Serilog.ILogger logger = host.GetLogger(ToolId);
        _engine ??= new HttpEngine(_clientFactory, logger);
        _orchestrator ??= new SendOrchestrator(Engine, logger);
        return new HermesPanel(host, this);
    }
}

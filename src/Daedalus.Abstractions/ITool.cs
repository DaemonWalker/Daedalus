using System.Windows.Forms;

using Microsoft.Extensions.DependencyInjection;

namespace Daedalus.Abstractions;

/// <summary>工具插件：拥有主界面的功能模块，如 Hermes（HTTP 客户端）、Proteus（格式化工具）。</summary>
public interface ITool
{
    /// <summary>工具元数据。</summary>
    ToolMetadata Metadata { get; }

    /// <summary>
    /// 向本工具的独立服务集合注册插件内部服务（架构 §6：每个工具拥有独立 ServiceProvider，
    /// 宿主已预置 <see cref="IToolHost"/> 与按插件 id 打好 SourceContext 的 Serilog.ILogger 实例）。
    /// 生命周期约定：跨标签页共享的服务（引擎、Store 等）注册 singleton（工具容器随进程存活）；
    /// 视图树注册 transient；"标签页内共享、标签页间隔离"的状态才用 scoped。
    /// 按需弹出的对话框不要注册，需要注入时用 ActivatorUtilities.CreateInstance。
    /// </summary>
    /// <param name="services">本工具的服务集合（已含宿主预置实例）。</param>
    void RegisterServices(IServiceCollection services);

    /// <summary>
    /// 创建工具主面板，由主窗口以标签页承载。每次打开标签页调用一次，每次调用返回新实例。
    /// 实现约定：先 <c>services.CreateScope()</c>，从 scope 解析根面板；面板 Disposed 时释放 scope
    /// （disposable transient 由容器持有至 scope 释放，scope 必须与标签页同生灭）。
    /// </summary>
    /// <param name="host">主程序提供的宿主服务（同一份实例也已预置在 <paramref name="services"/> 中）。</param>
    /// <param name="services">本工具的 ServiceProvider（由 <see cref="RegisterServices"/> 注册的内容构建）。</param>
    Control CreateView(IToolHost host, IServiceProvider services);
}

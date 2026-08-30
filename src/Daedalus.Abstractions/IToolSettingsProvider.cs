using System.Windows.Forms;

namespace Daedalus.Abstractions;

/// <summary>
/// 可选能力：工具提供设置页，由宿主设置窗口以标签页承载（与工具主面板同属一个工具容器）。
/// 由 <see cref="ITool"/> 实现类额外实现；工具没有可配置项时不实现本接口，设置窗口不显示对应标签页。
/// </summary>
public interface IToolSettingsProvider
{
    /// <summary>
    /// 创建工具设置页控件，每次打开设置窗口调用一次，每次调用返回新实例。
    /// 生命周期约定同 <see cref="ITool.CreateView"/>：实现内 <c>services.CreateScope()</c>，
    /// 控件 Disposed 时释放 scope（scope 必须与设置窗口同生灭）。
    /// </summary>
    /// <param name="host">主程序提供的宿主服务（同一份实例也已预置在 <paramref name="services"/> 中）。</param>
    /// <param name="services">本工具的 ServiceProvider（由 <see cref="ITool.RegisterServices"/> 注册的内容构建）。</param>
    Control CreateSettingsView(IToolHost host, IServiceProvider services);
}

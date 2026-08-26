using System.Windows.Forms;

namespace Daedalus.Abstractions;

/// <summary>工具插件：拥有主界面的功能模块，如 Hermes（HTTP 客户端）、Proteus（格式化工具）。</summary>
public interface ITool
{
    /// <summary>工具元数据。</summary>
    ToolMetadata Metadata { get; }

    /// <summary>创建工具主面板，由主窗口以标签页承载。每次打开标签页调用一次，每次调用返回新实例。</summary>
    /// <param name="host">主程序提供的宿主服务。</param>
    Control CreateView(IToolHost host);
}

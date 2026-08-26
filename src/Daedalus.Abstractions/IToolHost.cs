using Serilog;

namespace Daedalus.Abstractions;

/// <summary>主程序提供给插件的宿主服务。</summary>
public interface IToolHost
{
    /// <summary>获取指定工具的数据目录（data/&lt;toolId&gt;/），不存在则创建。</summary>
    string GetDataDirectory(string toolId);

    /// <summary>按插件 id 获取日志器（带插件 id 上下文）。插件禁止自行创建日志管道。</summary>
    ILogger GetLogger(string pluginId);

    /// <summary>全部已安装的格式化器。</summary>
    IReadOnlyList<IFormatter> Formatters { get; }

    /// <summary>按格式 id 查找已安装的格式化器，未安装返回 null。</summary>
    IFormatter? FindFormatter(string formatId);
}

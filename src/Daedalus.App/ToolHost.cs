using Daedalus.Abstractions;

using Serilog;
using Serilog.Core;

namespace Daedalus.App;

/// <summary>
/// <see cref="IToolHost"/> 实现（架构 §6）：向插件提供数据目录分配、日志器工厂与格式化器查询。
/// </summary>
public sealed class ToolHost : IToolHost
{
    private readonly string _dataRootDirectory;
    private readonly ILogger _logger;

    /// <param name="baseDirectory">程序目录，数据目录根为其下的 data/。</param>
    /// <param name="logger">宿主根日志器，插件日志器经 <c>ForContext(SourceContext, 插件id)</c> 派生。</param>
    /// <param name="formatters">Hosting 加载出的格式化器表。</param>
    public ToolHost(string baseDirectory, ILogger logger, IReadOnlyList<IFormatter> formatters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(formatters);
        _dataRootDirectory = Path.Combine(baseDirectory, "data");
        _logger = logger;
        Formatters = formatters;
    }

    /// <inheritdoc/>
    public IReadOnlyList<IFormatter> Formatters { get; }

    /// <inheritdoc/>
    public string GetDataDirectory(string toolId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolId);
        string directory = Path.Combine(_dataRootDirectory, toolId);
        Directory.CreateDirectory(directory);
        _logger.Debug("为插件 {PluginId} 分配数据目录 {Directory}", toolId, directory);
        return directory;
    }

    /// <inheritdoc/>
    public ILogger GetLogger(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        // 必须用 SourceContext 承载插件 id：daedalus.json 的 logging.overrides 经
        // Serilog MinimumLevel.Override 按 SourceContext 前缀匹配生效（架构 §6.2）。
        // 因此插件侧不得再用 ForContext<T>() 覆盖该属性（规范 §7）
        _logger.Debug("为插件 {PluginId} 创建日志器", pluginId);
        return _logger.ForContext(Constants.SourceContextPropertyName, pluginId);
    }

    /// <inheritdoc/>
    public IFormatter? FindFormatter(string formatId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formatId);
        return Formatters.FirstOrDefault(f => string.Equals(f.FormatId, formatId, StringComparison.OrdinalIgnoreCase));
    }
}

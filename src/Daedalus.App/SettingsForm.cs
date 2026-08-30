using Daedalus.Abstractions;
using Daedalus.Hosting;

using Serilog;

namespace Daedalus.App;

/// <summary>
/// 统一设置窗口（FR-SHELL-006）：模态对话框 + 标签页。第一个标签页为全局设置（日志级别），
/// 其后每个实现可选契约 <see cref="IToolSettingsProvider"/> 的工具一个标签页。
/// 设置页与工具主面板同属一个工具容器，经 <see cref="IToolSettingsProvider.CreateSettingsView"/> 创建；
/// 单个工具设置页创建失败只影响该标签页，不影响窗口其余部分（FR-SHELL-004 同款隔离）。
/// </summary>
internal sealed class SettingsForm : Form
{
    public SettingsForm(
        string configFilePath,
        LoggingSettings loggingSettings,
        PluginCatalog catalog,
        IToolHost host,
        ToolContainerRegistry containers,
        ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configFilePath);
        ArgumentNullException.ThrowIfNull(loggingSettings);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(containers);
        ArgumentNullException.ThrowIfNull(logger);

        Text = "设置";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(560, 420);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        AddTab(tabs, "全局", new GlobalSettingsPanel(configFilePath, loggingSettings, catalog.Tools, logger));
        foreach (ITool tool in catalog.Tools)
        {
            if (tool is IToolSettingsProvider provider)
            {
                AddTab(tabs, tool.Metadata.DisplayName, CreateToolSettingsView(tool, provider, host, containers, logger));
            }
        }

        Controls.Add(tabs);

        // 高 DPI 适配：标签页内容在构造函数内全部建好后随窗口整体缩放一次（详见 DpiScale）
        DpiScale.Apply(this);
    }

    private static void AddTab(TabControl tabs, string title, Control content)
    {
        content.Dock = DockStyle.Fill;
        var page = new TabPage(title);
        page.Controls.Add(content);
        tabs.TabPages.Add(page);
    }

    private static Control CreateToolSettingsView(
        ITool tool, IToolSettingsProvider provider, IToolHost host, ToolContainerRegistry containers, ILogger logger)
    {
        IServiceProvider? services = containers.Find(tool);
        if (services is null)
        {
            logger.Error("工具 {ToolId} 的服务容器不可用，无法加载设置页", tool.Metadata.Id);
            return ErrorLabel("服务容器构建失败，无法加载该工具的设置页（详见日志）");
        }

        try
        {
            return provider.CreateSettingsView(host, services);
        }
        catch (Exception ex)
        {
            // 单个工具设置页失败不拖垮整个设置窗口
            logger.Error(ex, "工具 {ToolId} 创建设置页失败", tool.Metadata.Id);
            return ErrorLabel($"设置页加载失败：{ex.Message}");
        }
    }

    private static Label ErrorLabel(string message) =>
        new() { Text = message, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
}

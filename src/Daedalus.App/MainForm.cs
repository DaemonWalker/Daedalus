using Daedalus.Abstractions;
using Daedalus.Hosting;

using Serilog;

namespace Daedalus.App;

/// <summary>
/// 主窗口（工具箱外壳，架构 §6）：顶部菜单栏（工具入口 + 设置入口）+ 标签页容器 + 底部状态栏。
/// 「工具」菜单单击工具名调用 <see cref="ITool.CreateView"/> 开新标签页（FR-SHELL-002），同一工具可开多个、
/// 标签页点 × 或中键关闭（FR-SHELL-003）；「设置」菜单打开统一设置窗口（FR-SHELL-006）；
/// 插件加载失败清单显示在状态栏、点击查看详情（FR-SHELL-004）。
/// </summary>
internal sealed class MainForm : Form
{
    private const int CloseButtonSize = 14;

    private readonly PluginCatalog _catalog;
    private readonly IToolHost _host;
    private readonly ToolContainerRegistry _containers;
    private readonly ILogger _logger;
    private readonly string _configFilePath;
    private readonly LoggingSettings _loggingSettings;
    private readonly TabControl _tabs;

    // 各标签页 × 按钮的命中区域，在 OwnerDraw 时计算；标签页增删后索引位移，需清空重算
    private readonly Dictionary<int, Rectangle> _closeButtonBounds = [];

    public MainForm(
        PluginCatalog catalog,
        IToolHost host,
        ToolContainerRegistry containers,
        ILogger logger,
        string configFilePath,
        LoggingSettings loggingSettings)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(containers);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(configFilePath);
        ArgumentNullException.ThrowIfNull(loggingSettings);
        _catalog = catalog;
        _host = host;
        _containers = containers;
        _logger = logger;
        _configFilePath = configFilePath;
        _loggingSettings = loggingSettings;

        Text = "Daedalus";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1024, 768);
        WindowState = FormWindowState.Maximized;

        _tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            DrawMode = TabDrawMode.OwnerDrawFixed,
            Padding = new Point(20, 4),
        };
        _tabs.DrawItem += OnTabDrawItem;
        _tabs.MouseClick += OnTabMouseClick;
        FormClosing += OnMainFormClosing;

        var toolsMenu = new ToolStripMenuItem("工具(&T)");
        foreach (ITool tool in _catalog.Tools)
        {
            var item = new ToolStripMenuItem(tool.Metadata.DisplayName) { Tag = tool };
            item.Click += (_, _) => OpenTool(tool);
            toolsMenu.DropDownItems.Add(item);
        }

        var settingsMenu = new ToolStripMenuItem("设置(&S)");
        settingsMenu.Click += (_, _) => OpenSettings();

        var menuStrip = new MenuStrip();
        menuStrip.Items.Add(toolsMenu);
        menuStrip.Items.Add(settingsMenu);

        var statusStrip = new StatusStrip();
        var statusLabel = new ToolStripStatusLabel();
        statusStrip.Items.Add(statusLabel);
        if (_catalog.Failures.Count > 0)
        {
            statusLabel.Text = $"{_catalog.Failures.Count} 个插件加载失败（点击查看详情）";
            statusLabel.IsLink = true;
            statusLabel.Click += (_, _) => ShowLoadFailures();
        }
        else
        {
            statusLabel.Text = $"插件加载完成：{_catalog.Tools.Count} 个工具，{_catalog.Formatters.Count} 个格式化器";
        }

        // 停靠顺序与 z-order 相反：后添加的先停靠，故 Fill 的 TabControl 最先加入
        Controls.Add(_tabs);
        Controls.Add(statusStrip);
        Controls.Add(menuStrip);
        MainMenuStrip = menuStrip;

        // 高 DPI 适配（详见 DpiScale）
        DpiScale.Apply(this);
    }

    private void OpenTool(ITool tool)
    {
        // 容器构建失败的工具仍在菜单中可见，打开时按打开失败处理（提示与 CreateView 异常路径一致）
        IServiceProvider? services = _containers.Find(tool);
        if (services is null)
        {
            ToolContainerFailure? failure = _containers.Failures.FirstOrDefault(f => f.ToolId == tool.Metadata.Id);
            _logger.Error("工具 {ToolId} 的服务容器不可用（注册/构建失败），无法打开", tool.Metadata.Id);
            MessageBox.Show(
                this,
                $"打开工具「{tool.Metadata.DisplayName}」失败：服务容器构建失败（{failure?.Exception.Message ?? "未知原因"}），详见日志。",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        Control view;
        try
        {
            view = tool.CreateView(_host, services);
        }
        catch (Exception ex)
        {
            // 插件边界兜底（架构 §8）：CreateView 抛异常不允许拖垮外壳
            _logger.Error(ex, "工具 {ToolId} 创建主面板失败", tool.Metadata.Id);
            MessageBox.Show(
                this,
                $"打开工具「{tool.Metadata.DisplayName}」失败：{ex.Message}",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        view.Dock = DockStyle.Fill;

        // 高 DPI 适配：宿主对工具视图整体缩放一次（详见 DpiScale；嵌套 UserControl 不自行缩放）
        DpiScale.Apply(view);

        var page = new TabPage(GetTabTitle(tool));
        page.Controls.Add(view);
        _tabs.TabPages.Add(page);
        _tabs.SelectedTab = page;
    }

    /// <summary>打开统一设置窗口（FR-SHELL-006）：全局设置 + 各工具设置页。</summary>
    private void OpenSettings()
    {
        using var form = new SettingsForm(_configFilePath, _loggingSettings, _catalog, _host, _containers, _logger);
        form.ShowDialog(this);
    }

    private string GetTabTitle(ITool tool)
    {
        string title = tool.Metadata.DisplayName;
        int count = _tabs.TabPages.Cast<TabPage>().Count(p => p.Text == title || p.Text.StartsWith(title + " (", StringComparison.Ordinal));
        return count == 0 ? title : $"{title} ({count + 1})";
    }

    private void ShowLoadFailures()
    {
        string details = string.Join(
            Environment.NewLine + Environment.NewLine,
            _catalog.Failures.Select(f => $"{f.DllName}\n{f.Exception.Message}"));
        MessageBox.Show(this, details, "插件加载失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void OnTabDrawItem(object? sender, DrawItemEventArgs e)
    {
        e.DrawBackground();
        Rectangle tabBounds = _tabs.GetTabRect(e.Index);
        var textBounds = new Rectangle(
            tabBounds.X + 6,
            tabBounds.Y,
            tabBounds.Width - CloseButtonSize - 16,
            tabBounds.Height);
        TextRenderer.DrawText(
            e.Graphics,
            _tabs.TabPages[e.Index].Text,
            _tabs.Font,
            textBounds,
            _tabs.ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        var closeBounds = new Rectangle(
            tabBounds.Right - CloseButtonSize - 6,
            tabBounds.Y + (tabBounds.Height - CloseButtonSize) / 2,
            CloseButtonSize,
            CloseButtonSize);
        _closeButtonBounds[e.Index] = closeBounds;
        TextRenderer.DrawText(
            e.Graphics,
            "×",
            _tabs.Font,
            closeBounds,
            Color.Gray,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private void OnTabMouseClick(object? sender, MouseEventArgs e)
    {
        for (int i = 0; i < _tabs.TabPages.Count; i++)
        {
            bool isHit = e.Button == MouseButtons.Middle
                ? _tabs.GetTabRect(i).Contains(e.Location)
                : e.Button == MouseButtons.Left
                    && _closeButtonBounds.TryGetValue(i, out Rectangle bounds)
                    && bounds.Contains(e.Location);
            if (isHit)
            {
                CloseTab(i);
                return;
            }
        }
    }

    private void CloseTab(int index)
    {
        TabPage page = _tabs.TabPages[index];
        if (!ViewConfirmsClose(page))
        {
            return;
        }

        _tabs.TabPages.RemoveAt(index);
        _closeButtonBounds.Clear();
        page.Dispose();
    }

    // 关闭主窗口时逐一咨询各标签页视图（如 Hermes 的未保存提示），任一拒绝则取消关闭
    private void OnMainFormClosing(object? sender, FormClosingEventArgs e)
    {
        foreach (TabPage page in _tabs.TabPages)
        {
            if (!ViewConfirmsClose(page))
            {
                e.Cancel = true;
                return;
            }
        }
    }

    // 视图实现可选契约 IToolCloseConfirmation 时先咨询；未实现视为允许关闭
    private static bool ViewConfirmsClose(TabPage page) =>
        page.Controls.Count == 0
        || page.Controls[0] is not IToolCloseConfirmation confirmation
        || confirmation.ConfirmClose();
}

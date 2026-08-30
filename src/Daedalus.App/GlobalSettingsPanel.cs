using Daedalus.Abstractions;

using Serilog;
using Serilog.Events;

namespace Daedalus.App;

/// <summary>
/// 设置窗口的「全局」页：编辑 daedalus.json 的日志节（架构 §6.2）——全局默认级别 + 按工具 override。
/// 修改即保存；Serilog 管道在启动时按当时配置构建，级别修改重启后生效。
/// </summary>
internal sealed class GlobalSettingsPanel : UserControl
{
    private const string FollowGlobalText = "跟随全局";

    private static readonly LogEventLevel[] Levels =
    [
        LogEventLevel.Verbose, LogEventLevel.Debug, LogEventLevel.Information,
        LogEventLevel.Warning, LogEventLevel.Error, LogEventLevel.Fatal,
    ];

    private readonly string _filePath;
    private readonly ILogger _logger;
    private readonly ComboBox _defaultLevelCombo;
    private readonly Dictionary<string, ComboBox> _overrideCombos = new();

    // 初始化填充期间抑制变更事件，避免把刚读出的配置立即写回
    private bool _suppressEvents = true;

    public GlobalSettingsPanel(string filePath, LoggingSettings settings, IReadOnlyList<ITool> tools, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(logger);
        _filePath = filePath;
        _logger = logger;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10), AutoSize = true };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _defaultLevelCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
        foreach (LogEventLevel level in Levels)
        {
            _defaultLevelCombo.Items.Add(level);
        }

        _defaultLevelCombo.SelectedItem = settings.DefaultLevel;
        layout.Controls.Add(new Label { Text = "全局默认级别", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        layout.Controls.Add(_defaultLevelCombo, 1, 0);

        // override 键约定为插件 id（架构 §6.2）；只列出已安装的工具，卸载工具的残留键在下次保存时自然清掉
        int row = 1;
        foreach (ITool tool in tools)
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
            combo.Items.Add(FollowGlobalText);
            foreach (LogEventLevel level in Levels)
            {
                combo.Items.Add(level);
            }

            if (settings.Overrides.TryGetValue(tool.Metadata.Id, out LogEventLevel overrideLevel))
            {
                combo.SelectedItem = overrideLevel;
            }
            else
            {
                combo.SelectedItem = FollowGlobalText;
            }

            _overrideCombos[tool.Metadata.Id] = combo;
            layout.Controls.Add(
                new Label { Text = $"{tool.Metadata.DisplayName}（{tool.Metadata.Id}）", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            layout.Controls.Add(combo, 1, row);
            row++;
        }

        var hint = new Label { Text = "日志级别修改将在重启后生效。", AutoSize = true, ForeColor = Color.Gray };
        layout.Controls.Add(hint, 0, row);
        layout.SetColumnSpan(hint, 2);

        Controls.Add(layout);

        _defaultLevelCombo.SelectedIndexChanged += OnLevelChanged;
        foreach (ComboBox combo in _overrideCombos.Values)
        {
            combo.SelectedIndexChanged += OnLevelChanged;
        }

        _suppressEvents = false;
    }

    private async void OnLevelChanged(object? sender, EventArgs e)
    {
        // WinForms 事件处理允许 async void（规范 §5），内部必须 try-catch 兜底
        if (_suppressEvents)
        {
            return;
        }

        var overrides = new Dictionary<string, LogEventLevel>();
        foreach ((string toolId, ComboBox combo) in _overrideCombos)
        {
            if (combo.SelectedItem is LogEventLevel level)
            {
                overrides[toolId] = level;
            }
        }

        LogEventLevel defaultLevel = _defaultLevelCombo.SelectedItem is LogEventLevel selected
            ? selected
            : LogEventLevel.Information;
        try
        {
            await LoggingBootstrap.SaveAsync(_filePath, new LoggingSettings(defaultLevel, overrides));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "保存日志配置失败");
            MessageBox.Show(this, $"日志配置保存失败：{ex.Message}", "设置", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

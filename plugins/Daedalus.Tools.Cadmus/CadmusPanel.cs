using System.Windows.Forms;

using Serilog;

namespace Daedalus.Tools.Cadmus;

/// <summary>
/// Cadmus 主面板（cadmus.md §4）：输入/输出左右分栏、编码方式下拉、编码/清空/复制输出、
/// 状态栏展示结果。界面保持薄，操作编排在 <see cref="CadmusOperations"/>。
/// </summary>
internal sealed class CadmusPanel : UserControl
{
    private readonly ILogger _logger;
    private readonly CadmusSettingsStore _settingsStore;

    private readonly ComboBox _encodingCombo;
    private readonly Button _encodeButton;
    private readonly Button _clearButton;
    private readonly Button _copyButton;
    private readonly TextBox _inputBox;
    private readonly TextBox _outputBox;
    private readonly ToolStripStatusLabel _statusLabel;

    // 初始化/加载设置期间抑制选择变化事件，避免把未加载完的状态写回 settings.json
    private bool _suppressEvents = true;
    private CadmusSettings _settings = CadmusSettings.Default;

    /// <summary>
    /// 构造注入（cadmus.md §4.1）：ILogger 为宿主按插件 id 打好 SourceContext 的实例，
    /// 设置 Store 由容器注入。
    /// </summary>
    public CadmusPanel(ILogger logger, CadmusSettingsStore settingsStore)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(settingsStore);
        _logger = logger;
        _settingsStore = settingsStore;

        _encodingCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = nameof(CadmusEncoding.DisplayName) };
        _encodeButton = new Button { Text = "编码" };
        _clearButton = new Button { Text = "清空" };
        _copyButton = new Button { Text = "复制输出" };
        var toolPanel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4) };
        toolPanel.Controls.Add(new Label { Text = "方式:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(0, 6, 0, 0) });
        toolPanel.Controls.Add(_encodingCombo);
        toolPanel.Controls.Add(_encodeButton);
        toolPanel.Controls.Add(_clearButton);
        toolPanel.Controls.Add(_copyButton);

        _inputBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, AcceptsTab = true, ScrollBars = ScrollBars.Both, WordWrap = false };
        _outputBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false };
        var splitContainer = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
        splitContainer.Panel1.Controls.Add(_inputBox);
        splitContainer.Panel2.Controls.Add(_outputBox);

        _statusLabel = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(_statusLabel);

        // 后添加的先停靠：状态栏贴底、工具栏贴顶，分栏容器填满剩余
        Controls.Add(splitContainer);
        Controls.Add(toolPanel);
        Controls.Add(statusStrip);

        _encodingCombo.SelectedIndexChanged += EncodingCombo_SelectedIndexChanged;
        _encodeButton.Click += (_, _) => EncodeCurrent();
        _clearButton.Click += (_, _) => ClearAll();
        _copyButton.Click += (_, _) => CopyOutput();
        Load += CadmusPanel_Load;

        foreach (CadmusEncoding encoding in CadmusOperations.Encodings)
        {
            _encodingCombo.Items.Add(encoding);
        }

        // 抑制期内选中默认项，实际恢复由 Load 中的 ApplySettings 完成
        _encodingCombo.SelectedIndex = 0;
    }

    private CadmusEncoding? CurrentEncoding => _encodingCombo.SelectedItem as CadmusEncoding;

    private async void CadmusPanel_Load(object? sender, EventArgs e)
    {
        // WinForms 事件处理允许 async void（规范 §5），内部必须 try-catch 兜底
        try
        {
            CadmusSettingsLoadResult result = await _settingsStore.LoadAsync();
            _settings = result.Settings;
            ApplySettings(result.Settings);

            if (result.RecoveredFromCorruption)
            {
                _logger.Warning("设置文件损坏，已备份到 {BackupPath} 并以默认值启动", result.BackupFilePath);
                _statusLabel.Text = "设置文件损坏，已备份原文件并以默认设置启动";
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "加载 Cadmus 设置失败");
            _statusLabel.Text = $"设置加载失败：{ex.Message}";
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void ApplySettings(CadmusSettings settings)
    {
        _encodingCombo.SelectedItem = CadmusOperations.ResolveInitialEncoding(settings.LastEncoding);
    }

    private void EncodingCombo_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        SaveSettings();
    }

    private void SaveSettings()
    {
        _settings = _settings with
        {
            LastEncoding = CurrentEncoding?.Id,
        };
        _ = SaveSettingsSafelyAsync(_settings);
    }

    private async Task SaveSettingsSafelyAsync(CadmusSettings settings)
    {
        try
        {
            await _settingsStore.SaveAsync(settings);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "保存 Cadmus 设置失败");
            _statusLabel.Text = $"设置保存失败：{ex.Message}";
        }
    }

    private void EncodeCurrent()
    {
        if (CurrentEncoding is not { } encoding)
        {
            _statusLabel.Text = "未选择编码方式，无法执行操作";
            return;
        }

        CadmusOperationResult result = CadmusOperations.Encode(encoding, _inputBox.Text);
        _statusLabel.Text = result.StatusText;
        if (result.Output is not null)
        {
            _outputBox.Text = result.Output;
        }
    }

    private void ClearAll()
    {
        _inputBox.Clear();
        _outputBox.Clear();
        _statusLabel.Text = string.Empty;
    }

    private void CopyOutput()
    {
        if (string.IsNullOrEmpty(_outputBox.Text))
        {
            _statusLabel.Text = "输出区为空，无内容可复制";
            return;
        }

        Clipboard.SetText(_outputBox.Text);
        _statusLabel.Text = "输出已复制到剪贴板";
    }
}

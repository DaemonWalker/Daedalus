using System.Windows.Forms;

using Serilog;

namespace Daedalus.Tools.Oedipus;

/// <summary>
/// Oedipus 主面板（oedipus.md §4）：输入/输出左右分栏、解码方式下拉、解码/清空/复制输出、
/// 状态栏展示结果。界面保持薄，操作编排在 <see cref="OedipusOperations"/>。
/// </summary>
internal sealed class OedipusPanel : UserControl
{
    private readonly ILogger _logger;
    private readonly OedipusSettingsStore _settingsStore;

    private readonly ComboBox _decodingCombo;
    private readonly Button _decodeButton;
    private readonly Button _clearButton;
    private readonly Button _copyButton;
    private readonly TextBox _inputBox;
    private readonly TextBox _outputBox;
    private readonly ToolStripStatusLabel _statusLabel;

    // 初始化/加载设置期间抑制选择变化事件，避免把未加载完的状态写回 settings.json
    private bool _suppressEvents = true;
    private OedipusSettings _settings = OedipusSettings.Default;

    /// <summary>
    /// 构造注入（oedipus.md §4.1）：ILogger 为宿主按插件 id 打好 SourceContext 的实例，
    /// 设置 Store 由容器注入。
    /// </summary>
    public OedipusPanel(ILogger logger, OedipusSettingsStore settingsStore)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(settingsStore);
        _logger = logger;
        _settingsStore = settingsStore;

        _decodingCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = nameof(OedipusDecoding.DisplayName) };
        _decodeButton = new Button { Text = "解码" };
        _clearButton = new Button { Text = "清空" };
        _copyButton = new Button { Text = "复制输出" };
        var toolPanel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4) };
        toolPanel.Controls.Add(new Label { Text = "方式:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(0, 6, 0, 0) });
        toolPanel.Controls.Add(_decodingCombo);
        toolPanel.Controls.Add(_decodeButton);
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

        _decodingCombo.SelectedIndexChanged += DecodingCombo_SelectedIndexChanged;
        _decodeButton.Click += (_, _) => DecodeCurrent();
        _clearButton.Click += (_, _) => ClearAll();
        _copyButton.Click += (_, _) => CopyOutput();
        Load += OedipusPanel_Load;

        foreach (OedipusDecoding decoding in OedipusOperations.Decodings)
        {
            _decodingCombo.Items.Add(decoding);
        }

        // 抑制期内选中默认项，实际恢复由 Load 中的 ApplySettings 完成
        _decodingCombo.SelectedIndex = 0;
    }

    private OedipusDecoding? CurrentDecoding => _decodingCombo.SelectedItem as OedipusDecoding;

    private async void OedipusPanel_Load(object? sender, EventArgs e)
    {
        // WinForms 事件处理允许 async void（规范 §5），内部必须 try-catch 兜底
        try
        {
            OedipusSettingsLoadResult result = await _settingsStore.LoadAsync();
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
            _logger.Error(ex, "加载 Oedipus 设置失败");
            _statusLabel.Text = $"设置加载失败：{ex.Message}";
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void ApplySettings(OedipusSettings settings)
    {
        _decodingCombo.SelectedItem = OedipusOperations.ResolveInitialDecoding(settings.LastDecoding);
    }

    private void DecodingCombo_SelectedIndexChanged(object? sender, EventArgs e)
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
            LastDecoding = CurrentDecoding?.Id,
        };
        _ = SaveSettingsSafelyAsync(_settings);
    }

    private async Task SaveSettingsSafelyAsync(OedipusSettings settings)
    {
        try
        {
            await _settingsStore.SaveAsync(settings);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "保存 Oedipus 设置失败");
            _statusLabel.Text = $"设置保存失败：{ex.Message}";
        }
    }

    private void DecodeCurrent()
    {
        if (CurrentDecoding is not { } decoding)
        {
            _statusLabel.Text = "未选择解码方式，无法执行操作";
            return;
        }

        OedipusOperationResult result = OedipusOperations.Decode(decoding, _inputBox.Text);
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

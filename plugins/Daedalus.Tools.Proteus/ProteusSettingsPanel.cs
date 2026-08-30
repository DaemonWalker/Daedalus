using Serilog;

namespace Daedalus.Tools.Proteus;

/// <summary>
/// Proteus 设置页（proteus.md §6）：美化缩进宽度。修改即保存（保留文件中的上次格式选择）。
/// 由统一设置窗口以标签页承载（FR-SHELL-006）。
/// </summary>
internal sealed class ProteusSettingsPanel : UserControl
{
    // 与主面板工具栏的缩进候选保持一致
    private static readonly int[] IndentChoices = [2, 4, 8];

    private readonly ProteusSettingsStore _settingsStore;
    private readonly ILogger _logger;
    private readonly ComboBox _indentCombo;

    private ProteusSettings _settings = ProteusSettings.Default;

    // 加载完成前抑制变更事件，避免把默认控件状态写回 settings.json
    private bool _suppressEvents = true;

    public ProteusSettingsPanel(ProteusSettingsStore settingsStore, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(logger);
        _settingsStore = settingsStore;
        _logger = logger;

        _indentCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
        foreach (int choice in IndentChoices)
        {
            _indentCombo.Items.Add(choice.ToString());
        }

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10), AutoSize = true };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = "美化缩进宽度（空格数）", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        layout.Controls.Add(_indentCombo, 1, 0);
        Controls.Add(layout);

        _indentCombo.SelectedIndexChanged += async (_, _) => await SaveAsync();
        Load += ProteusSettingsPanel_Load;
    }

    private async void ProteusSettingsPanel_Load(object? sender, EventArgs e)
    {
        // WinForms 事件处理允许 async void（规范 §5），内部必须 try-catch 兜底
        try
        {
            ProteusSettingsLoadResult result = await _settingsStore.LoadAsync();
            _settings = result.Settings;
            if (result.RecoveredFromCorruption)
            {
                _logger.Warning("设置文件损坏，已备份到 {BackupPath} 并以默认值启动", result.BackupFilePath);
                MessageBox.Show(this, "设置文件损坏，已备份原文件并以默认设置启动。", "Proteus 设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            string indent = _settings.IndentSize.ToString();
            if (!_indentCombo.Items.Contains(indent))
            {
                // 容忍手工编辑出的非常规缩进值，直接加进下拉（与主面板同款处理）
                _indentCombo.Items.Add(indent);
            }

            _indentCombo.SelectedItem = indent;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "加载 Proteus 设置失败");
            MessageBox.Show(this, $"设置加载失败：{ex.Message}", "Proteus 设置", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private async Task SaveAsync()
    {
        if (_suppressEvents)
        {
            return;
        }

        if (!int.TryParse(_indentCombo.SelectedItem as string, out int indentSize) || indentSize <= 0)
        {
            return;
        }

        _settings = _settings with { IndentSize = indentSize };
        try
        {
            await _settingsStore.SaveAsync(_settings);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "保存 Proteus 设置失败");
            MessageBox.Show(this, $"设置保存失败：{ex.Message}", "Proteus 设置", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

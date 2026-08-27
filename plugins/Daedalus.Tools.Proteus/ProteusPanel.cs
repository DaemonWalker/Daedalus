using System.Windows.Forms;

using Daedalus.Abstractions;

using FastColoredTextBoxNS;

using Serilog;

namespace Daedalus.Tools.Proteus;

/// <summary>
/// Proteus 主面板（proteus.md §4）：输入/输出左右分栏、格式与缩进选择、格式化/压缩/校验/清空/复制、
/// 状态栏展示校验结果与错误行列。界面保持薄，操作编排在 <see cref="ProteusOperations"/>。
/// </summary>
internal sealed class ProteusPanel : UserControl
{
    private static readonly int[] IndentChoices = [2, 4, 8];

    // JSON 自定义高亮的样式（proteus.md §5：json → 自定义规则）。先数字/关键字后字符串，字符串样式覆盖前者
    private static readonly Style JsonNumberStyle = new TextStyle(Brushes.MediumPurple, null, FontStyle.Regular);
    private static readonly Style JsonKeywordStyle = new TextStyle(Brushes.Blue, null, FontStyle.Regular);
    private static readonly Style JsonStringStyle = new TextStyle(Brushes.Brown, null, FontStyle.Regular);

    private readonly IToolHost _host;
    private readonly ILogger _logger;
    private readonly ProteusSettingsStore _settingsStore;

    private readonly ComboBox _formatCombo;
    private readonly ComboBox _indentCombo;
    private readonly Button _formatButton;
    private readonly Button _minifyButton;
    private readonly Button _validateButton;
    private readonly Button _clearButton;
    private readonly Button _copyButton;
    private readonly FastColoredTextBox _inputBox;
    private readonly FastColoredTextBox _outputBox;
    private readonly ToolStripStatusLabel _statusLabel;

    private ProteusHighlightKind _highlightKind;

    // 初始化/加载设置期间抑制选择变化事件，避免把未加载完的状态写回 settings.json
    private bool _suppressEvents = true;
    private ProteusSettings _settings = ProteusSettings.Default;

    /// <summary>
    /// 构造注入（step 14，proteus.md §4.1）：ILogger 为宿主按插件 id 打好 SourceContext 的实例，
    /// 设置 Store 与宿主服务由容器注入。
    /// </summary>
    public ProteusPanel(IToolHost host, ILogger logger, ProteusSettingsStore settingsStore)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(settingsStore);
        _host = host;
        _logger = logger;
        _settingsStore = settingsStore;

        _formatCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = nameof(IFormatter.DisplayName) };
        _indentCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        _formatButton = new Button { Text = "格式化" };
        _minifyButton = new Button { Text = "压缩" };
        _validateButton = new Button { Text = "校验" };
        _clearButton = new Button { Text = "清空" };
        _copyButton = new Button { Text = "复制输出" };
        var toolPanel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4) };
        toolPanel.Controls.Add(new Label { Text = "格式:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(0, 6, 0, 0) });
        toolPanel.Controls.Add(_formatCombo);
        toolPanel.Controls.Add(new Label { Text = "缩进:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 6, 0, 0) });
        toolPanel.Controls.Add(_indentCombo);
        toolPanel.Controls.Add(_formatButton);
        toolPanel.Controls.Add(_minifyButton);
        toolPanel.Controls.Add(_validateButton);
        toolPanel.Controls.Add(_clearButton);
        toolPanel.Controls.Add(_copyButton);

        _inputBox = new FastColoredTextBox { Dock = DockStyle.Fill };
        _outputBox = new FastColoredTextBox { Dock = DockStyle.Fill, ReadOnly = true };
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

        _formatCombo.SelectedIndexChanged += FormatCombo_SelectedIndexChanged;
        _indentCombo.SelectedIndexChanged += IndentCombo_SelectedIndexChanged;
        _formatButton.Click += (_, _) => RunWithFormatter(f => ProteusOperations.Format(f, _inputBox.Text, CurrentIndentSize));
        _minifyButton.Click += (_, _) => RunWithFormatter(f => ProteusOperations.Minify(f, _inputBox.Text));
        _validateButton.Click += (_, _) => RunWithFormatter(f => ProteusOperations.Validate(f, _inputBox.Text));
        _clearButton.Click += (_, _) => ClearAll();
        _copyButton.Click += (_, _) => CopyOutput();
        _inputBox.TextChanged += Editor_TextChanged;
        _outputBox.TextChanged += Editor_TextChanged;
        Load += ProteusPanel_Load;

        foreach (IFormatter formatter in host.Formatters)
        {
            _formatCombo.Items.Add(formatter);
        }

        foreach (int choice in IndentChoices)
        {
            _indentCombo.Items.Add(choice.ToString());
        }

        _indentCombo.SelectedItem = ProteusSettings.DefaultIndentSize.ToString();

        if (_formatCombo.Items.Count == 0)
        {
            // 未安装任何格式化器：提示并禁用操作按钮（proteus.md §5）
            SetOperationsEnabled(false);
            _statusLabel.Text = "未安装任何格式化器插件，格式化功能不可用";
        }
    }

    private IFormatter? CurrentFormatter => _formatCombo.SelectedItem as IFormatter;

    private int CurrentIndentSize =>
        int.TryParse(_indentCombo.SelectedItem as string, out int size) && size > 0
            ? size
            : ProteusSettings.DefaultIndentSize;

    private async void ProteusPanel_Load(object? sender, EventArgs e)
    {
        // WinForms 事件处理允许 async void（规范 §5），内部必须 try-catch 兜底
        try
        {
            ProteusSettingsLoadResult result = await _settingsStore.LoadAsync();
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
            _logger.Error(ex, "加载 Proteus 设置失败");
            _statusLabel.Text = $"设置加载失败：{ex.Message}";
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void ApplySettings(ProteusSettings settings)
    {
        IFormatter? initial = ProteusOperations.ResolveInitialFormatter(_host.Formatters, settings.LastFormatId);
        if (initial is not null)
        {
            _formatCombo.SelectedItem = initial;
        }

        string indent = settings.IndentSize.ToString();
        if (!_indentCombo.Items.Contains(indent))
        {
            // 容忍手工编辑出的非常规缩进值，直接加进下拉
            _indentCombo.Items.Add(indent);
        }

        _indentCombo.SelectedItem = indent;
        ApplyHighlight();
    }

    private void FormatCombo_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        ApplyHighlight();
        SaveSettings();
    }

    private void IndentCombo_SelectedIndexChanged(object? sender, EventArgs e)
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
            LastFormatId = CurrentFormatter?.FormatId,
            IndentSize = CurrentIndentSize,
        };
        _ = SaveSettingsSafelyAsync(_settings);
    }

    private async Task SaveSettingsSafelyAsync(ProteusSettings settings)
    {
        try
        {
            await _settingsStore.SaveAsync(settings);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "保存 Proteus 设置失败");
            _statusLabel.Text = $"设置保存失败：{ex.Message}";
        }
    }

    private void RunWithFormatter(Func<IFormatter, ProteusOperationResult> operation)
    {
        if (CurrentFormatter is not { } formatter)
        {
            _statusLabel.Text = "未选择格式，无法执行操作";
            return;
        }

        RunOperation(operation(formatter));
    }

    private void RunOperation(ProteusOperationResult result)
    {
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

    private void SetOperationsEnabled(bool enabled)
    {
        _formatButton.Enabled = enabled;
        _minifyButton.Enabled = enabled;
        _validateButton.Enabled = enabled;
        _indentCombo.Enabled = enabled;
    }

    private void ApplyHighlight()
    {
        _highlightKind = ProteusHighlightMapper.Map(CurrentFormatter?.FormatId);
        ApplyHighlight(_inputBox);
        ApplyHighlight(_outputBox);
    }

    private void ApplyHighlight(FastColoredTextBox box)
    {
        box.WordWrap = false;
        if (_highlightKind == ProteusHighlightKind.Xml)
        {
            box.Language = Language.XML;
        }
        else
        {
            // None 与 Json 都走 Custom：Json 由 TextChanged 里的自定义规则着色，None 仅清除残留样式
            box.Language = Language.Custom;
            HighlightJson(box);
        }
    }

    private void Editor_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_highlightKind == ProteusHighlightKind.Json && sender is FastColoredTextBox box)
        {
            HighlightJson(box);
        }
    }

    // 自定义 JSON 高亮规则：工具场景文本规模有限，直接整篇重着色，不做增量优化
    private static void HighlightJson(FastColoredTextBox box)
    {
        FastColoredTextBoxNS.Range range = box.Range;
        range.ClearStyle(StyleIndex.All);
        range.SetStyle(JsonNumberStyle, @"(?<![\w""])-?\d+(\.\d+)?([eE][+-]?\d+)?(?![\w""])");
        range.SetStyle(JsonKeywordStyle, @"\b(true|false|null)\b");
        range.SetStyle(JsonStringStyle, @"""([^""\\]|\\.)*""");
    }
}

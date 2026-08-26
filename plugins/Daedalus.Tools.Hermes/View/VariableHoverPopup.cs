using System.Windows.Forms;

using Daedalus.Tools.Hermes.Variables;

namespace Daedalus.Tools.Hermes.View;

/// <summary>
/// {{变量}} 悬浮编辑弹窗（FR-HERMES-024）：显示变量名、当前值、来源环境；
/// 就地改值，回车或失焦保存；secret 变量默认掩码、眼睛按钮切换明文；
/// 未定义变量可就地在当前环境创建；无启用环境时只读提示。
/// </summary>
internal sealed class VariableHoverPopup : Form
{
    private readonly Label _nameLabel;
    private readonly Label _sourceLabel;
    private readonly TextBox _valueBox;
    private readonly Button _revealButton;

    private string _variableName = string.Empty;
    private string _originalValue = string.Empty;
    private bool _secret;
    private bool _revealed;
    private bool _editable;

    public VariableHoverPopup()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ClientSize = new Size(300, 82);
        BackColor = SystemColors.Info;

        _nameLabel = new Label { Name = "VariableName", Dock = DockStyle.Top, AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
        _sourceLabel = new Label { Name = "VariableSource", Dock = DockStyle.Top, AutoSize = true };
        _valueBox = new TextBox { Name = "VariableValue", Dock = DockStyle.Fill };
        _revealButton = new Button { Name = "RevealSecret", Text = "👁", Dock = DockStyle.Right, Width = 28 };
        var valuePanel = new Panel { Name = "ValuePanel", Dock = DockStyle.Top, Height = 26, Padding = new Padding(6, 2, 6, 2) };
        valuePanel.Controls.Add(_valueBox);
        valuePanel.Controls.Add(_revealButton);

        Controls.Add(valuePanel);
        Controls.Add(_sourceLabel);
        Controls.Add(_nameLabel);

        _revealButton.Click += (_, _) => ToggleReveal();
        _valueBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                _ = CommitAndCloseAsync();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                Hide();
            }
        };
        // 失焦保存（FR-HERMES-024）
        Deactivate += (_, _) => _ = CommitAndCloseAsync();
        Shown += (_, _) => _valueBox.Focus();
    }

    /// <summary>回车/失焦保存（name, value）；仅可编辑且值变化时触发。</summary>
    public event Func<string, string, Task>? SaveRequested;

    /// <summary>为指定变量展示弹窗。</summary>
    /// <param name="reference">命中的变量引用。</param>
    /// <param name="environment">当前启用环境；null 时只读提示。</param>
    /// <param name="screenLocation">弹出位置（屏幕坐标）。</param>
    public void ShowFor(VariableReference reference, HermesEnvironment? environment, Point screenLocation)
    {
        _variableName = reference.Name;
        EnvironmentVariable? variable = environment?.Variables.FirstOrDefault(v => v.Key == reference.Name);
        _nameLabel.Text = $"变量：{reference.Name}";

        if (environment is null)
        {
            _sourceLabel.Text = "未启用环境（只读）";
            _editable = false;
            _valueBox.ReadOnly = true;
            _valueBox.Text = string.Empty;
            _revealButton.Visible = false;
            _secret = false;
        }
        else if (variable is null)
        {
            _sourceLabel.Text = $"未定义 — 输入值将在「{environment.Name}」创建";
            _editable = true;
            _valueBox.ReadOnly = false;
            _valueBox.Text = string.Empty;
            _revealButton.Visible = false;
            _secret = false;
        }
        else
        {
            _sourceLabel.Text = $"来源：{environment.Name}" + (variable.Enabled ? string.Empty : "（已停用）");
            _editable = true;
            _valueBox.ReadOnly = false;
            _valueBox.Text = variable.Value;
            _secret = variable.Secret;
            _revealButton.Visible = variable.Secret;
        }

        _revealed = false;
        _originalValue = _valueBox.Text;
        ApplyMask();

        Location = screenLocation;
        Show();
    }

    private void ToggleReveal()
    {
        _revealed = !_revealed;
        ApplyMask();
    }

    private void ApplyMask()
    {
        _valueBox.UseSystemPasswordChar = _secret && !_revealed;
    }

    private async Task CommitAndCloseAsync()
    {
        Hide();
        // 只读或值未变化时不写盘
        if (_editable && _valueBox.Text != _originalValue && SaveRequested is not null)
        {
            await SaveRequested.Invoke(_variableName, _valueBox.Text);
        }
    }
}

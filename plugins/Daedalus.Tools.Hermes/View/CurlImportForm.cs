using System.Windows.Forms;

namespace Daedalus.Tools.Hermes.View;

/// <summary>从 cURL 导入的粘贴对话框（hermes.md §9.2）：多行文本框承接 Chrome "Copy as cURL (bash)"。</summary>
internal sealed class CurlImportForm : Form
{
    private readonly TextBox _commandBox;

    public CurlImportForm()
    {
        Text = "从 cURL 导入";
        Width = 720;
        Height = 420;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;

        _commandBox = new TextBox
        {
            Multiline = true,
            Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Both,
            AcceptsReturn = true,
            AcceptsTab = true,
            WordWrap = false,
            PlaceholderText = "粘贴 Chrome DevTools 的“Copy as cURL (bash)”文本…",
        };

        var okButton = new Button { Text = "导入", DialogResult = DialogResult.OK, AutoSize = true };
        var cancelButton = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttonBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(4),
        };
        buttonBar.Controls.Add(okButton);
        buttonBar.Controls.Add(cancelButton);

        Controls.Add(_commandBox);
        Controls.Add(buttonBar);
        AcceptButton = okButton;
        CancelButton = cancelButton;

        // 高 DPI 适配（详见 DpiScale）
        DpiScale.Apply(this);
    }

    /// <summary>用户粘贴的命令文本。</summary>
    public string CommandText => _commandBox.Text;
}

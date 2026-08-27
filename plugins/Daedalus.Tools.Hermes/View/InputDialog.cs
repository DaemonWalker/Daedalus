using System.Windows.Forms;

namespace Daedalus.Tools.Hermes.View;

/// <summary>单行文本输入对话框（集合树与管理窗口的新建/重命名共用）。</summary>
internal static class InputDialog
{
    /// <summary>弹出输入框；用户确认返回输入文本（裁剪首尾空白），取消或空文本返回 null。</summary>
    public static string? Prompt(IWin32Window owner, string title, string label, string initialValue = "")
    {
        using var form = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ClientSize = new Size(360, 96),
        };
        var promptLabel = new Label { Text = label, Left = 12, Top = 12, AutoSize = true };
        var input = new TextBox { Left = 12, Top = 32, Width = 336, Text = initialValue };
        var okButton = new Button { Text = "确定", DialogResult = DialogResult.OK, Left = 192, Top = 62, Width = 75 };
        var cancelButton = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Left = 273, Top = 62, Width = 75 };
        form.Controls.AddRange([promptLabel, input, okButton, cancelButton]);
        form.AcceptButton = okButton;
        form.CancelButton = cancelButton;

        // 高 DPI 适配（详见 DpiScale）
        DpiScale.Apply(form);

        if (form.ShowDialog(owner) != DialogResult.OK)
        {
            return null;
        }

        string value = input.Text.Trim();
        return value.Length == 0 ? null : value;
    }
}

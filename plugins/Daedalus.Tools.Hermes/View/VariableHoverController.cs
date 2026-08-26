using System.Windows.Forms;

using Daedalus.Tools.Hermes.Variables;

using FastColoredTextBoxNS;

namespace Daedalus.Tools.Hermes.View;

/// <summary>
/// {{变量}} 悬浮编辑的接线器（FR-HERMES-024）：鼠标悬浮约 500ms 后在命中位置弹出
/// <see cref="VariableHoverPopup"/>。支持 URL 输入框、请求头/urlencoded 表格的值列、
/// FastColoredTextBox 请求体编辑器三种输入位置（hermes.md §6.1）。
/// </summary>
internal sealed class VariableHoverController
{
    private const int HoverDelayMs = 500;

    private readonly Func<HermesEnvironment?> _activeEnvironmentProvider;
    private readonly VariableHoverPopup _popup = new();
    private readonly System.Windows.Forms.Timer _hoverTimer = new() { Interval = HoverDelayMs };

    // 计时器触发时待处理的一次命中检测
    private Control? _pendingControl;
    private Point _pendingLocation;
    private Func<Control, Point, VariableReference?>? _pendingHitTest;

    /// <param name="activeEnvironmentProvider">取当前启用环境。</param>
    /// <param name="setVariableAsync">保存变量（name, value）→ 立即持久化并刷新环境缓存。</param>
    public VariableHoverController(
        Func<HermesEnvironment?> activeEnvironmentProvider,
        Func<string, string, Task> setVariableAsync)
    {
        _activeEnvironmentProvider = activeEnvironmentProvider;
        _popup.SaveRequested += setVariableAsync;
        _hoverTimer.Tick += OnHoverTick;
    }

    /// <summary>挂到 TextBox（URL 输入框）。</summary>
    public void AttachTextBox(TextBox box) =>
        Attach(box, static (control, location) =>
        {
            var textBox = (TextBox)control;
            int charIndex = textBox.GetCharIndexFromPosition(location);
            return VariableReferenceFinder.FindAt(textBox.Text, charIndex);
        });

    /// <summary>挂到 FastColoredTextBox（请求体编辑器）。</summary>
    public void AttachEditor(FastColoredTextBox editor) =>
        Attach(editor, static (control, location) =>
        {
            var box = (FastColoredTextBox)control;
            int offset = box.PlaceToPosition(box.PointToPlace(location));
            return offset < 0 ? null : VariableReferenceFinder.FindAt(box.Text, offset);
        });

    /// <summary>挂到 DataGridView 的指定值列（请求头 / urlencoded 表格）。</summary>
    public void AttachGrid(DataGridView grid, int valueColumnIndex) =>
        Attach(grid, (control, location) => HitTestGrid((DataGridView)control, location, valueColumnIndex));

    private void Attach(Control control, Func<Control, Point, VariableReference?> hitTest)
    {
        control.MouseMove += (_, e) =>
        {
            // 悬浮中移动鼠标：重新计时；弹窗已显示时不打断（交由弹窗失焦关闭）
            _hoverTimer.Stop();
            if (!_popup.Visible)
            {
                _pendingControl = control;
                _pendingLocation = e.Location;
                _pendingHitTest = hitTest;
                _hoverTimer.Start();
            }
        };
        control.MouseLeave += (_, _) =>
        {
            if (ReferenceEquals(_pendingControl, control))
            {
                _hoverTimer.Stop();
            }
        };
        // 点击立即取消待触发的悬浮
        control.MouseDown += (_, _) => _hoverTimer.Stop();
    }

    private void OnHoverTick(object? sender, EventArgs e)
    {
        _hoverTimer.Stop();
        if (_pendingControl is null || _pendingHitTest is null || _pendingControl.IsDisposed)
        {
            return;
        }

        VariableReference? reference = _pendingHitTest(_pendingControl, _pendingLocation);
        if (reference is null)
        {
            return;
        }

        Point screen = _pendingControl.PointToScreen(new Point(_pendingLocation.X, _pendingLocation.Y + 20));
        _popup.ShowFor(reference, _activeEnvironmentProvider(), screen);
    }

    // 表格值列命中：按渲染宽度逐字符逼近鼠标 x 对应的字符下标，再查引用
    private static VariableReference? HitTestGrid(DataGridView grid, Point location, int valueColumnIndex)
    {
        DataGridView.HitTestInfo hit = grid.HitTest(location.X, location.Y);
        if (hit.Type != DataGridViewHitTestType.Cell || hit.ColumnIndex != valueColumnIndex || hit.RowIndex < 0)
        {
            return null;
        }

        DataGridViewRow row = grid.Rows[hit.RowIndex];
        if (row.IsNewRow)
        {
            return null;
        }

        string text = row.Cells[valueColumnIndex].Value as string ?? string.Empty;
        if (text.Length == 0)
        {
            return null;
        }

        Rectangle cellBounds = grid.GetCellDisplayRectangle(valueColumnIndex, hit.RowIndex, false);
        int relativeX = location.X - cellBounds.X - 4; // 单元格内容左内边距约 4px
        if (relativeX <= 0)
        {
            return VariableReferenceFinder.FindAt(text, 0);
        }

        int charIndex = text.Length;
        for (int i = 1; i <= text.Length; i++)
        {
            if (TextRenderer.MeasureText(text[..i], grid.Font).Width > relativeX)
            {
                charIndex = i - 1;
                break;
            }
        }

        return VariableReferenceFinder.FindAt(text, charIndex);
    }
}

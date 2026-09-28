using System.Text.RegularExpressions;
using System.Windows.Forms;

using Daedalus.Tools.Hermes.Editing;

using FastColoredTextBoxNS;

namespace Daedalus.Tools.Hermes.View;

/// <summary>
/// 仿 VSCode 的内嵌查找条：悬浮在编辑器右上角（不参与布局），替代 FCTB 自带的弹窗式查找。
/// 忽略大小写的普通子串查找、Enter/Shift+Enter 与 F3/Shift+F3 环绕导航、全部命中半透明高亮、
/// 输入约 300ms 防抖重算；Esc/× 关闭时清除高亮并把焦点还给编辑器。
/// </summary>
internal sealed class FindBar : UserControl
{
    private const int DebounceMs = 300;
    private const int BarWidth = 320;
    private const int BarHeight = 30;
    private const int TopRightMargin = 12;

    // 全部命中的高亮底色：半透明橙（深色/浅色文本下均可读）；静态复用，不可 Dispose
    private static readonly Style MatchStyle = new MarkerStyle(new SolidBrush(Color.FromArgb(80, 255, 170, 0)));

    private readonly FastColoredTextBox _box;
    private readonly TextBox _findBox;
    private readonly Label _countLabel;
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = DebounceMs };

    private int[] _matches = [];
    private int _currentIndex = -1;

    private FindBar(FastColoredTextBox box)
    {
        _box = box;
        Size = new Size(BarWidth, BarHeight);
        BorderStyle = BorderStyle.FixedSingle;
        BackColor = SystemColors.Window;

        _findBox = new TextBox { Width = 160, Margin = new Padding(4, 4, 2, 2) };
        _countLabel = new Label { AutoSize = true, Margin = new Padding(2, 7, 2, 2), ForeColor = SystemColors.GrayText };
        var prevButton = new Button { Text = "↑", Width = 26, Height = 23, Margin = new Padding(2) };
        var nextButton = new Button { Text = "↓", Width = 26, Height = 23, Margin = new Padding(2) };
        var closeButton = new Button { Text = "×", Width = 26, Height = 23, Margin = new Padding(2) };
        var tips = new ToolTip();
        tips.SetToolTip(prevButton, "上一个（Shift+Enter）");
        tips.SetToolTip(nextButton, "下一个（Enter）");
        tips.SetToolTip(closeButton, "关闭（Esc）");

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(1) };
        flow.Controls.AddRange([_findBox, _countLabel, prevButton, nextButton, closeButton]);
        Controls.Add(flow);

        prevButton.Click += (_, _) => Navigate(FindMatchIndex.WrapPrevious);
        nextButton.Click += (_, _) => Navigate(FindMatchIndex.WrapNext);
        closeButton.Click += (_, _) => Close();
        _findBox.TextChanged += (_, _) => ScheduleRefresh();
        _findBox.KeyDown += OnFindBoxKeyDown;
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            RefreshMatches();
        };

        // 接管 FCTB 默认查找热键：Ctrl+F 打开查找条，F3/Shift+F3 在查找条可见时导航（旧弹窗不再出现）
        _box.HotkeysMapping.Remove(Keys.Control | Keys.F);
        _box.HotkeysMapping.Remove(Keys.F3);
        _box.HotkeysMapping.Remove(Keys.Shift | Keys.F3);
        _box.HotkeysMapping.Add(Keys.Control | Keys.F, FCTBAction.CustomAction1);
        _box.HotkeysMapping.Add(Keys.F3, FCTBAction.CustomAction2);
        _box.HotkeysMapping.Add(Keys.Shift | Keys.F3, FCTBAction.CustomAction3);
        _box.CustomAction += OnBoxCustomAction;
        // 编辑期间查找条开着时同步重算（如请求体可编辑页）
        _box.TextChanged += (_, _) =>
        {
            if (Visible)
            {
                ScheduleRefresh();
            }
        };
    }

    /// <summary>
    /// 把 <paramref name="box"/> 包进一个 Dock=Fill 的容器并接入查找条，返回容器（由调用方放入界面）。
    /// 每次发送新建的 FCTB 实例重新调本方法即可。
    /// </summary>
    public static Control Attach(FastColoredTextBox box)
    {
        ArgumentNullException.ThrowIfNull(box);

        var container = new Panel { Dock = DockStyle.Fill };
        box.Dock = DockStyle.Fill;
        container.Controls.Add(box);

        var bar = new FindBar(box) { Visible = false };
        container.Controls.Add(bar);
        container.Resize += (_, _) => bar.MoveToTopRight();
        bar.MoveToTopRight();
        bar.BringToFront();
        return container;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _debounce.Dispose();
        }

        base.Dispose(disposing);
    }

    private void MoveToTopRight()
    {
        if (Parent is not null)
        {
            Left = Math.Max(0, Parent.ClientSize.Width - Width - TopRightMargin);
            Top = TopRightMargin;
        }
    }

    private void OnBoxCustomAction(object? sender, CustomActionEventArgs e)
    {
        switch (e.Action)
        {
            case FCTBAction.CustomAction1:
                Open();
                break;
            case FCTBAction.CustomAction2 when Visible:
                Navigate(FindMatchIndex.WrapNext);
                break;
            case FCTBAction.CustomAction3 when Visible:
                Navigate(FindMatchIndex.WrapPrevious);
                break;
        }
    }

    private void Open()
    {
        // 单行选区预填查找词（多行选区不预填）；赋值触发 TextChanged → 防抖重算
        FastColoredTextBoxNS.Range selection = _box.Selection;
        if (!selection.IsEmpty && selection.Start.iLine == selection.End.iLine)
        {
            _findBox.Text = selection.Text;
        }

        Visible = true;
        BringToFront();
        RefreshMatches();
        _findBox.Focus();
        _findBox.SelectAll();
    }

    private void Close()
    {
        _debounce.Stop();
        Visible = false;
        _matches = [];
        _currentIndex = -1;
        _box.Range.ClearStyle([MatchStyle]);
        _box.Focus();
    }

    private void OnFindBoxKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyData)
        {
            case Keys.Enter:
                Navigate(FindMatchIndex.WrapNext);
                e.SuppressKeyPress = true;
                break;
            case Keys.Shift | Keys.Enter:
                Navigate(FindMatchIndex.WrapPrevious);
                e.SuppressKeyPress = true;
                break;
            case Keys.Escape:
                Close();
                e.SuppressKeyPress = true;
                break;
        }
    }

    private void ScheduleRefresh()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private void RefreshMatches()
    {
        string query = _findBox.Text;
        _box.Range.ClearStyle([MatchStyle]);
        _matches = FindMatchIndex.FindAll(_box.Text, query);
        if (_matches.Length > 0)
        {
            _box.Range.SetStyle(MatchStyle, Regex.Escape(query), RegexOptions.IgnoreCase);
            _currentIndex = FindMatchIndex.IndexAtOrAfter(_matches, _box.SelectionStart);
            SelectCurrent();
        }
        else
        {
            _currentIndex = -1;
        }

        UpdateCountLabel();
    }

    private void Navigate(Func<int, int, int> wrap)
    {
        if (_matches.Length == 0)
        {
            return;
        }

        _currentIndex = wrap(_currentIndex, _matches.Length);
        SelectCurrent();
        UpdateCountLabel();
    }

    private void SelectCurrent()
    {
        _box.SelectionStart = _matches[_currentIndex];
        _box.SelectionLength = _findBox.Text.Length;
        _box.DoSelectionVisible();
    }

    private void UpdateCountLabel() =>
        _countLabel.Text = _findBox.Text.Length == 0
            ? string.Empty
            : _matches.Length == 0 ? "无结果" : $"{_currentIndex + 1}/{_matches.Length}";
}

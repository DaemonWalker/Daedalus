using System.Windows.Forms;

using Daedalus.Tools.Hermes.History;

namespace Daedalus.Tools.Hermes.View;

/// <summary>
/// 历史列表面板（FR-HERMES-052/054/055）：默认显示最近 N 天记录（新→旧）；
/// 顶部搜索框输入关键词后切换为全量未压缩历史的文本子串搜索结果（匹配逻辑在 HistorySearch，
/// 本面板只发事件、展示结果）；结果为空且存在归档包时显示"搜索更久"按钮，点击后逐包推进
/// 归档搜索，搜索中按钮变为"停止"。双击重放到编辑区（含归档中的记录）。
/// </summary>
internal sealed class HistoryPanel : UserControl
{
    private readonly TextBox _searchBox;
    private readonly Button _deeperButton;
    private readonly ListBox _list;
    private readonly System.Windows.Forms.Timer _debounceTimer;

    private bool _deeperSearchRunning;

    public HistoryPanel()
    {
        _searchBox = new TextBox { Dock = DockStyle.Top, PlaceholderText = "搜索历史（关键词）" };
        _deeperButton = new Button { Text = "搜索更久（归档）", Dock = DockStyle.Top, Visible = false };
        _list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };

        // 输入防抖：停止输入约 400ms 后才发起搜索，避免逐字符扫描历史文件
        _debounceTimer = new System.Windows.Forms.Timer { Interval = 400 };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            SearchRequested?.Invoke(this, _searchBox.Text.Trim());
        };
        _searchBox.TextChanged += (_, _) =>
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        };
        _deeperButton.Click += (_, _) =>
        {
            if (_deeperSearchRunning)
            {
                SearchStopRequested?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                SearchDeeperRequested?.Invoke(this, EventArgs.Empty);
            }
        };
        _list.DoubleClick += (_, _) => ReplaySelected();

        Controls.Add(_list);
        Controls.Add(_deeperButton);
        Controls.Add(_searchBox);
    }

    /// <summary>搜索框防抖结束（含清空回空串，调用方据此恢复最近列表）。</summary>
    public event EventHandler<string>? SearchRequested;

    /// <summary>点击"搜索更久"（开始逐包搜索归档）。</summary>
    public event EventHandler? SearchDeeperRequested;

    /// <summary>归档搜索进行中点击"停止"。</summary>
    public event EventHandler? SearchStopRequested;

    /// <summary>双击某条历史请求重放。</summary>
    public event EventHandler<HistoryEntry>? ReplayRequested;

    /// <summary>显示最近历史清单。</summary>
    public void SetEntries(IReadOnlyList<HistoryEntry> entries) => ShowEntries(entries);

    /// <summary>显示搜索结果（替换列表内容）。</summary>
    public void ShowSearchResults(IReadOnlyList<HistoryEntry> entries) => ShowEntries(entries);

    /// <summary>结果为空且存在归档包时显示"搜索更久"按钮；其余情况隐藏。</summary>
    public void SetDeeperSearchAvailable(bool available)
    {
        _deeperButton.Visible = available;
        if (!available)
        {
            SetDeeperSearchRunning(false);
        }
    }

    /// <summary>切换归档搜索进行中状态：按钮文本在"搜索更久"与"停止"间切换。</summary>
    public void SetDeeperSearchRunning(bool running)
    {
        _deeperSearchRunning = running;
        _deeperButton.Text = running ? "停止" : "搜索更久（归档）";
    }

    private void ShowEntries(IReadOnlyList<HistoryEntry> entries)
    {
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            foreach (HistoryEntry entry in entries)
            {
                _list.Items.Add(new HistoryListItem(entry));
            }
        }
        finally
        {
            _list.EndUpdate();
        }
    }

    private void ReplaySelected()
    {
        if (_list.SelectedItem is HistoryListItem item)
        {
            ReplayRequested?.Invoke(this, item.Entry);
        }
    }

    /// <summary>列表项：METHOD URL → 状态码 (时间)。</summary>
    private sealed record HistoryListItem(HistoryEntry Entry)
    {
        public override string ToString() =>
            $"{Entry.Request.Method} {Entry.Request.Url} → {Entry.Response.Status}  {Entry.Timestamp:MM-dd HH:mm:ss}";
    }
}

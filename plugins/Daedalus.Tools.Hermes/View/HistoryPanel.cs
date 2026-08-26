using System.Windows.Forms;

using Daedalus.Tools.Hermes.History;

namespace Daedalus.Tools.Hermes.View;

/// <summary>
/// 历史列表面板（FR-HERMES-052）：最近 N 天记录（新→旧），顶部搜索框按方法/URL 子串过滤，
/// 双击重放到编辑区。归档搜索（"搜索更久"）属下一步（FR-HERMES-054/055）。
/// </summary>
internal sealed class HistoryPanel : UserControl
{
    private readonly TextBox _filterBox;
    private readonly ListBox _list;

    private IReadOnlyList<HistoryEntry> _entries = [];

    public HistoryPanel()
    {
        _filterBox = new TextBox { Dock = DockStyle.Top, PlaceholderText = "搜索历史（方法 / URL）" };
        _list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };

        _filterBox.TextChanged += (_, _) => Refilter();
        _list.DoubleClick += (_, _) => ReplaySelected();

        Controls.Add(_list);
        Controls.Add(_filterBox);
    }

    /// <summary>双击某条历史请求重放。</summary>
    public event EventHandler<HistoryEntry>? ReplayRequested;

    /// <summary>刷新历史清单（保留当前过滤词）。</summary>
    public void SetEntries(IReadOnlyList<HistoryEntry> entries)
    {
        _entries = entries;
        Refilter();
    }

    private void Refilter()
    {
        string filter = _filterBox.Text.Trim();
        IEnumerable<HistoryEntry> visible = _entries;
        if (filter.Length > 0)
        {
            visible = _entries.Where(e =>
                e.Request.Method.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || e.Request.Url.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            foreach (HistoryEntry entry in visible)
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

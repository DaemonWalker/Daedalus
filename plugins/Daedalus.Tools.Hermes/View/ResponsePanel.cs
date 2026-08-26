using System.Text;
using System.Windows.Forms;

using Daedalus.Tools.Hermes.History;
using Daedalus.Tools.Hermes.Http;
using Daedalus.Tools.Hermes.Response;

using FastColoredTextBoxNS;

namespace Daedalus.Tools.Hermes.View;

/// <summary>
/// 响应区（hermes.md §3）：无跳转时单个响应视图；跟随重定向产生跳转链时每跳一个 tab
/// （FR-HERMES-006，标题"序号: 状态码"，最终一跳默认选中）。响应体按 Content-Type 美化（FR-HERMES-004）。
/// </summary>
internal sealed class ResponsePanel : UserControl
{
    private readonly TabControl _hopTabs;
    private readonly Label _emptyLabel;

    public ResponsePanel()
    {
        _hopTabs = new TabControl { Dock = DockStyle.Fill };
        _emptyLabel = new Label
        {
            Text = "尚未发送请求",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = SystemColors.GrayText,
        };
        Controls.Add(_hopTabs);
        Controls.Add(_emptyLabel);
        _emptyLabel.BringToFront();
    }

    /// <summary>渲染一次发送的跳转链；美化按每跳的 Content-Type 独立生效。</summary>
    public void ShowResult(SendResult result, ResponseBeautifier beautifier)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(beautifier);

        ClearTabs();
        foreach (ResponseHop hop in result.Hops)
        {
            TabPage page = BuildHopTab(hop, beautifier);
            _hopTabs.TabPages.Add(page);
        }

        _hopTabs.SelectedIndex = _hopTabs.TabPages.Count - 1; // 最终一跳默认选中
        _hopTabs.BringToFront();
    }

    /// <summary>渲染发送失败（网络错误等，无响应）。</summary>
    public void ShowError(string message)
    {
        ClearTabs();
        var page = new TabPage("发送失败");
        var box = new FastColoredTextBox { Dock = DockStyle.Fill, ReadOnly = true, Text = message };
        page.Controls.Add(box);
        _hopTabs.TabPages.Add(page);
        _hopTabs.BringToFront();
    }

    /// <summary>清空回到占位。</summary>
    public void Clear()
    {
        ClearTabs();
        _emptyLabel.BringToFront();
    }

    private void ClearTabs()
    {
        foreach (TabPage page in _hopTabs.TabPages)
        {
            page.Dispose();
        }

        _hopTabs.TabPages.Clear();
    }

    private static TabPage BuildHopTab(ResponseHop hop, ResponseBeautifier beautifier)
    {
        HopResponse response = hop.Response;
        string title = $"{hop.Index}: {response.Status} {response.ReasonPhrase}".TrimEnd();
        var page = new TabPage(title);

        long bodyBytes = Encoding.UTF8.GetByteCount(response.Body);
        var info = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(4),
            Text = $"{hop.Request.Method} {hop.Request.Url}    状态 {response.Status}    耗时 {response.ElapsedMs} ms    响应体 {bodyBytes} 字节",
        };

        var innerTabs = new TabControl { Dock = DockStyle.Fill };

        BeautifyResult beautified = beautifier.Beautify(response.Body, FindContentType(response));
        var bodyPage = new TabPage("Body");
        var bodyBox = new FastColoredTextBox { Dock = DockStyle.Fill, ReadOnly = true, Text = beautified.Text, WordWrap = false };
        FctbHighlight.Apply(bodyBox, beautified.FormatId);
        bodyPage.Controls.Add(bodyBox);

        var headersPage = new TabPage("Headers");
        var headersGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };
        headersGrid.Columns.Add("Key", "键");
        headersGrid.Columns.Add("Value", "值");
        foreach (NameValuePair header in response.Headers)
        {
            headersGrid.Rows.Add(header.Key, header.Value);
        }

        headersPage.Controls.Add(headersGrid);
        innerTabs.TabPages.Add(bodyPage);
        innerTabs.TabPages.Add(headersPage);

        page.Controls.Add(innerTabs);
        page.Controls.Add(info);
        return page;
    }

    private static string? FindContentType(HopResponse response) =>
        response.Headers.FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))?.Value;
}

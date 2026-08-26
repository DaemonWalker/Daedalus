using System.Windows.Forms;

using Daedalus.Tools.Hermes.Collections;
using Daedalus.Tools.Hermes.Editing;

using FastColoredTextBoxNS;

namespace Daedalus.Tools.Hermes.View;

/// <summary>
/// 请求编辑区（hermes.md §3）：方法可编辑下拉（FR-HERMES-001）、URL、发送/取消/保存、
/// Params / Headers / Body（raw + urlencoded，FR-HERMES-002）/ 选项 / 后事件脚本五个页签。
/// 持有"已保存草稿"快照，内容变化时比较得出脏标记（FR-HERMES-012）。
/// </summary>
internal sealed class RequestEditorPanel : UserControl
{
    private static readonly string[] CommonMethods = ["GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS"];
    private static readonly string[] RawContentTypes =
        ["application/json", "application/xml", "text/xml", "text/plain", "text/html", "application/javascript"];

    private readonly ComboBox _methodCombo;
    private readonly TextBox _urlBox;
    private readonly Button _sendButton;
    private readonly Button _saveButton;
    private readonly KeyValueGrid _paramsGrid;
    private readonly KeyValueGrid _headersGrid;
    private readonly ComboBox _bodyKindCombo;
    private readonly Panel _rawBodyPanel;
    private readonly Panel _urlEncodedBodyPanel;
    private readonly ComboBox _bodyContentTypeCombo;
    private readonly FastColoredTextBox _rawBodyBox;
    private readonly KeyValueGrid _bodyFieldsGrid;
    private readonly RadioButton _redirectGlobal;
    private readonly RadioButton _redirectForce;
    private readonly RadioButton _redirectForbid;
    private readonly RadioButton _cookieGlobal;
    private readonly RadioButton _cookieForce;
    private readonly RadioButton _cookieForbid;
    private readonly FastColoredTextBox _scriptBox;

    private RequestDraft _savedDraft = RequestDraft.Empty;

    // LoadDraft / Params 双向同步期间抑制事件，避免把加载当成用户编辑或互相回写死循环
    private bool _suppressEvents;

    public RequestEditorPanel(VariableHoverController hover)
    {
        _methodCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 90 };
        _methodCombo.Items.AddRange(CommonMethods);
        _methodCombo.Text = "GET";
        _urlBox = new TextBox { Width = 420, Anchor = AnchorStyles.Left | AnchorStyles.Right };
        _sendButton = new Button { Text = "发送", Width = 60 };
        _saveButton = new Button { Text = "保存", Width = 60, Enabled = false };
        var urlPanel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4), WrapContents = false };
        urlPanel.Controls.AddRange([_methodCombo, _urlBox, _sendButton, _saveButton]);

        _paramsGrid = new KeyValueGrid { Dock = DockStyle.Fill };
        _headersGrid = new KeyValueGrid { Dock = DockStyle.Fill };

        _bodyKindCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        _bodyKindCombo.Items.AddRange(["无请求体", "raw（可指定 Content-Type）", "x-www-form-urlencoded"]);
        _bodyKindCombo.SelectedIndex = 0;
        _bodyContentTypeCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Top };
        _bodyContentTypeCombo.Items.AddRange(RawContentTypes);
        _bodyContentTypeCombo.Text = "application/json";
        _rawBodyBox = new FastColoredTextBox { Dock = DockStyle.Fill, Language = Language.Custom };
        _rawBodyPanel = new Panel { Dock = DockStyle.Fill };
        _rawBodyPanel.Controls.Add(_rawBodyBox);
        _rawBodyPanel.Controls.Add(_bodyContentTypeCombo);
        _bodyFieldsGrid = new KeyValueGrid { Dock = DockStyle.Fill };
        _urlEncodedBodyPanel = new Panel { Dock = DockStyle.Fill, Visible = false };
        _urlEncodedBodyPanel.Controls.Add(_bodyFieldsGrid);
        var bodyPanel = new Panel { Dock = DockStyle.Fill };
        bodyPanel.Controls.Add(_rawBodyPanel);
        bodyPanel.Controls.Add(_urlEncodedBodyPanel);
        bodyPanel.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(4, 2, 4, 2), Controls = { _bodyKindCombo } });

        _redirectGlobal = new RadioButton { Text = "跟随全局设置", Checked = true, AutoSize = true };
        _redirectForce = new RadioButton { Text = "强制跟随", AutoSize = true };
        _redirectForbid = new RadioButton { Text = "强制不跟随", AutoSize = true };
        var redirectGroup = new GroupBox { Text = "重定向", Dock = DockStyle.Top, Height = 90 };
        var redirectFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown };
        redirectFlow.Controls.AddRange([_redirectGlobal, _redirectForce, _redirectForbid]);
        redirectGroup.Controls.Add(redirectFlow);

        _cookieGlobal = new RadioButton { Text = "跟随全局设置", Checked = true, AutoSize = true };
        _cookieForce = new RadioButton { Text = "强制启用", AutoSize = true };
        _cookieForbid = new RadioButton { Text = "强制禁用", AutoSize = true };
        var cookieGroup = new GroupBox { Text = "Cookie", Dock = DockStyle.Top, Height = 90 };
        var cookieFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown };
        cookieFlow.Controls.AddRange([_cookieGlobal, _cookieForce, _cookieForbid]);
        cookieGroup.Controls.Add(cookieFlow);
        var optionsPanel = new Panel { Dock = DockStyle.Fill };
        optionsPanel.Controls.Add(cookieGroup);
        optionsPanel.Controls.Add(redirectGroup);

        _scriptBox = new FastColoredTextBox { Dock = DockStyle.Fill, Language = Language.JS };

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(WrapTab("Params", _paramsGrid));
        tabs.TabPages.Add(WrapTab("Headers", _headersGrid));
        tabs.TabPages.Add(WrapTab("Body", bodyPanel));
        tabs.TabPages.Add(WrapTab("选项", optionsPanel));
        tabs.TabPages.Add(WrapTab("后事件脚本", _scriptBox));

        Controls.Add(tabs);
        Controls.Add(urlPanel);

        // 悬浮编辑（FR-HERMES-024）：URL 输入框、请求头表格值列、请求体编辑器、urlencoded 值列
        hover.AttachTextBox(_urlBox);
        hover.AttachGrid(_headersGrid.Grid, valueColumnIndex: 1);
        hover.AttachEditor(_rawBodyBox);
        hover.AttachGrid(_bodyFieldsGrid.Grid, valueColumnIndex: 1);

        _methodCombo.TextChanged += (_, _) => NotifyChanged();
        _urlBox.TextChanged += UrlBox_TextChanged;
        _sendButton.Click += (_, _) => SendRequested?.Invoke(this, EventArgs.Empty);
        _saveButton.Click += (_, _) => SaveRequested?.Invoke(this, EventArgs.Empty);
        _paramsGrid.ContentChanged += (_, _) => ParamsGrid_ContentChanged();
        _headersGrid.ContentChanged += (_, _) => NotifyChanged();
        _bodyKindCombo.SelectedIndexChanged += BodyKindCombo_SelectedIndexChanged;
        _bodyContentTypeCombo.TextChanged += (_, _) => NotifyChanged();
        _rawBodyBox.TextChanged += (_, _) => NotifyChanged();
        _bodyFieldsGrid.ContentChanged += (_, _) => NotifyChanged();
        _scriptBox.TextChanged += (_, _) => NotifyChanged();
        foreach (RadioButton radio in new[] { _redirectGlobal, _redirectForce, _redirectForbid, _cookieGlobal, _cookieForce, _cookieForbid })
        {
            radio.CheckedChanged += (_, _) => NotifyChanged();
        }
    }

    /// <summary>点击发送（发送中则为取消）。</summary>
    public event EventHandler? SendRequested;

    /// <summary>点击保存（写回集合树节点）。</summary>
    public event EventHandler? SaveRequested;

    /// <summary>脏标记变化。</summary>
    public event EventHandler<bool>? DirtyChanged;

    /// <summary>当前编辑内容是否与已保存草稿不一致。</summary>
    public bool IsDirty => !CurrentDraft.ContentEquals(_savedDraft);

    /// <summary>从控件读出的当前草稿（Params 表已与 URL query 保持同步）。</summary>
    public RequestDraft CurrentDraft
    {
        get
        {
            bool? followRedirect = _redirectForce.Checked ? true : _redirectForbid.Checked ? false : null;
            bool? useCookies = _cookieForce.Checked ? true : _cookieForbid.Checked ? false : null;
            string script = _scriptBox.Text.Trim();
            return new RequestDraft
            {
                Method = _methodCombo.Text.Trim() is { Length: > 0 } method ? method : "GET",
                Url = _urlBox.Text.Trim(),
                Headers = _headersGrid.GetEntries(),
                Body = BuildBody(),
                Options = followRedirect is null && useCookies is null ? null : new RequestOptions(followRedirect, useCookies),
                PostResponseScript = script.Length == 0 ? null : script,
            };
        }
    }

    /// <summary>载入草稿到编辑区（重置脏标记基准需再调 <see cref="MarkSaved"/>）。</summary>
    public void LoadDraft(RequestDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        _suppressEvents = true;
        try
        {
            _methodCombo.Text = draft.Method;
            _urlBox.Text = draft.Url;
            _paramsGrid.SetEntries(QueryParamMapper.Parse(draft.Url));
            _headersGrid.SetEntries(draft.Headers);
            LoadBody(draft.Body);
            _redirectGlobal.Checked = draft.Options?.FollowRedirect is null;
            _redirectForce.Checked = draft.Options?.FollowRedirect is true;
            _redirectForbid.Checked = draft.Options?.FollowRedirect is false;
            _cookieGlobal.Checked = draft.Options?.UseCookies is null;
            _cookieForce.Checked = draft.Options?.UseCookies is true;
            _cookieForbid.Checked = draft.Options?.UseCookies is false;
            _scriptBox.Text = draft.PostResponseScript ?? string.Empty;
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    /// <summary>把当前内容记为已保存基准，并更新脏标记事件。</summary>
    public void MarkSaved()
    {
        _savedDraft = CurrentDraft;
        DirtyChanged?.Invoke(this, false);
    }

    /// <summary>保存按钮可用性（仅在编辑树中请求时可用）。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool SaveEnabled
    {
        get => _saveButton.Enabled;
        set => _saveButton.Enabled = value;
    }

    /// <summary>切换发送中状态：发送中按钮显示"取消"。</summary>
    public void SetSending(bool sending) => _sendButton.Text = sending ? "取消" : "发送";

    private RequestBody? BuildBody() => _bodyKindCombo.SelectedIndex switch
    {
        1 => new RequestBody
        {
            Kind = RequestBodyKind.Raw,
            ContentType = _bodyContentTypeCombo.Text.Trim() is { Length: > 0 } contentType ? contentType : null,
            Text = _rawBodyBox.Text,
        },
        2 => new RequestBody { Kind = RequestBodyKind.UrlEncoded, Fields = _bodyFieldsGrid.GetEntries() },
        _ => null,
    };

    private void LoadBody(RequestBody? body)
    {
        if (body is null)
        {
            _bodyKindCombo.SelectedIndex = 0;
            _rawBodyBox.Text = string.Empty;
            _bodyFieldsGrid.SetEntries([]);
            return;
        }

        if (body.Kind == RequestBodyKind.UrlEncoded)
        {
            _bodyKindCombo.SelectedIndex = 2;
            _bodyFieldsGrid.SetEntries(body.Fields ?? []);
            _rawBodyBox.Text = string.Empty;
        }
        else
        {
            _bodyKindCombo.SelectedIndex = 1;
            _bodyContentTypeCombo.Text = body.ContentType ?? "application/json";
            _rawBodyBox.Text = body.Text ?? string.Empty;
            _bodyFieldsGrid.SetEntries([]);
        }

        SwitchBodyPanel();
    }

    private void UrlBox_TextChanged(object? sender, EventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        // URL 变化 → 同步 Params 表（SetEntries 自身抑制表格事件）
        _paramsGrid.SetEntries(QueryParamMapper.Parse(_urlBox.Text));
        NotifyChanged();
    }

    private void ParamsGrid_ContentChanged()
    {
        if (_suppressEvents)
        {
            return;
        }

        // Params 表变化 → 重建 URL query 段（抑制 URL 文本事件的回写）
        _suppressEvents = true;
        try
        {
            _urlBox.Text = QueryParamMapper.Apply(_urlBox.Text, _paramsGrid.GetEntries());
        }
        finally
        {
            _suppressEvents = false;
        }

        NotifyChanged();
    }

    private void BodyKindCombo_SelectedIndexChanged(object? sender, EventArgs e)
    {
        SwitchBodyPanel();
        NotifyChanged();
    }

    private void SwitchBodyPanel()
    {
        _rawBodyPanel.Visible = _bodyKindCombo.SelectedIndex == 1;
        _urlEncodedBodyPanel.Visible = _bodyKindCombo.SelectedIndex == 2;
    }

    private void NotifyChanged()
    {
        if (!_suppressEvents)
        {
            DirtyChanged?.Invoke(this, IsDirty);
        }
    }

    // Ctrl+Enter 发送（hermes.md §5.1）
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Enter))
        {
            SendRequested?.Invoke(this, EventArgs.Empty);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private static TabPage WrapTab(string title, Control content)
    {
        var page = new TabPage(title);
        content.Dock = DockStyle.Fill;
        page.Controls.Add(content);
        return page;
    }
}

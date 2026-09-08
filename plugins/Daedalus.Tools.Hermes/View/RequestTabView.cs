using System.Windows.Forms;

using Daedalus.Tools.Hermes.Collections;
using Daedalus.Tools.Hermes.Editing;
using Daedalus.Tools.Hermes.History;
using Daedalus.Tools.Hermes.Http;
using Daedalus.Tools.Hermes.Response;
using Daedalus.Tools.Hermes.Scripting;
using Daedalus.Tools.Hermes.Settings;
using Daedalus.Tools.Hermes.Variables;

using Serilog;

namespace Daedalus.Tools.Hermes.View;

/// <summary>
/// 单个请求标签页的内容控件（step 19，hermes.md §3）：上下分隔的请求编辑区 + 响应区，
/// 以及发送/取消、历史回填、保存写回等全部按 tab 隔离的状态（发送 CTS、绑定的树节点、标题）。
/// 由 HermesPanel 经 ActivatorUtilities 手工构造：容器可解析的服务（含每 tab 一份的 ScriptHost）
/// 从当前 scope 解析，面板共享的 <see cref="VariableHoverController"/> 与环境/设置回调显式传入。
/// </summary>
internal sealed class RequestTabView : UserControl
{
    private const string DraftTitle = "新请求";

    private readonly ILogger _logger;
    private readonly SendOrchestrator _orchestrator;
    private readonly HistoryStore _historyStore;
    private readonly RecentHistoryReader _historyReader;
    private readonly ScriptHost _scriptHost;
    private readonly ResponseBeautifier _beautifier;
    private readonly Func<EnvironmentData> _environmentProvider;
    private readonly Func<HermesSettings> _settingsProvider;

    private readonly RequestEditorPanel _editor;
    private readonly ResponsePanel _responsePanel;
    private readonly SplitContainer _split;

    // 发送状态：非 null 表示正在发送（发送按钮此时为"取消"）
    private CancellationTokenSource? _sendCts;

    // 绑定的树中请求；null 表示游离草稿（历史重放 / cURL 导入 / 空白新请求，保存禁用）
    private CollectionPanel.RequestNodeEventArgs? _boundTreeNode;
    private string _title = DraftTitle;

    // ApplyRightRatio 程序化调整期间抑制 SplitterMoved 上报，避免面板同步比例时递归落盘
    private bool _suppressSplitterEvent;

    public RequestTabView(
        SendOrchestrator orchestrator,
        HistoryStore historyStore,
        RecentHistoryReader historyReader,
        ScriptHost scriptHost,
        ResponseBeautifier beautifier,
        ILogger logger,
        VariableHoverController hover,
        Func<EnvironmentData> environmentProvider,
        Func<HermesSettings> settingsProvider)
    {
        ArgumentNullException.ThrowIfNull(orchestrator);
        ArgumentNullException.ThrowIfNull(hover);
        _orchestrator = orchestrator;
        _historyStore = historyStore;
        _historyReader = historyReader;
        _scriptHost = scriptHost;
        _beautifier = beautifier;
        _logger = logger;
        _environmentProvider = environmentProvider;
        _settingsProvider = settingsProvider;

        _editor = new RequestEditorPanel(hover) { Dock = DockStyle.Fill };
        _responsePanel = new ResponsePanel { Dock = DockStyle.Fill };
        _split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        _split.Panel1.Controls.Add(_editor);
        _split.Panel2.Controls.Add(_responsePanel);
        Controls.Add(_split);

        _editor.SendRequested += async (_, _) => await SendOrCancelAsync();
        _editor.SaveRequested += (_, _) => SaveToCollection();
        _editor.DirtyChanged += (_, dirty) => DirtyChanged?.Invoke(this, dirty);
        _split.SplitterMoved += (_, _) =>
        {
            if (!_suppressSplitterEvent)
            {
                SplitterMoved?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    /// <summary>状态栏文本更新（由面板写入共享状态栏）。</summary>
    public event EventHandler<string>? StatusChanged;

    /// <summary>脏标记变化（面板据此更新标签标题的未保存标记）。</summary>
    public event EventHandler<bool>? DirtyChanged;

    /// <summary>标题变化（载入树节点 / 草稿时）。</summary>
    public event EventHandler<string>? TitleChanged;

    /// <summary>请求把当前编辑内容写回集合树（树写回与持久化由面板完成）。</summary>
    public event EventHandler<RequestTabView>? SaveToCollectionRequested;

    /// <summary>后事件脚本修改了环境变量（已落盘），面板需刷新环境缓存与下拉。</summary>
    public event EventHandler<EnvironmentData>? EnvironmentUpdated;

    /// <summary>新历史已落盘，面板需刷新历史列表。</summary>
    public event EventHandler? HistoryChanged;

    /// <summary>用户拖动了 tab 内分隔条（面板据此把比例同步到全部 tab 并落盘）。</summary>
    public event EventHandler? SplitterMoved;

    /// <summary>当前编辑内容是否有未保存修改。</summary>
    public bool IsDirty => _editor.IsDirty;

    /// <summary>绑定的树中请求；null 表示游离草稿。</summary>
    public CollectionPanel.RequestNodeEventArgs? BoundTreeNode => _boundTreeNode;

    /// <summary>标签标题（解绑后保留最后标题）。</summary>
    public string Title => _title;

    /// <summary>tab 内分隔条当前比例；尺寸未就绪时为 null。</summary>
    public double? RightRatio =>
        _split.Height > 0 ? HermesLayout.DistanceToRatio(_split.SplitterDistance, _split.Height) : null;

    /// <summary>载入树中请求节点：绑定并回填最近一次历史响应。</summary>
    public void LoadNode(CollectionPanel.RequestNodeEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        _boundTreeNode = args;
        _editor.LoadDraft(RequestDraft.FromNode(args.Node));
        _editor.MarkSaved();
        _editor.SaveEnabled = true;
        SetTitle(args.Node.Name);

        // 切换请求先清空响应区，再回填该请求最近一次的历史响应
        _responsePanel.Clear();
        _ = ShowLatestHistoryAsync(args.Node);
    }

    /// <summary>载入游离草稿（历史重放 / cURL 导入 / 空白新请求）：不绑定树节点，保存禁用。</summary>
    public void LoadDraft(RequestDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        _boundTreeNode = null;
        _editor.LoadDraft(draft);
        _editor.MarkSaved();
        _editor.SaveEnabled = false;
        SetTitle(DraftTitle);
    }

    /// <summary>关闭前确认（FR-HERMES-012）：有未保存修改时提示保存 / 放弃 / 取消。</summary>
    public bool ConfirmClose()
    {
        if (!_editor.IsDirty)
        {
            return true;
        }

        DialogResult choice = MessageBox.Show(this,
            $"请求「{_title}」有未保存的修改。是否保存？\n（是＝保存并关闭；否＝放弃修改；取消＝不关闭）",
            "未保存的修改", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
        switch (choice)
        {
            case DialogResult.Cancel:
                return false;
            case DialogResult.Yes when _boundTreeNode is not null:
                // 关闭在即，保存即发即弃：Store 不依赖控件，写盘在后台完成后进程自然收尾
                SaveToCollection();
                return true;
            default:
                return true;
        }
    }

    /// <summary>树节点已删除：解除绑定（保存禁用、标题保留）。</summary>
    public void Unbind()
    {
        _boundTreeNode = null;
        _editor.SaveEnabled = false;
    }

    /// <summary>拖拽移动后旧 TreeNode 被摘除：按面板找回的新位置重新绑定（节点对象不变）。</summary>
    public void Rebind(CollectionPanel.RequestNodeEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        _boundTreeNode = args;
    }

    /// <summary>应用 tab 内分隔条比例（step 15 的 rightRatio 现作用于每个请求 tab）；尺寸未就绪或比例非法时跳过。</summary>
    public void ApplyRightRatio(double ratio)
    {
        int totalSize = _split.Height;
        if (!HermesLayout.IsValidRatio(ratio) || totalSize <= 0)
        {
            return;
        }

        _suppressSplitterEvent = true;
        try
        {
            _split.SplitterDistance = HermesLayout.RatioToDistance(
                ratio, totalSize, _split.Panel1MinSize, _split.Panel2MinSize, _split.SplitterWidth);
        }
        finally
        {
            _suppressSplitterEvent = false;
        }
    }

    private void SetTitle(string title)
    {
        _title = title;
        TitleChanged?.Invoke(this, title);
    }

    /// <summary>把当前编辑内容写回绑定的树节点（树更新与集合持久化经事件交给面板）。</summary>
    private void SaveToCollection()
    {
        if (_boundTreeNode is null)
        {
            return;
        }

        CollectionNode updated = _editor.CurrentDraft.ToNode(_boundTreeNode.Node.Name);
        _boundTreeNode = new CollectionPanel.RequestNodeEventArgs(_boundTreeNode.Collection, updated, _boundTreeNode.TreeNode);
        SaveToCollectionRequested?.Invoke(this, this);
        _editor.MarkSaved();
        StatusChanged?.Invoke(this, "已保存");
    }

    /// <summary>载入请求后回填最近一次历史响应（即发即弃）；期间界面内容已变化（再次载入/新发送）则放弃回填。</summary>
    private async Task ShowLatestHistoryAsync(CollectionNode node)
    {
        int clearedVersion = _responsePanel.DisplayVersion;
        try
        {
            string url = node.Url ?? string.Empty;
            if (url.Length == 0)
            {
                return;
            }

            HistoryEntry? entry = await _historyReader.FindLatestAsync(node.Method ?? "GET", url);
            if (entry is null || _responsePanel.DisplayVersion != clearedVersion)
            {
                return;
            }

            _responsePanel.ShowHistory(entry, _beautifier);
            StatusChanged?.Invoke(this, $"已显示最近一次历史响应（{entry.Timestamp:MM-dd HH:mm:ss}）");
        }
        catch (Exception ex)
        {
            // 回填是辅助动作，失败只记日志不干扰主流程
            _logger.Error(ex, "回填历史响应失败");
        }
    }

    private async Task SendOrCancelAsync()
    {
        if (_sendCts is not null)
        {
            // FR-HERMES-005：取消进行中的请求
            _sendCts.Cancel();
            return;
        }

        RequestDraft draft = _editor.CurrentDraft;
        if (draft.Url.Length == 0)
        {
            StatusChanged?.Invoke(this, "请输入 URL");
            return;
        }

        EnvironmentData environmentData = _environmentProvider();
        HermesSettings settings = _settingsProvider();
        PreparedRequest prepared = _orchestrator.Prepare(draft, environmentData.FindActive());
        if (prepared.UndefinedVariables.Count > 0)
        {
            // FR-HERMES-022：未定义变量原样保留并提示
            StatusChanged?.Invoke(this, $"未定义变量（已原样发送）：{string.Join("、", prepared.UndefinedVariables)}");
        }

        _sendCts = new CancellationTokenSource();
        _editor.SetSending(true);
        try
        {
            SendResult result = await _orchestrator.SendAsync(prepared, settings, _sendCts.Token);
            _logger.Debug("发送完成：状态 {Status}，共 {HopCount} 跳，{HasScript}",
                result.FinalHop.Response.Status, result.Hops.Count,
                draft.PostResponseScript is not null ? "有后事件脚本" : "无后事件脚本");

            // 后事件脚本（FR-HERMES-040/045）：只针对最终一跳执行一次；异常隔离进"脚本输出"页（FR-HERMES-043）
            ScriptExecutionResult? scriptResult = null;
            if (draft.PostResponseScript is not null)
            {
                scriptResult = await _scriptHost.RunAsync(
                    draft.PostResponseScript, result.FinalHop.Response, environmentData, settings, _sendCts.Token);
                if (scriptResult.UpdatedEnvironmentData is not null)
                {
                    // pm.environment.set/unset 已落盘（FR-HERMES-044），面板刷新环境缓存、下拉与悬浮编辑数据源
                    EnvironmentUpdated?.Invoke(this, scriptResult.UpdatedEnvironmentData);
                }
            }

            _responsePanel.ShowResult(result, _beautifier, scriptResult);

            string status = $"状态 {result.FinalHop.Response.Status}，耗时 {result.FinalHop.Response.ElapsedMs} ms";
            if (scriptResult?.Error is not null)
            {
                status += "；后事件脚本执行出错（详见响应区“脚本输出”页）";
            }
            if (result.RedirectLimitExceeded)
            {
                status += "；超过跳转上限（10 跳），已停止跟随";
            }
            else if (result.RedirectLoopDetected)
            {
                status += "；检测到跳转环，已停止跟随";
            }

            StatusChanged?.Invoke(this, status);

            // 历史落盘（hermes.md §5.1：只记最终一跳，异步追加）
            HistoryEntry entry = _orchestrator.BuildHistoryEntry(prepared, result, DateTimeOffset.Now);
            await _historyStore.AppendAsync(entry, settings.ResponseBodyLimitBytes, _sendCts.Token);
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            StatusChanged?.Invoke(this, "已取消");
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or UriFormatException)
        {
            _logger.Warning(ex, "请求发送失败");
            _responsePanel.ShowError($"发送失败：{ex.Message}");
            StatusChanged?.Invoke(this, $"发送失败：{ex.Message}");
        }
        finally
        {
            _editor.SetSending(false);
            _sendCts.Dispose();
            _sendCts = null;
        }
    }
}

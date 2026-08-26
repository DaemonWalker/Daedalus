using System.Windows.Forms;

using Daedalus.Abstractions;
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
/// Hermes 主面板（hermes.md §3）：顶部环境栏、左侧集合树/历史、右侧请求编辑区/响应区、底部状态栏。
/// 界面保持薄：发送编排在 <see cref="SendOrchestrator"/>，美化在 <see cref="ResponseBeautifier"/>，
/// 脏标记在 <see cref="RequestDraft"/>。
/// </summary>
internal sealed class HermesPanel : UserControl, IToolCloseConfirmation
{
    private const string NoEnvironmentText = "（未启用）";

    private readonly ILogger _logger;
    private readonly HermesTool _tool;
    private readonly CollectionStore _collectionStore;
    private readonly EnvironmentStore _environmentStore;
    private readonly HistoryStore _historyStore;
    private readonly HermesSettingsStore _settingsStore;
    private readonly RecentHistoryReader _historyReader;
    private readonly HistoryArchive _historyArchive;
    private readonly HistorySearch _historySearch;
    private readonly ResponseBeautifier _beautifier;
    private readonly VariableHoverController _hover;
    private readonly ScriptHost _scriptHost;
    private readonly PostmanImporter _postmanImporter = new();
    private readonly CurlImporter _curlImporter = new();

    private readonly ComboBox _envCombo;
    private readonly CollectionPanel _collectionPanel;
    private readonly HistoryPanel _historyPanel;
    private readonly RequestEditorPanel _editor;
    private readonly ResponsePanel _responsePanel;
    private readonly ToolStripStatusLabel _statusLabel;

    private HermesSettings _settings = HermesSettings.Default;
    private EnvironmentData _environmentData = EnvironmentData.Empty;

    // 发送状态：非 null 表示正在发送（发送按钮此时为"取消"）
    private CancellationTokenSource? _sendCts;

    // 历史搜索状态：_searchCts 管直搜；_deeperCts 非 null 表示归档搜索进行中（"搜索更久"按钮此时为"停止"）
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _deeperCts;
    private string _currentKeyword = string.Empty;

    // 当前正在编辑的树中请求；null 表示编辑区未绑定树节点（如历史重放）
    private CollectionPanel.RequestNodeEventArgs? _editingRequest;

    // 加载/刷新环境下拉期间抑制事件，避免把未加载完的状态写回 environments.json
    private bool _suppressEvents = true;

    public HermesPanel(IToolHost host, HermesTool tool)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(tool);
        _tool = tool;
        _logger = host.GetLogger(HermesTool.ToolId);
        string dataDirectory = host.GetDataDirectory(HermesTool.ToolId);
        _collectionStore = new CollectionStore(dataDirectory);
        _environmentStore = new EnvironmentStore(dataDirectory);
        _historyStore = new HistoryStore(dataDirectory);
        _settingsStore = new HermesSettingsStore(dataDirectory);
        _historyReader = new RecentHistoryReader(_historyStore);
        _historyArchive = new HistoryArchive(dataDirectory, _logger);
        _historySearch = new HistorySearch(dataDirectory, _logger);
        _beautifier = new ResponseBeautifier(host);
        _hover = new VariableHoverController(() => _environmentData.FindActive(), SetVariableFromHoverAsync);
        _scriptHost = new ScriptHost(_environmentStore, _logger);

        _envCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        var manageEnvButton = new Button { Text = "管理环境", AutoSize = true };
        var importButton = new Button { Text = "导入 ▾", AutoSize = true };
        var importMenu = new ContextMenuStrip();
        importMenu.Items.Add("从 Postman 文件导入…", null, async (_, _) => await ImportPostmanAsync());
        importMenu.Items.Add("从 cURL 命令导入…", null, (_, _) => ImportCurl());
        importButton.Click += (_, _) => importMenu.Show(importButton, new Point(0, importButton.Height));
        var settingsButton = new Button { Text = "设置", AutoSize = true };
        var topBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4) };
        topBar.Controls.Add(new Label { Text = "环境:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        topBar.Controls.Add(_envCombo);
        topBar.Controls.Add(manageEnvButton);
        topBar.Controls.Add(importButton);
        topBar.Controls.Add(settingsButton);

        _collectionPanel = new CollectionPanel { Dock = DockStyle.Fill };
        _historyPanel = new HistoryPanel { Dock = DockStyle.Fill };
        var leftSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        leftSplit.Panel1.Controls.Add(_collectionPanel);
        leftSplit.Panel2.Controls.Add(_historyPanel);

        _editor = new RequestEditorPanel(_hover) { Dock = DockStyle.Fill };
        _responsePanel = new ResponsePanel { Dock = DockStyle.Fill };
        var rightSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        rightSplit.Panel1.Controls.Add(_editor);
        rightSplit.Panel2.Controls.Add(_responsePanel);

        var mainSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 260 };
        mainSplit.Panel1.Controls.Add(leftSplit);
        mainSplit.Panel2.Controls.Add(rightSplit);

        _statusLabel = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(_statusLabel);

        Controls.Add(mainSplit);
        Controls.Add(topBar);
        Controls.Add(statusStrip);

        _envCombo.SelectedIndexChanged += async (_, _) => await ActiveEnvironmentChangedAsync();
        manageEnvButton.Click += (_, _) => OpenEnvironmentManager();
        settingsButton.Click += (_, _) => OpenSettings();
        _collectionPanel.RequestOpened += CollectionPanel_RequestOpened;
        _collectionPanel.CollectionsChanged += async (_, affected) => await SaveCollectionsAsync(affected);
        _collectionPanel.CollectionDeleteRequested += async (_, collection) => await DeleteCollectionAsync(collection);
        _editor.SendRequested += async (_, _) => await SendOrCancelAsync();
        _editor.SaveRequested += (_, _) => SaveCurrentEditing();
        _historyPanel.ReplayRequested += (_, entry) => ReplayHistory(entry);
        _historyPanel.SearchRequested += async (_, keyword) => await RunHistorySearchAsync(keyword);
        _historyPanel.SearchDeeperRequested += async (_, _) => await RunDeeperSearchAsync();
        _historyPanel.SearchStopRequested += (_, _) => _deeperCts?.Cancel();
        Load += HermesPanel_Load;
    }

    /// <summary>FR-HERMES-012：编辑内容未保存时关闭需提示。</summary>
    public bool ConfirmClose()
    {
        if (!_editor.IsDirty)
        {
            return true;
        }

        DialogResult choice = MessageBox.Show(this,
            "当前请求有未保存的修改。是否保存？\n（是＝保存并关闭；否＝放弃修改；取消＝不关闭）",
            "未保存的修改", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
        switch (choice)
        {
            case DialogResult.Cancel:
                return false;
            case DialogResult.Yes when _editingRequest is not null:
                // 关闭在即，保存即发即弃：Store 不依赖控件，写盘在后台完成后进程自然收尾
                SaveCurrentEditing();
                return true;
            default:
                return true;
        }
    }

    private async void HermesPanel_Load(object? sender, EventArgs e)
    {
        // WinForms 事件处理允许 async void（规范 §5），内部必须 try-catch 兜底
        try
        {
            HermesSettingsLoadResult settingsResult = await _settingsStore.LoadAsync();
            _settings = settingsResult.Settings;
            if (settingsResult.RecoveredFromCorruption)
            {
                _logger.Warning("设置文件损坏，已备份到 {BackupPath} 并以默认值启动", settingsResult.BackupFilePath);
                _statusLabel.Text = "设置文件损坏，已备份原文件并以默认设置启动";
            }

            EnvironmentLoadResult environmentResult = await _environmentStore.LoadAsync();
            _environmentData = environmentResult.Data;
            if (environmentResult.RecoveredFromCorruption)
            {
                _logger.Warning("环境文件损坏，已备份到 {BackupPath} 并以空数据启动", environmentResult.BackupFilePath);
                _statusLabel.Text = "环境文件损坏，已备份原文件并以空数据启动";
            }

            CollectionStoreLoadResult collectionResult = await _collectionStore.LoadAllAsync();
            _collectionPanel.SetCollections(collectionResult.Collections);
            if (collectionResult.Recoveries.Count > 0)
            {
                _logger.Warning("发现 {Count} 个损坏的集合文件，已备份恢复", collectionResult.Recoveries.Count);
                _statusLabel.Text = $"{collectionResult.Recoveries.Count} 个集合文件损坏，已备份原文件并跳过";
            }

            await RefreshHistoryAsync();

            // 启动时后台归档检查（hermes.md §10.2，FR-HERMES-053）：即发即弃，不拖慢面板加载
            _ = RunStartupArchiveCheckAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Hermes 面板加载失败");
            _statusLabel.Text = $"加载失败：{ex.Message}";
        }
        finally
        {
            RefreshEnvironmentCombo();
            _suppressEvents = false;
        }
    }

    // ---------- 环境 ----------

    private void RefreshEnvironmentCombo()
    {
        _suppressEvents = true;
        try
        {
            _envCombo.Items.Clear();
            _envCombo.Items.Add(NoEnvironmentText);
            int selected = 0;
            for (int i = 0; i < _environmentData.Environments.Count; i++)
            {
                HermesEnvironment environment = _environmentData.Environments[i];
                // ComboBox 按 ToString 显示；HermesEnvironment 是 record，包一层显示名
                _envCombo.Items.Add(new EnvironmentItem(environment));
                if (environment.Id == _environmentData.ActiveId)
                {
                    selected = i + 1;
                }
            }

            _envCombo.SelectedIndex = selected;
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private async Task ActiveEnvironmentChangedAsync()
    {
        if (_suppressEvents)
        {
            return;
        }

        // 启用切换立即持久化（FR-HERMES-021）
        _environmentData = _envCombo.SelectedItem is EnvironmentItem item
            ? _environmentData with { ActiveId = item.Environment.Id }
            : _environmentData with { ActiveId = null };
        try
        {
            await _environmentStore.SaveAsync(_environmentData);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "保存环境启用状态失败");
            _statusLabel.Text = $"环境保存失败：{ex.Message}";
        }
    }

    private void OpenEnvironmentManager()
    {
        using var form = new EnvironmentManagerForm(_environmentData, data => _environmentStore.SaveAsync(data));
        form.ShowDialog(this);
        _environmentData = form.Data;
        RefreshEnvironmentCombo();
    }

    private void OpenSettings()
    {
        using var form = new HermesSettingsForm(_settings, settings => _settingsStore.SaveAsync(settings));
        form.SettingsChanged += (_, settings) =>
        {
            bool ignoreCertificateChanged = settings.IgnoreServerCertificate != _settings.IgnoreServerCertificate;
            _settings = settings;
            if (ignoreCertificateChanged)
            {
                // 证书校验开关变化 → 销毁重建双 client（hermes.md §5.2，FR-HERMES-008）
                _tool.ClientFactory.SetIgnoreServerCertificate(settings.IgnoreServerCertificate);
            }
        };
        form.ArchiveRequested += async (_, _) => await RunManualArchiveAsync(form);
        form.ShowDialog(this);
    }

    /// <summary>设置面板"立即归档"（FR-HERMES-053 手动入口）：执行归档并反馈结果。</summary>
    private async Task RunManualArchiveAsync(IWin32Window owner)
    {
        try
        {
            HistoryArchiveResult result = await _historyArchive.ArchiveOldFilesAsync();
            string message;
            if (result.ArchivedMonths.Count == 0 && result.SkippedMonths.Count == 0 && result.FailedMonths.Count == 0)
            {
                message = "没有需要归档的历史文件。";
            }
            else
            {
                var lines = new List<string>();
                if (result.ArchivedMonths.Count > 0)
                {
                    lines.Add($"已归档（{result.Compressor}）：{string.Join("、", result.ArchivedMonths)}");
                }
                if (result.SkippedMonths.Count > 0)
                {
                    lines.Add($"已跳过（归档包已存在，原文件保留）：{string.Join("、", result.SkippedMonths)}");
                }
                if (result.FailedMonths.Count > 0)
                {
                    lines.Add($"归档失败（原文件保留，详见日志）：{string.Join("、", result.FailedMonths)}");
                }

                message = string.Join('\n', lines);
            }

            MessageBox.Show(owner, message, "历史归档", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "手动归档历史失败");
            MessageBox.Show(owner, $"归档失败：{ex.Message}", "历史归档", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>启动时后台归档检查（hermes.md §10.2）；有归档动作时刷新历史列表并在状态栏提示。</summary>
    private async Task RunStartupArchiveCheckAsync()
    {
        try
        {
            HistoryArchiveResult result = await _historyArchive.ArchiveOldFilesAsync();
            if (result.ArchivedMonths.Count > 0)
            {
                _statusLabel.Text = $"已归档 {result.ArchivedMonths.Count} 个月的历史（{string.Join("、", result.ArchivedMonths)}，{result.Compressor}）";
                await RefreshHistoryAsync();
            }
        }
        catch (Exception ex)
        {
            // 归档是后台辅助动作，失败只提示不干扰主流程（原文件均保留）
            _logger.Error(ex, "启动归档检查失败");
            _statusLabel.Text = $"历史归档检查失败：{ex.Message}";
        }
    }

    private async Task SetVariableFromHoverAsync(string name, string value)
    {
        if (_environmentData.ActiveId is null)
        {
            return;
        }

        try
        {
            // 悬浮编辑与后事件脚本共用同一条写盘路径（hermes.md §6.1）
            _environmentData = await _environmentStore.SetVariableAsync(_environmentData.ActiveId, name, value);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "悬浮编辑保存变量 {VariableName} 失败", name);
            _statusLabel.Text = $"变量保存失败：{ex.Message}";
        }
    }

    // ---------- 集合树 ----------

    private void CollectionPanel_RequestOpened(object? sender, CollectionPanel.RequestNodeEventArgs args)
    {
        if (!ConfirmDiscardOrSave())
        {
            // 用户取消切换：把树选择还原回正在编辑的节点（程序设置不触发 AfterSelect 载入）
            if (_editingRequest is not null)
            {
                _collectionPanel.SelectTreeNode(_editingRequest.TreeNode);
            }

            return;
        }

        _editingRequest = args;
        _editor.LoadDraft(RequestDraft.FromNode(args.Node));
        _editor.MarkSaved();
        _editor.SaveEnabled = true;
        _statusLabel.Text = string.Empty;
    }

    private async Task SaveCollectionsAsync(IReadOnlyList<HermesCollection> affected)
    {
        foreach (HermesCollection collection in affected)
        {
            try
            {
                await _collectionStore.SaveAsync(collection);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "保存集合 {CollectionName} 失败", collection.Name);
                _statusLabel.Text = $"集合保存失败：{ex.Message}";
            }
        }
    }

    private async Task DeleteCollectionAsync(HermesCollection collection)
    {
        DialogResult confirm = MessageBox.Show(this, $"确定删除集合「{collection.Name}」？删除后不可恢复。", "删除集合",
            MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
        if (confirm != DialogResult.OK)
        {
            return;
        }

        try
        {
            await _collectionStore.DeleteAsync(collection.Id);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "删除集合 {CollectionName} 失败", collection.Name);
            _statusLabel.Text = $"集合删除失败：{ex.Message}";
            return;
        }

        if (_editingRequest is not null && ReferenceEquals(_editingRequest.Collection, collection))
        {
            ClearEditor();
        }

        _collectionPanel.RemoveCollection(collection);
    }

    private void SaveCurrentEditing()
    {
        if (_editingRequest is null)
        {
            return;
        }

        CollectionNode updated = _editor.CurrentDraft.ToNode(_editingRequest.Node.Name);
        _editingRequest = new CollectionPanel.RequestNodeEventArgs(_editingRequest.Collection, updated, _editingRequest.TreeNode);
        _collectionPanel.UpdateRequestNode(_editingRequest.TreeNode, updated);
        _editor.MarkSaved();
        _statusLabel.Text = "已保存";
    }

    /// <summary>有未保存修改时提示：保存 / 放弃 / 取消。返回 true 表示可以继续（已保存或放弃）。</summary>
    private bool ConfirmDiscardOrSave()
    {
        if (!_editor.IsDirty || _editingRequest is null)
        {
            return true;
        }

        DialogResult choice = MessageBox.Show(this,
            $"请求「{_editingRequest.Node.Name}」有未保存的修改。是否保存？",
            "未保存的修改", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        switch (choice)
        {
            case DialogResult.Yes:
                SaveCurrentEditing();
                return true;
            case DialogResult.No:
                return true;
            default:
                return false;
        }
    }

    private void ClearEditor()
    {
        _editingRequest = null;
        _editor.LoadDraft(RequestDraft.Empty);
        _editor.MarkSaved();
        _editor.SaveEnabled = false;
    }

    // ---------- 发送 ----------

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
            _statusLabel.Text = "请输入 URL";
            return;
        }

        PreparedRequest prepared = _tool.Orchestrator.Prepare(draft, _environmentData.FindActive());
        if (prepared.UndefinedVariables.Count > 0)
        {
            // FR-HERMES-022：未定义变量原样保留并提示
            _statusLabel.Text = $"未定义变量（已原样发送）：{string.Join("、", prepared.UndefinedVariables)}";
        }

        _sendCts = new CancellationTokenSource();
        _editor.SetSending(true);
        try
        {
            SendResult result = await _tool.Orchestrator.SendAsync(prepared, _settings, _sendCts.Token);

            // 后事件脚本（FR-HERMES-040/045）：只针对最终一跳执行一次；异常隔离进"脚本输出"页（FR-HERMES-043）
            ScriptExecutionResult? scriptResult = null;
            if (draft.PostResponseScript is not null)
            {
                scriptResult = await _scriptHost.RunAsync(
                    draft.PostResponseScript, result.FinalHop.Response, _environmentData, _settings, _sendCts.Token);
                if (scriptResult.UpdatedEnvironmentData is not null)
                {
                    // pm.environment.set/unset 已落盘（FR-HERMES-044），刷新环境下拉与悬浮编辑的数据源
                    _environmentData = scriptResult.UpdatedEnvironmentData;
                    RefreshEnvironmentCombo();
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

            _statusLabel.Text = status;

            // 历史落盘（hermes.md §5.1：只记最终一跳，异步追加）
            HistoryEntry entry = _tool.Orchestrator.BuildHistoryEntry(prepared, result, DateTimeOffset.Now);
            await _historyStore.AppendAsync(entry, _settings.ResponseBodyLimitBytes, _sendCts.Token);
            await RefreshHistoryAsync();
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "已取消";
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or UriFormatException)
        {
            _logger.Warning(ex, "请求发送失败");
            _responsePanel.ShowError($"发送失败：{ex.Message}");
            _statusLabel.Text = $"发送失败：{ex.Message}";
        }
        finally
        {
            _editor.SetSending(false);
            _sendCts.Dispose();
            _sendCts = null;
        }
    }

    // ---------- 导入（hermes.md §9） ----------

    private async Task ImportPostmanAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Postman 导出文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            Title = "导入 Postman Collection / Environment",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            string json = await File.ReadAllTextAsync(dialog.FileName);
            PostmanImportResult result = _postmanImporter.Import(
                json,
                [.. _collectionPanel.Collections.Select(c => c.Name)],
                [.. _environmentData.Environments.Select(e => e.Name)]);

            if (result.Collection is { } collection)
            {
                // 作为新集合追加，不覆盖已有数据（§9.1）
                await _collectionStore.SaveAsync(collection);
                _collectionPanel.AddCollection(collection);
                _statusLabel.Text = $"已导入集合「{collection.Name}」";
            }
            else if (result.Environment is { } environment)
            {
                _environmentData.Environments.Add(environment);
                await _environmentStore.SaveAsync(_environmentData);
                RefreshEnvironmentCombo();
                _statusLabel.Text = $"已导入环境「{environment.Name}」";
            }

            if (result.IgnoredItems.Count > 0)
            {
                MessageBox.Show(this, "导入完成，以下内容未导入：\n\n" + string.Join('\n', result.IgnoredItems),
                    "导入结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (PostmanImportException ex)
        {
            MessageBox.Show(this, ex.Message, "导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Postman 导入失败");
            _statusLabel.Text = $"导入失败：{ex.Message}";
        }
    }

    private void ImportCurl()
    {
        using var form = new CurlImportForm();
        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        CurlImportResult result;
        try
        {
            result = _curlImporter.Import(form.CommandText);
        }
        catch (FormatException ex)
        {
            MessageBox.Show(this, ex.Message, "cURL 导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (!ConfirmDiscardOrSave())
        {
            return;
        }

        // 加载到当前编辑区，不自动入集合（FR-HERMES-034）
        _editingRequest = null;
        _editor.LoadDraft(result.Draft);
        _editor.MarkSaved();
        _editor.SaveEnabled = false;
        _statusLabel.Text = "已从 cURL 导入到编辑区（未入集合，需保存请先在集合树中选中请求）";

        var notes = new List<string>(result.IgnoredArguments);
        if (result.HasInsecureFlag)
        {
            notes.Add("-k/--insecure：未映射为请求属性；如需忽略证书校验，请在“设置”中开启全局开关");
        }

        if (notes.Count > 0)
        {
            MessageBox.Show(this, "导入完成，以下参数被忽略：\n\n" + string.Join('\n', notes),
                "cURL 导入结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    // ---------- 历史 ----------

    /// <summary>历史搜索框防抖结束（FR-HERMES-054）：空关键词恢复最近列表；否则直搜未压缩 jsonl。</summary>
    private async Task RunHistorySearchAsync(string keyword)
    {
        // 直搜与归档搜索互斥：换关键词先停掉进行中的归档搜索
        _deeperCts?.Cancel();
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        _currentKeyword = keyword;
        CancellationToken cancellationToken = _searchCts.Token;

        if (keyword.Length == 0)
        {
            _historyPanel.SetDeeperSearchAvailable(false);
            await RefreshHistoryAsync();
            return;
        }

        try
        {
            HistorySearchResult result = await _historySearch.SearchRecentAsync(keyword, cancellationToken);
            _historyPanel.ShowSearchResults(result.Entries);
            // 结果为空且存在归档包时显示"搜索更久"按钮（hermes.md §3）
            _historyPanel.SetDeeperSearchAvailable(result.Entries.Count == 0 && _historySearch.HasArchives());

            string status = result.Entries.Count == 0
                ? "未找到匹配的历史记录"
                : $"找到 {result.Entries.Count} 条匹配的历史记录";
            if (result.SkippedLines > 0)
            {
                status += $"（另有 {result.SkippedLines} 行命中但损坏无法展示）";
            }

            _statusLabel.Text = status;
        }
        catch (OperationCanceledException)
        {
            // 被更新的搜索取代，无需提示
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "搜索历史失败");
            _statusLabel.Text = $"历史搜索失败：{ex.Message}";
        }
    }

    /// <summary>"搜索更久"（FR-HERMES-055）：从最新到最旧逐包搜索归档，每包刷新一次结果；可停止。</summary>
    private async Task RunDeeperSearchAsync()
    {
        string keyword = _currentKeyword;
        if (keyword.Length == 0 || _deeperCts is not null)
        {
            return;
        }

        _deeperCts = new CancellationTokenSource();
        _historyPanel.SetDeeperSearchRunning(true);

        var accumulated = new List<HistoryEntry>();
        int packages = 0;
        try
        {
            await foreach (HistorySearchBatch batch in _historySearch.SearchArchivesAsync(keyword, _deeperCts.Token))
            {
                packages++;
                accumulated.AddRange(batch.Entries);
                // 每处理完一个包刷新一次结果（hermes.md §10.3）；按钮只在直搜为空时出现，故直接替换列表
                _historyPanel.ShowSearchResults([.. accumulated]);
                _statusLabel.Text = $"正在搜索归档：已处理 {batch.ArchiveName}，累计命中 {accumulated.Count} 条";
            }

            _statusLabel.Text = $"归档搜索完成：共处理 {packages} 个归档包，命中 {accumulated.Count} 条";
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = $"已停止归档搜索（已处理 {packages} 个包，命中 {accumulated.Count} 条）";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "归档历史搜索失败");
            _statusLabel.Text = $"归档搜索失败：{ex.Message}";
        }
        finally
        {
            _historyPanel.SetDeeperSearchRunning(false);
            _deeperCts.Dispose();
            _deeperCts = null;
        }
    }

    private async Task RefreshHistoryAsync()
    {
        try
        {
            _historyPanel.SetEntries(await _historyReader.ReadRecentAsync());
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "读取历史记录失败");
            _statusLabel.Text = $"历史读取失败：{ex.Message}";
        }
    }

    private void ReplayHistory(HistoryEntry entry)
    {
        if (!ConfirmDiscardOrSave())
        {
            return;
        }

        // 重放到编辑区（FR-HERMES-052）：不绑定树节点，用户自行保存
        string? contentType = entry.Request.Headers
            .FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))?.Value;
        var draft = new RequestDraft
        {
            Method = entry.Request.Method,
            Url = entry.Request.Url,
            Headers = [.. entry.Request.Headers.Select(h => new KeyValueEntry(h.Key, h.Value))],
            Body = entry.Request.Body is null
                ? null
                : new RequestBody { Kind = RequestBodyKind.Raw, ContentType = contentType, Text = entry.Request.Body },
        };
        _editingRequest = null;
        _editor.LoadDraft(draft);
        _editor.MarkSaved();
        _editor.SaveEnabled = false;
        _statusLabel.Text = $"已重放历史记录（{entry.Timestamp:MM-dd HH:mm:ss}）";
    }

    /// <summary>环境下拉项：按环境名显示。</summary>
    private sealed record EnvironmentItem(HermesEnvironment Environment)
    {
        public override string ToString() => Environment.Name;
    }
}

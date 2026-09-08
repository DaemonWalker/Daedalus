using System.Windows.Forms;

using Daedalus.Abstractions;
using Daedalus.Tools.Hermes.Collections;
using Daedalus.Tools.Hermes.Editing;
using Daedalus.Tools.Hermes.History;
using Daedalus.Tools.Hermes.Settings;
using Daedalus.Tools.Hermes.Variables;

using Microsoft.Extensions.DependencyInjection;

using Serilog;

namespace Daedalus.Tools.Hermes.View;

/// <summary>
/// Hermes 主面板（hermes.md §3）：顶部环境栏、左侧集合树/历史、右侧请求多标签页（<see cref="RequestTabView"/>）、
/// 底部状态栏。请求编辑/响应/发送等按 tab 隔离的状态全部在 RequestTabView；面板保留共享职责：
/// 环境栏与环境缓存、状态栏、历史搜索、导入、布局持久化与 tab 管理。
/// </summary>
internal sealed class HermesPanel : UserControl, IToolCloseConfirmation
{
    private const string NoEnvironmentText = "（未启用）";
    private const int CloseButtonSize = 16;

    // 无布局记录且无活动 tab 时落盘使用的默认右栏比例（与 hermes.md §11.4 示例一致）
    private const double DefaultRightRatio = 0.55;

    private readonly ILogger _logger;
    private readonly CollectionStore _collectionStore;
    private readonly EnvironmentStore _environmentStore;
    private readonly HermesSettingsStore _settingsStore;
    private readonly RecentHistoryReader _historyReader;
    private readonly HistoryArchive _historyArchive;
    private readonly HistorySearch _historySearch;

    // 当前标签页 scope 的 provider（MS DI 解析面板时注入）：新建 tab 用它 ActivatorUtilities 手工构造，
    // transient 依赖（ScriptHost/ResponseBeautifier 等）随 scope 与面板同生灭
    private readonly IServiceProvider _services;

    private readonly VariableHoverController _hover;
    private readonly PostmanImporter _postmanImporter = new();
    private readonly CurlImporter _curlImporter = new();

    private readonly ComboBox _envCombo;
    private readonly CollectionPanel _collectionPanel;
    private readonly HistoryPanel _historyPanel;
    private readonly ToolStripStatusLabel _statusLabel;
    private readonly SplitContainer _mainSplit;
    private readonly SplitContainer _leftSplit;
    private readonly TabControl _tabs;
    private readonly TabPage _plusPage;

    // 各请求标签页 × 按钮的命中区域，在 OwnerDraw 时计算；标签页增删后索引位移，需清空重算
    private readonly Dictionary<int, Rectangle> _closeButtonBounds = [];

    private HermesSettings _settings = HermesSettings.Default;
    private EnvironmentData _environmentData = EnvironmentData.Empty;

    // 程序还原布局期间抑制 SplitterMoved 落盘，避免刚读出的比例被立即覆盖回写
    private bool _restoringLayout;

    // Load 完成（布局还原结束）后才允许 SplitterMoved 落盘：初始化布局期间 splitter 位置被动调整
    // 也会触发 SplitterMoved，不拦住会把默认布局覆盖写回刚读出的比例
    private bool _layoutLoaded;

    // 历史搜索状态：_searchCts 管直搜；_deeperCts 非 null 表示归档搜索进行中（"搜索更久"按钮此时为"停止"）
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _deeperCts;
    private string _currentKeyword = string.Empty;

    // 移除标签页期间抑制 ＋tab 的自动新建（选中项被动移到 ＋tab 不应又开草稿）
    private bool _suppressPlusCreate;

    // 加载/刷新环境下拉期间抑制事件，避免把未加载完的状态写回 environments.json
    private bool _suppressEvents = true;

    /// <summary>
    /// 构造注入（step 14/19，hermes.md §4.1）：Store 等为跨标签共享的 singleton，子面板为 transient；
    /// 请求编辑相关服务（SendOrchestrator/ScriptHost/ResponseBeautifier 等）不再进面板，
    /// 由各 <see cref="RequestTabView"/> 经 <paramref name="services"/>（当前 scope）手工构造。
    /// </summary>
    public HermesPanel(
        ILogger logger,
        CollectionStore collectionStore,
        EnvironmentStore environmentStore,
        HermesSettingsStore settingsStore,
        RecentHistoryReader historyReader,
        HistoryArchive historyArchive,
        HistorySearch historySearch,
        CollectionPanel collectionPanel,
        HistoryPanel historyPanel,
        IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _collectionStore = collectionStore;
        _environmentStore = environmentStore;
        _settingsStore = settingsStore;
        _historyReader = historyReader;
        _historyArchive = historyArchive;
        _historySearch = historySearch;
        _services = services;
        _hover = new VariableHoverController(() => _environmentData.FindActive(), SetVariableFromHoverAsync);

        _envCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        var manageEnvButton = new Button { Text = "管理环境", AutoSize = true };
        var importButton = new Button { Text = "导入 ▾", AutoSize = true };
        var importMenu = new ContextMenuStrip();
        importMenu.Items.Add("从 Postman 文件导入…", null, async (_, _) => await ImportPostmanAsync());
        importMenu.Items.Add("从 cURL 命令导入…", null, (_, _) => ImportCurl());
        importButton.Click += (_, _) => importMenu.Show(importButton, new Point(0, importButton.Height));
        var topBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4) };
        topBar.Controls.Add(new Label { Text = "环境:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        topBar.Controls.Add(_envCombo);
        topBar.Controls.Add(manageEnvButton);
        topBar.Controls.Add(importButton);

        // 子面板由容器以 transient 注入；运行时委托（悬浮编辑）与各请求 tab 保留手工接线
        _collectionPanel = collectionPanel;
        _collectionPanel.Dock = DockStyle.Fill;
        _historyPanel = historyPanel;
        _historyPanel.Dock = DockStyle.Fill;
        _leftSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        _leftSplit.Panel1.Controls.Add(_collectionPanel);
        _leftSplit.Panel2.Controls.Add(_historyPanel);

        // 右栏为请求多标签页（step 19）：OwnerDraw 画 × 关闭按钮，画法参照外壳 MainForm；
        // 末尾固定 ＋tab，选中即新建空白草稿 tab
        _tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            DrawMode = TabDrawMode.OwnerDrawFixed,
            Padding = new Point(20, 4),
        };
        _plusPage = new TabPage("＋");
        _tabs.TabPages.Add(_plusPage);
        _tabs.DrawItem += Tabs_DrawItem;
        _tabs.MouseClick += Tabs_MouseClick;
        _tabs.SelectedIndexChanged += Tabs_SelectedIndexChanged;

        _mainSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 260 };
        _mainSplit.Panel1.Controls.Add(_leftSplit);
        _mainSplit.Panel2.Controls.Add(_tabs);

        _statusLabel = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(_statusLabel);

        Controls.Add(_mainSplit);
        Controls.Add(topBar);
        Controls.Add(statusStrip);

        _envCombo.SelectedIndexChanged += async (_, _) => await ActiveEnvironmentChangedAsync();
        manageEnvButton.Click += (_, _) => OpenEnvironmentManager();
        _settingsStore.Changed += SettingsStore_Changed;
        _mainSplit.SplitterMoved += async (_, _) => await SaveLayoutAsync();
        _leftSplit.SplitterMoved += async (_, _) => await SaveLayoutAsync();
        _collectionPanel.RequestOpened += CollectionPanel_RequestOpened;
        _collectionPanel.CollectionsChanged += async (_, affected) => await SaveCollectionsAsync(affected);
        _collectionPanel.CollectionDeleteRequested += async (_, collection) => await DeleteCollectionAsync(collection);
        _historyPanel.ReplayRequested += (_, entry) => ReplayHistory(entry);
        _historyPanel.SearchRequested += async (_, keyword) => await RunHistorySearchAsync(keyword);
        _historyPanel.SearchDeeperRequested += async (_, _) => await RunDeeperSearchAsync();
        _historyPanel.SearchStopRequested += (_, _) => _deeperCts?.Cancel();
        Load += HermesPanel_Load;
    }

    /// <summary>FR-HERMES-012：逐个咨询各请求标签页（先激活被询问的 tab），任一取消则中止关闭。</summary>
    public bool ConfirmClose()
    {
        foreach (TabPage page in _tabs.TabPages)
        {
            if (page.Tag is not RequestTabView tab)
            {
                continue;
            }

            _tabs.SelectedTab = page;
            if (!tab.ConfirmClose())
            {
                return false;
            }
        }

        return true;
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

            // Load 事件在控件首次显示时触发，此时分隔条尺寸已确定，可以安全设置 SplitterDistance
            if (_settings.Layout is { } layout)
            {
                ApplyLayout(layout);
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

            // 启动即开一个空白草稿 tab（step 19）
            NewRequestTab();

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
            _layoutLoaded = true;
        }
    }

    // ---------- 请求标签页管理（step 19） ----------

    /// <summary>新建空白草稿 tab（插入 ＋tab 左侧并选中）；有布局记录时应用 rightRatio。</summary>
    private RequestTabView NewRequestTab()
    {
        var tab = ActivatorUtilities.CreateInstance<RequestTabView>(
            _services,
            _hover,
            (Func<EnvironmentData>)(() => _environmentData),
            (Func<HermesSettings>)(() => _settings));
        tab.Dock = DockStyle.Fill;
        var page = new TabPage();
        page.Controls.Add(tab);
        page.Tag = tab;

        tab.StatusChanged += (_, text) => _statusLabel.Text = text;
        tab.DirtyChanged += (_, _) => UpdateTabTitle(page, tab);
        tab.TitleChanged += (_, _) => UpdateTabTitle(page, tab);
        tab.SaveToCollectionRequested += RequestTab_SaveToCollectionRequested;
        tab.EnvironmentUpdated += (_, data) =>
        {
            _environmentData = data;
            RefreshEnvironmentCombo();
        };
        tab.HistoryChanged += async (_, _) => await RefreshHistoryAsync();
        tab.SplitterMoved += (_, _) => RequestTab_SplitterMoved(tab);

        _tabs.TabPages.Insert(_tabs.TabPages.IndexOf(_plusPage), page);
        _closeButtonBounds.Clear();
        if (_settings.Layout is { } layout)
        {
            tab.ApplyRightRatio(layout.RightRatio);
        }

        _tabs.SelectedTab = page;
        UpdateTabTitle(page, tab);
        return tab;
    }

    /// <summary>移除并释放一个请求 tab（调用方须已完成关闭确认）。</summary>
    private void RemoveRequestTab(TabPage page)
    {
        _suppressPlusCreate = true;
        try
        {
            _tabs.TabPages.Remove(page);
        }
        finally
        {
            _suppressPlusCreate = false;
        }

        _closeButtonBounds.Clear();
        page.Dispose();
    }

    /// <summary>标签标题 = 请求名 + 未保存标记（*）。</summary>
    private static void UpdateTabTitle(TabPage page, RequestTabView tab) =>
        page.Text = tab.IsDirty ? tab.Title + " *" : tab.Title;

    private void Tabs_SelectedIndexChanged(object? sender, EventArgs e)
    {
        // 选中末尾 ＋tab 即在其左侧新建空白草稿 tab 并选中它
        if (!_suppressPlusCreate && _tabs.SelectedTab == _plusPage)
        {
            NewRequestTab();
        }
    }

    private void Tabs_DrawItem(object? sender, DrawItemEventArgs e)
    {
        e.DrawBackground();
        TabPage page = _tabs.TabPages[e.Index];
        Rectangle tabBounds = _tabs.GetTabRect(e.Index);
        if (page == _plusPage)
        {
            TextRenderer.DrawText(e.Graphics, page.Text, _tabs.Font, tabBounds, _tabs.ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        var textBounds = new Rectangle(
            tabBounds.X + 6,
            tabBounds.Y,
            tabBounds.Width - CloseButtonSize - 16,
            tabBounds.Height);
        TextRenderer.DrawText(
            e.Graphics,
            page.Text,
            _tabs.Font,
            textBounds,
            _tabs.ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        var closeBounds = new Rectangle(
            tabBounds.Right - CloseButtonSize - 6,
            tabBounds.Y + (tabBounds.Height - CloseButtonSize) / 2,
            CloseButtonSize,
            CloseButtonSize);
        _closeButtonBounds[e.Index] = closeBounds;
        TextRenderer.DrawText(
            e.Graphics,
            "×",
            _tabs.Font,
            closeBounds,
            Color.Gray,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private void Tabs_MouseClick(object? sender, MouseEventArgs e)
    {
        for (int i = 0; i < _tabs.TabPages.Count; i++)
        {
            TabPage page = _tabs.TabPages[i];
            if (page == _plusPage || page.Tag is not RequestTabView tab)
            {
                continue;
            }

            bool isHit = e.Button == MouseButtons.Middle
                ? _tabs.GetTabRect(i).Contains(e.Location)
                : e.Button == MouseButtons.Left
                    && _closeButtonBounds.TryGetValue(i, out Rectangle bounds)
                    && bounds.Contains(e.Location);
            if (isHit)
            {
                // 先激活被关闭的 tab，让用户看清确认框针对哪个请求
                _tabs.SelectedTab = page;
                if (tab.ConfirmClose())
                {
                    RemoveRequestTab(page);
                }

                return;
            }
        }
    }

    private void RequestTab_SaveToCollectionRequested(object? sender, RequestTabView tab)
    {
        // tab 已把更新后的节点放进 BoundTreeNode；树写回会触发 CollectionsChanged → 集合持久化
        if (tab.BoundTreeNode is { } bound)
        {
            _collectionPanel.UpdateRequestNode(bound.TreeNode, bound.Node);
        }
    }

    /// <summary>任一 tab 拖动分隔条：比例落盘并同步应用到全部 tab（ApplyRightRatio 内部抑制事件，不递归）。</summary>
    private void RequestTab_SplitterMoved(RequestTabView source)
    {
        if (_restoringLayout || !_layoutLoaded || source.RightRatio is not { } ratio)
        {
            return;
        }

        foreach (TabPage page in _tabs.TabPages)
        {
            if (page.Tag is RequestTabView tab && !ReferenceEquals(tab, source))
            {
                tab.ApplyRightRatio(ratio);
            }
        }

        _ = SaveLayoutAsync(ratio);
    }

    // ---------- 布局持久化（hermes.md §11.4，FR-HERMES-061） ----------

    /// <summary>按比例还原三个分隔条；rightRatio 作用于各请求 tab 内分隔条。每个字段独立校验 ∈ (0,1)，非法字段按缺失处理。</summary>
    private void ApplyLayout(HermesLayout layout)
    {
        _restoringLayout = true;
        try
        {
            ApplyRatio(_mainSplit, layout.MainRatio);
            ApplyRatio(_leftSplit, layout.LeftRatio);
            foreach (TabPage page in _tabs.TabPages)
            {
                if (page.Tag is RequestTabView tab)
                {
                    tab.ApplyRightRatio(layout.RightRatio);
                }
            }
        }
        finally
        {
            _restoringLayout = false;
        }
    }

    /// <summary>尺寸未就绪（Horizontal 分隔条在高度为 0 时设 SplitterDistance 会抛异常）或比例非法时跳过。</summary>
    private static void ApplyRatio(SplitContainer split, double ratio)
    {
        int totalSize = split.Orientation == Orientation.Vertical ? split.Width : split.Height;
        if (!HermesLayout.IsValidRatio(ratio) || totalSize <= 0)
        {
            return;
        }

        split.SplitterDistance = HermesLayout.RatioToDistance(
            ratio, totalSize, split.Panel1MinSize, split.Panel2MinSize, split.SplitterWidth);
    }

    /// <summary>SplitterMoved（拖动结束）时按比例落盘；还原过程由 _restoringLayout 抑制，不回写。</summary>
    private async Task SaveLayoutAsync(double? rightRatio = null)
    {
        if (_restoringLayout || !_layoutLoaded)
        {
            return;
        }

        // 尺寸未就绪时不存（比例会算成 0/非法值）
        if (_mainSplit.Width <= 0 || _leftSplit.Height <= 0)
        {
            return;
        }

        var layout = new HermesLayout(
            HermesLayout.DistanceToRatio(_mainSplit.SplitterDistance, _mainSplit.Width),
            HermesLayout.DistanceToRatio(_leftSplit.SplitterDistance, _leftSplit.Height),
            rightRatio ?? ActiveRequestTab()?.RightRatio ?? _settings.Layout?.RightRatio ?? DefaultRightRatio);
        _settings = _settings with { Layout = layout };
        try
        {
            await _settingsStore.SaveAsync(_settings);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "保存布局失败");
            _statusLabel.Text = $"布局保存失败：{ex.Message}";
        }
    }

    private RequestTabView? ActiveRequestTab() => _tabs.SelectedTab?.Tag as RequestTabView;

    // Store 为跨标签共享 singleton：设置经统一设置窗口修改后广播到此，同步本面板的发送参数副本，
    // 否则后续布局落盘（SaveLayoutAsync 整体回写 settings.json）会把新设置覆盖回旧值
    private void SettingsStore_Changed(object? sender, HermesSettings settings)
    {
        _settings = settings;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Store 是进程级 singleton，不退订会让已关闭的面板一直被它引用
            _settingsStore.Changed -= SettingsStore_Changed;
            // 悬浮弹窗是独立 Form，不随控件树释放，由 controller 统一 Dispose
            _hover.Dispose();
        }

        base.Dispose(disposing);
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
        // 按 TreeNode 引用查重：已打开则聚焦，不再弹切换保存确认（step 19）
        foreach (TabPage page in _tabs.TabPages)
        {
            if (page.Tag is RequestTabView tab && ReferenceEquals(tab.BoundTreeNode?.TreeNode, args.TreeNode))
            {
                _tabs.SelectedTab = page;
                return;
            }
        }

        NewRequestTab().LoadNode(args);
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

        SyncTabBindings();
    }

    /// <summary>
    /// 集合变更后核对各 tab 的树绑定：TreeNode 仍在树中（含重命名/保存的原地更新）不动；
    /// 拖拽移动会摘除旧 TreeNode 新建同节点 TreeNode——找回则改绑；找不到则节点已删除，解绑（保存禁用、标题保留）。
    /// </summary>
    private void SyncTabBindings()
    {
        foreach (TabPage page in _tabs.TabPages)
        {
            if (page.Tag is not RequestTabView tab || tab.BoundTreeNode is not { } bound || bound.TreeNode.TreeView is not null)
            {
                continue;
            }

            if (_collectionPanel.FindRequestTreeNode(bound.Node) is { } found)
            {
                tab.Rebind(new CollectionPanel.RequestNodeEventArgs(found.Collection, bound.Node, found.TreeNode));
            }
            else
            {
                tab.Unbind();
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

        // 绑定该集合的 tab 逐个确认（脏时保存/放弃/取消），任一取消则中止删除
        var boundPages = _tabs.TabPages.Cast<TabPage>()
            .Where(p => p.Tag is RequestTabView t
                && t.BoundTreeNode is not null
                && ReferenceEquals(t.BoundTreeNode.Collection, collection))
            .ToList();
        foreach (TabPage page in boundPages)
        {
            _tabs.SelectedTab = page;
            if (page.Tag is RequestTabView tab && !tab.ConfirmClose())
            {
                return;
            }
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

        foreach (TabPage page in boundPages)
        {
            RemoveRequestTab(page);
        }

        _collectionPanel.RemoveCollection(collection);
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

        // 导入为新的游离 tab（step 19）：不绑定树节点、保存禁用，不自动入集合（FR-HERMES-034）
        NewRequestTab().LoadDraft(result.Draft);
        _statusLabel.Text = "已从 cURL 导入到新标签页（未入集合）";

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
        // 重放为新的游离 tab（step 19，FR-HERMES-052）：不绑定树节点、保存禁用
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
        NewRequestTab().LoadDraft(draft);
        _statusLabel.Text = $"已重放历史记录（{entry.Timestamp:MM-dd HH:mm:ss}）到新标签页";
    }

    /// <summary>环境下拉项：按环境名显示。</summary>
    private sealed record EnvironmentItem(HermesEnvironment Environment)
    {
        public override string ToString() => Environment.Name;
    }
}

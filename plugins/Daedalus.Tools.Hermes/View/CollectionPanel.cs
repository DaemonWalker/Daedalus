using System.Windows.Forms;

using Daedalus.Tools.Hermes.Collections;

namespace Daedalus.Tools.Hermes.View;

/// <summary>
/// 集合树面板（hermes.md §3，FR-HERMES-010/011）：集合 → 文件夹（可嵌套）→ 请求，
/// 右键菜单增删改/重命名，拖拽移动节点。持久化委托给宿主面板（每次变更后保存对应集合）。
/// </summary>
internal sealed class CollectionPanel : UserControl
{
    private readonly TreeView _tree;
    private readonly ContextMenuStrip _menu;

    // 拖拽中携带的节点
    private TreeNode? _draggedNode;

    public CollectionPanel()
    {
        _tree = new TreeView { Dock = DockStyle.Fill, AllowDrop = true, HideSelection = false };

        _menu = new ContextMenuStrip();
        _menu.Opening += Menu_Opening;
        _tree.ContextMenuStrip = _menu;
        _tree.NodeMouseDoubleClick += Tree_NodeMouseDoubleClick;
        _tree.AfterSelect += Tree_AfterSelect;
        _tree.ItemDrag += Tree_ItemDrag;
        _tree.DragEnter += Tree_DragEnter;
        _tree.DragDrop += Tree_DragDrop;

        Controls.Add(_tree);
    }

    /// <summary>选中了请求节点（双击或单击选中均触发载入由宿主决定；这里只在双击时请求载入）。</summary>
    public event EventHandler<RequestNodeEventArgs>? RequestOpened;

    /// <summary>树内容变更（增删改/拖拽），宿主需持久化涉及的集合。</summary>
    public event EventHandler<IReadOnlyList<HermesCollection>>? CollectionsChanged;

    /// <summary>请求删除某集合（宿主确认后调 <see cref="RemoveCollection"/>）。</summary>
    public event EventHandler<HermesCollection>? CollectionDeleteRequested;

    /// <summary>当前全部集合。</summary>
    public List<HermesCollection> Collections { get; private set; } = [];

    /// <summary>当前选中节点携带的请求节点；非请求返回 null。</summary>
    public (HermesCollection Collection, CollectionNode Node, TreeNode TreeNode)? SelectedRequest
    {
        get
        {
            if (_tree.SelectedNode?.Tag is CollectionNode { Type: CollectionNodeType.Request } node
                && FindCollectionRoot(_tree.SelectedNode) is { } root)
            {
                // FindCollectionRoot 返回的根 Tag 必然是 HermesCollection（建树时设定）
                return ((HermesCollection)root.Tag!, node, _tree.SelectedNode);
            }

            return null;
        }
    }

    /// <summary>用集合清单重建整棵树。</summary>
    public void SetCollections(IReadOnlyList<HermesCollection> collections)
    {
        Collections = [.. collections];
        _tree.BeginUpdate();
        try
        {
            _tree.Nodes.Clear();
            foreach (HermesCollection collection in Collections)
            {
                TreeNode root = _tree.Nodes.Add(collection.Name);
                root.Tag = collection;
                BuildChildren(root, collection.Items);
                root.Expand();
            }
        }
        finally
        {
            _tree.EndUpdate();
        }
    }

    /// <summary>移除集合并刷新。</summary>
    public void RemoveCollection(HermesCollection collection)
    {
        List<HermesCollection> remaining = [.. Collections.Where(c => c != collection)];
        SetCollections(remaining);
    }

    private static void BuildChildren(TreeNode parent, List<CollectionNode> items)
    {
        foreach (CollectionNode node in items)
        {
            TreeNode treeNode = parent.Nodes.Add(node.Name);
            treeNode.Tag = node;
            if (node.Type == CollectionNodeType.Folder)
            {
                BuildChildren(treeNode, node.Items ??= []);
            }
        }
    }

    private void Menu_Opening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // 右键时先选中节点，再按节点种类出菜单
        Point point = _tree.PointToClient(System.Windows.Forms.Cursor.Position);
        TreeNode? hit = _tree.GetNodeAt(point);
        if (hit is not null)
        {
            _tree.SelectedNode = hit;
        }

        e.Cancel = false;
        _menu.Items.Clear();
        TreeNode? selected = _tree.SelectedNode;

        if (selected is null)
        {
            _menu.Items.Add("新建集合", null, (_, _) => CreateCollection());
            return;
        }

        switch (selected.Tag)
        {
            case HermesCollection collection:
                _menu.Items.Add("新建文件夹", null, (_, _) => CreateNode(selected, collection.Items, CollectionNodeType.Folder));
                _menu.Items.Add("新建请求", null, (_, _) => CreateNode(selected, collection.Items, CollectionNodeType.Request));
                _menu.Items.Add("重命名集合", null, (_, _) => RenameCollection(selected, collection));
                _menu.Items.Add("删除集合", null, (_, _) => CollectionDeleteRequested?.Invoke(this, collection));
                break;
            case CollectionNode { Type: CollectionNodeType.Folder } folder:
                _menu.Items.Add("新建子文件夹", null, (_, _) => CreateNode(selected, folder.Items ??= [], CollectionNodeType.Folder));
                _menu.Items.Add("新建请求", null, (_, _) => CreateNode(selected, folder.Items ??= [], CollectionNodeType.Request));
                _menu.Items.Add("重命名", null, (_, _) => RenameNode(selected, folder));
                _menu.Items.Add("删除", null, (_, _) => DeleteNode(selected));
                break;
            case CollectionNode request:
                _menu.Items.Add("重命名", null, (_, _) => RenameNode(selected, request));
                _menu.Items.Add("删除", null, (_, _) => DeleteNode(selected));
                break;
        }

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("新建集合", null, (_, _) => CreateCollection());
    }

    private void CreateCollection()
    {
        string? name = InputDialog.Prompt(this, "新建集合", "集合名：");
        if (name is null)
        {
            return;
        }

        var collection = new HermesCollection { Id = IdGenerator.NewId(), Name = name };
        SetCollections([.. Collections, collection]);
        NotifyChanged(collection);
    }

    private void CreateNode(TreeNode parentTreeNode, List<CollectionNode> siblings, CollectionNodeType type)
    {
        string? name = InputDialog.Prompt(this, type == CollectionNodeType.Folder ? "新建文件夹" : "新建请求", "名称：");
        if (name is null)
        {
            return;
        }

        var node = new CollectionNode
        {
            Type = type,
            Name = name,
            Items = type == CollectionNodeType.Folder ? [] : null,
        };
        siblings.Add(node);
        TreeNode treeNode = parentTreeNode.Nodes.Add(name);
        treeNode.Tag = node;
        parentTreeNode.Expand();
        _tree.SelectedNode = treeNode;

        HermesCollection? changedOwner = CollectionOf(parentTreeNode);
        if (changedOwner is not null)
        {
            NotifyChanged(changedOwner);
        }

        // 新建请求后立即打开编辑
        if (type == CollectionNodeType.Request && changedOwner is { } owner)
        {
            RequestOpened?.Invoke(this, new RequestNodeEventArgs(owner, node, treeNode));
        }
    }

    private void RenameCollection(TreeNode treeNode, HermesCollection collection)
    {
        string? name = InputDialog.Prompt(this, "重命名集合", "集合名：", collection.Name);
        if (name is null)
        {
            return;
        }

        int index = Collections.IndexOf(collection);
        // with 表达式需重赋 required 成员（Id/Name）
        var renamed = collection with { Id = collection.Id, Name = name };
        List<HermesCollection> updated = [.. Collections];
        updated[index] = renamed;
        treeNode.Tag = renamed;
        treeNode.Text = name;
        Collections = updated;
        NotifyChanged(renamed);
    }

    private void RenameNode(TreeNode treeNode, CollectionNode node)
    {
        string? name = InputDialog.Prompt(this, "重命名", "名称：", node.Name);
        if (name is null)
        {
            return;
        }

        // with 表达式需重赋 required 成员（Type/Name）
        ReplaceNode(treeNode, node with { Type = node.Type, Name = name });
    }

    private void DeleteNode(TreeNode treeNode)
    {
        if (treeNode.Tag is not CollectionNode node)
        {
            return;
        }

        DialogResult confirm = MessageBox.Show(this, $"确定删除「{node.Name}」？删除后不可恢复。", "删除",
            MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
        if (confirm != DialogResult.OK)
        {
            return;
        }

        HermesCollection? owner = CollectionOf(treeNode);
        SiblingsOf(treeNode)?.Remove(node);
        treeNode.Remove();
        if (owner is not null)
        {
            NotifyChanged(owner);
        }
    }

    private void Tree_NodeMouseDoubleClick(object? sender, TreeNodeMouseClickEventArgs e)
    {
        if (e.Node is { } treeNode
            && treeNode.Tag is CollectionNode { Type: CollectionNodeType.Request } node
            && CollectionOf(treeNode) is { } owner)
        {
            RequestOpened?.Invoke(this, new RequestNodeEventArgs(owner, node, treeNode));
        }
    }

    private void Tree_AfterSelect(object? sender, TreeViewEventArgs e)
    {
        // 单击选中请求也载入编辑区（与 Postman 一致）
        if (e.Action is TreeViewAction.ByMouse or TreeViewAction.ByKeyboard
            && e.Node?.Tag is CollectionNode { Type: CollectionNodeType.Request } node
            && CollectionOf(e.Node) is { } owner)
        {
            RequestOpened?.Invoke(this, new RequestNodeEventArgs(owner, node, e.Node));
        }
    }

    private void Tree_ItemDrag(object? sender, ItemDragEventArgs e)
    {
        // 集合根节点不参与拖拽
        if (e.Item is TreeNode { Tag: CollectionNode } node)
        {
            _draggedNode = node;
            _tree.DoDragDrop(node, DragDropEffects.Move);
        }
    }

    private void Tree_DragEnter(object? sender, DragEventArgs e)
    {
        TreeNode? target = _tree.GetNodeAt(_tree.PointToClient(new Point(e.X, e.Y)));
        e.Effect = IsValidDropTarget(target) ? DragDropEffects.Move : DragDropEffects.None;
    }

    private void Tree_DragDrop(object? sender, DragEventArgs e)
    {
        TreeNode? target = _tree.GetNodeAt(_tree.PointToClient(new Point(e.X, e.Y)));
        if (_draggedNode is null || !IsValidDropTarget(target) || target is null)
        {
            _draggedNode = null;
            return;
        }

        TreeNode dragged = _draggedNode;
        _draggedNode = null;
        // ItemDrag 仅对 Tag 为 CollectionNode 的节点触发，此处 Tag 非空
        var node = (CollectionNode)dragged.Tag!;
        HermesCollection? sourceOwner = CollectionOf(dragged);
        HermesCollection? targetOwner = CollectionOf(target);

        SiblingsOf(dragged)?.Remove(node);
        dragged.Remove();

        // 目标是文件夹 → 成为其子节点；目标是集合根 → 进入根清单；目标是请求 → 与其同级（插到其前）
        TreeNode newTreeNode;
        if (target.Tag is HermesCollection collection)
        {
            collection.Items.Add(node);
            newTreeNode = target.Nodes.Add(node.Name);
        }
        else if (target.Tag is CollectionNode { Type: CollectionNodeType.Folder } folder)
        {
            (folder.Items ??= []).Add(node);
            newTreeNode = target.Nodes.Add(node.Name);
        }
        else
        {
            List<CollectionNode>? siblings = SiblingsOf(target);
            if (siblings is null || target.Parent is null)
            {
                return;
            }

            // 进入此分支时 target.Tag 必为 CollectionNode（前面的 switch 已排除集合根与文件夹）
            int index = siblings.IndexOf((CollectionNode)target.Tag!);
            siblings.Insert(index < 0 ? siblings.Count : index, node);
            newTreeNode = target.Parent.Nodes.Insert(Math.Max(target.Index, 0), node.Name);
        }

        newTreeNode.Tag = node;
        if (node.Type == CollectionNodeType.Folder)
        {
            BuildChildren(newTreeNode, node.Items ??= []);
        }

        target.Expand();
        _tree.SelectedNode = newTreeNode;

        var affected = new List<HermesCollection>();
        if (sourceOwner is not null)
        {
            affected.Add(sourceOwner);
        }

        if (targetOwner is not null && targetOwner != sourceOwner)
        {
            affected.Add(targetOwner);
        }

        if (affected.Count > 0)
        {
            CollectionsChanged?.Invoke(this, affected);
        }
    }

    private bool IsValidDropTarget(TreeNode? target)
    {
        if (target is null || _draggedNode is null || ReferenceEquals(target, _draggedNode))
        {
            return false;
        }

        // 不能拖进自己的子树
        for (TreeNode? node = target; node is not null; node = node.Parent)
        {
            if (ReferenceEquals(node, _draggedNode))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>编辑区保存后更新树中节点（保持 Tag 与文本同步）。</summary>
    public void UpdateRequestNode(TreeNode treeNode, CollectionNode updated)
    {
        ReplaceNode(treeNode, updated);
    }

    /// <summary>程序化选中树节点（如用户取消切换请求后还原选择）；不触发 AfterSelect 的鼠标/键盘载入。</summary>
    public void SelectTreeNode(TreeNode treeNode) => _tree.SelectedNode = treeNode;

    private void ReplaceNode(TreeNode treeNode, CollectionNode updated)
    {
        HermesCollection? owner = CollectionOf(treeNode);
        List<CollectionNode>? siblings = SiblingsOf(treeNode);
        if (siblings is not null && treeNode.Tag is CollectionNode old)
        {
            int index = siblings.IndexOf(old);
            if (index >= 0)
            {
                siblings[index] = updated;
            }
        }

        treeNode.Tag = updated;
        treeNode.Text = updated.Name;
        if (owner is not null)
        {
            NotifyChanged(owner);
        }
    }

    private HermesCollection? CollectionOf(TreeNode node) =>
        FindCollectionRoot(node)?.Tag as HermesCollection;

    private static TreeNode? FindCollectionRoot(TreeNode node)
    {
        TreeNode current = node;
        while (current.Parent is not null)
        {
            current = current.Parent;
        }

        return current;
    }

    private List<CollectionNode>? SiblingsOf(TreeNode treeNode)
    {
        if (treeNode.Parent is null)
        {
            return treeNode.Tag is HermesCollection collection ? collection.Items : null;
        }

        return treeNode.Parent.Tag switch
        {
            HermesCollection collection => collection.Items,
            CollectionNode { Type: CollectionNodeType.Folder } folder => folder.Items ??= [],
            _ => null,
        };
    }

    private void NotifyChanged(HermesCollection collection) =>
        CollectionsChanged?.Invoke(this, [collection]);

    /// <summary>请求节点事件参数。</summary>
    public sealed class RequestNodeEventArgs(HermesCollection collection, CollectionNode node, TreeNode treeNode) : EventArgs
    {
        public HermesCollection Collection { get; } = collection;

        public CollectionNode Node { get; } = node;

        public TreeNode TreeNode { get; } = treeNode;
    }
}

using System.Windows.Forms;

using Daedalus.Tools.Hermes.Collections;

namespace Daedalus.Tools.Hermes.View;

/// <summary>
/// 键值编辑表格（请求头 / Params / urlencoded 字段 / 环境变量共用模式）：
/// Key / Value / Enabled 三列，末尾空行即新增，任意编辑经 <see cref="ContentChanged"/> 上报。
/// </summary>
internal sealed class KeyValueGrid : UserControl
{
    private readonly DataGridView _grid;

    // 自动计算行（Host/Content-Length/Content-Type 预览）的行标记：只读、灰显、不进 GetEntries、不可删除
    private static readonly object AutoRowTag = new();

    // SetEntries 期间抑制事件，避免加载数据被当成用户编辑
    private bool _suppressEvents;

    public KeyValueGrid()
    {
        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
        };
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Key", HeaderText = "键", FillWeight = 40 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "值", FillWeight = 50 });
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "启用", FillWeight = 10 });

        // 复选框单击即提交（默认要两次点击才触发 CellValueChanged）
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell)
            {
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        _grid.CellValueChanged += (_, _) => RaiseContentChanged();
        _grid.RowsRemoved += (_, _) => RaiseContentChanged();
        // 自动计算行是预览，不允许用户删除
        _grid.UserDeletingRow += (_, e) => e.Cancel = ReferenceEquals(e.Row?.Tag, AutoRowTag);
        // DefaultValuesNeeded 事件参数 Row 由框架保证非空
        _grid.DefaultValuesNeeded += (_, e) => e.Row!.Cells[2].Value = true;

        Controls.Add(_grid);
    }

    /// <summary>内容被用户编辑（增删行、改值、切换启用）。</summary>
    public event EventHandler? ContentChanged;

    /// <summary>内部表格控件（悬浮编辑等场景需要命中检测）。</summary>
    public DataGridView Grid => _grid;

    /// <summary>用键值表填充（清空原有行）。</summary>
    public void SetEntries(IReadOnlyList<KeyValueEntry> entries)
    {
        _suppressEvents = true;
        try
        {
            _grid.Rows.Clear();
            foreach (KeyValueEntry entry in entries)
            {
                _grid.Rows.Add(entry.Key, entry.Value, entry.Enabled);
            }
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    /// <summary>读出现有行为键值表（不含末尾新增空行与自动计算行；键为空的行跳过）。</summary>
    public List<KeyValueEntry> GetEntries()
    {
        var entries = new List<KeyValueEntry>();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow || ReferenceEquals(row.Tag, AutoRowTag))
            {
                continue;
            }

            string key = row.Cells[0].Value as string ?? string.Empty;
            if (key.Length == 0)
            {
                continue;
            }

            string value = row.Cells[1].Value as string ?? string.Empty;
            bool enabled = row.Cells[2].Value is true;
            entries.Add(new KeyValueEntry(key, value, enabled));
        }

        return entries;
    }

    /// <summary>
    /// 设置顶部的自动计算行（Host / Content-Length / Content-Type 的发送值预览）：
    /// 只读灰显、不参与 <see cref="GetEntries"/>、不允许删除；与可编辑行同键的启用项存在时由调用方决定不显示。
    /// </summary>
    public void SetAutoRows(IReadOnlyList<KeyValueEntry> entries)
    {
        _suppressEvents = true;
        try
        {
            // 移除旧的自动行（RowsRemoved 事件被抑制，不会误报内容变化）
            for (int i = _grid.Rows.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_grid.Rows[i].Tag, AutoRowTag))
                {
                    _grid.Rows.RemoveAt(i);
                }
            }

            for (int i = 0; i < entries.Count; i++)
            {
                _grid.Rows.Insert(i, entries[i].Key, entries[i].Value, true);
                DataGridViewRow row = _grid.Rows[i];
                row.Tag = AutoRowTag;
                row.ReadOnly = true;
                row.DefaultCellStyle.ForeColor = SystemColors.GrayText;
            }
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void RaiseContentChanged()
    {
        if (!_suppressEvents)
        {
            ContentChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

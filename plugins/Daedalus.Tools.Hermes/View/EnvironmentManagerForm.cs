using System.Windows.Forms;

using Daedalus.Tools.Hermes.Variables;

namespace Daedalus.Tools.Hermes.View;

/// <summary>
/// 环境管理窗口（FR-HERMES-021）：左侧环境清单（新建/重命名/删除），右侧变量表格
/// （键/值/密文/启用，FR-HERMES-023 密文标记控制掩码显示）。所有变更立即持久化。
/// </summary>
internal sealed class EnvironmentManagerForm : Form
{
    private readonly Func<EnvironmentData, Task> _saveAsync;
    private readonly ListBox _envList;
    private readonly DataGridView _variablesGrid;
    private readonly Button _addButton;
    private readonly Button _renameButton;
    private readonly Button _deleteButton;

    private EnvironmentData _data;
    private bool _suppressEvents;

    public EnvironmentManagerForm(EnvironmentData data, Func<EnvironmentData, Task> saveAsync)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(saveAsync);
        _data = data;
        _saveAsync = saveAsync;

        Text = "管理环境";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(640, 400);
        MinimizeBox = false;
        MaximizeBox = false;

        _envList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        _addButton = new Button { Text = "新建", Dock = DockStyle.Top };
        _renameButton = new Button { Text = "重命名", Dock = DockStyle.Top };
        _deleteButton = new Button { Text = "删除", Dock = DockStyle.Top };
        var leftButtons = new Panel { Dock = DockStyle.Top, Height = 96 };
        leftButtons.Controls.Add(_deleteButton);
        leftButtons.Controls.Add(_renameButton);
        leftButtons.Controls.Add(_addButton);
        var leftPanel = new Panel { Dock = DockStyle.Left, Width = 180, Padding = new Padding(4) };
        leftPanel.Controls.Add(_envList);
        leftPanel.Controls.Add(leftButtons);

        _variablesGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = true,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };
        _variablesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Key", HeaderText = "变量名", FillWeight = 30 });
        _variablesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "值", FillWeight = 46 });
        _variablesGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Secret", HeaderText = "密文", FillWeight = 12 });
        _variablesGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "启用", FillWeight = 12 });
        var rightPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
        rightPanel.Controls.Add(_variablesGrid);

        Controls.Add(rightPanel);
        Controls.Add(leftPanel);

        _envList.SelectedIndexChanged += (_, _) => ShowSelectedEnvironment();
        _addButton.Click += async (_, _) => await AddEnvironmentAsync();
        _renameButton.Click += async (_, _) => await RenameEnvironmentAsync();
        _deleteButton.Click += async (_, _) => await DeleteEnvironmentAsync();
        _variablesGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_variablesGrid.IsCurrentCellDirty && _variablesGrid.CurrentCell is DataGridViewCheckBoxCell)
            {
                _variablesGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        _variablesGrid.CellValueChanged += async (_, _) => await GridEditedAsync();
        _variablesGrid.RowsRemoved += async (_, _) => await GridEditedAsync();
        _variablesGrid.DefaultValuesNeeded += (_, e) =>
        {
            // DefaultValuesNeeded 事件参数 Row 由框架保证非空
            e.Row!.Cells[2].Value = false;
            e.Row.Cells[3].Value = true;
        };
        // 密文列掩码显示（FR-HERMES-023）：进入编辑时临时明文
        _variablesGrid.CellFormatting += VariablesGrid_CellFormatting;

        RefreshEnvList();

        // 高 DPI 适配（详见 DpiScale）
        DpiScale.Apply(this);
    }

    /// <summary>窗口关闭后的最新环境数据（供调用方刷新切换下拉）。</summary>
    public EnvironmentData Data => _data;

    private void RefreshEnvList()
    {
        _suppressEvents = true;
        try
        {
            int selectedIndex = _envList.SelectedIndex;
            _envList.Items.Clear();
            foreach (HermesEnvironment environment in _data.Environments)
            {
                _envList.Items.Add(environment.Name);
            }

            if (_envList.Items.Count > 0)
            {
                _envList.SelectedIndex = Math.Clamp(selectedIndex, 0, _envList.Items.Count - 1);
            }
        }
        finally
        {
            _suppressEvents = false;
        }

        ShowSelectedEnvironment();
    }

    private HermesEnvironment? SelectedEnvironment =>
        _envList.SelectedIndex >= 0 && _envList.SelectedIndex < _data.Environments.Count
            ? _data.Environments[_envList.SelectedIndex]
            : null;

    private void ShowSelectedEnvironment()
    {
        if (_suppressEvents)
        {
            return;
        }

        _variablesGrid.Rows.Clear();
        HermesEnvironment? environment = SelectedEnvironment;
        if (environment is null)
        {
            return;
        }

        foreach (EnvironmentVariable variable in environment.Variables)
        {
            _variablesGrid.Rows.Add(variable.Key, variable.Value, variable.Secret, variable.Enabled);
        }
    }

    private void VariablesGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        // 值列在密文标记下以 ● 掩码显示；正在编辑的单元格不掩码
        if (e.ColumnIndex == 1 && e.RowIndex >= 0 && e.Value is string { Length: > 0 }
            && _variablesGrid.Rows[e.RowIndex].Cells[2].Value is true)
        {
            e.Value = new string('●', 8);
            e.FormattingApplied = true;
        }
    }

    private async Task GridEditedAsync()
    {
        if (_suppressEvents || SelectedEnvironment is not { } environment)
        {
            return;
        }

        var variables = new List<EnvironmentVariable>();
        foreach (DataGridViewRow row in _variablesGrid.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            string key = row.Cells[0].Value as string ?? string.Empty;
            if (key.Length == 0)
            {
                continue;
            }

            variables.Add(new EnvironmentVariable(
                key,
                row.Cells[1].Value as string ?? string.Empty,
                row.Cells[2].Value is true,
                row.Cells[3].Value is not false));
        }

        environment.Variables = variables;
        await PersistAsync();
    }

    private async Task AddEnvironmentAsync()
    {
        string? name = InputDialog.Prompt(this, "新建环境", "环境名：");
        if (name is null)
        {
            return;
        }

        _data.Environments.Add(new HermesEnvironment { Id = IdGenerator.NewId(), Name = name });
        await PersistAsync();
        RefreshEnvList();
        _envList.SelectedIndex = _envList.Items.Count - 1;
    }

    private async Task RenameEnvironmentAsync()
    {
        if (SelectedEnvironment is not { } environment)
        {
            return;
        }

        string? name = InputDialog.Prompt(this, "重命名环境", "环境名：", environment.Name);
        if (name is null)
        {
            return;
        }

        // with 表达式需重赋 required 成员（Id/Name）
        _data.Environments[_envList.SelectedIndex] = environment with { Id = environment.Id, Name = name };
        await PersistAsync();
        RefreshEnvList();
    }

    private async Task DeleteEnvironmentAsync()
    {
        if (SelectedEnvironment is not { } environment)
        {
            return;
        }

        DialogResult confirm = MessageBox.Show(this, $"确定删除环境「{environment.Name}」？", "删除环境",
            MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
        if (confirm != DialogResult.OK)
        {
            return;
        }

        _data.Environments.Remove(environment);
        if (_data.ActiveId == environment.Id)
        {
            // ActiveId 为 init-only 属性，经 with 表达式清除
            _data = _data with { ActiveId = null };
        }

        await PersistAsync();
        RefreshEnvList();
    }

    private async Task PersistAsync()
    {
        try
        {
            await _saveAsync(_data);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"环境保存失败：{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

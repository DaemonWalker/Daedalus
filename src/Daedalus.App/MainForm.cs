namespace Daedalus.App;

/// <summary>
/// 主窗口（工具箱外壳）。第 1 步脚手架阶段为空白窗口；
/// 工具列表与标签页容器在第 3 步实现。
/// </summary>
internal sealed class MainForm : Form
{
    public MainForm()
    {
        Text = "Daedalus";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1024, 768);
    }
}

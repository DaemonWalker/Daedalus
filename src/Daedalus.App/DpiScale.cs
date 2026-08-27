namespace Daedalus.App;

/// <summary>高 DPI 适配：AutoScaleMode 对手工构建的界面不生效，按当前 DPI 显式缩放一次（布局以 96 DPI 为基准）。</summary>
internal static class DpiScale
{
    public static void Apply(Control root)
    {
        float factor = root.DeviceDpi / 96f;
        if (factor == 1f)
        {
            return;
        }

        root.Scale(new SizeF(factor, factor));
        ResetAutoSizes(root);
    }

    // AutoSize 控件构造时已按放大字体量好尺寸，Scale 会再乘一次导致双重放大；
    // 且 GrowOnly 模式下直接取 PreferredSize 缩不回来，需清零后重新测量。
    // TextBox/NumericUpDown 的 AutoSize 只管高度：宽度保留缩放结果，只重置高度
    private static void ResetAutoSizes(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            ResetAutoSizes(child);
            if (!child.AutoSize)
            {
                continue;
            }

            child.AutoSize = false;
            child.Size = child is TextBoxBase or UpDownBase ? new Size(child.Width, 1) : new Size(1, 1);
            child.AutoSize = true;
        }
    }
}

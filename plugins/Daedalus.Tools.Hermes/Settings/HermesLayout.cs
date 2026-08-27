namespace Daedalus.Tools.Hermes.Settings;

/// <summary>
/// Hermes 主面板三个分隔条的布局比例（hermes.md §11.4）：存比例而非像素，窗口缩放后仍按比例还原。
/// 比例 = SplitterDistance / 对应方向总尺寸（Vertical 用 Width，Horizontal 用 Height）。
/// </summary>
/// <param name="MainRatio">主分隔条（左栏 / 右栏，Vertical）比例。</param>
/// <param name="LeftRatio">左栏分隔条（集合 / 历史，Horizontal）比例。</param>
/// <param name="RightRatio">右栏分隔条（编辑区 / 响应区，Horizontal）比例。</param>
public sealed record HermesLayout(double MainRatio, double LeftRatio, double RightRatio)
{
    /// <summary>单个比例字段的合法性：必须 ∈ (0,1)；非法值按字段缺失处理（用默认布局），不触发 DR-003。</summary>
    internal static bool IsValidRatio(double ratio) => ratio is > 0 and < 1;

    /// <summary>SplitterDistance → 比例。调用方须保证 totalSize &gt; 0。</summary>
    internal static double DistanceToRatio(int distance, int totalSize) => (double)distance / totalSize;

    /// <summary>
    /// 比例 → SplitterDistance 像素，clamp 到 [panel1MinSize, totalSize - panel2MinSize - splitterWidth]。
    /// 窗口极小时上限可能低于下限，此时退回下限（SplitterDistance 不能小于 Panel1MinSize）。
    /// </summary>
    internal static int RatioToDistance(double ratio, int totalSize, int panel1MinSize, int panel2MinSize, int splitterWidth)
    {
        int distance = (int)Math.Round(ratio * totalSize);
        int max = Math.Max(panel1MinSize, totalSize - panel2MinSize - splitterWidth);
        return Math.Clamp(distance, panel1MinSize, max);
    }
}

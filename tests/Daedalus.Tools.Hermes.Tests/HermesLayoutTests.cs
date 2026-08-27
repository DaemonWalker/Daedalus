using Daedalus.Tools.Hermes.Settings;

namespace Daedalus.Tools.Hermes.Tests;

/// <summary>HermesLayout 测试：比例合法性、比例↔像素换算与 clamp（hermes.md §11.4）。</summary>
public sealed class HermesLayoutTests
{
    [Theory]
    [InlineData(0.5, true)]
    [InlineData(0.001, true)]
    [InlineData(0.999, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(-0.1, false)]
    [InlineData(1.5, false)]
    public void IsValidRatio_边界值_仅开区间零一内合法(double ratio, bool expected)
    {
        Assert.Equal(expected, HermesLayout.IsValidRatio(ratio));
    }

    [Fact]
    public void DistanceToRatio_正常尺寸_距离除以总尺寸()
    {
        double ratio = HermesLayout.DistanceToRatio(260, 1000);

        Assert.Equal(0.26, ratio, precision: 6);
    }

    [Fact]
    public void RatioToDistance_正常比例_四舍五入到像素()
    {
        int distance = HermesLayout.RatioToDistance(0.26, 1000, 25, 25, 4);

        Assert.Equal(260, distance);
    }

    [Fact]
    public void RatioToDistance_比例贴边_clamp到面板最小尺寸区间()
    {
        // 0.001 * 1000 = 1 < Panel1MinSize=25 → clamp 到 25
        Assert.Equal(25, HermesLayout.RatioToDistance(0.001, 1000, 25, 25, 4));
        // 0.999 * 1000 = 999 > 1000 - 25 - 4 = 971 → clamp 到 971
        Assert.Equal(971, HermesLayout.RatioToDistance(0.999, 1000, 25, 25, 4));
    }

    [Fact]
    public void RatioToDistance_窗口极小上限低于下限_退回下限()
    {
        // 总尺寸 30：上限 30 - 25 - 4 = 1 < 下限 25，不能抛异常，退回 Panel1MinSize
        int distance = HermesLayout.RatioToDistance(0.5, 30, 25, 25, 4);

        Assert.Equal(25, distance);
    }
}

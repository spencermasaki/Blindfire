using Blindfire.Calibration;

namespace Blindfire.Tests;

public class FieldOfViewProjectionTests
{
    [Fact]
    public void DegreesBetween_CenterToCenter_IsZero()
    {
        var result = FieldOfViewProjection.DegreesBetween(960, 960, 1920, 70);
        Assert.Equal(0.0, result, precision: 9);
    }

    [Fact]
    public void DegreesBetween_CenterToEdge_IsHalfFov()
    {
        var result = FieldOfViewProjection.DegreesBetween(960, 1920, 1920, 70);
        Assert.Equal(35.0, result, precision: 9);
    }

    [Fact]
    public void DegreesBetween_SymmetricPointsAroundCenter_AreEqual()
    {
        var leftToCenter = FieldOfViewProjection.DegreesBetween(0, 960, 1920, 70);
        var centerToRight = FieldOfViewProjection.DegreesBetween(960, 1920, 1920, 70);
        Assert.Equal(leftToCenter, centerToRight, precision: 9);
    }

    [Fact]
    public void DegreesBetween_OrderIndependent()
    {
        var forward = FieldOfViewProjection.DegreesBetween(400, 1200, 1920, 70);
        var backward = FieldOfViewProjection.DegreesBetween(1200, 400, 1920, 70);
        Assert.Equal(forward, backward, precision: 9);
    }

    [Fact]
    public void DeriveVerticalFov_16by9_IsNarrowerThanHorizontal()
    {
        var vFov = FieldOfViewProjection.DeriveVerticalFov(70, 1920, 1080);
        Assert.True(vFov > 0);
        Assert.True(vFov < 70);
    }

    [Fact]
    public void DeriveVerticalFov_SquareScreen_EqualsHorizontalFov()
    {
        var vFov = FieldOfViewProjection.DeriveVerticalFov(70, 1000, 1000);
        Assert.Equal(70.0, vFov, precision: 9);
    }
}

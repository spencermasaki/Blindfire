using Blindfire.GameProfiles;

namespace Blindfire.Tests;

public class ApexLegendsProfileTests
{
    [Fact]
    public void RecommendSensitivity_MatchesMYawFormula()
    {
        var profile = new ApexLegendsProfile();
        var targetDegreesPerCount = 0.022 * 2.5; // equivalent to in-game sensitivity 2.5

        var result = profile.RecommendSensitivity(targetDegreesPerCount);

        Assert.Equal(2.5, result, precision: 9);
    }

    [Fact]
    public void Name_IsApexLegends()
    {
        Assert.Equal("Apex Legends", new ApexLegendsProfile().Name);
    }
}

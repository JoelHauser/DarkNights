using DarkNights.Client;

namespace DarkNights.Tests;

public class HandGlowTests
{
    [Fact]
    public void OffDayAndNight_TheGlowIsGoneAtNoonAndMidnight()
    {
        Assert.Equal(0f, NightModel.HighlightFactor(0f, 0f, byDayToo: true));
        Assert.Equal(0f, NightModel.HighlightFactor(1f, 0f, byDayToo: true));
    }

    [Fact]
    public void NightOnly_ByDayItIsVanilla_AndItFadesWithDusk()
    {
        Assert.Equal(1f, NightModel.HighlightFactor(0f, 0f, byDayToo: false));
        Assert.Equal(0.5f, NightModel.HighlightFactor(0.5f, 0f, byDayToo: false), 5);
        Assert.Equal(0f, NightModel.HighlightFactor(1f, 0f, byDayToo: false));
    }

    [Fact]
    public void ThePassIsNeverMadeBrighterThanVanilla()
    {
        foreach (float setting in new[] { -1f, 0f, 0.4f, 1f, 3f })
        foreach (float nightness in new[] { 0f, 0.3f, 1f })
        foreach (bool byDay in new[] { true, false })
        {
            Assert.InRange(NightModel.HighlightFactor(nightness, setting, byDay), 0f, 1f);
        }
    }
}

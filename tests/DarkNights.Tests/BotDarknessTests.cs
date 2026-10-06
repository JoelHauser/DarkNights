using DarkNights.Client;

namespace DarkNights.Tests;

/// <summary>How lit a target is, and how far a bot without light or NVG can see it.</summary>
public class BotDarknessTests
{
    private const float PitchBlack = 3f;

    [Fact]
    public void InOpenDaylightNothingIsCapped()
    {
        Assert.True(float.IsPositiveInfinity(NightModel.SightCap(NightModel.TargetLight(1f, 1f, 0f), PitchBlack)));
    }

    [Fact]
    public void AWindowlessRoomAtNoonIsSeenOnlyUpClose()
    {
        float light = NightModel.TargetLight(1f, NightModel.DaylightFromOpenness(0f), 0f);
        Assert.Equal(PitchBlack, NightModel.SightCap(light, PitchBlack));
    }

    [Fact]
    public void ARoomWithAWindowAtNoonIsFairlyLit()
    {
        float daylight = NightModel.DaylightFromOpenness(0.05f);
        Assert.InRange(daylight, 0.4f, 0.6f);
        Assert.True(NightModel.SightCap(NightModel.TargetLight(1f, daylight, 0f), PitchBlack) > 60f);
    }

    [Fact]
    public void OutdoorsAtNightStarlightAlwaysShowsASilhouette()
    {
        float light = NightModel.TargetLight(0f, 1f, 0f);
        Assert.Equal(NightModel.Starlight, light);
        Assert.True(NightModel.SightCap(light, PitchBlack) > PitchBlack);
    }

    [Fact]
    public void ABrighterMoonLetsBotsSeeFurther()
    {
        float dark = NightModel.SightCap(NightModel.TargetLight(0.1f, 1f, 0f), PitchBlack);
        float moonlit = NightModel.SightCap(NightModel.TargetLight(0.4f, 1f, 0f), PitchBlack);
        Assert.True(moonlit > dark);
    }

    [Fact]
    public void StandingByALampMakesYouVisible()
    {
        float light = NightModel.TargetLight(0.02f, 0f, 1f);
        Assert.True(float.IsPositiveInfinity(NightModel.SightCap(light, PitchBlack)));
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.2f, 1f)]
    [InlineData(0.9f, 1f)]
    public void DaylightFromOpennessEnds(float openness, float expected)
    {
        Assert.Equal(expected, NightModel.DaylightFromOpenness(openness), 3);
    }

    [Fact]
    public void ABunkerIsDarkAtNoon()
    {
        var s = Presets.For(DarknessLevel.Medium);
        NightState day = NightState.Vanilla;
        NightState inside = NightModel.WithBunker(day, s, 1f, nightVisionOn: false);
        Assert.True(inside.Ambient < 0.2f);
        Assert.True(inside.Interior < 0.1f);
        Assert.Equal(1f, inside.Nightness);
        Assert.Equal(day.Moonlight, inside.Moonlight);
    }

    [Fact]
    public void OutsideABunkerNothingChanges()
    {
        var s = Presets.For(DarknessLevel.Medium);
        NightState day = NightState.Vanilla;
        Assert.Equal(day, NightModel.WithBunker(day, s, 0f, nightVisionOn: false));
    }

    [Fact]
    public void ABunkerNeverBrightensANightThatIsAlreadyDarker()
    {
        var s = Presets.For(DarknessLevel.Medium);
        var night = new NightState { Nightness = 1f, Ambient = 0.03f, Moonlight = 0.1f, Interior = 0.02f, Exposure = 1f };
        NightState inside = NightModel.WithBunker(night, s, 1f, nightVisionOn: false);
        Assert.True(inside.Ambient <= night.Ambient);
        Assert.True(inside.Interior <= night.Interior);
    }

    [Fact]
    public void TheCapNeverGoesBelowThePitchBlackRange()
    {
        Assert.Equal(PitchBlack, NightModel.SightCap(0f, PitchBlack));
    }
}

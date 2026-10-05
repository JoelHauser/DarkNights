using DarkNights.Client;

namespace DarkNights.Tests;

/// <summary>
/// The SAIN bridge's arithmetic. SAIN's own getModifier is reproduced here from its 4.5.1
/// source (TimeClass.cs) so the inference is tested against the formula it inverts, not
/// against itself.
/// </summary>
public class BotVisibilityTests
{
    private static readonly NightSettings Medium = Presets.For(DarknessLevel.Medium);

    private static NightInputs Sky(float sun = -30f, float moon = -10f, float phase = 0f,
        float cloud = -1f, float fog = 0f, float rain = 0f) => new()
    {
        SunElevation = sun, MoonElevation = moon, MoonPhase = phase, Cloudiness = cloud, Fog = fog, Rain = rain,
    };

    /// <summary>SAIN 4.5.1 TimeClass.getModifier with its default hours, as a pure function.</summary>
    private static float SainGetModifier(float time, float min, out float ratio)
    {
        const float dawnStart = 6f, dawnEnd = 8f, duskStart = 20f, duskEnd = 22f;
        if (time <= duskStart && time >= dawnEnd) { ratio = 1f; return 1f; }
        if (time >= duskEnd || time <= dawnStart) { ratio = 0f; return min; }
        ratio = time < dawnEnd
            ? (time - dawnStart) / (dawnEnd - dawnStart)
            : 1f - (time - duskStart) / (duskEnd - duskStart);
        return min + (1f - min) * ratio;
    }

    [Theory]
    [InlineData(45f)]
    [InlineData(5f)]
    [InlineData(0f)]
    public void ByDayBotsSeeAsSainSays(float sun)
    {
        Assert.Equal(1f, NightModel.BotVisibility(Sky(sun: sun, cloud: 1f, fog: 0.05f, rain: 1f), Medium));
    }

    [Fact]
    public void AClearMoonlessNightIsSainsFullNight()
    {
        Assert.Equal(0f, NightModel.BotVisibility(Sky(), Medium), 5);
    }

    [Fact]
    public void AHighClearFullMoonGivesBotsBackThePresetsMoonShare()
    {
        float v = NightModel.BotVisibility(Sky(moon: 40f, phase: 1f), Medium);
        Assert.Equal(NightModel.MoonShare(Medium), v, 4);
        Assert.InRange(v, 0.3f, 0.7f);
    }

    [Fact]
    public void OvercastHidesTheMoonFromBotsToo()
    {
        float clear = NightModel.BotVisibility(Sky(moon: 40f, phase: 1f), Medium);
        float overcast = NightModel.BotVisibility(Sky(moon: 40f, phase: 1f, cloud: 1f), Medium);
        Assert.True(overcast < clear / 2f);
    }

    [Fact]
    public void FogAndRainAreLeftToSain_SoTheyAreNotCountedTwice()
    {
        float plain = NightModel.BotVisibility(Sky(moon: 40f, phase: 1f), Medium);
        Assert.Equal(plain, NightModel.BotVisibility(Sky(moon: 40f, phase: 1f, fog: 0.05f, rain: 1f), Medium));
    }

    [Fact]
    public void BotSightNeverRecoversAsTheSunSinks()
    {
        float previous = 1f;
        for (float sun = 2f; sun >= -20f; sun -= 0.25f)
        {
            float v = NightModel.BotVisibility(Sky(sun: sun, moon: 20f, phase: 0.6f), Medium);
            Assert.True(v <= previous + 1e-6f, $"rose at sun {sun}");
            previous = v;
        }
    }

    [Fact]
    public void ADarkerPresetMakesTheMoonHelpBotsLess()
    {
        Assert.True(NightModel.MoonShare(Presets.For(DarknessLevel.Darkest)) < NightModel.MoonShare(Medium));
        Assert.True(NightModel.MoonShare(Presets.For(DarknessLevel.Lightest)) > NightModel.MoonShare(Medium));
    }

    [Theory]
    [InlineData(0.2f)]   // SAIN's NightTimeVisionModifier default
    [InlineData(0.35f)]  // its snow default
    [InlineData(0.6f)]
    public void SainsNightMinimumIsRecoveredFromItsOwnAnswer(float min)
    {
        foreach (float time in new[] { 23f, 3f, 20.5f, 21.5f, 6.5f, 7.9f })
        {
            float result = SainGetModifier(time, min, out float ratio);
            Assert.True(NightModel.TryInferSainMinimum(ratio, result, out float inferred), $"at {time}");
            Assert.Equal(min, inferred, 4);
        }
    }

    [Fact]
    public void ByDayTheMinimumCannotBeInferred_AndIsNotClaimed()
    {
        float result = SainGetModifier(12f, 0.2f, out float ratio);
        Assert.False(NightModel.TryInferSainMinimum(ratio, result, out _));
    }

    [Fact]
    public void AtStrengthZeroSainGetsExactlyItsOwnAnswerBack()
    {
        foreach (float time in new[] { 23f, 20.5f, 21.9f, 7f })
        {
            float result = SainGetModifier(time, 0.2f, out float clockRatio);
            NightModel.TryInferSainMinimum(clockRatio, result, out float min);

            float ratio = NightModel.Lerp(clockRatio, 0.9f, 0f);
            Assert.Equal(result, NightModel.SainModifier(min, ratio), 5);
        }
    }
}

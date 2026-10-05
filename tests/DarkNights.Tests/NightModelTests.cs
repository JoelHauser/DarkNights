using DarkNights.Client;

namespace DarkNights.Tests;

public class NightModelTests
{
    private static readonly NightSettings Medium = Presets.For(DarknessLevel.Medium);

    private static NightInputs Night(float sun = -30f, float moon = -10f, float phase = 0f,
        float cloud = -1f, float fog = 0f, float rain = 0f, bool nvg = false) => new()
    {
        SunElevation = sun,
        MoonElevation = moon,
        MoonPhase = phase,
        Cloudiness = cloud,
        Fog = fog,
        Rain = rain,
        NightVisionOn = nvg,
    };

    // ------------------------------------------------------------------ day

    [Theory]
    [InlineData(60f)]
    [InlineData(10f)]
    [InlineData(0.5f)]
    [InlineData(0f)]
    public void DaytimeIsExactlyVanilla_WhateverTheWeather(float sun)
    {
        foreach (var level in Enum.GetValues<DarknessLevel>())
        {
            var s = Presets.For(level);
            var stormy = Night(sun: sun, moon: 40f, phase: 1f, cloud: 1f, fog: 0.05f, rain: 1f, nvg: true);
            var state = NightModel.Evaluate(stormy, s);

            Assert.Equal(0f, state.Nightness);
            Assert.Equal(1f, state.Ambient);
            Assert.Equal(1f, state.Moonlight);
            Assert.Equal(1f, state.Interior);
            Assert.Equal(0f, state.Exposure);
        }
    }

    // ------------------------------------------------------------- twilight

    [Fact]
    public void DuskIsSmooth_AndNeverBrightensAsTheSunSinks()
    {
        float previous = 1f;
        for (float sun = 2f; sun >= -20f; sun -= 0.25f)
        {
            float ambient = NightModel.Evaluate(Night(sun: sun), Medium).Ambient;
            Assert.True(ambient <= previous + 1e-6f, $"brightened at sun {sun}");

            // No step bigger than a sliver of the range in a quarter of a degree.
            Assert.True(previous - ambient < 0.05f, $"jumped at sun {sun}");
            previous = ambient;
        }
    }

    [Fact]
    public void NightIsCompleteAtTheEndOfTwilight_AndHalfWayInTheMiddle()
    {
        Assert.Equal(1f, NightModel.Evaluate(Night(sun: Medium.TwilightEnd), Medium).Nightness, 5);
        Assert.Equal(1f, NightModel.Evaluate(Night(sun: -40f), Medium).Nightness, 5);

        float mid = (Medium.TwilightStart + Medium.TwilightEnd) / 2f;
        Assert.Equal(0.5f, NightModel.Evaluate(Night(sun: mid), Medium).Nightness, 3);
    }

    // ----------------------------------------------------------- the moon

    [Fact]
    public void AFullMoonGivesSomeOfTheNightBack()
    {
        var moonless = NightModel.Evaluate(Night(moon: 40f, phase: 0f), Medium);
        var full = NightModel.Evaluate(Night(moon: 40f, phase: 1f), Medium);

        Assert.Equal(Medium.MoonlessAmbient, moonless.Ambient, 4);
        Assert.Equal(Medium.FullMoonAmbient, full.Ambient, 4);
        Assert.True(full.Ambient > moonless.Ambient);
    }

    [Fact]
    public void AFullMoonBelowTheHorizonIsAMoonlessNight()
    {
        var set = NightModel.Evaluate(Night(moon: -5f, phase: 1f), Medium);
        var moonless = NightModel.Evaluate(Night(moon: 40f, phase: 0f), Medium);
        Assert.Equal(moonless.Ambient, set.Ambient, 5);
    }

    // ------------------------------------------------------------ weather

    [Fact]
    public void CloudFogAndRainEachMakeTheNightDarker()
    {
        var clear = NightModel.Evaluate(Night(moon: 40f, phase: 1f), Medium);

        Assert.True(NightModel.Evaluate(Night(moon: 40f, phase: 1f, cloud: 1f), Medium).Ambient < clear.Ambient);
        Assert.True(NightModel.Evaluate(Night(moon: 40f, phase: 1f, fog: NightModel.FullFog), Medium).Ambient < clear.Ambient);
        Assert.True(NightModel.Evaluate(Night(moon: 40f, phase: 1f, rain: 1f), Medium).Ambient < clear.Ambient);
    }

    [Fact]
    public void OvercastHidesTheMoon_SoAFullMoonBarelyHelps()
    {
        var overcastFull = NightModel.Evaluate(Night(moon: 40f, phase: 1f, cloud: 1f), Medium);
        var overcastNew = NightModel.Evaluate(Night(moon: 40f, phase: 0f, cloud: 1f), Medium);
        var clearFull = NightModel.Evaluate(Night(moon: 40f, phase: 1f), Medium);
        var clearNew = NightModel.Evaluate(Night(moon: 40f, phase: 0f), Medium);

        Assert.True(overcastFull.Ambient - overcastNew.Ambient < (clearFull.Ambient - clearNew.Ambient) / 2f);
    }

    // ---------------------------------------------------------------- NVG

    [Fact]
    public void NightVisionRetention_RunsFromTheFullDarknessToVanilla()
    {
        var s = Medium;
        s.NightVisionRetention = 0f;
        var dark = NightModel.Evaluate(Night(nvg: true), s);
        var without = NightModel.Evaluate(Night(nvg: false), s);
        Assert.Equal(without.Ambient, dark.Ambient, 5);

        s.NightVisionRetention = 1f;
        var vanilla = NightModel.Evaluate(Night(nvg: true), s);
        Assert.Equal(1f, vanilla.Ambient);
        Assert.Equal(1f, vanilla.Moonlight);
        Assert.Equal(1f, vanilla.Interior);
        Assert.Equal(0f, vanilla.Exposure);
    }

    // ------------------------------------------------------------- limits

    [Fact]
    public void NoSettingCanBrightenTheNight_OrBlackTheScreenOut()
    {
        var wild = Medium;
        wild.MoonlessAmbient = -3f;
        wild.FullMoonAmbient = 7f;
        wild.Moonlight = 0f;
        wild.Interior = 5f;
        wild.Overcast = 0f;

        foreach (float sun in new[] { -3f, -8f, -30f })
        foreach (float phase in new[] { 0f, 1f })
        foreach (float cloud in new[] { -1f, 1f })
        {
            var state = NightModel.Evaluate(Night(sun: sun, moon: 30f, phase: phase, cloud: cloud), wild);
            foreach (float f in new[] { state.Ambient, state.Moonlight, state.Interior })
            {
                Assert.InRange(f, NightModel.Floor, 1f);
            }
        }
    }

    // ------------------------------------------------------------ presets

    [Fact]
    public void EachPresetIsAtLeastAsDarkAsTheOneBeforeIt()
    {
        var ladder = new[]
        {
            DarknessLevel.Lightest, DarknessLevel.Lighter, DarknessLevel.Light, DarknessLevel.Medium,
            DarknessLevel.Dark, DarknessLevel.Darker, DarknessLevel.Darkest,
        };

        for (int k = 1; k < ladder.Length; k++)
        {
            var lighter = Presets.For(ladder[k - 1]);
            var darker = Presets.For(ladder[k]);

            Assert.True(darker.MoonlessAmbient < lighter.MoonlessAmbient, $"{ladder[k]} moonless");
            Assert.True(darker.FullMoonAmbient < lighter.FullMoonAmbient, $"{ladder[k]} full moon");
            Assert.True(darker.Moonlight < lighter.Moonlight, $"{ladder[k]} moonlight");
            Assert.True(darker.Interior < lighter.Interior, $"{ladder[k]} interior");
            Assert.True(darker.ExposureCeiling < lighter.ExposureCeiling, $"{ladder[k]} exposure");
        }
    }

    [Fact]
    public void EveryPresetHasAFullMoonBrighterThanNoMoon_AndAllDarkerThanVanilla()
    {
        foreach (var level in Enum.GetValues<DarknessLevel>())
        {
            var s = Presets.For(level);
            Assert.True(s.FullMoonAmbient > s.MoonlessAmbient, level.ToString());
            Assert.True(s.FullMoonAmbient < 1f, level.ToString());
            Assert.True(s.TwilightEnd < s.TwilightStart, level.ToString());
        }
    }

    [Fact]
    public void CustomStartsFromMedium()
    {
        Assert.Equal(Presets.For(DarknessLevel.Medium).MoonlessAmbient, Presets.For(DarknessLevel.Custom).MoonlessAmbient);
    }

    // ---------------------------------------------------------- interiors

    [Theory]
    [InlineData(0.01f, 0.3f, 0.02f)]
    [InlineData(0.02f, 0.3f, 0.02f)]
    [InlineData(0f, 0.3f, 0.02f)]
    public void AnInteriorThatIsAlreadyDarkIsLeftAlone(float luminance, float factor, float floor)
    {
        Assert.Equal(1f, NightModel.InteriorMultiplier(luminance, factor, floor));
    }

    [Theory]
    [InlineData(0.5f, 0.3f, 0.02f)]
    [InlineData(0.05f, 0.1f, 0.02f)]
    [InlineData(0.03f, 0.02f, 0.02f)]
    public void ABrightInteriorDarkens_ButNeverBelowTheFloor_AndNeverBrightens(float luminance, float factor, float floor)
    {
        float m = NightModel.InteriorMultiplier(luminance, factor, floor);
        Assert.InRange(m, factor, 1f);
        Assert.True(luminance * m >= floor - 1e-6f);
    }

    [Fact]
    public void ByDayInteriorsAreUntouched()
    {
        Assert.Equal(1f, NightModel.InteriorMultiplier(0.5f, 1f, 0.02f));
    }

    // ----------------------------------------------------------- exposure

    [Fact]
    public void TheExposureCeilingOnlyEverComesDown()
    {
        Assert.Equal(6f, NightModel.ExposureCeiling(6f, 1.5f, 0f));
        Assert.Equal(1.5f, NightModel.ExposureCeiling(6f, 1.5f, 1f), 5);
        Assert.Equal(3.75f, NightModel.ExposureCeiling(6f, 1.5f, 0.5f), 5);

        // A game ceiling already below the night one is not raised.
        Assert.Equal(1f, NightModel.ExposureCeiling(1f, 1.5f, 1f));
    }

    // ------------------------------------------------------------ helpers

    [Fact]
    public void InverseLerpWorksEitherWayRound_LikeMathf()
    {
        Assert.Equal(0.5f, NightModel.InverseLerp(0f, -12f, -6f), 5);
        Assert.Equal(0.25f, NightModel.InverseLerp(-1f, 1f, -0.5f), 5);
        Assert.Equal(0f, NightModel.InverseLerp(3f, 3f, 10f));
        Assert.Equal(1f, NightModel.InverseLerp(0f, -12f, -40f));
    }
}

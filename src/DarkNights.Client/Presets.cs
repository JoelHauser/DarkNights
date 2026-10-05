namespace DarkNights.Client
{
    /// <summary>Seven levels, as Darker Nights has, plus Custom for the hand-set values.</summary>
    public enum DarknessLevel
    {
        Lightest,
        Lighter,
        Light,
        Medium,
        Dark,
        Darker,
        Darkest,
        Custom,
    }

    /// <summary>
    /// The seven presets. Pure data, compiled into the tests, which hold the ordering: each
    /// level is at least as dark as the one before it in every value.
    ///
    /// These numbers have NOT been tuned in game. They are a starting ladder, chosen so that
    /// the middle of it is clearly darker than vanilla without being unplayable, and they
    /// will move once the diagnostics have been read on a real night.
    /// </summary>
    public static class Presets
    {
        /// <summary>Shared by every preset: dusk starts at sunset and night is complete at nautical dusk.</summary>
        public const float TwilightStart = 0f;
        public const float TwilightEnd = -12f;

        public static NightSettings For(DarknessLevel level)
        {
            switch (level)
            {
                case DarknessLevel.Lightest: return Make(0.75f, 0.95f, 0.90f, 0.85f, 3.0f);
                case DarknessLevel.Lighter:  return Make(0.60f, 0.90f, 0.80f, 0.75f, 2.5f);
                case DarknessLevel.Light:    return Make(0.50f, 0.80f, 0.70f, 0.65f, 2.0f);
                case DarknessLevel.Dark:     return Make(0.28f, 0.60f, 0.50f, 0.45f, 1.0f);
                case DarknessLevel.Darker:   return Make(0.20f, 0.50f, 0.40f, 0.35f, 0.5f);
                case DarknessLevel.Darkest:  return Make(0.12f, 0.40f, 0.30f, 0.25f, 0.0f);
                default:                     return Make(0.38f, 0.70f, 0.60f, 0.55f, 1.5f);
            }
        }

        private static NightSettings Make(float moonless, float fullMoon, float moonlight, float interior, float exposureCeiling)
        {
            return new NightSettings
            {
                TwilightStart = TwilightStart,
                TwilightEnd = TwilightEnd,
                MoonlessAmbient = moonless,
                FullMoonAmbient = fullMoon,
                Moonlight = moonlight,
                Interior = interior,
                Overcast = 0.6f,
                FogDark = 0.75f,
                RainDark = 0.85f,
                NightVisionRetention = 0.5f,
                ExposureCeiling = exposureCeiling,
            };
        }
    }
}

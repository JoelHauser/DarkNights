using UnityEngine;
using UnityEngine.Rendering;

namespace DarkNights.Client
{
    /// <summary>
    /// The Harmony patches. Each one scales a value at the point the game hands it on, never
    /// a setting upstream of it, so whatever produced the value -- the game's own curves, a
    /// graphics mod's replacements, the weather -- still decides its shape.
    /// </summary>
    internal static class Patches
    {
        /// <summary>
        /// AmbientLight.SetSH: the one place all sky ambient passes. ToDController and
        /// TODSkySimple both build their spherical harmonics and end here.
        /// Parameter by index (__0) so a renamed parameter does not break the patch.
        /// </summary>
        internal static void SkyAmbientPrefix(object __instance, ref SphericalHarmonicsL2 __0)
        {
            if (Faults.IsOff(Faults.Part.SkyAmbient))
            {
                return;
            }

            NightState night = NightDriver.Get();
            long started = Perf.Start();
            try
            {
                Reflections.Seen(__instance as Object);
                LastSkyStrength = 0.2126f * __0[0, 0] + 0.7152f * __0[1, 0] + 0.0722f * __0[2, 0];
                if (DarkNightsPlugin.DarkenSky.Value)
                {
                    Scale(ref __0, night.Ambient);
                }
            }
            catch (System.Exception e)
            {
                Faults.Report(Faults.Part.SkyAmbient, "the sky ambient is vanilla", e);
            }

            Perf.Stop(Perf.Part.Hooks, started);
        }

        /// <summary>
        /// The game's sky ambient before scaling: the luminance of the harmonics' constant
        /// term. For the log, to compare day with night and calibrate the bunker level.
        /// </summary>
        internal static float LastSkyStrength = float.NaN;

        /// <summary>What LevelSettings wrote before the last scaling, for the log.</summary>
        internal static Color FlatAmbientBase = Color.clear;
        internal static float FlatAmbientIntensity = float.NaN;
        internal static float FlatAmbientFactor = 1f;

        /// <summary>
        /// LevelSettings.OnPreCullCallback: before every camera renders, it sets Unity's own
        /// flat ambient (RenderSettings.ambient*) from the map's fixed SkyColor and
        /// AmbientIntensity. That never follows the time of day, so it lit an unlit room at
        /// 23:30 exactly as at noon -- an even, directionless glow under everything else. It
        /// is rewritten from the map's values on every call, so scaling the output here never
        /// compounds and never fights a mod that edits those values. Night-vision mode is left
        /// alone: that is the goggles' own ambient.
        /// </summary>
        internal static void FlatAmbientPostfix(object __instance)
        {
            if (Faults.IsOff(Faults.Part.FlatAmbient))
            {
                return;
            }

            float f = NightDriver.Get().Ambient;
            long started = Perf.Start();
            try
            {
                ScaleFlatAmbient(__instance, f);
            }
            catch (System.Exception e)
            {
                Faults.Report(Faults.Part.FlatAmbient, "the flat map ambient is vanilla", e);
            }

            Perf.Stop(Perf.Part.Hooks, started);
        }

        private static void ScaleFlatAmbient(object levelSettings, float f)
        {
            // Runs before every camera renders: compared as the enum value, not by name --
            // Enum.ToString is slow and allocates a string each call.
            if (!DarkNightsPlugin.DarkenSky.Value
                || Equals(GameTypes.LevelSettings_AmbientType.GetValue(levelSettings, null), GameTypes.LevelSettings_NightVisionAmbient))
            {
                FlatAmbientFactor = 1f;
                return;
            }

            FlatAmbientBase = RenderSettings.ambientLight;
            FlatAmbientIntensity = RenderSettings.ambientIntensity;
            FlatAmbientFactor = f;
            if (f >= 1f)
            {
                return;
            }

            RenderSettings.ambientLight *= f;
            RenderSettings.ambientSkyColor *= f;
            RenderSettings.ambientEquatorColor *= f;
            RenderSettings.ambientGroundColor *= f;
        }

        /// <summary>
        /// AmbientHighlight.SetSH: the same ambient, drawn again on hands and characters. Left
        /// alone, your own hands would stay lit by a sky the rest of the world no longer gets.
        /// </summary>
        internal static void HighlightPrefix(ref SphericalHarmonicsL2 __0)
        {
            float f = NightDriver.Get().Ambient;
            long started = Perf.Start();
            if (DarkNightsPlugin.DarkenSky.Value)
            {
                Scale(ref __0, f);
            }

            Perf.Stop(Perf.Part.Hooks, started);
        }

        /// <summary>
        /// CloudController.UpdateAmbient: WeatherController passes it ToDController.SH, the
        /// unscaled harmonics, so clouds would glow against a darkened sky.
        /// </summary>
        internal static void CloudsPrefix(ref SphericalHarmonicsL2 __0)
        {
            float f = NightDriver.Get().Ambient;
            long started = Perf.Start();
            if (DarkNightsPlugin.DarkenClouds.Value)
            {
                Scale(ref __0, f);
            }

            Perf.Stop(Perf.Part.Hooks, started);
        }

        private static Light _light;
        private static Tracked _moonlight = new Tracked();

        /// <summary>
        /// After TOD_Sky.LateUpdate, which sets the directional light's intensity -- but only
        /// every Light.UpdateInterval (0 to 1 s by shadow quality), so the light is scaled
        /// through Tracked rather than multiplied every frame.
        /// </summary>
        internal static void MoonlightPostfix(object __instance)
        {
            if (Faults.IsOff(Faults.Part.Moonlight))
            {
                return;
            }

            NightState night = NightDriver.Get();
            long started = Perf.Start();
            try
            {
                Light light = GameTypes.SkyLight(__instance);
                if (light != null)
                {
                    if (!ReferenceEquals(light, _light))
                    {
                        _light = light;
                        _moonlight = new Tracked();
                    }

                    float factor = DarkNightsPlugin.DarkenMoonlight.Value ? night.Moonlight : 1f;
                    light.intensity = _moonlight.Apply(light.intensity, factor);
                }
            }
            catch (System.Exception e)
            {
                Faults.Report(Faults.Part.Moonlight, "the moonlight is vanilla", e);
            }

            Perf.Stop(Perf.Part.Hooks, started);
        }

        internal static Tracked MoonlightTracking => _moonlight;

        private static void Scale(ref SphericalHarmonicsL2 sh, float factor)
        {
            if (factor < 1f)
            {
                sh *= factor;
            }
        }
    }
}

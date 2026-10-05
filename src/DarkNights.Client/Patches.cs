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
        internal static void SkyAmbientPrefix(ref SphericalHarmonicsL2 __0)
        {
            if (DarkNightsPlugin.DarkenSky.Value)
            {
                Scale(ref __0, NightDriver.Get().Ambient);
            }
        }

        /// <summary>
        /// AmbientHighlight.SetSH: the same ambient, drawn again on hands and characters. Left
        /// alone, your own hands would stay lit by a sky the rest of the world no longer gets.
        /// </summary>
        internal static void HighlightPrefix(ref SphericalHarmonicsL2 __0)
        {
            if (DarkNightsPlugin.DarkenSky.Value)
            {
                Scale(ref __0, NightDriver.Get().Ambient);
            }
        }

        /// <summary>
        /// CloudController.UpdateAmbient: WeatherController passes it ToDController.SH, the
        /// unscaled harmonics, so clouds would glow against a darkened sky.
        /// </summary>
        internal static void CloudsPrefix(ref SphericalHarmonicsL2 __0)
        {
            if (DarkNightsPlugin.DarkenClouds.Value)
            {
                Scale(ref __0, NightDriver.Get().Ambient);
            }
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
            Light light = GameTypes.SkyLight(__instance);
            if (light == null)
            {
                return;
            }

            if (!ReferenceEquals(light, _light))
            {
                _light = light;
                _moonlight = new Tracked();
            }

            float factor = DarkNightsPlugin.DarkenMoonlight.Value ? NightDriver.Get().Moonlight : 1f;
            light.intensity = _moonlight.Apply(light.intensity, factor);
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

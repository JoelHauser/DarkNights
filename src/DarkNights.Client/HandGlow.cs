using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace DarkNights.Client
{
    /// <summary>
    /// Takes away the glow EFT puts on your hands.
    ///
    /// AmbientHighlight draws a second ambient pass, after lighting, over the pixels of one
    /// stencil class each -- Hands by default, Characters where the game sets it. Its
    /// strength is lerp(HighlightMinMultiplier, HighlightMaxMultiplier, curve(main light
    /// height)): code defaults 0.2 with the light down and 1.7 with it high, and at night the
    /// main light is the moon. On top of that SetSH adds a list of fixed directional lights
    /// (_additionalLights) that never change with the time of day. Read from the decompiled
    /// class; the values the game actually ships are prefab data, logged once per raid.
    ///
    /// Both are scaled for the length of the one call that reads them and put straight back,
    /// so nothing of ours persists in the game's objects between frames.
    ///
    /// Only on an HDR camera. There the pass blends One/One -- it adds -- so scaling it to 0
    /// removes exactly the extra and leaves normal lighting. On a non-HDR camera it blends
    /// DstColor/Zero, a multiply, and scaling it toward 0 would black the hands out instead;
    /// that case is left vanilla and the log says so.
    /// </summary>
    internal static class HandGlow
    {
        private static bool _failed;
        private static bool _lastCameraHdr = true;
        private static bool _ldrSaid;
        private static int _reportedGeneration = -1;

        internal static string Report = "not seen";

        internal static void Install(Harmony harmony)
        {
            if (!GameTypes.HandGlowReady)
            {
                DarkNightsPlugin.Log.LogWarning("Not removing the hand glow: its game members were not found.");
                return;
            }

            try
            {
                harmony.Patch(GameTypes.AmbientHighlight_Render,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(HandGlow), nameof(RenderPrefix))),
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(HandGlow), nameof(RenderPostfix))));

                if (GameTypes.AmbientHighlight_SetSH != null && GameTypes.AmbientHighlight_ExtraLights != null && GameTypes.ExtraLight_Intensity != null)
                {
                    harmony.Patch(GameTypes.AmbientHighlight_SetSH,
                        prefix: new HarmonyMethod(AccessTools.Method(typeof(HandGlow), nameof(ExtraLightsPrefix))),
                        postfix: new HarmonyMethod(AccessTools.Method(typeof(HandGlow), nameof(ExtraLightsPostfix))));
                }
            }
            catch (Exception e)
            {
                DarkNightsPlugin.Log.LogError("Could not patch the hand highlight: " + e.Message);
            }
        }

        private static float Factor()
        {
            if (_failed)
            {
                return 1f;
            }

            NightState state = NightDriver.Get();
            if (!string.IsNullOrEmpty(NightDriver.Idle))
            {
                return 1f;
            }

            return NightModel.HighlightFactor(state.Nightness, DarkNightsPlugin.HandGlowAmount.Value, DarkNightsPlugin.HandGlowByDay.Value);
        }

        // ---------------------------------------------------- the highlight pass

        internal static void RenderPrefix(object __instance, Camera __0, out float[] __state)
        {
            __state = null;
            try
            {
                ReportOnce(__instance, __0);

                if (__0 != null)
                {
                    _lastCameraHdr = __0.allowHDR;
                }

                float f = Factor();
                if (f >= 1f || __0 == null)
                {
                    return;
                }

                if (!__0.allowHDR)
                {
                    if (!_ldrSaid)
                    {
                        _ldrSaid = true;
                        DarkNightsPlugin.Log.LogWarning("Hand glow left as vanilla: the camera is not HDR, where the highlight " +
                                                        "multiplies rather than adds and removing it would black the hands out.");
                    }
                    return;
                }

                var passes = GameTypes.AmbientHighlight_Settings.GetValue(__instance) as Array;
                if (passes == null || passes.Length == 0)
                {
                    return;
                }

                var saved = new float[passes.Length * 2];
                for (int i = 0; i < passes.Length; i++)
                {
                    object pass = passes.GetValue(i);
                    if (pass == null)
                    {
                        saved[2 * i] = saved[2 * i + 1] = float.NaN;
                        continue;
                    }

                    var min = (float)GameTypes.HighlightSettings_Min.GetValue(pass);
                    var max = (float)GameTypes.HighlightSettings_Max.GetValue(pass);
                    saved[2 * i] = min;
                    saved[2 * i + 1] = max;
                    GameTypes.HighlightSettings_Min.SetValue(pass, min * f);
                    GameTypes.HighlightSettings_Max.SetValue(pass, max * f);
                }

                __state = saved;
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        internal static void RenderPostfix(object __instance, float[] __state)
        {
            if (__state == null)
            {
                return;
            }

            try
            {
                var passes = GameTypes.AmbientHighlight_Settings.GetValue(__instance) as Array;
                for (int i = 0; passes != null && i < passes.Length && 2 * i + 1 < __state.Length; i++)
                {
                    object pass = passes.GetValue(i);
                    if (pass == null || float.IsNaN(__state[2 * i]))
                    {
                        continue;
                    }

                    GameTypes.HighlightSettings_Min.SetValue(pass, __state[2 * i]);
                    GameTypes.HighlightSettings_Max.SetValue(pass, __state[2 * i + 1]);
                }
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        // ------------------------------------------------------ the fixed lights

        internal static void ExtraLightsPrefix(object __instance, out float[] __state)
        {
            __state = null;
            try
            {
                float f = Factor();
                if (f >= 1f || !_lastCameraHdr)
                {
                    return;
                }

                var lights = GameTypes.AmbientHighlight_ExtraLights.GetValue(__instance) as Array;
                if (lights == null || lights.Length == 0)
                {
                    return;
                }

                var saved = new float[lights.Length];
                for (int i = 0; i < lights.Length; i++)
                {
                    // A struct: unbox, change, box back into the array.
                    object light = lights.GetValue(i);
                    saved[i] = (float)GameTypes.ExtraLight_Intensity.GetValue(light);
                    GameTypes.ExtraLight_Intensity.SetValue(light, saved[i] * f);
                    lights.SetValue(light, i);
                }

                __state = saved;
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        internal static void ExtraLightsPostfix(object __instance, float[] __state)
        {
            if (__state == null)
            {
                return;
            }

            try
            {
                var lights = GameTypes.AmbientHighlight_ExtraLights.GetValue(__instance) as Array;
                for (int i = 0; lights != null && i < lights.Length && i < __state.Length; i++)
                {
                    object light = lights.GetValue(i);
                    GameTypes.ExtraLight_Intensity.SetValue(light, __state[i]);
                    lights.SetValue(light, i);
                }
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        // ---------------------------------------------------------------- report

        /// <summary>Once per raid: what the game actually ships for this pass, which the code alone cannot say.</summary>
        private static void ReportOnce(object highlight, Camera camera)
        {
            if (_reportedGeneration == NightDriver.WorldGeneration || NightDriver.Location == null)
            {
                return;
            }

            _reportedGeneration = NightDriver.WorldGeneration;
            var text = new StringBuilder("Hand highlight: ");

            var passes = GameTypes.AmbientHighlight_Settings.GetValue(highlight) as Array;
            var described = new List<string>();
            for (int i = 0; passes != null && i < passes.Length; i++)
            {
                object pass = passes.GetValue(i);
                if (pass == null)
                {
                    continue;
                }

                string stencil = GameTypes.HighlightSettings_Stencil?.GetValue(pass)?.ToString() ?? "?";
                described.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1:0.##} to {2:0.##}",
                    stencil, (float)GameTypes.HighlightSettings_Min.GetValue(pass), (float)GameTypes.HighlightSettings_Max.GetValue(pass)));
            }
            text.Append(described.Count == 0 ? "no passes" : string.Join(", ", described.ToArray()));

            var lights = GameTypes.AmbientHighlight_ExtraLights?.GetValue(highlight) as Array;
            if (lights != null && lights.Length > 0 && GameTypes.ExtraLight_Intensity != null)
            {
                var intensities = new List<string>();
                foreach (object light in lights)
                {
                    intensities.Add(((float)GameTypes.ExtraLight_Intensity.GetValue(light)).ToString("0.##", CultureInfo.InvariantCulture));
                }
                text.Append("; fixed lights " + string.Join(", ", intensities.ToArray()));
            }
            else
            {
                text.Append("; no fixed lights");
            }

            text.Append(camera == null ? "" : camera.allowHDR ? "; HDR camera" : "; NOT HDR - glow left vanilla");
            Report = text.ToString();
            DarkNightsPlugin.Log.LogInfo(Report);
        }

        private static void Fail(Exception e)
        {
            if (_failed)
            {
                return;
            }

            _failed = true;
            DarkNightsPlugin.Log.LogError("Hand glow turned off after an error; hands are vanilla. " + e);
        }
    }
}

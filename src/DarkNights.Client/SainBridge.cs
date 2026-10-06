using System;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using HarmonyLib;

namespace DarkNights.Client
{
    /// <summary>
    /// Makes SAIN's bots share the night the player sees.
    ///
    /// Bots never read rendered light. Vanilla EFT scales their sight by the clock hour; SAIN
    /// (4.5.1, read from github.com/ArchangelWTF/SAIN) replaces that with its own clock model
    /// -- dusk 20:00 to 22:00, dawn 06:00 to 08:00, a flat night multiplier between -- in
    /// SAIN.Components.BotController.TimeClass. Everything SAIN does with the dark comes from
    /// that class's one private method, getModifier(time, timeOfDay, out visibilityRatio):
    /// view distance, how much longer bots take to spot you, when they put lights and NVGs
    /// on and off, and its close-range always-visible rule.
    ///
    /// So this postfixes that method and swaps SAIN's clock-based visibility for one read off
    /// the same sky Dark Nights darkens: the sun's real elevation, the moon's height and
    /// phase, cloud hiding the moon. SAIN's own night minimum is kept (recovered from its own
    /// answer, so its snow setting and the player's config still apply), and SAIN still adds
    /// its weather penalty on top. Nothing else in SAIN is touched.
    ///
    /// SAIN runs wherever bots run, so under Fika this has to be installed on the host.
    /// </summary>
    internal static class SainBridge
    {
        internal const string SainGuid = "me.sol.sain";
        private const string TimeClassName = "SAIN.Components.BotController.TimeClass";
        private const string MethodName = "getModifier";

        private static bool _failed;
        private static float _minimum = 0.2f;     // SAIN's default NightTimeVisionModifier, until one is seen
        private static bool _minimumSeen;

        internal static bool Installed;
        internal static float LastClockRatio = float.NaN;
        internal static float LastSkyRatio = float.NaN;
        internal static float LastModifier = float.NaN;

        internal static void Install(Harmony harmony, ManualLogSource log)
        {
            if (!Chainloader.PluginInfos.TryGetValue(SainGuid, out var sain))
            {
                log.LogInfo("SAIN is not loaded; bots keep vanilla night vision.");
                return;
            }

            MethodInfo target = Find();
            if (target == null)
            {
                log.LogWarning($"SAIN {sain.Metadata.Version} is loaded, but {TimeClassName}.{MethodName} was not found. " +
                               "Bots keep SAIN's own clock-based night.");
                return;
            }

            try
            {
                harmony.Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(SainBridge), nameof(Postfix))));
                Installed = true;
                log.LogInfo($"SAIN {sain.Metadata.Version}: bots see by the same sky as you (Bots Share The Night).");
            }
            catch (Exception e)
            {
                log.LogError($"Could not patch SAIN's {MethodName}: {e.Message}. Bots keep SAIN's own night.");
            }
        }

        /// <summary>By name and shape -- (float, ETimeOfDay, out float) returning float -- not by SAIN's types.</summary>
        private static MethodInfo Find()
        {
            Type time = AccessTools.TypeByName(TimeClassName);
            if (time == null)
            {
                return null;
            }

            foreach (MethodInfo m in time.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
            {
                ParameterInfo[] p = m.GetParameters();
                if (m.Name == MethodName
                    && m.ReturnType == typeof(float)
                    && p.Length == 3
                    && p[0].ParameterType == typeof(float)
                    && p[1].ParameterType.IsEnum
                    && p[2].ParameterType == typeof(float).MakeByRefType())
                {
                    return m;
                }
            }

            return null;
        }

        /// <summary>__2 is the out visibilityRatio, which SAIN's caller turns into the gain-sight slowdown.</summary>
        private static void Postfix(ref float __result, ref float __2)
        {
            if (_failed)
            {
                return;
            }

            try
            {
                float clockRatio = __2;
                LastClockRatio = clockRatio;

                if (NightModel.TryInferSainMinimum(clockRatio, __result, out float minimum))
                {
                    _minimum = minimum;
                    _minimumSeen = true;
                }

                NightDriver.Get();
                if (!DarkNightsPlugin.BotsShareTheNight.Value || !string.IsNullOrEmpty(NightDriver.Idle))
                {
                    LastSkyRatio = float.NaN;
                    LastModifier = __result;
                    return;
                }

                float sky = NightModel.BotVisibility(NightDriver.Inputs, NightDriver.Settings);

                __2 = sky;
                __result = NightModel.SainModifier(_minimum, sky);

                LastSkyRatio = sky;
                LastModifier = __result;
            }
            catch (Exception e)
            {
                _failed = true;
                DarkNightsPlugin.Log.LogError("SAIN bridge turned off after an error; bots keep SAIN's own night. " + e);
            }
        }

        internal static string Report()
        {
            if (!Installed)
            {
                return "SAIN bridge off";
            }

            string sky = float.IsNaN(LastSkyRatio) ? "not applied" : LastSkyRatio.ToString("0.00");
            return $"SAIN clock {LastClockRatio:0.00} sky {sky} -> bot sight x{LastModifier:0.00} " +
                   $"(night min {_minimum:0.00}{(_minimumSeen ? string.Empty : ", SAIN default")})";
        }
    }
}

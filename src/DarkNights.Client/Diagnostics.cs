using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;

namespace DarkNights.Client
{
    /// <summary>
    /// The A/B key, the two brightness-bubble experiments, and one log line that says what the
    /// night is and what was done about it. The presets are untuned until this line has been
    /// read on real nights, so it is on by default.
    /// </summary>
    internal static class Diagnostics
    {
        private static float _nextLog;
        private static string _changedSetting;
        private static float _settingLogAt;

        /// <summary>
        /// Called for every change made in the F12 menu. A slider fires this on every step of a
        /// drag, so the line is written once, half a second after the last change.
        /// </summary>
        internal static void SettingChanged(string setting)
        {
            _changedSetting = setting;
            _settingLogAt = Time.unscaledTime + 0.5f;
        }

        internal static void HandleKeys()
        {
            if (DarkNightsPlugin.ToggleKey.Value.IsDown())
            {
                NightDriver.Suspended = !NightDriver.Suspended;
                DarkNightsPlugin.Log.LogInfo(NightDriver.Suspended ? "A/B: vanilla nights." : "A/B: Dark Nights.");
                Write();
            }

            if (DarkNightsPlugin.DumpKey.Value.IsDown())
            {
                Write();
            }

            if (DarkNightsPlugin.FillLightsKey.Value.IsDown())
            {
                Interiors.FillLightsOff = !Interiors.FillLightsOff;
                DarkNightsPlugin.Log.LogInfo("Test: interior fill light " + (Interiors.FillLightsOff ? "OFF." : "back on."));
            }

            if (DarkNightsPlugin.CullingKey.Value.IsDown())
            {
                Interiors.RangeMultiplier = Interiors.RangeMultiplier == 1f ? 4f : 1f;
                DarkNightsPlugin.Log.LogInfo(Interiors.RangeMultiplier == 1f
                    ? "Test: interior volumes back to their own range."
                    : "Test: interior volumes drawn 4x further out. Watch whether the bubble's edge moves.");
            }
        }

        internal static void Tick()
        {
            if (_changedSetting != null && Time.unscaledTime >= _settingLogAt)
            {
                DarkNightsPlugin.Log.LogInfo("Setting changed: " + _changedSetting);
                _changedSetting = null;
                Write();
            }

            float interval = DarkNightsPlugin.LogInterval.Value;
            if (interval <= 0f || Time.unscaledTime < _nextLog)
            {
                return;
            }

            // Computes this frame's night if nothing else has yet, which also reads the map.
            NightDriver.Get();
            if (NightDriver.Location == null)
            {
                return;
            }

            _nextLog = Time.unscaledTime + interval;
            Write();
        }

        /// <summary>
        /// The first line of every raid: the map, the version, and every setting that is not at
        /// its default -- so a report of "too bright" or "bots see me" comes with the settings
        /// it was played on, not only the changes made during the session.
        /// </summary>
        internal static void RaidStarted()
        {
            try
            {
                var changed = new List<string>();
                foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> pair in DarkNightsPlugin.Configuration)
                {
                    ConfigEntryBase entry = pair.Value;
                    if (!Equals(entry.BoxedValue, entry.DefaultValue))
                    {
                        changed.Add(pair.Key.Key + " = " + Convert.ToString(entry.BoxedValue, CultureInfo.InvariantCulture));
                    }
                }

                DarkNightsPlugin.Log.LogInfo(
                    $"Raid started: {NightDriver.Location}. Dark Nights {DarkNightsPlugin.PluginVersion}, darkness {DarkNightsPlugin.Darkness.Value}. " +
                    $"Settings changed from default: {(changed.Count == 0 ? "none" : string.Join("; ", changed.ToArray()))}.");
            }
            catch (Exception e)
            {
                Faults.Report(Faults.Part.Diagnostics, "the log is missing some lines", e);
            }
        }

        internal static void Write()
        {
            try
            {
                WriteLine();
            }
            catch (Exception e)
            {
                Faults.Report(Faults.Part.Diagnostics, "the [night] line is not written", e);
            }
        }

        private static void WriteLine()
        {
            NightState s = NightDriver.Get();
            if (!string.IsNullOrEmpty(NightDriver.Idle))
            {
                DarkNightsPlugin.Log.LogInfo("[night] vanilla: " + NightDriver.Idle + " | errors " + Faults.Summary());
                return;
            }

            NightInputs i = NightDriver.Inputs;
            string hour = float.IsNaN(NightDriver.Hour) ? "?" : $"{(int)NightDriver.Hour:00}:{(int)((NightDriver.Hour % 1f) * 60f):00}";

            Light light = GameTypes.SkyLight(GameTypes.Sky());
            string lightText = light == null
                ? "light ?"
                : $"light {light.intensity:0.###} (game {Patches.MoonlightTracking.Base:0.###})";

            string ranges = float.IsNaN(Interiors.NearestRange)
                ? "range ?"
                : Interiors.NearestRange == Interiors.FurthestRange
                    ? $"range {Interiors.NearestRange:0} m"
                    : $"range {Interiors.NearestRange:0}-{Interiors.FurthestRange:0} m";

            DarkNightsPlugin.Log.LogInfo(string.Format(CultureInfo.InvariantCulture,
                "[night] {0} {1} | sun {2:0.0} moon {3:0.0} phase {4:0.00} | cloud {5:0.00} fog {6:0.0000} rain {7:0.00} | NVG {8} | " +
                "{9} night {10:0.00} -> sky {11} moon {12} interior {13} | {14} | {15} | {16} | " +
                "interiors {17} ambient / {18} fill, {19}{20}{21} | written by others: light {22}, exposure {23} | reflections {25:0.###} (game {26:0.###}) | " +
                "flat ambient {27} x{28:0.##} -> x{29:0.00} | sky SH {32:0.####} | bunker {31:0.00}, daylight here {33:0.00} (open sky {34:0.00}) | {35} | {24} | {30} | errors {36}",
                NightDriver.Location, hour,
                i.SunElevation, i.MoonElevation, i.MoonPhase,
                i.Cloudiness, i.Fog, i.Rain,
                i.NightVisionOn ? "on" : "off",
                DarkNightsPlugin.Darkness.Value, s.Nightness,
                Applied(s.Ambient, DarkNightsPlugin.DarkenSky.Value),
                Applied(s.Moonlight, DarkNightsPlugin.DarkenMoonlight.Value),
                Applied(s.Interior, DarkNightsPlugin.DarkenInteriors.Value),
                lightText, EyeAdaptation.Report, Environment(),
                Interiors.AmbientCount, Interiors.FillCount, ranges,
                Interiors.RangeMultiplier != 1f ? " (TEST x4)" : string.Empty,
                Interiors.FillLightsOff ? " (TEST fill off)" : string.Empty,
                Patches.MoonlightTracking.BaseChanges, EyeAdaptation.OtherWrites,
                SainBridge.Report() + CloudSixBridge.Report(),
                Reflections.LastWritten, Reflections.LastBase,
                ColorUtility.ToHtmlStringRGB(Patches.FlatAmbientBase), Patches.FlatAmbientIntensity, Patches.FlatAmbientFactor,
                BotDarkness.Report(), NightDriver.Bunker, Patches.LastSkyStrength, Daylight.Here, Daylight.Openness, Perf.Report(), Faults.Summary()));
        }

        /// <summary>The multiplier, or "off" when its part is switched off in the F12 menu.</summary>
        private static string Applied(float factor, bool on) =>
            on ? "x" + factor.ToString("0.00", CultureInfo.InvariantCulture) : "off";

        private static string Environment()
        {
            if (!GameTypes.EnvironmentReady)
            {
                return "indoor ?";
            }

            object manager = GameTypes.EnvironmentManager_Instance.GetValue(null, null);
            return manager == null ? "indoor ?" : GameTypes.EnvironmentManager_Environment.GetValue(manager, null)?.ToString().ToLowerInvariant();
        }
    }
}

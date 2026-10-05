using System.Globalization;
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

        internal static void Write()
        {
            NightState s = NightDriver.Get();
            if (!string.IsNullOrEmpty(NightDriver.Idle))
            {
                DarkNightsPlugin.Log.LogInfo("[night] vanilla: " + NightDriver.Idle);
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
                "{9} night {10:0.00} -> sky x{11:0.00} moon x{12:0.00} interior x{13:0.00} | {14} | {15} | {16} | " +
                "interiors {17} ambient / {18} fill, {19}{20}{21} | written by others: light {22}, exposure {23} | reflections {25:0.###} (game {26:0.###}) | {24}",
                NightDriver.Location, hour,
                i.SunElevation, i.MoonElevation, i.MoonPhase,
                i.Cloudiness, i.Fog, i.Rain,
                i.NightVisionOn ? "on" : "off",
                DarkNightsPlugin.Darkness.Value, s.Nightness, s.Ambient, s.Moonlight, s.Interior,
                lightText, EyeAdaptation.Report, Environment(),
                Interiors.AmbientCount, Interiors.FillCount, ranges,
                Interiors.RangeMultiplier != 1f ? " (TEST x4)" : string.Empty,
                Interiors.FillLightsOff ? " (TEST fill off)" : string.Empty,
                Patches.MoonlightTracking.BaseChanges, EyeAdaptation.OtherWrites,
                SainBridge.Report(),
                Reflections.LastWritten, Reflections.LastBase));
        }

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

using System;
using System.Text;

namespace DarkNights.Client
{
    /// <summary>
    /// Every error the mod catches, in one place, so a bug report's log says what failed,
    /// where, and what it did about it.
    ///
    /// Most of the mod runs inside the game's own methods -- Harmony patches on its render
    /// path -- where an uncaught exception would break the game's method, every frame, under
    /// a stack trace that never names Dark Nights. So every entry point catches, and reports
    /// here. The first error of each part is logged in full with the context needed to read
    /// it; repeats are only counted (the [night] line shows the counts). A part that keeps
    /// failing is turned off for the session, so a broken part neither floods the log nor
    /// costs an exception every frame. One-off errors -- a raid tearing down mid-frame --
    /// stay below the limit and change nothing.
    /// </summary>
    internal static class Faults
    {
        internal enum Part
        {
            Night,
            SkyAmbient,
            FlatAmbient,
            Moonlight,
            Daylight,
            Reflections,
            Exposure,
            Interiors,
            Diagnostics,
            Bots,
            HandGlow,
            Sain,
            CloudSix,
            NvgGlow,
            Count,
        }

        /// <summary>Errors a part survives before it is turned off for the session.</summary>
        private const int Limit = 10;

        private static readonly int[] Errors = new int[(int)Part.Count];
        private static readonly bool[] Off = new bool[(int)Part.Count];

        /// <summary>Whether a part has been turned off after repeated errors. Cheap enough for every call.</summary>
        internal static bool IsOff(Part part) => Off[(int)part];

        /// <summary>
        /// Records an error. "consequence" says what the player sees while the part is off,
        /// e.g. "the sky ambient is vanilla". With giveUp, the part is off from this error on.
        /// Never throws.
        /// </summary>
        internal static void Report(Part part, string consequence, Exception e, bool giveUp = false)
        {
            try
            {
                int i = (int)part;
                Errors[i]++;
                if (Errors[i] == 1)
                {
                    DarkNightsPlugin.Log.LogError(
                        $"{part} failed ({Context()}). {(giveUp ? "Turned off for this session: " + consequence + "." : "Skipping that call; it is turned off if it keeps failing.")} " +
                        $"Please report this with the whole LogOutput.log.\n{e}");
                }

                if (!Off[i] && (giveUp || Errors[i] >= Limit))
                {
                    Off[i] = true;
                    if (!giveUp)
                    {
                        DarkNightsPlugin.Log.LogError($"{part} failed {Errors[i]} times and is turned off for this session: {consequence}. " +
                                                      $"Last error: {e.GetType().Name}: {e.Message}");
                    }
                }
            }
            catch
            {
                // Reporting must never be the thing that breaks the game.
            }
        }

        /// <summary>For the [night] line: "none", or each failed part with its count.</summary>
        internal static string Summary()
        {
            var text = new StringBuilder();
            for (int i = 0; i < (int)Part.Count; i++)
            {
                if (Errors[i] == 0)
                {
                    continue;
                }

                text.Append(text.Length == 0 ? string.Empty : ", ")
                    .Append((Part)i).Append(" x").Append(Errors[i]).Append(Off[i] ? " (off)" : string.Empty);
            }

            return text.Length == 0 ? "none" : text.ToString();
        }

        private static string Context() =>
            $"Dark Nights {DarkNightsPlugin.PluginVersion}, map {NightDriver.Location ?? "none"}, " +
            $"darkness {DarkNightsPlugin.Darkness?.Value}, sun {NightDriver.Inputs.SunElevation:0.0}";
    }
}

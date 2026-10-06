using System.Diagnostics;
using System.Globalization;
using UnityEngine;

namespace DarkNights.Client
{
    /// <summary>
    /// What the mod's own work costs, measured, for the log line. Each part's time is summed
    /// between log lines and reported as milliseconds per second of play (10 ms/s is 1% of the
    /// frame time at any frame rate), along with the longest single run, which is what a
    /// hitch would show.
    /// </summary>
    internal static class Perf
    {
        internal enum Part
        {
            Daylight,
            Interiors,
            Bots,
            Count,
        }

        private static readonly long[] Total = new long[(int)Part.Count];
        private static readonly long[] Longest = new long[(int)Part.Count];
        private static float _since = -1f;

        internal static long Start() => Stopwatch.GetTimestamp();

        internal static void Stop(Part part, long started)
        {
            long spent = Stopwatch.GetTimestamp() - started;
            Total[(int)part] += spent;
            if (spent > Longest[(int)part])
            {
                Longest[(int)part] = spent;
            }
        }

        /// <summary>Per-second cost of each part since the last call, then starts counting again.</summary>
        internal static string Report()
        {
            float now = Time.unscaledTime;
            float seconds = _since < 0f ? 0f : now - _since;
            _since = now;

            string text = "perf";
            for (int i = 0; i < (int)Part.Count; i++)
            {
                double totalMs = Total[i] * 1000.0 / Stopwatch.Frequency;
                double longestMs = Longest[i] * 1000.0 / Stopwatch.Frequency;
                text += string.Format(CultureInfo.InvariantCulture, " {0} {1:0.00} ms/s (longest {2:0.00} ms)",
                    ((Part)i).ToString().ToLowerInvariant(), seconds > 0f ? totalMs / seconds : 0.0, longestMs);
                Total[i] = 0;
                Longest[i] = 0;
            }

            return text;
        }
    }
}

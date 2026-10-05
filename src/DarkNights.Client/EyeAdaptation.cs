using System.Collections.Generic;
using UnityEngine;

namespace DarkNights.Client
{
    /// <summary>
    /// Keeps auto-exposure from undoing the night.
    ///
    /// PrismEffects adapts exposure to the scene, between exposureLowerLimit and
    /// exposureUpperLimit, at a speed and toward a middle grey that EnvironmentManager hands it
    /// every frame. Darken the scene and adaptation brightens it back, up to the upper limit.
    /// So at night the upper limit comes down toward the preset's ceiling: the eye can only
    /// adapt so far, the way it can in real darkness. Anything already bright -- a flashlight's
    /// pool, a lit room -- never needed that much exposure and looks the same.
    ///
    /// The limit is only rewritten by the game when a Prism preset is applied, so it goes
    /// through Tracked: a preset load is adopted as the new base, never fought.
    /// </summary>
    internal static class EyeAdaptation
    {
        private const float ScanSeconds = 2f;

        private static readonly Dictionary<UnityEngine.Object, Tracked> Limits = new Dictionary<UnityEngine.Object, Tracked>();
        private static UnityEngine.Object[] _prisms = new UnityEngine.Object[0];
        private static float _nextScan;
        private static int _generation = -1;

        /// <summary>For the log: the first camera's state.</summary>
        internal static string Report = "not seen";

        internal static int OtherWrites
        {
            get
            {
                int n = 0;
                foreach (Tracked t in Limits.Values)
                {
                    n += t.BaseChanges;
                }
                return n;
            }
        }

        internal static void Tick()
        {
            if (!GameTypes.ExposureReady)
            {
                return;
            }

            if (NightDriver.WorldGeneration != _generation)
            {
                _generation = NightDriver.WorldGeneration;
                Limits.Clear();
                _nextScan = 0f;
            }

            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + ScanSeconds;
                _prisms = Object.FindObjectsOfType(GameTypes.PrismEffects);
            }

            NightState state = NightDriver.Get();
            float blend = DarkNightsPlugin.LimitEyeAdaptation.Value ? state.Exposure : 0f;
            float ceiling = NightDriver.Settings.ExposureCeiling;

            string report = null;
            foreach (UnityEngine.Object prism in _prisms)
            {
                if (prism == null)
                {
                    continue;
                }

                bool adapting = (bool)GameTypes.Prism_UseExposure.GetValue(prism);
                var lower = (float)GameTypes.Prism_LowerLimit.GetValue(prism);
                var upper = (float)GameTypes.Prism_UpperLimit.GetValue(prism);

                if (!Limits.TryGetValue(prism, out Tracked tracked))
                {
                    if (blend <= 0f)
                    {
                        report = report ?? Describe(prism, adapting, lower, upper, upper);
                        continue;
                    }

                    tracked = new Tracked();
                    Limits.Add(prism, tracked);
                }

                float floor = lower;
                float next = tracked.Apply(upper, b => NightModel.ExposureCeiling(b, Mathf.Max(ceiling, floor), blend));
                if (next != upper)
                {
                    GameTypes.Prism_UpperLimit.SetValue(prism, next);
                }

                report = report ?? Describe(prism, adapting, lower, tracked.Base, next);
            }

            Report = report ?? "no PrismEffects in the scene";
        }

        private static string Describe(UnityEngine.Object prism, bool adapting, float lower, float baseUpper, float upper)
        {
            string grey = GameTypes.Prism_MiddleGrey == null ? "?" : ((float)GameTypes.Prism_MiddleGrey.GetValue(prism)).ToString("0.###");
            return $"auto-exposure {(adapting ? "on" : "OFF")}, limits {lower:0.##} to {baseUpper:0.##}" +
                   (upper != baseUpper ? $" (night ceiling {upper:0.##})" : string.Empty) + $", grey {grey}";
        }
    }
}

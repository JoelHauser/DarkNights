using System;
using UnityEngine;

namespace DarkNights.Client
{
    /// <summary>
    /// Reads the world once per frame and hands every applier the same NightState.
    ///
    /// Computed lazily on first use in a frame, so it does not matter which of the game's
    /// LateUpdates (WeatherController's, which calls SetSH; TOD_Sky's, which writes the
    /// light) or this plugin's own runs first -- they all see one answer.
    /// </summary>
    internal static class NightDriver
    {
        private static int _frame = -1;
        private static NightState _state = NightState.Vanilla;
        private static float _nextGoggleScan;
        private static UnityEngine.Object[] _goggles = new UnityEngine.Object[0];

        /// <summary>The A/B key. True puts vanilla nights back until pressed again.</summary>
        internal static bool Suspended;

        internal static NightInputs Inputs;
        internal static NightSettings Settings;
        internal static string Location;
        internal static float Hour = float.NaN;

        /// <summary>Why the state is vanilla, when it is. Empty when darkening is live.</summary>
        internal static string Idle = "not started";

        /// <summary>Changes whenever a different raid's world is up, so caches can be dropped.</summary>
        internal static int WorldGeneration;
        private static string _lastWorldKey;

        internal static NightState Get()
        {
            if (Time.frameCount != _frame)
            {
                _frame = Time.frameCount;
                _state = Compute();
            }

            return _state;
        }

        private static NightState Compute()
        {
            Settings = DarkNightsPlugin.CurrentSettings();
            Location = GameTypes.LocationId();

            string worldKey = Location == null ? null : Location + "#" + GameTypes.GameWorld_Instance.GetValue(null, null)?.GetHashCode();
            if (worldKey != _lastWorldKey)
            {
                _lastWorldKey = worldKey;
                WorldGeneration++;
            }

            if (!DarkNightsPlugin.Enabled.Value)
            {
                return Vanilla("turned off in the config");
            }

            if (Suspended)
            {
                return Vanilla("suspended by the A/B key");
            }

            if (Location == null)
            {
                return Vanilla("not in a raid");
            }

            if (DarkNightsPlugin.IsExcluded(Location))
            {
                return Vanilla($"'{Location}' is in Excluded Maps");
            }

            object sky = GameTypes.Sky();
            if (sky == null)
            {
                return Vanilla("no TOD_Sky in this scene");
            }

            Vector3 sun = (Vector3)GameTypes.TOD_Sky_SunDirection.GetValue(sky, null);
            Vector3 moon = (Vector3)GameTypes.TOD_Sky_MoonDirection.GetValue(sky, null);

            var input = new NightInputs
            {
                SunElevation = Elevation(sun),
                MoonElevation = Elevation(moon),
                MoonPhase = 0.5f - 0.5f * Vector3.Dot(moon.normalized, sun.normalized),
                NightVisionOn = NightVisionOn(),
            };

            object curve = GameTypes.WeatherCurve();
            if (curve != null)
            {
                input.Cloudiness = (float)GameTypes.WeatherCurve_Cloudiness.GetValue(curve, null);
                input.Fog = (float)GameTypes.WeatherCurve_Fog.GetValue(curve, null);
                input.Rain = (float)GameTypes.WeatherCurve_Rain.GetValue(curve, null);
            }

            Hour = ReadHour(sky);
            Inputs = input;
            Idle = string.Empty;
            return NightModel.Evaluate(input, Settings);
        }

        private static NightState Vanilla(string why)
        {
            Idle = why;
            return NightState.Vanilla;
        }

        private static float Elevation(Vector3 direction)
        {
            float y = Mathf.Clamp(direction.normalized.y, -1f, 1f);
            return Mathf.Asin(y) * Mathf.Rad2Deg;
        }

        private static float ReadHour(object sky)
        {
            if (GameTypes.TOD_Sky_Cycle == null || GameTypes.TOD_Cycle_Hour == null)
            {
                return float.NaN;
            }

            object cycle = GameTypes.TOD_Sky_Cycle.GetValue(sky, null);
            return cycle == null ? float.NaN : (float)GameTypes.TOD_Cycle_Hour.GetValue(cycle);
        }

        /// <summary>
        /// Any NightVision effect switched on. The components live on the player's cameras and
        /// are found again every two seconds, which is cheap and survives a camera swap.
        /// </summary>
        private static bool NightVisionOn()
        {
            if (!GameTypes.NightVisionReady)
            {
                return false;
            }

            if (Time.unscaledTime >= _nextGoggleScan)
            {
                _nextGoggleScan = Time.unscaledTime + 2f;
                _goggles = UnityEngine.Object.FindObjectsOfType(GameTypes.NightVision);
            }

            foreach (UnityEngine.Object goggle in _goggles)
            {
                if (goggle != null && (bool)GameTypes.NightVision_On.GetValue(goggle, null))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

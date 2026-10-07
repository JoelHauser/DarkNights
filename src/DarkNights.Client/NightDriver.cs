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

        /// <summary>0 outside a bunker, 1 inside, fading over BunkerFadeSeconds like EFT's own indoor fade.</summary>
        internal static float Bunker;

        /// <summary>How night it is by the sun alone, before your bunker darkens anything. Bots judge targets by this.</summary>
        internal static float SkyNightness;
        private const float BunkerFadeSeconds = 1f;
        private static bool _wasInBunker;

        /// <summary>Why the state is vanilla, when it is. Empty when darkening is live.</summary>
        internal static string Idle = "not started";

        /// <summary>Changes whenever a different raid's world is up, so caches can be dropped.</summary>
        internal static int WorldGeneration;
        private static string _excludedFor;
        private static string _excludedText;
        private static object _lastWorld;
        private static string _lastLocation;

        internal static NightState Get()
        {
            if (Time.frameCount != _frame)
            {
                _frame = Time.frameCount;
                long started = Perf.Start();
                if (Faults.IsOff(Faults.Part.Night))
                {
                    _state = Vanilla("stopped after repeated errors (see the log)");
                }
                else
                {
                    try
                    {
                        _state = Compute();
                    }
                    catch (Exception e)
                    {
                        // Every part asks for this, many from inside the game's own methods: an
                        // error here becomes vanilla lighting for the frame, never a throw.
                        Faults.Report(Faults.Part.Night, "every part is vanilla", e);
                        _state = Vanilla("error (see the log)");
                    }
                }

                Perf.Stop(Perf.Part.Night, started);
                LogTransitions();
            }

            return _state;
        }

        private static string _loggedIdle;
        private static string _raidLocation;

        /// <summary>
        /// One line when a raid starts or ends, and one whenever the mod goes idle or comes
        /// back -- an excluded map, the A/B key, turned off -- so a log shows which stretches of
        /// play were darkened at all.
        /// </summary>
        private static void LogTransitions()
        {
            if (!string.Equals(Location, _raidLocation))
            {
                if (_raidLocation != null)
                {
                    DarkNightsPlugin.Log.LogInfo($"Raid ended ({_raidLocation}).");
                }

                _raidLocation = Location;
                if (Location != null)
                {
                    Diagnostics.RaidStarted();
                }
            }

            if (!string.Equals(Idle, _loggedIdle))
            {
                _loggedIdle = Idle;
                DarkNightsPlugin.Log.LogInfo(string.IsNullOrEmpty(Idle) ? "Lighting: Dark Nights active." : "Lighting: vanilla -- " + Idle + ".");
            }
        }

        private static NightState Compute()
        {
            Settings = DarkNightsPlugin.CurrentSettings();
            Location = GameTypes.LocationId();

            // Compared by reference every frame, so nothing is built to compare.
            object world = Location == null ? null : GameTypes.GameWorld_Instance.GetValue(null, null);
            if (!ReferenceEquals(world, _lastWorld) || !string.Equals(Location, _lastLocation))
            {
                _lastWorld = world;
                _lastLocation = Location;
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
                // Built once per map, not every frame.
                if (!string.Equals(_excludedFor, Location))
                {
                    _excludedFor = Location;
                    _excludedText = $"'{Location}' is in Excluded Maps";
                }

                return Vanilla(_excludedText);
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

            bool inBunker = InBunker();
            if (inBunker != _wasInBunker)
            {
                _wasInBunker = inBunker;
                DarkNightsPlugin.Log.LogInfo(inBunker ? "Bunker: entered (the map flags this area as a bunker)." : "Bunker: left.");
            }

            float target = DarkNightsPlugin.DarkBunkers.Value && inBunker ? 1f : 0f;
            Bunker = Mathf.MoveTowards(Bunker, target, Time.unscaledDeltaTime / BunkerFadeSeconds);
            NightState night = NightModel.Evaluate(input, Settings);
            SkyNightness = night.Nightness;

            // A flagged bunker is sealed outright; anywhere else, sealed by however little sky
            // reaches you. At night only: by day rooms are vanilla.
            float sealedOff = NightModel.RoomDarkness(Mathf.Max(Bunker, 1f - Daylight.Here), night.Nightness);
            return NightModel.WithBunker(night, Settings, sealedOff, input.NightVisionOn);
        }

        /// <summary>Your player is in an area the map flags as a bunker -- the flag that also muffles outside sound.</summary>
        private static bool InBunker()
        {
            if (!GameTypes.BunkerReady)
            {
                return false;
            }

            object manager = GameTypes.EnvironmentManager_Instance.GetValue(null, null);
            return manager != null && (bool)GameTypes.EnvironmentManager_InBunker.GetValue(manager, null);
        }

        /// <summary>Whether a point is inside an area the map flags as a bunker. For bots judging a target.</summary>
        internal static bool IsBunkerAt(Vector3 position)
        {
            if (!GameTypes.BunkerAtReady || !DarkNightsPlugin.DarkBunkers.Value)
            {
                return false;
            }

            object manager = GameTypes.EnvironmentManager_Instance.GetValue(null, null);
            object trigger = manager == null ? null : GameTypes.EnvironmentManager_TriggerAt.Invoke(manager, new object[] { position });
            return trigger is UnityEngine.Object o && o != null && (bool)GameTypes.IndoorTrigger_IsBunker.GetValue(trigger);
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
        /// are read off the live cameras every two seconds, which is cheap and survives a camera swap.
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
                _goggles = CameraComponents.Find(GameTypes.NightVision);
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

using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkNights.Client
{
    /// <summary>
    /// Holds a raid at a chosen hour and weather, for tuning. Off by default.
    ///
    /// The clock: TOD_Sky is drawn from TODSkyProvider's GameDateTime, and the raid (bots, SAIN)
    /// keeps GameWorld.GameDateTime; they may be the same object. Each is reset to the chosen
    /// hour and its TimeFactorMod set to 0, which makes Calculate() return that moment forever.
    /// Unfreezing resets it to the frozen moment first, so time carries on from there instead of
    /// jumping ahead by everything that passed while frozen.
    ///
    /// The weather: WeatherController.WeatherCurve returns its WeatherDebug instead of the
    /// server's forecast while WeatherDebug.isEnabled is set, so cloud, fog and rain are written
    /// there every frame. The server's weather arriving at raid start turns isEnabled off, which
    /// is why it is set again every frame rather than once.
    /// </summary>
    internal static class TestConditions
    {
        private static readonly Dictionary<object, float> SavedFactorMods = new Dictionary<object, float>();
        private static readonly List<object> Clocks = new List<object>(2);
        private static int _generation = -1;
        private static bool _weatherOverridden;

        internal static void Tick()
        {
            NightDriver.Get();
            if (NightDriver.WorldGeneration != _generation)
            {
                _generation = NightDriver.WorldGeneration;
                SavedFactorMods.Clear();
                _weatherOverridden = false;
            }

            if (NightDriver.Location == null)
            {
                return;
            }

            TickClock();
            TickWeather();
        }

        private static void TickClock()
        {
            if (!GameTypes.TestClockReady)
            {
                return;
            }

            if (!DarkNightsPlugin.FreezeTime.Value)
            {
                if (SavedFactorMods.Count > 0)
                {
                    foreach (KeyValuePair<object, float> saved in SavedFactorMods)
                    {
                        var now = (DateTime)GameTypes.GameDateTime_Calculate.Invoke(saved.Key, null);
                        Reset(saved.Key, now);
                        GameTypes.GameDateTime_TimeFactorMod.SetValue(saved.Key, saved.Value);
                    }

                    SavedFactorMods.Clear();
                    DarkNightsPlugin.Log.LogInfo("Test: clock running again.");
                }

                return;
            }

            FindClocks();
            TimeSpan hour = TimeSpan.FromHours(Mathf.Clamp(DarkNightsPlugin.FreezeHour.Value, 0f, 23.99f));
            foreach (object clock in Clocks)
            {
                float mod = (float)GameTypes.GameDateTime_TimeFactorMod.GetValue(clock);
                var now = (DateTime)GameTypes.GameDateTime_Calculate.Invoke(clock, null);
                DateTime wanted = now.Date + hour;
                if (mod == 0f && Math.Abs((now - wanted).TotalSeconds) < 30)
                {
                    continue;
                }

                if (!SavedFactorMods.ContainsKey(clock))
                {
                    SavedFactorMods.Add(clock, mod == 0f ? 1f : mod);
                }

                Reset(clock, wanted);
                GameTypes.GameDateTime_TimeFactorMod.SetValue(clock, 0f);
                DarkNightsPlugin.Log.LogInfo($"Test: clock frozen at {wanted:HH:mm} ({Clocks.Count} clock(s)).");
            }
        }

        private static void FindClocks()
        {
            Clocks.Clear();
            object sky = GameTypes.TODSkyProvider_Instance.GetValue(null);
            object time = sky == null ? null : GameTypes.TODSky_CurrentTime.GetValue(sky, null);
            Add(time == null ? null : GameTypes.TODTime_GameDateTime.GetValue(time));

            object world = GameTypes.GameWorld_Instance?.GetValue(null, null);
            if (world != null && GameTypes.GameWorld_GameDateTime != null)
            {
                Add(GameTypes.GameWorld_GameDateTime.GetValue(world));
            }
        }

        private static void Add(object clock)
        {
            if (clock != null && !Clocks.Contains(clock))
            {
                Clocks.Add(clock);
            }
        }

        private static void Reset(object clock, DateTime game)
        {
            float factor = (float)GameTypes.GameDateTime_TimeFactor.GetValue(clock, null);
            GameTypes.GameDateTime_Reset.Invoke(clock, new object[] { DateTime.UtcNow, game, factor, true });
        }

        private static void TickWeather()
        {
            if (!GameTypes.TestWeatherReady)
            {
                return;
            }

            object controller = GameTypes.WeatherController_Instance.GetValue(null);
            object debug = controller == null ? null : GameTypes.WeatherController_Debug.GetValue(controller);
            if (debug == null)
            {
                return;
            }

            if (!DarkNightsPlugin.OverrideWeather.Value)
            {
                if (_weatherOverridden)
                {
                    GameTypes.WeatherDebug_Enabled.SetValue(debug, false);
                    _weatherOverridden = false;
                    DarkNightsPlugin.Log.LogInfo("Test: weather back to the raid's forecast.");
                }

                return;
            }

            GameTypes.WeatherDebug_Cloud.SetValue(debug, DarkNightsPlugin.TestCloud.Value);
            GameTypes.WeatherDebug_Fog.SetValue(debug, DarkNightsPlugin.TestFog.Value);
            GameTypes.WeatherDebug_Rain.SetValue(debug, DarkNightsPlugin.TestRain.Value);
            GameTypes.WeatherDebug_Enabled.SetValue(debug, true);
            if (!_weatherOverridden)
            {
                _weatherOverridden = true;
                DarkNightsPlugin.Log.LogInfo("Test: weather overridden from the F12 menu.");
            }
        }
    }
}

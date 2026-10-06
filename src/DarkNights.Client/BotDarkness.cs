using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace DarkNights.Client
{
    /// <summary>
    /// Bots cannot see into the dark without a light or night vision.
    ///
    /// A bot sees a target only within LookSensor.VisibleDist + EnemyInfo.GetAdditionalSensorDistance
    /// (EnemyPartVision.CheckLineOfSight). SAIN adds its per-enemy distance to the second in a
    /// postfix; this postfix runs after it and caps the sum by how lit the target is where it
    /// stands. A target in an unlit room at night is seen only from a few metres; a target near
    /// a lamp, under a bright moon, or with its own light on is seen as before. A bot with its
    /// NVGs or its flashlight on is never capped.
    ///
    /// Lamps are found once per raid -- the only scene-wide search in the mod, and never on a
    /// timer -- and a lamp switched off later stops counting.
    /// </summary>
    internal static class BotDarkness
    {
        private struct Cached
        {
            public float Until;
            public float Value;
        }

        private const float TargetRefresh = 0.25f;
        private const float BotRefresh = 0.5f;

        /// <summary>
        /// A lamp as found at raid start. Lamps are fixtures, so where they are and how far they
        /// reach is read once; the per-check work is a distance test, and Unity is only asked
        /// whether a lamp is on for the few close enough to matter.
        /// </summary>
        private struct Lamp
        {
            public Light Light;
            public Vector3 Position;
            public float Range;
            public float RangeSquared;
        }

        private static readonly List<Lamp> Lamps = new List<Lamp>();
        private static readonly Dictionary<object, Cached> TargetLights = new Dictionary<object, Cached>();
        private static readonly Dictionary<object, Cached> BotSight = new Dictionary<object, Cached>();
        private static readonly Dictionary<object, Cached> TargetDaylights = new Dictionary<object, Cached>();
        private const float DaylightRefresh = 2f;
        private static int _generation = -1;
        private static bool _lampsFound;
        private static bool _failed;

        private static int _skyFrame = -1;
        private static float _skyVisibility = 1f;

        // For the log.
        internal static int LampCount;
        internal static long LampScanMs;
        internal static int CappedCalls;
        internal static float YourLight = float.NaN;
        internal static float YourCap = float.NaN;
        internal static bool YourInside;
        internal static bool YourBunker;
        internal static float YourDaylight = float.NaN;

        internal static void SensorDistancePostfix(object __instance, ref float __result)
        {
            if (_failed || !DarkNightsPlugin.DarknessBlindsBots.Value)
            {
                return;
            }

            long started = Perf.Start();
            try
            {
                NightDriver.Get();
                if (!string.IsNullOrEmpty(NightDriver.Idle))
                {
                    return;
                }

                Reset();
                object owner = GameTypes.EnemyInfo_Owner.GetValue(__instance, null);
                object person = GameTypes.EnemyInfo_Person.GetValue(__instance, null);
                if (owner == null || person == null || BotSeesInTheDark(owner))
                {
                    return;
                }

                object ai = GameTypes.IPlayer_AIData.GetValue(person, null);
                if (ai == null || (bool)GameTypes.AIData_UsingLight.GetValue(ai, null))
                {
                    return;
                }

                float cap = NightModel.SightCap(TargetLight(person, ai), DarkNightsPlugin.PitchBlackSight.Value);
                if (float.IsPositiveInfinity(cap))
                {
                    return;
                }

                object look = GameTypes.BotOwner_LookSensor.GetValue(owner, null);
                float visible = look == null ? 0f : (float)GameTypes.LookSensor_VisibleDist.GetValue(look, null);
                float limit = cap - visible;
                if (__result > limit)
                {
                    __result = limit;
                    CappedCalls++;
                }
            }
            catch (Exception e)
            {
                _failed = true;
                DarkNightsPlugin.Log.LogError("Bots in the dark turned off after an error: " + e);
            }
            finally
            {
                Perf.Stop(Perf.Part.Bots, started);
            }
        }

        private static void Reset()
        {
            if (NightDriver.WorldGeneration == _generation)
            {
                return;
            }

            _generation = NightDriver.WorldGeneration;
            Lamps.Clear();
            TargetLights.Clear();
            BotSight.Clear();
            TargetDaylights.Clear();
            _lampsFound = false;
            CappedCalls = 0;
        }

        private static bool BotSeesInTheDark(object owner)
        {
            float now = Time.time;
            if (BotSight.TryGetValue(owner, out Cached c) && c.Until > now)
            {
                return c.Value > 0f;
            }

            object goggles = GameTypes.BotOwner_NightVision.GetValue(owner, null);
            object light = GameTypes.BotOwner_BotLight.GetValue(owner, null);
            bool sees = (goggles != null && (bool)GameTypes.NightVisionData_UsingNow.GetValue(goggles, null))
                        || (light != null && (bool)GameTypes.BotLight_IsEnable.GetValue(light, null));
            BotSight[owner] = new Cached { Until = now + BotRefresh, Value = sees ? 1f : 0f };
            return sees;
        }

        private static float TargetLight(object person, object ai)
        {
            float now = Time.time;
            if (TargetLights.TryGetValue(person, out Cached c) && c.Until > now)
            {
                return c.Value;
            }

            if (_skyFrame != Time.frameCount)
            {
                _skyFrame = Time.frameCount;
                _skyVisibility = NightModel.BotVisibility(NightDriver.Inputs, NightDriver.Settings);
            }

            var position = (Vector3)GameTypes.IPlayer_Position.GetValue(person, null);
            bool bunker = NightDriver.IsBunkerAt(position);
            bool inside = bunker || (bool)GameTypes.AIData_IsInside.GetValue(ai, null);
            float daylight = bunker ? 0f : TargetDaylight(person, position, now);
            float value;
            if (NightDriver.SkyNightness <= 0f && daylight >= 1f)
            {
                // Open air by day: nothing to judge, and no lamp search.
                value = 1f;
            }
            else
            {
                value = NightModel.TargetLight(_skyVisibility, daylight, LampLight(position + Vector3.up));
            }
            TargetLights[person] = new Cached { Until = now + TargetRefresh, Value = value };

            if (GameTypes.IPlayer_IsYourPlayer != null && (bool)GameTypes.IPlayer_IsYourPlayer.GetValue(person, null))
            {
                YourLight = value;
                YourInside = inside;
                YourBunker = bunker;
                YourDaylight = daylight;
                YourCap = NightModel.SightCap(value, DarkNightsPlugin.PitchBlackSight.Value);
            }

            return value;
        }

        /// <summary>How much daylight reaches a target, probed at head height and kept for a couple of seconds.</summary>
        private static float TargetDaylight(object person, Vector3 position, float now)
        {
            if (!DarkNightsPlugin.DarkWithoutDaylight.Value)
            {
                return 1f;
            }

            if (TargetDaylights.TryGetValue(person, out Cached c) && c.Until > now)
            {
                return c.Value;
            }

            float value = NightModel.DaylightFromOpenness(Daylight.OpennessAt(position + Vector3.up * 1.5f));
            TargetDaylights[person] = new Cached { Until = now + DaylightRefresh, Value = value };
            return value;
        }

        /// <summary>
        /// How lit a point is by the scene's own lamps, 0 to 1. Point and spot lights only (the
        /// sun and moon are directional), falling off with distance across each light's range.
        /// Walls are not traced: a lamp counts if its range reaches the point.
        /// </summary>
        private static float LampLight(Vector3 point)
        {
            if (!_lampsFound)
            {
                _lampsFound = true;
                var watch = Stopwatch.StartNew();
                foreach (Light l in UnityEngine.Object.FindObjectsOfType<Light>())
                {
                    if (l.type == LightType.Point || l.type == LightType.Spot)
                    {
                        Lamps.Add(new Lamp
                        {
                            Light = l,
                            Position = l.transform.position,
                            Range = l.range,
                            RangeSquared = l.range * l.range,
                        });
                    }
                }

                LampScanMs = watch.ElapsedMilliseconds;
                LampCount = Lamps.Count;
                DarkNightsPlugin.Log.LogInfo($"Bots in the dark: {LampCount} lamps in this raid (found once, {LampScanMs} ms).");
            }

            float best = 0f;
            foreach (Lamp lamp in Lamps)
            {
                Vector3 toPoint = point - lamp.Position;
                float squared = toPoint.sqrMagnitude;
                if (squared >= lamp.RangeSquared)
                {
                    continue;
                }

                Light l = lamp.Light;
                if (l == null || !l.enabled || l.intensity <= 0f || !l.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (l.type == LightType.Spot && Vector3.Angle(l.transform.forward, toPoint) > l.spotAngle * 0.5f)
                {
                    continue;
                }

                float falloff = 1f - Mathf.Sqrt(squared) / lamp.Range;
                best = Math.Max(best, l.intensity * falloff * falloff);
            }

            // About half a unit of lamp light at the target counts as a lit room.
            return Mathf.Clamp01(best / 0.5f);
        }

        internal static string Report()
        {
            if (!GameTypes.BotDarknessReady || !DarkNightsPlugin.DarknessBlindsBots.Value)
            {
                return "bots in the dark off";
            }

            if (float.IsNaN(YourLight))
            {
                return $"bots in the dark: lamps {LampCount}, capped {CappedCalls}";
            }

            string cap = float.IsPositiveInfinity(YourCap) ? "no cap" : $"seen within {YourCap:0} m";
            return $"bots in the dark: you lit {YourLight:0.00} ({(YourBunker ? "bunker" : YourInside ? "inside" : "outside")}, " +
                   $"daylight {YourDaylight:0.00}) -> {cap} | " +
                   $"lamps {LampCount} ({LampScanMs} ms), capped {CappedCalls}";
        }
    }
}

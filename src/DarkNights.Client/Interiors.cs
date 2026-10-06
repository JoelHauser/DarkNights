using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkNights.Client
{
    /// <summary>
    /// The daylight the game carries into buildings, taken away at night.
    ///
    /// AmbientLight draws two kinds of volume around the camera: StencilShadow, which gives an
    /// interior a fixed ambient colour, and AnalyticSource, which adds fill light around doors
    /// and windows. Neither follows the time of day, so at night an interior near you keeps
    /// its daytime ambient while the sky outside goes dark. Darkening only the sky would make
    /// that contrast worse -- and both are drawn only within a set distance of the camera
    /// (100 m by default), which is the likeliest cause of the brightness bubble.
    ///
    /// Each volume is scaled from the colour it had, re-read whenever something else changes
    /// it, and put back exactly at dawn. Real lights are separate Light components and are
    /// never touched, so a lit room stays lit.
    /// </summary>
    internal static class Interiors
    {
        private sealed class AmbientState
        {
            public Color Original;
            public Color Written;
        }

        private sealed class FillState
        {
            public float Multiplier;
            public float Add;
            public float WrittenMultiplier;
            public float WrittenAdd;
        }

        private const float SweepSeconds = 5f;
        private const float Step = 0.05f;
        private const float RelativeStep = 0.25f;
        private const float MinPassInterval = 0.25f;

        private static readonly Dictionary<UnityEngine.Object, AmbientState> Ambients = new Dictionary<UnityEngine.Object, AmbientState>();
        private static readonly Dictionary<UnityEngine.Object, FillState> Fills = new Dictionary<UnityEngine.Object, FillState>();
        private static readonly Dictionary<object, float> Ranges = new Dictionary<object, float>();

        private static int _generation = -1;
        private static float _nextSweep;
        private static float _nextChangePass;
        private static float _appliedAmbient = 1f;
        private static float _appliedFill = 1f;
        private static float _appliedRange = 1f;
        private static float _appliedFloor = float.NaN;

        /// <summary>Diagnostic: fill light off entirely, to see whether the bubble goes with it.</summary>
        internal static bool FillLightsOff;

        /// <summary>Diagnostic: how far out the volumes are drawn, as a multiple of the game's own distance.</summary>
        internal static float RangeMultiplier = 1f;

        internal static int AmbientCount;
        internal static int FillCount;
        internal static float NearestRange = float.NaN;
        internal static float FurthestRange = float.NaN;

        internal static void Tick()
        {
            if (!GameTypes.InteriorsReady)
            {
                return;
            }

            if (NightDriver.WorldGeneration != _generation)
            {
                _generation = NightDriver.WorldGeneration;
                Ambients.Clear();
                Fills.Clear();
                Ranges.Clear();
                _appliedAmbient = _appliedFill = _appliedRange = 1f;
            }

            NightState state = NightDriver.Get();
            float ambient = DarkNightsPlugin.DarkenInteriors.Value ? state.Interior : 1f;
            float fill = FillLightsOff ? 0f : ambient;

            float floor = DarkNightsPlugin.InteriorFloor.Value;
            bool changed = Moved(ambient, _appliedAmbient) || Moved(fill, _appliedFill) || RangeMultiplier != _appliedRange
                           || floor != _appliedFloor;
            float now = Time.unscaledTime;

            // Vanilla wanted and nothing of ours in any volume -- day, outdoors, which is most of
            // play: the 5-second sweep would only read every volume to change none of them.
            if (ambient >= 1f && fill >= 1f && RangeMultiplier == 1f && Ambients.Count == 0 && Fills.Count == 0 && Ranges.Count == 0)
            {
                _appliedAmbient = _appliedFill = _appliedRange = 1f;
                return;
            }

            if ((!changed || now < _nextChangePass) && now < _nextSweep)
            {
                return;
            }

            // A pass rewrites every volume and rebuilds every fill light's GPU data, a few
            // milliseconds on a big map. Since 0.1.8 the factor moves whenever you walk through
            // a door, not just at dusk, so passes are held to MinPassInterval apart.
            _nextSweep = now + SweepSeconds;
            _nextChangePass = now + MinPassInterval;

            long started = Perf.Start();
            try
            {
                ApplyAmbient(ambient, floor);
                ApplyFill(fill);
                ApplyRange(RangeMultiplier);
            }
            catch (Exception e)
            {
                DarkNightsPlugin.Log.LogError("Interiors: " + e);
                _nextSweep = now + 60f;
            }

            Perf.Stop(Perf.Part.Interiors, started);

            _appliedAmbient = ambient;
            _appliedFill = fill;
            _appliedRange = RangeMultiplier;
            _appliedFloor = floor;
        }

        /// <summary>
        /// Changed enough to be worth a pass: by Step, or by RelativeStep of itself (so the
        /// small values of a dark night still move) -- and always when arriving back at exactly
        /// vanilla.
        /// </summary>
        private static bool Moved(float now, float applied)
        {
            float delta = Math.Abs(now - applied);
            return delta >= Step
                || delta >= RelativeStep * Math.Max(Math.Max(now, applied), 0.01f)
                || (now >= 1f && applied < 1f) || (now <= 0f && applied > 0f);
        }

        private static void ApplyAmbient(float factor, float floor)
        {
            int count = 0;
            foreach (object item in GameTypes.Registered(GameTypes.AmbientLight_StencilShadows))
            {
                var volume = item as UnityEngine.Object;
                if (volume == null)
                {
                    continue;
                }

                count++;
                var current = (Color)GameTypes.StencilShadow_Ambient.GetValue(item);
                if (!Ambients.TryGetValue(volume, out AmbientState s))
                {
                    if (factor >= 1f)
                    {
                        continue;
                    }

                    s = new AmbientState { Original = current, Written = current };
                    Ambients.Add(volume, s);
                }
                else if (current != s.Written)
                {
                    // Something else set it since we did. That is the new original.
                    s.Original = current;
                }

                float luminance = 0.2126f * s.Original.r + 0.7152f * s.Original.g + 0.0722f * s.Original.b;
                float m = NightModel.InteriorMultiplier(luminance, factor, floor);
                var next = new Color(s.Original.r * m, s.Original.g * m, s.Original.b * m, s.Original.a);
                if (next != current)
                {
                    GameTypes.StencilShadow_Ambient.SetValue(item, next);
                }
                s.Written = next;
            }

            AmbientCount = count;
        }

        private static void ApplyFill(float factor)
        {
            int count = 0;
            foreach (object item in GameTypes.Registered(GameTypes.AmbientLight_AnalyticSources))
            {
                var volume = item as UnityEngine.Object;
                if (volume == null)
                {
                    continue;
                }

                count++;
                var multiplier = (float)GameTypes.AnalyticSource_LightIntensityMultiplicator.GetValue(item);
                var add = (float)GameTypes.AnalyticSource_AddAmbient.GetValue(item);

                if (!Fills.TryGetValue(volume, out FillState s))
                {
                    if (factor >= 1f)
                    {
                        continue;
                    }

                    s = new FillState { Multiplier = multiplier, Add = add, WrittenMultiplier = multiplier, WrittenAdd = add };
                    Fills.Add(volume, s);
                }
                else if (multiplier != s.WrittenMultiplier || add != s.WrittenAdd)
                {
                    s.Multiplier = multiplier;
                    s.Add = add;
                }

                float nextMultiplier = s.Multiplier * factor;
                float nextAdd = s.Add * factor;
                if (nextMultiplier != multiplier || nextAdd != add)
                {
                    GameTypes.AnalyticSource_LightIntensityMultiplicator.SetValue(item, nextMultiplier);
                    GameTypes.AnalyticSource_AddAmbient.SetValue(item, nextAdd);

                    // The values only reach the GPU through the source's property block, which
                    // UpdateSettings rebuilds.
                    GameTypes.AnalyticSource_UpdateSettings.Invoke(item, null);
                }

                s.WrittenMultiplier = nextMultiplier;
                s.WrittenAdd = nextAdd;
            }

            FillCount = count;
        }

        /// <summary>Diagnostic only: multiply every volume's culling distance, or put it back.</summary>
        private static void ApplyRange(float multiplier)
        {
            if (GameTypes.CullingSettings_Distance == null || GameTypes.CullingSettings_Update == null)
            {
                return;
            }

            float nearest = float.MaxValue;
            float furthest = 0f;

            void Visit(object volume, System.Reflection.FieldInfo cullingField)
            {
                if (cullingField == null || !(volume is UnityEngine.Object o) || o == null)
                {
                    return;
                }

                object culling = cullingField.GetValue(volume);
                if (culling == null)
                {
                    return;
                }

                var distance = (float)GameTypes.CullingSettings_Distance.GetValue(culling);
                if (!Ranges.TryGetValue(culling, out float original))
                {
                    if (multiplier == 1f)
                    {
                        nearest = Math.Min(nearest, distance);
                        furthest = Math.Max(furthest, distance);
                        return;
                    }

                    original = distance;
                    Ranges.Add(culling, original);
                }

                nearest = Math.Min(nearest, original);
                furthest = Math.Max(furthest, original);

                float next = original * multiplier;
                if (next != distance)
                {
                    GameTypes.CullingSettings_Distance.SetValue(culling, next);
                    GameTypes.CullingSettings_Update.Invoke(culling, null);
                }
            }

            foreach (object item in GameTypes.Registered(GameTypes.AmbientLight_StencilShadows))
            {
                Visit(item, GameTypes.StencilShadow_Culling);
            }

            foreach (object item in GameTypes.Registered(GameTypes.AmbientLight_AnalyticSources))
            {
                Visit(item, GameTypes.AnalyticSource_Culling);
            }

            if (multiplier == 1f)
            {
                Ranges.Clear();
            }

            NearestRange = furthest > 0f ? nearest : float.NaN;
            FurthestRange = furthest > 0f ? furthest : float.NaN;
        }
    }
}

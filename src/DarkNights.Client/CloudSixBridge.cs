using System;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace DarkNights.Client
{
    /// <summary>
    /// Darkens CloudSix's volumetric clouds with the rest of the night.
    ///
    /// CloudSix (matsix, 4.0.0, github.com/matsixx/CloudSix, read 2026-10-05) turns the game's
    /// clouds off and draws its own. It lights them from its own inputs, not the spherical
    /// harmonics Dark Nights scales: _MoonIntensity from its config, _MoonColor from TOD's
    /// MoonLightColor, _AmbientColor from ToDController.AddTopAmbient or its own sky's
    /// view LUT, times _AmbientStrength. So without this its clouds keep vanilla night
    /// brightness over a darkened world.
    ///
    /// It writes those every frame in a postfix on WeatherController.LateUpdate. This is a
    /// second postfix on the same method, ordered after CloudSix's, scaling _MoonIntensity by
    /// the moonlight factor and _AmbientStrength by the sky-ambient factor. _AmbientColor is
    /// left alone: the shader is compiled into CloudSix's bundle, and if _AmbientStrength
    /// multiplies the colour, scaling both would darken the ambient twice. Through Tracked,
    /// so a frame where CloudSix returns early and writes nothing is not scaled again.
    ///
    /// The rest of CloudSix sits fine beside Dark Nights, from its source:
    ///   - "Ground Lighting From Sky" swaps the hue of ToDController.LightColor/AddTopAmbient
    ///     by day and leaves night vanilla. Upstream of SetSH, so the darkening stacks on it.
    ///   - "Disable Eye Adaptation" (default on) locks Prism's lower and upper exposure limits
    ///     to "World Exposure" every frame. Dark Nights' exposure ceiling never goes below the
    ///     lower limit, so it simply does nothing then -- no fight.
    ///   - Its custom sky, its own sun and moon discs, and its postfix on TOD_Sky.LateUpdate
    ///     (which only switches the atmosphere renderer off) do not touch anything we scale.
    /// </summary>
    internal static class CloudSixBridge
    {
        internal const string CloudSixGuid = "com.matsix.cloudsix";
        private const string RendererType = "CloudSix.Source.VolCloudRenderer";
        private static readonly int MoonIntensity = Shader.PropertyToID("_MoonIntensity");
        private static readonly int AmbientStrength = Shader.PropertyToID("_AmbientStrength");

        private static FieldInfo _material;
        private static Material _current;
        private static Tracked _moon = new Tracked();
        private static Tracked _ambient = new Tracked();
        private static bool _failed;

        internal static bool Installed;

        internal static void Install(Harmony harmony, ManualLogSource log)
        {
            if (!Chainloader.PluginInfos.TryGetValue(CloudSixGuid, out var cloudSix))
            {
                return;
            }

            _material = AccessTools.TypeByName(RendererType)?.GetField("lowMaterial", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo target = GameTypes.WeatherController?.GetMethod("LateUpdate",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);

            if (_material == null || _material.FieldType != typeof(Material) || target == null)
            {
                log.LogWarning($"CloudSix {cloudSix.Metadata.Version} is loaded, but its cloud material was not found. " +
                               "Its clouds keep their own night brightness.");
                return;
            }

            try
            {
                var postfix = new HarmonyMethod(AccessTools.Method(typeof(CloudSixBridge), nameof(Postfix)))
                {
                    after = new[] { CloudSixGuid },
                    priority = Priority.Last,
                };
                harmony.Patch(target, postfix: postfix);
                Installed = true;
                log.LogInfo($"CloudSix {cloudSix.Metadata.Version}: its clouds darken with the night.");
            }
            catch (Exception e)
            {
                log.LogError("Could not patch for CloudSix: " + e.Message);
            }
        }

        private static void Postfix()
        {
            if (_failed)
            {
                return;
            }

            NightState state = NightDriver.Get();
            long started = Perf.Start();
            try
            {
                var material = _material.GetValue(null) as Material;
                if (material == null)
                {
                    return;
                }

                if (!ReferenceEquals(material, _current))
                {
                    _current = material;
                    _moon = new Tracked();
                    _ambient = new Tracked();
                }

                bool on = DarkNightsPlugin.DarkenClouds.Value;
                Scale(material, MoonIntensity, _moon, on ? state.Moonlight : 1f);
                Scale(material, AmbientStrength, _ambient, on ? state.Ambient : 1f);
            }
            catch (Exception e)
            {
                _failed = true;
                DarkNightsPlugin.Log.LogError("CloudSix bridge turned off after an error; its clouds keep their own brightness. " + e);
            }
            finally
            {
                Perf.Stop(Perf.Part.Hooks, started);
            }
        }

        private static void Scale(Material material, int property, Tracked tracked, float factor)
        {
            if (!material.HasProperty(property))
            {
                return;
            }

            float current = material.GetFloat(property);
            float next = tracked.Apply(current, factor);
            if (next != current)
            {
                material.SetFloat(property, next);
            }
        }

        internal static string Report()
        {
            if (!Installed)
            {
                return string.Empty;
            }

            return $" | CloudSix moon {_moon.Base:0.##} -> x{(_moon.Base > 0f && _current != null && _current.HasProperty(MoonIntensity) ? _current.GetFloat(MoonIntensity) / _moon.Base : 1f):0.00}, " +
                   $"ambient {_ambient.Base:0.##} -> x{(_ambient.Base > 0f && _current != null && _current.HasProperty(AmbientStrength) ? _current.GetFloat(AmbientStrength) / _ambient.Base : 1f):0.00}";
        }
    }
}

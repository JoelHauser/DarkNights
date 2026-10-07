using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DarkNights.Client
{
    /// <summary>
    /// The green glow on the face of anyone wearing switched-on NVGs, made visible at night.
    ///
    /// Each worn NVG model carries a NightVisionDevice with one Light (_light), which the game
    /// enables while the goggles are on -- for bots too: BotNightVisionData switches the same
    /// item. The light is tuned for vanilla nights and reads as nothing against a dark one, so
    /// at night its intensity (and a little of its range) is raised by NVG Eyepiece Glow,
    /// fading in with dusk. Your own goggles are skipped: their light sits at the camera.
    ///
    /// Devices are collected from a postfix on Init, never by scanning the scene. Values are
    /// scaled from the light's own, re-read whenever something else changes them, and put
    /// back when the factor returns to 1.
    /// </summary>
    internal static class NvgGlow
    {
        private sealed class Glow
        {
            public Light Light;
            public float BaseIntensity;
            public float BaseRange;
            public float WrittenIntensity;
            public float WrittenRange;
            public bool Logged;
        }

        /// <summary>A light this close to the camera is your own goggles'.</summary>
        private const float OwnDistance = 0.75f;

        /// <summary>Log lines per raid describing the glow lights, so their real values are known.</summary>
        private const int MaxLogged = 4;

        private static readonly List<Glow> Glows = new List<Glow>();
        private static int _generation = -1;
        private static int _logged;

        internal static void Install(Harmony harmony)
        {
            if (!GameTypes.NvgGlowReady)
            {
                DarkNightsPlugin.Log.LogWarning("Not brightening the NVG eyepiece glow: its game members were not found.");
                return;
            }

            try
            {
                harmony.Patch(GameTypes.NightVisionDevice_Init,
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(NvgGlow), nameof(InitPostfix))));
            }
            catch (Exception e)
            {
                DarkNightsPlugin.Log.LogError("Could not patch NightVisionDevice.Init for the NVG glow: " + e.Message);
            }
        }

        internal static void InitPostfix(object __instance)
        {
            if (Faults.IsOff(Faults.Part.NvgGlow))
            {
                return;
            }

            try
            {
                var light = GameTypes.NightVisionDevice_Light.GetValue(__instance) as Light;
                if (light == null)
                {
                    return;
                }

                foreach (Glow g in Glows)
                {
                    if (ReferenceEquals(g.Light, light))
                    {
                        return;
                    }
                }

                Glows.Add(new Glow
                {
                    Light = light,
                    BaseIntensity = light.intensity,
                    BaseRange = light.range,
                    WrittenIntensity = light.intensity,
                    WrittenRange = light.range,
                });
            }
            catch (Exception e)
            {
                Faults.Report(Faults.Part.NvgGlow, "NVG eyepiece glow is vanilla", e);
            }
        }

        internal static void Tick()
        {
            if (NightDriver.WorldGeneration != _generation)
            {
                _generation = NightDriver.WorldGeneration;
                _logged = 0;
            }

            if (Glows.Count == 0)
            {
                return;
            }

            NightState night = NightDriver.Get();
            float factor = Mathf.Lerp(1f, Mathf.Max(1f, DarkNightsPlugin.NvgGlowAmount.Value), Mathf.Clamp01(night.Nightness));
            Camera camera = Camera.main;
            Vector3 eye = camera != null ? camera.transform.position : new Vector3(float.NaN, 0f, 0f);

            for (int i = Glows.Count - 1; i >= 0; i--)
            {
                Glow g = Glows[i];
                Light light = g.Light;
                if (light == null)
                {
                    Glows.RemoveAt(i);
                    continue;
                }

                // Something else set it since we did: that is the new base.
                if (light.intensity != g.WrittenIntensity)
                {
                    g.BaseIntensity = light.intensity;
                }
                if (light.range != g.WrittenRange)
                {
                    g.BaseRange = light.range;
                }

                float distance = Vector3.Distance(light.transform.position, eye);
                float f = distance < OwnDistance ? 1f : factor;

                if (light.enabled && !g.Logged && _logged < MaxLogged && distance >= OwnDistance)
                {
                    g.Logged = true;
                    _logged++;
                    DarkNightsPlugin.Log.LogInfo(
                        $"NVG glow: {light.transform.root.name} at {distance:0} m -- intensity {g.BaseIntensity:0.###}, range {g.BaseRange:0.##} m, " +
                        $"colour {ColorUtility.ToHtmlStringRGB(light.color)}, {light.type}, {light.renderMode}, culling mask 0x{light.cullingMask:X}, " +
                        $"shadows {light.shadows}, active {light.gameObject.activeInHierarchy} -> x{f:0.#}");
                }

                float intensity = g.BaseIntensity * f;
                float range = g.BaseRange * Mathf.Sqrt(f);
                if (light.intensity != intensity)
                {
                    light.intensity = intensity;
                }
                if (light.range != range)
                {
                    light.range = range;
                }
                g.WrittenIntensity = light.intensity;
                g.WrittenRange = light.range;
            }
        }
    }
}

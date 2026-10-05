using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace DarkNights.Client
{
    /// <summary>
    /// Darker, more realistic nights for SPT.
    ///
    /// Not an overlay and not a brightness slider. The game builds its night from a handful of
    /// separate things -- the sky's ambient light, the moon's direct light, the ambient the
    /// interiors carry, and the eye adaptation that brightens whatever is dark -- and each is
    /// darkened at the point the game hands it on, by the same NightState, so the world dims
    /// as one thing. Flashlights, lamps, muzzle flash and every other real light are never
    /// touched, so they matter more. Thermal is untouched by construction: it draws surface
    /// colour and temperature, not lighting.
    ///
    /// Each part multiplies what the game (or another mod) produced instead of setting the
    /// same field, so nothing is fought over. See Tracked.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(SainBridge.SainGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class DarkNightsPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.mybutthasarash.darknights";
        public const string PluginName = "Dark Nights";

        // Must match <Version> in DarkNights.Client.csproj. pack.ps1 refuses to pack if not.
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<DarknessLevel> Darkness;
        internal static ConfigEntry<string> ExcludedMaps;

        internal static ConfigEntry<bool> DarkenSky;
        internal static ConfigEntry<bool> DarkenMoonlight;
        internal static ConfigEntry<bool> DarkenClouds;
        internal static ConfigEntry<bool> DarkenInteriors;
        internal static ConfigEntry<bool> LimitEyeAdaptation;
        internal static ConfigEntry<bool> DarkenReflections;
        internal static ConfigEntry<float> HandGlowAmount;
        internal static ConfigEntry<bool> HandGlowByDay;

        internal static ConfigEntry<float> TwilightStart;
        internal static ConfigEntry<float> TwilightEnd;
        internal static ConfigEntry<float> Overcast;
        internal static ConfigEntry<float> FogDark;
        internal static ConfigEntry<float> RainDark;
        internal static ConfigEntry<float> NightVisionRetention;
        internal static ConfigEntry<float> InteriorFloor;

        internal static ConfigEntry<float> CustomMoonless;
        internal static ConfigEntry<float> CustomFullMoon;
        internal static ConfigEntry<float> CustomMoonlight;
        internal static ConfigEntry<float> CustomInterior;
        internal static ConfigEntry<float> CustomExposureCeiling;

        internal static ConfigEntry<bool> BotsShareTheNight;
        internal static ConfigEntry<float> BotsShareStrength;

        internal static ConfigEntry<float> LogInterval;
        internal static ConfigEntry<KeyboardShortcut> ToggleKey;
        internal static ConfigEntry<KeyboardShortcut> DumpKey;
        internal static ConfigEntry<KeyboardShortcut> FillLightsKey;
        internal static ConfigEntry<KeyboardShortcut> CullingKey;

        private static readonly HashSet<string> Excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private void Awake()
        {
            Log = Logger;
            BindConfig();

            GameTypes.Resolve(Log);
            var harmony = new Harmony(PluginGuid);
            Install(harmony);
            HandGlow.Install(harmony);
            SainBridge.Install(harmony, Log);

            Log.LogInfo($"{PluginName} {PluginVersion} loaded. Darkness: {Darkness.Value}.");
        }

        private void Update()
        {
            Diagnostics.HandleKeys();
        }

        private void LateUpdate()
        {
            Interiors.Tick();
            Reflections.Tick();
            EyeAdaptation.Tick();
            Diagnostics.Tick();
        }

        // ------------------------------------------------------------------ config

        private void BindConfig()
        {
            const string general = "1. General";
            const string parts = "2. What gets darker";
            const string world = "3. How the world reacts";
            const string custom = "4. Custom darkness";
            const string diag = "6. Diagnostics";
            const string bots = "5. Bots (SAIN)";

            Enabled = Config.Bind(general, "Enabled", true, "Turn the whole mod off without uninstalling it.");
            Darkness = Config.Bind(general, "Darkness", DarknessLevel.Medium,
                "How dark nights get. Seven levels, Lightest to Darkest; Custom uses section 4.");
            ExcludedMaps = Config.Bind(general, "Excluded Maps", "laboratory,labyrinth,factory4_day,factory4_night,hideout",
                "Location ids left at vanilla lighting, comma separated. The defaults are the maps that are " +
                "indoors with their own lighting.");
            ExcludedMaps.SettingChanged += (_, __) => ParseExcluded();
            ParseExcluded();

            DarkenSky = Config.Bind(parts, "Sky Ambient", true,
                "The light the sky casts on everything outdoors. The main part of a dark night.");
            DarkenMoonlight = Config.Bind(parts, "Moonlight", true, "The moon's direct, shadow-casting light.");
            DarkenClouds = Config.Bind(parts, "Clouds", true,
                "Clouds are lit by the same sky ambient. Without this they glow against a darkened sky.");
            DarkenInteriors = Config.Bind(parts, "Interiors", true,
                "Daylight carried into buildings by the game's interior ambient. Interiors that are already dark, " +
                "and every real lamp, are left alone.");
            LimitEyeAdaptation = Config.Bind(parts, "Limit Eye Adaptation", true,
                "Caps how far auto-exposure can brighten a dark night, so the darkness is not adapted straight " +
                "back away. Lit areas still expose normally.");
            DarkenReflections = Config.Bind(parts, "Reflections", true,
                "The sky reflection on metal, glass and wet ground -- your weapon included. Without this, shiny " +
                "surfaces keep their daytime shine against a dark world. Screen-space reflections already follow " +
                "the darker scene.");
            HandGlowAmount = Config.Bind(parts, "Hand Glow", 0f,
                new ConfigDescription("EFT draws an extra ambient pass over your hands -- and your weapon, wherever the " +
                    "game counts it as part of them -- plus characters where the game sets it, so they stay bright. " +
                    "That is the glow. 0 removes it, leaving them lit like everything else; 1 is vanilla.",
                    new AcceptableValueRange<float>(0f, 1f)));
            HandGlowByDay = Config.Bind(parts, "Hand Glow Off By Day Too", true,
                "On: the glow setting applies day and night. Off: by day it is vanilla, and it fades with dusk.");

            TwilightStart = Config.Bind(world, "Dusk Starts (sun degrees)", Presets.TwilightStart,
                new ConfigDescription("Sun elevation where darkening begins. Nothing above it changes, so day is vanilla.",
                    new AcceptableValueRange<float>(-6f, 6f)));
            TwilightEnd = Config.Bind(world, "Night Complete (sun degrees)", Presets.TwilightEnd,
                new ConfigDescription("Sun elevation where darkening is complete. -12 is nautical dusk.",
                    new AcceptableValueRange<float>(-18f, -3f)));
            Overcast = Config.Bind(world, "Overcast Darkening", 0.6f,
                new ConfigDescription("Extra multiplier under full cloud, which blocks the moon and the stars. 1 = none.",
                    new AcceptableValueRange<float>(0.1f, 1f)));
            FogDark = Config.Bind(world, "Fog Darkening", 0.75f,
                new ConfigDescription("Extra multiplier in thick fog. 1 = none.", new AcceptableValueRange<float>(0.1f, 1f)));
            RainDark = Config.Bind(world, "Rain Darkening", 0.85f,
                new ConfigDescription("Extra multiplier in heavy rain. 1 = none.", new AcceptableValueRange<float>(0.1f, 1f)));
            NightVisionRetention = Config.Bind(world, "Night Vision Keeps Vanilla", 0.5f,
                new ConfigDescription("While NVGs are on, how much of the vanilla night they see. 0 = they see the full " +
                    "darkness (realistic, and noisier); 1 = NVGs behave exactly as vanilla.",
                    new AcceptableValueRange<float>(0f, 1f)));
            InteriorFloor = Config.Bind(world, "Interior Floor", 0.02f,
                new ConfigDescription("Interior ambient at or below this brightness is never darkened, and nothing is " +
                    "darkened below it.", new AcceptableValueRange<float>(0f, 0.2f)));

            NightSettings medium = Presets.For(DarknessLevel.Medium);
            CustomMoonless = Config.Bind(custom, "Moonless Sky Ambient", medium.MoonlessAmbient,
                new ConfigDescription("Sky ambient on a clear moonless night, as a share of vanilla.", new AcceptableValueRange<float>(0.02f, 1f)));
            CustomFullMoon = Config.Bind(custom, "Full Moon Sky Ambient", medium.FullMoonAmbient,
                new ConfigDescription("Sky ambient under a high, clear full moon, as a share of vanilla.", new AcceptableValueRange<float>(0.02f, 1f)));
            CustomMoonlight = Config.Bind(custom, "Moonlight", medium.Moonlight,
                new ConfigDescription("The moon's direct light, as a share of vanilla.", new AcceptableValueRange<float>(0.02f, 1f)));
            CustomInterior = Config.Bind(custom, "Interior Ambient", medium.Interior,
                new ConfigDescription("Interior daylight ambient at night, as a share of vanilla.", new AcceptableValueRange<float>(0.02f, 1f)));
            CustomExposureCeiling = Config.Bind(custom, "Night Exposure Ceiling", medium.ExposureCeiling,
                new ConfigDescription("The highest auto-exposure may reach at full night, in the game's own units " +
                    "(its range is -6 to 6). Lower is darker.", new AcceptableValueRange<float>(-6f, 6f)));

            BotsShareTheNight = Config.Bind(bots, "Bots Share The Night", true,
                "Bots never see rendered light; SAIN judges the dark by the clock hour. On, SAIN judges it by the " +
                "same sky you see instead: the real sunset, the moon's height and phase, cloud hiding the moon. " +
                "SAIN keeps its own night strength and weather. No effect without SAIN. Under Fika, the host's " +
                "setting is the one that counts.");
            BotsShareStrength = Config.Bind(bots, "Strength", 1f,
                new ConfigDescription("1 = bots go entirely by the sky; 0 = SAIN's clock; between blends the two.",
                    new AcceptableValueRange<float>(0f, 1f)));

            LogInterval = Config.Bind(diag, "Log Interval (seconds)", 30f,
                new ConfigDescription("How often a line describing the night goes into the BepInEx log during a raid. 0 = never.",
                    new AcceptableValueRange<float>(0f, 600f)));
            ToggleKey = Config.Bind(diag, "A/B Toggle", new KeyboardShortcut(KeyCode.F8, KeyCode.LeftControl),
                "Switch between Dark Nights and vanilla, to compare.");
            DumpKey = Config.Bind(diag, "Log Now", new KeyboardShortcut(KeyCode.F11, KeyCode.LeftControl),
                "Write the full diagnostic line to the log immediately.");
            FillLightsKey = Config.Bind(diag, "Test: Interior Fill Light Off", new KeyboardShortcut(KeyCode.F9, KeyCode.LeftControl),
                "Turns the game's interior fill-light volumes off and on. For tracking down the brightness bubble.");
            CullingKey = Config.Bind(diag, "Test: Interior Volume Range x4", new KeyboardShortcut(KeyCode.F10, KeyCode.LeftControl),
                "Draws the interior ambient volumes four times further out, and back. If the bubble's edge moves " +
                "with it, the bubble is those volumes. Costs GPU while on.");
        }

        private static void ParseExcluded()
        {
            Excluded.Clear();
            foreach (string id in (ExcludedMaps.Value ?? string.Empty).Split(','))
            {
                string trimmed = id.Trim();
                if (trimmed.Length > 0)
                {
                    Excluded.Add(trimmed);
                }
            }
        }

        internal static bool IsExcluded(string locationId) => Excluded.Contains(locationId);

        /// <summary>The preset (or Custom) with the shared world settings laid over it.</summary>
        internal static NightSettings CurrentSettings()
        {
            NightSettings s = Presets.For(Darkness.Value);
            if (Darkness.Value == DarknessLevel.Custom)
            {
                s.MoonlessAmbient = CustomMoonless.Value;
                s.FullMoonAmbient = CustomFullMoon.Value;
                s.Moonlight = CustomMoonlight.Value;
                s.Interior = CustomInterior.Value;
                s.ExposureCeiling = CustomExposureCeiling.Value;
            }

            s.TwilightStart = TwilightStart.Value;
            s.TwilightEnd = TwilightEnd.Value;
            s.Overcast = Overcast.Value;
            s.FogDark = FogDark.Value;
            s.RainDark = RainDark.Value;
            s.NightVisionRetention = NightVisionRetention.Value;
            return s;
        }

        // ----------------------------------------------------------------- patches

        private static void Install(Harmony harmony)
        {
            Patch(harmony, GameTypes.AmbientReady, GameTypes.AmbientLight_SetSH, nameof(Patches.SkyAmbientPrefix), prefix: true, "sky ambient");
            Patch(harmony, GameTypes.HighlightReady, GameTypes.AmbientHighlight_SetSH, nameof(Patches.HighlightPrefix), prefix: true, "character highlight");
            Patch(harmony, GameTypes.CloudsReady, GameTypes.CloudController_UpdateAmbient, nameof(Patches.CloudsPrefix), prefix: true, "clouds");
            Patch(harmony, GameTypes.MoonlightReady, GameTypes.TOD_Sky_LateUpdate, nameof(Patches.MoonlightPostfix), prefix: false, "moonlight");
        }

        private static void Patch(Harmony harmony, bool ready, System.Reflection.MethodInfo target, string patch, bool prefix, string what)
        {
            if (!ready)
            {
                Log.LogWarning($"Not darkening the {what}: its game members were not found.");
                return;
            }

            try
            {
                var method = new HarmonyMethod(AccessTools.Method(typeof(Patches), patch));
                if (prefix)
                {
                    harmony.Patch(target, prefix: method);
                }
                else
                {
                    harmony.Patch(target, postfix: method);
                }
            }
            catch (Exception e)
            {
                Log.LogError($"Could not patch {target.DeclaringType?.Name}.{target.Name} for the {what}: {e.Message}");
            }
        }
    }
}

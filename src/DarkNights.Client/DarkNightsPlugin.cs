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
    [BepInDependency(CloudSixBridge.CloudSixGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class DarkNightsPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.mybutthasarash.darknights";
        public const string PluginName = "Dark Nights";

        // Must match <Version> in DarkNights.Client.csproj. pack.ps1 refuses to pack if not.
        public const string PluginVersion = "0.1.10";

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
        internal static ConfigEntry<bool> DarkBunkers;
        internal static ConfigEntry<bool> DarkWithoutDaylight;
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
        internal static ConfigEntry<bool> DarknessBlindsBots;
        internal static ConfigEntry<float> PitchBlackSight;

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
            Config.SettingChanged += (_, e) =>
                Diagnostics.SettingChanged($"{e.ChangedSetting.Definition.Key} = {e.ChangedSetting.BoxedValue}");

            GameTypes.Resolve(Log);
            var harmony = new Harmony(PluginGuid);
            Install(harmony);
            HandGlow.Install(harmony);
            SainBridge.Install(harmony, Log);
            CloudSixBridge.Install(harmony, Log);

            Log.LogInfo($"{PluginName} {PluginVersion} loaded. Darkness: {Darkness.Value}.");
        }

        private void Update()
        {
            Diagnostics.HandleKeys();
        }

        private void LateUpdate()
        {
            NightDriver.Get();
            long started = Perf.Start();
            Reflections.Tick();
            EyeAdaptation.Tick();
            Perf.Stop(Perf.Part.Upkeep, started);

            Daylight.Tick();     // timed itself
            Interiors.Tick();    // timed itself
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
                "The light the sky casts on everything, the main part of a dark night. It is also what takes the " +
                "sky's light out of rooms by day: off, rooms and bunkers keep it and only their interior ambient darkens.");
            DarkenMoonlight = Config.Bind(parts, "Moonlight", true, "The moon's direct, shadow-casting light.");
            DarkenClouds = Config.Bind(parts, "Clouds", true,
                "Clouds are lit by the same sky ambient. Without this they glow against a darkened sky.");
            DarkenInteriors = Config.Bind(parts, "Interiors", true,
                "Daylight carried into buildings by the game's interior ambient. Interiors that are already dark, " +
                "and every real lamp, are left alone.");
            DarkWithoutDaylight = Config.Bind(parts, "Rooms Without Daylight Are Dark", true,
                "At any hour, a place only gets the daylight that can reach it. Dark Nights measures how much open sky " +
                "is visible from where you stand -- through windows and open doors, not through walls or closed doors. " +
                "A room is dark at noon, windows or not: it is lit only where the sun's beams fall through them and by " +
                "its lamps, and the sky's even fill comes back only as you reach a doorway or step outside. No eye " +
                "adaptation. Bots without a light or NVGs cannot see into such a room either. While you are in a dark " +
                "room, what you see through its doorway dims with it.");
            DarkBunkers = Config.Bind(parts, "Bunkers Dark By Day", true,
                "Areas the map marks as bunkers -- the ones where outside sound goes muffled -- get no daylight, so " +
                "inside one it is as dark as a moonless overcast night at any hour. Lamps still light them, and bots " +
                "without a light or NVGs cannot see into them either. Ordinary rooms are not marked and follow the time of day.");
            LimitEyeAdaptation = Config.Bind(parts, "Limit Eye Adaptation", true,
                "Caps how far auto-exposure can brighten a dark night, so the darkness is not adapted straight " +
                "back away. Lit areas still expose normally. No effect while CloudSix's Disable Eye Adaptation is on " +
                "(its default): there is no auto-exposure then to limit.");
            DarkenReflections = Config.Bind(parts, "Reflections", true,
                "The sky reflection on metal, glass and wet ground -- your weapon included. Without this, shiny " +
                "surfaces keep their daytime shine against a dark world. With SSR on in the graphics settings " +
                "(or SSRSix installed) the game does not use this value: reflections then come from the screen, " +
                "which already follows the darker night, and this setting has nothing to do.");
            HandGlowAmount = Config.Bind(parts, "Hand Glow", 0f,
                new ConfigDescription("EFT draws an extra ambient pass over your hands -- and your weapon, wherever the " +
                    "game counts it as part of them -- plus characters where the game sets it, so they stay bright. " +
                    "That is the glow. 0 removes it, leaving them lit like everything else; 1 is vanilla. HDR only: " +
                    "with HDR off the game draws it differently, removing it would black your hands out, and it is " +
                    "left vanilla (the log says so).",
                    new AcceptableValueRange<float>(0f, 1f)));
            HandGlowByDay = Config.Bind(parts, "Hand Glow Off By Day Too", true,
                "On: the glow setting applies day and night. Off: by day it is vanilla, and it fades with dusk.");

            TwilightStart = Config.Bind(world, "Dusk Starts (sun degrees)", Presets.TwilightStart,
                new ConfigDescription("Only matters at dusk and dawn. Sun elevation where darkening begins; nothing " +
                    "above it changes, so day is vanilla.",
                    new AcceptableValueRange<float>(-6f, 6f)));
            TwilightEnd = Config.Bind(world, "Night Complete (sun degrees)", Presets.TwilightEnd,
                new ConfigDescription("Only matters at dusk and dawn. Sun elevation where darkening is complete; " +
                    "-12 is nautical dusk. Always taken as at least 1 degree below Dusk Starts.",
                    new AcceptableValueRange<float>(-18f, -3f)));
            Overcast = Config.Bind(world, "Overcast Darkening", 0.6f,
                new ConfigDescription("Only matters under cloud (none on a clear night). Extra multiplier under full " +
                    "cloud, which blocks the moon and the stars. 1 = none.",
                    new AcceptableValueRange<float>(0.1f, 1f)));
            FogDark = Config.Bind(world, "Fog Darkening", 0.75f,
                new ConfigDescription("Only matters in fog. Extra multiplier in thick fog. 1 = none.",
                    new AcceptableValueRange<float>(0.1f, 1f)));
            RainDark = Config.Bind(world, "Rain Darkening", 0.85f,
                new ConfigDescription("Only matters in rain. Extra multiplier in heavy rain. 1 = none.",
                    new AcceptableValueRange<float>(0.1f, 1f)));
            NightVisionRetention = Config.Bind(world, "Night Vision Keeps Vanilla", 0.5f,
                new ConfigDescription("Only matters with NVGs on. How much of the vanilla night they see. 0 = they see " +
                    "the full darkness (realistic, and noisier); 1 = NVGs behave exactly as vanilla.",
                    new AcceptableValueRange<float>(0f, 1f)));
            InteriorFloor = Config.Bind(world, "Interior Floor", 0.005f,
                new ConfigDescription("Rooms whose ambient is already at or below this brightness are left alone, and no " +
                    "room is darkened below it. Higher keeps dark rooms more visible; 0 lets every room go fully dark.",
                    new AcceptableValueRange<float>(0f, 0.2f)));

            NightSettings medium = Presets.For(DarknessLevel.Medium);
            const string customOnly = "Only used when Darkness is set to Custom. ";
            CustomMoonless = Config.Bind(custom, "Moonless Sky Ambient", medium.MoonlessAmbient,
                new ConfigDescription(customOnly + "Sky ambient on a clear moonless night, as a share of vanilla.",
                    new AcceptableValueRange<float>(0.02f, 1f)));
            CustomFullMoon = Config.Bind(custom, "Full Moon Sky Ambient", medium.FullMoonAmbient,
                new ConfigDescription(customOnly + "Sky ambient under a high, clear full moon, as a share of vanilla. Taken as at least " +
                    "Moonless Sky Ambient, so the moon never darkens the night.",
                    new AcceptableValueRange<float>(0.02f, 1f)));
            CustomMoonlight = Config.Bind(custom, "Moonlight", medium.Moonlight,
                new ConfigDescription(customOnly + "The moon's direct light, as a share of vanilla.",
                    new AcceptableValueRange<float>(0.02f, 1f)));
            CustomInterior = Config.Bind(custom, "Interior Ambient", medium.Interior,
                new ConfigDescription(customOnly + "How much of the night outside reaches an unlit room, as a share.",
                    new AcceptableValueRange<float>(0.02f, 1f)));
            CustomExposureCeiling = Config.Bind(custom, "Night Exposure Ceiling", medium.ExposureCeiling,
                new ConfigDescription(customOnly + "The highest auto-exposure may reach at full night, in the game's own " +
                    "units (its range is -6 to 6). Lower is darker. No effect while CloudSix's Disable Eye Adaptation is on.",
                    new AcceptableValueRange<float>(-6f, 6f)));

            BotsShareTheNight = Config.Bind(bots, "Bots Share The Night", true,
                "On: SAIN judges the dark by the same sky you see -- the real sunset, the moon's height and phase, " +
                "cloud hiding the moon. Off: SAIN's own clock (dusk 20:00-22:00, moon ignored). Bots never see " +
                "rendered light, so this is what makes them share your night. SAIN keeps its own night strength and " +
                "weather. No effect without SAIN. Under Fika, the host's setting is the one that counts.");
            DarknessBlindsBots = Config.Bind(bots, "Darkness Blinds Bots", true,
                "On: a bot with no flashlight and no NVGs on cannot see you in the dark -- in an unlit room, at any " +
                "hour, only from a few metres; outdoors at night further the brighter the moon. Standing near a lamp, " +
                "or using your own light, makes you visible as normal. Works with or without SAIN. They can still hear you.");
            PitchBlackSight = Config.Bind(bots, "Pitch Black Sight (m)", 3f,
                new ConfigDescription("How close a bot without light or NVGs must be to see you in total darkness.",
                    new AcceptableValueRange<float>(1f, 20f)));

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

                // A full moon never darker than no moon: set the other way round, the moon
                // rising would darken the night.
                s.FullMoonAmbient = Math.Max(CustomFullMoon.Value, CustomMoonless.Value);
                s.Moonlight = CustomMoonlight.Value;
                s.Interior = CustomInterior.Value;
                s.ExposureCeiling = CustomExposureCeiling.Value;
            }

            // The two sliders' ranges overlap (-6 to -3). Crossed, the curve runs backwards --
            // full night at noon -- so night always completes at least a degree below its start.
            s.TwilightStart = TwilightStart.Value;
            s.TwilightEnd = Math.Min(TwilightEnd.Value, TwilightStart.Value - 1f);
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
            Patch(harmony, GameTypes.FlatAmbientReady, GameTypes.LevelSettings_OnPreCull, nameof(Patches.FlatAmbientPostfix), prefix: false, "flat map ambient");
            Patch(harmony, GameTypes.MoonlightReady, GameTypes.TOD_Sky_LateUpdate, nameof(Patches.MoonlightPostfix), prefix: false, "moonlight");

            // After SAIN's own postfix on the same method, which adds its per-enemy distance:
            // the cap has to see the final sum.
            if (!GameTypes.BotDarknessReady)
            {
                Log.LogWarning("Bots in the dark: its game members were not found; bots see as before.");
                return;
            }

            try
            {
                var postfix = new HarmonyMethod(AccessTools.Method(typeof(BotDarkness), nameof(BotDarkness.SensorDistancePostfix)))
                {
                    priority = Priority.Last,
                    after = new[] { SainBridge.SainGuid },
                };
                harmony.Patch(GameTypes.EnemyInfo_AdditionalSensorDistance, postfix: postfix);
            }
            catch (Exception e)
            {
                Log.LogError("Could not patch EnemyInfo.GetAdditionalSensorDistance for bots in the dark: " + e.Message);
            }
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

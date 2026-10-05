using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace DarkNights.Client
{
    /// <summary>
    /// Every game type, member and method this plugin touches, resolved by name at load.
    ///
    /// Nothing is compiled against Assembly-CSharp. The copy in Managed is the unpatched
    /// original until the SPT Launcher applies its delta, and that delta renames obfuscated
    /// types. Everything here is a type with a readable name in both (TOD_Sky and its
    /// parameters are a third-party asset; AmbientLight, PrismEffects, NightVision and the
    /// weather classes are BSG's but not obfuscated), and is looked up by that name so a
    /// rename turns off one feature with a log line instead of the whole plugin.
    ///
    /// New game members belong here, never at the patch site.
    /// </summary>
    internal static class GameTypes
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        // ------------------------------------------------------------------ the sky

        internal static Type TOD_Sky;
        internal static PropertyInfo TOD_Sky_Instance;
        internal static PropertyInfo TOD_Sky_SunDirection;
        internal static PropertyInfo TOD_Sky_MoonDirection;
        internal static PropertyInfo TOD_Sky_Cycle;
        internal static FieldInfo TOD_Cycle_Hour;
        internal static PropertyInfo TOD_Sky_Components;
        internal static PropertyInfo TOD_Components_LightSource;
        internal static MethodInfo TOD_Sky_LateUpdate;

        // -------------------------------------------------------------- the weather

        internal static Type WeatherController;
        internal static FieldInfo WeatherController_Instance;
        internal static PropertyInfo WeatherController_WeatherCurve;
        internal static PropertyInfo WeatherCurve_Cloudiness;
        internal static PropertyInfo WeatherCurve_Fog;
        internal static PropertyInfo WeatherCurve_Rain;

        // -------------------------------------------------------------- the ambient

        internal static Type AmbientLight;
        internal static FieldInfo AmbientLight_ReflectionIntensity;
        internal static MethodInfo AmbientLight_SetSH;
        internal static MethodInfo AmbientHighlight_SetSH;
        internal static MethodInfo CloudController_UpdateAmbient;

        // ---------------------------------------------------------- the hand glow

        /// <summary>AmbientHighlight.ManualOnRenderObject(Camera): where the extra hands/characters pass is recorded.</summary>
        internal static MethodInfo AmbientHighlight_Render;
        internal static FieldInfo AmbientHighlight_Settings;          // HighlightSettings[] _highlightSettings
        internal static FieldInfo HighlightSettings_Min;              // HighlightMinMultiplier
        internal static FieldInfo HighlightSettings_Max;              // HighlightMaxMultiplier
        internal static FieldInfo HighlightSettings_Stencil;          // StencilTypeToUse
        internal static FieldInfo AmbientHighlight_ExtraLights;       // AmbientDirectionalLight[] _additionalLights
        internal static FieldInfo ExtraLight_Intensity;               // AmbientDirectionalLight.Intensity

        // ------------------------------------------------------------ the interiors

        internal static Type StencilShadow;
        internal static FieldInfo StencilShadow_Ambient;
        internal static FieldInfo StencilShadow_Culling;
        internal static Type AnalyticSource;
        internal static FieldInfo AnalyticSource_LightIntensityMultiplicator;
        internal static FieldInfo AnalyticSource_AddAmbient;
        internal static FieldInfo AnalyticSource_Culling;
        internal static MethodInfo AnalyticSource_UpdateSettings;
        internal static FieldInfo CullingSettings_Distance;
        internal static MethodInfo CullingSettings_Update;

        /// <summary>AmbientLight's private static registries, found by element type rather than by name.</summary>
        internal static FieldInfo AmbientLight_StencilShadows;
        internal static FieldInfo AmbientLight_AnalyticSources;

        // ------------------------------------------------------------- the exposure

        internal static Type PrismEffects;
        internal static FieldInfo Prism_UseExposure;
        internal static FieldInfo Prism_UpperLimit;
        internal static FieldInfo Prism_LowerLimit;
        internal static FieldInfo Prism_MiddleGrey;

        // ------------------------------------------------------------- the goggles

        internal static Type NightVision;
        internal static PropertyInfo NightVision_On;

        // ---------------------------------------------------------------- the raid

        internal static PropertyInfo GameWorld_Instance;
        internal static PropertyInfo GameWorld_LocationId;
        internal static PropertyInfo EnvironmentManager_Instance;
        internal static PropertyInfo EnvironmentManager_Environment;

        // ------------------------------------------------------------ what works

        internal static bool SkyReady;
        internal static bool WeatherReady;
        internal static bool AmbientReady;
        internal static bool HighlightReady;
        internal static bool HandGlowReady;
        internal static bool ReflectionsReady;
        internal static bool CloudsReady;
        internal static bool MoonlightReady;
        internal static bool InteriorsReady;
        internal static bool ExposureReady;
        internal static bool NightVisionReady;
        internal static bool WorldReady;
        internal static bool EnvironmentReady;

        private static readonly List<string> Missing = new List<string>();

        internal static void Resolve(ManualLogSource log)
        {
            // The sky. Without it there is no sun to measure, and nothing else is worth doing.
            TOD_Sky = Find("TOD_Sky");
            TOD_Sky_Instance = Prop(TOD_Sky, "Instance");
            TOD_Sky_SunDirection = Prop(TOD_Sky, "SunDirection");
            TOD_Sky_MoonDirection = Prop(TOD_Sky, "MoonDirection");
            TOD_Sky_Cycle = Prop(TOD_Sky, "Cycle");
            TOD_Cycle_Hour = Field(TOD_Sky_Cycle?.PropertyType, "Hour");
            TOD_Sky_Components = Prop(TOD_Sky, "Components");
            TOD_Components_LightSource = Prop(TOD_Sky_Components?.PropertyType, "LightSource");
            TOD_Sky_LateUpdate = Method(TOD_Sky, "LateUpdate", Type.EmptyTypes);
            SkyReady = All(TOD_Sky_Instance, TOD_Sky_SunDirection, TOD_Sky_MoonDirection);

            WeatherController = Find("EFT.Weather.WeatherController");
            WeatherController_Instance = Field(WeatherController, "Instance");
            WeatherController_WeatherCurve = Prop(WeatherController, "WeatherCurve");
            Type curve = WeatherController_WeatherCurve?.PropertyType;
            WeatherCurve_Cloudiness = Prop(curve, "Cloudiness");
            WeatherCurve_Fog = Prop(curve, "Fog");
            WeatherCurve_Rain = Prop(curve, "Rain");
            WeatherReady = All(WeatherController_Instance, WeatherController_WeatherCurve, WeatherCurve_Cloudiness, WeatherCurve_Fog, WeatherCurve_Rain);

            var sh = new[] { typeof(UnityEngine.Rendering.SphericalHarmonicsL2) };
            Type ambientLight = AmbientLight = Find("AmbientLight");
            AmbientLight_SetSH = Method(ambientLight, "SetSH", sh);
            AmbientLight_ReflectionIntensity = Field(ambientLight, "ReflectionIntensity");
            ReflectionsReady = SkyReady && AmbientLight_ReflectionIntensity?.FieldType == typeof(float);
            Type highlight = Find("AmbientHighlight");
            AmbientHighlight_SetSH = Method(highlight, "SetSH", sh);

            // The serialized field names are Unity's own serialization keys, which is why they
            // survive obfuscation; the element types are private nested types, read off the fields.
            AmbientHighlight_Render = Method(highlight, "ManualOnRenderObject", new[] { typeof(Camera) });
            AmbientHighlight_Settings = Field(highlight, "_highlightSettings");
            Type settings = AmbientHighlight_Settings?.FieldType.GetElementType();
            HighlightSettings_Min = Field(settings, "HighlightMinMultiplier");
            HighlightSettings_Max = Field(settings, "HighlightMaxMultiplier");
            HighlightSettings_Stencil = Field(settings, "StencilTypeToUse");
            AmbientHighlight_ExtraLights = Field(highlight, "_additionalLights");
            ExtraLight_Intensity = Field(AmbientHighlight_ExtraLights?.FieldType.GetElementType(), "Intensity");
            HandGlowReady = SkyReady && All(AmbientHighlight_Render, AmbientHighlight_Settings, HighlightSettings_Min, HighlightSettings_Max)
                && AmbientHighlight_Settings.FieldType.IsArray;
            CloudController_UpdateAmbient = Method(Find("EFT.Rendering.Clouds.CloudController"), "UpdateAmbient", sh);
            AmbientReady = SkyReady && AmbientLight_SetSH != null;
            HighlightReady = SkyReady && AmbientHighlight_SetSH != null;
            CloudsReady = SkyReady && CloudController_UpdateAmbient != null;

            MoonlightReady = SkyReady && All(TOD_Sky_Components, TOD_Components_LightSource, TOD_Sky_LateUpdate)
                && TOD_Components_LightSource.PropertyType == typeof(Light);

            StencilShadow = Find("StencilShadow");
            StencilShadow_Ambient = Field(StencilShadow, "Ambient");
            StencilShadow_Culling = Field(StencilShadow, "Culling");
            AnalyticSource = Find("AnalyticSource");
            AnalyticSource_LightIntensityMultiplicator = Field(AnalyticSource, "LightIntensityMultiplicator");
            AnalyticSource_AddAmbient = Field(AnalyticSource, "AddAmbient");
            AnalyticSource_Culling = Field(AnalyticSource, "Culling");
            AnalyticSource_UpdateSettings = Method(AnalyticSource, "UpdateSettings", Type.EmptyTypes);
            Type culling = StencilShadow_Culling?.FieldType;
            CullingSettings_Distance = Field(culling, "Distance");
            CullingSettings_Update = Method(culling, "Update", Type.EmptyTypes);
            AmbientLight_StencilShadows = Registry(ambientLight, StencilShadow);
            AmbientLight_AnalyticSources = Registry(ambientLight, AnalyticSource);
            InteriorsReady = SkyReady
                && StencilShadow_Ambient?.FieldType == typeof(Color)
                && All(AmbientLight_StencilShadows, AmbientLight_AnalyticSources,
                       AnalyticSource_LightIntensityMultiplicator, AnalyticSource_AddAmbient, AnalyticSource_UpdateSettings);

            PrismEffects = Find("PrismEffects");
            Prism_UseExposure = Field(PrismEffects, "useExposure");
            Prism_UpperLimit = Field(PrismEffects, "exposureUpperLimit");
            Prism_LowerLimit = Field(PrismEffects, "exposureLowerLimit");
            Prism_MiddleGrey = Field(PrismEffects, "exposureMiddleGrey");
            ExposureReady = SkyReady && All(Prism_UseExposure, Prism_UpperLimit, Prism_LowerLimit);

            NightVision = Find("BSG.CameraEffects.NightVision");
            NightVision_On = Prop(NightVision, "On");
            NightVisionReady = NightVision_On != null;

            Type gameWorld = Find("EFT.GameWorld");
            Type singleton = AccessTools.TypeByName("Comfort.Common.Singleton`1");
            if (gameWorld != null && singleton != null)
            {
                GameWorld_Instance = Prop(singleton.MakeGenericType(gameWorld), "Instance");
            }
            GameWorld_LocationId = Prop(gameWorld, "LocationId");
            WorldReady = All(GameWorld_Instance, GameWorld_LocationId);

            Type environment = Find("EFT.EnvironmentEffect.EnvironmentManager");
            EnvironmentManager_Instance = environment?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            EnvironmentManager_Environment = Prop(environment, "Environment");
            EnvironmentReady = All(EnvironmentManager_Instance, EnvironmentManager_Environment);

            if (Missing.Count > 0)
            {
                log.LogWarning("Not found in this game build: " + string.Join(", ", Missing.ToArray()));
            }

            log.LogInfo(
                $"Features: sky {On(SkyReady)}, weather {On(WeatherReady)}, ambient {On(AmbientReady)}, " +
                $"highlight {On(HighlightReady)}, hand glow {On(HandGlowReady)}, reflections {On(ReflectionsReady)}, clouds {On(CloudsReady)},moonlight {On(MoonlightReady)}, " +
                $"interiors {On(InteriorsReady)}, exposure {On(ExposureReady)}, night vision {On(NightVisionReady)}, " +
                $"raid {On(WorldReady)}, indoor/outdoor {On(EnvironmentReady)}");
        }

        // ------------------------------------------------------------- readers

        internal static object Sky() => SkyReady ? TOD_Sky_Instance.GetValue(null, null) : null;

        internal static Light SkyLight(object sky)
        {
            if (!MoonlightReady || sky == null)
            {
                return null;
            }

            object components = TOD_Sky_Components.GetValue(sky, null);
            return components == null ? null : TOD_Components_LightSource.GetValue(components, null) as Light;
        }

        internal static object WeatherCurve()
        {
            if (!WeatherReady)
            {
                return null;
            }

            object controller = WeatherController_Instance.GetValue(null);
            return controller == null ? null : WeatherController_WeatherCurve.GetValue(controller, null);
        }

        internal static string LocationId()
        {
            if (!WorldReady)
            {
                return null;
            }

            object world = GameWorld_Instance.GetValue(null, null);
            return world == null ? null : GameWorld_LocationId.GetValue(world, null) as string;
        }

        internal static IEnumerable Registered(FieldInfo registry) => registry?.GetValue(null) as IEnumerable;

        // ------------------------------------------------------------- lookups

        private static Type Find(string name)
        {
            Type t = AccessTools.TypeByName(name);
            if (t == null)
            {
                Missing.Add(name);
            }
            return t;
        }

        private static PropertyInfo Prop(Type t, string name)
        {
            if (t == null)
            {
                return null;
            }

            for (Type at = t; at != null; at = at.BaseType)
            {
                PropertyInfo p = at.GetProperty(name, Any | BindingFlags.DeclaredOnly);
                if (p != null)
                {
                    return p;
                }
            }

            // Interfaces do not inherit through BaseType.
            foreach (Type i in t.GetInterfaces())
            {
                PropertyInfo p = i.GetProperty(name, Any);
                if (p != null)
                {
                    return p;
                }
            }

            Missing.Add(t.Name + "." + name);
            return null;
        }

        private static FieldInfo Field(Type t, string name)
        {
            if (t == null)
            {
                return null;
            }

            for (Type at = t; at != null; at = at.BaseType)
            {
                FieldInfo f = at.GetField(name, Any | BindingFlags.DeclaredOnly);
                if (f != null)
                {
                    return f;
                }
            }

            Missing.Add(t.Name + "." + name);
            return null;
        }

        private static MethodInfo Method(Type t, string name, Type[] parameters)
        {
            if (t == null)
            {
                return null;
            }

            MethodInfo m = t.GetMethod(name, Any | BindingFlags.DeclaredOnly, null, parameters, null);
            if (m == null)
            {
                Missing.Add(t.Name + "." + name);
            }
            return m;
        }

        /// <summary>
        /// A static HashSet&lt;element&gt; on owner. AmbientLight keeps a HashSet and a SortedSet
        /// of each volume type under obfuscated names; the HashSet is the one every volume is
        /// added to, and its element type is the only stable way to tell which is which.
        /// </summary>
        private static FieldInfo Registry(Type owner, Type element)
        {
            if (owner == null || element == null)
            {
                return null;
            }

            Type wanted = typeof(HashSet<>).MakeGenericType(element);
            foreach (FieldInfo f in owner.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static))
            {
                if (f.FieldType == wanted)
                {
                    return f;
                }
            }

            Missing.Add(owner.Name + " registry of " + element.Name);
            return null;
        }

        private static bool All(params object[] members)
        {
            foreach (object m in members)
            {
                if (m == null)
                {
                    return false;
                }
            }
            return true;
        }

        private static string On(bool ready) => ready ? "on" : "OFF";
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace DarkNights.Client
{
    /// <summary>
    /// Sky reflections, darkened with the sky.
    ///
    /// AmbientLight renders its own cubemap and every shiny surface -- weapon metal, glass,
    /// wet ground -- reflects it at ReflectionIntensity (times rain wetting), unless
    /// screen-space reflections are on, in which case it uses 1 and reflects the already
    /// darkened screen. Darkening the ambient alone would leave that cubemap reflection at
    /// vanilla strength, so steel and glass would shine against a dark world. This scales
    /// ReflectionIntensity by the same factor as the sky ambient.
    ///
    /// The field is only set by the game through SetReflectionIntensity, so it goes through
    /// Tracked: a value the game sets is adopted as the new base.
    /// </summary>
    internal static class Reflections
    {
        private static readonly Dictionary<Object, Tracked> Intensities = new Dictionary<Object, Tracked>();

        // AmbientLight is a scene object that draws into the cameras, not a camera component,
        // so it is collected from the SetSH prefix, which every live one calls each frame.
        private static readonly HashSet<Object> Lights = new HashSet<Object>();
        private static readonly System.Predicate<Object> Destroyed = light => light == null;
        private static int _generation = -1;

        internal static void Seen(Object light)
        {
            if (light != null)
            {
                Lights.Add(light);
            }
        }

        internal static float LastBase = float.NaN;
        internal static float LastWritten = float.NaN;

        internal static void Tick()
        {
            if (!GameTypes.ReflectionsReady)
            {
                return;
            }

            if (NightDriver.WorldGeneration != _generation)
            {
                _generation = NightDriver.WorldGeneration;
                Intensities.Clear();
            }

            Lights.RemoveWhere(Destroyed);
            float factor = DarkNightsPlugin.DarkenReflections.Value ? NightDriver.Get().Ambient : 1f;

            foreach (Object light in Lights)
            {

                if (!Intensities.TryGetValue(light, out Tracked tracked))
                {
                    if (factor >= 1f)
                    {
                        continue;
                    }

                    tracked = new Tracked();
                    Intensities.Add(light, tracked);
                }

                var current = (float)GameTypes.AmbientLight_ReflectionIntensity.GetValue(light);
                float next = tracked.Apply(current, factor);
                if (next != current)
                {
                    GameTypes.AmbientLight_ReflectionIntensity.SetValue(light, next);
                }

                LastBase = tracked.Base;
                LastWritten = next;
            }
        }
    }
}

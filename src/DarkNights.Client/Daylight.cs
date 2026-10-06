using System;
using UnityEngine;

namespace DarkNights.Client
{
    /// <summary>
    /// How much daylight reaches a point: the share of the sky it can see.
    ///
    /// EFT has no notion of whether a room has windows. Its interior volumes carry a fixed
    /// ambient and the sky ambient reaches everything outside them, so a windowless room on a
    /// second floor is lit at noon like a porch. This measures it instead: rays from the point
    /// over the upper half of the sphere, stopped by walls, terrain and closed doors
    /// (HighPolyCollider, Terrain, DoorLowPolyCollider) but not by glass (TransparentCollider),
    /// and the share that reaches open sky is how open the point is. A windowless room
    /// scores 0, a room with a window a little, the open street nearly everything.
    ///
    /// For your view the rays are spread over frames, a few per frame. Bots judging a target
    /// use a coarser probe, cached per target.
    /// </summary>
    internal static class Daylight
    {
        private const int DirectionCount = 96;
        private const int RaysPerFrame = 12;
        private const float RayLength = 150f;
        private const float FadeSeconds = 0.75f;

        private static readonly Vector3[] Directions = MakeDirections();
        private static readonly bool[] Open = new bool[DirectionCount];
        private static int _next;
        private static int _openCount = DirectionCount;
        private static int _mask;
        private static int _generation = -1;

        /// <summary>Share of probe rays from your view that reach open sky, 0 to 1.</summary>
        internal static float Openness = 1f;

        /// <summary>Daylight reaching your view, 0 (sealed) to 1 (open air), faded.</summary>
        internal static float Here = 1f;

        internal static void Tick()
        {
            if (NightDriver.WorldGeneration != _generation)
            {
                _generation = NightDriver.WorldGeneration;
                for (int i = 0; i < Open.Length; i++)
                {
                    Open[i] = true;
                }

                _openCount = DirectionCount;
                Openness = 1f;
                Here = 1f;
            }

            // Off, suspended by the A/B key, or on an excluded map: no rays.
            if (!DarkNightsPlugin.DarkWithoutDaylight.Value || NightDriver.Location == null || !string.IsNullOrEmpty(NightDriver.Idle))
            {
                Here = 1f;
                return;
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            long started = Perf.Start();
            Vector3 origin = camera.transform.position;
            for (int n = 0; n < RaysPerFrame; n++)
            {
                bool open = Escapes(origin, Directions[_next]);
                if (open != Open[_next])
                {
                    _openCount += open ? 1 : -1;
                    Open[_next] = open;
                }

                _next = (_next + 1) % DirectionCount;
            }

            Perf.Stop(Perf.Part.Daylight, started);

            Openness = _openCount / (float)DirectionCount;
            Here = Mathf.MoveTowards(Here, NightModel.DaylightFromOpenness(Openness), Time.unscaledDeltaTime / FadeSeconds);
        }

        /// <summary>A coarser, one-off probe of a point, for bots judging a target. Every 4th direction.</summary>
        internal static float OpennessAt(Vector3 point)
        {
            int open = 0;
            int total = 0;
            for (int i = 0; i < DirectionCount; i += 4)
            {
                total++;
                if (Escapes(point, Directions[i]))
                {
                    open++;
                }
            }

            return open / (float)total;
        }

        private static bool Escapes(Vector3 origin, Vector3 direction)
        {
            if (_mask == 0)
            {
                _mask = LayerMask.GetMask("HighPolyCollider", "Terrain", "DoorLowPolyCollider");
            }

            return !Physics.Raycast(origin, direction, RayLength, _mask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// Even directions over the upper half of the sphere and a little below the horizon,
        /// on a Fibonacci spiral. Straight down is left out: the ground always blocks it, and
        /// light from below the horizon is mostly what came in through the same openings.
        /// </summary>
        private static Vector3[] MakeDirections()
        {
            var result = new Vector3[DirectionCount];
            const float lowest = -0.15f;
            float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (int i = 0; i < DirectionCount; i++)
            {
                float y = 1f - (i + 0.5f) / DirectionCount * (1f - lowest);
                float radius = Mathf.Sqrt(Math.Max(0f, 1f - y * y));
                float theta = golden * i;
                result[i] = new Vector3(Mathf.Cos(theta) * radius, y, Mathf.Sin(theta) * radius);
            }

            return result;
        }
    }
}

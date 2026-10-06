using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkNights.Client
{
    /// <summary>
    /// Finds a component type on the enabled cameras.
    ///
    /// PrismEffects and NightVision are image effects, so they sit on cameras. 0.1.0 found
    /// them (and AmbientLight, now collected in Reflections) with FindObjectsOfType every two
    /// seconds, which walks every object in the scene: on Shoreline that cost about 110 ms
    /// each, three of them, and froze the game for a third of a second every two seconds.
    /// Asking the few live cameras costs microseconds and still follows a camera swap.
    /// </summary>
    internal static class CameraComponents
    {
        private static Camera[] _cameras = new Camera[8];

        internal static UnityEngine.Object[] Find(Type type)
        {
            int count = Camera.allCamerasCount;
            if (_cameras.Length < count)
            {
                _cameras = new Camera[count * 2];
            }

            count = Camera.GetAllCameras(_cameras);
            var found = new List<UnityEngine.Object>();
            for (int i = 0; i < count; i++)
            {
                Component component = _cameras[i].GetComponent(type);
                if (component != null)
                {
                    found.Add(component);
                }

                _cameras[i] = null;
            }

            return found.ToArray();
        }
    }
}

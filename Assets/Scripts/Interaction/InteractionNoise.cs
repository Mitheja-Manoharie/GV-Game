using System;
using UnityEngine;

namespace GVGame.Interaction
{
    /// <summary>
    /// Static noise bus. Physics props (impacts) and doors raise noises here;
    /// the AI / Intelligent Systems code can subscribe to make enemies investigate.
    /// Nothing in this module depends on a subscriber existing.
    /// </summary>
    public static class InteractionNoise
    {
        /// <summary>World position, loudness (roughly metres of hearing radius), source object.</summary>
        public static event Action<Vector3, float, GameObject> NoiseEmitted;

        public static void Emit(Vector3 position, float loudness, GameObject source)
        {
            NoiseEmitted?.Invoke(position, loudness, source);
        }
    }
}

using ResortEmpire.Gameplay.Progression;
using UnityEngine;

namespace ResortEmpire.Gameplay.Integration
{
    [CreateAssetMenu(menuName = "Resort Empire/Economy and Waves Configuration")]
    public sealed class ResortEconomyConfig : ScriptableObject
    {
        [Tooltip("Demo tuning only; validate balance before a release.")]
        [SerializeField] private ProgressionConfig tuning;
        public ProgressionConfig CreateRuntimeConfig() => tuning == null
            ? throw new System.InvalidOperationException("No economy tuning is assigned.") : tuning.Copy();
    }
}

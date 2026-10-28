using System.IO;
using ResortEmpire.Gameplay.Progression;
using UnityEngine;

namespace ResortEmpire.Gameplay.Integration
{
    [DisallowMultipleComponent]
    public sealed class ResortGameplayDependencySource : MonoBehaviour, IResortProgressionDependencySource
    {
        [SerializeField] private ResortEconomyConfig configuration;
        public ProgressionConfig ProgressionConfig => configuration == null
            ? throw new System.InvalidOperationException("Assign the economy configuration asset.") : configuration.CreateRuntimeConfig();
        public IProgressionPersistence CreatePersistence() =>
            new JsonFileProgressionPersistence(Path.Combine(Application.persistentDataPath, "resort-progress-v1.json"));
    }
}

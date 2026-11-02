using System;
using UnityEngine;

namespace ResortEmpire.Gameplay.Integration
{
    /// <summary>
    /// Presentation-only signal raised when the guest boat reaches the dock.
    /// It carries no gameplay state and does not participate in wave simulation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoatArrivalSignal : MonoBehaviour
    {
        public event Action Arrived;

        public bool HasArrived { get; private set; }

        public void BeginArrival()
        {
            HasArrived = false;
        }

        public void NotifyArrived()
        {
            HasArrived = true;
            Arrived?.Invoke();
        }

        public void ResetArrival()
        {
            HasArrived = false;
        }
    }
}

namespace ResortEmpire.Gameplay.Integration
{
    /// <summary>Presentation data without exposing the simulation assembly to uGUI.</summary>
    public readonly struct SuddenRushNotice
    {
        public SuddenRushNotice(string eventId, BuildingId buildingId, int demandUnits, double duration)
        {
            EventId = eventId;
            BuildingId = buildingId;
            DemandUnits = demandUnits;
            Duration = duration;
        }

        public string EventId { get; }
        public BuildingId BuildingId { get; }
        public int DemandUnits { get; }
        public double Duration { get; }
        public bool IsVisible => !string.IsNullOrEmpty(EventId);
    }

    /// <summary>
    /// Adapter boundary for SCRUM-17. The boost implementation owns all costs, restrictions,
    /// atomic spending and service effects; the rush UI must never implement those rules.
    /// </summary>
    public interface IEnergyBoostService
    {
        ResortGameplayCommandResult GetAvailability(BuildingId buildingId);
        ResortGameplayCommandResult TryUseBoost(BuildingId buildingId);
    }

    public interface ISuddenRushUiFacade
    {
        SuddenRushNotice CurrentSuddenRush { get; }
        ResortGameplayCommandResult TryDismissSuddenRush(string eventId);
        ResortGameplayCommandResult GetEnergyBoostAvailability(BuildingId buildingId);
        ResortGameplayCommandResult TryUseEnergyBoost(BuildingId buildingId);
    }
}

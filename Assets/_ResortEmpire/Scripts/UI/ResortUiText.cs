using System.Globalization;
using ResortEmpire.Gameplay;

namespace ResortEmpire.UI
{
    /// <summary>Player-facing names for gameplay identifiers, shared by the S1–S9 presenters.</summary>
    public static class ResortUiText
    {
        public static string BuildingName(BuildingId building)
        {
            switch (building)
            {
                case BuildingId.Reception: return "Reception";
                case BuildingId.Restaurant: return "Restaurant";
                case BuildingId.BeachBar: return "Beach Bar";
                case BuildingId.Pool: return "Pool";
                case BuildingId.Bungalows: return "Bungalows";
                default: return building.ToString();
            }
        }

        public static string NeedName(NeedType need)
        {
            switch (need)
            {
                case NeedType.Rooms: return "Rooms";
                case NeedType.Food: return "Food";
                case NeedType.Drinks: return "Drinks";
                case NeedType.Relax: return "Relax";
                case NeedType.OceanView: return "Ocean View";
                default: return need.ToString();
            }
        }

        public static string Number(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
    }
}

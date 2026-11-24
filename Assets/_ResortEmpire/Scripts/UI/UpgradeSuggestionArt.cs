using ResortEmpire.Gameplay;
using UnityEngine;

namespace ResortEmpire.UI
{
    /// <summary>Prefab-owned artwork shared by static previews and the live suggestion presenter.</summary>
    public sealed class UpgradeSuggestionArt : MonoBehaviour
    {
        [Tooltip("Rooms, Drinks, Food, Relax, Ocean View, in NeedType order.")]
        public Sprite[] ServiceIcons;

        public Sprite IconFor(NeedType need)
        {
            int index = (int)need;
            return ServiceIcons != null && index >= 0 && index < ServiceIcons.Length ? ServiceIcons[index] : null;
        }
    }
}

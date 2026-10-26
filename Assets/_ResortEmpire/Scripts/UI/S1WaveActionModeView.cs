using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ResortEmpire.UI
{
    /// <summary>
    /// Presentation-only switch for S1's normal wave action and its VIP alternative.
    /// Gameplay can select the visible action through <see cref="SetVipMode"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class S1WaveActionModeView : MonoBehaviour
    {
        [SerializeField] private Button startWaveButton;
        [SerializeField] private Button vipStartWaveButton;
        [SerializeField] private TextMeshProUGUI vipWavesText;
        [SerializeField] private string normalVipText = "VIP: 3 WAVES";
        [SerializeField] private string vipModeText = "VIP: 0 WAVES";
        [SerializeField] private bool vipMode;

        private void OnEnable()
        {
            Apply();
        }

        public void SetVipMode(bool enabled)
        {
            vipMode = enabled;
            Apply();
        }

        private void OnValidate()
        {
            Apply();
        }

        private void Apply()
        {
            if (startWaveButton != null)
            {
                startWaveButton.gameObject.SetActive(!vipMode);
            }

            if (vipStartWaveButton != null)
            {
                vipStartWaveButton.gameObject.SetActive(vipMode);
            }

            if (vipWavesText != null)
            {
                vipWavesText.text = vipMode ? vipModeText : normalVipText;
            }
        }
    }
}

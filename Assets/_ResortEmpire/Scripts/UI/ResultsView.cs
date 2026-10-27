using TMPro;
using UnityEngine;
using UnityEngine.Events;
namespace ResortEmpire.UI {
    public sealed class ResultsView : ResortWindowView {
        public TextMeshProUGUI Heading;
        public TextMeshProUGUI Served;
        public TextMeshProUGUI Satisfaction;
        public TextMeshProUGUI Feedback;
        public StarRatingView Stars;
        public GameObject PerfectStayBadge;
        public UnityEngine.UI.Button ClaimButton;
        public RewardItemView[] Rewards;
        public UnityEvent ClaimRequested = new UnityEvent();
        bool claimPending;
        public void SetResults(string served, string satisfaction, string feedback, int stars, bool perfectStay) {
            Served.text=served; Satisfaction.text=satisfaction; Feedback.text=feedback;
            Stars.SetRating(stars); PerfectStayBadge.SetActive(perfectStay); ResetClaim();
        }
        public void RequestClaim() { if(claimPending)return; claimPending=true; ClaimButton.interactable=false; ClaimRequested.Invoke(); }
        public void ResetClaim() { claimPending=false; ClaimButton.interactable=true; }
    }
}

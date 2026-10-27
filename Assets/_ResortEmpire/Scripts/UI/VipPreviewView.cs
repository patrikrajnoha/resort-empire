using TMPro;
using UnityEngine;
using UnityEngine.Events;
namespace ResortEmpire.UI {
    public sealed class VipPreviewView : ResortWindowView {
        public TextMeshProUGUI GuestName;
        public TextMeshProUGUI Description;
        public TextMeshProUGUI Promise;
        public TextMeshProUGUI Arrival;
        public UnityEngine.UI.Image Progress;
        public UnityEngine.UI.Image Portrait;
        public GameObject MissingPortrait;
        public StarRatingView Difficulty;
        public RewardItemView[] Rewards;
        public UnityEvent OpenRequestsRequested = new UnityEvent();
        public void SetGuest(string guestName,string description,string promise,Sprite portrait,int difficulty) {
            GuestName.text=guestName; Description.text=description; Promise.text=promise;
            Portrait.sprite=portrait; Portrait.enabled=portrait; MissingPortrait.SetActive(!portrait); Difficulty.SetRating(difficulty);
        }
        public void SetArrival(float secondsRemaining,float totalSeconds) {
            secondsRemaining=Mathf.Max(0,secondsRemaining);
            Arrival.text="ARRIVING IN "+Mathf.FloorToInt(secondsRemaining/60).ToString("00")+":"+Mathf.FloorToInt(secondsRemaining%60).ToString("00");
            Progress.fillAmount=totalSeconds>0?Mathf.Clamp01(1-secondsRemaining/totalSeconds):1;
        }
        public void OpenRequests() { OpenRequestsRequested.Invoke(); }
    }
}

using UnityEngine;
using UnityEngine.Events;
namespace ResortEmpire.UI {
    public class ResortWindowView : MonoBehaviour {
        public UnityEvent Closed = new UnityEvent();
        public void Open() { gameObject.SetActive(true); }
        public void Close() { gameObject.SetActive(false); Closed.Invoke(); }
    }
}

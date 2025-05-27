using UnityEngine;
using UnityEngine.EventSystems;

public class MarkerBehaviour : MonoBehaviour, IPointerClickHandler
{
    [HideInInspector] public int holeId;
    [HideInInspector] public System.Action<int, GameObject> onClickMarker;

    public void OnPointerClick(PointerEventData eventData)
    {
        // 點擊自己時呼叫管理者回調
        onClickMarker?.Invoke(holeId, gameObject);
    }
}

using UnityEngine;
using UnityEngine.EventSystems;

public class MarkerBehaviour : MonoBehaviour, IPointerClickHandler
{
    [HideInInspector] public int holeId;
    [HideInInspector] public System.Action<int, GameObject> onClickMarker;

    public void OnPointerClick(PointerEventData eventData)
    {
        // �I���ۤv�ɩI�s�޲z�̦^��
        onClickMarker?.Invoke(holeId, gameObject);
    }
}

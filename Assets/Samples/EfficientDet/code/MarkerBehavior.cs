using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 控制單一標示框行為的腳本
/// </summary>
public class MarkerBehaviour : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler
{
    [Header("UI References")]
    public Button actionButton; // 請在Prefab中將右上角的按鈕拖到這裡
    public TMP_Text buttonText; // 請在Prefab中將按鈕上的文字元件拖到這裡

    private HoleAnnotationTool mainTool;
    private RectTransform rectTransform;
    private bool isDraggable = false;
    private Vector2 dragOffset;

    public void Initialize(HoleAnnotationTool tool, RectTransform rt)
    {
        mainTool = tool;
        rectTransform = rt;
    }

    public void SetDraggable(bool draggable)
    {
        isDraggable = draggable;
    }

    public void SetActionButtonActive(bool isActive)
    {
        if (actionButton != null)
        {
            actionButton.gameObject.SetActive(isActive);
        }
    }

    public void SetActionButtonText(string text)
    {
        if (buttonText != null)
        {
            buttonText.text = text;
        }
    }

    public Button GetActionButton()
    {
        return actionButton;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // 將點擊事件傳遞給主工具
        mainTool.OnMarkerClicked(this);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!isDraggable) return;

        // 計算滑鼠點擊位置與物件中心點的偏移
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            transform.parent.GetComponent<RectTransform>(),
            eventData.position,
            eventData.pressEventCamera,
            out Vector2 localPointerPosition);

        dragOffset = rectTransform.anchoredPosition - localPointerPosition;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isDraggable) return;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
            transform.parent.GetComponent<RectTransform>(),
            eventData.position,
            eventData.pressEventCamera,
            out Vector2 localPointerPosition))
        {
            // 更新物件位置
            rectTransform.anchoredPosition = localPointerPosition + dragOffset;
        }
    }
}
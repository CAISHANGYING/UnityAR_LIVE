using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// �����@�Хܮئ欰���}��
/// </summary>
public class MarkerBehaviour : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler
{
    [Header("UI References")]
    public Button actionButton; // �ЦbPrefab���N�k�W�������s���o��
    public TMP_Text buttonText; // �ЦbPrefab���N���s�W����r������o��

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
        // �N�I���ƥ�ǻ����D�u��
        mainTool.OnMarkerClicked(this);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!isDraggable) return;

        // �p��ƹ��I����m�P���󤤤��I������
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
            // ��s�����m
            rectTransform.anchoredPosition = localPointerPosition + dragOffset;
        }
    }
}
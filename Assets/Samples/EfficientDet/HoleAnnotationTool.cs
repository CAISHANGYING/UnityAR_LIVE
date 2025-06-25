using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine.EventSystems;

[System.Serializable]
public class HoleAnnotation
{
    public int id;
    public float x;   // �۹��Ϥ��e�ת���� 0~1
    public float y;   // �۹��Ϥ����ת���� 0~1
}

[System.Serializable]
public class HoleAnnotationList
{
    public List<HoleAnnotation> annotations = new List<HoleAnnotation>();
}

public class HoleAnnotationTool : MonoBehaviour
{
    [Header("UI Components")]
    public RawImage imageDisplay;
    // AspectRatioFitter �ڭ̤��A�ݭn�A���O�d�ܼƥH���z���ӷQ��
    public AspectRatioFitter aspectFitter;
    public TMP_InputField inputField;
    public Text noticeText;
    public Button confirmButton;

    [Header("Marker Prefab")]
    public GameObject markerPrefab;

    // �����ϥ�
    private RectTransform markerParent;
    private bool imageConfirmed = false;
    private Texture2D currentTexture;
    private Vector2 dragStartLocal;
    private GameObject currentMarker;
    private List<HoleAnnotation> annotations = new List<HoleAnnotation>();

    void Start()
    {
        // 1. �򥻳]�w
        imageDisplay.raycastTarget = true;
        // �i���n�j�T�O AspectRatioFitter �w�b�s�边���Q���Ωβ���
        if (aspectFitter != null)
        {
            aspectFitter.enabled = false;
        }

        // 2. �w�q RawImage ���u�̤j��ܽd��v������ 2/3
        var imgRT = imageDisplay.rectTransform;
        imgRT.anchorMin = new Vector2(0, 0);
        imgRT.anchorMax = new Vector2(2f / 3f, 1);
        // �M�Ű����A�������j�p���������I�M�w
        imgRT.offsetMin = Vector2.zero;
        imgRT.offsetMax = Vector2.zero;
        // �N�b�߳]�b���U���A��K�ڭ̭p���m
        imgRT.pivot = new Vector2(0, 0);
        imgRT.anchoredPosition = Vector2.zero;

        // 3. �]�w�аO�I��������
        markerParent = imageDisplay.rectTransform;

        // 4. �� RawImage ���u�����ܽd��v�[�W�I����ϥ\��
        var trigger = imageDisplay.gameObject.AddComponent<EventTrigger>();
        var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
        entry.callback.AddListener((_) => {
            if (!imageConfirmed) PickImage();
        });
        trigger.triggers.Add(entry);

        // 5. ��L UI ��l��
        if (confirmButton != null)
        {
            confirmButton.onClick.AddListener(ConfirmImage);
            confirmButton.gameObject.SetActive(false);
        }
        if (noticeText != null)
        {
            noticeText.text = "�Х���ܹϤ�";
        }
    }

    public void PickImage()
    {
        if (imageConfirmed)
        {
            noticeText.text = "�Ϥ��w�T�{";
            return;
        }

        NativeGallery.GetImageFromGallery((path) =>
        {
            if (string.IsNullOrEmpty(path)) return;

            var tex = NativeGallery.LoadImageAtPath(path, 2048);
            if (tex == null)
            {
                noticeText.text = "�Ϥ����J����";
                return;
            }

            tex = MakeReadable(tex);
            if (tex.height > tex.width)
                tex = RotateTexture(tex, true);

            currentTexture = tex;
            imageDisplay.texture = tex;

            // ---�i�֤��޿�G��ʭp��Ϥ��j�p�P��m�j---

            // 1. ���o RawImage �̤j��ܽd�򪺼e��
            float containerWidth = imageDisplay.rectTransform.rect.width;
            float containerHeight = imageDisplay.rectTransform.rect.height;

            // 2. �p������u�Ϥ������v������ܦb�e�������̤j�Y����
            float widthScale = containerWidth / tex.width;
            float heightScale = containerHeight / tex.height;
            float scale = Mathf.Min(widthScale, heightScale);

            // 3. �p��Ϥ��Y��᪺��ڤؤo
            float newWidth = tex.width * scale;
            float newHeight = tex.height * scale;

            // 4. �i����j�ڭ̭n�ק諸�O RawImage �� UV Rect�A�Ӥ��O���� RectTransform
            // �o��b�����ܡu�خءv�j�p���e���U�A���ܡu�Ϥ����e�v����ܤ覡
            float uv_x = (1f - newWidth / containerWidth) / 2f;
            float uv_y = (1f - newHeight / containerHeight) / 2f;
            float uv_w = newWidth / containerWidth;
            float uv_h = newHeight / containerHeight;
            imageDisplay.uvRect = new Rect(uv_x, uv_y, uv_w, uv_h);


            noticeText.text = $"�Ϥ��w���J ({tex.width}x{tex.height})";
            if (confirmButton != null)
            {
                confirmButton.gameObject.SetActive(true);
            }
        }, "��ܤ@�i�Ϥ�");
    }

    public void ConfirmImage()
    {
        if (currentTexture == null)
        {
            noticeText.text = "�Х���ܹϤ�";
            return;
        }
        imageConfirmed = true;
        confirmButton.gameObject.SetActive(false);
        noticeText.text = "�Ϥ��w�T�{�A�}�l�ؿ�լ}";
    }

    void Update()
    {
        if (!imageConfirmed) return;

        Camera cam = imageDisplay.canvas.renderMode == RenderMode.ScreenSpaceCamera
            ? imageDisplay.canvas.worldCamera
            : null;

#if UNITY_ANDROID || UNITY_IOS
        if (Input.touchCount > 0)
        {
            var t = Input.GetTouch(0);
            HandleTouch(t.phase, t.position, cam);
        }
#else
        if (Input.GetMouseButtonDown(0)) HandleTouch(TouchPhase.Began, Input.mousePosition, cam);
        if (Input.GetMouseButton(0)) HandleTouch(TouchPhase.Moved, Input.mousePosition, cam);
        if (Input.GetMouseButtonUp(0)) HandleTouch(TouchPhase.Ended, Input.mousePosition, cam);
#endif
    }

    void HandleTouch(TouchPhase phase, Vector2 screenPos, Camera cam)
    {
        // ���o�۹�� markerParent (��ӥ���2/3�϶�) �����a�y��
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                markerParent, screenPos, cam, out Vector2 local))
            return;

        // �ھ� UV Rect �p��X��ڹϤ����e����ܽd��
        Rect imgContainerRect = markerParent.rect;
        Rect uv = imageDisplay.uvRect;
        float actualImgX = imgContainerRect.x + imgContainerRect.width * uv.x;
        float actualImgY = imgContainerRect.y + imgContainerRect.height * uv.y;
        float actualImgW = imgContainerRect.width * uv.width;
        float actualImgH = imgContainerRect.height * uv.height;
        Rect actualImageRect = new Rect(actualImgX, actualImgY, actualImgW, actualImgH);

        // �i����ץ��j�ڭ̲����F���e�|�צ�ø�Ϫ� if �P�_
        // �{�b�A�Y�ϱq�¦�ϰ�}�l�즲�A�خؤ]�|�q�Ϥ���t�}�l�e

        if (phase == TouchPhase.Began)
        {
            // �N�_�l�I�y�бj���b�Ϥ�����ڽd��
            Vector2 clampedStartPos = local;
            clampedStartPos.x = Mathf.Clamp(clampedStartPos.x, actualImageRect.x, actualImageRect.xMax);
            clampedStartPos.y = Mathf.Clamp(clampedStartPos.y, actualImageRect.y, actualImageRect.yMax);

            dragStartLocal = clampedStartPos; // �x�s�Q����L���_�l�I

            currentMarker = Instantiate(markerPrefab, markerParent, false);
            var rt = currentMarker.GetComponent<RectTransform>();
            rt.pivot = Vector2.zero;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.anchoredPosition = clampedStartPos; // �аO�ت���l��m�]�έ���L���y��
            rt.sizeDelta = Vector2.zero;
        }
        else if (phase == TouchPhase.Moved && currentMarker != null)
        {
            var rt = currentMarker.GetComponent<RectTransform>();

            // �N��e�ƹ��y�Ф]����b�Ϥ�����ڽd��
            Vector2 clampedCurrentPos = local;
            clampedCurrentPos.x = Mathf.Clamp(clampedCurrentPos.x, actualImageRect.x, actualImageRect.xMax);
            clampedCurrentPos.y = Mathf.Clamp(clampedCurrentPos.y, actualImageRect.y, actualImageRect.yMax);

            // �ϥγQ����L���_�l�I�M��e�I�ӭp��خت��|�Ө���
            float x0 = dragStartLocal.x;
            float y0 = dragStartLocal.y;
            float x1 = clampedCurrentPos.x;
            float y1 = clampedCurrentPos.y;

            float minX = Mathf.Min(x0, x1);
            float minY = Mathf.Min(y0, y1);
            float w = Mathf.Abs(x1 - x0);
            float h = Mathf.Abs(y1 - y0);

            rt.anchoredPosition = new Vector2(minX, minY);
            rt.sizeDelta = new Vector2(w, h);
        }
        else if (phase == TouchPhase.Ended && currentMarker != null)
        {
            var rt = currentMarker.GetComponent<RectTransform>();

            // �p�G�خؤӤp�A�����~Ĳ�A�����R��
            if (rt.sizeDelta.x < 5f || rt.sizeDelta.y < 5f)
            {
                Destroy(currentMarker);
                currentMarker = null;
                return;
            }

            // �p�⥿�W�Ʈy�ЮɡA�n�H actualImageRect �����
            float nx = (rt.anchoredPosition.x - actualImageRect.x) / actualImageRect.width;
            float ny = (rt.anchoredPosition.y - actualImageRect.y) / actualImageRect.height;
            int id = annotations.Count + 1;
            annotations.Add(new HoleAnnotation { id = id, x = nx, y = ny });

            // �i����ץ��j���{���X�ܱo���o���A��P�ɳB�z�s�¨�ؤ�r����
            // �u���M�� TMP_Text
            var tmpText = currentMarker.GetComponentInChildren<TMP_Text>();
            if (tmpText != null)
            {
                tmpText.text = id.ToString();
            }
            else
            {
                // �p�G�䤣��A�A�h���ª� Text
                var legacyText = currentMarker.GetComponentInChildren<Text>();
                if (legacyText != null)
                {
                    legacyText.text = id.ToString();
                }
            }

            noticeText.text = $"�s�W�լ} #{id}";
            currentMarker = null;
        }
    }

    public void SaveJson()
    {
        var wrapper = new HoleAnnotationList { annotations = annotations };
        File.WriteAllText(
            Application.persistentDataPath + "/hole_sequence.json",
            JsonUtility.ToJson(wrapper, true)
        );
        noticeText.text = $"JSON �w�x�s�� {Application.persistentDataPath}";
    }

    Texture2D MakeReadable(Texture2D src)
    {
        var rt = RenderTexture.GetTemporary(src.width, src.height);
        Graphics.Blit(src, rt);
        RenderTexture.active = rt;
        var r = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
        r.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        r.Apply();
        RenderTexture.ReleaseTemporary(rt);
        return r;
    }

    Texture2D RotateTexture(Texture2D src, bool cw)
    {
        int w = src.width, h = src.height;
        var r = new Texture2D(h, w);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                r.SetPixel(cw ? h - y - 1 : x,
                           cw ? x : w - x - 1,
                           src.GetPixel(x, y));
        r.Apply();
        return r;
    }
}

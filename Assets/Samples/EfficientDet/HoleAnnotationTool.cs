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
    public AspectRatioFitter aspectFitter; // �o�� AspectRatioFitter ���ӬO���b imageDisplay ����W
    public TMP_InputField inputField;
    public Text noticeText;
    public Button confirmButton;

    [Header("Marker Prefab")]
    public GameObject markerPrefab;

    // �����ϥ�
    private RectTransform markerParent; // �{�b�A�o�N�O�ڭ̪��e��
    private bool imageConfirmed = false;
    private Texture2D currentTexture;
    private Vector2 dragStartLocal;
    private GameObject currentMarker;
    private List<HoleAnnotation> annotations = new List<HoleAnnotation>();

    void Start()
    {
        // 1. �򥻳]�w
        imageDisplay.raycastTarget = true;

        // �i���n�j�T�O AspectRatioFitter �O�ҥΪ��A�åB�Ҧ����T
        // �o�ӨB�J�{�b������ Unity �s�边����A�{���X���ݭn�z�A
        if (aspectFitter != null)
        {
            aspectFitter.enabled = true;
            aspectFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        }

        // �i���n�j�o�̤��A�ݭn�{���X�ӳ]�w RawImage ���j�p
        // �]�������j�p�{�b�ѥ���������(ImageContainer)�MAspectRatioFitter�ӨM�w

        // 3. �]�w�аO�I�������� (�e��)
        // �i���n�j�ڭ̪��e���A�N�O imageDisplay �����C�]�������j�p�|�۰��Y��A�ҥH���N�O�̧������e���C
        markerParent = imageDisplay.rectTransform;

        // 4. �I���Ϥ��϶��ӿ�Ϫ��\�� (�i�̻ݨD�O�d�β���)
        // �`�N�G�{�b�I�����d��|�O�Y�p�᪺�Ϥ��A�Ӥ��O��ӥ���϶�
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
            noticeText.text = "�Ϥ��w�T�{�A�L�k���s���";
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

            // ---�i�֤߭ק�j---
            // �ڭ̤��A�ݭn�����ʭp��j�p�Φ�m���{���X�C
            // �Ҧ����u�@���浹�ڭ̦b�s�边�]�w�n�� ImageContainer �M AspectRatioFitter�C
            // �o���{���X�ܱo�D�`���b�I

            noticeText.text = $"�Ϥ��w���J ({tex.width}x{tex.height})�A�Ы��T�{";
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
        // �ڭ̪��e��(markerParent)�{�b�N�O imageDisplay�A�����j�p�O�����K�X�Ϥ����C
        // �ҥH�A�ڭ̪����b�o�ӧ������e���W�i��y���ഫ�C
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                markerParent, screenPos, cam, out Vector2 local))
            return;

        // �i���n�j�p�G�I������m���b�e�����A�N���������A�קK�b�e���~���ͼаO
        if (!markerParent.rect.Contains(local))
        {
            // �p�G�O�b�즲����������ƹ��A���ƹ��w�g�b�e���~�A�ڭ��٬O�n�B�z�o�ӼаO
            if (phase == TouchPhase.Ended && currentMarker != null)
            {
                // �o�̪��޿�M�U���� TouchPhase.Ended �@��
                FinalizeMarker(local, true); //�ǤJ true �N��o�O�b�e���~������
            }
            return;
        }

        if (phase == TouchPhase.Began)
        {
            dragStartLocal = local; // �����ϥΥ��a�y��
            currentMarker = Instantiate(markerPrefab, markerParent, false);
            var rt = currentMarker.GetComponent<RectTransform>();
            rt.pivot = Vector2.zero;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.anchoredPosition = local;
            rt.sizeDelta = Vector2.zero;
        }
        else if (phase == TouchPhase.Moved && currentMarker != null)
        {
            var rt = currentMarker.GetComponent<RectTransform>();

            float x0 = dragStartLocal.x;
            float y0 = dragStartLocal.y;
            float x1 = local.x;
            float y1 = local.y;

            float minX = Mathf.Min(x0, x1);
            float minY = Mathf.Min(y0, y1);
            float w = Mathf.Abs(x1 - x0);
            float h = Mathf.Abs(y1 - y0);

            rt.anchoredPosition = new Vector2(minX, minY);
            rt.sizeDelta = new Vector2(w, h);
        }
        else if (phase == TouchPhase.Ended && currentMarker != null)
        {
            FinalizeMarker(local, false); // �ǤJ false �N��o�O�b�e����������
        }
    }

    // �s�W�@�Ө禡�ӳB�z�аO�ت��̲ץͦ��A�קK���ƪ��{���X
    void FinalizeMarker(Vector2 localPos, bool outside)
    {
        var rt = currentMarker.GetComponent<RectTransform>();

        // �p�G�O�b�e���~�����A�ڭ̻ݭn���s�p��خؤj�p�A�T�O�����|�W�X���
        if (outside)
        {
            float x0 = dragStartLocal.x;
            float y0 = dragStartLocal.y;
            // �N�����I�j���b�e����
            float x1 = Mathf.Clamp(localPos.x, markerParent.rect.xMin, markerParent.rect.xMax);
            float y1 = Mathf.Clamp(localPos.y, markerParent.rect.yMin, markerParent.rect.yMax);

            float minX = Mathf.Min(x0, x1);
            float minY = Mathf.Min(y0, y1);
            float w = Mathf.Abs(x1 - x0);
            float h = Mathf.Abs(y1 - y0);
            rt.anchoredPosition = new Vector2(minX, minY);
            rt.sizeDelta = new Vector2(w, h);
        }


        if (rt.sizeDelta.x < 5f || rt.sizeDelta.y < 5f)
        {
            Destroy(currentMarker);
            currentMarker = null;
            return;
        }

        // ���W�Ʈy�Ъ��p��]�ܱo�D�`²��
        Rect drawingRect = markerParent.rect;
        float nx = (rt.anchoredPosition.x - drawingRect.xMin) / drawingRect.width;
        float ny = (rt.anchoredPosition.y - drawingRect.yMin) / drawingRect.height;
        int id = annotations.Count + 1;
        annotations.Add(new HoleAnnotation { id = id, x = nx, y = ny });

        var tmpText = currentMarker.GetComponentInChildren<TMP_Text>();
        if (tmpText != null)
        {
            tmpText.text = id.ToString();
        }
        else
        {
            var legacyText = currentMarker.GetComponentInChildren<Text>();
            if (legacyText != null)
            {
                legacyText.text = id.ToString();
            }
        }

        noticeText.text = $"�s�W�լ} #{id}";
        currentMarker = null;
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

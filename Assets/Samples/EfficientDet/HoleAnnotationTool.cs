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
    public AspectRatioFitter aspectFitter;
    public TMP_InputField inputField;
    public Text noticeText;
    public Button confirmButton;

    [Header("Deletion UI")]
    public GameObject deletePanel;       // �@�ӥ]�t���D��r + Yes/No ���s�� Panel�A�w�]����
    public Text deleteQuestionText;      // Panel ����� "�n�R���լ} #id �ܡH"
    public Button deleteYesButton;
    public Button deleteNoButton;


    [Header("Marker Prefab")]
    public GameObject markerPrefab;

    // �����ϥ�
    private RectTransform markerParent;      // = imageDisplay.rectTransform
    private bool imageConfirmed = false;
    private Texture2D currentTexture;

    private Vector2 dragStartLocal;          // �e�ذ_�I�]local �y�С^
    private GameObject currentMarker;
    private List<HoleAnnotation> annotations = new List<HoleAnnotation>();

    // �ΨӼȦs�u�ݧR���v�� id + marker ����
    private int pendingDeleteId;
    private GameObject pendingDeleteMarker;

    void Start()
    {
        // 1. RawImage �౵���I��
        imageDisplay.raycastTarget = true;

        // 2. AspectRatio �]�� FitInParent
        aspectFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;

        var canvasRT = imageDisplay.canvas.GetComponent<RectTransform>();
        float canvasH = canvasRT.rect.height;

        // �Ҧp�G�Ϥ����ù� 60% ������
        float targetH = canvasH * 0.6f;
        var imgRT = imageDisplay.rectTransform;
        imgRT.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, targetH);

        // �]�i��^�p�G�Q�����T�w�K���W��
        imgRT.anchorMin = new Vector2(0, 1);
        imgRT.anchorMax = new Vector2(1, 1);
        imgRT.pivot = new Vector2(0.5f, 1);
        imgRT.anchoredPosition = Vector2.zero;

        // 3. �� markerParent ���� RawImage �� RectTransform�A�éԺ���
        markerParent = imageDisplay.rectTransform;
        markerParent.pivot = Vector2.zero;
        markerParent.anchorMin = Vector2.zero;
        markerParent.anchorMax = Vector2.one;
        markerParent.anchoredPosition = Vector2.zero;
        markerParent.sizeDelta = Vector2.zero;

        // 4. �I RawImage Ĳ�o����
        var trigger = imageDisplay.gameObject.AddComponent<EventTrigger>();
        var entry = new EventTrigger.Entry
        {
            eventID = EventTriggerType.PointerClick
        };
        entry.callback.AddListener((_) => {
            if (!imageConfirmed) PickImage();
        });
        trigger.triggers.Add(entry);

        // 5. �T�{���s
        confirmButton.onClick.AddListener(ConfirmImage);
        confirmButton.gameObject.SetActive(false);
        deleteYesButton.onClick.AddListener(OnDeleteYes);
        deleteNoButton.onClick.AddListener(() => deletePanel.SetActive(false));

        noticeText.text = "�Х���ܹϤ�";
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

            // ��K�ϥiŪ�B���V
            tex = MakeReadable(tex);
            if (tex.height > tex.width)
                tex = RotateTexture(tex, true);

            currentTexture = tex;
            imageDisplay.texture = tex;

            // �̿ù����ӳ̤j�ؤo�A�A�M�� AspectRatioFitter
            float maxW = Screen.width * 0.8f;
            float maxH = Screen.height * 0.6f;
            float scale = Mathf.Min(maxW / tex.width, maxH / tex.height, 1f);

            imageDisplay.rectTransform.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal, tex.width * scale);
            imageDisplay.rectTransform.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical, tex.height * scale);

            noticeText.text = $"�Ϥ��w���J ({tex.width}x{tex.height})�A�Ы��T�{";
            confirmButton.gameObject.SetActive(true);
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
        if (Input.GetMouseButtonDown(0))  HandleTouch(TouchPhase.Began,  Input.mousePosition, cam);
        if (Input.GetMouseButton(0))      HandleTouch(TouchPhase.Moved,  Input.mousePosition, cam);
        if (Input.GetMouseButtonUp(0))    HandleTouch(TouchPhase.Ended,  Input.mousePosition, cam);
#endif
    }

    void HandleTouch(TouchPhase phase, Vector2 screenPos, Camera cam)
    {
        // ��ù��I���ন markerParent�]RawImage�^�y�Шt�U�� local
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                markerParent, screenPos, cam, out Vector2 local))
            return;

        // �X�X �s�W�G�� local ��b�Ϥ� Rect ��
        Rect imgRect = markerParent.rect;
        local.x = Mathf.Clamp(local.x, imgRect.xMin, imgRect.xMax);
        local.y = Mathf.Clamp(local.y, imgRect.yMin, imgRect.yMax);

        if (phase == TouchPhase.Began)
        {
            dragStartLocal = local;

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

            // �P�ˤ] Clamp �}�l�P��e�I�p��X�Ӫ� min/max
            float x0 = Mathf.Clamp(dragStartLocal.x, imgRect.xMin, imgRect.xMax);
            float y0 = Mathf.Clamp(dragStartLocal.y, imgRect.yMin, imgRect.yMax);
            float x1 = Mathf.Clamp(local.x, imgRect.xMin, imgRect.xMax);
            float y1 = Mathf.Clamp(local.y, imgRect.yMin, imgRect.yMax);

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
            Rect r = markerParent.rect;
           

            float nx = (rt.anchoredPosition.x - r.xMin) / r.width;
            float ny = (rt.anchoredPosition.y - r.yMin) / r.height;

            int id = annotations.Count + 1;
            annotations.Add(new HoleAnnotation { id = id, x = nx, y = ny });
            var txt = currentMarker.GetComponentInChildren<TMP_Text>();
            if (txt) txt.text = id.ToString();

            var mb = currentMarker.AddComponent<MarkerBehaviour>();
            mb.holeId = id;
            mb.onClickMarker = PromptDelete;


            var label = currentMarker.GetComponentInChildren<TMP_Text>();
            if (label) label.text = id.ToString();
            noticeText.text = $"�s�W�լ} #{id}";
            currentMarker = null;
        }
    }

    /// <summary>
    /// MarkerBehaviour click �ɰ���G��ܧR����ܮ�
    /// </summary>
    void PromptDelete(int id, GameObject markerGO)
    {
        pendingDeleteId = id;
        pendingDeleteMarker = markerGO;
        deleteQuestionText.text = $"�T�w�n�R���լ} #{id} �ܡH";
        deletePanel.SetActive(true);
    }

    /// <summary>
    /// ���U Yes �ɰ���G������� & �R�� GameObject
    /// </summary>
    void OnDeleteYes()
    {
        // �R�����
        annotations.RemoveAll(h => h.id == pendingDeleteId);
        // �R���e���W����
        Destroy(pendingDeleteMarker);
        noticeText.text = $"�w�R���լ} #{pendingDeleteId}";
        deletePanel.SetActive(false);
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

    // �U��G�ন�iŪ�BRGBA32
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

    // ����K�ϡ]CW or CCW�^
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

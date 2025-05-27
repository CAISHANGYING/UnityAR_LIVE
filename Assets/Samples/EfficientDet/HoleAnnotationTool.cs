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
    public float x;   // 相對於圖片寬度的比例 0~1
    public float y;   // 相對於圖片高度的比例 0~1
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
    public GameObject deletePanel;       // 一個包含問題文字 + Yes/No 按鈕的 Panel，預設隱藏
    public Text deleteQuestionText;      // Panel 裡顯示 "要刪除孔洞 #id 嗎？"
    public Button deleteYesButton;
    public Button deleteNoButton;


    [Header("Marker Prefab")]
    public GameObject markerPrefab;

    // 內部使用
    private RectTransform markerParent;      // = imageDisplay.rectTransform
    private bool imageConfirmed = false;
    private Texture2D currentTexture;

    private Vector2 dragStartLocal;          // 畫框起點（local 座標）
    private GameObject currentMarker;
    private List<HoleAnnotation> annotations = new List<HoleAnnotation>();

    // 用來暫存「待刪除」的 id + marker 物件
    private int pendingDeleteId;
    private GameObject pendingDeleteMarker;

    void Start()
    {
        // 1. RawImage 能接受點擊
        imageDisplay.raycastTarget = true;

        // 2. AspectRatio 設為 FitInParent
        aspectFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;

        var canvasRT = imageDisplay.canvas.GetComponent<RectTransform>();
        float canvasH = canvasRT.rect.height;

        // 例如：圖片佔螢幕 60% 的高度
        float targetH = canvasH * 0.6f;
        var imgRT = imageDisplay.rectTransform;
        imgRT.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, targetH);

        // （可選）如果想讓它固定貼齊上方
        imgRT.anchorMin = new Vector2(0, 1);
        imgRT.anchorMax = new Vector2(1, 1);
        imgRT.pivot = new Vector2(0.5f, 1);
        imgRT.anchoredPosition = Vector2.zero;

        // 3. 把 markerParent 指到 RawImage 的 RectTransform，並拉滿它
        markerParent = imageDisplay.rectTransform;
        markerParent.pivot = Vector2.zero;
        markerParent.anchorMin = Vector2.zero;
        markerParent.anchorMax = Vector2.one;
        markerParent.anchoredPosition = Vector2.zero;
        markerParent.sizeDelta = Vector2.zero;

        // 4. 點 RawImage 觸發載圖
        var trigger = imageDisplay.gameObject.AddComponent<EventTrigger>();
        var entry = new EventTrigger.Entry
        {
            eventID = EventTriggerType.PointerClick
        };
        entry.callback.AddListener((_) => {
            if (!imageConfirmed) PickImage();
        });
        trigger.triggers.Add(entry);

        // 5. 確認按鈕
        confirmButton.onClick.AddListener(ConfirmImage);
        confirmButton.gameObject.SetActive(false);
        deleteYesButton.onClick.AddListener(OnDeleteYes);
        deleteNoButton.onClick.AddListener(() => deletePanel.SetActive(false));

        noticeText.text = "請先選擇圖片";
    }

    public void PickImage()
    {
        if (imageConfirmed)
        {
            noticeText.text = "圖片已確認，無法重新選擇";
            return;
        }

        NativeGallery.GetImageFromGallery((path) =>
        {
            if (string.IsNullOrEmpty(path)) return;

            var tex = NativeGallery.LoadImageAtPath(path, 2048);
            if (tex == null)
            {
                noticeText.text = "圖片載入失敗";
                return;
            }

            // 轉貼圖可讀、正向
            tex = MakeReadable(tex);
            if (tex.height > tex.width)
                tex = RotateTexture(tex, true);

            currentTexture = tex;
            imageDisplay.texture = tex;

            // 依螢幕給個最大尺寸，再套用 AspectRatioFitter
            float maxW = Screen.width * 0.8f;
            float maxH = Screen.height * 0.6f;
            float scale = Mathf.Min(maxW / tex.width, maxH / tex.height, 1f);

            imageDisplay.rectTransform.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal, tex.width * scale);
            imageDisplay.rectTransform.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical, tex.height * scale);

            noticeText.text = $"圖片已載入 ({tex.width}x{tex.height})，請按確認";
            confirmButton.gameObject.SetActive(true);
        }, "選擇一張圖片");
    }

    public void ConfirmImage()
    {
        if (currentTexture == null)
        {
            noticeText.text = "請先選擇圖片";
            return;
        }
        imageConfirmed = true;
        confirmButton.gameObject.SetActive(false);
        noticeText.text = "圖片已確認，開始框選孔洞";
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
        // 把螢幕點擊轉成 markerParent（RawImage）座標系下的 local
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                markerParent, screenPos, cam, out Vector2 local))
            return;

        // —— 新增：把 local 鎖在圖片 Rect 內
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

            // 同樣也 Clamp 開始與當前點計算出來的 min/max
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
            noticeText.text = $"新增孔洞 #{id}";
            currentMarker = null;
        }
    }

    /// <summary>
    /// MarkerBehaviour click 時執行：顯示刪除對話框
    /// </summary>
    void PromptDelete(int id, GameObject markerGO)
    {
        pendingDeleteId = id;
        pendingDeleteMarker = markerGO;
        deleteQuestionText.text = $"確定要刪除孔洞 #{id} 嗎？";
        deletePanel.SetActive(true);
    }

    /// <summary>
    /// 按下 Yes 時執行：移除資料 & 刪掉 GameObject
    /// </summary>
    void OnDeleteYes()
    {
        // 刪除資料
        annotations.RemoveAll(h => h.id == pendingDeleteId);
        // 刪除畫面上的框
        Destroy(pendingDeleteMarker);
        noticeText.text = $"已刪除孔洞 #{pendingDeleteId}";
        deletePanel.SetActive(false);
    }

    public void SaveJson()
    {
        var wrapper = new HoleAnnotationList { annotations = annotations };
        File.WriteAllText(
            Application.persistentDataPath + "/hole_sequence.json",
            JsonUtility.ToJson(wrapper, true)
        );
        noticeText.text = $"JSON 已儲存於 {Application.persistentDataPath}";
    }

    // 助手：轉成可讀、RGBA32
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

    // 輪轉貼圖（CW or CCW）
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

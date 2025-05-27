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
    public TMP_InputField inputField;    // 如不需要可自行移除
    public Text noticeText;
    public Button confirmButton;

    [Header("Marker Prefab")]
    public GameObject markerPrefab;

    // 內部使用
    private RectTransform markerParent;
    private bool imageConfirmed = false;
    private Texture2D currentTexture;

    private Vector2 dragStartLocal;
    private GameObject currentMarker;
    private List<HoleAnnotation> annotations = new List<HoleAnnotation>();

    void Start()
    {
        // 1. RawImage 能接收點擊
        imageDisplay.raycastTarget = true;
        aspectFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;

        // 2. 設定 imageDisplay 範圍：上方 60% 畫面
        var imgRT = imageDisplay.rectTransform;
        imgRT.anchorMin = new Vector2(0, 0.4f);
        imgRT.anchorMax = new Vector2(1, 1f);
        imgRT.offsetMin = imgRT.offsetMax = Vector2.zero;

        // 3. markerParent 直接指到 imageDisplay 並覆蓋它
        markerParent = imageDisplay.rectTransform;
        markerParent.pivot = Vector2.zero;
        markerParent.anchorMin = Vector2.zero;
        markerParent.anchorMax = Vector2.one;
        markerParent.anchoredPosition = Vector2.zero;
        markerParent.sizeDelta = Vector2.zero;

        // 4. 點 RawImage 才能選圖（確認前）
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

        noticeText.text = "請先選擇圖片";
    }

    public void PickImage()
    {
        // 已確認過就不再選圖
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

            // 轉到可讀貼圖、若需要旋轉則旋轉
            tex = MakeReadable(tex);
            if (tex.height > tex.width)
                tex = RotateTexture(tex, true);

            currentTexture = tex;
            imageDisplay.texture = tex;

            // 給最大範圍後套用 FitInParent
            float maxW = Screen.width * 0.8f;
            float maxH = Screen.height * 0.6f;
            float scale = Mathf.Min(maxW / tex.width, maxH / tex.height, 1f);

            imageDisplay.rectTransform.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal, tex.width * scale);
            imageDisplay.rectTransform.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical, tex.height * scale);

            noticeText.text = $"圖片已載入 ({tex.width}×{tex.height})，請按確認";
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
        if (Input.GetMouseButtonDown(0)) HandleTouch(TouchPhase.Began, Input.mousePosition, cam);
        if (Input.GetMouseButton(0)) HandleTouch(TouchPhase.Moved, Input.mousePosition, cam);
        if (Input.GetMouseButtonUp(0)) HandleTouch(TouchPhase.Ended, Input.mousePosition, cam);
#endif
    }

    void HandleTouch(TouchPhase phase, Vector2 screenPos, Camera cam)
    {
        // 轉成 markerParent 座標系下的 local
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                markerParent, screenPos, cam, out Vector2 local))
            return;

        // Clamp 在圖片範圍內
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

            float x0 = Mathf.Clamp(dragStartLocal.x, imgRect.xMin, imgRect.xMax);
            float y0 = Mathf.Clamp(dragStartLocal.y, imgRect.yMin, imgRect.yMax);
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
            var rt = currentMarker.GetComponent<RectTransform>();
            Rect r = markerParent.rect;

            float nx = (rt.anchoredPosition.x - r.xMin) / r.width;
            float ny = (rt.anchoredPosition.y - r.yMin) / r.height;

            int id = annotations.Count + 1;
            annotations.Add(new HoleAnnotation { id = id, x = nx, y = ny });

            var txt = currentMarker.GetComponentInChildren<TMP_Text>();
            if (txt) txt.text = id.ToString();

            noticeText.text = $"新增孔洞 #{id}";
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
        noticeText.text = $"JSON 已儲存於 {Application.persistentDataPath}";
    }

    // 工具：轉貼圖可讀、RGBA32
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

    // 工具：旋轉貼圖
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

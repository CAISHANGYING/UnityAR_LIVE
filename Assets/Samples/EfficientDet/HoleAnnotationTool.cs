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
    // AspectRatioFitter 我們不再需要，但保留變數以防您未來想用
    public AspectRatioFitter aspectFitter;
    public TMP_InputField inputField;
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
        // 1. 基本設定
        imageDisplay.raycastTarget = true;
        // 【重要】確保 AspectRatioFitter 已在編輯器中被停用或移除
        if (aspectFitter != null)
        {
            aspectFitter.enabled = false;
        }

        // 2. 定義 RawImage 的「最大顯示範圍」為左邊 2/3
        var imgRT = imageDisplay.rectTransform;
        imgRT.anchorMin = new Vector2(0, 0);
        imgRT.anchorMax = new Vector2(2f / 3f, 1);
        // 清空偏移，讓它的大小完全由錨點決定
        imgRT.offsetMin = Vector2.zero;
        imgRT.offsetMax = Vector2.zero;
        // 將軸心設在左下角，方便我們計算位置
        imgRT.pivot = new Vector2(0, 0);
        imgRT.anchoredPosition = Vector2.zero;

        // 3. 設定標記點的父物件
        markerParent = imageDisplay.rectTransform;

        // 4. 為 RawImage 的「整個顯示範圍」加上點擊選圖功能
        var trigger = imageDisplay.gameObject.AddComponent<EventTrigger>();
        var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
        entry.callback.AddListener((_) => {
            if (!imageConfirmed) PickImage();
        });
        trigger.triggers.Add(entry);

        // 5. 其他 UI 初始化
        if (confirmButton != null)
        {
            confirmButton.onClick.AddListener(ConfirmImage);
            confirmButton.gameObject.SetActive(false);
        }
        if (noticeText != null)
        {
            noticeText.text = "請先選擇圖片";
        }
    }

    public void PickImage()
    {
        if (imageConfirmed)
        {
            noticeText.text = "圖片已確認";
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

            tex = MakeReadable(tex);
            if (tex.height > tex.width)
                tex = RotateTexture(tex, true);

            currentTexture = tex;
            imageDisplay.texture = tex;

            // ---【核心邏輯：手動計算圖片大小與位置】---

            // 1. 取得 RawImage 最大顯示範圍的寬高
            float containerWidth = imageDisplay.rectTransform.rect.width;
            float containerHeight = imageDisplay.rectTransform.rect.height;

            // 2. 計算能讓「圖片本身」完整顯示在容器內的最大縮放比例
            float widthScale = containerWidth / tex.width;
            float heightScale = containerHeight / tex.height;
            float scale = Mathf.Min(widthScale, heightScale);

            // 3. 計算圖片縮放後的實際尺寸
            float newWidth = tex.width * scale;
            float newHeight = tex.height * scale;

            // 4. 【關鍵】我們要修改的是 RawImage 的 UV Rect，而不是它的 RectTransform
            // 這能在不改變「框框」大小的前提下，改變「圖片內容」的顯示方式
            float uv_x = (1f - newWidth / containerWidth) / 2f;
            float uv_y = (1f - newHeight / containerHeight) / 2f;
            float uv_w = newWidth / containerWidth;
            float uv_h = newHeight / containerHeight;
            imageDisplay.uvRect = new Rect(uv_x, uv_y, uv_w, uv_h);


            noticeText.text = $"圖片已載入 ({tex.width}x{tex.height})";
            if (confirmButton != null)
            {
                confirmButton.gameObject.SetActive(true);
            }
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
        // 取得相對於 markerParent (整個左側2/3區塊) 的本地座標
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                markerParent, screenPos, cam, out Vector2 local))
            return;

        // 根據 UV Rect 計算出實際圖片內容的顯示範圍
        Rect imgContainerRect = markerParent.rect;
        Rect uv = imageDisplay.uvRect;
        float actualImgX = imgContainerRect.x + imgContainerRect.width * uv.x;
        float actualImgY = imgContainerRect.y + imgContainerRect.height * uv.y;
        float actualImgW = imgContainerRect.width * uv.width;
        float actualImgH = imgContainerRect.height * uv.height;
        Rect actualImageRect = new Rect(actualImgX, actualImgY, actualImgW, actualImgH);

        // 【關鍵修正】我們移除了之前會擋住繪圖的 if 判斷
        // 現在，即使從黑色區域開始拖曳，框框也會從圖片邊緣開始畫

        if (phase == TouchPhase.Began)
        {
            // 將起始點座標強制限制在圖片的實際範圍內
            Vector2 clampedStartPos = local;
            clampedStartPos.x = Mathf.Clamp(clampedStartPos.x, actualImageRect.x, actualImageRect.xMax);
            clampedStartPos.y = Mathf.Clamp(clampedStartPos.y, actualImageRect.y, actualImageRect.yMax);

            dragStartLocal = clampedStartPos; // 儲存被限制過的起始點

            currentMarker = Instantiate(markerPrefab, markerParent, false);
            var rt = currentMarker.GetComponent<RectTransform>();
            rt.pivot = Vector2.zero;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.anchoredPosition = clampedStartPos; // 標記框的初始位置也用限制過的座標
            rt.sizeDelta = Vector2.zero;
        }
        else if (phase == TouchPhase.Moved && currentMarker != null)
        {
            var rt = currentMarker.GetComponent<RectTransform>();

            // 將當前滑鼠座標也限制在圖片的實際範圍內
            Vector2 clampedCurrentPos = local;
            clampedCurrentPos.x = Mathf.Clamp(clampedCurrentPos.x, actualImageRect.x, actualImageRect.xMax);
            clampedCurrentPos.y = Mathf.Clamp(clampedCurrentPos.y, actualImageRect.y, actualImageRect.yMax);

            // 使用被限制過的起始點和當前點來計算框框的四個角落
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

            // 如果框框太小，視為誤觸，直接刪除
            if (rt.sizeDelta.x < 5f || rt.sizeDelta.y < 5f)
            {
                Destroy(currentMarker);
                currentMarker = null;
                return;
            }

            // 計算正規化座標時，要以 actualImageRect 為基準
            float nx = (rt.anchoredPosition.x - actualImageRect.x) / actualImageRect.width;
            float ny = (rt.anchoredPosition.y - actualImageRect.y) / actualImageRect.height;
            int id = annotations.Count + 1;
            annotations.Add(new HoleAnnotation { id = id, x = nx, y = ny });

            // 【關鍵修正】讓程式碼變得更聰明，能同時處理新舊兩種文字元件
            // 優先尋找 TMP_Text
            var tmpText = currentMarker.GetComponentInChildren<TMP_Text>();
            if (tmpText != null)
            {
                tmpText.text = id.ToString();
            }
            else
            {
                // 如果找不到，再去找舊的 Text
                var legacyText = currentMarker.GetComponentInChildren<Text>();
                if (legacyText != null)
                {
                    legacyText.text = id.ToString();
                }
            }

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

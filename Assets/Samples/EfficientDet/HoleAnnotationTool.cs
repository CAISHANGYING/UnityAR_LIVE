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
    public AspectRatioFitter aspectFitter; // 這個 AspectRatioFitter 應該是掛在 imageDisplay 物件上
    public TMP_InputField inputField;
    public Text noticeText;
    public Button confirmButton;

    [Header("Marker Prefab")]
    public GameObject markerPrefab;

    // 內部使用
    private RectTransform markerParent; // 現在，這就是我們的畫布
    private bool imageConfirmed = false;
    private Texture2D currentTexture;
    private Vector2 dragStartLocal;
    private GameObject currentMarker;
    private List<HoleAnnotation> annotations = new List<HoleAnnotation>();

    void Start()
    {
        // 1. 基本設定
        imageDisplay.raycastTarget = true;

        // 【重要】確保 AspectRatioFitter 是啟用的，並且模式正確
        // 這個步驟現在完全由 Unity 編輯器控制，程式碼不需要干涉
        if (aspectFitter != null)
        {
            aspectFitter.enabled = true;
            aspectFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        }

        // 【重要】這裡不再需要程式碼來設定 RawImage 的大小
        // 因為它的大小現在由它的父物件(ImageContainer)和AspectRatioFitter來決定

        // 3. 設定標記點的父物件 (畫布)
        // 【重要】我們的畫布，就是 imageDisplay 本身。因為它的大小會自動縮放，所以它就是最完美的畫布。
        markerParent = imageDisplay.rectTransform;

        // 4. 點擊圖片區塊來選圖的功能 (可依需求保留或移除)
        // 注意：現在點擊的範圍會是縮小後的圖片，而不是整個左邊區塊
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

            tex = MakeReadable(tex);
            if (tex.height > tex.width)
                tex = RotateTexture(tex, true);

            currentTexture = tex;
            imageDisplay.texture = tex;

            // ---【核心修改】---
            // 我們不再需要任何手動計算大小或位置的程式碼。
            // 所有的工作都交給我們在編輯器設定好的 ImageContainer 和 AspectRatioFitter。
            // 這讓程式碼變得非常乾淨！

            noticeText.text = $"圖片已載入 ({tex.width}x{tex.height})，請按確認";
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
        // 我們的畫布(markerParent)現在就是 imageDisplay，它的大小是完美貼合圖片的。
        // 所以，我們直接在這個完美的畫布上進行座標轉換。
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                markerParent, screenPos, cam, out Vector2 local))
            return;

        // 【重要】如果點擊的位置不在畫布內，就直接忽略，避免在畫布外產生標記
        if (!markerParent.rect.Contains(local))
        {
            // 如果是在拖曳結束時釋放滑鼠，但滑鼠已經在畫布外，我們還是要處理這個標記
            if (phase == TouchPhase.Ended && currentMarker != null)
            {
                // 這裡的邏輯和下面的 TouchPhase.Ended 一樣
                FinalizeMarker(local, true); //傳入 true 代表這是在畫布外完成的
            }
            return;
        }

        if (phase == TouchPhase.Began)
        {
            dragStartLocal = local; // 直接使用本地座標
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
            FinalizeMarker(local, false); // 傳入 false 代表這是在畫布內完成的
        }
    }

    // 新增一個函式來處理標記框的最終生成，避免重複的程式碼
    void FinalizeMarker(Vector2 localPos, bool outside)
    {
        var rt = currentMarker.GetComponent<RectTransform>();

        // 如果是在畫布外完成，我們需要重新計算框框大小，確保它不會超出邊界
        if (outside)
        {
            float x0 = dragStartLocal.x;
            float y0 = dragStartLocal.y;
            // 將結束點強制限制在畫布內
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

        // 正規化座標的計算也變得非常簡單
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

        noticeText.text = $"新增孔洞 #{id}";
        currentMarker = null;
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

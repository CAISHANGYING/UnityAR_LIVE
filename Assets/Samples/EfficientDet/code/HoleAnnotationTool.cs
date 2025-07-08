using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine.EventSystems;
using System.Linq;
using TensorFlowLite;
using System.Collections;

public enum OperatingMode { None, ImageLoaded, Viewing, Editing, Numbering, Cropping }

[System.Serializable]
public class HoleAnnotation { public int id; public string label; public float x; public float y; public float width; public float height; }

[System.Serializable]
public class HoleAnnotationList { public List<HoleAnnotation> annotations = new List<HoleAnnotation>(); }

public class HoleAnnotationTool : MonoBehaviour
{
    [Header("UI Components")]
    public RawImage imageDisplay;
    public AspectRatioFitter aspectFitter;
    public Text noticeText;
    public Button confirmButton;

    [Header("Mode Buttons")]
    public Button EditModeButton;
    public Button NumberingModeButton;
    public Button DeleteAllMarkersButton;
    public Button DeleteAllNumbersButton;

    [Header("Cropping UI")]
    public Button CropModeButton;
    public Button ConfirmCropButton;
    public Button ResetCropButton;
    public GameObject CropUITools;
    public RectTransform CropAreaIndicator;
    public RectTransform[] CropHandles = new RectTransform[4]; // 0:BL, 1:BR, 2:TL, 3:TR

    [Header("Marker Prefab")]
    public GameObject markerPrefab;

    [Header("AI Model Settings (from EfficientDet)")]
    public EfficientDet.Options options = default;
    public TextAsset labelMapAsset;
    [Range(0f, 1f)]
    public float scoreThreshold = 0.5f;

    private TFLiteDetector detector;
    private OperatingMode currentMode;
    private int currentNumberingIndex = 1;
    private Texture2D currentTexture;
    private List<GameObject> allMarkers = new List<GameObject>();
    private List<HoleAnnotation> annotations = new List<HoleAnnotation>();
    private RectTransform markerParent;
    private MarkerBehaviour currentlySelectedMarker;
    private GameObject currentDrawingMarker;
    private RectTransform currentDraggedHandle;
    private Vector2 handleManualDrawing_dragStartLocal;


    void Start()
    {
        markerParent = imageDisplay.rectTransform;
        EditModeButton.onClick.AddListener(OnClickEditModeButton);
        NumberingModeButton.onClick.AddListener(OnClickNumberingModeButton);
        DeleteAllMarkersButton.onClick.AddListener(OnClickDeleteAllMarkersButton);
        DeleteAllNumbersButton.onClick.AddListener(OnClickDeleteAllNumbersButton);
        confirmButton.onClick.AddListener(ConfirmImage);
        CropModeButton.onClick.AddListener(OnClickCropModeButton);
        ConfirmCropButton.onClick.AddListener(ApplyCrop);
        ResetCropButton.onClick.AddListener(ResetCrop);
        if (labelMapAsset != null) { if (Application.isEditor) { options.delegateType = TfLiteDelegateType.XNNPACK; } detector = new TFLiteDetector(options, labelMapAsset, scoreThreshold); } else { Debug.LogError("AI 標籤 (Label Map Asset) 未指派！請在 Inspector 中設定。"); }
        SetMode(OperatingMode.None);
    }

    private void OnDestroy()
    {
        detector?.Dispose();
        if (currentTexture != null) Destroy(currentTexture);
    }

    void Update()
    {
        if (currentMode == OperatingMode.Cropping) { HandleCropping(); }
        else if (currentMode == OperatingMode.Editing) { HandleManualDrawing(); }
        else if (Input.GetMouseButtonDown(0))
        {
            PointerEventData pointerData = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
            List<RaycastResult> results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointerData, results);
            if (results.Count > 0 && results[0].gameObject == imageDisplay.gameObject && currentMode != OperatingMode.Cropping)
            {
                PickImage();
            }
        }
    }

    public void PickImage()
    {
        NativeGallery.GetImageFromGallery((path) =>
        {
            if (string.IsNullOrEmpty(path)) return;
            ResetCrop();
            OnClickDeleteAllMarkersButton();
            Texture2D originalTexture = NativeGallery.LoadImageAtPath(path, -1, false);
            if (originalTexture == null) { SetMode(OperatingMode.None); return; }
            var properties = NativeGallery.GetImageProperties(path);
            Texture2D processedTexture = TextureUtils.RotateTexture(originalTexture, properties.orientation);
            Destroy(originalTexture);
            if (processedTexture.height > processedTexture.width)
            {
                Texture2D rotatedTexture = TextureUtils.Rotate90DegreesCCW(processedTexture);
                Destroy(processedTexture);
                processedTexture = rotatedTexture;
            }
            if (currentTexture != null) Destroy(currentTexture);
            currentTexture = processedTexture;
            imageDisplay.texture = currentTexture;
            if (aspectFitter != null)
            {
                aspectFitter.enabled = true;
                aspectFitter.aspectRatio = (float)currentTexture.width / (float)currentTexture.height;
            }
            SetMode(OperatingMode.ImageLoaded);
        }, "選擇一張圖片");
    }

    public void ConfirmImage()
    {
        if (currentTexture == null || detector == null) { noticeText.text = "圖片或 AI 模型尚未準備好"; return; }

        int modelWidth = detector.InputWidth;
        int modelHeight = detector.InputHeight;
        RenderTexture detectionRT = RenderTexture.GetTemporary(modelWidth, modelHeight, 0, RenderTextureFormat.Default);

        Graphics.Blit(currentTexture, detectionRT);

        List<DetectionResult> results = detector.Detect(detectionRT);
        RenderTexture.ReleaseTemporary(detectionRT);
        int holeCount = 0;
        foreach (var result in results) { if (result.Label == "hole") { InstantiateMarker(result.Bbox.position, result.Bbox.size); holeCount++; } }
        noticeText.text = $"偵測到 {holeCount} 個孔洞 (hole)。";
        SetMode(OperatingMode.Viewing);
    }

    void SetMode(OperatingMode newMode)
    {
        // 修正BUG：先更新當前模式，再根據新模式來設定UI
        currentMode = newMode;

        if (currentlySelectedMarker != null) { currentlySelectedMarker.SetActionButtonActive(false); currentlySelectedMarker = null; }

        bool isViewing = currentMode == OperatingMode.Viewing;
        bool isEditing = currentMode == OperatingMode.Editing;
        bool isNumbering = currentMode == OperatingMode.Numbering;
        bool isCropping = currentMode == OperatingMode.Cropping;
        bool imageIsLoaded = currentMode != OperatingMode.None;

        confirmButton.gameObject.SetActive(currentMode == OperatingMode.ImageLoaded);
        EditModeButton.gameObject.SetActive(isViewing || isNumbering);
        NumberingModeButton.gameObject.SetActive(isViewing || isEditing);
        DeleteAllMarkersButton.gameObject.SetActive(isEditing);
        DeleteAllNumbersButton.gameObject.SetActive(isNumbering);

        CropModeButton.gameObject.SetActive(imageIsLoaded && !isCropping);
        ConfirmCropButton.gameObject.SetActive(isCropping);
        ResetCropButton.gameObject.SetActive(isCropping);
        if (CropUITools != null) CropUITools.SetActive(isCropping);

        switch (currentMode)
        {
            case OperatingMode.None:
                noticeText.text = "請點擊左側區域以選擇圖片";
                break;
            case OperatingMode.ImageLoaded:
                noticeText.text = "圖片已載入，可進行AI偵測或進入裁切模式";
                break;
            case OperatingMode.Viewing:
                noticeText.text = $"圖片已確認，可切換模式";
                UpdateMarkersForViewingMode();
                break;
            case OperatingMode.Editing:
                noticeText.text = "編輯模式：可拖曳、點擊選取、或拖曳背景以新增";
                UpdateMarkersForEditingMode();
                break;
            case OperatingMode.Numbering:
                noticeText.text = "編號模式：請點擊標記框右上角的按鈕進行編號";
                currentNumberingIndex = 1;
                foreach (var markerGO in allMarkers) { var behaviour = markerGO.GetComponent<MarkerBehaviour>(); if (behaviour != null) behaviour.SetActionButtonText(""); }
                UpdateMarkersForNumberingMode();
                break;
            case OperatingMode.Cropping:
                noticeText.text = "裁切模式：請拖動四個角點來選取範圍";
                StartCoroutine(ResetIndicatorAfterFrame());
                UpdateMarkersForViewingMode();
                break;
        }
    }

    IEnumerator ResetIndicatorAfterFrame()
    {
        yield return new WaitForEndOfFrame();
        ResetCropIndicator();
    }

    void ResetCropIndicator()
    {
        if (CropUITools == null) return;

        var containerRT = imageDisplay.transform.parent.GetComponent<RectTransform>();
        if (containerRT == null) return;

        var parentRT = CropUITools.GetComponent<RectTransform>();

        LayoutRebuilder.ForceRebuildLayoutImmediate(containerRT);

        parentRT.anchoredPosition = containerRT.anchoredPosition;
        parentRT.sizeDelta = containerRT.rect.size;

        CropAreaIndicator.anchoredPosition = Vector2.zero;
        CropAreaIndicator.sizeDelta = parentRT.rect.size;

        UpdateHandlesFromIndicator();
    }

    void HandleCropping()
    {
        Camera cam = imageDisplay.canvas.worldCamera;
        if (Input.GetMouseButtonDown(0))
        {
            foreach (var handle in CropHandles)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(handle, Input.mousePosition, cam))
                {
                    currentDraggedHandle = handle;
                    break;
                }
            }
        }
        else if (Input.GetMouseButton(0) && currentDraggedHandle != null)
        {
            var parentRT = CropUITools.GetComponent<RectTransform>();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRT, Input.mousePosition, cam, out Vector2 localMousePos);

            localMousePos.x = Mathf.Clamp(localMousePos.x, parentRT.rect.xMin, parentRT.rect.xMax);
            localMousePos.y = Mathf.Clamp(localMousePos.y, parentRT.rect.yMin, parentRT.rect.yMax);

            currentDraggedHandle.anchoredPosition = localMousePos;

            UpdateCropAreaFromHandles();
        }
        else if (Input.GetMouseButtonUp(0))
        {
            currentDraggedHandle = null;
        }
    }

    void UpdateCropAreaFromHandles()
    {
        if (currentDraggedHandle == null) return;
        int draggedIndex = -1;
        for (int i = 0; i < CropHandles.Length; i++)
        {
            if (CropHandles[i] == currentDraggedHandle)
            {
                draggedIndex = i;
                break;
            }
        }
        if (draggedIndex == -1) return;

        int oppositeIndex = 3 - draggedIndex;
        Vector2 draggedPos = currentDraggedHandle.anchoredPosition;
        Vector2 oppositePos = CropHandles[oppositeIndex].anchoredPosition;

        float xMin = Mathf.Min(draggedPos.x, oppositePos.x);
        float yMin = Mathf.Min(draggedPos.y, oppositePos.y);
        float xMax = Mathf.Max(draggedPos.x, oppositePos.x);
        float yMax = Mathf.Max(draggedPos.y, oppositePos.y);

        CropAreaIndicator.anchoredPosition = new Vector2(xMin, yMin);
        CropAreaIndicator.sizeDelta = new Vector2(xMax - xMin, yMax - yMin);

        CropHandles[0].anchoredPosition = new Vector2(xMin, yMin); // BL
        CropHandles[1].anchoredPosition = new Vector2(xMax, yMin); // BR
        CropHandles[2].anchoredPosition = new Vector2(xMin, yMax); // TL
        CropHandles[3].anchoredPosition = new Vector2(xMax, yMax); // TR
    }

    void UpdateHandlesFromIndicator()
    {
        Vector2 min = CropAreaIndicator.anchoredPosition;
        Vector2 max = min + CropAreaIndicator.sizeDelta;
        CropHandles[0].anchoredPosition = new Vector2(min.x, min.y);
        CropHandles[1].anchoredPosition = new Vector2(max.x, min.y);
        CropHandles[2].anchoredPosition = new Vector2(min.x, max.y);
        CropHandles[3].anchoredPosition = new Vector2(max.x, max.y);
    }

    void ApplyCrop()
    {
        if (currentTexture == null) return;
        RectTransform indicatorParentRT = CropAreaIndicator.parent.GetComponent<RectTransform>();
        if (indicatorParentRT.rect.width == 0 || indicatorParentRT.rect.height == 0) return;

        Rect cropIndicatorRect = CropAreaIndicator.rect;

        float normX = (cropIndicatorRect.x - indicatorParentRT.rect.x) / indicatorParentRT.rect.width;
        float normY = (cropIndicatorRect.y - indicatorParentRT.rect.y) / indicatorParentRT.rect.height;
        float normW = cropIndicatorRect.width / indicatorParentRT.rect.width;
        float normH = cropIndicatorRect.height / indicatorParentRT.rect.height;

        Rect cropUVRect = new Rect(normX, normY, normW, normH);
        int x = Mathf.FloorToInt(cropUVRect.x * currentTexture.width);
        int y = Mathf.FloorToInt(cropUVRect.y * currentTexture.height);
        int w = Mathf.FloorToInt(cropUVRect.width * currentTexture.width);
        int h = Mathf.FloorToInt(cropUVRect.height * currentTexture.height);

        if (w <= 0 || h <= 0) return;

        Texture2D croppedTexture = new Texture2D(w, h, currentTexture.format, false);

        RenderTexture rt = RenderTexture.GetTemporary(w, h);
        Graphics.Blit(currentTexture, rt, cropUVRect.size, cropUVRect.position);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;
        croppedTexture.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        croppedTexture.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);

        Destroy(currentTexture);
        currentTexture = croppedTexture;
        imageDisplay.texture = currentTexture;
        if (aspectFitter != null)
        {
            aspectFitter.aspectRatio = (float)currentTexture.width / (float)currentTexture.height;
        }

        OnClickDeleteAllMarkersButton();
        SetMode(OperatingMode.ImageLoaded);
        noticeText.text = "圖片裁切完成。";
    }

    void ResetCrop()
    {
        ResetCropIndicator();
        noticeText.text = "裁切範圍已重設。";
    }

    public void OnClickCropModeButton() => SetMode(OperatingMode.Cropping);
    public void OnClickEditModeButton() => SetMode(OperatingMode.Editing);
    public void OnClickNumberingModeButton() => SetMode(OperatingMode.Numbering);
    public void OnClickDeleteAllMarkersButton() { foreach (var marker in allMarkers) { Destroy(marker); } allMarkers.Clear(); annotations.Clear(); currentlySelectedMarker = null; if (currentMode == OperatingMode.Editing || currentMode == OperatingMode.None) noticeText.text = "所有標記框已清除"; }
    public void OnClickDeleteAllNumbersButton() { foreach (var markerGO in allMarkers) { var behaviour = markerGO.GetComponent<MarkerBehaviour>(); if (behaviour != null) behaviour.SetActionButtonText(""); } currentNumberingIndex = 1; noticeText.text = "所有編號已清除，請重新編號"; }
    void UpdateMarkersForViewingMode() { foreach (var markerGO in allMarkers) { var behaviour = markerGO.GetComponent<MarkerBehaviour>(); if (behaviour != null) { behaviour.SetDraggable(false); behaviour.SetActionButtonActive(false); } } }
    void UpdateMarkersForEditingMode() { foreach (var markerGO in allMarkers) { var behaviour = markerGO.GetComponent<MarkerBehaviour>(); if (behaviour != null) { behaviour.SetDraggable(true); behaviour.SetActionButtonActive(false); } } }
    void UpdateMarkersForNumberingMode() { foreach (var markerGO in allMarkers) { var behaviour = markerGO.GetComponent<MarkerBehaviour>(); if (behaviour != null) { behaviour.SetDraggable(false); Button btn = behaviour.GetActionButton(); if (btn != null) { behaviour.SetActionButtonActive(true); btn.onClick.RemoveAllListeners(); btn.onClick.AddListener(() => NumberingAction(behaviour)); } } } }
    public void DeleteMarker(GameObject markerToDelete) { if (allMarkers.Contains(markerToDelete)) { allMarkers.Remove(markerToDelete); Destroy(markerToDelete); if (currentlySelectedMarker != null && currentlySelectedMarker.gameObject == markerToDelete) { currentlySelectedMarker = null; } } }
    public void OnMarkerClicked(MarkerBehaviour marker) { if (currentMode == OperatingMode.Numbering) return; if (currentMode == OperatingMode.Editing) { if (currentlySelectedMarker != null && currentlySelectedMarker != marker) { currentlySelectedMarker.SetActionButtonActive(false); } currentlySelectedMarker = marker; Button btn = currentlySelectedMarker.GetActionButton(); if (btn != null) { currentlySelectedMarker.SetActionButtonText("X"); currentlySelectedMarker.SetActionButtonActive(true); btn.onClick.RemoveAllListeners(); btn.onClick.AddListener(() => DeleteMarker(currentlySelectedMarker.gameObject)); } } }
    private void NumberingAction(MarkerBehaviour marker) { if (currentMode == OperatingMode.Numbering) { var textComponent = marker.GetActionButton()?.GetComponentInChildren<TMP_Text>(); if (textComponent != null && string.IsNullOrEmpty(textComponent.text)) { marker.SetActionButtonText(currentNumberingIndex.ToString()); currentNumberingIndex++; } } }

    GameObject InstantiateMarker(Vector2 normalizedPosition, Vector2 normalizedSize)
    {
        float bottomY = normalizedPosition.y - normalizedSize.y;
        GameObject markerGO = Instantiate(markerPrefab, markerParent, false);
        var rt = markerGO.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(normalizedPosition.x, bottomY);
        rt.anchorMax = new Vector2(normalizedPosition.x + normalizedSize.x, normalizedPosition.y);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var behaviour = markerGO.GetComponent<MarkerBehaviour>();
        if (behaviour != null) { behaviour.Initialize(this, rt); behaviour.SetActionButtonActive(false); behaviour.SetDraggable(false); }
        allMarkers.Add(markerGO);
        return markerGO;
    }

    void HandleManualDrawing()
    {
        Camera cam = imageDisplay.canvas.renderMode == RenderMode.ScreenSpaceCamera ? imageDisplay.canvas.worldCamera : null;
        Rect imageRect = markerParent.rect;

        if (Input.GetMouseButtonDown(0))
        {
            PointerEventData pointerData = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
            List<RaycastResult> results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointerData, results);
            if (results.Any(r => r.gameObject.GetComponentInParent<MarkerBehaviour>() != null || r.gameObject.GetComponent<Button>() != null || r.gameObject.GetComponentInParent<Button>() != null))
            {
                return;
            }

            if (currentlySelectedMarker != null)
            {
                currentlySelectedMarker.SetActionButtonActive(false);
                currentlySelectedMarker = null;
            }

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(markerParent, Input.mousePosition, cam, out handleManualDrawing_dragStartLocal))
            {
                if (!imageRect.Contains(handleManualDrawing_dragStartLocal)) { return; }
                currentDrawingMarker = Instantiate(markerPrefab, markerParent, false);
                var behaviour = currentDrawingMarker.GetComponent<MarkerBehaviour>();
                if (behaviour != null) behaviour.enabled = false;

                var rt = currentDrawingMarker.GetComponent<RectTransform>();
                rt.pivot = new Vector2(0, 0);
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = handleManualDrawing_dragStartLocal;
                rt.sizeDelta = Vector2.zero;
            }
        }
        else if (Input.GetMouseButton(0) && currentDrawingMarker != null)
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(markerParent, Input.mousePosition, cam, out Vector2 currentLocal))
            {
                var rt = currentDrawingMarker.GetComponent<RectTransform>();

                float minX = Mathf.Min(handleManualDrawing_dragStartLocal.x, currentLocal.x);
                float minY = Mathf.Min(handleManualDrawing_dragStartLocal.y, currentLocal.y);
                float w = Mathf.Abs(currentLocal.x - handleManualDrawing_dragStartLocal.x);
                float h = Mathf.Abs(currentLocal.y - handleManualDrawing_dragStartLocal.y);

                rt.anchoredPosition = new Vector2(minX, minY);
                rt.sizeDelta = new Vector2(w, h);
            }
        }
        else if (Input.GetMouseButtonUp(0) && currentDrawingMarker != null)
        {
            var rt = currentDrawingMarker.GetComponent<RectTransform>();
            if (rt.sizeDelta.x < 10f || rt.sizeDelta.y < 10f)
            {
                Destroy(currentDrawingMarker);
            }
            else
            {
                Rect parentRect = markerParent.rect;
                Vector2 finalPos = rt.anchoredPosition;
                Vector2 finalSize = rt.sizeDelta;

                float newAnchorMinX = (finalPos.x - parentRect.x) / parentRect.width;
                float newAnchorMinY = (finalPos.y - parentRect.y) / parentRect.height;
                float newAnchorMaxX = (finalPos.x + finalSize.x - parentRect.x) / parentRect.width;
                float newAnchorMaxY = (finalPos.y + finalSize.y - parentRect.y) / parentRect.height;

                rt.anchorMin = new Vector2(newAnchorMinX, newAnchorMinY);
                rt.anchorMax = new Vector2(newAnchorMaxX, newAnchorMaxY);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = Vector2.zero;

                var behaviour = currentDrawingMarker.GetComponent<MarkerBehaviour>();
                if (behaviour != null)
                {
                    behaviour.enabled = true;
                    behaviour.Initialize(this, rt);
                    behaviour.SetDraggable(true);
                }

                if (currentlySelectedMarker != null) { currentlySelectedMarker.SetActionButtonActive(false); }
                allMarkers.Add(currentDrawingMarker);
                OnMarkerClicked(behaviour);
            }
            currentDrawingMarker = null;
        }
    }

    public void SaveJson()
    {
        annotations.Clear();
        var sortedMarkers = new List<GameObject>();
        var numberedMarkers = new Dictionary<int, GameObject>();
        var unnumberedMarkers = new List<GameObject>();

        foreach (var markerGO in allMarkers)
        {
            var behaviour = markerGO.GetComponent<MarkerBehaviour>();
            string numText = "";
            var textComponent = behaviour.GetActionButton()?.GetComponentInChildren<TMP_Text>();
            if (textComponent != null) numText = textComponent.text;

            if (!string.IsNullOrEmpty(numText) && int.TryParse(numText, out int id))
            {
                if (!numberedMarkers.ContainsKey(id)) numberedMarkers.Add(id, markerGO);
            }
            else
            {
                unnumberedMarkers.Add(markerGO);
            }
        }

        sortedMarkers.AddRange(numberedMarkers.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value));
        sortedMarkers.AddRange(unnumberedMarkers);

        int nextUnnumberedId = (numberedMarkers.Count > 0 ? numberedMarkers.Keys.Max() : 0) + 1;

        foreach (var markerGO in sortedMarkers)
        {
            var rt = markerGO.GetComponent<RectTransform>();
            var behaviour = markerGO.GetComponent<MarkerBehaviour>();
            float nx = rt.anchorMin.x;
            float ny_from_top = 1 - rt.anchorMax.y;
            float nw = rt.anchorMax.x - rt.anchorMin.x;
            float nh = rt.anchorMax.y - rt.anchorMin.y;

            string numText = "";
            var textComponent = behaviour.GetActionButton()?.GetComponentInChildren<TMP_Text>();
            if (textComponent != null) numText = textComponent.text;

            int.TryParse(numText, out int holeNumber);
            int finalId = (holeNumber > 0) ? holeNumber : nextUnnumberedId++;

            annotations.Add(new HoleAnnotation { id = finalId, label = "hole", x = nx, y = ny_from_top, width = nw, height = nh });
        }

        var wrapper = new HoleAnnotationList { annotations = this.annotations };
        string json = JsonUtility.ToJson(wrapper, true);
        string path = Path.Combine(Application.persistentDataPath, "hole_annotations.json");
        File.WriteAllText(path, json);

        noticeText.text = $"JSON 已儲存 ({annotations.Count} 筆資料)";
        Debug.Log($"JSON saved to: {path}");
    }
}
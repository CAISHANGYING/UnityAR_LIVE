using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine.EventSystems;
using System.Linq;
using TensorFlowLite;

public enum OperatingMode { None, ImageLoaded, Viewing, Editing, Numbering, Cropping }
[System.Serializable] public class HoleAnnotation { public int id; public string label; public float x; public float y; public float width; public float height; }
[System.Serializable] public class HoleAnnotationList { public List<HoleAnnotation> annotations = new List<HoleAnnotation>(); }


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

    [Header("4-Point Cropping UI")]
    public RectTransform cropAreaVisual;
    public RectTransform topLeftHandle;
    public RectTransform topRightHandle;
    public RectTransform bottomLeftHandle;
    public RectTransform bottomRightHandle;
    private RectTransform currentlyDraggedHandle;

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
    private Vector2 manualDrawStartPosition;
    private Vector2 dragOffset;

    void Start()
    {
        markerParent = imageDisplay.rectTransform;
        if (cropAreaVisual != null) cropAreaVisual.pivot = new Vector2(0, 0);

        EditModeButton.onClick.AddListener(OnClickEditModeButton);
        NumberingModeButton.onClick.AddListener(OnClickNumberingModeButton);
        DeleteAllMarkersButton.onClick.AddListener(OnClickDeleteAllMarkersButton);
        DeleteAllNumbersButton.onClick.AddListener(OnClickDeleteAllNumbersButton);
        confirmButton.onClick.AddListener(ConfirmImage);

        CropModeButton.onClick.AddListener(OnClickCropModeButton);
        ConfirmCropButton.onClick.AddListener(ApplyCrop);
        ResetCropButton.onClick.AddListener(ResetCropHandles);

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
        if (currentMode == OperatingMode.Cropping) { HandleFourPointCrop(); }
        else if (currentMode == OperatingMode.Editing) { HandleManualDrawing(); }
        else if (Input.GetMouseButtonDown(0))
        {
            PointerEventData pointerData = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
            List<RaycastResult> results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointerData, results);
            if (currentMode != OperatingMode.Cropping && results.Count > 0 && results[0].gameObject == imageDisplay.gameObject)
            {
                if (currentMode == OperatingMode.None || currentMode == OperatingMode.ImageLoaded)
                    PickImage();
            }
        }
    }

    public void PickImage()
    {
        NativeGallery.GetImageFromGallery((path) =>
        {
            if (string.IsNullOrEmpty(path)) return;
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
        Graphics.Blit(currentTexture, detectionRT, imageDisplay.uvRect.size, imageDisplay.uvRect.position);
        List<DetectionResult> results = detector.Detect(detectionRT);
        RenderTexture.ReleaseTemporary(detectionRT);
        int holeCount = 0;
        foreach (var result in results) { if (result.Label == "hole") { InstantiateMarker(result.Bbox.position, result.Bbox.size); holeCount++; } }
        noticeText.text = $"偵測到 {holeCount} 個孔洞 (hole)。";
        SetMode(OperatingMode.Viewing);
    }

    void SetMode(OperatingMode newMode)
    {
        if (newMode != OperatingMode.Cropping)
        {
            SetCropControlsActive(false);
        }

        if (currentlySelectedMarker != null)
        {
            currentlySelectedMarker.SetActionButtonActive(false);
            currentlySelectedMarker = null;
        }

        currentMode = newMode;

        confirmButton.gameObject.SetActive(currentMode == OperatingMode.ImageLoaded);
        CropModeButton.gameObject.SetActive(currentMode == OperatingMode.ImageLoaded);
        EditModeButton.gameObject.SetActive(currentMode == OperatingMode.Viewing || currentMode == OperatingMode.Numbering);
        NumberingModeButton.gameObject.SetActive(currentMode == OperatingMode.Viewing || currentMode == OperatingMode.Editing);
        DeleteAllMarkersButton.gameObject.SetActive(currentMode == OperatingMode.Editing);
        DeleteAllNumbersButton.gameObject.SetActive(currentMode == OperatingMode.Numbering);
        ConfirmCropButton.gameObject.SetActive(currentMode == OperatingMode.Cropping);
        ResetCropButton.gameObject.SetActive(currentMode == OperatingMode.Cropping);

        switch (currentMode)
        {
            case OperatingMode.None:
                noticeText.text = "請點擊畫面載入圖片";
                break;
            case OperatingMode.ImageLoaded:
                noticeText.text = "圖片已載入，請確認或進行裁切";
                break;
            case OperatingMode.Viewing:
                noticeText.text = "檢視模式，請選擇操作";
                UpdateMarkersForViewingMode();
                break;
            case OperatingMode.Editing:
                noticeText.text = "編輯模式：可新增、拖曳、刪除標記框";
                UpdateMarkersForEditingMode();
                break;
            case OperatingMode.Numbering:
                noticeText.text = "編號模式：請點擊標記框右上角的按鈕進行編號";
                currentNumberingIndex = 1;
                foreach (var markerGO in allMarkers) { markerGO.GetComponent<MarkerBehaviour>().SetActionButtonText(""); }
                UpdateMarkersForNumberingMode();
                break;
            // 【步驟1: 已修改】
            case OperatingMode.Cropping:
                noticeText.text = "裁切模式：請拖曳角落控制點以調整範圍";
                UpdateMarkersForViewingMode();
                SetCropControlsActive(true);
                StartCoroutine(ResetHandlesAfterFrame()); // 改為啟動協程
                break;
        }
    }

    #region Cropping Logic

    // 【步驟2: 已新增】
    private System.Collections.IEnumerator ResetHandlesAfterFrame()
    {
        // 等待當前畫面影格的結尾，此時所有UI佈局都已計算完畢
        yield return new WaitForEndOfFrame();

        // 現在才執行位置設定
        ResetCropHandles();
    }

    private void SetCropControlsActive(bool isActive)
    {
        if (cropAreaVisual != null) cropAreaVisual.gameObject.SetActive(isActive);
        if (topLeftHandle != null) topLeftHandle.gameObject.SetActive(isActive);
        if (topRightHandle != null) topRightHandle.gameObject.SetActive(isActive);
        if (bottomLeftHandle != null) bottomLeftHandle.gameObject.SetActive(isActive);
        if (bottomRightHandle != null) bottomRightHandle.gameObject.SetActive(isActive);
    }

    private void ResetCropHandles()
    {
        if (imageDisplay.texture == null || topLeftHandle == null) return;

        Vector3[] imageCorners = new Vector3[4];
        imageDisplay.rectTransform.GetWorldCorners(imageCorners);

        topLeftHandle.position = imageCorners[1];
        topRightHandle.position = imageCorners[2];
        bottomRightHandle.position = imageCorners[3];
        bottomLeftHandle.position = imageCorners[0];

        UpdateCropVisuals();
    }

    void HandleFourPointCrop()
    {
        Camera cam = imageDisplay.canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : imageDisplay.canvas.worldCamera;
        var handleParent = (RectTransform)topLeftHandle.parent;

        if (Input.GetMouseButtonDown(0))
        {
            RectTransform[] handles = { topLeftHandle, topRightHandle, bottomLeftHandle, bottomRightHandle };
            currentlyDraggedHandle = null;

            foreach (var handle in handles)
            {
                if (handle != null && RectTransformUtility.RectangleContainsScreenPoint(handle, Input.mousePosition, cam))
                {
                    currentlyDraggedHandle = handle;
                    break;
                }
            }

            if (currentlyDraggedHandle != null)
            {
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(handleParent, Input.mousePosition, cam, out Vector2 localMousePos))
                {
                    dragOffset = currentlyDraggedHandle.anchoredPosition - localMousePos;
                }
            }
        }
        else if (Input.GetMouseButton(0) && currentlyDraggedHandle != null)
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(handleParent, Input.mousePosition, cam, out Vector2 localMousePos))
            {
                currentlyDraggedHandle.anchoredPosition = localMousePos + dragOffset;
                UpdateCropVisuals();
            }
        }
        else if (Input.GetMouseButtonUp(0) && currentlyDraggedHandle != null)
        {
            currentlyDraggedHandle = null;
        }
    }

    void UpdateCropVisuals()
    {
        if (topLeftHandle == null || topRightHandle == null || bottomLeftHandle == null || bottomRightHandle == null || cropAreaVisual == null) return;

        Vector2 tlPos = topLeftHandle.anchoredPosition;
        Vector2 trPos = topRightHandle.anchoredPosition;
        Vector2 blPos = bottomLeftHandle.anchoredPosition;
        Vector2 brPos = bottomRightHandle.anchoredPosition;

        if (currentlyDraggedHandle == topLeftHandle)
        {
            bottomLeftHandle.anchoredPosition = new Vector2(tlPos.x, brPos.y);
            topRightHandle.anchoredPosition = new Vector2(brPos.x, tlPos.y);
        }
        else if (currentlyDraggedHandle == bottomRightHandle)
        {
            topRightHandle.anchoredPosition = new Vector2(brPos.x, tlPos.y);
            bottomLeftHandle.anchoredPosition = new Vector2(tlPos.x, brPos.y);
        }
        else if (currentlyDraggedHandle == topRightHandle)
        {
            topLeftHandle.anchoredPosition = new Vector2(blPos.x, trPos.y);
            bottomRightHandle.anchoredPosition = new Vector2(trPos.x, blPos.y);
        }
        else if (currentlyDraggedHandle == bottomLeftHandle)
        {
            topLeftHandle.anchoredPosition = new Vector2(blPos.x, trPos.y);
            bottomRightHandle.anchoredPosition = new Vector2(trPos.x, blPos.y);
        }

        Vector2 final_bl = bottomLeftHandle.anchoredPosition;
        Vector2 final_tr = topRightHandle.anchoredPosition;

        cropAreaVisual.anchoredPosition = final_bl;
        cropAreaVisual.sizeDelta = final_tr - final_bl;
    }

    void ApplyCrop()
    {
        if (currentTexture == null) return;

        var cropControlsRectTransform = (RectTransform)cropAreaVisual.parent;
        Vector2 bl_handle_local = bottomLeftHandle.anchoredPosition;
        Vector2 tr_handle_local = topRightHandle.anchoredPosition;

        Rect displayRect = cropControlsRectTransform.rect;

        float normX = (bl_handle_local.x - displayRect.x) / displayRect.width;
        float normY = (bl_handle_local.y - displayRect.y) / displayRect.height;
        float normW = (tr_handle_local.x - bl_handle_local.x) / displayRect.width;
        float normH = (tr_handle_local.y - bl_handle_local.y) / displayRect.height;

        int texX = (int)(normX * currentTexture.width);
        int texY = (int)(normY * currentTexture.height);
        int texW = (int)(normW * currentTexture.width);
        int texH = (int)(normH * currentTexture.height);

        if (texW <= 0 || texH <= 0) return;

        Texture2D croppedTexture = new Texture2D(texW, texH, currentTexture.format, false);
        RenderTexture rt = RenderTexture.GetTemporary(texW, texH);
        Graphics.Blit(currentTexture, rt, new Vector2(normW, normH), new Vector2(normX, normY));
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;
        croppedTexture.ReadPixels(new Rect(0, 0, texW, texH), 0, 0);
        croppedTexture.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);

        Destroy(currentTexture);
        currentTexture = croppedTexture;

        if (imageDisplay != null) imageDisplay.texture = currentTexture;
        if (aspectFitter != null) aspectFitter.aspectRatio = (float)currentTexture.width / (float)currentTexture.height;

        OnClickDeleteAllMarkersButton();
        SetCropControlsActive(false);
        SetMode(OperatingMode.ImageLoaded);
    }
    #endregion

    #region Marker Handling and Mode Logics

    public void OnClickCropModeButton() => SetMode(OperatingMode.Cropping);
    public void OnClickEditModeButton() => SetMode(OperatingMode.Editing);
    public void OnClickNumberingModeButton() => SetMode(OperatingMode.Numbering);

    public void OnClickDeleteAllMarkersButton() { foreach (var marker in allMarkers) { Destroy(marker); } allMarkers.Clear(); annotations.Clear(); currentlySelectedMarker = null; currentNumberingIndex = 1; if (currentMode == OperatingMode.Editing || currentMode == OperatingMode.None) noticeText.text = "所有標記框已清除"; }
    public void OnClickDeleteAllNumbersButton() { foreach (var markerGO in allMarkers) { markerGO.GetComponent<MarkerBehaviour>().SetActionButtonText(""); } currentNumberingIndex = 1; noticeText.text = "所有編號已清除，請重新編號"; }

    void UpdateMarkersForViewingMode() { foreach (var markerGO in allMarkers) { var behaviour = markerGO.GetComponent<MarkerBehaviour>(); behaviour.SetDraggable(false); behaviour.SetActionButtonActive(false); } }
    void UpdateMarkersForEditingMode() { foreach (var markerGO in allMarkers) { var behaviour = markerGO.GetComponent<MarkerBehaviour>(); behaviour.SetDraggable(true); behaviour.SetActionButtonActive(false); } }

    void UpdateMarkersForNumberingMode()
    {
        foreach (var markerGO in allMarkers)
        {
            var behaviour = markerGO.GetComponent<MarkerBehaviour>();
            behaviour.SetDraggable(false);
            Button btn = behaviour.GetActionButton();
            if (btn != null)
            {
                behaviour.SetActionButtonActive(true);
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => NumberingAction(behaviour));
            }
        }
    }

    public void DeleteMarker(GameObject markerToDelete) { if (allMarkers.Contains(markerToDelete)) { allMarkers.Remove(markerToDelete); Destroy(markerToDelete); if (currentlySelectedMarker != null && currentlySelectedMarker.gameObject == markerToDelete) { currentlySelectedMarker = null; } } }

    public void OnMarkerClicked(MarkerBehaviour marker)
    {
        if (currentMode == OperatingMode.Numbering) return;

        if (currentMode == OperatingMode.Editing)
        {
            if (currentlySelectedMarker != null && currentlySelectedMarker != marker)
            {
                currentlySelectedMarker.SetActionButtonActive(false);
            }
            currentlySelectedMarker = marker;
            Button btn = currentlySelectedMarker.GetActionButton();
            if (btn != null)
            {
                currentlySelectedMarker.SetActionButtonText("X");
                currentlySelectedMarker.SetActionButtonActive(true);
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => DeleteMarker(currentlySelectedMarker.gameObject));
            }
        }
    }

    private void NumberingAction(MarkerBehaviour marker)
    {
        if (currentMode == OperatingMode.Numbering)
        {
            var textComponent = marker.GetActionButton()?.GetComponentInChildren<TMP_Text>();
            if (textComponent != null && string.IsNullOrEmpty(textComponent.text))
            {
                marker.SetActionButtonText(currentNumberingIndex.ToString());
                currentNumberingIndex++;
            }
        }
    }

    #endregion

    #region Drawing and Instantiation

    void HandleManualDrawing()
    {
        Camera cam = imageDisplay.canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : imageDisplay.canvas.worldCamera;
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
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(markerParent, Input.mousePosition, cam, out Vector2 localStartPos))
            {
                if (!imageRect.Contains(localStartPos)) { return; }
                currentDrawingMarker = Instantiate(markerPrefab, markerParent, false);
                var behaviour = currentDrawingMarker.GetComponent<MarkerBehaviour>();
                if (behaviour != null) behaviour.enabled = false;
                var rt = currentDrawingMarker.GetComponent<RectTransform>();
                rt.pivot = new Vector2(0, 0);
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = localStartPos;
                manualDrawStartPosition = localStartPos;
                rt.sizeDelta = Vector2.zero;
            }
        }
        else if (Input.GetMouseButton(0) && currentDrawingMarker != null)
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(markerParent, Input.mousePosition, cam, out Vector2 currentLocal))
            {
                var rt = currentDrawingMarker.GetComponent<RectTransform>();
                float minX = Mathf.Min(manualDrawStartPosition.x, currentLocal.x);
                float minY = Mathf.Min(manualDrawStartPosition.y, currentLocal.y);
                float w = Mathf.Abs(currentLocal.x - manualDrawStartPosition.x);
                float h = Mathf.Abs(currentLocal.y - manualDrawStartPosition.y);
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
                SetMode(OperatingMode.Editing);
                OnMarkerClicked(behaviour);
            }
            currentDrawingMarker = null;
        }
    }

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
        if (behaviour != null)
        {
            behaviour.Initialize(this, rt);
            behaviour.SetActionButtonActive(false);
            behaviour.SetDraggable(false);
        }
        allMarkers.Add(markerGO);
        return markerGO;
    }

    public void SaveJson()
    {
        annotations.Clear(); var sortedMarkers = new List<GameObject>(); var numberedMarkers = new Dictionary<int, GameObject>(); var unnumberedMarkers = new List<GameObject>(); foreach (var markerGO in allMarkers) { var behaviour = markerGO.GetComponent<MarkerBehaviour>(); string numText = ""; var textComponent = behaviour.GetActionButton()?.GetComponentInChildren<TMP_Text>(); if (textComponent != null) numText = textComponent.text; if (!string.IsNullOrEmpty(numText) && int.TryParse(numText, out int id)) { if (!numberedMarkers.ContainsKey(id)) numberedMarkers.Add(id, markerGO); } else { unnumberedMarkers.Add(markerGO); } }
        sortedMarkers.AddRange(numberedMarkers.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value)); sortedMarkers.AddRange(unnumberedMarkers); int nextUnnumberedId = (numberedMarkers.Count > 0 ? numberedMarkers.Keys.Max() : 0) + 1; foreach (var markerGO in sortedMarkers) { var rt = markerGO.GetComponent<RectTransform>(); var behaviour = markerGO.GetComponent<MarkerBehaviour>(); float nx = rt.anchorMin.x; float ny_from_top = 1 - rt.anchorMax.y; float nw = rt.anchorMax.x - rt.anchorMin.x; float nh = rt.anchorMax.y - rt.anchorMin.y; string numText = ""; var textComponent = behaviour.GetActionButton()?.GetComponentInChildren<TMP_Text>(); if (textComponent != null) numText = textComponent.text; int.TryParse(numText, out int holeNumber); int finalId = (holeNumber > 0) ? holeNumber : nextUnnumberedId++; annotations.Add(new HoleAnnotation { id = finalId, label = "hole", x = nx, y = ny_from_top, width = nw, height = nh }); }
        var wrapper = new HoleAnnotationList { annotations = this.annotations }; string json = JsonUtility.ToJson(wrapper, true); string path = Path.Combine(Application.persistentDataPath, "hole_annotations.json"); File.WriteAllText(path, json); noticeText.text = $"JSON 已儲存 ({annotations.Count} 筆資料)"; Debug.Log($"JSON saved to: {path}");
    }
    #endregion
}
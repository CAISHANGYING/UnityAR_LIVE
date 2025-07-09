using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine.EventSystems;
using System.Linq;
using TensorFlowLite;

// �i�������ܡj
public enum OperatingMode { None, ImageLoaded, Viewing, Editing, Numbering, Cropping }
[System.Serializable] public class HoleAnnotation { public int id; public string label; public float x; public float y; public float width; public float height; }
[System.Serializable] public class HoleAnnotationList { public List<HoleAnnotation> annotations = new List<HoleAnnotation>(); }


public class HoleAnnotationTool : MonoBehaviour
{
    // �i�������ܡj
    [Header("UI Components")]
    public RawImage imageDisplay;
    public AspectRatioFitter aspectFitter;
    public Text noticeText;
    public Button confirmButton;

    // �i�������ܡj
    [Header("Mode Buttons")]
    public Button EditModeButton;
    public Button NumberingModeButton;
    public Button DeleteAllMarkersButton;
    public Button DeleteAllNumbersButton;

    // �i�������ܡj
    [Header("Cropping UI")]
    public Button CropModeButton;
    public Button ConfirmCropButton;
    public Button ResetCropButton;

    // �i�������ܡj
    [Header("Marker Prefab")]
    public GameObject markerPrefab;

    // �i�������ܡj
    [Header("AI Model Settings (from EfficientDet)")]
    public EfficientDet.Options options = default;
    public TextAsset labelMapAsset;
    [Range(0f, 1f)]
    public float scoreThreshold = 0.5f;

    // --- �p���ܼ� ---
    private TFLiteDetector detector;
    private OperatingMode currentMode;
    private int currentNumberingIndex = 1;
    private Texture2D currentTexture;
    private List<GameObject> allMarkers = new List<GameObject>();
    private List<HoleAnnotation> annotations = new List<HoleAnnotation>();
    private RectTransform markerParent;
    private MarkerBehaviour currentlySelectedMarker;
    private GameObject currentDrawingMarker;
    private Vector2 manualDrawStartPosition; // �i<<< �B�J1: �s�W���ܼơj

    // �i�������ܡj
    private Vector2 panStartPosition;
    private Vector2 panStartUVPosition;
    private const float MIN_ZOOM = 0.1f;
    private const float MAX_ZOOM = 1.0f;
    public float zoomSpeed = 0.1f;

    void Start()
    {
        // �i�������ܡj
        markerParent = imageDisplay.rectTransform;

        EditModeButton.onClick.AddListener(OnClickEditModeButton);
        NumberingModeButton.onClick.AddListener(OnClickNumberingModeButton);
        DeleteAllMarkersButton.onClick.AddListener(OnClickDeleteAllMarkersButton);
        DeleteAllNumbersButton.onClick.AddListener(OnClickDeleteAllNumbersButton);
        confirmButton.onClick.AddListener(ConfirmImage);

        CropModeButton.onClick.AddListener(OnClickCropModeButton);
        ConfirmCropButton.onClick.AddListener(ApplyCrop);
        ResetCropButton.onClick.AddListener(ResetCrop);

        if (labelMapAsset != null) { if (Application.isEditor) { options.delegateType = TfLiteDelegateType.XNNPACK; } detector = new TFLiteDetector(options, labelMapAsset, scoreThreshold); } else { Debug.LogError("AI ���� (Label Map Asset) �������I�Цb Inspector ���]�w�C"); }
        SetMode(OperatingMode.None);
    }

    private void OnDestroy()
    {
        // �i�������ܡj
        detector?.Dispose();
        if (currentTexture != null) Destroy(currentTexture);
    }

    void Update()
    {
        // �i�������ܡj
        if (currentMode == OperatingMode.Cropping) { HandleCropping(); }
        else if (currentMode == OperatingMode.Editing) { HandleManualDrawing(); }
        else if (Input.GetMouseButtonDown(0))
        {
            PointerEventData pointerData = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
            List<RaycastResult> results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointerData, results);
            if (results.Count > 0 && results[0].gameObject.transform.parent.gameObject == imageDisplay.transform.parent.gameObject && currentMode != OperatingMode.Cropping)
            {
                if (results[0].gameObject == imageDisplay.gameObject)
                    PickImage();
            }
        }
    }

    // --- �H�U�Ҧ���L�禡���������ܡA�u�ק� HandleManualDrawing ---

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
        }, "��ܤ@�i�Ϥ�");
    }

    public void ConfirmImage()
    {
        if (currentTexture == null || detector == null) { noticeText.text = "�Ϥ��� AI �ҫ��|���ǳƦn"; return; }
        int modelWidth = detector.InputWidth;
        int modelHeight = detector.InputHeight;
        RenderTexture detectionRT = RenderTexture.GetTemporary(modelWidth, modelHeight, 0, RenderTextureFormat.Default);
        Graphics.Blit(currentTexture, detectionRT, imageDisplay.uvRect.size, imageDisplay.uvRect.position);
        List<DetectionResult> results = detector.Detect(detectionRT);
        RenderTexture.ReleaseTemporary(detectionRT);
        int holeCount = 0;
        foreach (var result in results) { if (result.Label == "hole") { InstantiateMarker(result.Bbox.position, result.Bbox.size); holeCount++; } }
        noticeText.text = $"������ {holeCount} �Ӥլ} (hole)�C";
        SetMode(OperatingMode.Viewing);
    }

    void SetMode(OperatingMode newMode)
    {
        if (currentlySelectedMarker != null) { currentlySelectedMarker.SetActionButtonActive(false); currentlySelectedMarker = null; }
        currentMode = newMode;

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

        switch (currentMode)
        {
            case OperatingMode.None: noticeText.text = "���I���e�����J�Ϥ�"; break;
            case OperatingMode.ImageLoaded: noticeText.text = "�Ϥ��w���J�A�i�i��AI�����ζi�J�����Ҧ�"; break;
            case OperatingMode.Viewing: noticeText.text = $"�Ϥ��w�T�{�A�i�����Ҧ�"; UpdateMarkersForViewingMode(); break;
            case OperatingMode.Editing: noticeText.text = "�s��Ҧ��G�i�즲�B�I������B�Ω즲�I���H�s�W"; UpdateMarkersForEditingMode(); break;
            case OperatingMode.Numbering: noticeText.text = "�s���Ҧ��G�I�����s�H�s��"; UpdateMarkersForNumberingMode(); break;
            case OperatingMode.Cropping: noticeText.text = "�����Ҧ��G�즲�H�����A�u���H�Y��"; UpdateMarkersForViewingMode(); break;
        }
    }

    void HandleCropping()
    {
        var rt = imageDisplay.rectTransform;
        Camera cam = imageDisplay.canvas.worldCamera;

        if (!RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, cam)) { return; }

        float scroll = Input.mouseScrollDelta.y;
        if (scroll != 0)
        {
            Rect uvRect = imageDisplay.uvRect;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, Input.mousePosition, cam, out Vector2 localPos);
            Vector2 pivot = new Vector2((localPos.x - rt.rect.x) / rt.rect.width, (localPos.y - rt.rect.y) / rt.rect.height);
            float zoomFactor = 1 - scroll * zoomSpeed;
            float newWidth = Mathf.Clamp(uvRect.width * zoomFactor, MIN_ZOOM, MAX_ZOOM);
            float newHeight = Mathf.Clamp(uvRect.height * zoomFactor, MIN_ZOOM, MAX_ZOOM);
            uvRect.x = pivot.x - (pivot.x - uvRect.x) * (newWidth / uvRect.width);
            uvRect.y = pivot.y - (pivot.y - uvRect.y) * (newHeight / uvRect.height);
            uvRect.width = newWidth;
            uvRect.height = newHeight;
            imageDisplay.uvRect = uvRect;
        }

        if (Input.GetMouseButtonDown(0))
        {
            panStartPosition = Input.mousePosition;
            panStartUVPosition = imageDisplay.uvRect.position;
        }

        if (Input.GetMouseButton(0))
        {
            Vector2 delta = (Vector2)Input.mousePosition - panStartPosition;
            float deltaUVX = -(delta.x / rt.rect.width) * imageDisplay.uvRect.width;
            float deltaUVY = -(delta.y / rt.rect.height) * imageDisplay.uvRect.height;
            Rect uvRect = imageDisplay.uvRect;
            uvRect.position = panStartUVPosition + new Vector2(deltaUVX, deltaUVY);
            imageDisplay.uvRect = uvRect;
        }

        Rect finalRect = imageDisplay.uvRect;
        finalRect.x = Mathf.Clamp(finalRect.x, 0, 1 - finalRect.width);
        finalRect.y = Mathf.Clamp(finalRect.y, 0, 1 - finalRect.height);
        imageDisplay.uvRect = finalRect;
    }

    void ApplyCrop()
    {
        if (currentTexture == null) return;
        Rect cropRect = imageDisplay.uvRect;
        int x = Mathf.FloorToInt(cropRect.x * currentTexture.width);
        int y = Mathf.FloorToInt(cropRect.y * currentTexture.height);
        int w = Mathf.FloorToInt(cropRect.width * currentTexture.width);
        int h = Mathf.FloorToInt(cropRect.height * currentTexture.height);

        Texture2D croppedTexture = new Texture2D(w, h, currentTexture.format, false);
        RenderTexture rt = RenderTexture.GetTemporary(w, h);
        Graphics.Blit(currentTexture, rt, cropRect.size, cropRect.position);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;
        croppedTexture.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        croppedTexture.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);

        Destroy(currentTexture);
        currentTexture = croppedTexture;
        imageDisplay.texture = currentTexture;
        imageDisplay.uvRect = new Rect(0, 0, 1, 1);
        if (aspectFitter != null) { aspectFitter.aspectRatio = (float)currentTexture.width / (float)currentTexture.height; }
        OnClickDeleteAllMarkersButton();
        SetMode(OperatingMode.ImageLoaded);
        noticeText.text = "�Ϥ����������C";
    }

    void ResetCrop()
    {
        imageDisplay.uvRect = new Rect(0, 0, 1, 1);
        noticeText.text = "�����w���]�C";
    }

    #region Unchanged Methods
    public void OnClickCropModeButton() => SetMode(OperatingMode.Cropping);
    public void OnClickEditModeButton() => SetMode(OperatingMode.Editing);
    public void OnClickNumberingModeButton() => SetMode(OperatingMode.Numbering);
    public void OnClickDeleteAllMarkersButton() { foreach (var marker in allMarkers) { Destroy(marker); } allMarkers.Clear(); annotations.Clear(); currentlySelectedMarker = null; if (currentMode == OperatingMode.Editing || currentMode == OperatingMode.None) noticeText.text = "�Ҧ��аO�ؤw�M��"; }
    public void OnClickDeleteAllNumbersButton() { foreach (var markerGO in allMarkers) { markerGO.GetComponent<MarkerBehaviour>().SetActionButtonText(""); } currentNumberingIndex = 1; noticeText.text = "�Ҧ��s���w�M���A�Э��s�s��"; }
    void UpdateMarkersForViewingMode() { foreach (var markerGO in allMarkers) { var behaviour = markerGO.GetComponent<MarkerBehaviour>(); behaviour.SetDraggable(false); behaviour.SetActionButtonActive(false); } }
    void UpdateMarkersForEditingMode() { foreach (var markerGO in allMarkers) { var behaviour = markerGO.GetComponent<MarkerBehaviour>(); behaviour.SetDraggable(true); behaviour.SetActionButtonActive(false); } }
    void UpdateMarkersForNumberingMode() { foreach (var markerGO in allMarkers) { var behaviour = markerGO.GetComponent<MarkerBehaviour>(); behaviour.SetDraggable(false); Button btn = behaviour.GetActionButton(); if (btn != null) { behaviour.SetActionButtonActive(true); btn.onClick.RemoveAllListeners(); btn.onClick.AddListener(() => NumberingAction(behaviour)); } } }
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
        if (behaviour != null)
        {
            behaviour.Initialize(this, rt);
            behaviour.SetActionButtonActive(false);
            behaviour.SetDraggable(false);
        }
        allMarkers.Add(markerGO);
        return markerGO;
    }

    void HandleManualDrawing()
    {
        Camera cam = imageDisplay.canvas.renderMode == RenderMode.ScreenSpaceCamera ? imageDisplay.canvas.worldCamera : null;
        Rect imageRect = markerParent.rect;
        Vector2 dragStartLocal = Vector2.zero; // �o���ܼƲ{�b�u�b MouseDown ���{�ɨϥ�

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

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(markerParent, Input.mousePosition, cam, out dragStartLocal))
            {
                if (!imageRect.Contains(dragStartLocal)) { return; }
                currentDrawingMarker = Instantiate(markerPrefab, markerParent, false);
                var behaviour = currentDrawingMarker.GetComponent<MarkerBehaviour>();
                if (behaviour != null) behaviour.enabled = false;

                var rt = currentDrawingMarker.GetComponent<RectTransform>();
                rt.pivot = new Vector2(0, 0);
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);

                // �i�B�J2: �ק�j
                rt.anchoredPosition = dragStartLocal;
                manualDrawStartPosition = dragStartLocal; // �x�s�u�����_�l�I

                rt.sizeDelta = Vector2.zero;
            }
        }
        else if (Input.GetMouseButton(0) && currentDrawingMarker != null)
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(markerParent, Input.mousePosition, cam, out Vector2 currentLocal))
            {
                // �i�B�J3: �ק�j
                var rt = currentDrawingMarker.GetComponent<RectTransform>();

                // �ϥ� manualDrawStartPosition (�T�w���_�l�I) �ӭp��
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
                OnMarkerClicked(behaviour);
            }
            currentDrawingMarker = null;
        }
    }

    public void SaveJson()
    {
        annotations.Clear(); var sortedMarkers = new List<GameObject>(); var numberedMarkers = new Dictionary<int, GameObject>(); var unnumberedMarkers = new List<GameObject>(); foreach (var markerGO in allMarkers) { var behaviour = markerGO.GetComponent<MarkerBehaviour>(); string numText = ""; var textComponent = behaviour.GetActionButton()?.GetComponentInChildren<TMP_Text>(); if (textComponent != null) numText = textComponent.text; if (!string.IsNullOrEmpty(numText) && int.TryParse(numText, out int id)) { if (!numberedMarkers.ContainsKey(id)) numberedMarkers.Add(id, markerGO); } else { unnumberedMarkers.Add(markerGO); } }
        sortedMarkers.AddRange(numberedMarkers.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value)); sortedMarkers.AddRange(unnumberedMarkers); int nextUnnumberedId = (numberedMarkers.Count > 0 ? numberedMarkers.Keys.Max() : 0) + 1; foreach (var markerGO in sortedMarkers) { var rt = markerGO.GetComponent<RectTransform>(); var behaviour = markerGO.GetComponent<MarkerBehaviour>(); float nx = rt.anchorMin.x; float ny_from_top = 1 - rt.anchorMax.y; float nw = rt.anchorMax.x - rt.anchorMin.x; float nh = rt.anchorMax.y - rt.anchorMin.y; string numText = ""; var textComponent = behaviour.GetActionButton()?.GetComponentInChildren<TMP_Text>(); if (textComponent != null) numText = textComponent.text; int.TryParse(numText, out int holeNumber); int finalId = (holeNumber > 0) ? holeNumber : nextUnnumberedId++; annotations.Add(new HoleAnnotation { id = finalId, label = "hole", x = nx, y = ny_from_top, width = nw, height = nh }); }
        var wrapper = new HoleAnnotationList { annotations = this.annotations }; string json = JsonUtility.ToJson(wrapper, true); string path = Path.Combine(Application.persistentDataPath, "hole_annotations.json"); File.WriteAllText(path, json); noticeText.text = $"JSON �w�x�s ({annotations.Count} �����)"; Debug.Log($"JSON saved to: {path}");
    }
    #endregion
}
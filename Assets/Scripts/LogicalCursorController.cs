using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DefaultExecutionOrder(-50)]
[DisallowMultipleComponent]
public class LogicalCursorController : MonoBehaviour
{
    public static LogicalCursorController Active { get; private set; }

    [Header("References")]
    public Camera targetCamera;
    public TilePlacementGrid roomGrid;
    public Image uiCursorImage;
    public string uiCursorObjectName = "drag";

    [Header("System Cursor")]
    public bool hideSystemCursor = true;
    public bool confineSystemCursorToWindow = true;
    public bool restoreSystemCursorOnDisable = true;

    [Header("Logical Cursor")]
    public bool clampCursorToRoom = true;
    public bool freezeWorldCursorWhenPointerOverUI = true;
    public bool showUICursorAtSystemPointerOverUI = true;

    [Header("Camera Pan")]
    public bool driveCameraFromCursorOffset = true;
    public float maxCameraPanSpeed = 12f;
    public float panDeadZonePixels = 24f;
    public bool useUnscaledTime = false;

    private RectTransform uiCursorRectTransform;
    private Canvas uiCursorCanvas;
    private Vector3 currentWorldPosition;
    private bool hasWorldPosition;
    private bool previousCursorVisible;
    private CursorLockMode previousCursorLockMode;
    private bool cursorStateApplied;
    private bool warnedAboutMissingRoomArea;

    public Vector3 CurrentWorldPosition => currentWorldPosition;
    public bool HasWorldPosition => hasWorldPosition;

    public static bool IsRunning => Active != null && Active.isActiveAndEnabled;
    public static bool HasActive => IsRunning && Active.hasWorldPosition;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateDefaultControllerIfNeeded()
    {
        if (FindFirstObjectByType<LogicalCursorController>() != null)
            return;

        if (FindFirstObjectByType<TilePlacementGrid>() == null)
            return;

        GameObject controllerObject = new GameObject("Interaction Manager");
        controllerObject.AddComponent<LogicalCursorController>();
    }

    void Awake()
    {
        if (Active != null && Active != this)
        {
            enabled = false;
            return;
        }

        Active = this;
        ResolveReferences();
        ApplySystemCursorState();
        InitializeWorldPosition();
    }

    void OnEnable()
    {
        Active = this;
        ResolveReferences();
        ApplySystemCursorState();
    }

    void Start()
    {
        ResolveReferences();
        InitializeWorldPosition();
    }

    void Update()
    {
        DeveloperModeState.TryHandleHotkey();
    }

    void LateUpdate()
    {
        ResolveReferences();

        bool pointerOverUI = IsPointerOverUI();
        float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

        UpdateLogicalWorldPosition(pointerOverUI);
        ApplyCameraPan(pointerOverUI, deltaTime);
        UpdateUICursorVisual(pointerOverUI);
    }

    void OnDisable()
    {
        if (Active == this)
            Active = null;

        if (restoreSystemCursorOnDisable)
            RestoreSystemCursorState();
    }

    void OnDestroy()
    {
        if (Active == this)
            Active = null;

        if (restoreSystemCursorOnDisable)
            RestoreSystemCursorState();
    }

    public static bool TryGetWorldPosition(out Vector3 worldPosition)
    {
        if (HasActive)
        {
            worldPosition = Active.currentWorldPosition;
            return true;
        }

        worldPosition = default;
        return false;
    }

    public static bool TryGetScreenPosition(out Vector2 screenPosition)
    {
        if (HasActive && Active.targetCamera != null)
        {
            screenPosition = Active.targetCamera.WorldToScreenPoint(Active.currentWorldPosition);
            return true;
        }

        screenPosition = default;
        return false;
    }

    private void ResolveReferences()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        if (roomGrid == null)
            roomGrid = FindFirstObjectByType<TilePlacementGrid>();

        if (uiCursorImage == null)
            uiCursorImage = FindUICursorImageByName(uiCursorObjectName);

        if (uiCursorImage != null)
        {
            uiCursorImage.raycastTarget = false;
            uiCursorRectTransform = uiCursorImage.rectTransform;
            uiCursorCanvas = uiCursorImage.GetComponentInParent<Canvas>();
        }
    }

    private void InitializeWorldPosition()
    {
        if (hasWorldPosition || targetCamera == null)
            return;

        Vector3 startWorld = ScreenToWorld(Input.mousePosition);
        if (clampCursorToRoom && roomGrid != null && roomGrid.HasRoomCells)
            startWorld = roomGrid.ClampWorldPoint(startWorld, startWorld);

        startWorld.z = 0f;
        currentWorldPosition = startWorld;
        hasWorldPosition = true;
    }

    private void UpdateLogicalWorldPosition(bool pointerOverUI)
    {
        if (targetCamera == null)
            return;

        InitializeWorldPosition();

        if (pointerOverUI && freezeWorldCursorWhenPointerOverUI)
            return;

        Vector3 desiredWorld = ScreenToWorld(Input.mousePosition);
        Vector3 nextWorld = desiredWorld;

        if (clampCursorToRoom && roomGrid != null)
        {
            if (roomGrid.HasRoomCells)
            {
                nextWorld = roomGrid.ClampWorldPoint(desiredWorld, currentWorldPosition);
                warnedAboutMissingRoomArea = false;
            }
            else if (!warnedAboutMissingRoomArea)
            {
                Debug.LogWarning("[LogicalCursorController] No occupied room cells found. Cursor clamp is inactive until room cells are registered.");
                warnedAboutMissingRoomArea = true;
            }
        }

        nextWorld.z = 0f;
        currentWorldPosition = nextWorld;
        hasWorldPosition = true;
    }

    private void ApplyCameraPan(bool pointerOverUI, float deltaTime)
    {
        if (!driveCameraFromCursorOffset || targetCamera == null || !hasWorldPosition)
            return;

        if (pointerOverUI && freezeWorldCursorWhenPointerOverUI)
            return;

        Vector2 cursorScreenPoint = targetCamera.WorldToScreenPoint(currentWorldPosition);
        Vector2 panInput = GetCenterPanInput(cursorScreenPoint);
        if (panInput == Vector2.zero)
            return;

        Vector3 cameraPosition = targetCamera.transform.position;
        cameraPosition += new Vector3(panInput.x, panInput.y, 0f) * maxCameraPanSpeed * deltaTime;
        targetCamera.transform.position = cameraPosition;
    }

    private Vector2 GetCenterPanInput(Vector2 cursorScreenPoint)
    {
        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        Vector2 delta = cursorScreenPoint - screenCenter;

        float distance = delta.magnitude;
        if (distance <= panDeadZonePixels)
            return Vector2.zero;

        Vector2 halfScreen = new Vector2(
            Mathf.Max(1f, Screen.width * 0.5f),
            Mathf.Max(1f, Screen.height * 0.5f)
        );

        Vector2 normalized = new Vector2(
            Mathf.Clamp(delta.x / halfScreen.x, -1f, 1f),
            Mathf.Clamp(delta.y / halfScreen.y, -1f, 1f)
        );

        float maxDistance = Mathf.Max(1f, Mathf.Min(halfScreen.x, halfScreen.y));
        float strength = Mathf.Clamp01((distance - panDeadZonePixels) / Mathf.Max(1f, maxDistance - panDeadZonePixels));

        if (normalized.sqrMagnitude > 1f)
            normalized = normalized.normalized;

        return normalized * strength;
    }

    private void UpdateUICursorVisual(bool pointerOverUI)
    {
        if (uiCursorImage == null || uiCursorRectTransform == null)
            return;

        Vector2 screenPoint;
        if (pointerOverUI && showUICursorAtSystemPointerOverUI)
            screenPoint = Input.mousePosition;
        else if (targetCamera != null && hasWorldPosition)
            screenPoint = targetCamera.WorldToScreenPoint(currentWorldPosition);
        else
            screenPoint = Input.mousePosition;

        RectTransform parentRect = uiCursorRectTransform.parent as RectTransform;
        if (parentRect == null)
            return;

        Camera eventCamera = null;
        if (uiCursorCanvas != null && uiCursorCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            eventCamera = uiCursorCanvas.worldCamera != null ? uiCursorCanvas.worldCamera : targetCamera;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screenPoint, eventCamera, out Vector2 localPoint))
            uiCursorRectTransform.anchoredPosition = localPoint;
    }

    private Vector3 ScreenToWorld(Vector3 screenPosition)
    {
        screenPosition.x = Mathf.Clamp(screenPosition.x, 0f, Mathf.Max(1f, Screen.width));
        screenPosition.y = Mathf.Clamp(screenPosition.y, 0f, Mathf.Max(1f, Screen.height));
        screenPosition.z = targetCamera != null ? Mathf.Abs(targetCamera.transform.position.z) : 0f;

        Vector3 world = targetCamera.ScreenToWorldPoint(screenPosition);
        world.z = 0f;
        return world;
    }

    private bool IsPointerOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    private Image FindUICursorImageByName(string objectName)
    {
        Image[] images = FindObjectsByType<Image>(FindObjectsSortMode.None);
        for (int i = 0; i < images.Length; i++)
        {
            Image image = images[i];
            if (image == null || image.gameObject == null)
                continue;

            if (image.gameObject.name.Equals(objectName, System.StringComparison.OrdinalIgnoreCase))
                return image;
        }

        return null;
    }

    private void ApplySystemCursorState()
    {
        if (cursorStateApplied)
            return;

        previousCursorVisible = Cursor.visible;
        previousCursorLockMode = Cursor.lockState;
        cursorStateApplied = true;

        if (!hideSystemCursor)
            return;

        Cursor.visible = false;
        Cursor.lockState = confineSystemCursorToWindow ? CursorLockMode.Confined : CursorLockMode.None;
    }

    private void RestoreSystemCursorState()
    {
        if (!cursorStateApplied)
            return;

        Cursor.visible = previousCursorVisible;
        Cursor.lockState = previousCursorLockMode;
        cursorStateApplied = false;
    }
}

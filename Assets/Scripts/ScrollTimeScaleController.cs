using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public class ScrollTimeScaleController : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Speed")]
    public float scrollStep = 0.5f;
    public float minTimeScale = 0.5f;
    public float maxTimeScale = 4f;
    public bool invertScrollDirection = false;

    [Header("Target")]
    public bool affectGlobalTimeScale = true;
    public bool useLogicalCursorForWorldTarget = true;

    [Header("Cursor")]
    public Texture2D hoverCursorTexture;
    public Vector2 cursorHotspot;
    public CursorMode cursorMode = CursorMode.Auto;

    private bool isPointerOverUiTarget;
    private bool isPointerOverWorldTarget;
    private float baseFixedDeltaTime;
    private Collider2D targetCollider2D;
    private Collider targetCollider3D;

    void Awake()
    {
        baseFixedDeltaTime = Time.fixedDeltaTime;
        targetCollider2D = GetComponent<Collider2D>();
        targetCollider3D = GetComponent<Collider>();
    }

    void Update()
    {
        if (!affectGlobalTimeScale)
            return;

        UpdateLogicalWorldTargetHover();

        if (!IsPointerOverThisTarget())
            return;

        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) < 0.01f)
            return;

        if (invertScrollDirection)
            scroll = -scroll;

        float nextTimeScale = Mathf.Clamp(Time.timeScale + scroll * scrollStep, minTimeScale, maxTimeScale);
        ApplyTimeScale(nextTimeScale);
    }

    void OnDisable()
    {
        isPointerOverUiTarget = false;
        isPointerOverWorldTarget = false;
        RestoreDefaultCursor();
    }

    void OnMouseEnter()
    {
        if (useLogicalCursorForWorldTarget && PlayerControl.HasActive)
            return;

        isPointerOverWorldTarget = true;
        ApplyHoverCursor();
    }

    void OnMouseExit()
    {
        if (useLogicalCursorForWorldTarget && PlayerControl.HasActive)
            return;

        isPointerOverWorldTarget = false;
        RestoreDefaultCursor();
    }

    void OnMouseOver()
    {
        if (useLogicalCursorForWorldTarget && PlayerControl.HasActive)
            return;

        isPointerOverWorldTarget = true;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isPointerOverUiTarget = true;
        ApplyHoverCursor();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isPointerOverUiTarget = false;
        RestoreDefaultCursor();
    }

    public void SetTimeScale(float value)
    {
        ApplyTimeScale(Mathf.Clamp(value, minTimeScale, maxTimeScale));
    }

    private bool IsPointerOverThisTarget()
    {
        return isPointerOverUiTarget || isPointerOverWorldTarget;
    }

    private void UpdateLogicalWorldTargetHover()
    {
        if (!useLogicalCursorForWorldTarget)
            return;

        if (!PlayerControl.TryGetInteractionWorldPosition(out Vector3 cursorWorld))
            return;

        bool isHovering = false;
        if (targetCollider2D != null)
        {
            cursorWorld.z = transform.position.z;
            isHovering = targetCollider2D.OverlapPoint(cursorWorld);
        }
        else if (targetCollider3D != null)
        {
            Vector3 boundsPoint = new Vector3(cursorWorld.x, cursorWorld.y, targetCollider3D.bounds.center.z);
            isHovering = targetCollider3D.bounds.Contains(boundsPoint);
        }

        if (isPointerOverWorldTarget == isHovering)
            return;

        isPointerOverWorldTarget = isHovering;
        if (isPointerOverWorldTarget)
            ApplyHoverCursor();
        else if (!isPointerOverUiTarget)
            RestoreDefaultCursor();
    }

    private void ApplyTimeScale(float newTimeScale)
    {
        Time.timeScale = newTimeScale;
        Time.fixedDeltaTime = baseFixedDeltaTime * newTimeScale;
    }

    private void ApplyHoverCursor()
    {
        if (hoverCursorTexture == null)
            return;

        Cursor.SetCursor(hoverCursorTexture, cursorHotspot, cursorMode);
    }

    private void RestoreDefaultCursor()
    {
        Cursor.SetCursor(null, Vector2.zero, cursorMode);
    }
}

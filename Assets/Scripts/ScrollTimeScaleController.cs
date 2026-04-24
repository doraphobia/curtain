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

    [Header("Cursor")]
    public Texture2D hoverCursorTexture;
    public Vector2 cursorHotspot;
    public CursorMode cursorMode = CursorMode.Auto;

    private bool isPointerOverUiTarget;
    private bool isPointerOverWorldTarget;
    private float baseFixedDeltaTime;

    void Awake()
    {
        baseFixedDeltaTime = Time.fixedDeltaTime;
    }

    void Update()
    {
        if (!affectGlobalTimeScale)
            return;

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
        isPointerOverWorldTarget = true;
        ApplyHoverCursor();
    }

    void OnMouseExit()
    {
        isPointerOverWorldTarget = false;
        RestoreDefaultCursor();
    }

    void OnMouseOver()
    {
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

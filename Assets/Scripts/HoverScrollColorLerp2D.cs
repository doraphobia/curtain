using UnityEngine;

[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class HoverScrollColorLerp2D : MonoBehaviour
{
    public enum SideType
    {
        None,
        Left,
        Right
    }

    [Header("Colors")]
    public Color colorA = Color.white;
    public Color colorB = Color.red;

    [Header("Currency")]
    public SideType sideType = SideType.None;
    public StageCycleController stageController;
    public TimeCounterUI currencyTarget;
    [Range(0f, 1f)]
    public float colorBThreshold = 0.99f;

    [Header("Scroll Settings")]
    [Tooltip("Scroll一次改变多少（0~1之间的进度）")]
    [Range(0.001f, 0.5f)]
    public float stepPerScroll = 0.08f;

    [Tooltip("是否反转滚轮方向")]
    public bool invertScroll = false;

    [Header("Input")]
    public bool allowLocalHoverInput = true;

    [Header("Cursor")]
    public Texture2D hoverCursorTexture;
    public Vector2 cursorHotspot = Vector2.zero;
    public CursorMode cursorMode = CursorMode.Auto;

    private SpriteRenderer sr;
    private float t = 0f; // 0 -> colorA, 1 -> colorB
    private bool isHovering = false;
    private float currencyBuffer = 0f;

    public float ColorProgress => t;
    public bool IsAtColorA => t <= 1f - colorBThreshold;
    public bool IsAtColorB => t >= colorBThreshold;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        AutoAssignReferences();
        ApplyColor();
    }

    void Update()
    {
        HandleScrollInput();
        GenerateCurrency();
    }

    void HandleScrollInput()
    {
        if (!allowLocalHoverInput) return;
        if (!isHovering) return;

        float scroll = Input.mouseScrollDelta.y;
        ApplyScrollDelta(scroll);
    }

    void GenerateCurrency()
    {
        if (currencyTarget == null || stageController == null)
            return;

        if (!IsAtColorB)
        {
            currencyBuffer = 0f;
            return;
        }

        float rate = GetCurrencyPerSecond(stageController.CurrentStageId);
        if (rate <= 0f)
        {
            currencyBuffer = 0f;
            return;
        }

        currencyBuffer += rate * Time.deltaTime;

        if (currencyBuffer < 1f)
            return;

        int wholeCurrency = Mathf.FloorToInt(currencyBuffer);
        currencyBuffer -= wholeCurrency;
        currencyTarget.AddValue(wholeCurrency);
    }

    float GetCurrencyPerSecond(string stageId)
    {
        switch (stageId)
        {
            case "DayTop":
                switch (sideType)
                {
                    case SideType.Right:
                        return 2f;
                    case SideType.Left:
                    case SideType.None:
                        return 1f;
                    default:
                        return 0f;
                }

            case "DayBottom":
                switch (sideType)
                {
                    case SideType.Left:
                        return 2f;
                    case SideType.Right:
                    case SideType.None:
                        return 1f;
                    default:
                        return 0f;
                }

            case "Night":
            case "BeforeNight":
            default:
                return 0f;
        }
    }

    void ApplyColor()
    {
        // 均匀渐变（线性）
        sr.color = Color.Lerp(colorA, colorB, t);
    }

    void OnMouseEnter()
    {
        isHovering = true;

        if (hoverCursorTexture != null)
            Cursor.SetCursor(hoverCursorTexture, cursorHotspot, cursorMode);
    }

    void OnMouseExit()
    {
        isHovering = false;
        Cursor.SetCursor(null, Vector2.zero, cursorMode);
    }

    void AutoAssignReferences()
    {
        if (stageController == null)
            stageController = FindFirstObjectByType<StageCycleController>();

        if (currencyTarget == null)
            currencyTarget = FindFirstObjectByType<TimeCounterUI>();
    }

    public void ApplyScrollDelta(float scroll)
    {
        if (Mathf.Abs(scroll) < 0.0001f) return;

        if (invertScroll) scroll *= -1f;

        float dir = Mathf.Sign(scroll);
        SetProgress(t + dir * stepPerScroll);
    }

    public void SetProgress(float progress)
    {
        t = Mathf.Clamp01(progress);
        ApplyColor();
    }
}

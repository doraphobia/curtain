using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Graphic))]
public sealed class HeadingPointUiHoverEffect : MonoBehaviour
{
    [SerializeField] private Graphic targetGraphic;
    [SerializeField] private Color normalColor = new Color(0f, 0f, 0f, 0.32f);
    [SerializeField] private Color hoverColor = new Color(1f, 1f, 1f, 0.24f);
    [SerializeField] private float blendSpeed = 10f;

    private RectTransform rectTransform;
    private Canvas parentCanvas;
    private float hoverBlend;

    public static void Ensure(Graphic graphic, Color normal, Color hover, float speed = 10f)
    {
        if (graphic == null)
            return;

        HeadingPointUiHoverEffect effect = graphic.GetComponent<HeadingPointUiHoverEffect>();
        if (effect == null)
            effect = graphic.gameObject.AddComponent<HeadingPointUiHoverEffect>();

        effect.Configure(graphic, normal, hover, speed);
    }

    public void Configure(Graphic graphic, Color normal, Color hover, float speed = 10f)
    {
        targetGraphic = graphic;
        normalColor = normal;
        hoverColor = hover;
        blendSpeed = Mathf.Max(0.01f, speed);
        hoverBlend = 0f;
        ApplyColor();
    }

    private void Awake()
    {
        if (targetGraphic == null)
            targetGraphic = GetComponent<Graphic>();

        rectTransform = transform as RectTransform;
        parentCanvas = GetComponentInParent<Canvas>();
    }

    private void OnEnable()
    {
        hoverBlend = 0f;
        ApplyColor();
    }

    private void Update()
    {
        float target = IsHeadingPointInside() ? 1f : 0f;
        hoverBlend = Mathf.MoveTowards(hoverBlend, target, blendSpeed * Time.unscaledDeltaTime);
        ApplyColor();
    }

    private bool IsHeadingPointInside()
    {
        if (rectTransform == null || !isActiveAndEnabled)
            return false;

        Vector2 screenPoint = PlayerControl.Active != null
            ? PlayerControl.Active.HeadingScreenPosition
            : (Vector2)Input.mousePosition;

        Camera eventCamera = parentCanvas != null && parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? parentCanvas.worldCamera
            : null;

        return RectTransformUtility.RectangleContainsScreenPoint(rectTransform, screenPoint, eventCamera);
    }

    private void ApplyColor()
    {
        if (targetGraphic == null)
            return;

        targetGraphic.color = Color.Lerp(normalColor, hoverColor, hoverBlend);
    }
}

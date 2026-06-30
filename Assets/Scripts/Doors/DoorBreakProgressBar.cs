using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// World-space progress bar shown above a <see cref="BreakableExteriorDoor"/> while it is being broken.
/// </summary>
[DisallowMultipleComponent]
public class DoorBreakProgressBar : MonoBehaviour
{
    [SerializeField] private Canvas canvas;
    [SerializeField] private Image fillImage;
    [SerializeField] private Image backgroundImage;
    [Min(0.01f)]
    public float smoothSpeed = 8f;

    private float displayedProgress;
    private float targetProgress;
    private bool visible;

    public static DoorBreakProgressBar CreateDefault(Transform parent, Vector3 localOffset)
    {
        GameObject root = new GameObject("Door Break Progress Bar");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localOffset;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;

        DoorBreakProgressBar bar = root.AddComponent<DoorBreakProgressBar>();
        bar.BuildDefaultVisual();
        return bar;
    }

    void LateUpdate()
    {
        if (fillImage == null)
            return;

        displayedProgress = Mathf.Lerp(displayedProgress, targetProgress, 1f - Mathf.Exp(-smoothSpeed * Time.deltaTime));
        fillImage.fillAmount = displayedProgress;

        if (canvas != null)
            canvas.enabled = visible;
    }

    public void SetProgress(float normalized, bool show)
    {
        targetProgress = Mathf.Clamp01(normalized);
        SetVisible(show || normalized > 0f);
    }

    public void SetVisible(bool value)
    {
        visible = value;
        if (canvas != null)
            canvas.enabled = visible;
    }

    private void BuildDefaultVisual()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 1200;

        RectTransform canvasRect = canvas.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(1.4f, 0.22f);
        canvasRect.localScale = Vector3.one * 0.01f;

        GameObject backgroundObject = new GameObject("Background");
        backgroundObject.transform.SetParent(transform, false);
        backgroundImage = backgroundObject.AddComponent<Image>();
        backgroundImage.color = new Color(0f, 0f, 0f, 0.65f);
        RectTransform backgroundRect = backgroundImage.rectTransform;
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.one;
        backgroundRect.offsetMin = Vector2.zero;
        backgroundRect.offsetMax = Vector2.zero;

        GameObject fillObject = new GameObject("Fill");
        fillObject.transform.SetParent(transform, false);
        fillImage = fillObject.AddComponent<Image>();
        fillImage.color = new Color(1f, 0.35f, 0.15f, 0.95f);
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        RectTransform fillRect = fillImage.rectTransform;
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = new Vector2(2f, 2f);
        fillRect.offsetMax = new Vector2(-2f, -2f);

        SetVisible(false);
    }
}

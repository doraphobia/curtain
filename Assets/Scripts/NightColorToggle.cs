using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class NightColorToggle : MonoBehaviour
{
    [Header("References")]
    public StageCycleController stageController;
    public SpriteRenderer targetSpriteRenderer;
    public Image targetImage;
    public TMP_Text targetText;

    [Header("Colors")]
    public Color dayColor = Color.white;
    public Color nightColor = Color.gray;

    void Awake()
    {
        if (stageController == null)
            stageController = FindFirstObjectByType<StageCycleController>();

        if (targetSpriteRenderer == null)
            targetSpriteRenderer = GetComponent<SpriteRenderer>();

        if (targetImage == null)
            targetImage = GetComponent<Image>();

        if (targetText == null)
            targetText = GetComponent<TMP_Text>();
    }

    void Start()
    {
        ApplyCurrentColor();
    }

    void Update()
    {
        ApplyCurrentColor();
    }

    private void ApplyCurrentColor()
    {
        bool isNight = stageController != null &&
                       string.Equals(stageController.CurrentStageId, "Night", System.StringComparison.Ordinal);

        Color targetColor = isNight ? nightColor : dayColor;

        if (targetSpriteRenderer != null)
        {
            Color color = targetSpriteRenderer.color;
            color.r = targetColor.r;
            color.g = targetColor.g;
            color.b = targetColor.b;
            targetSpriteRenderer.color = color;
        }

        if (targetImage != null)
        {
            Color color = targetImage.color;
            color.r = targetColor.r;
            color.g = targetColor.g;
            color.b = targetColor.b;
            targetImage.color = color;
        }

        if (targetText != null)
        {
            Color color = targetText.color;
            color.r = targetColor.r;
            color.g = targetColor.g;
            color.b = targetColor.b;
            targetText.color = color;
        }
    }
}

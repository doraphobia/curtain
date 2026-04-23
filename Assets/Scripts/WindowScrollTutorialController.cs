using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class WindowScrollTutorialController : MonoBehaviour
{
    [Header("References")]
    public GameObject tutorialRoot;
    public TMP_Text tutorialText;
    public Image tutorialImage;
    public CanvasGroup tutorialCanvasGroup;

    [Header("Content")]
    [TextArea]
    public string tutorialMessage = "Use the mouse wheel on a window to change its color.";
    public Sprite tutorialSprite;

    [Header("Blink")]
    public bool blinkAlpha = true;
    [Min(0.01f)]
    public float blinkSpeed = 2f;
    [Range(0f, 1f)]
    public float minAlpha = 0.35f;
    [Range(0f, 1f)]
    public float maxAlpha = 1f;

    [Header("Window Search")]
    public HoverScrollColorLerp2D[] windows;
    public bool useWindowTag = true;
    public string windowTag = "Window";
    public bool autoFindWindows = true;

    private readonly Dictionary<HoverScrollColorLerp2D, float> initialProgress = new Dictionary<HoverScrollColorLerp2D, float>();
    private bool tutorialCompleted;

    void Start()
    {
        RefreshWindowList();
        CacheInitialProgress();
        ApplyContent();
        SetupCanvasGroup();
        SetTutorialVisible(true);
    }

    void Update()
    {
        if (tutorialCompleted)
            return;

        UpdateBlink();

        if (autoFindWindows || useWindowTag)
            RefreshWindowList();

        if (!HasAnyWindowReachedColorB())
            return;

        tutorialCompleted = true;
        SetTutorialVisible(false);
    }

    private void ApplyContent()
    {
        if (tutorialText != null)
            tutorialText.text = tutorialMessage;

        if (tutorialImage != null)
            tutorialImage.sprite = tutorialSprite;
    }

    private void SetupCanvasGroup()
    {
        if (tutorialCanvasGroup == null && tutorialRoot != null)
            tutorialCanvasGroup = tutorialRoot.GetComponent<CanvasGroup>();

        if (tutorialCanvasGroup == null && tutorialRoot != null)
            tutorialCanvasGroup = tutorialRoot.AddComponent<CanvasGroup>();

        if (tutorialCanvasGroup != null)
            tutorialCanvasGroup.alpha = maxAlpha;
    }

    private void SetTutorialVisible(bool visible)
    {
        if (tutorialRoot != null)
            tutorialRoot.SetActive(visible);

        if (tutorialCanvasGroup != null)
            tutorialCanvasGroup.alpha = visible ? maxAlpha : 0f;
    }

    private void RefreshWindowList()
    {
        if (useWindowTag && !string.IsNullOrWhiteSpace(windowTag))
        {
            GameObject[] taggedObjects = GameObject.FindGameObjectsWithTag(windowTag);
            List<HoverScrollColorLerp2D> taggedWindows = new List<HoverScrollColorLerp2D>();

            for (int i = 0; i < taggedObjects.Length; i++)
            {
                HoverScrollColorLerp2D hover = taggedObjects[i].GetComponent<HoverScrollColorLerp2D>();
                if (hover != null)
                    taggedWindows.Add(hover);
            }

            windows = taggedWindows.ToArray();
            return;
        }

        if (autoFindWindows)
            windows = FindObjectsByType<HoverScrollColorLerp2D>(FindObjectsSortMode.None);
    }

    private void CacheInitialProgress()
    {
        initialProgress.Clear();

        if (windows == null)
            return;

        for (int i = 0; i < windows.Length; i++)
        {
            HoverScrollColorLerp2D window = windows[i];
            if (window == null)
                continue;

            initialProgress[window] = window.ColorProgress;
        }
    }

    private bool HasAnyWindowReachedColorB()
    {
        if (windows == null || windows.Length == 0)
            return false;

        for (int i = 0; i < windows.Length; i++)
        {
            HoverScrollColorLerp2D window = windows[i];
            if (window == null)
                continue;

            if (window.IsAtColorB)
                return true;
        }

        return false;
    }

    private void UpdateBlink()
    {
        if (!blinkAlpha || tutorialCanvasGroup == null || tutorialRoot == null || !tutorialRoot.activeInHierarchy)
            return;

        float t = (Mathf.Sin(Time.unscaledTime * blinkSpeed * Mathf.PI * 2f) + 1f) * 0.5f;
        tutorialCanvasGroup.alpha = Mathf.Lerp(minAlpha, maxAlpha, t);
    }
}

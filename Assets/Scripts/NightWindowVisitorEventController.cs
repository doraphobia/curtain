using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class NightWindowVisitorEventController : MonoBehaviour
{
    [Serializable]
    public class VisitorSentence
    {
        [TextArea]
        public string text;
        public bool isGood;
    }

    [Header("References")]
    public StageCycleController stageController;
    public HoverScrollColorLerp2D[] windows;
    public bool useWindowTag = true;
    public string windowTag = "Window";
    public Camera targetCamera;
    public Canvas targetCanvas;
    public RectTransform eventPanel;
    public GameObject dialogueRoot;
    public TMP_Text sentenceText;
    public Button letInButton;
    public Button ignoreButton;
    public TimeCounterUI currencySource;
    public SanitySystem sanitySystem;
    public AudioSource eventAudioSource;

    [Header("Gameplay")]
    public int goodVisitorReward = 10;
    public bool autoFindWindows = true;
    public bool hidePanelWhenWindowLeavesScreen = true;

    [Header("Text")]
    public string noSentenceFallback = "...";

    [Header("Audio")]
    public AudioClip eventTriggerClip;
    [Range(0f, 1f)]
    public float eventTriggerVolume = 1f;

    [Header("Sentence Library")]
    public List<VisitorSentence> sentenceLibrary = new List<VisitorSentence>();

    private HoverScrollColorLerp2D activeWindow;
    private WindowEventAnchor activeAnchor;
    private VisitorSentence activeSentence;
    private bool isEventActive;
    private bool hasTriggeredThisNight;
    private bool hasRevealedDialogue;
    private bool wasNightLastFrame;

    public bool IsEventActive => isEventActive;
    public bool HasEventTonight => isEventActive;

    void Awake()
    {
        RefreshWindowList();
    }

    void Start()
    {
        if (stageController == null)
            stageController = FindFirstObjectByType<StageCycleController>();

        if (targetCamera == null)
            targetCamera = Camera.main;

        if (targetCanvas == null)
            targetCanvas = eventPanel != null ? eventPanel.GetComponentInParent<Canvas>() : FindFirstObjectByType<Canvas>();

        if (currencySource == null)
            currencySource = FindFirstObjectByType<TimeCounterUI>();

        if (sanitySystem == null)
            sanitySystem = FindFirstObjectByType<SanitySystem>();

        if (letInButton != null)
        {
            letInButton.onClick.RemoveListener(HandleLetInClicked);
            letInButton.onClick.AddListener(HandleLetInClicked);
        }

        if (ignoreButton != null)
        {
            ignoreButton.onClick.RemoveListener(HandleIgnoreClicked);
            ignoreButton.onClick.AddListener(HandleIgnoreClicked);
        }

        SetPanelVisible(false);
        SetDialogueVisible(false);
        ClearDialogueText();
    }

    void Update()
    {
        if (stageController == null)
            return;

        bool isNight = string.Equals(stageController.CurrentStageId, "Night", StringComparison.Ordinal);

        if (isNight && !wasNightLastFrame)
            HandleNightStarted();

        if (!isNight && wasNightLastFrame)
            EndEvent();

        wasNightLastFrame = isNight;

        if (!isNight)
            return;

        if (!hasTriggeredThisNight)
            TryStartNightEvent();

        if (!isEventActive)
            return;

        RefreshWindowListIfNeeded();
        UpdatePanelPosition();
        UpdateDialogueState();
    }

    public void HandleLetInClicked()
    {
        if (!isEventActive || activeSentence == null || !hasRevealedDialogue)
            return;

        GameRunStats.Instance.RecordEventChoice(activeSentence.isGood);

        if (activeSentence.isGood)
        {
            if (currencySource != null)
                currencySource.AddValue(goodVisitorReward);
        }
        else if (sanitySystem != null)
        {
            sanitySystem.ApplyHalfSanityPenalty();
        }

        EndEvent();
    }

    public void HandleIgnoreClicked()
    {
        if (!isEventActive)
            return;

        if (activeSentence != null && hasRevealedDialogue)
            GameRunStats.Instance.RecordEventChoice(!activeSentence.isGood);

        EndEvent();
    }

    private void HandleNightStarted()
    {
        hasTriggeredThisNight = false;
    }

    private void TryStartNightEvent()
    {
        hasTriggeredThisNight = true;
        RefreshWindowListIfNeeded();

        List<HoverScrollColorLerp2D> candidates = GetEligibleWindows();
        if (candidates.Count == 0)
            return;

        activeWindow = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        activeAnchor = activeWindow != null ? activeWindow.GetComponent<WindowEventAnchor>() : null;
        activeSentence = PickRandomSentence();
        isEventActive = true;
        hasRevealedDialogue = false;

        PlayEventTriggerSound();
        SetPanelVisible(true);
        ClearDialogueText();
        SetDialogueVisible(false);
        UpdatePanelPosition();
        UpdateDialogueState();
    }

    private void UpdateDialogueState()
    {
        if (activeWindow == null)
        {
            EndEvent();
            return;
        }

        if (hasRevealedDialogue)
            return;

        if (!activeWindow.IsAtColorB)
        {
            SetDialogueVisible(false);
            ClearDialogueText();
            return;
        }

        hasRevealedDialogue = true;
        SetDialogueVisible(true);

        if (sentenceText != null)
            sentenceText.text = activeSentence != null && !string.IsNullOrWhiteSpace(activeSentence.text)
                ? activeSentence.text
                : noSentenceFallback;
    }

    private void UpdatePanelPosition()
    {
        if (eventPanel == null || targetCamera == null || activeWindow == null)
            return;

        Transform anchor = activeAnchor != null ? activeAnchor.GetAnchor() : activeWindow.transform;
        Vector3 viewportPoint = targetCamera.WorldToViewportPoint(anchor.position);
        bool isVisible = viewportPoint.z > 0f &&
                         viewportPoint.x >= 0f && viewportPoint.x <= 1f &&
                         viewportPoint.y >= 0f && viewportPoint.y <= 1f;

        if (hidePanelWhenWindowLeavesScreen && !isVisible)
        {
            eventPanel.gameObject.SetActive(false);
            return;
        }

        if (!eventPanel.gameObject.activeSelf)
            eventPanel.gameObject.SetActive(true);

        Vector3 screenPoint = targetCamera.WorldToScreenPoint(anchor.position);
        RectTransform canvasRect = targetCanvas != null ? targetCanvas.transform as RectTransform : null;
        Camera uiCamera = ResolveCanvasCamera();

        if (canvasRect != null &&
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, uiCamera, out Vector2 localPoint))
        {
            eventPanel.anchoredPosition = localPoint;
        }
        else
        {
            eventPanel.position = screenPoint;
        }
    }

    private Camera ResolveCanvasCamera()
    {
        if (targetCanvas == null)
            return null;

        if (targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
            return null;

        return targetCanvas.worldCamera != null ? targetCanvas.worldCamera : targetCamera;
    }

    private List<HoverScrollColorLerp2D> GetEligibleWindows()
    {
        List<HoverScrollColorLerp2D> result = new List<HoverScrollColorLerp2D>();
        if (windows == null || windows.Length == 0 || targetCamera == null)
            return result;

        for (int i = 0; i < windows.Length; i++)
        {
            HoverScrollColorLerp2D window = windows[i];
            if (!IsWindowEligible(window))
                continue;

            result.Add(window);
        }

        return result;
    }

    private bool IsWindowEligible(HoverScrollColorLerp2D window)
    {
        if (window == null || !window.enabled || !window.gameObject.activeInHierarchy)
            return false;

        if (!window.IsAtColorA && !window.IsAtColorB)
            return false;

        Renderer renderer = window.GetComponent<Renderer>();
        Collider2D collider2D = window.GetComponent<Collider2D>();
        if (renderer == null && collider2D == null)
            return false;

        Transform anchor = window.transform;
        WindowEventAnchor anchorComponent = window.GetComponent<WindowEventAnchor>();
        if (anchorComponent != null && anchorComponent.GetAnchor() != null)
            anchor = anchorComponent.GetAnchor();

        Vector3 viewportPoint = targetCamera.WorldToViewportPoint(anchor.position);
        return viewportPoint.z > 0f &&
               viewportPoint.x >= 0f && viewportPoint.x <= 1f &&
               viewportPoint.y >= 0f && viewportPoint.y <= 1f;
    }

    private VisitorSentence PickRandomSentence()
    {
        if (sentenceLibrary == null || sentenceLibrary.Count == 0)
            return null;

        int index = UnityEngine.Random.Range(0, sentenceLibrary.Count);
        return sentenceLibrary[index];
    }

    private void PlayEventTriggerSound()
    {
        if (eventTriggerClip == null)
            return;

        if (eventAudioSource != null)
        {
            eventAudioSource.PlayOneShot(eventTriggerClip, eventTriggerVolume);
            return;
        }

        Vector3 playPosition = targetCamera != null ? targetCamera.transform.position : transform.position;
        AudioSource.PlayClipAtPoint(eventTriggerClip, playPosition, eventTriggerVolume);
    }

    private void EndEvent()
    {
        isEventActive = false;
        hasRevealedDialogue = false;
        activeWindow = null;
        activeAnchor = null;
        activeSentence = null;

        SetPanelVisible(false);
        SetDialogueVisible(false);
        ClearDialogueText();
    }

    private void RefreshWindowListIfNeeded()
    {
        if (autoFindWindows || useWindowTag)
            RefreshWindowList();
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

    private void SetPanelVisible(bool visible)
    {
        if (eventPanel != null)
            eventPanel.gameObject.SetActive(visible);
    }

    private void SetDialogueVisible(bool visible)
    {
        if (dialogueRoot != null)
        {
            dialogueRoot.SetActive(visible);
            return;
        }

        if (sentenceText != null)
            sentenceText.gameObject.SetActive(visible);

        if (letInButton != null)
            letInButton.gameObject.SetActive(visible);

        if (ignoreButton != null)
            ignoreButton.gameObject.SetActive(visible);
    }

    private void ClearDialogueText()
    {
        if (sentenceText != null)
            sentenceText.text = string.Empty;
    }
}

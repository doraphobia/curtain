using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DuoCurtain.RuntimeTileMesh;

[DisallowMultipleComponent]
public class NightWindowEventController : MonoBehaviour
{
    [Serializable]
    public class EventSentence
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
    public TimeCounterUI currencySource;
    public SanitySystem sanitySystem;
    public FusionSanityController fusionSanitySystem;
    public GameObject eventPanel;
    public GameObject leftSideIndicator;
    public GameObject rightSideIndicator;
    public TMP_Text promptText;
    public TMP_Text sentenceText;
    public Button openDoorButton;

    [Header("Gameplay")]
    public int goodVisitorReward = 10;
    [Range(0f, 1f)]
    public float halfOpenMin = 0.4f;
    [Range(0f, 1f)]
    public float halfOpenMax = 0.6f;
    [Range(0f, 1f)]
    public float fullyOpenThreshold = 0.9f;
    public bool autoFindWindows = true;

    [Header("Text")]
    public string leftWindowPrompt = "有人在左边敲窗。把任意左窗开到一半看看。";
    public string rightWindowPrompt = "有人在右边敲窗。把任意右窗开到一半看看。";
    public string closeWindowPrompt = "看完后把窗户关上，时间才会继续。";
    public string noSentenceFallback = "门外传来声音。";

    [Header("Sentence Library")]
    public List<EventSentence> sentenceLibrary = new List<EventSentence>();

    private HoverScrollColorLerp2D.SideType activeSide = HoverScrollColorLerp2D.SideType.None;
    private EventSentence activeSentence;
    private bool isEventActive;
    private bool hasEventTonight;
    private bool sentenceRevealed;
    private bool triggeredThisNight;
    private bool rewardGranted;
    private bool pendingSanityPenalty;
    private HoverScrollColorLerp2D inspectedWindow;
    private bool wasNightLastFrame;
    private int nightCount;

    public bool IsEventActive => isEventActive;
    public bool HasEventTonight => hasEventTonight;

    void Awake()
    {
        RefreshWindowList();
    }

    void Start()
    {
        if (stageController == null)
            stageController = FindFirstObjectByType<StageCycleController>();

        if (currencySource == null)
            currencySource = FindFirstObjectByType<TimeCounterUI>();

        if (sanitySystem == null)
            sanitySystem = FindFirstObjectByType<SanitySystem>();
        if (fusionSanitySystem == null)
            fusionSanitySystem = FusionSanityController.Active != null
                ? FusionSanityController.Active
                : FindFirstObjectByType<FusionSanityController>();

        SetPanelVisible(false);
        SetSideIndicatorsVisible(HoverScrollColorLerp2D.SideType.None);
        SetSentenceVisible(false);

        if (openDoorButton != null)
        {
            openDoorButton.onClick.RemoveListener(HandleOpenDoorClicked);
            openDoorButton.onClick.AddListener(HandleOpenDoorClicked);
            openDoorButton.gameObject.SetActive(false);
        }
    }

    void Update()
    {
        if (stageController == null)
            return;

        bool isNight = stageController.IsNight;

        if (isNight && !wasNightLastFrame)
            HandleNightStarted();

        wasNightLastFrame = isNight;

        if (!isNight)
        {
            triggeredThisNight = false;
            hasEventTonight = false;

            if (isEventActive)
                EndEvent(applyPenalty: false);

            return;
        }

        if (!hasEventTonight)
            return;

        if (!isEventActive && !triggeredThisNight)
        {
            StartNightEvent();
            return;
        }

        if (isEventActive)
            UpdateActiveEvent();
    }

    public void HandleOpenDoorClicked()
    {
        if (!isEventActive || !sentenceRevealed || activeSentence == null)
            return;

        if (activeSentence.isGood)
        {
            if (!rewardGranted && currencySource != null)
            {
                currencySource.AddValue(goodVisitorReward);
                rewardGranted = true;
            }
        }
        else
        {
            pendingSanityPenalty = true;
        }

        if (openDoorButton != null)
            openDoorButton.gameObject.SetActive(false);

        if (promptText != null)
            promptText.text = closeWindowPrompt;
    }

    private void HandleNightStarted()
    {
        nightCount++;
        hasEventTonight = nightCount % 2 == 0;
        triggeredThisNight = false;
    }

    private void StartNightEvent()
    {
        triggeredThisNight = true;
        isEventActive = true;
        sentenceRevealed = false;
        rewardGranted = false;
        pendingSanityPenalty = false;
        activeSide = UnityEngine.Random.value < 0.5f ? HoverScrollColorLerp2D.SideType.Left : HoverScrollColorLerp2D.SideType.Right;
        activeSentence = PickRandomSentence();
        inspectedWindow = null;

        stageController.SetPaused(true);
        SetPanelVisible(true);
        SetSideIndicatorsVisible(activeSide);

        if (promptText != null)
            promptText.text = activeSide == HoverScrollColorLerp2D.SideType.Left ? leftWindowPrompt : rightWindowPrompt;

        if (sentenceText != null)
            sentenceText.text = string.Empty;

        SetSentenceVisible(false);

        if (openDoorButton != null)
            openDoorButton.gameObject.SetActive(false);
    }

    private void UpdateActiveEvent()
    {
        HoverScrollColorLerp2D[] matchingWindows = GetMatchingWindows();
        if (matchingWindows.Length == 0)
            return;

        if (AnyWindowFullyOpen(matchingWindows))
            pendingSanityPenalty = true;

        if (!sentenceRevealed)
        {
            HoverScrollColorLerp2D halfOpenWindow = GetHalfOpenWindow(matchingWindows);
            if (halfOpenWindow == null)
                return;

            inspectedWindow = halfOpenWindow;
            RevealSentence();
            return;
        }

        if (inspectedWindow != null && inspectedWindow.IsAtColorA)
            EndEvent(applyPenalty: pendingSanityPenalty);
    }

    private void RevealSentence()
    {
        sentenceRevealed = true;

        if (sentenceText != null)
            sentenceText.text = activeSentence != null && !string.IsNullOrWhiteSpace(activeSentence.text)
                ? activeSentence.text
                : noSentenceFallback;

        SetSentenceVisible(true);

        if (promptText != null)
            promptText.text = closeWindowPrompt;

        if (openDoorButton != null)
            openDoorButton.gameObject.SetActive(true);
    }

    private void EndEvent(bool applyPenalty)
    {
        if (applyPenalty)
        {
            if (fusionSanitySystem == null)
                fusionSanitySystem = FusionSanityController.Active != null
                    ? FusionSanityController.Active
                    : FindFirstObjectByType<FusionSanityController>();

            if (fusionSanitySystem != null)
                fusionSanitySystem.ApplyHalfSanityPenalty();
            else if (sanitySystem != null)
                sanitySystem.ApplyHalfSanityPenalty();
        }

        isEventActive = false;
        sentenceRevealed = false;
        rewardGranted = false;
        pendingSanityPenalty = false;
        activeSide = HoverScrollColorLerp2D.SideType.None;
        activeSentence = null;
        inspectedWindow = null;

        if (stageController != null)
            stageController.SetPaused(false);

        SetPanelVisible(false);
        SetSideIndicatorsVisible(HoverScrollColorLerp2D.SideType.None);
        SetSentenceVisible(false);
    }

    private HoverScrollColorLerp2D[] GetMatchingWindows()
    {
        if (autoFindWindows || useWindowTag)
            RefreshWindowList();

        if (windows == null || windows.Length == 0)
            return Array.Empty<HoverScrollColorLerp2D>();

        List<HoverScrollColorLerp2D> result = new List<HoverScrollColorLerp2D>();
        for (int i = 0; i < windows.Length; i++)
        {
            HoverScrollColorLerp2D window = windows[i];
            if (window == null || window.sideType != activeSide)
                continue;

            result.Add(window);
        }

        return result.ToArray();
    }

    private HoverScrollColorLerp2D GetHalfOpenWindow(HoverScrollColorLerp2D[] matchingWindows)
    {
        for (int i = 0; i < matchingWindows.Length; i++)
        {
            float progress = matchingWindows[i].ColorProgress;
            if (progress >= halfOpenMin && progress <= halfOpenMax)
                return matchingWindows[i];
        }

        return null;
    }

    private bool AnyWindowFullyOpen(HoverScrollColorLerp2D[] matchingWindows)
    {
        for (int i = 0; i < matchingWindows.Length; i++)
        {
            if (matchingWindows[i].ColorProgress >= fullyOpenThreshold)
                return true;
        }

        return false;
    }
    private EventSentence PickRandomSentence()
    {
        if (sentenceLibrary == null || sentenceLibrary.Count == 0)
            return null;

        int index = UnityEngine.Random.Range(0, sentenceLibrary.Count);
        return sentenceLibrary[index];
    }

    private void SetPanelVisible(bool visible)
    {
        if (eventPanel != null)
            eventPanel.SetActive(visible);
    }

    private void SetSentenceVisible(bool visible)
    {
        if (sentenceText != null)
            sentenceText.gameObject.SetActive(visible);
    }

    private void RefreshWindowList()
    {
        windows = WindowQueryUtility.RefreshWindowList(windows, useWindowTag, windowTag, autoFindWindows);
    }

    private void SetSideIndicatorsVisible(HoverScrollColorLerp2D.SideType side)
    {
        if (leftSideIndicator != null)
            leftSideIndicator.SetActive(side == HoverScrollColorLerp2D.SideType.Left);

        if (rightSideIndicator != null)
            rightSideIndicator.SetActive(side == HoverScrollColorLerp2D.SideType.Right);
    }
}

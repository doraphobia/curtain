using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class FollowWorldButtonRewardUI : MonoBehaviour
{
    private enum RewardCycleState
    {
        WaitingForConstruction,
        ReadyToStart,
        CoolingDown,
        ReadyToCollect
    }

    [Header("References")]
    public Camera targetCamera;
    public Canvas targetCanvas;
    public GameObject buttonPrefab;
    public TimeCounterUI currencySource;
    public RoomConstructionController constructionController;

    [Header("Reward")]
    [Min(1f)]
    public float waitSeconds = 60f;
    public int rewardAmount = 10;

    [Header("Text")]
    public string startButtonText = "Start";
    public string collectButtonText = "Collect";

    [Header("Follow")]
    public Vector2 screenOffset;
    public bool hideWhenTargetOffScreen = true;

    private RectTransform buttonRect;
    private Button rewardButton;
    private TMP_Text countdownText;
    private float cooldownRemaining;
    private RewardCycleState currentState = RewardCycleState.WaitingForConstruction;

    void Awake()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        if (targetCanvas == null)
            targetCanvas = FindFirstObjectByType<Canvas>();

        if (currencySource == null)
            currencySource = FindFirstObjectByType<TimeCounterUI>();

        if (constructionController == null)
            constructionController = GetComponent<RoomConstructionController>();
    }

    void OnEnable()
    {
        EnsureButtonInstance();
        InitializeState();
        RefreshButtonVisualState();
        UpdateButtonPosition();
    }

    void OnDestroy()
    {
        if (buttonRect != null)
            Destroy(buttonRect.gameObject);
    }

    void Update()
    {
        UpdateConstructionState();
        UpdateCooldown();
        UpdateButtonPosition();
        RefreshButtonVisualState();
    }

    private void HandleRewardButtonClicked()
    {
        switch (currentState)
        {
            case RewardCycleState.ReadyToStart:
                currentState = RewardCycleState.CoolingDown;
                cooldownRemaining = waitSeconds;
                break;

            case RewardCycleState.ReadyToCollect:
                if (currencySource != null)
                    currencySource.AddValue(rewardAmount);

                currentState = RewardCycleState.ReadyToStart;
                break;
        }

        RefreshButtonVisualState();
    }

    private void UpdateCooldown()
    {
        if (currentState != RewardCycleState.CoolingDown)
            return;

        cooldownRemaining -= Time.deltaTime;
        if (cooldownRemaining > 0f)
            return;

        cooldownRemaining = 0f;
        currentState = RewardCycleState.ReadyToCollect;
    }

    private void UpdateButtonPosition()
    {
        if (buttonRect == null || targetCamera == null)
            return;

        Vector3 screenPoint = targetCamera.WorldToScreenPoint(transform.position);
        bool isVisible = screenPoint.z > 0f &&
                         screenPoint.x >= 0f && screenPoint.x <= Screen.width &&
                         screenPoint.y >= 0f && screenPoint.y <= Screen.height;

        bool shouldShowButton = currentState != RewardCycleState.WaitingForConstruction;

        if (!shouldShowButton)
        {
            if (buttonRect.gameObject.activeSelf)
                buttonRect.gameObject.SetActive(false);
            return;
        }

        if (hideWhenTargetOffScreen && !isVisible)
        {
            if (buttonRect.gameObject.activeSelf)
                buttonRect.gameObject.SetActive(false);
            return;
        }

        if (!buttonRect.gameObject.activeSelf)
            buttonRect.gameObject.SetActive(true);

        screenPoint.x += screenOffset.x;
        screenPoint.y += screenOffset.y;

        RectTransform canvasRect = targetCanvas != null ? targetCanvas.transform as RectTransform : null;
        Camera uiCamera = ResolveCanvasCamera();

        if (canvasRect != null &&
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, uiCamera, out Vector2 localPoint))
        {
            buttonRect.anchoredPosition = localPoint;
        }
        else
        {
            buttonRect.position = screenPoint;
        }
    }

    private void EnsureButtonInstance()
    {
        if (buttonRect != null)
            return;

        if (buttonPrefab == null || targetCanvas == null)
            return;

        GameObject buttonInstance = Instantiate(buttonPrefab, targetCanvas.transform);
        buttonInstance.name = buttonPrefab.name + "_" + gameObject.name;

        buttonRect = buttonInstance.GetComponent<RectTransform>();
        rewardButton = buttonInstance.GetComponent<Button>();
        countdownText = buttonInstance.GetComponentInChildren<TMP_Text>(true);

        if (rewardButton != null)
        {
            rewardButton.onClick.RemoveListener(HandleRewardButtonClicked);
            rewardButton.onClick.AddListener(HandleRewardButtonClicked);
        }
    }

    private void InitializeState()
    {
        if (constructionController != null && constructionController.IsConstructing)
        {
            currentState = RewardCycleState.WaitingForConstruction;
            cooldownRemaining = 0f;
            return;
        }

        if (currentState == RewardCycleState.WaitingForConstruction)
            currentState = RewardCycleState.ReadyToStart;
    }

    private void UpdateConstructionState()
    {
        if (constructionController == null)
        {
            if (currentState == RewardCycleState.WaitingForConstruction)
                currentState = RewardCycleState.ReadyToStart;
            return;
        }

        if (constructionController.IsConstructing)
        {
            currentState = RewardCycleState.WaitingForConstruction;
            cooldownRemaining = 0f;
            return;
        }

        if (currentState == RewardCycleState.WaitingForConstruction)
            currentState = RewardCycleState.ReadyToStart;
    }

    private Camera ResolveCanvasCamera()
    {
        if (targetCanvas == null)
            return null;

        if (targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
            return null;

        return targetCanvas.worldCamera != null ? targetCanvas.worldCamera : targetCamera;
    }

    private void RefreshButtonVisualState()
    {
        if (rewardButton != null)
            rewardButton.interactable = currentState == RewardCycleState.ReadyToStart || currentState == RewardCycleState.ReadyToCollect;

        if (countdownText == null)
            return;

        switch (currentState)
        {
            case RewardCycleState.ReadyToStart:
                countdownText.text = startButtonText;
                break;

            case RewardCycleState.ReadyToCollect:
                countdownText.text = collectButtonText;
                break;

            case RewardCycleState.CoolingDown:
                countdownText.text = Mathf.CeilToInt(cooldownRemaining).ToString();
                break;

            default:
                countdownText.text = string.Empty;
                break;
        }
    }
}

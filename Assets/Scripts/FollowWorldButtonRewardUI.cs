using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class FollowWorldButtonRewardUI : MonoBehaviour
{
    [Header("References")]
    public Camera targetCamera;
    public Canvas targetCanvas;
    public GameObject buttonPrefab;
    public TimeCounterUI currencySource;

    [Header("Reward")]
    [Min(1f)]
    public float waitSeconds = 60f;
    public int rewardAmount = 10;

    [Header("Follow")]
    public Vector2 screenOffset;
    public bool hideWhenTargetOffScreen = true;

    private RectTransform buttonRect;
    private Button rewardButton;
    private TMP_Text countdownText;
    private float cooldownRemaining;
    private bool isCoolingDown;

    void Awake()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        if (targetCanvas == null)
            targetCanvas = FindFirstObjectByType<Canvas>();

        if (currencySource == null)
            currencySource = FindFirstObjectByType<TimeCounterUI>();
    }

    void OnEnable()
    {
        EnsureButtonInstance();
        RefreshButtonInteractable();
        RefreshCountdownLabel();
        UpdateButtonPosition();
    }

    void OnDestroy()
    {
        if (buttonRect != null)
            Destroy(buttonRect.gameObject);
    }

    void Update()
    {
        UpdateButtonPosition();
        UpdateCooldown();
    }

    private void HandleRewardButtonClicked()
    {
        if (isCoolingDown)
            return;

        isCoolingDown = true;
        cooldownRemaining = waitSeconds;
        RefreshButtonInteractable();
        RefreshCountdownLabel();
    }

    private void UpdateCooldown()
    {
        if (!isCoolingDown)
            return;

        cooldownRemaining -= Time.deltaTime;
        if (cooldownRemaining > 0f)
        {
            RefreshCountdownLabel();
            return;
        }

        cooldownRemaining = 0f;
        isCoolingDown = false;

        if (currencySource != null)
            currencySource.AddValue(rewardAmount);

        RefreshButtonInteractable();
        RefreshCountdownLabel();
    }

    private void UpdateButtonPosition()
    {
        if (buttonRect == null || targetCamera == null)
            return;

        Vector3 screenPoint = targetCamera.WorldToScreenPoint(transform.position);
        bool isVisible = screenPoint.z > 0f &&
                         screenPoint.x >= 0f && screenPoint.x <= Screen.width &&
                         screenPoint.y >= 0f && screenPoint.y <= Screen.height;

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

    private Camera ResolveCanvasCamera()
    {
        if (targetCanvas == null)
            return null;

        if (targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
            return null;

        return targetCanvas.worldCamera != null ? targetCanvas.worldCamera : targetCamera;
    }

    private void RefreshButtonInteractable()
    {
        if (rewardButton != null)
            rewardButton.interactable = !isCoolingDown;
    }

    private void RefreshCountdownLabel()
    {
        if (countdownText == null)
            return;

        if (!isCoolingDown)
        {
            countdownText.text = string.Empty;
            return;
        }

        countdownText.text = Mathf.CeilToInt(cooldownRemaining).ToString();
    }
}

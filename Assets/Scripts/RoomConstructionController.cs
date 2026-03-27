using UnityEngine;
using TMPro;

[DisallowMultipleComponent]
public class RoomConstructionController : MonoBehaviour
{
    [Header("Timing")]
    [Min(0.1f)]
    public float buildDuration = 10f;
    public bool startConstructed = true;

    [Header("Indicator")]
    public GameObject indicatorRoot;
    public TMP_Text countdownText;
    public string countdownSuffix = "s";

    [Header("Blocked Interactions")]
    public HoverScrollColorLerp2D[] blockedInteractions;
    public bool autoFindBlockedInteractions = true;

    private float buildTimer;
    private bool isConstructing;
    private bool hasRuntimeStateBeenAssigned;

    public bool IsConstructing => isConstructing;
    public float Progress01 => isConstructing ? Mathf.Clamp01(buildTimer / Mathf.Max(0.1f, buildDuration)) : 1f;

    void Awake()
    {
        if ((blockedInteractions == null || blockedInteractions.Length == 0) && autoFindBlockedInteractions)
            blockedInteractions = GetComponentsInChildren<HoverScrollColorLerp2D>(true);
    }

    void Start()
    {
        if (hasRuntimeStateBeenAssigned)
            return;

        if (startConstructed)
        {
            SetInteractionsEnabled(true);
            SetIndicatorVisible(false);
            UpdateCountdownText(0f);
        }
        else
        {
            BeginConstruction();
        }
    }

    void Update()
    {
        if (!isConstructing)
            return;

        buildTimer += Time.deltaTime;
        UpdateCountdownText(Mathf.Max(0f, buildDuration - buildTimer));

        if (buildTimer < buildDuration)
            return;

        CompleteConstruction();
    }

    public void BeginConstruction()
    {
        hasRuntimeStateBeenAssigned = true;
        buildTimer = 0f;
        isConstructing = true;
        SetInteractionsEnabled(false);
        SetIndicatorVisible(true);
        UpdateCountdownText(buildDuration);
    }

    public void CompleteConstruction()
    {
        hasRuntimeStateBeenAssigned = true;
        isConstructing = false;
        buildTimer = buildDuration;
        SetInteractionsEnabled(true);
        UpdateCountdownText(0f);
        SetIndicatorVisible(false);
    }

    private void SetInteractionsEnabled(bool enabled)
    {
        if (blockedInteractions == null)
            return;

        for (int i = 0; i < blockedInteractions.Length; i++)
        {
            HoverScrollColorLerp2D interaction = blockedInteractions[i];
            if (interaction != null)
                interaction.enabled = enabled;
        }
    }

    private void SetIndicatorVisible(bool visible)
    {
        if (indicatorRoot != null)
            indicatorRoot.SetActive(visible);
    }

    private void UpdateCountdownText(float remainingSeconds)
    {
        if (countdownText == null)
            return;

        int seconds = Mathf.CeilToInt(Mathf.Max(0f, remainingSeconds));
        countdownText.text = seconds + countdownSuffix;
    }
}

using UnityEngine;
using System.Collections.Generic;

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
    public bool useLogicalCursorHover = true;

    [Header("Cursor")]
    public Texture2D hoverCursorTexture;
    public Vector2 cursorHotspot = Vector2.zero;
    public CursorMode cursorMode = CursorMode.Auto;

    [Header("Audio")]
    public AudioSource scrollAudioSource;
    public AudioClip scrollClip;
    [Range(0f, 1f)]
    public float scrollVolume = 1f;
    public bool useFoleyProfileForScroll = true;
    public FoleyPlayer scrollFoleyPlayer;
    public FoleyProfile scrollFoleyProfile;
    public string scrollFoleySurfaceId = "Curtain";

    [Header("Visual Sync")]
    public List<Transform> syncedRotationTargets = new List<Transform>();
    public float minSyncedXRotation = 0f;
    public float maxSyncedXRotation = 90f;
    public bool syncParentSpriteAlpha = true;
    public SpriteRenderer parentSpriteRenderer;
    [Range(0f, 1f)]
    public float parentAlphaAtColorA = 0f;
    [Range(0f, 1f)]
    public float parentAlphaAtColorB = 1f;
    public bool toggleParentColliderByAlpha = true;
    public Collider2D parentCollider2D;
    [Range(0f, 1f)]
    public float parentColliderVisibleAlphaThreshold = 0.01f;

    private SpriteRenderer sr;
    private Collider2D targetCollider;
    private float t = 0f; // 0 -> colorA, 1 -> colorB
    private bool isHovering = false;
    private float currencyBuffer = 0f;

    public float ColorProgress => t;
    public bool IsAtColorA => t <= 1f - colorBThreshold;
    public bool IsAtColorB => t >= colorBThreshold;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        targetCollider = GetComponent<Collider2D>();
        AutoAssignReferences();
        ApplyColor();
        ApplyParentSpriteAlpha();
        SetupScrollAudioSource();
    }

    void Update()
    {
        UpdateLogicalCursorHover();
        HandleScrollInput();
        GenerateCurrency();
    }

    void LateUpdate()
    {
        ApplyParentSpriteAlpha();
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
        if (useLogicalCursorHover && PlayerControl.HasActive)
            return;

        SetHovering(true);
    }

    void OnMouseExit()
    {
        if (useLogicalCursorHover && PlayerControl.HasActive)
            return;

        SetHovering(false);
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
        PlayScrollSound();
    }

    private void UpdateLogicalCursorHover()
    {
        if (!useLogicalCursorHover || targetCollider == null)
            return;

        if (!PlayerControl.TryGetInteractionWorldPosition(out Vector3 cursorWorld))
            return;

        cursorWorld.z = transform.position.z;
        SetHovering(targetCollider.OverlapPoint(cursorWorld));
    }

    private void SetHovering(bool hovering)
    {
        if (isHovering == hovering)
            return;

        isHovering = hovering;

        if (hoverCursorTexture == null)
            return;

        Cursor.SetCursor(isHovering ? hoverCursorTexture : null, isHovering ? cursorHotspot : Vector2.zero, cursorMode);
    }

    public void SetProgress(float progress)
    {
        t = Mathf.Clamp01(progress);
        ApplyColor();
        ApplySyncedRotations();
        ApplyParentSpriteAlpha();
    }

    void SetupScrollAudioSource()
    {
        ResolveScrollFoleyPlayer();

        if (scrollAudioSource == null)
            scrollAudioSource = GetComponent<AudioSource>();

        if (scrollAudioSource != null)
        {
            scrollAudioSource.playOnAwake = false;
            scrollAudioSource.loop = false;
        }
    }

    void PlayScrollSound()
    {
        if (TryPlayScrollFoley())
            return;

        if (scrollClip == null)
            return;

        if (scrollAudioSource != null)
        {
            if (scrollAudioSource.isPlaying)
                return;

            if (scrollAudioSource.clip != scrollClip)
                scrollAudioSource.clip = scrollClip;

            scrollAudioSource.volume = scrollVolume;
            scrollAudioSource.Play();
            return;
        }

        Vector3 playPosition = Camera.main != null ? Camera.main.transform.position : transform.position;
        AudioSource.PlayClipAtPoint(scrollClip, playPosition, scrollVolume);
    }

    private void ResolveScrollFoleyPlayer()
    {
        if (!useFoleyProfileForScroll || scrollFoleyPlayer != null || scrollFoleyProfile == null)
            return;

        scrollFoleyPlayer = GetComponent<FoleyPlayer>();
        if (scrollFoleyPlayer == null)
            scrollFoleyPlayer = gameObject.AddComponent<FoleyPlayer>();
    }

    private bool TryPlayScrollFoley()
    {
        if (!useFoleyProfileForScroll || scrollFoleyProfile == null)
            return false;

        ResolveScrollFoleyPlayer();
        if (scrollFoleyPlayer == null)
            return false;

        string surfaceId = string.IsNullOrWhiteSpace(scrollFoleySurfaceId) ? null : scrollFoleySurfaceId;
        return scrollFoleyPlayer.Play(scrollFoleyProfile, transform.position, scrollVolume, surfaceId);
    }

    void ApplySyncedRotations()
    {
        if (syncedRotationTargets == null || syncedRotationTargets.Count == 0)
            return;

        float xRotation = Mathf.Lerp(minSyncedXRotation, maxSyncedXRotation, t);

        for (int i = 0; i < syncedRotationTargets.Count; i++)
        {
            Transform target = syncedRotationTargets[i];
            if (target == null)
                continue;

            Vector3 euler = target.localEulerAngles;
            euler.x = xRotation;
            target.localEulerAngles = euler;
        }
    }

    void ApplyParentSpriteAlpha()
    {
        if (!syncParentSpriteAlpha)
            return;

        if (parentSpriteRenderer == null && transform.parent != null)
            parentSpriteRenderer = transform.parent.GetComponent<SpriteRenderer>();

        if (parentCollider2D == null && transform.parent != null)
            parentCollider2D = transform.parent.GetComponent<Collider2D>();

        if (parentSpriteRenderer == null)
            return;

        Color color = parentSpriteRenderer.color;
        float alpha = Mathf.Lerp(parentAlphaAtColorA, parentAlphaAtColorB, t);
        color.a = alpha;
        parentSpriteRenderer.color = color;

        if (toggleParentColliderByAlpha && parentCollider2D != null)
            parentCollider2D.enabled = alpha > parentColliderVisibleAlphaThreshold;
    }
}

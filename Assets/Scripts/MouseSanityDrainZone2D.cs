using UnityEngine;

[RequireComponent(typeof(Collider2D))]
[DisallowMultipleComponent]
public class MouseSanityDrainZone2D : MonoBehaviour
{
    [Header("References")]
    public Camera targetCamera;
    public SanitySystem sanitySystem;
    public Collider2D targetCollider;
    public StageCycleController stageController;
    public Renderer[] flickerRenderers;

    [Header("Drain")]
    public float sanityDrainPerSecond = 5f;

    [Header("Flicker")]
    public bool flickerWhenMouseInside = true;
    public float flickerInterval = 0.12f;

    private bool flickerVisibleState = true;
    private float nextFlickerTime;

    void Awake()
    {
        if (targetCollider == null)
            targetCollider = GetComponent<Collider2D>();

        if (targetCamera == null)
            targetCamera = Camera.main;

        if (sanitySystem == null)
            sanitySystem = FindFirstObjectByType<SanitySystem>();

        if (stageController == null)
            stageController = FindFirstObjectByType<StageCycleController>();

        if (flickerRenderers == null || flickerRenderers.Length == 0)
            flickerRenderers = GetComponentsInChildren<Renderer>(true);
    }

    void Update()
    {
        if (targetCollider == null || targetCamera == null || sanitySystem == null || stageController == null)
        {
            RestoreFlickerRenderers();
            return;
        }

        if (!stageController.IsNight)
        {
            RestoreFlickerRenderers();
            return;
        }

        if (!IsMouseInsideZone())
        {
            RestoreFlickerRenderers();
            return;
        }

        sanitySystem.DrainSanity(sanityDrainPerSecond * Time.deltaTime);
        UpdateRendererFlicker();
    }

    void OnDisable()
    {
        RestoreFlickerRenderers();
    }

    private bool IsMouseInsideZone()
    {
        Vector3 mouseWorld;
        if (!PlayerControl.TryGetPlayerWorldPosition(out mouseWorld))
            mouseWorld = targetCamera.ScreenToWorldPoint(Input.mousePosition);

        mouseWorld.z = transform.position.z;
        return targetCollider.OverlapPoint(mouseWorld);
    }

    private void UpdateRendererFlicker()
    {
        if (!flickerWhenMouseInside || flickerRenderers == null || flickerRenderers.Length == 0)
            return;

        if (Time.time >= nextFlickerTime)
        {
            flickerVisibleState = !flickerVisibleState;
            nextFlickerTime = Time.time + Mathf.Max(0.01f, flickerInterval);
            SetRendererEnabledState(flickerVisibleState);
        }
    }

    private void RestoreFlickerRenderers()
    {
        nextFlickerTime = 0f;
        flickerVisibleState = true;
        SetRendererEnabledState(true);
    }

    private void SetRendererEnabledState(bool isEnabled)
    {
        if (flickerRenderers == null)
            return;

        for (int i = 0; i < flickerRenderers.Length; i++)
        {
            if (flickerRenderers[i] != null)
                flickerRenderers[i].enabled = isEnabled;
        }
    }
}

using System.Collections;
using DuoCurtain.GameplayVisuals;
using UnityEngine;

/// <summary>
/// Per-spawned footprint visual. Uses instanced SpriteRenderer color; compatible with future shader renderers.
/// </summary>
[DisallowMultipleComponent]
public class FootprintInstance : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Renderer[] additionalRenderers;

    private FootprintVisualProfile profile;
    private Material[] instancedMaterials;
    private Color baseColor = Color.white;
    private float currentAlpha = 1f;
    private Coroutine animationRoutine;
    private GameplayVisualRenderer adaptiveVisualRenderer;

    public FootprintSide Side { get; private set; }
    public int DecayIndex { get; private set; }
    public bool IsLatest { get; private set; }
    public Color BaseColor => baseColor;

    void Awake()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        if (additionalRenderers == null || additionalRenderers.Length == 0)
        {
            Renderer[] found = GetComponentsInChildren<Renderer>(true);
            if (found.Length > 0 && (spriteRenderer == null || found.Length > 1))
                additionalRenderers = found;
        }

        adaptiveVisualRenderer = GameplayVisualRenderer.Ensure(
            gameObject,
            GameplayVisualPriority.EnemyFootprint);
        if (adaptiveVisualRenderer != null)
        {
            adaptiveVisualRenderer.adaptiveBlend = 1f;
            adaptiveVisualRenderer.contrastStrength = 1.15f;
            adaptiveVisualRenderer.edgeContrast = 0.6f;
            adaptiveVisualRenderer.enableOutline = true;
            adaptiveVisualRenderer.outlineWidth = 1f;
            adaptiveVisualRenderer.outlineStrength = 0.55f;
            adaptiveVisualRenderer.Refresh();
        }
    }

    public void Initialize(FootprintSide side, FootprintVisualProfile visualProfile)
    {
        Side = side;
        profile = visualProfile ?? new FootprintVisualProfile();
        DecayIndex = 0;
        IsLatest = false;
        baseColor = profile.normalFootprintColor;
        ApplyColor(baseColor, 0f);
    }

    public void SetAsLatest(float targetAlpha, Color color)
    {
        IsLatest = true;
        DecayIndex = 0;
        baseColor = color;
        StartVisualRoutine(FadeInRoutine(targetAlpha, color));
    }

    public void SetResidual(int decayIndex, float alpha, Color color)
    {
        IsLatest = false;
        DecayIndex = decayIndex;
        baseColor = color;
        StartVisualRoutine(ResidualRoutine(alpha, color));
    }

    public void Tint(Color color)
    {
        baseColor = color;
        ApplyColor(baseColor, currentAlpha);
    }

    public void FadeOutAndDestroy(float duration, AnimationCurve curve)
    {
        StartVisualRoutine(FadeOutRoutine(duration, curve));
    }

    private IEnumerator FadeInRoutine(float targetAlpha, Color color)
    {
        float duration = Mathf.Max(0.0001f, profile.fadeInDuration);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float curveT = profile.fadeInCurve != null ? profile.fadeInCurve.Evaluate(t) : t;
            ApplyColor(color, Mathf.Lerp(0f, targetAlpha, curveT));
            yield return null;
        }

        ApplyColor(color, targetAlpha);
    }

    private IEnumerator ResidualRoutine(float targetAlpha, Color color)
    {
        float startAlpha = currentAlpha;
        float duration = Mathf.Max(0.0001f, profile.residualDecayDuration);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float curveT = profile.residualDecayCurve != null ? profile.residualDecayCurve.Evaluate(t) : t;
            ApplyColor(color, Mathf.Lerp(startAlpha, targetAlpha, curveT));
            yield return null;
        }

        ApplyColor(color, targetAlpha);
    }

    private IEnumerator FadeOutRoutine(float duration, AnimationCurve curve)
    {
        float startAlpha = currentAlpha;
        duration = Mathf.Max(0.0001f, duration);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float curveT = curve != null ? curve.Evaluate(t) : 1f - t;
            ApplyColor(baseColor, Mathf.Lerp(startAlpha, 0f, curveT));
            yield return null;
        }

        Destroy(gameObject);
    }

    private void StartVisualRoutine(IEnumerator routine)
    {
        if (animationRoutine != null)
            StopCoroutine(animationRoutine);

        animationRoutine = StartCoroutine(routine);
    }

    private void ApplyColor(Color color, float alpha)
    {
        currentAlpha = Mathf.Clamp01(alpha);
        Color tinted = new Color(color.r, color.g, color.b, currentAlpha);

        if (spriteRenderer != null)
            spriteRenderer.color = tinted;

        if (additionalRenderers == null)
            return;

        for (int i = 0; i < additionalRenderers.Length; i++)
        {
            Renderer renderer = additionalRenderers[i];
            if (renderer == null || ReferenceEquals(renderer, spriteRenderer))
                continue;

            if (renderer is SpriteRenderer sprite)
            {
                sprite.color = tinted;
                continue;
            }

            EnsureInstancedMaterials(renderer);
            if (instancedMaterials != null)
            {
                for (int m = 0; m < instancedMaterials.Length; m++)
                {
                    Material material = instancedMaterials[m];
                    if (material == null)
                        continue;

                    if (material.HasProperty("_Color"))
                        material.color = tinted;
                    else if (material.HasProperty("_BaseColor"))
                        material.SetColor("_BaseColor", tinted);
                }
            }
        }
    }

    private void EnsureInstancedMaterials(Renderer renderer)
    {
        if (instancedMaterials != null)
            return;

        Material[] shared = renderer.materials;
        instancedMaterials = new Material[shared.Length];
        for (int i = 0; i < shared.Length; i++)
            instancedMaterials[i] = shared[i] != null ? new Material(shared[i]) : null;

        renderer.materials = instancedMaterials;
    }

    void OnDestroy()
    {
        if (instancedMaterials == null)
            return;

        for (int i = 0; i < instancedMaterials.Length; i++)
        {
            if (instancedMaterials[i] != null)
                Destroy(instancedMaterials[i]);
        }
    }
}

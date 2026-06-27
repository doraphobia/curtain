using UnityEngine;

[DisallowMultipleComponent]
public class FoleySurfaceResolver2D : MonoBehaviour
{
    public string fallbackSurfaceId = "Default";
    public LayerMask surfaceLayers = ~0;
    [Min(0f)]
    public float checkInterval = 0.03f;
    public bool useTriggerColliders = true;

    private string cachedSurfaceId;
    private Vector3 cachedPosition;
    private float nextCheckTime;
    private bool hasCachedSurface;

    public string ResolveSurfaceId(Vector3 worldPosition)
    {
        float now = Time.time;
        if (hasCachedSurface && now < nextCheckTime && Vector2.Distance(cachedPosition, worldPosition) < 0.02f)
            return cachedSurfaceId;

        cachedSurfaceId = ResolveSurfaceIdImmediate(worldPosition);
        cachedPosition = worldPosition;
        nextCheckTime = now + checkInterval;
        hasCachedSurface = true;
        return cachedSurfaceId;
    }

    public string ResolveSurfaceIdImmediate(Vector3 worldPosition)
    {
        Collider2D[] colliders = Physics2D.OverlapPointAll(worldPosition, surfaceLayers);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider2D hit = colliders[i];
            if (hit == null)
                continue;

            if (!useTriggerColliders && hit.isTrigger)
                continue;

            FoleySurface2D surface = hit.GetComponentInParent<FoleySurface2D>();
            if (surface == null || string.IsNullOrWhiteSpace(surface.surfaceId))
                continue;

            return surface.surfaceId.Trim();
        }

        return string.IsNullOrWhiteSpace(fallbackSurfaceId) ? "Default" : fallbackSurfaceId.Trim();
    }
}

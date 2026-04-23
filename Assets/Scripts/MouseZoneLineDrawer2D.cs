using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
[DisallowMultipleComponent]
public class MouseZoneLineDrawer2D : MonoBehaviour
{
    [Header("References")]
    public Camera targetCamera;
    public Collider2D targetCollider;
    public LineRenderer lineRenderer;

    [Header("Line")]
    public float minPointDistance = 0.05f;
    public float lineZOffset = 0f;
    public bool clearLineOnExit = false;
    public Color fallbackLineColor = Color.red;

    private Material runtimeFallbackMaterial;

    private readonly List<Vector3> points = new List<Vector3>();

    void Awake()
    {
        if (targetCollider == null)
            targetCollider = GetComponent<Collider2D>();

        if (targetCamera == null)
            targetCamera = Camera.main;

        if (lineRenderer == null)
            lineRenderer = GetComponent<LineRenderer>();

        if (lineRenderer != null)
        {
            lineRenderer.useWorldSpace = true;
            EnsureLineRendererHasMaterial();
        }
    }

    void OnDestroy()
    {
        if (runtimeFallbackMaterial != null)
            Destroy(runtimeFallbackMaterial);
    }

    void Update()
    {
        if (targetCollider == null || targetCamera == null || lineRenderer == null)
            return;

        if (!IsMouseInsideZone())
        {
            if (clearLineOnExit && points.Count > 0)
                ClearLine();

            return;
        }

        AddCurrentMousePoint();
    }

    private bool IsMouseInsideZone()
    {
        Vector3 mouseWorld = targetCamera.ScreenToWorldPoint(Input.mousePosition);
        mouseWorld.z = transform.position.z;
        return targetCollider.OverlapPoint(mouseWorld);
    }

    private void AddCurrentMousePoint()
    {
        Vector3 mouseWorld = targetCamera.ScreenToWorldPoint(Input.mousePosition);
        mouseWorld.z = lineZOffset;

        if (points.Count > 0 && Vector3.Distance(points[points.Count - 1], mouseWorld) < minPointDistance)
            return;

        points.Add(mouseWorld);
        lineRenderer.positionCount = points.Count;
        lineRenderer.SetPositions(points.ToArray());
    }

    public void ClearLine()
    {
        points.Clear();
        lineRenderer.positionCount = 0;
    }

    private void EnsureLineRendererHasMaterial()
    {
        if (lineRenderer == null)
            return;

        if (lineRenderer.sharedMaterial != null)
            return;

        Shader shader = Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        if (shader == null)
            return;

        runtimeFallbackMaterial = new Material(shader);

        if (runtimeFallbackMaterial.HasProperty("_Color"))
            runtimeFallbackMaterial.color = fallbackLineColor;

        lineRenderer.material = runtimeFallbackMaterial;
        lineRenderer.startColor = fallbackLineColor;
        lineRenderer.endColor = fallbackLineColor;
    }
}

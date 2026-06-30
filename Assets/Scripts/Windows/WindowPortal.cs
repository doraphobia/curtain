using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Vision portal for enemies. Integrates with existing <see cref="HoverScrollColorLerp2D"/> curtains when assigned.
/// Windows are visibility-only; enemies do not path through them.
/// </summary>
[DisallowMultipleComponent]
public class WindowPortal : MonoBehaviour
{
    [Header("Room")]
    public Room ownerRoom;

    [Header("Open State")]
    [Tooltip("When set, open state follows curtain progress (IsAtColorB).")]
    public HoverScrollColorLerp2D curtain;
    public bool manualIsOpen;

    [Header("Vision Shape")]
    public Collider2D windowCollider;
    public Transform[] optionalVisionPoints;

    public bool IsOpen
    {
        get
        {
            if (curtain != null)
                return curtain.IsAtColorB;
            return manualIsOpen;
        }
    }

    void Awake()
    {
        if (windowCollider == null)
            windowCollider = GetComponent<Collider2D>();

        if (curtain == null)
            curtain = GetComponent<HoverScrollColorLerp2D>();
    }

    void OnValidate()
    {
        if (windowCollider == null)
            windowCollider = GetComponent<Collider2D>();
    }

    public void CollectVisionSamplePoints(List<Vector2> results, float padding)
    {
        if (results == null)
            return;

        results.Clear();
        padding = Mathf.Max(0f, padding);

        if (optionalVisionPoints != null && optionalVisionPoints.Length > 0)
        {
            for (int i = 0; i < optionalVisionPoints.Length; i++)
            {
                Transform point = optionalVisionPoints[i];
                if (point != null)
                    results.Add(point.position);
            }

            return;
        }

        if (windowCollider == null)
        {
            results.Add(transform.position);
            return;
        }

        Bounds bounds = windowCollider.bounds;
        Vector2 center = bounds.center;
        Vector2 extents = bounds.extents;
        extents.x = Mathf.Max(0f, extents.x - padding);
        extents.y = Mathf.Max(0f, extents.y - padding);

        results.Add(center);
        results.Add(center + new Vector2(-extents.x, extents.y));
        results.Add(center + new Vector2(extents.x, extents.y));
        results.Add(center + new Vector2(-extents.x, -extents.y));
        results.Add(center + new Vector2(extents.x, -extents.y));
    }
}

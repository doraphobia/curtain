using System.Collections.Generic;
using DuoCurtain.Vision;
using UnityEngine;

/// <summary>
/// Vision portal for enemies. Integrates with existing <see cref="HoverScrollColorLerp2D"/> curtains when assigned.
/// Windows are visibility-only; enemies do not path through them.
/// </summary>
[DisallowMultipleComponent]
public class WindowPortal : MonoBehaviour, IVisionPortal
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

    [Header("Vision Portal")]
    public Vector2 portalCenter;
    public Vector2 portalTangent = Vector2.up;
    public Vector2 outwardNormal = Vector2.right;
    [Min(0.01f)]
    public float portalLength = 0.82f;
    [Min(0.001f)]
    public float portalExitOffset = 0.05f;
    [Min(0.01f)]
    public float portalContinuationDistance = 4f;
    [Range(1f, 179f)]
    public float portalSpreadAngle = 45f;

    private bool hasRuntimePortalOverride;
    private Vector2 runtimePortalCenter;
    private Vector2 runtimePortalTangent;
    private Vector2 runtimeOutwardNormal;
    private float runtimePortalLength;

    public bool IsOpen
    {
        get
        {
            if (curtain != null)
                return curtain.IsAtColorB;
            return manualIsOpen;
        }
    }

    public bool IsPortalOpen => IsOpen;
    public Vector2 PortalA => GetPortalCenter() - GetPortalTangent() * (GetPortalLength() * 0.5f);
    public Vector2 PortalB => GetPortalCenter() + GetPortalTangent() * (GetPortalLength() * 0.5f);
    public Vector2 ForwardNormal => GetPortalNormal();
    public Vector2 BackwardNormal => -GetPortalNormal();
    public int FrontRoomId => ownerRoom != null ? ownerRoom.GetInstanceID() : 0;
    public int BackRoomId => 0;

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
        portalLength = Mathf.Max(0.01f, portalLength);
        portalExitOffset = Mathf.Max(0.001f, portalExitOffset);
        portalContinuationDistance = Mathf.Max(0.01f, portalContinuationDistance);
        portalSpreadAngle = Mathf.Clamp(portalSpreadAngle, 1f, 179f);
    }

    public void ConfigurePortal(Vector2 center, Vector2 tangent, Vector2 normal, float length)
    {
        ClearRuntimePortalOverride();
        portalCenter = center;
        portalTangent = tangent.sqrMagnitude > 0.000001f ? tangent.normalized : Vector2.up;
        outwardNormal = normal.sqrMagnitude > 0.000001f ? normal.normalized : Vector2.right;
        portalLength = Mathf.Max(0.01f, length);
    }

    public void SetRuntimePortalOverride(Vector2 center, Vector2 tangent, Vector2 normal, float length)
    {
        hasRuntimePortalOverride = true;
        runtimePortalCenter = center;
        runtimePortalTangent = tangent.sqrMagnitude > 0.000001f ? tangent.normalized : Vector2.up;
        runtimeOutwardNormal = normal.sqrMagnitude > 0.000001f ? normal.normalized : Vector2.right;
        runtimePortalLength = Mathf.Max(0.01f, length);
    }

    public void ClearRuntimePortalOverride()
    {
        hasRuntimePortalOverride = false;
        runtimePortalLength = 0f;
    }

    public bool CanPassVision(Vector2 incomingOrigin, Vector2 incomingDirection)
    {
        if (!IsOpen || incomingDirection.sqrMagnitude <= 0.000001f)
            return false;

        return TryGetPortalHitPoint(incomingOrigin, incomingDirection.normalized, out _);
    }

    public VisionPortalExit GetExit(Vector2 incomingOrigin, Vector2 incomingDirection)
    {
        Vector2 safeDirection = incomingDirection.sqrMagnitude > 0.000001f
            ? incomingDirection.normalized
            : Vector2.up;
        Vector2 hitPoint = TryGetPortalHitPoint(incomingOrigin, safeDirection, out Vector2 rayHit)
            ? rayHit
            : GetClosestPointOnPortal(incomingOrigin);
        Vector2 normal = GetExitNormal(incomingOrigin);
        Vector2 exitOrigin = hitPoint + normal * Mathf.Max(0.001f, portalExitOffset);
        return new VisionPortalExit(
            exitOrigin,
            normal,
            portalContinuationDistance,
            GetTargetRoomId(incomingOrigin));
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

    private Vector2 GetPortalCenter()
    {
        if (hasRuntimePortalOverride)
            return runtimePortalCenter;
        if (portalCenter.sqrMagnitude > 0.000001f)
            return portalCenter;
        if (windowCollider != null)
            return windowCollider.bounds.center;
        return transform.position;
    }

    private Vector2 GetPortalTangent()
    {
        if (hasRuntimePortalOverride && runtimePortalTangent.sqrMagnitude > 0.000001f)
            return runtimePortalTangent.normalized;
        if (portalTangent.sqrMagnitude > 0.000001f)
            return portalTangent.normalized;
        return transform.up;
    }

    private Vector2 GetPortalNormal()
    {
        if (hasRuntimePortalOverride && runtimeOutwardNormal.sqrMagnitude > 0.000001f)
            return runtimeOutwardNormal.normalized;
        if (outwardNormal.sqrMagnitude > 0.000001f)
            return outwardNormal.normalized;
        return transform.right;
    }

    private float GetPortalLength()
    {
        return hasRuntimePortalOverride
            ? Mathf.Max(0.01f, runtimePortalLength)
            : Mathf.Max(0.01f, portalLength);
    }

    private Vector2 GetExitNormal(Vector2 incomingOrigin)
    {
        Vector2 center = GetPortalCenter();
        Vector2 normal = GetPortalNormal();
        float side = Vector2.Dot(incomingOrigin - center, normal);
        return side >= 0f ? -normal : normal;
    }

    private int GetTargetRoomId(Vector2 incomingOrigin)
    {
        Vector2 center = GetPortalCenter();
        Vector2 normal = GetPortalNormal();
        float side = Vector2.Dot(incomingOrigin - center, normal);
        return side >= 0f ? BackRoomId : FrontRoomId;
    }

    private bool TryGetPortalHitPoint(Vector2 origin, Vector2 direction, out Vector2 hitPoint)
    {
        hitPoint = Vector2.zero;
        Vector2 a = PortalA;
        Vector2 b = PortalB;
        Vector2 segment = b - a;
        float denominator = Cross(direction, segment);
        if (Mathf.Abs(denominator) <= 0.000001f)
            return false;

        Vector2 difference = a - origin;
        float rayDistance = Cross(difference, segment) / denominator;
        float segmentT = Cross(difference, direction) / denominator;
        if (rayDistance < 0f || segmentT < -0.0001f || segmentT > 1.0001f)
            return false;

        hitPoint = origin + direction * rayDistance;
        return true;
    }

    private Vector2 GetClosestPointOnPortal(Vector2 point)
    {
        Vector2 a = PortalA;
        Vector2 b = PortalB;
        Vector2 segment = b - a;
        float lengthSqr = segment.sqrMagnitude;
        if (lengthSqr <= 0.000001f)
            return a;

        float t = Mathf.Clamp01(Vector2.Dot(point - a, segment) / lengthSqr);
        return a + segment * t;
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }
}

using System.Collections.Generic;
using DuoCurtain.Vision;
using UnityEngine;

/// <summary>
/// Cone vision and world-geometry line-of-sight for enemies.
/// Uses Physics2D raycasts consistent with the project's 2D gameplay.
/// </summary>
public class EnemyVision : MonoBehaviour
{
    public struct VisionResult
    {
        public bool isVisible;
        public bool usedWindowPortal;
        public Room detectedRoom;
        public WindowPortal usedWindow;
        public Vector2 samplePoint;
    }

    private readonly List<Vector2> sampleBuffer = new List<Vector2>(8);
    private readonly RaycastHit2D[] hitBuffer = new RaycastHit2D[8];
    private readonly VisionSnapshot visibilitySnapshot = new VisionSnapshot();
    private readonly RadialVisionSampler2D visibilitySampler = new RadialVisionSampler2D();

    [Header("Segment Visibility")]
    public bool useVisibilityWorld = true;
    public bool requireActualVisibilityPolygonContainment = true;
    [Range(2, 512)]
    public int baseRayCount = 96;
    [Range(2, 1024)]
    public int maxRayCount = 384;
    [Range(0, 8)]
    public int edgeRefinementIterations = 2;
    [Min(0f)]
    public float edgeDistanceThreshold = 0.35f;

    [Header("Segment Movement")]
    public bool useVisibilityWorldMovementBlocking = true;

    [Header("Debug")]
    public bool debugLogDetectionSource;

    public VisionResult EvaluateVisibility(
        Vector2 observerPosition,
        Vector2 observerForward,
        Vector2 playerPosition,
        Transform playerRoot,
        float viewDistance,
        float viewAngleDegrees,
        LayerMask playerLayer,
        LayerMask wallLayer,
        LayerMask windowLayer,
        bool requireOpenWindow,
        int windowVisionSampleCount,
        float windowVisionSamplePadding,
        IEnumerable<WindowPortal> windows)
    {
        VisionResult result = new VisionResult();
        if (playerRoot == null)
            return result;

        if (useVisibilityWorld &&
            TryEvaluateVisibilityWorld(
                observerPosition,
                observerForward,
                playerPosition,
                viewDistance,
                viewAngleDegrees,
                wallLayer,
                requireOpenWindow,
                out result))
        {
            return result;
        }

        if (useVisibilityWorld && requireActualVisibilityPolygonContainment)
            return result;

        Vector2 toPlayer = playerPosition - observerPosition;
        float distance = toPlayer.magnitude;
        if (distance > viewDistance || distance <= 0.0001f)
            return result;

        float halfAngle = viewAngleDegrees * 0.5f;
        float angle = Vector2.Angle(observerForward, toPlayer.normalized);
        if (angle > halfAngle)
            return result;

        if (HasClearLineToTarget(observerPosition, playerPosition, wallLayer, playerLayer, playerRoot))
        {
            result.isVisible = true;
            result.detectedRoom = RoomManager.GetRoomAtPosition(playerPosition);
            return result;
        }

        return result;
    }

    public bool HasClearWallLine(Vector2 from, Vector2 to, LayerMask wallLayer, Collider2D ignoredCollider)
    {
        Vector2 delta = to - from;
        float distance = delta.magnitude;
        if (distance <= 0.0001f)
            return true;

        if (useVisibilityWorldMovementBlocking &&
            TryHasClearVisibilityWorldMovementLine(from, to, ignoredCollider, out bool visibilityWorldClear))
        {
            return visibilityWorldClear;
        }

        int hitCount = Physics2D.RaycastNonAlloc(from, delta.normalized, hitBuffer, distance, wallLayer);
        if (hitCount <= 0)
            return true;

        SortHits(hitBuffer, hitCount);
        for (int i = 0; i < hitCount; i++)
        {
            Collider2D collider = hitBuffer[i].collider;
            if (collider == null)
                continue;

            if (ignoredCollider != null && collider == ignoredCollider)
                continue;

            return false;
        }

        return true;
    }

    private bool TryEvaluateVisibilityWorld(
        Vector2 observerPosition,
        Vector2 observerForward,
        Vector2 playerPosition,
        float viewDistance,
        float viewAngleDegrees,
        LayerMask obstacleMask,
        bool requireOpenWindow,
        out VisionResult result)
    {
        result = new VisionResult();
        VisibilityWorld world = VisibilityWorld.GetOrCreate();
        if (world == null)
            return false;

        world.RebuildIfDirty();
        if (world.Segments.Count == 0)
            return false;

        visibilitySampler.Sample(
            visibilitySnapshot,
            observerPosition,
            observerForward,
            viewAngleDegrees,
            viewDistance,
            Mathf.Max(2, baseRayCount),
            Mathf.Max(baseRayCount, maxRayCount),
            edgeRefinementIterations,
            edgeDistanceThreshold,
            obstacleMask,
            false,
            transform,
            world,
            false);

        if (!visibilitySnapshot.TryGetDetectionInfo(
                playerPosition,
                out VisionDetectionSource source,
                out PortalVisionPolygon portalPolygon))
        {
            if (debugLogDetectionSource)
                Debug.Log("[EnemyVision] Segment detection=false Source=None", this);
            return true;
        }

        result.isVisible = true;
        result.usedWindowPortal = false;
        result.detectedRoom = RoomManager.GetRoomAtPosition(playerPosition);

        if (debugLogDetectionSource)
            Debug.Log("[EnemyVision] Segment detection=true Source=" + source, this);

        return true;
    }

    private bool TryHasClearVisibilityWorldMovementLine(
        Vector2 from,
        Vector2 to,
        Collider2D ignoredCollider,
        out bool isClear)
    {
        isClear = true;
        VisibilityWorld world = VisibilityWorld.Instance != null ? VisibilityWorld.Instance : VisibilityWorld.GetOrCreate();
        if (world == null)
            return false;

        world.RebuildIfDirty();
        if (world.Segments.Count == 0)
            return false;

        for (int i = 0; i < world.Segments.Count; i++)
        {
            VisibilitySegment segment = world.Segments[i];
            if (!segment.BlocksMovement)
                continue;
            if (ignoredCollider != null &&
                segment.sourceObject != null &&
                segment.sourceObject == ignoredCollider.gameObject)
            {
                continue;
            }

            if (TryLineSegmentIntersection(from, to, segment.a, segment.b, out float pathT, out float wallT) &&
                pathT > 0.001f &&
                pathT < 0.999f &&
                wallT >= -0.001f &&
                wallT <= 1.001f)
            {
                isClear = false;
                return true;
            }
        }

        return true;
    }

    private static bool TryLineSegmentIntersection(
        Vector2 a,
        Vector2 b,
        Vector2 c,
        Vector2 d,
        out float abT,
        out float cdT)
    {
        abT = 0f;
        cdT = 0f;
        Vector2 r = b - a;
        Vector2 s = d - c;
        float denominator = Cross(r, s);
        if (Mathf.Abs(denominator) <= 0.000001f)
            return false;

        Vector2 difference = c - a;
        abT = Cross(difference, s) / denominator;
        cdT = Cross(difference, r) / denominator;
        return abT >= -0.001f && abT <= 1.001f && cdT >= -0.001f && cdT <= 1.001f;
    }

    private static float Cross(Vector2 left, Vector2 right)
    {
        return left.x * right.y - left.y * right.x;
    }

    public bool HasClearLineToTarget(
        Vector2 from,
        Vector2 to,
        LayerMask wallLayer,
        LayerMask playerLayer,
        Transform targetTransform)
    {
        Vector2 delta = to - from;
        float distance = delta.magnitude;
        if (distance <= 0.0001f)
            return true;

        LayerMask mask = wallLayer | playerLayer;
        int hitCount = Physics2D.RaycastNonAlloc(from, delta.normalized, hitBuffer, distance, mask);
        if (hitCount <= 0)
            return true;

        SortHits(hitBuffer, hitCount);
        for (int i = 0; i < hitCount; i++)
        {
            Collider2D collider = hitBuffer[i].collider;
            if (collider == null)
                continue;

            if (targetTransform != null && collider.transform.IsChildOf(targetTransform))
                return true;

            if (IsLayerInMask(collider.gameObject.layer, wallLayer))
                return false;
        }

        return true;
    }

    private static void SortHits(RaycastHit2D[] buffer, int hitCount)
    {
        for (int i = 1; i < hitCount; i++)
        {
            RaycastHit2D key = buffer[i];
            int j = i - 1;
            while (j >= 0 && buffer[j].distance > key.distance)
            {
                buffer[j + 1] = buffer[j];
                j--;
            }

            buffer[j + 1] = key;
        }
    }

    private static bool IsLayerInMask(int layer, LayerMask mask)
    {
        return (mask.value & (1 << layer)) != 0;
    }
}

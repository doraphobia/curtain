using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cone vision, wall line-of-sight, and open-window portal visibility for enemies.
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

        if (windows == null)
            return result;

        foreach (WindowPortal window in windows)
        {
            if (window == null)
                continue;

            if (requireOpenWindow && !window.IsOpen)
                continue;

            window.CollectVisionSamplePoints(sampleBuffer, windowVisionSamplePadding);
            int sampleCount = Mathf.Min(sampleBuffer.Count, Mathf.Max(1, windowVisionSampleCount));
            for (int i = 0; i < sampleCount; i++)
            {
                Vector2 sample = sampleBuffer[i];
                if (!HasClearWallLine(observerPosition, sample, wallLayer, window.windowCollider))
                    continue;

                if (!HasClearLineToTarget(sample, playerPosition, wallLayer, playerLayer, playerRoot))
                    continue;

                result.isVisible = true;
                result.usedWindowPortal = true;
                result.usedWindow = window;
                result.samplePoint = sample;
                result.detectedRoom = window.ownerRoom != null
                    ? window.ownerRoom
                    : RoomManager.GetRoomAtPosition(playerPosition);
                return result;
            }
        }

        return result;
    }

    public bool HasClearWallLine(Vector2 from, Vector2 to, LayerMask wallLayer, Collider2D ignoredCollider)
    {
        Vector2 delta = to - from;
        float distance = delta.magnitude;
        if (distance <= 0.0001f)
            return true;

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

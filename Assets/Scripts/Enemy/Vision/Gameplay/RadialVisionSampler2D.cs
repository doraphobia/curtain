using System.Collections.Generic;
using UnityEngine;

namespace DuoCurtain.Vision
{
    /// <summary>
    /// Allocation-stable radial ray sampler with edge refinement around occluder changes.
    /// </summary>
    public sealed class RadialVisionSampler2D
    {
        private readonly List<VisionRaySample> coarseSamples = new List<VisionRaySample>(128);
        private readonly RaycastHit2D[] hitBuffer = new RaycastHit2D[32];
        private ContactFilter2D contactFilter;

        public void Sample(
            VisionSnapshot snapshot,
            Vector2 origin,
            Vector2 forward,
            float viewAngleDegrees,
            float viewDistance,
            int rayCount,
            int maxRayCount,
            int edgeRefinementIterations,
            float edgeDistanceThreshold,
            LayerMask obstacleMask,
            bool hitTriggers,
            Transform ignoredRoot)
        {
            if (snapshot == null)
                return;

            int coarseRayCount = Mathf.Clamp(rayCount, 2, Mathf.Max(2, maxRayCount));
            int maximumSamples = Mathf.Max(coarseRayCount, maxRayCount);
            float angle = Mathf.Clamp(viewAngleDegrees, 0.01f, 360f);
            float distance = Mathf.Max(0.0001f, viewDistance);
            Vector2 safeForward = forward.sqrMagnitude > 0.000001f ? forward.normalized : Vector2.up;
            float centerAngle = Mathf.Atan2(safeForward.y, safeForward.x) * Mathf.Rad2Deg;
            float startAngle = centerAngle - angle * 0.5f;
            float angleStep = angle / (coarseRayCount - 1);

            contactFilter = new ContactFilter2D();
            contactFilter.SetLayerMask(obstacleMask);
            contactFilter.useLayerMask = true;
            contactFilter.useTriggers = hitTriggers;

            coarseSamples.Clear();
            snapshot.Begin(origin, safeForward, angle, distance, Time.time, Time.frameCount);
            for (int i = 0; i < coarseRayCount; i++)
            {
                float normalizedAngle = coarseRayCount > 1 ? i / (float)(coarseRayCount - 1) : 0.5f;
                float sampleAngle = startAngle + angleStep * i;
                coarseSamples.Add(CastRay(
                    origin,
                    sampleAngle,
                    normalizedAngle,
                    distance,
                    ignoredRoot));
            }

            snapshot.AddSample(coarseSamples[0], maximumSamples);
            for (int i = 1; i < coarseSamples.Count; i++)
            {
                AppendRefinedPair(
                    snapshot,
                    coarseSamples[i - 1],
                    coarseSamples[i],
                    Mathf.Max(0, edgeRefinementIterations),
                    Mathf.Max(0f, edgeDistanceThreshold),
                    origin,
                    distance,
                    ignoredRoot,
                    maximumSamples);
            }

            snapshot.Complete();
        }

        private void AppendRefinedPair(
            VisionSnapshot snapshot,
            VisionRaySample left,
            VisionRaySample right,
            int remainingIterations,
            float edgeDistanceThreshold,
            Vector2 origin,
            float viewDistance,
            Transform ignoredRoot,
            int maximumSamples)
        {
            if (snapshot.SampleCount >= maximumSamples)
                return;

            if (remainingIterations <= 0 || !NeedsRefinement(left, right, edgeDistanceThreshold))
            {
                snapshot.AddSample(right, maximumSamples);
                return;
            }

            float middleAngle = (left.angleDegrees + right.angleDegrees) * 0.5f;
            float middleNormalizedAngle = (left.normalizedAngle + right.normalizedAngle) * 0.5f;
            VisionRaySample middle = CastRay(
                origin,
                middleAngle,
                middleNormalizedAngle,
                viewDistance,
                ignoredRoot);

            AppendRefinedPair(
                snapshot,
                left,
                middle,
                remainingIterations - 1,
                edgeDistanceThreshold,
                origin,
                viewDistance,
                ignoredRoot,
                maximumSamples);
            AppendRefinedPair(
                snapshot,
                middle,
                right,
                remainingIterations - 1,
                edgeDistanceThreshold,
                origin,
                viewDistance,
                ignoredRoot,
                maximumSamples);
        }

        private VisionRaySample CastRay(
            Vector2 origin,
            float angleDegrees,
            float normalizedAngle,
            float viewDistance,
            Transform ignoredRoot)
        {
            float radians = angleDegrees * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
            int hitCount = Physics2D.Raycast(origin, direction, contactFilter, hitBuffer, viewDistance);
            RaycastHit2D nearest = default;
            bool hasHit = false;
            float nearestDistance = viewDistance;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit2D candidate = hitBuffer[i];
                if (candidate.collider == null)
                    continue;
                if (ignoredRoot != null && candidate.collider.transform.IsChildOf(ignoredRoot))
                    continue;
                if (candidate.distance >= nearestDistance)
                    continue;

                nearest = candidate;
                nearestDistance = candidate.distance;
                hasHit = true;
            }

            return new VisionRaySample
            {
                angleDegrees = angleDegrees,
                normalizedAngle = Mathf.Clamp01(normalizedAngle),
                direction = direction,
                point = hasHit ? nearest.point : origin + direction * viewDistance,
                hitNormal = hasHit ? nearest.normal : Vector2.zero,
                distance = nearestDistance,
                normalizedDistance = Mathf.Clamp01(nearestDistance / viewDistance),
                hit = hasHit,
                colliderInstanceId = hasHit ? nearest.collider.GetInstanceID() : 0
            };
        }

        private static bool NeedsRefinement(
            VisionRaySample left,
            VisionRaySample right,
            float edgeDistanceThreshold)
        {
            if (left.hit != right.hit)
                return true;
            if (left.colliderInstanceId != right.colliderInstanceId)
                return true;
            return Mathf.Abs(left.distance - right.distance) > edgeDistanceThreshold;
        }
    }
}

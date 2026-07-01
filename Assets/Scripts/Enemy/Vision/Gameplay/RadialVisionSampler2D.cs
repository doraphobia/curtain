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
        private readonly List<float> candidateAngles = new List<float>(256);
        private readonly List<VisibilitySegment> relevantSegments = new List<VisibilitySegment>(256);
        private readonly List<VisibilityOpening> relevantOpenings = new List<VisibilityOpening>(64);
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
            Transform ignoredRoot,
            VisibilityWorld visibilityWorld,
            bool fallbackToPhysicsWhenNoSegments)
        {
            relevantSegments.Clear();
            relevantOpenings.Clear();
            if (visibilityWorld != null)
            {
                visibilityWorld.RebuildIfDirty();
                CollectRelevantBlockingSegments(
                    visibilityWorld.Segments,
                    origin,
                    forward,
                    viewAngleDegrees,
                    viewDistance,
                    ignoredRoot,
                    relevantSegments);
                CollectRelevantOpenings(
                    visibilityWorld.Openings,
                    origin,
                    forward,
                    viewAngleDegrees,
                    viewDistance,
                    ignoredRoot,
                    relevantOpenings);

                if (relevantSegments.Count > 0 || relevantOpenings.Count > 0)
                {
                    SampleSegments(
                        snapshot,
                        origin,
                        forward,
                        viewAngleDegrees,
                        viewDistance,
                        rayCount,
                        maxRayCount,
                        relevantSegments,
                        relevantOpenings);
                    return;
                }
            }

            if (fallbackToPhysicsWhenNoSegments)
            {
                Sample(
                    snapshot,
                    origin,
                    forward,
                    viewAngleDegrees,
                    viewDistance,
                    rayCount,
                    maxRayCount,
                    edgeRefinementIterations,
                    edgeDistanceThreshold,
                    obstacleMask,
                    hitTriggers,
                    ignoredRoot);
                return;
            }

            SampleSegments(
                snapshot,
                origin,
                forward,
                viewAngleDegrees,
                viewDistance,
                rayCount,
                maxRayCount,
                relevantSegments,
                relevantOpenings);
        }

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

        private void SampleSegments(
            VisionSnapshot snapshot,
            Vector2 origin,
            Vector2 forward,
            float viewAngleDegrees,
            float viewDistance,
            int rayCount,
            int maxRayCount,
            List<VisibilitySegment> segments,
            List<VisibilityOpening> openings)
        {
            if (snapshot == null)
                return;

            int coarseRayCount = Mathf.Clamp(rayCount, 2, Mathf.Max(2, maxRayCount));
            float angle = Mathf.Clamp(viewAngleDegrees, 0.01f, 360f);
            float distance = Mathf.Max(0.0001f, viewDistance);
            Vector2 safeForward = forward.sqrMagnitude > 0.000001f ? forward.normalized : Vector2.up;
            float centerAngle = Mathf.Atan2(safeForward.y, safeForward.x) * Mathf.Rad2Deg;
            BuildCandidateAngles(origin, centerAngle, angle, coarseRayCount, segments, openings);
            int maximumSamples = Mathf.Max(Mathf.Max(coarseRayCount, maxRayCount), candidateAngles.Count);

            snapshot.Begin(origin, safeForward, angle, distance, Time.time, Time.frameCount);
            for (int i = 0; i < candidateAngles.Count; i++)
            {
                float sampleAngle = candidateAngles[i];
                float normalizedAngle = GetNormalizedAngleInCone(sampleAngle, centerAngle, angle);
                snapshot.AddSample(
                    CastSegmentRay(origin, sampleAngle, normalizedAngle, distance, segments, openings),
                    maximumSamples);
            }

            snapshot.Complete();
        }

        private void BuildCandidateAngles(
            Vector2 origin,
            float centerAngle,
            float angle,
            int coarseRayCount,
            List<VisibilitySegment> segments,
            List<VisibilityOpening> openings)
        {
            candidateAngles.Clear();
            float startAngle = centerAngle - angle * 0.5f;
            bool fullCircle = angle >= 359.999f;
            if (fullCircle)
            {
                for (int i = 0; i < coarseRayCount; i++)
                {
                    float normalized = i / (float)coarseRayCount;
                    AddCandidateAngle(startAngle + angle * normalized, centerAngle, angle);
                }
            }
            else
            {
                float angleStep = angle / Mathf.Max(1, coarseRayCount - 1);
                for (int i = 0; i < coarseRayCount; i++)
                    AddCandidateAngle(startAngle + angleStep * i, centerAngle, angle);
            }

            const float endpointEpsilonDegrees = 0.08f;
            for (int i = 0; i < segments.Count; i++)
            {
                VisibilitySegment segment = segments[i];
                AddEndpointAngles(origin, segment.a, centerAngle, angle, endpointEpsilonDegrees);
                AddEndpointAngles(origin, segment.b, centerAngle, angle, endpointEpsilonDegrees);
            }

            if (openings != null)
            {
                for (int i = 0; i < openings.Count; i++)
                {
                    VisibilityOpening opening = openings[i];
                    if (!opening.allowsVision || opening.geometry.type != OpeningGeometryType.Segment)
                        continue;

                    AddEndpointAngles(origin, opening.geometry.segmentA, centerAngle, angle, endpointEpsilonDegrees);
                    AddEndpointAngles(origin, opening.geometry.segmentB, centerAngle, angle, endpointEpsilonDegrees);
                }
            }

            candidateAngles.Sort((left, right) =>
                GetNormalizedAngleInCone(left, centerAngle, angle)
                    .CompareTo(GetNormalizedAngleInCone(right, centerAngle, angle)));
        }

        private void AddEndpointAngles(
            Vector2 origin,
            Vector2 endpoint,
            float centerAngle,
            float angle,
            float epsilonDegrees)
        {
            Vector2 delta = endpoint - origin;
            if (delta.sqrMagnitude <= 0.000001f)
                return;

            float endpointAngle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            AddCandidateAngle(endpointAngle - epsilonDegrees, centerAngle, angle);
            AddCandidateAngle(endpointAngle, centerAngle, angle);
            AddCandidateAngle(endpointAngle + epsilonDegrees, centerAngle, angle);
        }

        private void AddCandidateAngle(float angleDegrees, float centerAngle, float coneAngle)
        {
            if (!IsAngleInsideCone(angleDegrees, centerAngle, coneAngle, 0.001f))
                return;

            float normalized = GetNormalizedAngleInCone(angleDegrees, centerAngle, coneAngle);
            for (int i = 0; i < candidateAngles.Count; i++)
            {
                if (Mathf.Abs(GetNormalizedAngleInCone(candidateAngles[i], centerAngle, coneAngle) - normalized) <= 0.00005f)
                    return;
            }

            candidateAngles.Add(angleDegrees);
        }

        private VisionRaySample CastSegmentRay(
            Vector2 origin,
            float angleDegrees,
            float normalizedAngle,
            float viewDistance,
            List<VisibilitySegment> segments,
            List<VisibilityOpening> openings)
        {
            float radians = angleDegrees * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
            bool hasHit = false;
            float nearestDistance = viewDistance;
            VisibilitySegment nearestSegment = default;
            Vector2 nearestPoint = origin + direction * viewDistance;
            float minimumDistance = 0f;
            const int maxOpeningPasses = 8;

            for (int pass = 0; pass < maxOpeningPasses; pass++)
            {
                hasHit = false;
                nearestDistance = viewDistance;
                nearestSegment = default;
                nearestPoint = origin + direction * viewDistance;

                for (int i = 0; i < segments.Count; i++)
                {
                    VisibilitySegment segment = segments[i];
                    if (!segment.BlocksVision)
                        continue;

                    if (!TryRaySegmentIntersection(
                            origin,
                            direction,
                            segment.a,
                            segment.b,
                            out float distanceAlongRay))
                    {
                        continue;
                    }

                    if (distanceAlongRay < minimumDistance || distanceAlongRay > viewDistance)
                        continue;

                    bool tiedWithCurrent = hasHit && Mathf.Abs(distanceAlongRay - nearestDistance) <= 0.0005f;
                    if (hasHit && distanceAlongRay > nearestDistance && !tiedWithCurrent)
                        continue;

                    if (tiedWithCurrent)
                    {
                        if (!ShouldPreferSegment(segment, nearestSegment))
                            continue;
                    }
                    else if (hasHit && distanceAlongRay >= nearestDistance)
                    {
                        continue;
                    }

                    nearestDistance = distanceAlongRay;
                    nearestPoint = origin + direction * distanceAlongRay;
                    nearestSegment = segment;
                    hasHit = true;
                }

                if (!hasHit ||
                    !CanRayContinueThroughOpening(
                        origin,
                        direction,
                        nearestDistance,
                        nearestPoint,
                        nearestSegment,
                        openings))
                {
                    break;
                }

                minimumDistance = nearestDistance + 0.001f;
            }

            Vector2 hitNormal = Vector2.zero;
            if (hasHit)
            {
                Vector2 edge = nearestSegment.b - nearestSegment.a;
                hitNormal = new Vector2(edge.y, -edge.x);
                if (hitNormal.sqrMagnitude > 0.000001f)
                    hitNormal.Normalize();
                if (Vector2.Dot(hitNormal, direction) > 0f)
                    hitNormal = -hitNormal;
            }

            return new VisionRaySample
            {
                angleDegrees = angleDegrees,
                normalizedAngle = Mathf.Clamp01(normalizedAngle),
                direction = direction,
                point = nearestPoint,
                hitNormal = hitNormal,
                distance = nearestDistance,
                normalizedDistance = Mathf.Clamp01(nearestDistance / viewDistance),
                hit = hasHit,
                colliderInstanceId = 0,
                visibilitySegmentType = hasHit ? nearestSegment.type : VisibilitySegmentType.OpenDoor,
                visibilitySegmentSourceId = hasHit ? nearestSegment.sourceId : 0,
                visibilitySourceObject = hasHit ? nearestSegment.sourceObject : null,
                visibilitySourceComponent = hasHit ? nearestSegment.sourceComponent : null
            };
        }

        private void CollectRelevantBlockingSegments(
            IReadOnlyList<VisibilitySegment> worldSegments,
            Vector2 origin,
            Vector2 forward,
            float viewAngleDegrees,
            float viewDistance,
            Transform ignoredRoot,
            List<VisibilitySegment> results,
            int ignoredSegmentSourceId = 0)
        {
            results.Clear();
            if (worldSegments == null)
                return;

            Vector2 safeForward = forward.sqrMagnitude > 0.000001f ? forward.normalized : Vector2.up;
            float centerAngle = Mathf.Atan2(safeForward.y, safeForward.x) * Mathf.Rad2Deg;
            float angle = Mathf.Clamp(viewAngleDegrees, 0.01f, 360f);
            float maxDistanceSqr = Mathf.Pow(Mathf.Max(0.0001f, viewDistance) + 0.05f, 2f);

            for (int i = 0; i < worldSegments.Count; i++)
            {
                VisibilitySegment segment = worldSegments[i];
                if (!segment.BlocksVision)
                    continue;
                if (ignoredSegmentSourceId != 0 && segment.sourceId == ignoredSegmentSourceId)
                    continue;

                if (ignoredRoot != null &&
                    segment.sourceObject != null &&
                    segment.sourceObject.transform.IsChildOf(ignoredRoot))
                {
                    continue;
                }

                if (DistanceSqrPointSegment(origin, segment.a, segment.b) > maxDistanceSqr &&
                    (segment.a - origin).sqrMagnitude > maxDistanceSqr &&
                    (segment.b - origin).sqrMagnitude > maxDistanceSqr)
                {
                    continue;
                }

                results.Add(segment);
            }
        }

        private void CollectRelevantOpenings(
            IReadOnlyList<VisibilityOpening> worldOpenings,
            Vector2 origin,
            Vector2 forward,
            float viewAngleDegrees,
            float viewDistance,
            Transform ignoredRoot,
            List<VisibilityOpening> results)
        {
            results.Clear();
            if (worldOpenings == null)
                return;

            Vector2 safeForward = forward.sqrMagnitude > 0.000001f ? forward.normalized : Vector2.up;
            float centerAngle = Mathf.Atan2(safeForward.y, safeForward.x) * Mathf.Rad2Deg;
            float angle = Mathf.Clamp(viewAngleDegrees, 0.01f, 360f);
            float maxDistanceSqr = Mathf.Pow(Mathf.Max(0.0001f, viewDistance) + 0.05f, 2f);

            for (int i = 0; i < worldOpenings.Count; i++)
            {
                VisibilityOpening opening = worldOpenings[i];
                if (!opening.allowsVision ||
                    opening.projectionRule != OpeningProjectionRule.ContinueIncomingRay ||
                    opening.geometry.type != OpeningGeometryType.Segment)
                {
                    continue;
                }

                if (ignoredRoot != null &&
                    opening.sourceObject != null &&
                    opening.sourceObject.transform.IsChildOf(ignoredRoot))
                {
                    continue;
                }

                Vector2 a = opening.geometry.segmentA;
                Vector2 b = opening.geometry.segmentB;
                if (DistanceSqrPointSegment(origin, a, b) > maxDistanceSqr &&
                    (a - origin).sqrMagnitude > maxDistanceSqr &&
                    (b - origin).sqrMagnitude > maxDistanceSqr)
                {
                    continue;
                }

                if (!SegmentCouldIntersectCone(origin, centerAngle, angle, a, b))
                    continue;

                results.Add(opening);
            }
        }

        private static bool CanRayContinueThroughOpening(
            Vector2 origin,
            Vector2 direction,
            float wallDistance,
            Vector2 wallHitPoint,
            VisibilitySegment wallSegment,
            List<VisibilityOpening> openings)
        {
            if (openings == null || openings.Count == 0)
                return false;

            const float distanceTolerance = 0.01f;
            float wallLineLength = (wallSegment.b - wallSegment.a).magnitude;
            float pointToleranceSqr = Mathf.Pow(Mathf.Max(0.015f, wallLineLength * 0.03f), 2f);

            for (int i = 0; i < openings.Count; i++)
            {
                VisibilityOpening opening = openings[i];
                if (!opening.allowsVision ||
                    opening.projectionRule != OpeningProjectionRule.ContinueIncomingRay ||
                    opening.geometry.type != OpeningGeometryType.Segment)
                {
                    continue;
                }

                if (!TryRaySegmentIntersection(
                        origin,
                        direction,
                        opening.geometry.segmentA,
                        opening.geometry.segmentB,
                        out float openingDistance))
                {
                    continue;
                }

                if (Mathf.Abs(openingDistance - wallDistance) > distanceTolerance)
                    continue;

                Vector2 openingHitPoint = origin + direction * openingDistance;
                if ((openingHitPoint - wallHitPoint).sqrMagnitude > pointToleranceSqr)
                    continue;

                return true;
            }

            return false;
        }

        private static bool ShouldPreferSegment(VisibilitySegment candidate, VisibilitySegment current)
        {
            if (current.type == VisibilitySegmentType.Wall)
            {
                return candidate.type == VisibilitySegmentType.ClosedWindow ||
                       candidate.type == VisibilitySegmentType.ClosedDoor;
            }

            return false;
        }

        private static bool SegmentCouldIntersectCone(
            Vector2 origin,
            float centerAngle,
            float coneAngle,
            VisibilitySegment segment)
        {
            return SegmentCouldIntersectCone(origin, centerAngle, coneAngle, segment.a, segment.b);
        }

        private static bool SegmentCouldIntersectCone(
            Vector2 origin,
            float centerAngle,
            float coneAngle,
            Vector2 segmentA,
            Vector2 segmentB)
        {
            if (coneAngle >= 359.999f)
                return true;

            Vector2 midpoint = (segmentA + segmentB) * 0.5f;
            return IsPointAngleInsideCone(origin, segmentA, centerAngle, coneAngle) ||
                   IsPointAngleInsideCone(origin, segmentB, centerAngle, coneAngle) ||
                   IsPointAngleInsideCone(origin, midpoint, centerAngle, coneAngle);
        }

        private static bool IsPointAngleInsideCone(
            Vector2 origin,
            Vector2 point,
            float centerAngle,
            float coneAngle)
        {
            Vector2 delta = point - origin;
            if (delta.sqrMagnitude <= 0.000001f)
                return true;

            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            return IsAngleInsideCone(angle, centerAngle, coneAngle, 3f);
        }

        private static bool IsAngleInsideCone(
            float angleDegrees,
            float centerAngle,
            float coneAngle,
            float paddingDegrees)
        {
            if (coneAngle >= 359.999f)
                return true;

            float delta = Mathf.DeltaAngle(centerAngle, angleDegrees);
            return delta >= -coneAngle * 0.5f - paddingDegrees &&
                   delta <= coneAngle * 0.5f + paddingDegrees;
        }

        private static float GetNormalizedAngleInCone(
            float angleDegrees,
            float centerAngle,
            float coneAngle)
        {
            if (coneAngle >= 359.999f)
            {
                float start = centerAngle - 180f;
                return Mathf.Repeat(angleDegrees - start, 360f) / 360f;
            }

            float delta = Mathf.DeltaAngle(centerAngle, angleDegrees);
            return Mathf.Clamp01((delta + coneAngle * 0.5f) / Mathf.Max(0.0001f, coneAngle));
        }

        private static bool TryRaySegmentIntersection(
            Vector2 rayOrigin,
            Vector2 rayDirection,
            Vector2 segmentStart,
            Vector2 segmentEnd,
            out float distanceAlongRay)
        {
            distanceAlongRay = 0f;
            Vector2 segment = segmentEnd - segmentStart;
            float denominator = Cross(rayDirection, segment);
            if (Mathf.Abs(denominator) <= 0.000001f)
                return false;

            Vector2 difference = segmentStart - rayOrigin;
            float t = Cross(difference, segment) / denominator;
            float u = Cross(difference, rayDirection) / denominator;
            if (t < 0f || u < -0.0001f || u > 1.0001f)
                return false;

            distanceAlongRay = t;
            return true;
        }

        private static float DistanceSqrPointSegment(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 segment = end - start;
            float lengthSqr = segment.sqrMagnitude;
            if (lengthSqr <= 0.000001f)
                return (point - start).sqrMagnitude;

            float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSqr);
            Vector2 closest = start + segment * t;
            return (point - closest).sqrMagnitude;
        }

        private static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
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

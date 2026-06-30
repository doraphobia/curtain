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
        private readonly List<VisibilitySegment> portalRelevantSegments = new List<VisibilitySegment>(256);
        private readonly List<int> portalSourceIds = new List<int>(16);
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
            bool fallbackToPhysicsWhenNoSegments,
            bool allowWindowPortals = true,
            float portalExitOffset = 0.05f,
            float portalContinuationDistance = 4f,
            float portalSpreadAngle = 45f,
            int maxPortalDepth = 1)
        {
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

                if (relevantSegments.Count > 0)
                {
                    SampleSegments(
                        snapshot,
                        origin,
                        forward,
                        viewAngleDegrees,
                        viewDistance,
                        rayCount,
                        maxRayCount,
                        relevantSegments);
                    BuildPortalContinuations(
                        snapshot,
                        visibilityWorld.Segments,
                        ignoredRoot,
                        rayCount,
                        maxRayCount,
                        allowWindowPortals,
                        portalExitOffset,
                        portalContinuationDistance,
                        portalSpreadAngle,
                        maxPortalDepth);
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
                relevantSegments);
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
            List<VisibilitySegment> segments)
        {
            if (snapshot == null)
                return;

            int coarseRayCount = Mathf.Clamp(rayCount, 2, Mathf.Max(2, maxRayCount));
            float angle = Mathf.Clamp(viewAngleDegrees, 0.01f, 360f);
            float distance = Mathf.Max(0.0001f, viewDistance);
            Vector2 safeForward = forward.sqrMagnitude > 0.000001f ? forward.normalized : Vector2.up;
            float centerAngle = Mathf.Atan2(safeForward.y, safeForward.x) * Mathf.Rad2Deg;
            BuildCandidateAngles(origin, centerAngle, angle, coarseRayCount, segments);
            int maximumSamples = Mathf.Max(Mathf.Max(coarseRayCount, maxRayCount), candidateAngles.Count);

            snapshot.Begin(origin, safeForward, angle, distance, Time.time, Time.frameCount);
            for (int i = 0; i < candidateAngles.Count; i++)
            {
                float sampleAngle = candidateAngles[i];
                float normalizedAngle = GetNormalizedAngleInCone(sampleAngle, centerAngle, angle);
                snapshot.AddSample(
                    CastSegmentRay(origin, sampleAngle, normalizedAngle, distance, segments),
                    maximumSamples);
            }

            snapshot.Complete();
        }

        private void BuildCandidateAngles(
            Vector2 origin,
            float centerAngle,
            float angle,
            int coarseRayCount,
            List<VisibilitySegment> segments)
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
            List<VisibilitySegment> segments)
        {
            float radians = angleDegrees * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
            bool hasHit = false;
            float nearestDistance = viewDistance;
            VisibilitySegment nearestSegment = default;
            Vector2 nearestPoint = origin + direction * viewDistance;

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

                if (distanceAlongRay < 0f || distanceAlongRay > viewDistance)
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

        private void BuildPortalContinuations(
            VisionSnapshot snapshot,
            IReadOnlyList<VisibilitySegment> worldSegments,
            Transform ignoredRoot,
            int rayCount,
            int maxRayCount,
            bool allowWindowPortals,
            float portalExitOffset,
            float portalContinuationDistance,
            float portalSpreadAngle,
            int maxPortalDepth)
        {
            if (snapshot == null ||
                worldSegments == null ||
                maxPortalDepth <= 0 ||
                !allowWindowPortals)
            {
                return;
            }

            portalSourceIds.Clear();
            bool addedPortal = false;
            for (int i = 0; i < snapshot.RaySamples.Count; i++)
            {
                VisionRaySample sample = snapshot.RaySamples[i];
                if (!sample.hit || !VisibilityWorld.IsPortalType(sample.visibilitySegmentType))
                    continue;
                if (sample.visibilitySegmentType != VisibilitySegmentType.OpenWindow &&
                    sample.visibilitySegmentType != VisibilitySegmentType.Portal)
                    continue;
                if (sample.visibilitySegmentSourceId != 0 &&
                    portalSourceIds.Contains(sample.visibilitySegmentSourceId))
                {
                    continue;
                }

                IVisionPortal portal = ResolvePortal(sample);
                if (portal == null || !portal.IsPortalOpen)
                    continue;
                if (!portal.CanPassVision(snapshot.origin, sample.direction))
                    continue;

                VisionPortalExit portalExit = portal.GetExit(snapshot.origin, sample.direction);
                Vector2 exitForward = portalExit.forward.sqrMagnitude > 0.000001f
                    ? portalExit.forward.normalized
                    : sample.direction.normalized;
                float effectiveExitOffset = Mathf.Max(0.001f, portalExitOffset);
                Vector2 apertureA = portal.PortalA + exitForward * effectiveExitOffset;
                Vector2 apertureB = portal.PortalB + exitForward * effectiveExitOffset;
                Vector2 exitOrigin = (apertureA + apertureB) * 0.5f;
                float distance = Mathf.Min(
                    Mathf.Max(0.01f, portalExit.maxDistance),
                    Mathf.Max(0.01f, portalContinuationDistance));
                float angle = Mathf.Clamp(portalSpreadAngle, 1f, 179f);
                OrderPortalApertureEndpoints(
                    ref apertureA,
                    ref apertureB,
                    exitOrigin,
                    exitForward,
                    angle);

                CollectRelevantBlockingSegments(
                    worldSegments,
                    exitOrigin,
                    exitForward,
                    angle,
                    distance,
                    ignoredRoot,
                    portalRelevantSegments,
                    sample.visibilitySegmentSourceId);

                VisionSnapshot portalSnapshot = new VisionSnapshot();
                SampleSegments(
                    portalSnapshot,
                    exitOrigin,
                    exitForward,
                    angle,
                    distance,
                    Mathf.Max(4, rayCount / 2),
                    Mathf.Max(8, maxRayCount / 2),
                    portalRelevantSegments);

                if (portalSnapshot.IsValid)
                {
                    snapshot.AddPortalPolygon(
                        portal,
                        sample.point,
                        exitOrigin,
                        portalSnapshot.VisibilityPolygon,
                        portalExit.targetRoomId,
                        sample.visibilitySegmentType == VisibilitySegmentType.OpenWindow
                            ? VisionDetectionSource.OpenWindowPortal
                            : VisionDetectionSource.OpenDoorPortal,
                        true,
                        apertureA,
                        apertureB);
                    addedPortal = true;
                }

                if (sample.visibilitySegmentSourceId != 0)
                    portalSourceIds.Add(sample.visibilitySegmentSourceId);
            }

            if (addedPortal)
                snapshot.Complete();
        }

        private static IVisionPortal ResolvePortal(VisionRaySample sample)
        {
            if (sample.visibilitySourceComponent is IVisionPortal componentPortal)
                return componentPortal;
            if (sample.visibilitySourceObject == null)
                return null;
            return sample.visibilitySourceObject.GetComponent<IVisionPortal>();
        }

        private static void OrderPortalApertureEndpoints(
            ref Vector2 apertureA,
            ref Vector2 apertureB,
            Vector2 origin,
            Vector2 forward,
            float coneAngle)
        {
            Vector2 safeForward = forward.sqrMagnitude > 0.000001f ? forward.normalized : Vector2.up;
            float centerAngle = Mathf.Atan2(safeForward.y, safeForward.x) * Mathf.Rad2Deg;
            float angleA = Mathf.Atan2(apertureA.y - origin.y, apertureA.x - origin.x) * Mathf.Rad2Deg;
            float angleB = Mathf.Atan2(apertureB.y - origin.y, apertureB.x - origin.x) * Mathf.Rad2Deg;
            float normalizedA = GetNormalizedAngleInCone(angleA, centerAngle, Mathf.Clamp(coneAngle, 1f, 179f));
            float normalizedB = GetNormalizedAngleInCone(angleB, centerAngle, Mathf.Clamp(coneAngle, 1f, 179f));
            if (normalizedA <= normalizedB)
                return;

            Vector2 temp = apertureA;
            apertureA = apertureB;
            apertureB = temp;
        }

        private static bool ShouldPreferSegment(VisibilitySegment candidate, VisibilitySegment current)
        {
            if (candidate.IsPortal != current.IsPortal)
                return candidate.IsPortal;

            if (current.type == VisibilitySegmentType.Wall)
            {
                return candidate.type == VisibilitySegmentType.OpenWindow ||
                       candidate.type == VisibilitySegmentType.ClosedWindow ||
                       candidate.type == VisibilitySegmentType.ClosedDoor ||
                       candidate.type == VisibilitySegmentType.Portal;
            }

            return false;
        }

        private static bool SegmentCouldIntersectCone(
            Vector2 origin,
            float centerAngle,
            float coneAngle,
            VisibilitySegment segment)
        {
            if (coneAngle >= 359.999f)
                return true;

            Vector2 midpoint = (segment.a + segment.b) * 0.5f;
            return IsPointAngleInsideCone(origin, segment.a, centerAngle, coneAngle) ||
                   IsPointAngleInsideCone(origin, segment.b, centerAngle, coneAngle) ||
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

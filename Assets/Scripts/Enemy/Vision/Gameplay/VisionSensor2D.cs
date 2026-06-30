using System;
using UnityEngine;

namespace DuoCurtain.Vision
{
    [DisallowMultipleComponent]
    public sealed class VisionSensor2D : MonoBehaviour
    {
        public enum ForwardSource
        {
            TransformUp,
            TransformRight,
            Manual
        }

        [Header("Geometry")]
        public Transform originTransform;
        public Vector2 localOriginOffset;
        public ForwardSource forwardSource = ForwardSource.Manual;
        public Vector2 manualForward = Vector2.up;
        [Min(0.01f)]
        public float viewDistance = 8f;
        [Range(1f, 360f)]
        public float viewAngle = 90f;

        [Header("Sampling")]
        [Range(2, 512)]
        public int rayCount = 72;
        [Range(2, 1024)]
        public int maxRayCount = 256;
        [Range(0, 8)]
        public int edgeRefinementIterations = 3;
        [Min(0f)]
        public float edgeDistanceThreshold = 0.35f;
        [Min(0f)]
        public float updateFrequency = 0f;
        public LayerMask obstacleMask = ~0;
        public bool hitTriggers = false;
        public bool sampleAutomatically = true;

        [Header("Visibility World")]
        public bool useVisibilityWorld = true;
        public VisibilityWorld visibilityWorld;
        public bool fallbackToPhysicsWhenNoVisibilitySegments = true;

        private readonly VisionSnapshot latestSnapshot = new VisionSnapshot();
        private readonly RadialVisionSampler2D sampler = new RadialVisionSampler2D();
        private float nextSampleTime;

        public VisionSnapshot LatestSnapshot => latestSnapshot;
        public event Action<VisionSnapshot> SnapshotUpdated;

        void OnEnable()
        {
            ForceSample();
        }

        void Update()
        {
            if (!sampleAutomatically)
                return;

            if (updateFrequency > 0f && Time.time < nextSampleTime)
                return;

            ForceSample();
        }

        public void SetForward(Vector2 worldDirection)
        {
            if (worldDirection.sqrMagnitude <= 0.000001f)
                return;

            manualForward = worldDirection.normalized;
        }

        public void ForceSample()
        {
            Vector2 origin = GetWorldOrigin();
            Vector2 forward = GetWorldForward();
            if (useVisibilityWorld)
            {
                VisibilityWorld resolvedWorld = visibilityWorld != null
                    ? visibilityWorld
                    : VisibilityWorld.GetOrCreate();
                sampler.Sample(
                    latestSnapshot,
                    origin,
                    forward,
                    viewAngle,
                    viewDistance,
                    rayCount,
                    Mathf.Max(rayCount, maxRayCount),
                    edgeRefinementIterations,
                    edgeDistanceThreshold,
                    obstacleMask,
                    hitTriggers,
                    transform,
                    resolvedWorld,
                    fallbackToPhysicsWhenNoVisibilitySegments);
            }
            else
            {
                sampler.Sample(
                    latestSnapshot,
                    origin,
                    forward,
                    viewAngle,
                    viewDistance,
                    rayCount,
                    Mathf.Max(rayCount, maxRayCount),
                    edgeRefinementIterations,
                    edgeDistanceThreshold,
                    obstacleMask,
                    hitTriggers,
                    transform);
            }

            nextSampleTime = updateFrequency > 0f
                ? Time.time + 1f / Mathf.Max(0.01f, updateFrequency)
                : Time.time;
            SnapshotUpdated?.Invoke(latestSnapshot);
        }

        public bool CanSeeWorldPoint(Vector2 worldPoint)
        {
            return latestSnapshot.ContainsWorldPoint(worldPoint);
        }

        public Vector2 GetWorldOrigin()
        {
            Transform source = originTransform != null ? originTransform : transform;
            return source.TransformPoint(localOriginOffset);
        }

        public Vector2 GetWorldForward()
        {
            Transform source = originTransform != null ? originTransform : transform;
            switch (forwardSource)
            {
                case ForwardSource.TransformRight:
                    return source.right;
                case ForwardSource.TransformUp:
                    return source.up;
                default:
                    return manualForward.sqrMagnitude > 0.000001f ? manualForward.normalized : Vector2.up;
            }
        }

        void OnValidate()
        {
            viewDistance = Mathf.Max(0.01f, viewDistance);
            viewAngle = Mathf.Clamp(viewAngle, 1f, 360f);
            rayCount = Mathf.Clamp(rayCount, 2, 512);
            maxRayCount = Mathf.Clamp(maxRayCount, rayCount, 1024);
            edgeRefinementIterations = Mathf.Clamp(edgeRefinementIterations, 0, 8);
            edgeDistanceThreshold = Mathf.Max(0f, edgeDistanceThreshold);
            updateFrequency = Mathf.Max(0f, updateFrequency);
        }
    }
}

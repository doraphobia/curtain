using System.Collections.Generic;
using UnityEngine;

namespace DuoCurtain.Combat
{
    [DefaultExecutionOrder(10000)]
    [DisallowMultipleComponent]
    public sealed class ImpactCameraFeedback : MonoBehaviour
    {
        private sealed class ShakeInstance
        {
            public Camera camera;
            public Vector3 impactOrigin;
            public Vector3 cameraPositionSnapshot;
            public Vector2 direction;
            public float distance;
            public float amplitude;
            public float frequency;
            public float duration;
            public float startTime;
            public float directionalWeight;
            public float noiseStrength;
            public AnimationCurve decay;
            public int priority;
            public float seedX;
            public float seedY;
        }

        [Header("Distance Falloff")]
        [Min(0f)] public float minimumRadius;
        [Min(0.01f)] public float maximumRadius = 14f;
        [Min(0f)] public float minimumStrength;
        [Min(0f)] public float maximumStrength = 0.35f;
        public AnimationCurve distanceCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

        [Header("Layering")]
        [Min(0f)] public float maximumCombinedOffset = 0.75f;
        public bool useUnscaledTime = true;

        [Header("Debug")]
        public bool drawDebug;
        public bool logImpacts;
        [SerializeField] private int activeShakeCount;
        [SerializeField] private string currentCameraName;
        [SerializeField] private Vector2 lastShakeDirection;
        [SerializeField] private float lastShakeStrength;

        private readonly List<ShakeInstance> shakes = new List<ShakeInstance>(16);
        private readonly Dictionary<Camera, Vector3> previousOffsets = new Dictionary<Camera, Vector3>();
        private readonly Dictionary<Camera, Vector3> previousFinalPositions = new Dictionary<Camera, Vector3>();
        private readonly Dictionary<Camera, Vector3> frameOffsets = new Dictionary<Camera, Vector3>();
        private static ImpactCameraFeedback instance;

        public int ActiveShakeCount => shakes.Count;
        public string CurrentCameraName => currentCameraName;
        public Vector2 LastShakeDirection => lastShakeDirection;
        public float LastShakeStrength => lastShakeStrength;
        public float DebugMaximumRadius => maximumRadius;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureRuntimeInstance()
        {
            if (FindFirstObjectByType<ImpactCameraFeedback>() != null)
                return;
            new GameObject("Impact Camera Feedback").AddComponent<ImpactCameraFeedback>();
        }

        void OnEnable()
        {
            if (instance != null && instance != this)
            {
                enabled = false;
                return;
            }
            instance = this;
            ImpactEventBus.Impacted += HandleImpact;
        }

        void OnDisable()
        {
            ImpactEventBus.Impacted -= HandleImpact;
            RestoreCameraOffsets();
            if (instance == this)
                instance = null;
        }

        void LateUpdate()
        {
            TickShakes(useUnscaledTime ? Time.unscaledTime : Time.time);
        }

        private void HandleImpact(ImpactEvent impact)
        {
            if (!CurrentCameraService.TryGetCurrentGameplayCamera(out Camera camera))
                return;

            Vector3 cameraSnapshot = camera.transform.position;
            float distance = Vector2.Distance(cameraSnapshot, impact.worldPosition);
            float radius = Mathf.Min(Mathf.Max(0.01f, maximumRadius), impact.parameters.radius);
            if (distance > radius)
                return;

            float normalizedDistance = Mathf.InverseLerp(
                Mathf.Max(0f, minimumRadius),
                Mathf.Max(minimumRadius + 0.01f, radius),
                distance);
            float distanceWeight = distanceCurve != null && distanceCurve.length > 0
                ? Mathf.Clamp01(distanceCurve.Evaluate(normalizedDistance))
                : 1f - normalizedDistance;
            float requestedStrength = Mathf.Clamp(
                impact.parameters.strength,
                minimumStrength,
                Mathf.Max(minimumStrength, maximumStrength));
            float amplitude = requestedStrength * distanceWeight;
            if (amplitude <= 0.00001f)
                return;

            System.Random random = new System.Random(impact.randomSeed);
            Vector2 direction = (Vector2)(cameraSnapshot - impact.worldPosition);
            if (direction.sqrMagnitude <= 0.000001f)
                direction = impact.direction.sqrMagnitude > 0.000001f ? impact.direction : Vector2.right;

            ShakeInstance shake = new ShakeInstance
            {
                camera = camera,
                impactOrigin = impact.worldPosition,
                cameraPositionSnapshot = cameraSnapshot,
                direction = direction.normalized,
                distance = distance,
                amplitude = amplitude,
                frequency = impact.parameters.frequency,
                duration = impact.parameters.duration,
                startTime = useUnscaledTime ? Time.unscaledTime : Time.time,
                directionalWeight = impact.parameters.directionalWeight,
                noiseStrength = impact.parameters.noiseStrength,
                decay = new AnimationCurve(impact.parameters.falloff.keys),
                priority = impact.parameters.priority,
                seedX = (float)random.NextDouble() * 1000f,
                seedY = (float)random.NextDouble() * 1000f
            };
            shakes.Add(shake);
            currentCameraName = camera.name;
            lastShakeDirection = shake.direction;
            lastShakeStrength = amplitude;
            if (logImpacts)
            {
                Debug.Log(
                    "[ImpactCamera] camera=" + camera.name + " distance=" + distance.ToString("0.00") +
                    " strength=" + amplitude.ToString("0.000") + " shakes=" + shakes.Count,
                    this);
            }
        }

        private void TickShakes(float now)
        {
            frameOffsets.Clear();
            for (int i = shakes.Count - 1; i >= 0; i--)
            {
                ShakeInstance shake = shakes[i];
                if (shake.camera == null)
                {
                    shakes.RemoveAt(i);
                    continue;
                }

                float normalized = Mathf.Clamp01((now - shake.startTime) / Mathf.Max(0.01f, shake.duration));
                if (normalized >= 1f)
                {
                    shakes.RemoveAt(i);
                    continue;
                }

                float decay = shake.decay != null ? Mathf.Max(0f, shake.decay.Evaluate(normalized)) : 1f - normalized;
                float phase = (now - shake.startTime) * shake.frequency * Mathf.PI * 2f;
                Vector2 directional = shake.direction * Mathf.Sin(phase) * shake.directionalWeight;
                Vector2 noise = new Vector2(
                    Mathf.PerlinNoise(shake.seedX, phase * 0.05f) * 2f - 1f,
                    Mathf.PerlinNoise(shake.seedY, phase * 0.05f) * 2f - 1f) * shake.noiseStrength;
                float priorityWeight = Mathf.Lerp(0.75f, 1.25f, Mathf.Clamp01(shake.priority / 100f));
                Vector3 offset = (directional + noise) * (shake.amplitude * decay * priorityWeight);
                frameOffsets.TryGetValue(shake.camera, out Vector3 accumulated);
                frameOffsets[shake.camera] = accumulated + offset;
            }

            HashSet<Camera> cameras = new HashSet<Camera>(previousOffsets.Keys);
            foreach (Camera camera in frameOffsets.Keys)
                cameras.Add(camera);

            foreach (Camera camera in cameras)
            {
                if (camera == null)
                    continue;
                Vector3 currentPosition = camera.transform.position;
                previousFinalPositions.TryGetValue(camera, out Vector3 previousFinal);
                previousOffsets.TryGetValue(camera, out Vector3 previousOffset);
                bool cameraWasNotMovedByItsRig = (currentPosition - previousFinal).sqrMagnitude <= 0.000001f;
                Vector3 basePosition = cameraWasNotMovedByItsRig ? currentPosition - previousOffset : currentPosition;

                frameOffsets.TryGetValue(camera, out Vector3 offset);
                if (offset.magnitude > maximumCombinedOffset && maximumCombinedOffset > 0f)
                    offset = offset.normalized * maximumCombinedOffset;
                camera.transform.position = basePosition + offset;
                previousOffsets[camera] = offset;
                previousFinalPositions[camera] = camera.transform.position;
            }
            activeShakeCount = shakes.Count;
        }

        private void RestoreCameraOffsets()
        {
            foreach (KeyValuePair<Camera, Vector3> pair in previousOffsets)
            {
                if (pair.Key != null)
                    pair.Key.transform.position -= pair.Value;
            }
            previousOffsets.Clear();
            previousFinalPositions.Clear();
            frameOffsets.Clear();
            shakes.Clear();
        }

        void OnDrawGizmosSelected()
        {
            if (!drawDebug)
                return;
            Gizmos.color = new Color(1f, 0.35f, 0.1f, 0.5f);
            for (int i = 0; i < shakes.Count; i++)
            {
                ShakeInstance shake = shakes[i];
                Gizmos.DrawWireSphere(shake.impactOrigin, Mathf.Max(0.01f, maximumRadius));
                Gizmos.DrawLine(shake.impactOrigin, shake.cameraPositionSnapshot);
            }
        }
    }
}

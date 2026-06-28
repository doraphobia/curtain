using UnityEngine;

namespace AE2Unity
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class AE2MotionPlayer : MonoBehaviour
    {
        [SerializeField] private AE2MotionData motionData;
        [SerializeField] private Renderer targetRenderer;
        [SerializeField] private Material targetMaterial;
        [SerializeField] private bool playOnAwake = true;
        [SerializeField] private bool loop = true;
        [SerializeField, Min(0f)] private float speed = 1f;
        [SerializeField, Min(0f)] private float time;
        [SerializeField] private bool useMaterialPropertyBlock = true;

        private MaterialPropertyBlock propertyBlock;

        public AE2MotionData MotionData
        {
            get => motionData;
            set
            {
                motionData = value;
                Apply();
            }
        }

        public Renderer TargetRenderer
        {
            get => targetRenderer;
            set
            {
                targetRenderer = value;
                Apply();
            }
        }

        public Material TargetMaterial
        {
            get => targetMaterial;
            set
            {
                targetMaterial = value;
                Apply();
            }
        }

        public float TimeSeconds
        {
            get => time;
            set
            {
                time = Mathf.Max(0f, value);
                Apply();
            }
        }

        public bool Loop
        {
            get => loop;
            set => loop = value;
        }

        public float Speed
        {
            get => speed;
            set => speed = Mathf.Max(0f, value);
        }

        private void Reset()
        {
            targetRenderer = GetComponent<Renderer>();
        }

        private void Awake()
        {
            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<Renderer>();
            }

            if (Application.isPlaying && !playOnAwake)
            {
                enabled = false;
            }
        }

        private void OnEnable()
        {
            Apply();
        }

        private void OnValidate()
        {
            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<Renderer>();
            }

            time = Mathf.Max(0f, time);
            speed = Mathf.Max(0f, speed);
            Apply();
        }

        private void Update()
        {
            if (!Application.isPlaying || motionData == null || !playOnAwake)
            {
                Apply();
                return;
            }

            time += UnityEngine.Time.deltaTime * speed;
            var duration = motionData.Duration;
            if (loop && duration > 0f)
            {
                time %= duration;
            }

            Apply();
        }

        public void Apply()
        {
            if (motionData == null || targetRenderer == null)
            {
                return;
            }

            if (!AE2MotionEvaluator.TryEvaluateFirstRenderable(motionData, time, out var evaluated))
            {
                return;
            }

            if (targetMaterial != null && targetRenderer.sharedMaterial != targetMaterial)
            {
                targetRenderer.sharedMaterial = targetMaterial;
            }

            if (useMaterialPropertyBlock)
            {
                propertyBlock ??= new MaterialPropertyBlock();
                targetRenderer.GetPropertyBlock(propertyBlock);
                AE2ShaderPropertyBinder.Bind(propertyBlock, motionData, evaluated, time);
                targetRenderer.SetPropertyBlock(propertyBlock);
            }
            else
            {
                var material = targetRenderer.sharedMaterial;
                if (material == null)
                {
                    return;
                }

                AE2ShaderPropertyBinder.Bind(material, motionData, evaluated, time);
            }
        }
    }
}

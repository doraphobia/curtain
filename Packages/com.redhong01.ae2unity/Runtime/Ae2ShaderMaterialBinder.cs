using UnityEngine;

namespace AE2Unity
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class Ae2ShaderMaterialBinder : MonoBehaviour
    {
        private static readonly int AeTimeId = Shader.PropertyToID("_AeTime");
        private static readonly int CompSizeId = Shader.PropertyToID("_CompSize");
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");

        [SerializeField] private Ae2ShaderClip clip;
        [SerializeField] private Renderer targetRenderer;
        [SerializeField, Min(0f)] private float timeSeconds;
        [SerializeField, Range(0f, 1f)] private float opacity = 1f;
        [SerializeField] private bool loop = true;
        [SerializeField] private bool playInPlayMode = true;

        private MaterialPropertyBlock propertyBlock;

        public Ae2ShaderClip Clip
        {
            get => clip;
            set
            {
                clip = value;
                Apply();
            }
        }

        public float TimeSeconds
        {
            get => timeSeconds;
            set
            {
                timeSeconds = Mathf.Max(0f, value);
                Apply();
            }
        }

        private void Reset()
        {
            targetRenderer = GetComponent<Renderer>();
        }

        private void OnEnable()
        {
            Apply();
        }

        private void Update()
        {
            if (!Application.isPlaying || !playInPlayMode || clip == null)
            {
                return;
            }

            timeSeconds += Time.deltaTime;
            if (loop && clip.Duration > 0f)
            {
                timeSeconds %= clip.Duration;
            }

            Apply();
        }

        private void OnValidate()
        {
            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<Renderer>();
            }

            Apply();
        }

        public void Apply()
        {
            if (targetRenderer == null)
            {
                return;
            }

            propertyBlock ??= new MaterialPropertyBlock();
            targetRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(AeTimeId, timeSeconds);
            propertyBlock.SetFloat(OpacityId, opacity);

            if (clip != null)
            {
                propertyBlock.SetVector(CompSizeId, new Vector4(clip.Width, clip.Height, 1f / Mathf.Max(1, clip.Width), 1f / Mathf.Max(1, clip.Height)));
            }

            targetRenderer.SetPropertyBlock(propertyBlock);
        }
    }
}

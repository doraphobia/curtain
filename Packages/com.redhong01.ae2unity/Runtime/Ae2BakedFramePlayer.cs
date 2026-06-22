using UnityEngine;
using UnityEngine.UI;

namespace AE2Unity
{
    [ExecuteAlways]
    [RequireComponent(typeof(RawImage))]
    public sealed class Ae2BakedFramePlayer : MonoBehaviour
    {
        [SerializeField] private Ae2ShaderClip clip;
        [SerializeField] private Texture2D[] frames = System.Array.Empty<Texture2D>();
        [SerializeField] private float frameRate = 24f;
        [SerializeField] private float duration = 1f;
        [SerializeField] private bool playOnAwake = true;
        [SerializeField] private bool loop = true;
        [SerializeField] private float playbackSpeed = 1f;
        [SerializeField] private float timeSeconds;

        private RawImage rawImage;
        private int lastFrameIndex = -1;

        public Ae2ShaderClip Clip
        {
            get => clip;
            set => clip = value;
        }

        public Texture2D[] Frames
        {
            get => frames;
            set
            {
                frames = value ?? System.Array.Empty<Texture2D>();
                lastFrameIndex = -1;
                ApplyFrame();
            }
        }

        public float FrameRate
        {
            get => frameRate;
            set => frameRate = Mathf.Max(1f, value);
        }

        public float Duration
        {
            get => duration;
            set => duration = Mathf.Max(0f, value);
        }

        public float TimeSeconds
        {
            get => timeSeconds;
            set
            {
                timeSeconds = Mathf.Max(0f, value);
                ApplyFrame();
            }
        }

        private void OnEnable()
        {
            rawImage = GetComponent<RawImage>();
            ApplyFrame();
        }

        private void OnValidate()
        {
            frameRate = Mathf.Max(1f, frameRate);
            duration = Mathf.Max(0f, duration);
            rawImage = GetComponent<RawImage>();
            ApplyFrame();
        }

        private void Update()
        {
            if (frames == null || frames.Length == 0)
            {
                return;
            }

            if (Application.isPlaying && playOnAwake)
            {
                timeSeconds += Time.deltaTime * playbackSpeed;
                if (loop && duration > 0f)
                {
                    timeSeconds = Mathf.Repeat(timeSeconds, duration);
                }
                else
                {
                    timeSeconds = Mathf.Clamp(timeSeconds, 0f, duration);
                }
            }

            ApplyFrame();
        }

        private void ApplyFrame()
        {
            if (rawImage == null)
            {
                rawImage = GetComponent<RawImage>();
            }

            if (rawImage == null || frames == null || frames.Length == 0)
            {
                return;
            }

            var playbackDuration = duration > 0f ? duration : frames.Length / Mathf.Max(1f, frameRate);
            var playbackTime = loop && playbackDuration > 0f
                ? Mathf.Repeat(timeSeconds, playbackDuration)
                : Mathf.Clamp(timeSeconds, 0f, playbackDuration);
            var frameIndex = Mathf.Clamp(Mathf.FloorToInt(playbackTime * Mathf.Max(1f, frameRate) + 0.0001f), 0, frames.Length - 1);
            if (frameIndex == lastFrameIndex && rawImage.texture == frames[frameIndex])
            {
                return;
            }

            lastFrameIndex = frameIndex;
            rawImage.texture = frames[frameIndex];
        }
    }
}

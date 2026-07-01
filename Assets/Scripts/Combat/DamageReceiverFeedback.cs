using System.Collections;
using UnityEngine;

namespace DuoCurtain.Combat
{
    [DisallowMultipleComponent]
    public sealed class DamageReceiverProgressPresenter : MonoBehaviour
    {
        public CombatHealth health;
        public Transform barParent;
        public Vector3 localOffset = new Vector3(0f, 0.75f, 0f);
        [Min(0.01f)] public float smoothSpeed = 8f;
        [Min(0f)] public float hideDelay = 0.9f;
        public bool displayRemainingHealth;
        public DoorBreakProgressBar progressBar;

        private float hideAt = -1f;

        void OnEnable()
        {
            BindEvents();
        }

        void OnDisable()
        {
            UnbindEvents();
        }

        void Update()
        {
            if (progressBar != null && hideAt >= 0f && Time.unscaledTime >= hideAt)
            {
                progressBar.SetVisible(false);
                hideAt = -1f;
            }
        }

        public void Bind(CombatHealth targetHealth, Transform parent, Vector3 offset, float barSmoothSpeed)
        {
            UnbindEvents();
            health = targetHealth;
            barParent = parent;
            localOffset = offset;
            smoothSpeed = Mathf.Max(0.01f, barSmoothSpeed);
            EnsureBar();
            BindEvents();
        }

        private void BindEvents()
        {
            if (health == null)
                return;
            health.Damaged -= HandleDamaged;
            health.Destroyed -= HandleDestroyed;
            health.HealthReset -= HandleReset;
            health.Damaged += HandleDamaged;
            health.Destroyed += HandleDestroyed;
            health.HealthReset += HandleReset;
        }

        private void UnbindEvents()
        {
            if (health == null)
                return;
            health.Damaged -= HandleDamaged;
            health.Destroyed -= HandleDestroyed;
            health.HealthReset -= HandleReset;
        }

        private void HandleDamaged(CombatHealth source, DamageResult result)
        {
            EnsureBar();
            progressBar?.SetProgress(GetDisplayedProgress(source), true);
            hideAt = Time.unscaledTime + Mathf.Max(0f, hideDelay);
        }

        private void HandleDestroyed(CombatHealth source, DamageResult result)
        {
            EnsureBar();
            progressBar?.SetProgress(displayRemainingHealth ? 0f : 1f, true);
            hideAt = Time.unscaledTime + Mathf.Max(0f, hideDelay);
        }

        private void HandleReset(CombatHealth source)
        {
            EnsureBar();
            progressBar?.SetProgress(displayRemainingHealth ? 1f : 0f, false);
            hideAt = -1f;
        }

        private float GetDisplayedProgress(CombatHealth source)
        {
            return displayRemainingHealth ? source.NormalizedHealth : 1f - source.NormalizedHealth;
        }

        private void EnsureBar()
        {
            if (progressBar != null)
                return;
            Transform parent = barParent != null ? barParent : transform;
            progressBar = DoorBreakProgressBar.CreateDefault(parent, localOffset);
            progressBar.smoothSpeed = smoothSpeed;
            progressBar.SetVisible(false);
        }
    }

    [DisallowMultipleComponent]
    public sealed class ImpactObjectFeedback : MonoBehaviour
    {
        public GameObject receiverObject;
        public Transform visualTarget;
        [Min(0.01f)] public float duration = 0.16f;
        [Min(0f)] public float translation = 0.05f;
        [Min(0f)] public float rotationDegrees = 3f;
        [Min(0f)] public float squash = 0.04f;

        private Coroutine routine;

        void OnEnable()
        {
            ImpactEventBus.Impacted += HandleImpact;
        }

        void OnDisable()
        {
            ImpactEventBus.Impacted -= HandleImpact;
        }

        private void HandleImpact(ImpactEvent impact)
        {
            GameObject receiver = receiverObject != null ? receiverObject : gameObject;
            if (impact.damageReceiver != receiver || visualTarget == null)
                return;
            if (routine != null)
                StopCoroutine(routine);
            routine = StartCoroutine(PlayImpact(impact));
        }

        private IEnumerator PlayImpact(ImpactEvent impact)
        {
            Vector3 startPosition = visualTarget.localPosition;
            Quaternion startRotation = visualTarget.localRotation;
            Vector3 startScale = visualTarget.localScale;
            Vector2 direction = impact.direction.sqrMagnitude > 0.000001f ? impact.direction.normalized : Vector2.right;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration));
                float envelope = Mathf.Sin(t * Mathf.PI) * (1f - t * 0.35f);
                visualTarget.localPosition = startPosition + (Vector3)(direction * translation * envelope);
                visualTarget.localRotation = startRotation * Quaternion.Euler(0f, 0f, rotationDegrees * envelope);
                visualTarget.localScale = Vector3.Scale(
                    startScale,
                    new Vector3(1f + squash * envelope, 1f - squash * envelope, 1f));
                yield return null;
            }
            visualTarget.localPosition = startPosition;
            visualTarget.localRotation = startRotation;
            visualTarget.localScale = startScale;
            routine = null;
        }
    }

    [DisallowMultipleComponent]
    public sealed class ImpactAudioFeedback : MonoBehaviour
    {
        public GameObject receiverFilter;
        public AudioSource audioSource;
        public AudioClip[] impactClips;
        [Range(0f, 1f)] public float volume = 0.8f;
        public Vector2 pitchRange = new Vector2(0.94f, 1.06f);

        void OnEnable()
        {
            ImpactEventBus.Impacted += HandleImpact;
        }

        void OnDisable()
        {
            ImpactEventBus.Impacted -= HandleImpact;
        }

        private void HandleImpact(ImpactEvent impact)
        {
            if (receiverFilter != null && impact.damageReceiver != receiverFilter)
                return;
            if (impactClips == null || impactClips.Length == 0)
                return;

            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
                if (audioSource == null)
                {
                    audioSource = gameObject.AddComponent<AudioSource>();
                    audioSource.playOnAwake = false;
                    audioSource.spatialBlend = 1f;
                }
            }

            System.Random random = new System.Random(impact.randomSeed);
            AudioClip clip = impactClips[random.Next(0, impactClips.Length)];
            if (clip == null)
                return;
            audioSource.transform.position = impact.worldPosition;
            audioSource.pitch = Mathf.Lerp(pitchRange.x, pitchRange.y, (float)random.NextDouble());
            audioSource.PlayOneShot(clip, Mathf.Clamp01(volume));
        }
    }
}

using System;
using System.Collections;
using UnityEngine;

namespace DuoCurtain.Combat
{
    [DisallowMultipleComponent]
    public sealed class CombatHealth : MonoBehaviour, IDamageReceiver
    {
        [Header("Health")]
        [Min(0.01f)] public float maxHealth = 100f;
        [SerializeField, Min(0f)] private float currentHealth = 100f;
        public bool initializeAtMaxOnAwake = true;
        public bool invulnerable;
        [Min(0f)] public float destroyDelay;

        [Header("Impact")]
        public ImpactFeedbackPreset defaultImpactPreset;
        public ImpactParameters fallbackImpact = ImpactParameters.DoorBreachDefault;

        private bool destroyed;
        private bool depleted;
        private Coroutine destroyRoutine;

        public event Action<CombatHealth, DamageResult> Damaged;
        public event Action<CombatHealth, DamageResult> Destroyed;
        public event Action<CombatHealth> HealthReset;

        public GameObject ReceiverObject => gameObject;
        public float CurrentHealth => currentHealth;
        public float MaxHealth => maxHealth;
        public float NormalizedHealth => maxHealth > 0f ? Mathf.Clamp01(currentHealth / maxHealth) : 0f;
        public bool IsDestroyed => destroyed;

        void Awake()
        {
            maxHealth = Mathf.Max(0.01f, maxHealth);
            currentHealth = initializeAtMaxOnAwake
                ? maxHealth
                : Mathf.Clamp(currentHealth, 0f, maxHealth);
            destroyed = currentHealth <= 0f;
            depleted = destroyed;
        }

        void OnValidate()
        {
            maxHealth = Mathf.Max(0.01f, maxHealth);
            currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
            destroyDelay = Mathf.Max(0f, destroyDelay);
        }

        public void Configure(float health, bool isInvulnerable, float delay, ImpactFeedbackPreset preset)
        {
            float previousMax = Mathf.Max(0.01f, maxHealth);
            float ratio = Mathf.Clamp01(currentHealth / previousMax);
            maxHealth = Mathf.Max(0.01f, health);
            if (!destroyed)
                currentHealth = Mathf.Approximately(previousMax, maxHealth) ? currentHealth : maxHealth * ratio;
            invulnerable = isInvulnerable;
            destroyDelay = Mathf.Max(0f, delay);
            defaultImpactPreset = preset;
        }

        public DamageResult ReceiveDamage(DamageRequest request)
        {
            float before = currentHealth;
            if (destroyed || depleted || invulnerable || request.amount <= 0f)
                return new DamageResult(false, before, before, destroyed);

            currentHealth = Mathf.Max(0f, currentHealth - request.amount);
            bool reachedZero = currentHealth <= 0.0001f;
            DamageResult result = new DamageResult(true, before, currentHealth, reachedZero);

            ImpactParameters parameters = request.impactPreset != null
                ? request.impactPreset.parameters
                : defaultImpactPreset != null
                    ? defaultImpactPreset.parameters
                    : fallbackImpact;
            ImpactEventBus.Publish(new ImpactEvent(
                request.source,
                gameObject,
                request.worldPosition,
                request.direction,
                request.amount,
                parameters,
                request.randomSeed));
            Damaged?.Invoke(this, result);

            if (reachedZero)
            {
                depleted = true;
                if (destroyDelay <= 0.0001f)
                    FinalizeDestroyed(result);
                else
                    destroyRoutine = StartCoroutine(PublishDestroyedAfterDelay(result));
            }

            return result;
        }

        public void Repair(float amount)
        {
            if (amount <= 0f)
                return;
            if (destroyRoutine != null)
                StopCoroutine(destroyRoutine);
            destroyRoutine = null;
            destroyed = false;
            depleted = false;
            currentHealth = Mathf.Clamp(currentHealth + amount, 0f, maxHealth);
        }

        public void ResetHealth()
        {
            if (destroyRoutine != null)
                StopCoroutine(destroyRoutine);
            destroyRoutine = null;
            currentHealth = maxHealth;
            destroyed = false;
            depleted = false;
            HealthReset?.Invoke(this);
        }

        private IEnumerator PublishDestroyedAfterDelay(DamageResult result)
        {
            yield return new WaitForSeconds(Mathf.Max(0f, destroyDelay));
            destroyRoutine = null;
            FinalizeDestroyed(result);
        }

        private void FinalizeDestroyed(DamageResult result)
        {
            destroyed = true;
            Destroyed?.Invoke(this, result);
            CombatEventBus.PublishDestroyed(gameObject);
        }
    }
}

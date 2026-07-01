using System;
using UnityEngine;

namespace DuoCurtain.Combat
{
    [Serializable]
    public struct ImpactParameters
    {
        [Min(0f)] public float strength;
        [Min(0.01f)] public float radius;
        [Min(0.01f)] public float duration;
        [Min(0.01f)] public float frequency;
        [Range(0f, 1f)] public float directionalWeight;
        [Range(0f, 1f)] public float noiseStrength;
        public AnimationCurve falloff;
        public int priority;

        public static ImpactParameters DoorBreachDefault => new ImpactParameters
        {
            strength = 0.18f,
            radius = 14f,
            duration = 0.22f,
            frequency = 24f,
            directionalWeight = 0.72f,
            noiseStrength = 0.28f,
            falloff = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f),
            priority = 70
        };

        public ImpactParameters Sanitized()
        {
            ImpactParameters value = this;
            value.strength = Mathf.Max(0f, strength);
            value.radius = Mathf.Max(0.01f, radius);
            value.duration = Mathf.Max(0.01f, duration);
            value.frequency = Mathf.Max(0.01f, frequency);
            value.directionalWeight = Mathf.Clamp01(directionalWeight);
            value.noiseStrength = Mathf.Clamp01(noiseStrength);
            value.falloff = falloff != null && falloff.length > 0
                ? new AnimationCurve(falloff.keys)
                : AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
            return value;
        }
    }

    [CreateAssetMenu(fileName = "ImpactFeedbackPreset", menuName = "Duo Curtain/Combat/Impact Feedback Preset")]
    public sealed class ImpactFeedbackPreset : ScriptableObject
    {
        public ImpactParameters parameters = ImpactParameters.DoorBreachDefault;
    }

    public readonly struct ImpactEvent
    {
        public readonly GameObject attackSource;
        public readonly GameObject damageReceiver;
        public readonly Vector3 worldPosition;
        public readonly Vector2 direction;
        public readonly float damage;
        public readonly ImpactParameters parameters;
        public readonly int randomSeed;
        public readonly float timestamp;

        public ImpactEvent(
            GameObject source,
            GameObject receiver,
            Vector3 position,
            Vector2 impactDirection,
            float damageAmount,
            ImpactParameters impactParameters,
            int seed)
        {
            attackSource = source;
            damageReceiver = receiver;
            worldPosition = position;
            direction = impactDirection.sqrMagnitude > 0.000001f ? impactDirection.normalized : Vector2.right;
            damage = Mathf.Max(0f, damageAmount);
            parameters = impactParameters.Sanitized();
            randomSeed = seed;
            timestamp = Time.unscaledTime;
        }
    }

    public static class ImpactEventBus
    {
        public static event Action<ImpactEvent> Impacted;
        public static int PublishedImpactCount { get; private set; }
        public static ImpactEvent LastImpact { get; private set; }
        public static bool HasLastImpact { get; private set; }

        public static void Publish(ImpactEvent impact)
        {
            PublishedImpactCount++;
            LastImpact = impact;
            HasLastImpact = true;
            Impacted?.Invoke(impact);
        }
    }

    public readonly struct DamageReceiverDestroyedEvent
    {
        public readonly GameObject receiver;
        public readonly float timestamp;

        public DamageReceiverDestroyedEvent(GameObject destroyedReceiver)
        {
            receiver = destroyedReceiver;
            timestamp = Time.unscaledTime;
        }
    }

    public static class CombatEventBus
    {
        public static event Action<DamageReceiverDestroyedEvent> DamageReceiverDestroyed;

        public static void PublishDestroyed(GameObject receiver)
        {
            DamageReceiverDestroyed?.Invoke(new DamageReceiverDestroyedEvent(receiver));
        }
    }

    public readonly struct DamageRequest
    {
        public readonly GameObject source;
        public readonly float amount;
        public readonly Vector3 worldPosition;
        public readonly Vector2 direction;
        public readonly ImpactFeedbackPreset impactPreset;
        public readonly int randomSeed;

        public DamageRequest(
            GameObject attackSource,
            float damageAmount,
            Vector3 hitPosition,
            Vector2 hitDirection,
            ImpactFeedbackPreset preset = null,
            int seed = 0)
        {
            source = attackSource;
            amount = Mathf.Max(0f, damageAmount);
            worldPosition = hitPosition;
            direction = hitDirection;
            impactPreset = preset;
            randomSeed = seed != 0 ? seed : UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        }
    }

    public readonly struct DamageResult
    {
        public readonly bool accepted;
        public readonly float previousHealth;
        public readonly float currentHealth;
        public readonly bool destroyed;

        public DamageResult(bool wasAccepted, float before, float after, bool isDestroyed)
        {
            accepted = wasAccepted;
            previousHealth = before;
            currentHealth = after;
            destroyed = isDestroyed;
        }
    }

    public interface IDamageReceiver
    {
        GameObject ReceiverObject { get; }
        float CurrentHealth { get; }
        float MaxHealth { get; }
        float NormalizedHealth { get; }
        bool IsDestroyed { get; }
        DamageResult ReceiveDamage(DamageRequest request);
        void Repair(float amount);
        void ResetHealth();
    }
}

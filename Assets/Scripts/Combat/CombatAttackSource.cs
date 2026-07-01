using System;
using UnityEngine;

namespace DuoCurtain.Combat
{
    [DisallowMultipleComponent]
    public sealed class CombatAttackSource : MonoBehaviour
    {
        public enum AttackPhase
        {
            Idle,
            Windup,
            Recovery
        }

        [Header("Attack")]
        [Min(0f)] public float attackDamage = 20f;
        [Min(0.01f)] public float attackInterval = 1f;
        [Min(0f)] public float windupDuration = 0.25f;
        [Min(0f)] public float recoveryDuration = 0.75f;
        [Min(0f)] public float attackRange = 0.6f;
        public ImpactFeedbackPreset impactPreset;

        [Header("Debug")]
        public bool logAttacks;
        [SerializeField] private AttackPhase currentPhase;
        [SerializeField] private float phaseElapsed;

        private MonoBehaviour targetBehaviour;
        private IDamageReceiver target;

        public event Action<CombatAttackSource, DamageResult> Impacted;
        public event Action<CombatAttackSource, IDamageReceiver> TargetDestroyed;

        public AttackPhase CurrentPhase => currentPhase;
        public float PhaseElapsed => phaseElapsed;
        public float PhaseProgress
        {
            get
            {
                float duration = currentPhase == AttackPhase.Windup
                    ? windupDuration
                    : GetEffectiveRecoveryDuration();
                return duration > 0.0001f ? Mathf.Clamp01(phaseElapsed / duration) : 1f;
            }
        }
        public bool IsAttacking => currentPhase != AttackPhase.Idle && IsTargetValid();

        void Update()
        {
            if (PauseManager.IsGamePaused || currentPhase == AttackPhase.Idle)
                return;
            TickAttack(Time.deltaTime);
        }

        public bool BeginAttack(MonoBehaviour receiver)
        {
            if (!(receiver is IDamageReceiver damageReceiver) || damageReceiver.IsDestroyed)
                return false;

            targetBehaviour = receiver;
            target = damageReceiver;
            currentPhase = AttackPhase.Windup;
            phaseElapsed = 0f;
            return true;
        }

        public void CancelAttack()
        {
            targetBehaviour = null;
            target = null;
            currentPhase = AttackPhase.Idle;
            phaseElapsed = 0f;
        }

        public void TickAttack(float deltaTime)
        {
            if (!IsTargetValid())
            {
                IDamageReceiver previousTarget = target;
                CancelAttack();
                if (previousTarget != null && previousTarget.IsDestroyed)
                    TargetDestroyed?.Invoke(this, previousTarget);
                return;
            }

            if (attackRange > 0f &&
                Vector2.Distance(transform.position, targetBehaviour.transform.position) > attackRange)
            {
                return;
            }

            phaseElapsed += Mathf.Max(0f, deltaTime);
            if (currentPhase == AttackPhase.Windup)
            {
                if (phaseElapsed < Mathf.Max(0f, windupDuration))
                    return;
                PerformImpact();
                currentPhase = AttackPhase.Recovery;
                phaseElapsed = 0f;
                return;
            }

            if (currentPhase == AttackPhase.Recovery && phaseElapsed >= GetEffectiveRecoveryDuration())
            {
                currentPhase = AttackPhase.Windup;
                phaseElapsed = 0f;
            }
        }

        private void PerformImpact()
        {
            if (!IsTargetValid())
                return;

            Vector3 targetPosition = targetBehaviour.transform.position;
            Vector2 direction = (Vector2)(targetPosition - transform.position);
            DamageRequest request = new DamageRequest(
                gameObject,
                attackDamage,
                targetPosition,
                direction,
                impactPreset);
            DamageResult result = target.ReceiveDamage(request);
            if (logAttacks)
            {
                Debug.Log(
                    "[CombatAttack] " + name + " damage=" + attackDamage +
                    " target=" + target.ReceiverObject.name +
                    " hp=" + result.currentHealth + "/" + target.MaxHealth,
                    this);
            }
            Impacted?.Invoke(this, result);
            if (!result.destroyed || !target.IsDestroyed)
                return;

            IDamageReceiver destroyedTarget = target;
            CancelAttack();
            TargetDestroyed?.Invoke(this, destroyedTarget);
        }

        private bool IsTargetValid()
        {
            return target != null && targetBehaviour != null && !target.IsDestroyed;
        }

        private float GetEffectiveRecoveryDuration()
        {
            return Mathf.Max(
                Mathf.Max(0f, recoveryDuration),
                Mathf.Max(0f, attackInterval - Mathf.Max(0f, windupDuration)));
        }
    }
}

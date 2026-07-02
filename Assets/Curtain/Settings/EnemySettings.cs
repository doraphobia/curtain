#if UNITY_EDITOR
using UnityEngine;

namespace Curtain.Settings
{
    [CreateAssetMenu(menuName = "Curtain/Settings/Enemy Settings", fileName = "EnemySettings")]
    public sealed class EnemySettings : ScriptableObject
    {
        [Header("Movement")]
        [Min(0f)] public float moveSpeed = 2.5f;
        [Min(0f)] public float doorTargetingSpeedMultiplier = 1.65f;
        [Min(0f)] public float rotationSpeed = 360f;
        [Min(0f)] public float stoppingDistance = 0.6f;
        [Min(0f)] public float doorApproachDistance = 0.4f;

        [Header("Search")]
        [Min(0.01f)] public float searchInterval = 0.25f;
        [Min(0f)] public float lostSightDelay = 1f;
        [Min(0f)] public float investigateDuration = 2f;
        [Min(0f)] public float enterRoomDelay = 0.4f;
        public bool chaseLastKnownRoom = true;
        [Min(0f)] public float roomMemoryDuration = 5f;

        [Header("Vision")]
        [Min(0f)] public float viewDistance = 8f;
        [Range(1f, 179f)] public float viewAngle = 90f;
        [Min(0f)] public float detectionConfirmTime = 0.5f;

        [Header("Window Vision")]
        public bool requireOpenWindow = true;
        [Min(1)] public int windowVisionSampleCount = 5;
        [Min(0f)] public float windowVisionSamplePadding = 0.05f;
        [Min(0.01f)] public float windowCheckInterval = 0.1f;

        [Header("Attack")]
        [Min(0f)] public float attackRange = 0.8f;
        [Min(0f)] public float attackDamage = 1f;
        [Min(0f)] public float attackCooldown = 1.5f;
        [Min(0f)] public float attackWindupTime = 0.4f;

        [Header("Door Attack")]
        [Min(0f)] public float doorAttackDamage = 20f;
        [Min(0.01f)] public float doorAttackInterval = 1f;
        [Min(0f)] public float doorAttackWindup = 0.25f;
        [Min(0f)] public float doorAttackRecovery = 0.75f;
        [Min(0f)] public float doorAttackRange = 0.6f;

        [Header("Spawn")]
        [Min(0f)] public float spawnNearPlayerMinDistance = 2f;
        [Min(0f)] public float spawnNearPlayerMaxDistance = 12f;
        public bool autoRelocateInvalidSpawn = true;

        [Header("Debug")]
        public bool drawVisionCone = true;
        public bool drawLineOfSight = true;
        public bool drawWindowVisionSamples = true;
        public bool logStateChanges = true;
    }
}

#endif


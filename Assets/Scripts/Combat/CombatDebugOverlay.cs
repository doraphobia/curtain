using System.Text;
using UnityEngine;

namespace DuoCurtain.Combat
{
    [DisallowMultipleComponent]
    public sealed class CombatDebugOverlay : MonoBehaviour
    {
        [Header("Display")]
        public bool showOverlay;
        public bool toggleWithKey = true;
        public KeyCode toggleKey = KeyCode.F9;
        public Rect panelRect = new Rect(12f, 12f, 460f, 360f);
        [Min(8)] public int fontSize = 13;
        public Color textColor = Color.white;
        public Color backgroundColor = new Color(0f, 0f, 0f, 0.72f);

        [Header("Refresh")]
        [Min(0.05f)] public float refreshInterval = 0.5f;
        [Min(1)] public int maxAttackSources = 8;
        [Min(1)] public int maxDamageReceivers = 10;

        private CombatAttackSource[] attackSources = new CombatAttackSource[0];
        private CombatHealth[] damageReceivers = new CombatHealth[0];
        private ImpactCameraFeedback cameraFeedback;
        private float nextRefreshAt;
        private readonly StringBuilder builder = new StringBuilder(2048);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureRuntimeInstance()
        {
            if (FindFirstObjectByType<CombatDebugOverlay>() != null)
                return;

            GameObject root = new GameObject("Combat Debug Overlay");
            root.hideFlags = HideFlags.DontSave;
            root.AddComponent<CombatDebugOverlay>();
        }

        void Update()
        {
            if (toggleWithKey && Input.GetKeyDown(toggleKey))
                showOverlay = !showOverlay;

            if (!showOverlay)
                return;

            if (Time.unscaledTime >= nextRefreshAt)
                RefreshCache();
        }

        void OnGUI()
        {
            if (!showOverlay)
                return;

            if (Time.unscaledTime >= nextRefreshAt)
                RefreshCache();

            Color previousColor = GUI.color;
            GUI.color = backgroundColor;
            GUI.Box(panelRect, GUIContent.none);
            GUI.color = textColor;

            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                richText = false,
                wordWrap = false
            };

            GUILayout.BeginArea(new Rect(
                panelRect.x + 10f,
                panelRect.y + 8f,
                Mathf.Max(1f, panelRect.width - 20f),
                Mathf.Max(1f, panelRect.height - 16f)));
            GUILayout.Label(BuildSnapshot(), style);
            GUILayout.EndArea();
            GUI.color = previousColor;
        }

        public string BuildSnapshot()
        {
            builder.Length = 0;
            builder.AppendLine("Combat Debug");

            if (CurrentCameraService.TryGetCurrentGameplayCamera(out Camera camera))
                builder.AppendLine("Current Camera: " + camera.name);
            else
                builder.AppendLine("Current Camera: none");

            builder.AppendLine("Published Impacts: " + ImpactEventBus.PublishedImpactCount);
            if (ImpactEventBus.HasLastImpact)
            {
                ImpactEvent impact = ImpactEventBus.LastImpact;
                builder.AppendLine(
                    "Last Impact: receiver=" +
                    (impact.damageReceiver != null ? impact.damageReceiver.name : "none") +
                    " damage=" + impact.damage.ToString("0.0") +
                    " pos=" + FormatVector(impact.worldPosition));
            }

            if (cameraFeedback == null)
                cameraFeedback = FindFirstObjectByType<ImpactCameraFeedback>();

            if (cameraFeedback != null)
            {
                builder.AppendLine(
                    "Camera Feedback: shakes=" + cameraFeedback.ActiveShakeCount +
                    " radius=" + cameraFeedback.DebugMaximumRadius.ToString("0.0") +
                    " dir=" + FormatVector(cameraFeedback.LastShakeDirection) +
                    " strength=" + cameraFeedback.LastShakeStrength.ToString("0.000"));
            }
            else
            {
                builder.AppendLine("Camera Feedback: none");
            }

            builder.AppendLine();
            builder.AppendLine("Attack Sources");
            int attackLimit = Mathf.Min(attackSources.Length, maxAttackSources);
            if (attackLimit == 0)
                builder.AppendLine("  none");
            for (int i = 0; i < attackLimit; i++)
            {
                CombatAttackSource source = attackSources[i];
                if (source == null)
                    continue;
                builder.AppendLine(
                    "  " + source.name +
                    " phase=" + source.CurrentPhase +
                    " progress=" + source.PhaseProgress.ToString("0.00") +
                    " target=" + source.TargetName +
                    " distance=" + source.TargetDistance.ToString("0.00"));
            }

            builder.AppendLine();
            builder.AppendLine("Damage Receivers");
            int receiverLimit = Mathf.Min(damageReceivers.Length, maxDamageReceivers);
            if (receiverLimit == 0)
                builder.AppendLine("  none");
            for (int i = 0; i < receiverLimit; i++)
            {
                CombatHealth health = damageReceivers[i];
                if (health == null)
                    continue;
                builder.AppendLine(
                    "  " + health.name +
                    " hp=" + health.CurrentHealth.ToString("0.0") +
                    "/" + health.MaxHealth.ToString("0.0") +
                    " destroyed=" + health.IsDestroyed);
            }

            return builder.ToString();
        }

        private void RefreshCache()
        {
            attackSources = FindObjectsByType<CombatAttackSource>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            damageReceivers = FindObjectsByType<CombatHealth>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            cameraFeedback = FindFirstObjectByType<ImpactCameraFeedback>();
            nextRefreshAt = Time.unscaledTime + Mathf.Max(0.05f, refreshInterval);
        }

        private static string FormatVector(Vector2 value)
        {
            return "(" + value.x.ToString("0.00") + ", " + value.y.ToString("0.00") + ")";
        }

        private static string FormatVector(Vector3 value)
        {
            return "(" + value.x.ToString("0.00") + ", " + value.y.ToString("0.00") + ", " + value.z.ToString("0.00") + ")";
        }
    }
}

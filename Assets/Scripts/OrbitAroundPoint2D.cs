using UnityEngine;

[DisallowMultipleComponent]
public class OrbitAroundPoint2D : MonoBehaviour
{
    public enum MotionMode
    {
        OrbitAroundPoint,
        SelfRotate,
        OrbitAndSelfRotate
    }

    public enum StageProgressSource
    {
        [Tooltip("当前阶段内：StageTimer / CurrentStageDuration，0→1 对应一个阶段")]
        CurrentStageNormalized,
        [Tooltip("整轮循环：已流逝时间 / 各阶段 duration 之和，0→1 对应从当前轮起点回到起点")]
        FullCycleNormalized
    }

    [Header("Mode")]
    public MotionMode motionMode = MotionMode.OrbitAroundPoint;

    [Header("Stage progress (optional)")]
    [Tooltip("若赋值且 useStageProgress 为真，则角度由阶段进度驱动；否则仍用下方「每秒角度」累计")]
    public StageCycleController stageController;
    public bool useStageProgress = true;
    public StageProgressSource stageProgressSource = StageProgressSource.FullCycleNormalized;
    [Tooltip("进度从 0→1 时，轨道角走过的总度数（例如 360 = 每单位进度转一圈）")]
    public float orbitDegreesPerProgressUnit = 360f;
    [Tooltip("进度从 0→1 时，自转角走过的总度数（仅 SelfRotate / OrbitAndSelfRotate）")]
    public float selfRotationDegreesPerProgressUnit = 360f;

    [Header("Orbit Center")]
    public Transform centerPoint;
    public Vector2 centerPosition;
    public bool useCenterTransform = true;

    [Header("Motion")]
    public float radius = 1f;
    public float degreesPerSecond = 90f;
    public float startAngleDegrees = 0f;

    [Header("Rotation")]
    public float selfRotationDegreesPerSecond = 180f;
    public bool faceMovementDirection = false;
    public bool faceOrbitCenter = false;
    public float spriteForwardOffset = -90f;

    private float currentAngleDegrees;
    private float currentSelfRotationDegrees;

    void Start()
    {
        if (stageController == null)
            stageController = FindFirstObjectByType<StageCycleController>();

        currentAngleDegrees = startAngleDegrees;
        currentSelfRotationDegrees = startAngleDegrees;
        ApplyMotion();
    }

    void Update()
    {
        if (useStageProgress && stageController != null)
        {
            float t = GetStageProgress01Clamped();
            currentAngleDegrees = startAngleDegrees + t * orbitDegreesPerProgressUnit;
            currentSelfRotationDegrees = startAngleDegrees + t * selfRotationDegreesPerProgressUnit;
        }
        else
        {
            currentAngleDegrees += degreesPerSecond * Time.deltaTime;
            currentSelfRotationDegrees += selfRotationDegreesPerSecond * Time.deltaTime;
        }

        ApplyMotion();
    }

    private float GetStageProgress01Clamped()
    {
        if (stageController == null)
            return 0f;

        switch (stageProgressSource)
        {
            case StageProgressSource.CurrentStageNormalized:
            {
                float d = stageController.CurrentStageDuration;
                if (d <= 0f)
                    return 0f;
                return Mathf.Clamp01(stageController.StageTimer / d);
            }

            case StageProgressSource.FullCycleNormalized:
            default:
                return GetFullCycleProgress01(stageController);
        }
    }

    private static float GetFullCycleProgress01(StageCycleController ctrl)
    {
        if (ctrl.stages == null || ctrl.stages.Count == 0)
            return 0f;

        float total = 0f;
        for (int i = 0; i < ctrl.stages.Count; i++)
        {
            if (ctrl.stages[i] != null)
                total += Mathf.Max(0.01f, ctrl.stages[i].duration);
        }

        if (total <= 0f)
            return 0f;

        int idx = ctrl.GetCurrentStageIndex();
        float elapsed = 0f;
        for (int i = 0; i < idx && i < ctrl.stages.Count; i++)
        {
            if (ctrl.stages[i] != null)
                elapsed += Mathf.Max(0.01f, ctrl.stages[i].duration);
        }

        elapsed += ctrl.StageTimer;
        return Mathf.Clamp01(elapsed / total);
    }

    private void ApplyMotion()
    {
        if (motionMode == MotionMode.SelfRotate)
        {
            ApplySelfRotation();
            return;
        }

        if (motionMode == MotionMode.OrbitAndSelfRotate)
        {
            ApplyOrbitPosition(false);
            ApplySelfRotation();
            return;
        }

        ApplyOrbitPosition(faceMovementDirection);
    }

    private void ApplyOrbitPosition(bool rotateToMovementDirection)
    {
        Vector2 center = GetCenter();
        float radians = currentAngleDegrees * Mathf.Deg2Rad;
        Vector2 offset = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * radius;
        Vector3 nextPosition = new Vector3(center.x + offset.x, center.y + offset.y, transform.position.z);

        if (faceOrbitCenter)
        {
            Vector2 toCenter = center - new Vector2(nextPosition.x, nextPosition.y);
            if (toCenter.sqrMagnitude > 0.0001f)
            {
                float angle = Mathf.Atan2(toCenter.y, toCenter.x) * Mathf.Rad2Deg + spriteForwardOffset;
                transform.rotation = Quaternion.Euler(0f, 0f, angle);
            }
        }
        else if (rotateToMovementDirection)
        {
            float orbitSign = GetOrbitTangentSign();
            Vector2 tangent = new Vector2(-Mathf.Sin(radians), Mathf.Cos(radians)) * orbitSign;
            if (tangent.sqrMagnitude > 0.0001f)
            {
                float angle = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg + spriteForwardOffset;
                transform.rotation = Quaternion.Euler(0f, 0f, angle);
            }
        }

        transform.position = nextPosition;
    }

    private void ApplySelfRotation()
    {
        transform.rotation = Quaternion.Euler(0f, 0f, currentSelfRotationDegrees + spriteForwardOffset);
    }

    private Vector2 GetCenter()
    {
        if (useCenterTransform && centerPoint != null)
            return centerPoint.position;

        return centerPosition;
    }

    private float GetOrbitTangentSign()
    {
        if (useStageProgress && stageController != null)
        {
            if (Mathf.Abs(orbitDegreesPerProgressUnit) > 0.0001f)
                return Mathf.Sign(orbitDegreesPerProgressUnit);
            return 1f;
        }

        if (Mathf.Abs(degreesPerSecond) > 0.0001f)
            return Mathf.Sign(degreesPerSecond);
        return 1f;
    }
}

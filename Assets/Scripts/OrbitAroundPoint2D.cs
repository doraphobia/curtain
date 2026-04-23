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

    [Header("Mode")]
    public MotionMode motionMode = MotionMode.OrbitAroundPoint;

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
        currentAngleDegrees = startAngleDegrees;
        currentSelfRotationDegrees = startAngleDegrees;
        ApplyMotion();
    }

    void Update()
    {
        currentAngleDegrees += degreesPerSecond * Time.deltaTime;
        currentSelfRotationDegrees += selfRotationDegreesPerSecond * Time.deltaTime;
        ApplyMotion();
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
            Vector2 tangent = new Vector2(-Mathf.Sin(radians), Mathf.Cos(radians)) * Mathf.Sign(degreesPerSecond);
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
}

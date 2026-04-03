using UnityEngine;

[DisallowMultipleComponent]
public class OrbitAroundPoint2D : MonoBehaviour
{
    [Header("Orbit Center")]
    public Transform centerPoint;
    public Vector2 centerPosition;
    public bool useCenterTransform = true;

    [Header("Motion")]
    public float radius = 1f;
    public float degreesPerSecond = 90f;
    public float startAngleDegrees = 0f;

    [Header("Rotation")]
    public bool faceMovementDirection = false;
    public float spriteForwardOffset = -90f;

    private float currentAngleDegrees;

    void Start()
    {
        currentAngleDegrees = startAngleDegrees;
        ApplyOrbitPosition();
    }

    void Update()
    {
        currentAngleDegrees += degreesPerSecond * Time.deltaTime;
        ApplyOrbitPosition();
    }

    private void ApplyOrbitPosition()
    {
        Vector2 center = GetCenter();
        float radians = currentAngleDegrees * Mathf.Deg2Rad;
        Vector2 offset = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * radius;
        Vector3 nextPosition = new Vector3(center.x + offset.x, center.y + offset.y, transform.position.z);

        if (faceMovementDirection)
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

    private Vector2 GetCenter()
    {
        if (useCenterTransform && centerPoint != null)
            return centerPoint.position;

        return centerPosition;
    }
}

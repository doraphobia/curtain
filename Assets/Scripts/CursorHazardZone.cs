using UnityEngine;

[RequireComponent(typeof(Collider2D))]
[DisallowMultipleComponent]
public class CursorHazardZone : MonoBehaviour
{
    [Header("References")]
    public Camera targetCamera;
    public SanitySystem sanitySystem;
    public Collider2D targetCollider;

    [Header("Sanity")]
    public float sanityDrainPerSecond = 5f;

    [Header("Camera Shake")]
    public float shakeStrength = 0.12f;
    public float shakeSpeed = 30f;

    private Vector3 baseCameraPosition;
    private bool isShaking;

    void Awake()
    {
        if (targetCollider == null)
            targetCollider = GetComponent<Collider2D>();

        if (targetCamera == null)
            targetCamera = Camera.main;

        if (sanitySystem == null)
            sanitySystem = FindFirstObjectByType<SanitySystem>();
    }

    void LateUpdate()
    {
        if (targetCollider == null || targetCamera == null)
            return;

        if (IsCursorOverTarget())
        {
            DrainSanity();
            ShakeCamera();
        }
        else
        {
            if (isShaking)
            {
                RestoreCameraPosition();
                isShaking = false;
            }
        }
    }

    private bool IsCursorOverTarget()
    {
        Vector3 mouseWorld;
        if (!PlayerControl.TryGetInteractionWorldPosition(out mouseWorld))
            mouseWorld = targetCamera.ScreenToWorldPoint(Input.mousePosition);

        mouseWorld.z = transform.position.z;
        return targetCollider.OverlapPoint(mouseWorld);
    }

    private void DrainSanity()
    {
        if (sanitySystem == null)
            return;

        sanitySystem.AddSanity(-sanityDrainPerSecond * Time.deltaTime);
    }

    private void ShakeCamera()
    {
        baseCameraPosition = targetCamera.transform.position;

        float time = Time.time * shakeSpeed;
        Vector3 offset = new Vector3(
            Mathf.PerlinNoise(time, 0f) - 0.5f,
            Mathf.PerlinNoise(0f, time) - 0.5f,
            0f
        ) * shakeStrength;

        targetCamera.transform.position = baseCameraPosition + offset;
        isShaking = true;
    }

    private void RestoreCameraPosition()
    {
        targetCamera.transform.position = baseCameraPosition;
    }
}

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

    private Vector3 originalCameraPosition;
    private bool cameraPositionCached;

    void Awake()
    {
        if (targetCollider == null)
            targetCollider = GetComponent<Collider2D>();

        if (targetCamera == null)
            targetCamera = Camera.main;

        if (sanitySystem == null)
            sanitySystem = FindFirstObjectByType<SanitySystem>();

        CacheCameraPosition();
    }

    void LateUpdate()
    {
        if (targetCollider == null || targetCamera == null)
            return;

        CacheCameraPosition();

        if (IsCursorOverTarget())
        {
            DrainSanity();
            ShakeCamera();
        }
        else
        {
            RestoreCameraPosition();
        }
    }

    private bool IsCursorOverTarget()
    {
        Vector3 mouseWorld = targetCamera.ScreenToWorldPoint(Input.mousePosition);
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
        float time = Time.time * shakeSpeed;
        Vector3 offset = new Vector3(
            Mathf.PerlinNoise(time, 0f) - 0.5f,
            Mathf.PerlinNoise(0f, time) - 0.5f,
            0f
        ) * shakeStrength;

        targetCamera.transform.position = originalCameraPosition + offset;
    }

    private void RestoreCameraPosition()
    {
        targetCamera.transform.position = originalCameraPosition;
    }

    private void CacheCameraPosition()
    {
        if (targetCamera == null)
            return;

        if (!cameraPositionCached)
        {
            originalCameraPosition = targetCamera.transform.position;
            cameraPositionCached = true;
        }
    }
}

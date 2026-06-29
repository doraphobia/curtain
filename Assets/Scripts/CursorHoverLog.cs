using UnityEngine;

[DisallowMultipleComponent]
public class CursorHoverLog : MonoBehaviour
{
    [Header("Messages")]
    public string enterMessage = "Cursor is over this object";
    public string overMessage = "Cursor is over this object";
    public string exitMessage = "Cursor left the object";

    [Header("Behavior")]
    [Tooltip("默认关闭，避免每帧刷屏。")]
    public bool logOverEveryFrame = false;
    [Min(0f)]
    public float overLogInterval = 0.25f;

    [Header("References")]
    public Camera targetCamera;
    public Collider2D targetCollider2D;
    public Collider targetCollider3D;

    private bool wasHovering;
    private float nextOverLogTime;

    void Awake()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        if (targetCollider2D == null)
            targetCollider2D = GetComponent<Collider2D>();

        if (targetCollider3D == null)
            targetCollider3D = GetComponent<Collider>();
    }

    void Update()
    {
        if (!TryGetCursorWorldPoint(out Vector3 cursorWorldPoint))
            return;

        bool isHovering = IsHovering(cursorWorldPoint);

        if (isHovering && !wasHovering)
            Debug.Log(enterMessage);

        if (isHovering && logOverEveryFrame && Time.unscaledTime >= nextOverLogTime)
        {
            Debug.Log(overMessage);
            nextOverLogTime = Time.unscaledTime + overLogInterval;
        }

        if (!isHovering && wasHovering)
            Debug.Log(exitMessage);

        wasHovering = isHovering;
    }

    private bool TryGetCursorWorldPoint(out Vector3 cursorWorldPoint)
    {
        if (PlayerControl.TryGetInteractionWorldPosition(out cursorWorldPoint))
            return true;

        if (targetCamera == null)
        {
            cursorWorldPoint = default;
            return false;
        }

        cursorWorldPoint = targetCamera.ScreenToWorldPoint(Input.mousePosition);
        cursorWorldPoint.z = 0f;
        return true;
    }

    private bool IsHovering(Vector3 worldPoint)
    {
        if (targetCollider2D != null)
        {
            worldPoint.z = transform.position.z;
            return targetCollider2D.OverlapPoint(worldPoint);
        }

        if (targetCollider3D != null)
        {
            Vector3 boundsPoint = new Vector3(
                worldPoint.x,
                worldPoint.y,
                targetCollider3D.bounds.center.z
            );
            return targetCollider3D.bounds.Contains(boundsPoint);
        }

        return false;
    }
}

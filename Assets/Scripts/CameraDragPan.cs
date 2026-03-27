using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public class CameraDragPan : MonoBehaviour
{
    [Header("Target")]
    public Camera targetCamera;

    [Header("Input")]
    public int mouseButton = 0;
    public bool ignoreWhenPointerOverUI = true;

    [Header("Movement")]
    public bool invertDrag = false;
    public bool clampPosition = false;
    public Vector2 minPosition = new Vector2(-10f, -10f);
    public Vector2 maxPosition = new Vector2(10f, 10f);

    private bool isDragging;
    private Vector3 lastWorldPoint;

    void Awake()
    {
        if (targetCamera == null)
            targetCamera = GetComponent<Camera>();

        if (targetCamera == null)
            targetCamera = Camera.main;
    }

    void Update()
    {
        if (targetCamera == null)
            return;

        if (Input.GetMouseButtonDown(mouseButton))
        {
            if (ignoreWhenPointerOverUI && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            isDragging = true;
            lastWorldPoint = GetMouseWorldPoint();
        }

        if (Input.GetMouseButtonUp(mouseButton))
        {
            isDragging = false;
        }

        if (!isDragging)
            return;

        Vector3 currentWorldPoint = GetMouseWorldPoint();
        Vector3 delta = lastWorldPoint - currentWorldPoint;

        if (invertDrag)
            delta = -delta;

        Vector3 nextPosition = targetCamera.transform.position + new Vector3(delta.x, delta.y, 0f);

        if (clampPosition)
        {
            nextPosition.x = Mathf.Clamp(nextPosition.x, minPosition.x, maxPosition.x);
            nextPosition.y = Mathf.Clamp(nextPosition.y, minPosition.y, maxPosition.y);
        }

        targetCamera.transform.position = nextPosition;
        lastWorldPoint = GetMouseWorldPoint();
    }

    private Vector3 GetMouseWorldPoint()
    {
        Vector3 mousePosition = Input.mousePosition;
        mousePosition.z = Mathf.Abs(targetCamera.transform.position.z);

        Vector3 worldPoint = targetCamera.ScreenToWorldPoint(mousePosition);
        worldPoint.z = targetCamera.transform.position.z;
        return worldPoint;
    }
}

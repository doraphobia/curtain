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
    public bool disableWhenLogicalCursorActive = true;

    private bool isDragging;
    private Vector3 dragStartMousePosition;
    private Vector3 dragStartCameraPosition;

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

        if (disableWhenLogicalCursorActive && LogicalCursorController.IsRunning)
            return;

        if (Input.GetMouseButtonDown(mouseButton))
        {
            if (ignoreWhenPointerOverUI && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            isDragging = true;
            dragStartMousePosition = Input.mousePosition;
            dragStartCameraPosition = targetCamera.transform.position;
        }

        if (Input.GetMouseButtonUp(mouseButton))
        {
            isDragging = false;
        }

        if (!isDragging)
            return;

        Vector3 mouseDelta = Input.mousePosition - dragStartMousePosition;
        Vector3 worldDelta = ScreenDeltaToWorldDelta(mouseDelta);

        if (invertDrag)
            worldDelta = -worldDelta;

        Vector3 nextPosition = dragStartCameraPosition - new Vector3(worldDelta.x, worldDelta.y, 0f);

        if (clampPosition)
        {
            nextPosition.x = Mathf.Clamp(nextPosition.x, minPosition.x, maxPosition.x);
            nextPosition.y = Mathf.Clamp(nextPosition.y, minPosition.y, maxPosition.y);
        }

        targetCamera.transform.position = nextPosition;
    }

    private Vector3 ScreenDeltaToWorldDelta(Vector3 screenDelta)
    {
        if (targetCamera.orthographic)
        {
            float worldHeight = targetCamera.orthographicSize * 2f;
            float worldWidth = worldHeight * targetCamera.aspect;
            return new Vector3(
                screenDelta.x / Screen.width * worldWidth,
                screenDelta.y / Screen.height * worldHeight,
                0f
            );
        }

        Vector3 startScreen = dragStartMousePosition;
        startScreen.z = Mathf.Abs(targetCamera.transform.position.z);

        Vector3 endScreen = dragStartMousePosition + screenDelta;
        endScreen.z = startScreen.z;

        return targetCamera.ScreenToWorldPoint(endScreen) - targetCamera.ScreenToWorldPoint(startScreen);
    }
}

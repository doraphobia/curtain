using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public class ObjectAreaCursorLimiter2D : MonoBehaviour
{
    [Header("References")]
    public Camera targetCamera;

    [Header("Detection")]
    public LayerMask detectableLayers = ~0;
    public bool includeTriggers = true;
    public bool treatUIAsValidArea = true;

    private bool lastCursorVisible = true;

    void Awake()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;
    }

    void Update()
    {
        if (LogicalCursorController.IsRunning)
            return;

        bool shouldShowCursor = IsCursorOverValidArea();

        if (lastCursorVisible == shouldShowCursor)
            return;

        Cursor.visible = shouldShowCursor;
        lastCursorVisible = shouldShowCursor;
    }

    void OnDisable()
    {
        Cursor.visible = true;
        lastCursorVisible = true;
    }

    private bool IsCursorOverValidArea()
    {
        if (treatUIAsValidArea && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return true;

        if (targetCamera == null)
            return false;

        Vector3 mouseWorld = targetCamera.ScreenToWorldPoint(Input.mousePosition);
        Vector2 point = new Vector2(mouseWorld.x, mouseWorld.y);

        if (includeTriggers)
            return Physics2D.OverlapPoint(point, detectableLayers) != null;

        Collider2D[] colliders = Physics2D.OverlapPointAll(point, detectableLayers);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider2D col = colliders[i];
            if (col != null && !col.isTrigger)
                return true;
        }

        return false;
    }
}

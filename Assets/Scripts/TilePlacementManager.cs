using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public class TilePlacementManager : MonoBehaviour
{
    [Header("References")]
    public Camera targetCamera;
    public TilePlacementGrid placementGrid;
    public TimeCounterUI currencySource;

    [Header("Preview")]
    public Color validTint = new Color(0.5f, 1f, 0.5f, 0.75f);
    public Color invalidTint = new Color(1f, 0.4f, 0.4f, 0.75f);
    public int previewSortingOrder = 200;

    private TilePieceDefinition pendingDefinition;
    private GameObject previewInstance;
    private SpriteRenderer[] previewRenderers;
    private readonly Dictionary<SpriteRenderer, Color> previewOriginalColors = new Dictionary<SpriteRenderer, Color>();
    private Vector2Int previewAnchorCell;
    private bool previewCanPlace;
    private int pendingPrice;

    void Awake()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        if (placementGrid == null)
            placementGrid = FindFirstObjectByType<TilePlacementGrid>();
    }

    void Update()
    {
        if (pendingDefinition == null || previewInstance == null || placementGrid == null || targetCamera == null)
            return;

        UpdatePreviewPosition();

        if (Input.GetMouseButtonDown(1))
        {
            CancelPlacement();
            return;
        }

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        if (Input.GetMouseButtonDown(0) && previewCanPlace)
            ConfirmPlacement();
    }

    public bool TryBeginPlacement(TilePieceDefinition prefab)
    {
        if (prefab == null || placementGrid == null || currencySource == null)
            return false;

        CancelPlacement(true);

        int price = prefab.shopData.price;
        if (!currencySource.TrySpend(price))
            return false;

        pendingDefinition = prefab;
        pendingPrice = DeveloperModeState.IsEnabled ? 0 : price;
        previewInstance = Instantiate(prefab.gameObject);
        previewInstance.name = prefab.gameObject.name + "_Preview";

        DisablePreviewColliders(previewInstance);
        CachePreviewRenderers(previewInstance);
        UpdatePreviewPosition();
        return true;
    }

    public void CancelPlacement()
    {
        CancelPlacement(true);
    }

    public void CancelPlacement(bool refundCurrency)
    {
        if (refundCurrency && pendingPrice > 0 && currencySource != null)
            currencySource.AddValue(pendingPrice);

        pendingDefinition = null;
        pendingPrice = 0;

        if (previewInstance != null)
            Destroy(previewInstance);

        previewInstance = null;
        previewRenderers = null;
        previewOriginalColors.Clear();
    }

    private void ConfirmPlacement()
    {
        GameObject placedInstance = Instantiate(
            pendingDefinition.gameObject,
            placementGrid.CellToWorld(previewAnchorCell),
            pendingDefinition.transform.rotation
        );
        placedInstance.name = pendingDefinition.gameObject.name;

        TilePieceDefinition placedDefinition = placedInstance.GetComponent<TilePieceDefinition>();
        if (placedDefinition == null)
            placedDefinition = placedInstance.AddComponent<TilePieceDefinition>();

        placedDefinition.registerOnStart = false;
        placedDefinition.cells = new List<Vector2Int>(pendingDefinition.cells);
        placedDefinition.shopData.displayName = pendingDefinition.shopData.displayName;
        placedDefinition.shopData.price = pendingDefinition.shopData.price;
        placedDefinition.placementLayer = pendingDefinition.placementLayer;

        placementGrid.RegisterPiece(placedDefinition, previewAnchorCell);
        BeginConstructionIfPresent(placedInstance);
        CancelPlacement(false);
    }

    private void UpdatePreviewPosition()
    {
        Vector3 mouseWorld;
        if (!PlayerControl.TryGetHeadingWorldPosition(out mouseWorld) &&
            !PlayerControl.TryGetPlayerWorldPosition(out mouseWorld))
        {
            mouseWorld = targetCamera.ScreenToWorldPoint(Input.mousePosition);
            mouseWorld.z = 0f;
        }

        previewAnchorCell = placementGrid.WorldToCell(mouseWorld);
        previewCanPlace = placementGrid.CanPlace(pendingDefinition, previewAnchorCell);

        previewInstance.transform.position = placementGrid.CellToWorld(previewAnchorCell);
        ApplyPreviewTint(previewCanPlace ? validTint : invalidTint);
    }

    private void CachePreviewRenderers(GameObject instance)
    {
        previewRenderers = instance.GetComponentsInChildren<SpriteRenderer>(true);
        previewOriginalColors.Clear();

        for (int i = 0; i < previewRenderers.Length; i++)
        {
            SpriteRenderer sr = previewRenderers[i];
            if (sr == null)
                continue;

            previewOriginalColors[sr] = sr.color;
            sr.sortingOrder = previewSortingOrder;
        }
    }

    private void ApplyPreviewTint(Color tint)
    {
        if (previewRenderers == null)
            return;

        for (int i = 0; i < previewRenderers.Length; i++)
        {
            SpriteRenderer sr = previewRenderers[i];
            if (sr == null)
                continue;

            Color baseColor = previewOriginalColors.TryGetValue(sr, out Color original) ? original : Color.white;
            sr.color = new Color(
                baseColor.r * tint.r,
                baseColor.g * tint.g,
                baseColor.b * tint.b,
                tint.a
            );
        }
    }

    private void DisablePreviewColliders(GameObject instance)
    {
        Collider2D[] colliders2D = instance.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders2D.Length; i++)
        {
            colliders2D[i].enabled = false;
        }
    }

    private void BeginConstructionIfPresent(GameObject instance)
    {
        RoomConstructionController[] controllers = instance.GetComponentsInChildren<RoomConstructionController>(true);
        for (int i = 0; i < controllers.Length; i++)
        {
            if (controllers[i] != null)
                controllers[i].BeginConstruction();
        }
    }
}

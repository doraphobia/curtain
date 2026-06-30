using System.Collections.Generic;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DuoCurtain.RuntimeTileMesh
{
    [DisallowMultipleComponent]
    public sealed class FusionNightEnemySpawner : MonoBehaviour
    {
        [Header("References")]
        public StageCycleController stageController;
        public RuntimeTileMeshFusionSandbox fusionSandbox;
        public PlayerControl playerControl;
        public Transform footprintParent;

        [Header("Spawn Rules")]
        [Min(0)]
        public int enemiesPerNight = 1;
        [Min(1)]
        public int maxActiveEnemies = 5;
        [Min(0.5f)]
        public float spawnPadding = 2.5f;
        public bool spawnImmediatelyIfAlreadyNight = true;

        [Header("Spawn Warning")]
        public bool warnBeforeOffscreenSpawn = true;
        public bool warnOnlyWhenOffscreen = true;
        [Min(0f)]
        public float spawnWarningLeadTime = 1.25f;
        public string spawnWarningText = "SOMETHING IS OUTSIDE";
        public Color spawnWarningTextColor = new Color(1f, 0.95f, 0.78f, 1f);
        public Color spawnWarningPanelColor = new Color(0f, 0f, 0f, 0.34f);
        [Min(1f)]
        public float spawnWarningFontSize = 42f;
        public Vector2 spawnWarningAnchoredPosition = new Vector2(0f, 300f);
        public int spawnWarningCanvasSortingOrder = 1350;
        public bool showGridCellSpawnWarning = true;
        public GameObject spawnWarningPrefab;
        public Color spawnWarningColor = new Color(1f, 0.1f, 0.08f, 0.68f);
        public AnimationCurve warningPulseCurve = AnimationCurve.EaseInOut(0f, 0.35f, 1f, 1f);
        public bool blockSpawnIfPlayerEntersWarningCell = true;
        public bool cancelSpawnIfCellBecomesInvalid = true;
        [Min(1)]
        public int spawnCandidateAttempts = 32;

        [Header("Enemy Defaults")]
        [Min(0f)]
        public float enemyMoveSpeed = 2.1f;
        [Min(0.1f)]
        public float windowDetectionDistance = 12f;
        [Min(0.1f)]
        public float playerWindowDistance = 8f;
        public bool requireOpenWindow = true;

        [Header("Footprint Sprite")]
        public Color footprintColor = new Color(0f, 0f, 0f, 0.72f);
        public Color breakingFootprintColor = new Color(1f, 0.1f, 0.08f, 0.9f);
        [Range(16, 128)]
        public int footprintSpriteWidth = 40;
        [Range(24, 160)]
        public int footprintSpriteHeight = 72;
        [Range(1f, 64f)]
        public float footprintPixelsPerUnit = 64f;

        [Header("Debug")]
        public bool logSpawns = true;

        private readonly List<FusionNightFootprintEnemy> activeEnemies = new List<FusionNightFootprintEnemy>();
        private bool wasNight;
        private GameObject leftFootprintPrefab;
        private GameObject rightFootprintPrefab;
        private int pendingSpawnCount;
        private Canvas warningCanvas;
        private CanvasGroup warningCanvasGroup;
        private RectTransform warningPanelRect;
        private TextMeshProUGUI warningText;
        private Sprite spawnWarningSprite;

        void Awake()
        {
            ResolveReferences();
            EnsureFootprintPrefabs();
            wasNight = stageController != null && stageController.IsNight;
            if (wasNight && spawnImmediatelyIfAlreadyNight)
                SpawnNightWave();
        }

        void Update()
        {
            ResolveReferences();
            CleanupEnemyList();

            bool isNight = stageController != null && stageController.IsNight;
            if (isNight && !wasNight)
                SpawnNightWave();

            wasNight = isNight;
        }

        [ContextMenu("Spawn Night Wave Now")]
        public void SpawnNightWave()
        {
            ResolveReferences();
            EnsureFootprintPrefabs();
            CleanupEnemyList();

            int spawnCount = Mathf.Min(
                Mathf.Max(0, enemiesPerNight),
                Mathf.Max(0, maxActiveEnemies - activeEnemies.Count - pendingSpawnCount));
            for (int i = 0; i < spawnCount; i++)
                StartCoroutine(SpawnEnemyWithWarning());
        }

        private IEnumerator SpawnEnemyWithWarning()
        {
            pendingSpawnCount++;
            try
            {
                if (!TryGetSpawnPosition(out Vector3 spawnPosition))
                    spawnPosition = transform.position;

                if (logSpawns)
                {
                    Debug.Log(
                        "[EnemySpawn] Candidate selected position=" + spawnPosition +
                        " insideRoom=" + IsInsideAnyRoomOrFusionFloor(spawnPosition),
                        this);
                }

                bool shouldWarn = warnBeforeOffscreenSpawn &&
                    (!warnOnlyWhenOffscreen || IsOutsidePlayerView(spawnPosition));
                GameObject gridWarning = null;
                if (shouldWarn && spawnWarningLeadTime > 0.0001f)
                {
                    if (showGridCellSpawnWarning)
                    {
                        gridWarning = CreateSpawnWarningVisual(spawnPosition);
                        if (logSpawns)
                            Debug.Log("[EnemySpawn] Warning started position=" + spawnPosition, this);
                    }

                    ShowSpawnWarning(true);
                    float start = Time.realtimeSinceStartup;
                    bool cancelled = false;
                    while (Time.realtimeSinceStartup - start < spawnWarningLeadTime)
                    {
                        float normalized = Mathf.Clamp01((Time.realtimeSinceStartup - start) / Mathf.Max(0.0001f, spawnWarningLeadTime));
                        UpdateSpawnWarningVisual(gridWarning, normalized);

                        if (ShouldCancelSpawnWarning(spawnPosition))
                        {
                            cancelled = true;
                            break;
                        }

                        yield return null;
                    }

                    ShowSpawnWarning(false);
                    DestroySpawnWarningVisual(gridWarning);

                    if (cancelled)
                    {
                        if (logSpawns)
                            Debug.Log("[EnemySpawn] Warning cancelled position=" + spawnPosition, this);
                        yield break;
                    }
                }

                SpawnEnemyAt(spawnPosition);
            }
            finally
            {
                pendingSpawnCount = Mathf.Max(0, pendingSpawnCount - 1);
            }
        }

        private void SpawnEnemyAt(Vector3 spawnPosition)
        {
            GameObject enemyObject = new GameObject("Fusion Night Footprint Enemy");
            enemyObject.transform.position = spawnPosition;
            enemyObject.transform.SetParent(transform, true);

            FusionNightFootprintEnemy enemy = enemyObject.AddComponent<FusionNightFootprintEnemy>();
            enemy.moveSpeed = enemyMoveSpeed;
            enemy.windowDetectionDistance = windowDetectionDistance;
            enemy.playerWindowDistance = playerWindowDistance;
            enemy.requireOpenWindow = requireOpenWindow;
            enemy.Configure(fusionSandbox, playerControl, leftFootprintPrefab, rightFootprintPrefab, footprintParent);
            if (enemy.footprintTrace != null)
                enemy.footprintTrace.ConfigureFootprintColors(footprintColor, breakingFootprintColor);

            activeEnemies.Add(enemy);
            if (logSpawns)
                Debug.Log("[EnemySpawn] Enemy spawned at " + spawnPosition + " outside=" + !IsInsideAnyRoomOrFusionFloor(spawnPosition), enemy);
        }

        private bool IsOutsidePlayerView(Vector3 worldPosition)
        {
            Camera camera = fusionSandbox != null && fusionSandbox.worldCamera != null
                ? fusionSandbox.worldCamera
                : Camera.main;
            if (camera == null)
                return true;

            Vector3 viewport = camera.WorldToViewportPoint(worldPosition);
            return viewport.z < 0f ||
                   viewport.x < 0f || viewport.x > 1f ||
                   viewport.y < 0f || viewport.y > 1f;
        }

        private void ShowSpawnWarning(bool visible)
        {
            EnsureWarningCanvas();
            if (warningCanvas == null || warningCanvasGroup == null || warningText == null)
                return;

            warningCanvas.gameObject.SetActive(visible);
            warningCanvasGroup.alpha = visible ? 1f : 0f;
            warningText.text = spawnWarningText;
            warningText.fontSize = spawnWarningFontSize;
            warningText.color = spawnWarningTextColor;
            if (warningPanelRect != null)
                warningPanelRect.anchoredPosition = spawnWarningAnchoredPosition;
        }

        private void EnsureWarningCanvas()
        {
            if (warningCanvas != null && warningCanvasGroup != null && warningText != null)
                return;

            GameObject canvasObject = new GameObject(
                "Fusion Enemy Spawn Warning Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(CanvasGroup));
            canvasObject.transform.SetParent(transform, false);
            warningCanvas = canvasObject.GetComponent<Canvas>();
            warningCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            warningCanvas.sortingOrder = spawnWarningCanvasSortingOrder;
            warningCanvasGroup = canvasObject.GetComponent<CanvasGroup>();
            warningCanvasGroup.alpha = 0f;
            warningCanvasGroup.interactable = false;
            warningCanvasGroup.blocksRaycasts = false;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject panelObject = new GameObject("Warning Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panelObject.transform.SetParent(canvasObject.transform, false);
            warningPanelRect = panelObject.GetComponent<RectTransform>();
            warningPanelRect.anchorMin = new Vector2(0.5f, 0.5f);
            warningPanelRect.anchorMax = new Vector2(0.5f, 0.5f);
            warningPanelRect.pivot = new Vector2(0.5f, 0.5f);
            warningPanelRect.anchoredPosition = spawnWarningAnchoredPosition;
            warningPanelRect.sizeDelta = new Vector2(760f, 92f);
            Image panelImage = panelObject.GetComponent<Image>();
            panelImage.raycastTarget = false;
            panelImage.color = spawnWarningPanelColor;

            GameObject textObject = new GameObject("Warning Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(panelObject.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            warningText = textObject.GetComponent<TextMeshProUGUI>();
            warningText.raycastTarget = false;
            warningText.alignment = TextAlignmentOptions.Center;
            warningText.text = spawnWarningText;
            warningText.fontSize = spawnWarningFontSize;
            warningText.color = spawnWarningTextColor;

            canvasObject.SetActive(false);
        }

        private bool TryGetSpawnPosition(out Vector3 spawnPosition)
        {
            spawnPosition = transform.position;
            Bounds bounds;
            if (fusionSandbox == null || !fusionSandbox.TryGetWorldBounds(out bounds))
                return false;

            int attempts = Mathf.Max(1, spawnCandidateAttempts);
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                int side = Random.Range(0, 4);
                float x = Random.Range(bounds.min.x, bounds.max.x);
                float y = Random.Range(bounds.min.y, bounds.max.y);
                float padding = Mathf.Max(0.5f, spawnPadding);

                switch (side)
                {
                    case 0:
                        y = bounds.max.y + padding;
                        break;
                    case 1:
                        y = bounds.min.y - padding;
                        break;
                    case 2:
                        x = bounds.min.x - padding;
                        break;
                    default:
                        x = bounds.max.x + padding;
                        break;
                }

                Vector3 candidate = SnapToSpawnCellCenter(new Vector3(x, y, 0f));
                if (!IsValidSpawnCell(candidate))
                    continue;

                spawnPosition = candidate;
                return true;
            }

            Vector3 fallback = SnapToSpawnCellCenter(new Vector3(bounds.max.x + Mathf.Max(0.5f, spawnPadding), bounds.center.y, 0f));
            if (IsValidSpawnCell(fallback))
            {
                spawnPosition = fallback;
                return true;
            }

            return false;
        }

        private bool IsValidSpawnCell(Vector3 position)
        {
            if (IsInsideAnyRoomOrFusionFloor(position))
                return false;

            if (!PlayerControl.TryGetPlayerWorldPosition(out Vector3 playerPosition))
                return true;

            Vector2Int spawnCell = WorldToSpawnCell(position);
            Vector2Int playerCell = WorldToSpawnCell(playerPosition);
            return spawnCell != playerCell;
        }

        private bool ShouldCancelSpawnWarning(Vector3 spawnPosition)
        {
            if (cancelSpawnIfCellBecomesInvalid && !IsValidSpawnCell(spawnPosition))
                return true;

            if (!blockSpawnIfPlayerEntersWarningCell ||
                !PlayerControl.TryGetPlayerWorldPosition(out Vector3 playerPosition))
            {
                return false;
            }

            return WorldToSpawnCell(spawnPosition) == WorldToSpawnCell(playerPosition);
        }

        private bool IsInsideAnyRoomOrFusionFloor(Vector3 position)
        {
            if (RoomManager.IsInsideAnyRoom(position))
                return true;

            return fusionSandbox != null && fusionSandbox.ContainsWorldPoint(position, 0f);
        }

        private Vector3 SnapToSpawnCellCenter(Vector3 worldPosition)
        {
            Vector2Int cell = WorldToSpawnCell(worldPosition);
            float grid = fusionSandbox != null ? Mathf.Max(0.0001f, Mathf.Abs(fusionSandbox.gridSize)) : 1f;
            Vector2 origin = fusionSandbox != null ? fusionSandbox.gridOrigin : Vector2.zero;
            return new Vector3(
                origin.x + (cell.x + 0.5f) * grid,
                origin.y + (cell.y + 0.5f) * grid,
                0f);
        }

        private Vector2Int WorldToSpawnCell(Vector3 worldPosition)
        {
            float grid = fusionSandbox != null ? Mathf.Max(0.0001f, Mathf.Abs(fusionSandbox.gridSize)) : 1f;
            Vector2 origin = fusionSandbox != null ? fusionSandbox.gridOrigin : Vector2.zero;
            return new Vector2Int(
                Mathf.FloorToInt((worldPosition.x - origin.x) / grid),
                Mathf.FloorToInt((worldPosition.y - origin.y) / grid));
        }

        private GameObject CreateSpawnWarningVisual(Vector3 spawnPosition)
        {
            GameObject visual = spawnWarningPrefab != null
                ? Instantiate(spawnWarningPrefab)
                : CreateDefaultSpawnWarningVisual();

            if (visual == null)
                return null;

            visual.name = "Fusion Enemy Spawn Cell Warning";
            visual.transform.SetParent(transform, true);
            visual.transform.position = new Vector3(spawnPosition.x, spawnPosition.y, -0.2f);

            float grid = fusionSandbox != null ? Mathf.Max(0.0001f, Mathf.Abs(fusionSandbox.gridSize)) : 1f;
            visual.transform.localScale = Vector3.one * grid;
            ApplySpawnWarningColor(visual, spawnWarningColor);
            return visual;
        }

        private GameObject CreateDefaultSpawnWarningVisual()
        {
            GameObject visual = new GameObject("Default Spawn Warning");
            SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = GetSpawnWarningSprite();
            renderer.color = spawnWarningColor;
            renderer.sortingOrder = 70;
            return visual;
        }

        private void UpdateSpawnWarningVisual(GameObject visual, float normalized)
        {
            if (visual == null)
                return;

            float pulse = warningPulseCurve != null
                ? warningPulseCurve.Evaluate(Mathf.Repeat(normalized * 3f, 1f))
                : Mathf.PingPong(normalized * 3f, 1f);
            Color color = spawnWarningColor;
            color.a *= Mathf.Lerp(0.35f, 1f, pulse);
            ApplySpawnWarningColor(visual, color);
            visual.transform.localScale = Vector3.one *
                ((fusionSandbox != null ? Mathf.Max(0.0001f, Mathf.Abs(fusionSandbox.gridSize)) : 1f) *
                 Mathf.Lerp(0.85f, 1.05f, pulse));
        }

        private static void ApplySpawnWarningColor(GameObject visual, Color color)
        {
            SpriteRenderer[] spriteRenderers = visual.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < spriteRenderers.Length; i++)
            {
                if (spriteRenderers[i] != null)
                    spriteRenderers[i].color = color;
            }

            LineRenderer[] lineRenderers = visual.GetComponentsInChildren<LineRenderer>(true);
            for (int i = 0; i < lineRenderers.Length; i++)
            {
                if (lineRenderers[i] == null)
                    continue;

                lineRenderers[i].startColor = color;
                lineRenderers[i].endColor = color;
            }
        }

        private void DestroySpawnWarningVisual(GameObject visual)
        {
            if (visual == null)
                return;

            if (Application.isPlaying)
                Destroy(visual);
            else
                DestroyImmediate(visual);
        }

        private Sprite GetSpawnWarningSprite()
        {
            if (spawnWarningSprite != null)
                return spawnWarningSprite;

            Texture2D texture = new Texture2D(8, 8, TextureFormat.RGBA32, false)
            {
                name = "Fusion Enemy Spawn Warning Cell",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color clear = new Color(1f, 1f, 1f, 0f);
            Color fill = Color.white;
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    bool border = x == 0 || y == 0 || x == 7 || y == 7;
                    texture.SetPixel(x, y, border ? fill : clear);
                }
            }

            texture.Apply();
            spawnWarningSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, 8f, 8f),
                new Vector2(0.5f, 0.5f),
                8f);
            spawnWarningSprite.hideFlags = HideFlags.HideAndDontSave;
            return spawnWarningSprite;
        }

        private void CleanupEnemyList()
        {
            for (int i = activeEnemies.Count - 1; i >= 0; i--)
            {
                if (activeEnemies[i] == null)
                    activeEnemies.RemoveAt(i);
            }
        }

        private void ResolveReferences()
        {
            if (stageController == null)
                stageController = FindFirstObjectByType<StageCycleController>();
            if (fusionSandbox == null)
                fusionSandbox = FindFirstObjectByType<RuntimeTileMeshFusionSandbox>();
            if (playerControl == null)
                playerControl = PlayerControl.Active != null ? PlayerControl.Active : FindFirstObjectByType<PlayerControl>();

            if (footprintParent == null)
            {
                Transform existing = transform.Find("Fusion Enemy Footprints");
                if (existing != null)
                    footprintParent = existing;
                else
                {
                    GameObject parentObject = new GameObject("Fusion Enemy Footprints");
                    parentObject.transform.SetParent(transform, false);
                    footprintParent = parentObject.transform;
                }
            }
        }

        private void EnsureFootprintPrefabs()
        {
            if (leftFootprintPrefab != null && rightFootprintPrefab != null)
                return;

            leftFootprintPrefab = CreateFootprintPrefab("Left", false);
            rightFootprintPrefab = CreateFootprintPrefab("Right", true);
        }

        private GameObject CreateFootprintPrefab(string sideName, bool mirror)
        {
            GameObject prefab = new GameObject("Fusion " + sideName + " Footprint Sprite Prefab");
            prefab.hideFlags = HideFlags.HideAndDontSave;
            prefab.SetActive(false);

            SpriteRenderer renderer = prefab.AddComponent<SpriteRenderer>();
            renderer.sprite = CreateFootprintSprite(sideName, mirror);
            renderer.color = Color.white;
            renderer.sortingOrder = 45;
            prefab.AddComponent<FootprintInstance>();
            return prefab;
        }

        private Sprite CreateFootprintSprite(string sideName, bool mirror)
        {
            int width = Mathf.Clamp(footprintSpriteWidth, 16, 128);
            int height = Mathf.Clamp(footprintSpriteHeight, 24, 160);
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "Fusion " + sideName + " Footprint",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color clear = new Color(1f, 1f, 1f, 0f);
            Color ink = Color.white;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width;
                    float v = (y + 0.5f) / height;
                    if (mirror)
                        u = 1f - u;

                    bool sole = IsInsideEllipse(u, v, 0.48f, 0.38f, 0.23f, 0.30f, -10f);
                    bool heel = IsInsideEllipse(u, v, 0.48f, 0.18f, 0.15f, 0.13f, -4f);
                    bool toe1 = IsInsideEllipse(u, v, 0.34f, 0.72f, 0.055f, 0.075f, 0f);
                    bool toe2 = IsInsideEllipse(u, v, 0.44f, 0.79f, 0.064f, 0.09f, 0f);
                    bool toe3 = IsInsideEllipse(u, v, 0.55f, 0.80f, 0.056f, 0.082f, 0f);
                    bool toe4 = IsInsideEllipse(u, v, 0.65f, 0.75f, 0.048f, 0.07f, 0f);

                    texture.SetPixel(x, y, sole || heel || toe1 || toe2 || toe3 || toe4 ? ink : clear);
                }
            }

            texture.Apply();
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, width, height),
                new Vector2(0.5f, 0.5f),
                Mathf.Max(1f, footprintPixelsPerUnit));
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static bool IsInsideEllipse(
            float u,
            float v,
            float centerU,
            float centerV,
            float radiusU,
            float radiusV,
            float rotationDegrees)
        {
            float radians = rotationDegrees * Mathf.Deg2Rad;
            float sin = Mathf.Sin(radians);
            float cos = Mathf.Cos(radians);
            float dx = u - centerU;
            float dy = v - centerV;
            float rx = dx * cos - dy * sin;
            float ry = dx * sin + dy * cos;
            return (rx * rx) / Mathf.Max(0.0001f, radiusU * radiusU) +
                   (ry * ry) / Mathf.Max(0.0001f, radiusV * radiusV) <= 1f;
        }
    }
}

using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.UI;

namespace DuoCurtain.RuntimeTileMesh
{
    [DisallowMultipleComponent]
    public sealed class FusionShopThumbnailRenderer : MonoBehaviour
    {
        public enum ThumbnailFramingMode
        {
            FitBounds,
            FixedOrthographicSize
        }

        public enum ThumbnailClearMode
        {
            Transparent,
            SolidColor
        }

        [Serializable]
        public sealed class ThumbnailSettings
        {
            [Header("Camera")]
            [Tooltip("FitBounds frames each block from its renderer bounds. FixedOrthographicSize keeps every item at the same camera size.")]
            public ThumbnailFramingMode framingMode = ThumbnailFramingMode.FitBounds;
            [Tooltip("Transparent keeps the UI card background visible. SolidColor renders a fixed thumbnail background.")]
            public ThumbnailClearMode clearMode = ThumbnailClearMode.Transparent;
            [Tooltip("Square render texture resolution used by each shop thumbnail.")]
            [Range(64, 1024)]
            public int resolution = 256;
            [Tooltip("Extra camera padding around the item when Framing Mode is Fit Bounds.")]
            [Min(0.05f)]
            public float framingPadding = 0.35f;
            [Tooltip("Camera orthographic size when Framing Mode is Fixed Orthographic Size.")]
            [Min(0.1f)]
            public float fixedOrthographicSize = 2f;
            [Tooltip("Render texture anti-aliasing level. Unity normalizes unsupported values to 1, 2, 4, or 8.")]
            [Range(1, 8)]
            public int antiAliasing = 2;
            [Tooltip("Background color used when Clear Mode is Solid Color. Alpha is also respected for Transparent mode.")]
            public Color backgroundColor = new Color(0f, 0f, 0f, 0f);
            [Header("Appearance")]
            [Tooltip("Multiplies the final thumbnail image color.")]
            public Color tint = Color.white;
            [Tooltip("Final UI thumbnail opacity.")]
            [Range(0f, 1f)]
            public float opacity = 1f;
            [Header("Transform")]
            [Tooltip("Preview object rotation before rendering.")]
            public Vector3 previewRotationEuler = Vector3.zero;
            [Tooltip("Preview object local offset before rendering.")]
            public Vector2 previewOffset = Vector2.zero;
            [Tooltip("Preview object local scale before rendering.")]
            [Min(0.01f)]
            public float previewScale = 1f;
            [Header("Refresh")]
            [Tooltip("When enabled, thumbnail cameras stay active so animated materials/sprites can keep moving in the shop.")]
            public bool renderContinuously = true;
        }

        private sealed class PreviewEntry
        {
            public GameObject stage;
            public RenderTexture texture;
            public Camera camera;
            public RawImage target;
        }

        private readonly List<PreviewEntry> previews = new List<PreviewEntry>();

        public void Rebuild(
            IList<FusionGameModeController.BlockShopItem> items,
            IList<RawImage> targets,
            int resolution,
            Color backgroundColor,
            float framingPadding)
        {
            ThumbnailSettings settings = new ThumbnailSettings
            {
                resolution = resolution,
                backgroundColor = backgroundColor,
                framingPadding = framingPadding
            };
            Rebuild(items, targets, settings);
        }

        public void Rebuild(
            IList<FusionGameModeController.BlockShopItem> items,
            IList<RawImage> targets,
            ThumbnailSettings settings)
        {
            Cleanup();
            if (items == null || targets == null)
                return;

            if (settings == null)
                settings = new ThumbnailSettings();

            int count = Mathf.Min(items.Count, targets.Count);
            for (int i = 0; i < count; i++)
            {
                FusionGameModeController.BlockShopItem item = items[i];
                RawImage target = targets[i];
                if (item == null || !item.HasPurchasableContent() || target == null)
                    continue;

                CreatePreview(item, target, i, settings);
            }
        }

        void OnDestroy()
        {
            Cleanup();
        }

        private void CreatePreview(
            FusionGameModeController.BlockShopItem item,
            RawImage target,
            int index,
            ThumbnailSettings settings)
        {
            int resolution = Mathf.Clamp(settings.resolution, 64, 1024);
            Vector3 stagePosition = new Vector3(10000f + index * 64f, 10000f, 0f);

            GameObject stage = new GameObject("Shop Preview Stage - " + item.displayName);
            stage.hideFlags = HideFlags.HideAndDontSave;
            stage.transform.position = stagePosition;

            GameObject clone = CreateItemPreviewObject(item, stage.transform);
            clone.name = "Visual - " + item.displayName;
            clone.transform.localPosition = new Vector3(settings.previewOffset.x, settings.previewOffset.y, 0f);
            clone.transform.localRotation = Quaternion.Euler(settings.previewRotationEuler);
            clone.transform.localScale = Vector3.one * Mathf.Max(0.01f, settings.previewScale);
            SetHideFlagsRecursively(clone, HideFlags.HideAndDontSave);
            bool preserveFusionDoor = item.itemKind == FusionGameModeController.ShopItemKind.WallAttachment &&
                item.wallAttachmentCategory == FusionGameModeController.WallAttachmentCategory.Door;
            StripGameplayComponents(clone, preserveFusionDoor);
            if (preserveFusionDoor)
                EnsurePreviewRenderersEnabled(clone);

            RuntimeTileMeshView[] views = clone.GetComponentsInChildren<RuntimeTileMeshView>(true);
            for (int i = 0; i < views.Length; i++)
            {
                RuntimeTileMeshView view = views[i];
                if (view == null)
                    continue;

                view.rebuildOnStart = false;
                view.buildPolygonCollider2D = false;
                view.sortingOrder = 0;
                view.Rebuild();
            }

            Bounds bounds = CalculateVisualBounds(clone, item.blockPrefab);
            RenderTexture texture = new RenderTexture(resolution, resolution, 16, RenderTextureFormat.ARGB32)
            {
                name = "Shop Thumbnail - " + item.displayName,
                antiAliasing = NormalizeAntiAliasing(settings.antiAliasing),
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.Create();

            GameObject cameraObject = new GameObject("Shop Preview Camera - " + item.displayName, typeof(Camera));
            cameraObject.hideFlags = HideFlags.HideAndDontSave;
            cameraObject.transform.SetParent(stage.transform, false);

            Camera previewCamera = cameraObject.GetComponent<Camera>();
            previewCamera.orthographic = true;
            previewCamera.clearFlags = settings.clearMode == ThumbnailClearMode.Transparent
                ? CameraClearFlags.SolidColor
                : CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = settings.clearMode == ThumbnailClearMode.Transparent
                ? new Color(settings.backgroundColor.r, settings.backgroundColor.g, settings.backgroundColor.b, 0f)
                : settings.backgroundColor;
            previewCamera.allowHDR = false;
            previewCamera.allowMSAA = true;
            previewCamera.nearClipPlane = 0.1f;
            previewCamera.farClipPlane = 50f;
            previewCamera.depth = -100f - index;
            previewCamera.targetTexture = texture;
            previewCamera.enabled = settings.renderContinuously;
            previewCamera.transform.position = new Vector3(bounds.center.x, bounds.center.y, stagePosition.z - 10f);
            previewCamera.orthographicSize = settings.framingMode == ThumbnailFramingMode.FixedOrthographicSize
                ? Mathf.Max(0.1f, settings.fixedOrthographicSize)
                : Mathf.Max(
                    0.5f,
                    Mathf.Max(bounds.extents.x, bounds.extents.y) + Mathf.Max(0.05f, settings.framingPadding));
            previewCamera.Render();

            target.texture = texture;
            target.color = new Color(settings.tint.r, settings.tint.g, settings.tint.b, Mathf.Clamp01(settings.opacity));
            target.uvRect = new Rect(0f, 0f, 1f, 1f);
            target.raycastTarget = false;

            previews.Add(new PreviewEntry
            {
                stage = stage,
                texture = texture,
                camera = previewCamera,
                target = target
            });
        }

        private static GameObject CreateItemPreviewObject(
            FusionGameModeController.BlockShopItem item,
            Transform parent)
        {
            if (item.itemKind == FusionGameModeController.ShopItemKind.Block && item.blockPrefab != null)
                return Instantiate(item.blockPrefab.gameObject, parent);

            if (item.wallAttachmentPrefab != null)
                return Instantiate(item.wallAttachmentPrefab, parent);

            if (item.itemKind == FusionGameModeController.ShopItemKind.WallAttachment &&
                item.wallAttachmentCategory == FusionGameModeController.WallAttachmentCategory.Door)
            {
                return RuntimeTileMeshFusionDoor.CreatePanelOnlyShopPreview(parent).gameObject;
            }

            GameObject attachment = new GameObject("Default Window Attachment Preview");
            attachment.transform.SetParent(parent, false);
            SpriteRenderer sprite = attachment.AddComponent<SpriteRenderer>();
            sprite.sprite = FusionWallAttachment.GetDefaultWindowSprite();
            sprite.color = new Color(1f, 0.92f, 0.08f, 1f);
            attachment.transform.localScale = new Vector3(1f, 0.18f, 1f);
            return attachment;
        }

        private static int NormalizeAntiAliasing(int requested)
        {
            if (requested >= 8)
                return 8;
            if (requested >= 4)
                return 4;
            if (requested >= 2)
                return 2;
            return 1;
        }

        private static void StripGameplayComponents(GameObject clone, bool preserveFusionDoor = false)
        {
            RuntimeTileMeshDraggableBlock[] blocks = clone.GetComponentsInChildren<RuntimeTileMeshDraggableBlock>(true);
            for (int i = 0; i < blocks.Length; i++)
            {
                if (blocks[i] == null)
                    continue;

                blocks[i].enabled = false;
                Destroy(blocks[i]);
            }

            RuntimeTileMeshFusionDoor[] doors = clone.GetComponentsInChildren<RuntimeTileMeshFusionDoor>(true);
            for (int i = 0; i < doors.Length; i++)
            {
                if (doors[i] == null)
                    continue;

                if (preserveFusionDoor)
                {
                    doors[i].enabled = false;
                    continue;
                }

                doors[i].enabled = false;
                Destroy(doors[i]);
            }

            Collider2D[] colliders = clone.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] == null)
                    continue;

                colliders[i].enabled = false;
                Destroy(colliders[i]);
            }

            Rigidbody2D[] bodies = clone.GetComponentsInChildren<Rigidbody2D>(true);
            for (int i = 0; i < bodies.Length; i++)
            {
                if (bodies[i] != null)
                    Destroy(bodies[i]);
            }

            AudioSource[] audioSources = clone.GetComponentsInChildren<AudioSource>(true);
            for (int i = 0; i < audioSources.Length; i++)
            {
                if (audioSources[i] != null)
                    Destroy(audioSources[i]);
            }
        }

        private static Bounds CalculateVisualBounds(
            GameObject clone,
            RuntimeTileMeshDraggableBlock prefab)
        {
            Renderer[] renderers = clone.GetComponentsInChildren<Renderer>(true);
            bool found = false;
            Bounds bounds = new Bounds(clone.transform.position, Vector3.one);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled)
                    continue;

                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (found)
                return bounds;

            RuntimeTileMeshView sourceView = prefab != null ? prefab.View : null;
            if (sourceView == null || sourceView.tiles == null || sourceView.tiles.Count == 0)
                return bounds;

            int minX = int.MaxValue;
            int minY = int.MaxValue;
            int maxX = int.MinValue;
            int maxY = int.MinValue;
            for (int i = 0; i < sourceView.tiles.Count; i++)
            {
                Vector2Int cell = sourceView.tiles[i];
                minX = Mathf.Min(minX, cell.x);
                minY = Mathf.Min(minY, cell.y);
                maxX = Mathf.Max(maxX, cell.x);
                maxY = Mathf.Max(maxY, cell.y);
            }

            Vector2 size = new Vector2(
                (maxX - minX + 1) * sourceView.tileSize.x,
                (maxY - minY + 1) * sourceView.tileSize.y);
            Vector2 center = new Vector2(
                (minX + maxX + 1) * 0.5f * sourceView.tileSize.x,
                (minY + maxY + 1) * 0.5f * sourceView.tileSize.y);
            return new Bounds(clone.transform.position + (Vector3)center, new Vector3(size.x, size.y, 0.1f));
        }

        private static void EnsurePreviewRenderersEnabled(GameObject clone)
        {
            if (clone == null)
                return;

            Renderer[] renderers = clone.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    renderers[i].enabled = true;
            }
        }

        private static void SetHideFlagsRecursively(GameObject root, HideFlags flags)
        {
            if (root == null)
                return;

            root.hideFlags = flags;
            Transform transform = root.transform;
            for (int i = 0; i < transform.childCount; i++)
                SetHideFlagsRecursively(transform.GetChild(i).gameObject, flags);
        }

        private void Cleanup()
        {
            for (int i = 0; i < previews.Count; i++)
            {
                PreviewEntry entry = previews[i];
                if (entry == null)
                    continue;

                if (entry.target != null && entry.target.texture == entry.texture)
                    entry.target.texture = null;

                if (entry.camera != null)
                    entry.camera.targetTexture = null;

                if (entry.texture != null)
                {
                    entry.texture.Release();
                    Destroy(entry.texture);
                }

                if (entry.stage != null)
                    Destroy(entry.stage);
            }

            previews.Clear();
        }
    }
}

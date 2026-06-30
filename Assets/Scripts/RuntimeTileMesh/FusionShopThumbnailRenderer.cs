using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DuoCurtain.RuntimeTileMesh
{
    [DisallowMultipleComponent]
    public sealed class FusionShopThumbnailRenderer : MonoBehaviour
    {
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
            Cleanup();
            if (items == null || targets == null)
                return;

            int count = Mathf.Min(items.Count, targets.Count);
            for (int i = 0; i < count; i++)
            {
                FusionGameModeController.BlockShopItem item = items[i];
                RawImage target = targets[i];
                if (item == null || item.blockPrefab == null || target == null)
                    continue;

                CreatePreview(item, target, i, resolution, backgroundColor, framingPadding);
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
            int resolution,
            Color backgroundColor,
            float framingPadding)
        {
            resolution = Mathf.Clamp(resolution, 64, 1024);
            Vector3 stagePosition = new Vector3(10000f + index * 64f, 10000f, 0f);

            GameObject stage = new GameObject("Shop Preview Stage - " + item.displayName);
            stage.hideFlags = HideFlags.HideAndDontSave;
            stage.transform.position = stagePosition;

            GameObject clone = Instantiate(item.blockPrefab.gameObject, stage.transform);
            clone.name = "Visual - " + item.displayName;
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localRotation = Quaternion.identity;
            clone.transform.localScale = Vector3.one;
            SetHideFlagsRecursively(clone, HideFlags.HideAndDontSave);
            StripGameplayComponents(clone);

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
                antiAliasing = 2,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.Create();

            GameObject cameraObject = new GameObject("Shop Preview Camera - " + item.displayName, typeof(Camera));
            cameraObject.hideFlags = HideFlags.HideAndDontSave;
            cameraObject.transform.SetParent(stage.transform, false);

            Camera previewCamera = cameraObject.GetComponent<Camera>();
            previewCamera.orthographic = true;
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = backgroundColor;
            previewCamera.allowHDR = false;
            previewCamera.allowMSAA = true;
            previewCamera.nearClipPlane = 0.1f;
            previewCamera.farClipPlane = 50f;
            previewCamera.depth = -100f - index;
            previewCamera.targetTexture = texture;
            previewCamera.transform.position = new Vector3(bounds.center.x, bounds.center.y, stagePosition.z - 10f);
            previewCamera.orthographicSize = Mathf.Max(
                0.5f,
                Mathf.Max(bounds.extents.x, bounds.extents.y) + Mathf.Max(0.05f, framingPadding));

            target.texture = texture;
            target.color = Color.white;
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

        private static void StripGameplayComponents(GameObject clone)
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
            Renderer[] renderers = clone.GetComponentsInChildren<Renderer>(false);
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

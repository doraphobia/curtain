using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
#if VECTOR_GRAHICS_IMPORTED
using Unity.VectorGraphics;
#endif

namespace FigmaImporter.Editor
{
    public class ImageUtils
    {
        public static void AddOverridenSprite(GameObject nodeGo, Sprite overridenSprite)
        {
            var image = nodeGo.AddComponent<Image>();
            image.sprite = overridenSprite;
        }
        
#if VECTOR_GRAHICS_IMPORTED
        public static void AddOverridenSvgSprite(GameObject nodeGo, Sprite overridenSprite)
        {
            var image = nodeGo.AddComponent<SVGImage>();
            image.sprite = overridenSprite;
        }

        public static async Task RenderSvgNodeAndApply(Node node, GameObject nodeGo, FigmaImporter importer, CancellationToken cancellationToken = default)
        {
            if (ImportFallbackRegistry.TryGetSvgOverride(node.id, out var overrideSprite) && overrideSprite != null)
            {
                ApplySpriteFallback(nodeGo, overrideSprite);
                return;
            }

            var destinationAssetPath = ResolveNodeSpriteAssetPath(importer, node, "svg");

            // Reuse existing rendered assets to avoid repeated downloads/imports.
            var existingSprite = AssetDatabase.LoadAssetAtPath<Sprite>(destinationAssetPath);
            if (HasUsableSprite(existingSprite))
            {
                TransformUtils.EnsureRectTransform(nodeGo, "SVG Render");
                var existingImage = nodeGo.GetComponent<SVGImage>() ?? nodeGo.AddComponent<SVGImage>();
                existingImage.sprite = existingSprite;
                existingImage.preserveAspect = true;
                return;
            }

            if (existingSprite != null)
            {
                await FallbackSvgToRaster(
                    node,
                    nodeGo,
                    importer,
                    "Imported SVG sprite has zero-sized bounds.",
                    cancellationToken);
                return;
            }

            FigmaNodesProgressInfo.CurrentInfo = "Loading image";
            FigmaNodesProgressInfo.ShowProgress(0f);
            var result = await importer.GetSvgImage(node.id, true, cancellationToken);
            string svgAsString = result == null? null : Encoding.UTF8.GetString(result);
            if (svgAsString == null || svgAsString.Contains("image/jpg") || svgAsString.Contains("image/jpeg") || svgAsString.Contains("image/png"))
            {
                var fallbackSpritePath = ResolveNodeSpriteAssetPath(importer, node, "png");
                ImportFallbackRegistry.ReportSvgFallback(
                    node.id,
                    node.name,
                    fallbackSpritePath,
                    "SVG payload contains raster image data",
                    SvgFallbackType.RasterPayloadToPng);
                Debug.LogWarning("[FigmaImporter] SVG contains raster image data. Falling back to PNG render.");
                await RenderNodeAndApply(node, nodeGo, importer, cancellationToken);
                return;
            }

            try
            {
                SaveSvgTexture(result, destinationAssetPath);
                var destinationAbsolutePath = FigmaPathUtils.ToAbsolutePathFromAssetPath(destinationAssetPath);
                using (var stream = new StreamReader(destinationAbsolutePath))
                    SVGParser.ImportSVG(stream, ViewportOptions.DontPreserve, 0, 1, 100, 100);
                TransformUtils.EnsureRectTransform(nodeGo, "SVG Render");
            }
            catch (Exception e)
            {
                Debug.LogError("It seems that svg cant be imported. Trying to load raster image instead." + e.Message);
                var destinationAbsolutePath = FigmaPathUtils.ToAbsolutePathFromAssetPath(destinationAssetPath);
                if (File.Exists(destinationAbsolutePath))
                    File.Delete(destinationAbsolutePath);
                await FallbackSvgToRaster(
                    node,
                    nodeGo,
                    importer,
                    "SVG import failed",
                    cancellationToken);
                return;
            }

            SVGImage image = null;
            Sprite sprite = null;
            FigmaNodesProgressInfo.CurrentInfo = "Saving rendered node";
            FigmaNodesProgressInfo.ShowProgress(0f);
            try
            {
                sprite = AssetDatabase.LoadAssetAtPath<Sprite>(destinationAssetPath);
                if (!HasUsableSprite(sprite))
                {
                    await FallbackSvgToRaster(
                        node,
                        nodeGo,
                        importer,
                        "Imported SVG sprite has zero-sized bounds.",
                        cancellationToken);
                    return;
                }

                image = nodeGo.GetComponent<SVGImage>() ?? nodeGo.AddComponent<SVGImage>();
                image.sprite = sprite;
                image.preserveAspect = true;
            }
            catch (Exception e)
            {
                Debug.LogError(e.Message);
            }
        }

        private static void SaveSvgTexture(byte[] bytes, string assetPath)
        {
             var filePath = FigmaPathUtils.ToAbsolutePathFromAssetPath(assetPath);
             if (File.Exists(filePath))
             {
                 return;
             }

             var directory = Path.GetDirectoryName(filePath);
             if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
             {
                 Directory.CreateDirectory(directory);
             }

             System.IO.File.WriteAllBytes(filePath, bytes);
             UnityEditor.AssetDatabase.Refresh();

        }

        private static void ApplySpriteFallback(GameObject nodeGo, Sprite sprite)
        {
            if (nodeGo == null || sprite == null)
            {
                return;
            }

            TransformUtils.EnsureRectTransform(nodeGo, "SVG Fallback");
            var image = nodeGo.GetComponent<Image>() ?? nodeGo.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
        }

        private static bool HasUsableSprite(Sprite sprite)
        {
            return sprite != null &&
                   sprite.rect.width > 0.01f &&
                   sprite.rect.height > 0.01f;
        }

        private static async Task FallbackSvgToRaster(
            Node node,
            GameObject nodeGo,
            FigmaImporter importer,
            string reason,
            CancellationToken cancellationToken)
        {
            var fallbackSpritePath = ResolveNodeSpriteAssetPath(importer, node, "png");
            ImportFallbackRegistry.ReportSvgFallback(
                node.id,
                node.name,
                fallbackSpritePath,
                reason,
                SvgFallbackType.SvgImportFailedToPng);
            Debug.LogWarning($"[FigmaImporter] {reason} Falling back to PNG render.");
            await RenderNodeAndApply(node, nodeGo, importer, cancellationToken);
        }

#endif
        
        public static Sprite ChangeTextureToSprite(string path)
        {
            var assetPath = FigmaPathUtils.NormalizeAssetPath(path);
            TextureImporter textureImporter = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (textureImporter == null)
            {
                Debug.LogError($"[FigmaImporter] TextureImporter not found for path: {assetPath}");
                return null;
            }

            var importerChanged = false;
            if (textureImporter.textureType != TextureImporterType.Sprite)
            {
                textureImporter.textureType = TextureImporterType.Sprite;
                importerChanged = true;
            }

            if (textureImporter.spriteImportMode != SpriteImportMode.Single)
            {
                textureImporter.spriteImportMode = SpriteImportMode.Single;
                importerChanged = true;
            }

            if (textureImporter.mipmapEnabled)
            {
                textureImporter.mipmapEnabled = false;
                importerChanged = true;
            }

            if (importerChanged)
            {
                textureImporter.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        public static void SaveTexture(Texture2D texture, string assetPath, bool overwriteExisting = false)
        {
            if (texture == null || string.IsNullOrWhiteSpace(assetPath))
            {
                return;
            }

            var filePath = FigmaPathUtils.ToAbsolutePathFromAssetPath(assetPath);
            if (File.Exists(filePath) && !overwriteExisting)
            {
                return;
            }

            byte[] bytes = texture.EncodeToPNG();
            if (bytes != null && bytes.Length > 0)
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                System.IO.File.WriteAllBytes(filePath, bytes);
                AssetDatabase.ImportAsset(
                    FigmaPathUtils.NormalizeAssetPath(assetPath),
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            }
        }

        public static void SetMask(Node node, GameObject nodeGo)
        {
            if (node == null || nodeGo == null || !node.clipsContent)
            {
                return;
            }

            if (FigmaMaskingUtils.IsMaskNode(node))
            {
                // Explicit masks are configured after child scope remapping in FigmaNodeGenerator.
                return;
            }

            var hasVisibleFill = false;
            if (node.fills != null)
            {
                for (var i = 0; i < node.fills.Length; i++)
                {
                    var fill = node.fills[i];
                    if (fill == null || string.Equals(fill.visible, "false", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    hasVisibleFill = true;
                    break;
                }
            }

            if (!hasVisibleFill)
            {
                if (!nodeGo.TryGetComponent<RectMask2D>(out _))
                {
                    nodeGo.AddComponent<RectMask2D>();
                }
            }
            else
            {
                if (!nodeGo.TryGetComponent<Graphic>(out _))
                {
                    nodeGo.AddComponent<Image>();
                }

                if (!nodeGo.TryGetComponent<Mask>(out _))
                {
                    nodeGo.AddComponent<Mask>();   
                }
            }
        }

        public static void ConfigureExplicitMaskNode(Node node, GameObject nodeGo)
        {
            if (node == null || nodeGo == null || !FigmaMaskingUtils.IsMaskNode(node))
            {
                return;
            }

            // Figma explicit masks clip by alpha of the mask artwork.
            // We force UGUI Mask on this layer and hide the mask graphic itself.
            EnsureGraphicForMask(nodeGo);

            if (!nodeGo.TryGetComponent<Mask>(out var mask))
            {
                mask = nodeGo.AddComponent<Mask>();
            }

            if (mask != null)
            {
                mask.showMaskGraphic = false;
            }

            if (nodeGo.TryGetComponent<RectMask2D>(out var rectMask) && rectMask != null)
            {
                UnityEngine.Object.DestroyImmediate(rectMask);
            }

            if (!string.IsNullOrWhiteSpace(node.maskType) &&
                !string.Equals(node.maskType, "ALPHA", StringComparison.OrdinalIgnoreCase))
            {
                ImportFallbackRegistry.ReportMissingIssue(
                    "Mask",
                    node.id,
                    $"Mask type '{node.maskType}' is approximated with ALPHA mask in Unity UGUI.",
                    node.id,
                    node.name);
            }
        }

        private static void EnsureGraphicForMask(GameObject nodeGo)
        {
            if (nodeGo == null)
            {
                return;
            }

            if (!nodeGo.TryGetComponent<Graphic>(out var graphic) || graphic == null)
            {
                var image = nodeGo.GetComponent<Image>() ?? nodeGo.AddComponent<Image>();
                if (image != null && image.sprite == null)
                {
                    // Fallback to a full-rect alpha mask when no explicit sprite graphic exists.
                    image.color = new UnityEngine.Color(1f, 1f, 1f, 1f);
                }

                graphic = image;
            }

            if (graphic != null)
            {
                graphic.raycastTarget = false;
            }
        }
        
        public static async Task RenderNodeAndApply(Node node, GameObject nodeGo, FigmaImporter importer, CancellationToken cancellationToken = default)
        {
            var spriteAssetPath = ResolveNodeSpriteAssetPath(importer, node, "png");
            var sprite = GetExistingSprite(spriteAssetPath);

            if (sprite == null)
            {
                FigmaNodesProgressInfo.CurrentInfo = "Loading image";
                FigmaNodesProgressInfo.ShowProgress(0f);
                var result = await importer.GetImage(node.id, true, cancellationToken);
                if (result == null)
                {
                    HideInvalidImagePlaceholder(nodeGo);
                    ImportFallbackRegistry.ReportMissingIssue(
                        "Render",
                        node.id,
                        "Could not download node render image from Figma.",
                        node.id,
                        node.name);
                    return;
                }

                FigmaNodesProgressInfo.CurrentInfo = "Saving rendered node";
                FigmaNodesProgressInfo.ShowProgress(0f);
                try
                {
                    SaveTexture(result, spriteAssetPath, overwriteExisting: true);
                    sprite = ImageUtils.ChangeTextureToSprite(spriteAssetPath);
                }
                catch (Exception e)
                {
                    Debug.LogError(e.Message);
                }

                if (sprite == null)
                {
                    HideInvalidImagePlaceholder(nodeGo);
                    ImportFallbackRegistry.ReportMissingIssue(
                        "Render",
                        node.id,
                        "Node render image was downloaded but no Sprite could be created.",
                        node.id,
                        node.name);
                    return;
                }
            }

            ApplySpriteToNodeRect(nodeGo, sprite);
        }

        private static void ApplySpriteToNodeRect(GameObject nodeGo, Sprite sprite)
        {
            if (nodeGo == null || sprite == null)
            {
                return;
            }

            var rectTransform = TransformUtils.EnsureRectTransform(nodeGo, "Render");
            if (rectTransform == null)
            {
                return;
            }

            RemoveGeneratedRenderChildren(rectTransform);

            // Figma export pixels can differ from Unity UI layout size after API scaling or texture import.
            // Keep RectTransform as the source of truth so raster nodes stay aligned with native nodes.
            var image = nodeGo.GetComponent<Image>() ?? nodeGo.AddComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.enabled = true;
            image.color = UnityEngine.Color.white;
        }

        private static void RemoveGeneratedRenderChildren(RectTransform parent)
        {
            if (parent == null)
            {
                return;
            }

            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                if (child == null ||
                    !string.Equals(child.name, "Render", StringComparison.Ordinal) ||
                    child.GetComponent<Image>() == null)
                {
                    continue;
                }

                UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
        }

        private static Sprite GetExistingSprite(string spriteAssetPath)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spriteAssetPath);
            if (sprite != null)
            {
                return sprite;
            }

            var existingTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(spriteAssetPath);
            if (existingTexture == null)
            {
                return null;
            }

            return ChangeTextureToSprite(spriteAssetPath);
        }

        private static void HideInvalidImagePlaceholder(GameObject nodeGo)
        {
            if (nodeGo == null)
            {
                return;
            }

            var image = nodeGo.GetComponent<Image>();
            if (image == null || image.sprite != null)
            {
                return;
            }

            image.enabled = false;
            var color = image.color;
            image.color = new UnityEngine.Color(color.r, color.g, color.b, 0f);
        }

        public static string ResolveFillSpriteAssetPath(FigmaImporter importer, Node node, int fillIndex)
        {
            if (importer == null || node == null)
            {
                return null;
            }

            var idToken = GetNodeIdToken(node.id);
            var canonicalFileName = $"fill_{idToken}_{fillIndex}.png";
            var canonicalAssetPath = FigmaPathUtils.BuildAssetPath(importer.GetRendersFolderPath(), canonicalFileName);
            var existingCanonical = AssetDatabase.LoadAssetAtPath<Texture2D>(canonicalAssetPath);
            if (existingCanonical != null)
            {
                return canonicalAssetPath;
            }

            var rendersFolderAssetPath = FigmaPathUtils.BuildAssetPath(importer.GetRendersFolderPath());
            var guids = AssetDatabase.FindAssets($"{idToken}_{fillIndex} t:Texture2D", new[] { rendersFolderAssetPath });
            foreach (var guid in guids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    return assetPath;
                }
            }

            return canonicalAssetPath;
        }

        private static string ResolveNodeSpriteAssetPath(FigmaImporter importer, Node node, string extension)
        {
            if (importer == null || node == null)
            {
                return null;
            }

            var normalizedExtension = extension?.Trim().TrimStart('.').ToLowerInvariant();
            if (string.IsNullOrEmpty(normalizedExtension))
            {
                normalizedExtension = "png";
            }

            var idToken = GetNodeIdToken(node.id);
            var canonicalFileName = $"node_{idToken}.{normalizedExtension}";
            var canonicalAssetPath = FigmaPathUtils.BuildAssetPath(importer.GetRendersFolderPath(), canonicalFileName);
            var hasCanonicalAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(canonicalAssetPath) != null ||
                                    AssetDatabase.LoadAssetAtPath<Sprite>(canonicalAssetPath) != null;
            if (hasCanonicalAsset)
            {
                return canonicalAssetPath;
            }

            var rendersFolderAssetPath = FigmaPathUtils.BuildAssetPath(importer.GetRendersFolderPath());
            var guids = AssetDatabase.FindAssets($"{idToken} t:Texture2D", new[] { rendersFolderAssetPath });
            foreach (var guid in guids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (assetPath.EndsWith($".{normalizedExtension}", StringComparison.OrdinalIgnoreCase))
                {
                    return assetPath;
                }
            }

            return canonicalAssetPath;
        }

        private static string GetNodeIdToken(string nodeId)
        {
            return FigmaPathUtils.SanitizeFileName((nodeId ?? "node").Replace(':', '_'), "node");
        }
    }
}

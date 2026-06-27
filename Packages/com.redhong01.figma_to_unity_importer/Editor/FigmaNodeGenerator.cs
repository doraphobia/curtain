using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FigmaImporter.Editor.EditorTree.TreeData;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace FigmaImporter.Editor
{
    public class FigmaNodeGenerator
    {
        private readonly FigmaImporter _importer;
        private readonly CancellationToken _cancellationToken;

        public FigmaNodeGenerator(FigmaImporter importer, CancellationToken cancellationToken = default)
        {
            _importer = importer;
            _cancellationToken = cancellationToken;
        }

        private async Task WaitWhilePausedAndCheckCancellation()
        {
            _cancellationToken.ThrowIfCancellationRequested();
            await _importer.WaitWhilePausedAsync(_cancellationToken);
            _cancellationToken.ThrowIfCancellationRequested();
        }

        public Task GenerateNode(Node node, GameObject parent, IList<NodeTreeElement> nodeTreeElements)
        {
            return GenerateNode(node, parent, nodeTreeElements, Vector2.zero, false, false, false, true, null, true);
        }

        public Task GenerateNodeForSync(
            Node node,
            GameObject parent,
            IList<NodeTreeElement> nodeTreeElements,
            Node parentNode,
            bool isImportRoot = false,
            bool includeChildren = true)
        {
            if (node == null || parent == null)
            {
                return Task.CompletedTask;
            }

            var hasParentAbsolutePosition = parentNode != null && parentNode.absoluteBoundingBox != null;
            var parentAbsolutePosition = hasParentAbsolutePosition
                ? parentNode.absoluteBoundingBox.GetPosition()
                : Vector2.zero;
            var parentIsAutoLayout = AutoLayoutUtils.IsAutoLayoutContainer(parentNode);
            var parentIsHorizontalAutoLayout = AutoLayoutUtils.IsHorizontal(parentNode);

            return GenerateNode(
                node,
                parent,
                nodeTreeElements,
                parentAbsolutePosition,
                hasParentAbsolutePosition,
                parentIsAutoLayout,
                parentIsHorizontalAutoLayout,
                isImportRoot,
                parentNode,
                includeChildren);
        }

        private async Task GenerateNode(
            Node node,
            GameObject parent,
            IList<NodeTreeElement> nodeTreeElements,
            Vector2 parentAbsolutePosition,
            bool hasParentAbsolutePosition,
            bool parentIsAutoLayout,
            bool parentIsHorizontalAutoLayout,
            bool isImportRoot,
            Node parentNode,
            bool includeChildren)
        {
            await WaitWhilePausedAndCheckCancellation();
            FigmaNodesProgressInfo.CurrentNode ++;
            FigmaNodesProgressInfo.CurrentInfo = "Node generation in progress";
            FigmaNodesProgressInfo.ShowProgress(0f);
            
            //RendersFolderの有無の確認
            GenerateRenderSaveFolder(_importer.GetRendersFolderPath());
            
            var boundingBox = node.absoluteBoundingBox;
            if (parent == null)
            {
                throw new Exception("[FigmaImporter] Parent is null. Set the canvas reference.");
            }

            var isParentCanvas = parent.GetComponent<Canvas>();
            var nodeAbsolutePosition = boundingBox != null ? boundingBox.GetPosition() : Vector2.zero;
            var positionOffset = isParentCanvas || !hasParentAbsolutePosition
                ? nodeAbsolutePosition
                : parentAbsolutePosition;
            
            GameObject nodeGo = null;
            var treeElement = nodeTreeElements.FirstOrDefault(x => x.figmaId == node.id);
            if (treeElement == null)
            {
                Debug.LogWarning($"[FigmaImporter] Couldn't find tree element for node {node.id}.");
                return;
            }

            if (treeElement.actionType != ActionType.None)
            {
                nodeGo = isParentCanvas? null: TransformUtils.TryToFindPreviouslyCreatedObject(parent, node.id);
                RectTransform parentT = null;
                RectTransform rectTransform = null;
                if (nodeGo == null)
                {
                    nodeGo = new GameObject($"{node.name} [{node.id}]", typeof(RectTransform));
                    parentT = TransformUtils.EnsureRectTransform(parent, $"Parent for node {node.id}");
                    rectTransform = TransformUtils.EnsureRectTransform(nodeGo, $"Node {node.id}");
                    TransformUtils.SetParent(parentT, rectTransform);
                }
                else
                {
                    rectTransform = TransformUtils.EnsureRectTransform(nodeGo, $"Existing node {node.id}");
                    if (rectTransform == null)
                    {
                        return;
                    }

                    isParentCanvas = parent.GetComponent<Canvas>();
                    positionOffset = isParentCanvas || !hasParentAbsolutePosition
                        ? nodeAbsolutePosition
                        : parentAbsolutePosition;
                    parentT = TransformUtils.EnsureRectTransform(parent, $"Existing parent for node {node.id}");
                    if (rectTransform.parent != parentT && rectTransform != parentT)
                    {
                        TransformUtils.SetParent(parentT, rectTransform);
                    }
                }

                var nodeUiGo = rectTransform != null ? rectTransform.gameObject : nodeGo;
                var isAbsoluteInParentAutoLayout = parentIsAutoLayout && AutoLayoutUtils.IsAbsolutePositionedInAutoLayout(node);
                var shouldUseAbsolutePosition = !parentIsAutoLayout || isAbsoluteInParentAutoLayout;
                var resolvedNodeRotation = TransformUtils.ResolveNodeRotation(node);
                TransformUtils.SetPosition(
                    parentT,
                    rectTransform,
                    node,
                    boundingBox,
                    _importer,
                    positionOffset,
                    shouldUseAbsolutePosition,
                    parentNode);
                TransformUtils.SetRotation(rectTransform, resolvedNodeRotation);
                if (parentIsAutoLayout)
                {
                    AutoLayoutUtils.ConfigureChildForParentAutoLayout(
                        node,
                        nodeUiGo,
                        _importer.Scale,
                        parentIsHorizontalAutoLayout,
                        isAbsoluteInParentAutoLayout);
                }
                else
                {
                    AutoLayoutUtils.ClearChildAutoLayoutHints(nodeUiGo);
                }
                if (!isParentCanvas && shouldUseAbsolutePosition && !parentIsAutoLayout && Mathf.Abs(resolvedNodeRotation) <= 0.001f)
                    TransformUtils.SetConstraints(parentT, rectTransform, node.constraints);
                ImageUtils.SetMask(node, nodeUiGo);
                AutoLayoutUtils.ApplyContainerLayout(node, nodeUiGo, _importer.Scale);
                nodeGo = nodeUiGo;
            }
            
            switch (treeElement.actionType)
            {
                case ActionType.None:
                    break;
                case ActionType.Render:
                    await WaitWhilePausedAndCheckCancellation();
                    if (treeElement.sprite != null)
                    {
                        ImageUtils.AddOverridenSprite(nodeGo, treeElement.sprite);
                        break;
                    }
                    await ImageUtils.RenderNodeAndApply(node, nodeGo, _importer, _cancellationToken);
                    break;
#if VECTOR_GRAHICS_IMPORTED
                case ActionType.SvgRender:
                    await WaitWhilePausedAndCheckCancellation();
                    if (treeElement.sprite != null)
                    {
                        ImageUtils.AddOverridenSvgSprite(nodeGo, treeElement.sprite);
                        break;
                    }
                    await ImageUtils.RenderSvgNodeAndApply(node, nodeGo, _importer, _cancellationToken);
                    break;
#endif
                case ActionType.Generate:
                    await WaitWhilePausedAndCheckCancellation();
                    if (treeElement.sprite != null)
                    {
                        ImageUtils.AddOverridenSprite(nodeGo, treeElement.sprite);
                    }
                    else
                    {
                        AddText(node, nodeGo);
                        await AddFills(node, nodeGo);
                        if (node.children == null || !includeChildren) break;
                    }
                    if (node.children == null || !includeChildren) break;
                    await GenerateChildren(node, nodeGo, nodeTreeElements, nodeAbsolutePosition);
                    AutoLayoutUtils.FinalizeContainerLayout(node, nodeGo, _importer.Scale);
                    break;
                case ActionType.Transform:
                    if (node.children == null || !includeChildren)
                    {
                        break;
                    }
                    await GenerateChildren(node, nodeGo, nodeTreeElements, nodeAbsolutePosition);
                    AutoLayoutUtils.FinalizeContainerLayout(node, nodeGo, _importer.Scale);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (isImportRoot && treeElement.actionType != ActionType.None && nodeGo != null)
            {
                AttachOrUpdateSyncBinding(node, nodeGo);
            }
        }

        private async Task GenerateChildren(
            Node parentNode,
            GameObject parentGo,
            IList<NodeTreeElement> nodeTreeElements,
            Vector2 parentAbsolutePosition)
        {
            if (parentNode?.children == null || parentNode.children.Length == 0 || parentGo == null)
            {
                return;
            }

            if (!FigmaMaskingUtils.HasDirectMaskChildren(parentNode))
            {
                for (var i = 0; i < parentNode.children.Length; i++)
                {
                    await GenerateSingleChild(parentNode.children[i], parentNode, parentGo, nodeTreeElements, parentAbsolutePosition);
                }

                return;
            }

            var maskFlow = FigmaMaskingUtils.ResolveMaskFlowDirection(parentNode);
            if (maskFlow == FigmaMaskFlowDirection.Backward)
            {
                await GenerateChildrenWithBackwardMaskFlow(parentNode, parentGo, nodeTreeElements, parentAbsolutePosition);
                return;
            }

            await GenerateChildrenWithForwardMaskFlow(parentNode, parentGo, nodeTreeElements, parentAbsolutePosition);
        }

        private async Task GenerateChildrenWithForwardMaskFlow(
            Node parentNode,
            GameObject parentGo,
            IList<NodeTreeElement> nodeTreeElements,
            Vector2 parentAbsolutePosition)
        {
            GameObject currentMaskGo = null;
            for (var i = 0; i < parentNode.children.Length; i++)
            {
                var child = parentNode.children[i];
                await GenerateSingleChild(child, parentNode, parentGo, nodeTreeElements, parentAbsolutePosition);
                var childGo = TryFindGeneratedChildUnder(parentGo, child);
                if (childGo == null)
                {
                    continue;
                }

                if (FigmaMaskingUtils.IsMaskNode(child))
                {
                    ImageUtils.ConfigureExplicitMaskNode(child, childGo);
                    currentMaskGo = childGo;
                    continue;
                }

                if (currentMaskGo != null)
                {
                    ReparentToMaskScope(childGo, currentMaskGo);
                }
            }
        }

        private async Task GenerateChildrenWithBackwardMaskFlow(
            Node parentNode,
            GameObject parentGo,
            IList<NodeTreeElement> nodeTreeElements,
            Vector2 parentAbsolutePosition)
        {
            var pendingMaskedChildren = new List<GameObject>();
            for (var i = 0; i < parentNode.children.Length; i++)
            {
                var child = parentNode.children[i];
                await GenerateSingleChild(child, parentNode, parentGo, nodeTreeElements, parentAbsolutePosition);
                var childGo = TryFindGeneratedChildUnder(parentGo, child);
                if (childGo == null)
                {
                    continue;
                }

                if (!FigmaMaskingUtils.IsMaskNode(child))
                {
                    pendingMaskedChildren.Add(childGo);
                    continue;
                }

                ImageUtils.ConfigureExplicitMaskNode(child, childGo);
                for (var pendingIndex = 0; pendingIndex < pendingMaskedChildren.Count; pendingIndex++)
                {
                    ReparentToMaskScope(pendingMaskedChildren[pendingIndex], childGo);
                }

                pendingMaskedChildren.Clear();
            }
        }

        private async Task GenerateSingleChild(
            Node child,
            Node parentNode,
            GameObject parentGo,
            IList<NodeTreeElement> nodeTreeElements,
            Vector2 parentAbsolutePosition)
        {
            if (child == null)
            {
                return;
            }

            await WaitWhilePausedAndCheckCancellation();
            await GenerateNode(
                child,
                parentGo,
                nodeTreeElements,
                parentAbsolutePosition,
                true,
                AutoLayoutUtils.IsAutoLayoutContainer(parentNode),
                AutoLayoutUtils.IsHorizontal(parentNode),
                false,
                parentNode,
                true);
        }

        private static GameObject TryFindGeneratedChildUnder(GameObject parentGo, Node child)
        {
            if (parentGo == null || child == null)
            {
                return null;
            }

            return TransformUtils.TryToFindPreviouslyCreatedObject(parentGo, child.id);
        }

        private static void ReparentToMaskScope(GameObject childGo, GameObject maskGo)
        {
            if (childGo == null || maskGo == null || childGo == maskGo)
            {
                return;
            }

            var childRect = TransformUtils.EnsureRectTransform(childGo, "Mask child");
            var maskRect = TransformUtils.EnsureRectTransform(maskGo, "Mask scope");
            if (childRect == null || maskRect == null || childRect.parent == maskRect)
            {
                return;
            }

            childRect.SetParent(maskRect, true);
            childRect.localScale = Vector3.one;
        }

        private void AddText(Node node, GameObject nodeGo)
        {
            if (node.type != "TEXT")
            {
                return;
            }

            var tmp = TMPUtils.GetOrAddTMPComponentToObject(nodeGo);
            if (tmp == null)
            {
                return;
            }

            var style = node.style;
            if (style == null)
            {
                return;
            }

            // Keep raw text first so font fallback and glyph coverage are resolved on real content.
            tmp.text = node.characters ?? string.Empty;
            var settings = FigmaImporterSettings.GetInstance();
            var adapterEnabled = settings == null || settings.EnableTypographyAdapter;

            TMPUtils.ApplyFigmaStyleToTMP(tmp, style, _importer.Scale, applyTypographyDetails: !adapterEnabled);
            tmp.alignment = TMPUtils.FigmaAlignmentToTMP(style.textAlignHorizontal, style.textAlignVertical);
            tmp.fontStyle = TMPUtils.FigmaFontStyleToTMP(
                style.textDecoration,
                style.textCase,
                style);
            if (!adapterEnabled)
            {
                TMPUtils.ApplyFigmaNodeTypographyBehavior(tmp, node, style);
            }
            TMPUtils.ApplyStyleOverrideFallbacksToTMP(tmp, node);
            _importer.ApplyTextRenderingPipeline(tmp, node);
        }

        
        


        private async Task AddFills(Node node, GameObject nodeGo)
        {
            if (node.fills == null || node.fills.Length == 0)
            {
                return;
            }

            var gg = GetGradientsGenerator();
            Image image = nodeGo.GetComponent<Image>();
            var tmp = nodeGo.GetComponent<TextMeshProUGUI>();
            var appliedVisualToImage = false;
            var hasVisibleImageFill = false;
            
            for (var index = 0; index < node.fills.Length; index++)
            {
                var fill = node.fills[index];
                if (fill == null)
                {
                    continue;
                }

                var fillVisible = fill.visible != "false";
                if (string.Equals(fill.type, "IMAGE", StringComparison.OrdinalIgnoreCase) && fillVisible)
                {
                    hasVisibleImageFill = true;
                }

                if (index != 0)
                {
                    var go = TransformUtils.InstantiateChild(nodeGo, fill.type);
                    if (fillVisible)
                    {
                        image = go.GetComponent<Image>() ?? go.AddComponent<Image>();
                    }
                }

                switch (fill.type)
                {
                    case "SOLID":
                        if (tmp != null)
                        {
                            tmp.color = fill.color.ToColor();
                        }
                        else
                        {
                            if (image == null)
                            {
                                image = nodeGo.GetComponent<Image>() ?? nodeGo.AddComponent<Image>();
                            }
                            image.color = fill.color.ToColor();
                            appliedVisualToImage = true;
                        }
                        break;
                    case "GRADIENT_LINEAR" when tmp != null:
                        var gradient = fill.gradientStops;
                        tmp.enableVertexGradient = true;
                        var firstColor = gradient.Length <= 0 ? UnityEngine.Color.white : ColorUtils.ConvertToUnityColor(gradient[0].color);
                        var secondColor = gradient.Length <= 1 ? firstColor : ColorUtils.ConvertToUnityColor(gradient[1].color);
                        var thirdColor = gradient.Length <= 2 ? UnityEngine.Color.white : ColorUtils.ConvertToUnityColor(gradient[2].color);
                        var fourthColor = gradient.Length <= 3 ? thirdColor : ColorUtils.ConvertToUnityColor(gradient[3].color); 
                        tmp.colorGradient = new VertexGradient(firstColor, secondColor, thirdColor, fourthColor);
                        break;
                    case "IMAGE":
                        await ImageUtils.RenderNodeAndApply(node, nodeGo, _importer, _cancellationToken);
                        var renderedImage = nodeGo.GetComponent<Image>();
                        if (renderedImage != null && renderedImage.sprite != null)
                        {
                            appliedVisualToImage = true;
                        }
                        break;
                    case "GRADIENT_LINEAR":
                    case "GRADIENT_RADIAL":
                    case "GRADIENT_DIAMOND":
                    case "GRADIENT_ANGULAR":
                        if (image == null)
                        {
                            image = nodeGo.GetComponent<Image>() ?? nodeGo.AddComponent<Image>();
                        }
                        var spriteAssetPath = ImageUtils.ResolveFillSpriteAssetPath(_importer, node, index);
                        if (string.IsNullOrWhiteSpace(spriteAssetPath))
                        {
                            break;
                        }

                        // Skip recreating gradients if this rendered asset already exists.
                        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spriteAssetPath);
                        if (sprite == null)
                        {
                            var tex = gg.GetTexture(fill, node.absoluteBoundingBox.GetSize(), 256);
                            ImageUtils.SaveTexture(tex, spriteAssetPath);
                            sprite = ImageUtils.ChangeTextureToSprite(spriteAssetPath);
                        }

                        if (sprite != null)
                        {
                            image.sprite = sprite;
                            image.color = UnityEngine.Color.white;
                            appliedVisualToImage = true;
                        }
                        break;
                    default:
                        // Keep unsupported fill types from creating white placeholders.
                        ImportFallbackRegistry.ReportMissingIssue(
                            "Fill",
                            node.id,
                            $"Unsupported fill type '{fill.type}' in Generate mode.",
                            node.id,
                            node.name);
                        break;
                }

                if (image != null)
                {
                    image.enabled = fillVisible;
                }
            }

            if (hasVisibleImageFill && image != null && image.sprite == null && !appliedVisualToImage && tmp == null)
            {
                // If image fills fail to render, avoid leaving default white Image placeholders.
                image.enabled = false;
                var c = image.color;
                image.color = new UnityEngine.Color(c.r, c.g, c.b, 0f);
            }
        }

        private static GradientsGenerator GetGradientsGenerator()
        {
            var foundAssets = AssetDatabase.FindAssets("t:GradientsGenerator");
            GradientsGenerator gg = null;
            var preferredPath = FigmaPathUtils.LocalGradientsGeneratorAssetPath;

            var localAsset = AssetDatabase.LoadAssetAtPath<GradientsGenerator>(preferredPath);
            if (localAsset != null)
            {
                return localAsset;
            }

            if (foundAssets.Length == 0)
            {
                gg = ScriptableObject.CreateInstance<GradientsGenerator>();
                FigmaPathUtils.EnsureAssetFolderExists(FigmaPathUtils.LocalRootAssetPath);
                FigmaPathUtils.EnsureAssetFolderExists(FigmaPathUtils.LocalEditorFolderAssetPath);
                AssetDatabase.CreateAsset(gg, preferredPath);
                AssetDatabase.SaveAssets();
            }
            else
            {
                foreach (var gradientGeneratorId in foundAssets)
                {
                    var assetPath = AssetDatabase.GUIDToAssetPath(gradientGeneratorId);
                    var candidate = AssetDatabase.LoadAssetAtPath<GradientsGenerator>(assetPath);
                    if (candidate == null)
                    {
                        continue;
                    }

                    // Prefer copying legacy/shared configuration into local mutable asset.
                    gg = ScriptableObject.CreateInstance<GradientsGenerator>();
                    EditorUtility.CopySerialized(candidate, gg);
                    FigmaPathUtils.EnsureAssetFolderExists(FigmaPathUtils.LocalRootAssetPath);
                    FigmaPathUtils.EnsureAssetFolderExists(FigmaPathUtils.LocalEditorFolderAssetPath);
                    AssetDatabase.CreateAsset(gg, preferredPath);
                    AssetDatabase.SaveAssets();
                    break;
                }

                if (gg == null)
                {
                    gg = ScriptableObject.CreateInstance<GradientsGenerator>();
                    FigmaPathUtils.EnsureAssetFolderExists(FigmaPathUtils.LocalRootAssetPath);
                    FigmaPathUtils.EnsureAssetFolderExists(FigmaPathUtils.LocalEditorFolderAssetPath);
                    AssetDatabase.CreateAsset(gg, preferredPath);
                    AssetDatabase.SaveAssets();
                }
            }
            return gg;
        }

        private static void GenerateRenderSaveFolder(string path)
        {
            FigmaPathUtils.EnsureRendersFolderExists(path);
        }

        private void AttachOrUpdateSyncBinding(Node node, GameObject nodeGo)
        {
            if (node == null || nodeGo == null)
            {
                return;
            }

            var binding = nodeGo.GetComponent<global::FigmaImporter.FigmaFrameSyncBinding>();
            if (binding == null)
            {
                binding = nodeGo.AddComponent<global::FigmaImporter.FigmaFrameSyncBinding>();
            }

            if (binding == null)
            {
                Debug.LogWarning($"[FigmaImporter] Failed to attach FigmaFrameSyncBinding on '{nodeGo.name}'.");
                return;
            }

            Undo.RecordObject(binding, "Update Figma Frame Sync Binding");
            binding.Configure(
                _importer.CurrentFigmaUrl,
                _importer.CurrentFileKey,
                node.id,
                node.name);
            binding.SetBaselineSnapshot(FigmaFrameSyncDiffUtility.BuildSnapshot(node), "Generated by FigmaImporter");
            EditorUtility.SetDirty(binding);
        }
    }
}

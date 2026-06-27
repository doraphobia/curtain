using System;
using System.Collections.Generic;
using System.Linq;
using FigmaImporter.Editor.EditorTree.TreeData;

namespace FigmaImporter.Editor
{
    public class NodesAnalyzer
    {
        public static void AnalyzePngMode(IList<Node> nodes, IList<NodeTreeElement> nodesTreeElements)
        {
            foreach (var node in nodes)
            {
                AnalyzeSingleNodePng(node, nodesTreeElements.FirstOrDefault(x => x.figmaId == node.id));
                if (node.children != null)
                {
                    AnalyzePngMode(node.children, nodesTreeElements);
                }
            }
        }

        public static void AnalyzeGenerateMode(IList<Node> nodes, IList<NodeTreeElement> nodesTreeElements)
        {
            foreach (var node in nodes)
            {
                var matchedFigmaNode = nodesTreeElements.FirstOrDefault(nodeTreeElement => nodeTreeElement.figmaId == node.id);
                if (matchedFigmaNode != null)
                {
                    matchedFigmaNode.actionType = ActionType.Generate;
                }

                if (node.children != null)
                {
                    AnalyzeGenerateMode(node.children, nodesTreeElements);
                }
            }
        }

        public static void AnalyzeRenderMode(IList<Node> nodes, IList<NodeTreeElement> nodesTreeElements)
        {
            foreach (var node in nodes)
            {
                AnalyzeSingleNode(node, nodesTreeElements.FirstOrDefault(x => x.figmaId == node.id));
                if (node.children != null)
                {
                    AnalyzeRenderMode(node.children, nodesTreeElements);
                }
            }
        }

        public static void AnalyzeTransformMode(IList<Node> nodes, IList<NodeTreeElement> nodesTreeElements)
        {
            foreach (var node in nodes)
            {
                var matchedFigmaNode = nodesTreeElements.FirstOrDefault(nodeTreeElement => nodeTreeElement.figmaId == node.id);
                if (matchedFigmaNode != null)
                {
                    matchedFigmaNode.actionType = ActionType.Transform;
                }

                if (node.children != null)
                {
                    AnalyzeTransformMode(node.children, nodesTreeElements);
                }
            }
        }

        public static void AnalyzeSVGMode(IList<Node> nodes, IList<NodeTreeElement> nodesTreeElements)
        {
            foreach (var node in nodes)
            {
                AnalyzeSingleNodeSVG(node, nodesTreeElements.FirstOrDefault(x => x.figmaId == node.id));
                if (node.children != null)
                {
                    AnalyzeSVGMode(node.children, nodesTreeElements);
                }
            }
        }

        public static ActionDisplayState GetDisplayState(
            Node node,
            NodeTreeElement treeElement,
            IList<NodeTreeElement> nodesTreeElements)
        {
            if (treeElement == null)
            {
                return ActionDisplayState.Skip;
            }

            if (node == null || node.children == null || node.children.Length == 0)
            {
                return ActionDisplayStateDisplayNames.FromActionType(treeElement.actionType);
            }

            if (SubtreeMatchesUniformAction(node, nodesTreeElements, ActionType.None))
            {
                return ActionDisplayState.Skip;
            }

            if (SubtreeMatchesUniformAction(node, nodesTreeElements, ActionType.Render))
            {
                return ActionDisplayState.Render;
            }

            if (SubtreeMatchesUniformAction(node, nodesTreeElements, ActionType.Generate) ||
                SubtreeMatchesPreset(node, nodesTreeElements, ActionDisplayState.Generate))
            {
                return ActionDisplayState.Generate;
            }

            if (SubtreeMatchesUniformAction(node, nodesTreeElements, ActionType.Transform) ||
                SubtreeMatchesPreset(node, nodesTreeElements, ActionDisplayState.Transform))
            {
                return ActionDisplayState.Transform;
            }

#if VECTOR_GRAHICS_IMPORTED
            if (SubtreeMatchesUniformAction(node, nodesTreeElements, ActionType.SvgRender) ||
                SubtreeMatchesPreset(node, nodesTreeElements, ActionDisplayState.SvgRender))
            {
                return ActionDisplayState.SvgRender;
            }
#endif

            return ActionDisplayState.Customized;
        }

        public static void ApplyDisplayStateToSubtree(
            Node node,
            ActionDisplayState displayState,
            IList<NodeTreeElement> nodesTreeElements)
        {
            if (node == null || nodesTreeElements == null)
            {
                return;
            }

            switch (displayState)
            {
                case ActionDisplayState.Skip:
                    SetSubtreeActionRecursively(node, ActionType.None, nodesTreeElements);
                    break;
                case ActionDisplayState.Render:
                    SetSubtreeActionRecursively(node, ActionType.Render, nodesTreeElements);
                    break;
                case ActionDisplayState.Generate:
                    ApplyNativeGeneratePreset(node, nodesTreeElements);
                    break;
                case ActionDisplayState.Transform:
                    ApplyTransformPreset(node, nodesTreeElements);
                    break;
#if VECTOR_GRAHICS_IMPORTED
                case ActionDisplayState.SvgRender:
                    ApplySvgPreset(node, nodesTreeElements);
                    break;
#endif
                case ActionDisplayState.Customized:
                default:
                    break;
            }
        }

        public static void CheckActions(IList<Node> nodes, IList<NodeTreeElement> nodesTreeElements)
        {
            // Keep per-node manual overrides intact. Generation already stops traversing
            // into skipped/raster-rendered parents, so descendants do not need to be
            // rewritten to ActionType.None just to reflect inheritance in the UI.
        }

        private static void ApplyNativeGeneratePreset(Node node, IList<NodeTreeElement> nodesTreeElements)
        {
            SetActionForNode(node.id, ActionType.Generate, nodesTreeElements);
            if (node.children == null || node.children.Length == 0)
            {
                return;
            }

            AnalyzeRenderMode(node.children, nodesTreeElements);
        }

        private static void ApplyTransformPreset(Node node, IList<NodeTreeElement> nodesTreeElements)
        {
            SetActionForNode(node.id, ActionType.Transform, nodesTreeElements);
            if (node.children == null || node.children.Length == 0)
            {
                return;
            }

            AnalyzeRenderMode(node.children, nodesTreeElements);
        }

#if VECTOR_GRAHICS_IMPORTED
        private static void ApplySvgPreset(Node node, IList<NodeTreeElement> nodesTreeElements)
        {
            AnalyzeSingleNodeSVG(node, nodesTreeElements.FirstOrDefault(x => x.figmaId == node.id));
            if (node.children == null || node.children.Length == 0)
            {
                return;
            }

            AnalyzeSVGMode(node.children, nodesTreeElements);
        }
#endif

        private static bool SubtreeMatchesUniformAction(
            Node node,
            IList<NodeTreeElement> nodesTreeElements,
            ActionType actionType)
        {
            if (node == null)
            {
                return false;
            }

            var treeElement = nodesTreeElements.FirstOrDefault(x => x.figmaId == node.id);
            if (treeElement == null || treeElement.actionType != actionType)
            {
                return false;
            }

            if (node.children == null)
            {
                return true;
            }

            foreach (var child in node.children)
            {
                if (!SubtreeMatchesUniformAction(child, nodesTreeElements, actionType))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SubtreeMatchesPreset(
            Node node,
            IList<NodeTreeElement> nodesTreeElements,
            ActionDisplayState displayState)
        {
            var expectedActions = BuildExpectedSubtreeActionMap(node, displayState);
            foreach (var expected in expectedActions)
            {
                var actual = nodesTreeElements.FirstOrDefault(x => x.figmaId == expected.Key);
                if (actual == null || actual.actionType != expected.Value)
                {
                    return false;
                }
            }

            return expectedActions.Count > 0;
        }

        private static Dictionary<string, ActionType> BuildExpectedSubtreeActionMap(Node node, ActionDisplayState displayState)
        {
            var tempElements = BuildTemporaryTreeElements(node);
            switch (displayState)
            {
                case ActionDisplayState.Skip:
                    SetSubtreeActionRecursively(node, ActionType.None, tempElements);
                    break;
                case ActionDisplayState.Render:
                    SetSubtreeActionRecursively(node, ActionType.Render, tempElements);
                    break;
                case ActionDisplayState.Generate:
                    ApplyNativeGeneratePreset(node, tempElements);
                    break;
                case ActionDisplayState.Transform:
                    ApplyTransformPreset(node, tempElements);
                    break;
#if VECTOR_GRAHICS_IMPORTED
                case ActionDisplayState.SvgRender:
                    ApplySvgPreset(node, tempElements);
                    break;
#endif
                case ActionDisplayState.Customized:
                default:
                    break;
            }

            return tempElements.ToDictionary(x => x.figmaId, x => x.actionType, StringComparer.OrdinalIgnoreCase);
        }

        private static List<NodeTreeElement> BuildTemporaryTreeElements(Node node)
        {
            var result = new List<NodeTreeElement>();
            var idCounter = 0;
            AppendTemporaryElements(node, 0, ref idCounter, result);
            return result;
        }

        private static void AppendTemporaryElements(
            Node node,
            int depth,
            ref int idCounter,
            ICollection<NodeTreeElement> result)
        {
            if (node == null)
            {
                return;
            }

            result.Add(new NodeTreeElement(node.name ?? string.Empty, node.id ?? string.Empty, ActionType.None, null, depth, idCounter++));
            if (node.children == null)
            {
                return;
            }

            foreach (var child in node.children)
            {
                AppendTemporaryElements(child, depth + 1, ref idCounter, result);
            }
        }

        private static void SetSubtreeActionRecursively(
            Node node,
            ActionType actionType,
            IList<NodeTreeElement> nodesTreeElements)
        {
            if (node == null)
            {
                return;
            }

            SetActionForNode(node.id, actionType, nodesTreeElements);
            if (node.children == null)
            {
                return;
            }

            foreach (var child in node.children)
            {
                SetSubtreeActionRecursively(child, actionType, nodesTreeElements);
            }
        }

        private static void SetActionForNode(
            string nodeId,
            ActionType actionType,
            IList<NodeTreeElement> nodesTreeElements)
        {
            var element = nodesTreeElements.FirstOrDefault(x => x.figmaId == nodeId);
            if (element != null)
            {
                element.actionType = actionType;
            }
        }

        private static void AnalyzeSingleNodeSVG(Node node, NodeTreeElement treeElement)
        {
            if (treeElement == null)
            {
                return;
            }

#if VECTOR_GRAHICS_IMPORTED
            if (FigmaMaskingUtils.IsMaskNode(node))
            {
                // Explicit mask nodes should always use raster image output so UGUI Mask can clip by alpha reliably.
                treeElement.actionType = ActionType.Render;
                return;
            }

            if (ShouldPreferRasterRender(node))
            {
                treeElement.actionType = ActionType.Render;
                return;
            }

            if (node.type != "TEXT" && (node.children == null || node.children.Length == 0))
            {
                treeElement.actionType = ActionType.SvgRender;
            }
            else
            {
                treeElement.actionType = ActionType.Generate;
            }
#endif
        }

        private static void AnalyzeSingleNode(Node node, NodeTreeElement treeElement)
        {
            if (treeElement == null)
            {
                return;
            }

            if (FigmaMaskingUtils.IsMaskNode(node))
            {
                treeElement.actionType = ActionType.Render;
                return;
            }

            if (ShouldPreferRasterRender(node))
            {
                treeElement.actionType = ActionType.Render;
                return;
            }

#if VECTOR_GRAHICS_IMPORTED
            if (node.type != "TEXT" && (node.children == null || node.children.Length == 0))
            {
                treeElement.actionType = ActionType.SvgRender;
                return;
            }
#endif

            if (node.type != "TEXT" && (node.children == null || node.children.Length == 0))
            {
                treeElement.actionType = ActionType.Render;
            }
            else
            {
                treeElement.actionType = ActionType.Generate;
            }
        }

        private static void AnalyzeSingleNodePng(Node node, NodeTreeElement treeElement)
        {
            if (treeElement == null)
            {
                return;
            }

            if (FigmaMaskingUtils.IsMaskNode(node))
            {
                treeElement.actionType = ActionType.Render;
                return;
            }

            if (node.type != "TEXT" && (node.children == null || node.children.Length == 0))
            {
                treeElement.actionType = ActionType.Render;
            }
            else
            {
                treeElement.actionType = ActionType.Generate;
            }
        }

        private static bool ShouldPreferRasterRender(Node node)
        {
            if (node == null ||
                string.Equals(node.type, "TEXT", StringComparison.OrdinalIgnoreCase) ||
                (node.children != null && node.children.Length > 0))
            {
                return false;
            }

            var hasVisibleStroke = node.strokes != null &&
                                   node.strokes.Length > 0 &&
                                   !float.IsNaN(node.strokeWeight) &&
                                   !float.IsInfinity(node.strokeWeight) &&
                                   node.strokeWeight > 0.01f;
            if (!hasVisibleStroke)
            {
                return false;
            }

            var baseBounds = node.absoluteBoundingBox;
            var renderBounds = node.absoluteRenderBounds;
            var collapsedWidth = baseBounds != null && baseBounds.width <= 0.01f;
            var collapsedHeight = baseBounds != null && baseBounds.height <= 0.01f;
            var renderExpandsWidth = renderBounds != null && renderBounds.width > 0.01f;
            var renderExpandsHeight = renderBounds != null && renderBounds.height > 0.01f;

            return (collapsedWidth && renderExpandsWidth) ||
                   (collapsedHeight && renderExpandsHeight) ||
                   collapsedWidth ||
                   collapsedHeight;
        }
    }
}

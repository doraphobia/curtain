using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FigmaImporter.Editor
{
    internal static class AutoLayoutUtils
    {
        private const float Epsilon = 0.0001f;

        public static bool IsAutoLayoutContainer(Node node)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.layoutMode))
            {
                return false;
            }

            return node.layoutMode.Equals("HORIZONTAL", StringComparison.OrdinalIgnoreCase) ||
                   node.layoutMode.Equals("VERTICAL", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsHorizontal(Node node)
        {
            return node != null &&
                   node.layoutMode != null &&
                   node.layoutMode.Equals("HORIZONTAL", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsAbsolutePositionedInAutoLayout(Node node)
        {
            return node != null &&
                   node.layoutPositioning != null &&
                   node.layoutPositioning.Equals("ABSOLUTE", StringComparison.OrdinalIgnoreCase);
        }

        public static void ApplyContainerLayout(Node node, GameObject nodeGo, float scale)
        {
            if (nodeGo == null)
            {
                return;
            }

            if (!IsAutoLayoutContainer(node))
            {
                RemoveIfExists<HorizontalLayoutGroup>(nodeGo);
                RemoveIfExists<VerticalLayoutGroup>(nodeGo);
                RemoveIfExists<ContentSizeFitter>(nodeGo);
                return;
            }

            if (node.layoutWrap != null && node.layoutWrap.Equals("WRAP", StringComparison.OrdinalIgnoreCase))
            {
                ImportFallbackRegistry.ReportMissingIssue(
                    "AutoLayout",
                    "WrapUnsupported",
                    "Figma auto-layout WRAP mode is not fully supported. Importer is using a single-axis layout approximation.",
                    node.id,
                    node.name);
            }

            var isHorizontal = IsHorizontal(node);
            HorizontalOrVerticalLayoutGroup group;
            if (isHorizontal)
            {
                RemoveIfExists<VerticalLayoutGroup>(nodeGo);
                group = GetOrAdd<HorizontalLayoutGroup>(nodeGo);
            }
            else
            {
                RemoveIfExists<HorizontalLayoutGroup>(nodeGo);
                group = GetOrAdd<VerticalLayoutGroup>(nodeGo);
            }

            ConfigureGroup(group, node, scale, isHorizontal);

            var fitter = GetOrAdd<ContentSizeFitter>(nodeGo);
            ConfigureFitter(fitter, node, isHorizontal);
        }

        public static void ConfigureChildForParentAutoLayout(
            Node node,
            GameObject nodeGo,
            float scale,
            bool parentIsHorizontal,
            bool isAbsoluteInParentAutoLayout)
        {
            if (nodeGo == null)
            {
                return;
            }

            var layoutElement = GetOrAdd<LayoutElement>(nodeGo);
            if (isAbsoluteInParentAutoLayout)
            {
                layoutElement.ignoreLayout = true;
                return;
            }

            layoutElement.ignoreLayout = false;
            ApplySizeHints(node, layoutElement, scale);
            ConfigureChildSizingModes(node, layoutElement, parentIsHorizontal);
            ReportUnsupportedPerChildAlignment(node);
        }

        public static void ClearChildAutoLayoutHints(GameObject nodeGo)
        {
            if (nodeGo == null || !nodeGo.TryGetComponent<LayoutElement>(out var layoutElement) || layoutElement == null)
            {
                return;
            }

            layoutElement.ignoreLayout = false;
            layoutElement.minWidth = -1f;
            layoutElement.minHeight = -1f;
            layoutElement.preferredWidth = -1f;
            layoutElement.preferredHeight = -1f;
            layoutElement.flexibleWidth = 0f;
            layoutElement.flexibleHeight = 0f;
        }

        public static void FinalizeContainerLayout(Node node, GameObject nodeGo, float scale)
        {
            if (!IsAutoLayoutContainer(node) || nodeGo == null)
            {
                return;
            }

            var rectTransform = nodeGo.transform as RectTransform;
            if (rectTransform == null)
            {
                return;
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);

            if (node.primaryAxisAlignItems == null ||
                !node.primaryAxisAlignItems.Equals("SPACE_BETWEEN", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (nodeGo.TryGetComponent<HorizontalLayoutGroup>(out var horizontal))
            {
                if (TryCalculateFigmaMainAxisSpacing(node, true, out var figmaSpacing))
                {
                    horizontal.spacing = figmaSpacing * scale;
                }
                else
                {
                    var calculated = CalculateSpaceBetween(rectTransform, horizontal, 0);
                    horizontal.spacing = calculated > Epsilon ? calculated : node.itemSpacing * scale;
                }
            }
            else if (nodeGo.TryGetComponent<VerticalLayoutGroup>(out var vertical))
            {
                if (TryCalculateFigmaMainAxisSpacing(node, false, out var figmaSpacing))
                {
                    vertical.spacing = figmaSpacing * scale;
                }
                else
                {
                    var calculated = CalculateSpaceBetween(rectTransform, vertical, 1);
                    vertical.spacing = calculated > Epsilon ? calculated : node.itemSpacing * scale;
                }
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
        }

        private static void ApplySizeHints(Node node, LayoutElement layoutElement, float scale)
        {
            if (layoutElement == null)
            {
                return;
            }

            var resolvedSize = TransformUtils.ResolveNodeSize(node, node?.absoluteBoundingBox);
            if (resolvedSize.x > Epsilon && resolvedSize.y > Epsilon)
            {
                layoutElement.preferredWidth = Mathf.Max(0f, resolvedSize.x * scale);
                layoutElement.preferredHeight = Mathf.Max(0f, resolvedSize.y * scale);
            }
            else
            {
                layoutElement.preferredWidth = -1f;
                layoutElement.preferredHeight = -1f;
            }

            layoutElement.minWidth = ToPositiveConstraint(node != null ? node.minWidth : 0f, scale);
            layoutElement.minHeight = ToPositiveConstraint(node != null ? node.minHeight : 0f, scale);
        }

        private static void ConfigureChildSizingModes(Node node, LayoutElement layoutElement, bool parentIsHorizontal)
        {
            if (layoutElement == null)
            {
                return;
            }

            var mainSizing = parentIsHorizontal ? node?.layoutSizingHorizontal : node?.layoutSizingVertical;
            var crossSizing = parentIsHorizontal ? node?.layoutSizingVertical : node?.layoutSizingHorizontal;

            var fillMain = IsFill(mainSizing) || (node != null && node.layoutGrow > Epsilon);
            var fillCross = IsFill(crossSizing) || IsStretch(node?.layoutAlign);

            var mainFlexible = fillMain
                ? Mathf.Max(1f, node != null ? node.layoutGrow : 0f)
                : 0f;
            var crossFlexible = fillCross ? 1f : 0f;

            if (parentIsHorizontal)
            {
                layoutElement.flexibleWidth = mainFlexible;
                layoutElement.flexibleHeight = crossFlexible;
            }
            else
            {
                layoutElement.flexibleHeight = mainFlexible;
                layoutElement.flexibleWidth = crossFlexible;
            }
        }

        private static void ConfigureFitter(ContentSizeFitter fitter, Node node, bool isHorizontal)
        {
            if (fitter == null || node == null)
            {
                return;
            }

            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            if (isHorizontal)
            {
                if (IsAutoSizing(node.primaryAxisSizingMode))
                {
                    fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                }

                if (IsAutoSizing(node.counterAxisSizingMode))
                {
                    fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                }
            }
            else
            {
                if (IsAutoSizing(node.primaryAxisSizingMode))
                {
                    fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                }

                if (IsAutoSizing(node.counterAxisSizingMode))
                {
                    fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                }
            }
        }

        private static void ConfigureGroup(HorizontalOrVerticalLayoutGroup group, Node node, float scale, bool isHorizontal)
        {
            if (group == null || node == null)
            {
                return;
            }

            group.padding = new RectOffset(
                Mathf.RoundToInt(Mathf.Max(0f, node.paddingLeft * scale)),
                Mathf.RoundToInt(Mathf.Max(0f, node.paddingRight * scale)),
                Mathf.RoundToInt(Mathf.Max(0f, node.paddingTop * scale)),
                Mathf.RoundToInt(Mathf.Max(0f, node.paddingBottom * scale)));

            group.spacing = node.itemSpacing * scale;

            // Use LayoutElement hints from children (preferred/flexible) to emulate Figma AUTO/FILL sizing.
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childScaleWidth = false;
            group.childScaleHeight = false;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;
            group.childAlignment = GetChildAlignment(node, isHorizontal);

            if (node.counterAxisAlignItems != null &&
                node.counterAxisAlignItems.Equals("BASELINE", StringComparison.OrdinalIgnoreCase))
            {
                ImportFallbackRegistry.ReportMissingIssue(
                    "AutoLayout",
                    "BaselineUnsupported",
                    "Figma baseline alignment is not fully supported by Unity layout groups. Importer uses MIN alignment fallback.",
                    node.id,
                    node.name);
            }
        }

        private static TextAnchor GetChildAlignment(Node node, bool isHorizontal)
        {
            var primary = NormalizeAxisAlign(node != null ? node.primaryAxisAlignItems : null);
            var counter = NormalizeAxisAlign(node != null ? node.counterAxisAlignItems : null);

            var horizontal = isHorizontal ? primary : counter;
            var vertical = isHorizontal ? counter : primary;

            if (horizontal == AxisAlign.Min)
            {
                if (vertical == AxisAlign.Min) return TextAnchor.UpperLeft;
                if (vertical == AxisAlign.Max) return TextAnchor.LowerLeft;
                return TextAnchor.MiddleLeft;
            }

            if (horizontal == AxisAlign.Max)
            {
                if (vertical == AxisAlign.Min) return TextAnchor.UpperRight;
                if (vertical == AxisAlign.Max) return TextAnchor.LowerRight;
                return TextAnchor.MiddleRight;
            }

            if (vertical == AxisAlign.Min) return TextAnchor.UpperCenter;
            if (vertical == AxisAlign.Max) return TextAnchor.LowerCenter;
            return TextAnchor.MiddleCenter;
        }

        private static float CalculateSpaceBetween(RectTransform container, HorizontalOrVerticalLayoutGroup group, int axis)
        {
            if (container == null || group == null || container.childCount <= 1)
            {
                return 0f;
            }

            var innerSize = axis == 0 ? container.rect.width : container.rect.height;
            var padStart = axis == 0 ? group.padding.left : group.padding.top;
            var padEnd = axis == 0 ? group.padding.right : group.padding.bottom;
            var available = Mathf.Max(0f, innerSize - padStart - padEnd);

            var totalChildren = 0f;
            var childCount = 0;
            for (var i = 0; i < container.childCount; i++)
            {
                var child = container.GetChild(i) as RectTransform;
                if (!ShouldCountInLayout(child))
                {
                    continue;
                }

                var preferred = LayoutUtility.GetPreferredSize(child, axis);
                if (preferred <= Epsilon)
                {
                    preferred = axis == 0 ? child.rect.width : child.rect.height;
                }

                totalChildren += Mathf.Max(0f, preferred);
                childCount++;
            }

            if (childCount <= 1)
            {
                return 0f;
            }

            var remaining = available - totalChildren;
            if (remaining <= Epsilon)
            {
                return 0f;
            }

            return remaining / (childCount - 1);
        }

        private static bool TryCalculateFigmaMainAxisSpacing(Node containerNode, bool isHorizontal, out float spacing)
        {
            spacing = 0f;
            if (containerNode == null || containerNode.children == null || containerNode.children.Length < 2)
            {
                return false;
            }

            var gaps = new List<float>(containerNode.children.Length - 1);
            var previous = containerNode.children[0];
            for (var i = 1; i < containerNode.children.Length; i++)
            {
                var current = containerNode.children[i];
                if (previous?.absoluteBoundingBox == null || current?.absoluteBoundingBox == null)
                {
                    previous = current;
                    continue;
                }

                var previousEnd = isHorizontal
                    ? previous.absoluteBoundingBox.x + previous.absoluteBoundingBox.width
                    : previous.absoluteBoundingBox.y + previous.absoluteBoundingBox.height;
                var currentStart = isHorizontal
                    ? current.absoluteBoundingBox.x
                    : current.absoluteBoundingBox.y;
                var gap = currentStart - previousEnd;
                if (!float.IsNaN(gap) && !float.IsInfinity(gap))
                {
                    gaps.Add(gap);
                }

                previous = current;
            }

            if (gaps.Count == 0)
            {
                return false;
            }

            // Median is robust for effects/masks where a few children may expand render bounds.
            gaps.Sort();
            spacing = gaps[gaps.Count / 2];
            return true;
        }

        private static bool ShouldCountInLayout(RectTransform child)
        {
            if (child == null || !child.gameObject.activeSelf)
            {
                return false;
            }

            var layoutElement = child.GetComponent<LayoutElement>();
            if (layoutElement != null && layoutElement.ignoreLayout)
            {
                return false;
            }

            return true;
        }

        private static float ToPositiveConstraint(float value, float scale)
        {
            if (value <= Epsilon)
            {
                return -1f;
            }

            return Mathf.Max(0f, value * scale);
        }

        private static bool IsAutoSizing(string sizingMode)
        {
            return sizingMode != null &&
                   sizingMode.Equals("AUTO", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFill(string sizingMode)
        {
            return sizingMode != null &&
                   sizingMode.Equals("FILL", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsStretch(string align)
        {
            return align != null &&
                   align.Equals("STRETCH", StringComparison.OrdinalIgnoreCase);
        }

        private static void ReportUnsupportedPerChildAlignment(Node node)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.layoutAlign))
            {
                return;
            }

            if (node.layoutAlign.Equals("INHERIT", StringComparison.OrdinalIgnoreCase) ||
                node.layoutAlign.Equals("STRETCH", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ImportFallbackRegistry.ReportMissingIssue(
                "AutoLayout",
                "PerChildAlignApproximation",
                $"Child layoutAlign='{node.layoutAlign}' is approximated. Unity layout groups only support container-level cross-axis alignment.",
                node.id,
                node.name);
        }

        private static AxisAlign NormalizeAxisAlign(string align)
        {
            if (string.IsNullOrWhiteSpace(align))
            {
                return AxisAlign.Min;
            }

            if (align.Equals("MAX", StringComparison.OrdinalIgnoreCase))
            {
                return AxisAlign.Max;
            }

            if (align.Equals("CENTER", StringComparison.OrdinalIgnoreCase))
            {
                return AxisAlign.Center;
            }

            if (align.Equals("SPACE_BETWEEN", StringComparison.OrdinalIgnoreCase) ||
                align.Equals("BASELINE", StringComparison.OrdinalIgnoreCase))
            {
                return AxisAlign.Min;
            }

            return AxisAlign.Min;
        }

        private static T GetOrAdd<T>(GameObject nodeGo) where T : Component
        {
            if (nodeGo.TryGetComponent<T>(out var component))
            {
                return component;
            }

            return nodeGo.AddComponent<T>();
        }

        private static void RemoveIfExists<T>(GameObject nodeGo) where T : Component
        {
            if (nodeGo == null || !nodeGo.TryGetComponent<T>(out var component) || component == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(component);
                return;
            }

            UnityEngine.Object.DestroyImmediate(component);
        }

        private enum AxisAlign
        {
            Min,
            Center,
            Max
        }
    }
}

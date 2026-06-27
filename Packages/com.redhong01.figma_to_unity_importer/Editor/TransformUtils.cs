using System;
using System.Collections.Generic;
using UnityEngine;

namespace FigmaImporter.Editor
{
    public class TransformUtils
    {
        private const string RectWrapperName = "__FigmaRectRoot";
        private const float CollapsedAxisEpsilon = 0.01f;

        public static RectTransform EnsureRectTransform(GameObject go, string context = "")
        {
            if (go == null)
            {
                return null;
            }

            var existing = go.transform as RectTransform;
            if (existing != null)
            {
                return existing;
            }

            try
            {
                var rect = go.AddComponent<RectTransform>();
                if (rect != null)
                {
                    return rect;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FigmaImporter] Couldn't add RectTransform to '{go.name}'{(string.IsNullOrEmpty(context) ? "" : $" ({context})")}: {e.Message}");
            }

            var existingWrapper = FindRectWrapper(go);
            if (existingWrapper != null)
            {
                return existingWrapper;
            }

            Debug.LogWarning($"[FigmaImporter] '{go.name}' has no RectTransform{(string.IsNullOrEmpty(context) ? "" : $" ({context})")}. Creating a reusable RectTransform wrapper child.");
            var wrapper = new GameObject(RectWrapperName);
            wrapper.transform.SetParent(go.transform, false);
            var wrapperRect = wrapper.AddComponent<RectTransform>();
            wrapperRect.anchorMin = Vector2.zero;
            wrapperRect.anchorMax = Vector2.one;
            wrapperRect.offsetMin = Vector2.zero;
            wrapperRect.offsetMax = Vector2.zero;
            return wrapperRect;
        }

        public static void SetConstraints(RectTransform parentTransform, RectTransform rectTransform,
            Constraints nodeConstraints)
        {
            if (parentTransform == null || rectTransform == null || nodeConstraints == null)
            {
                return;
            }

            Vector2 offsetMin = rectTransform.offsetMin;
            Vector2 offsetMax = rectTransform.offsetMax;
            var parentSize = parentTransform.rect.size;
            Vector2 positionMin = Vector2.Scale(rectTransform.anchorMin, parentSize) + offsetMin;
            Vector2 positionMax = Vector2.Scale(rectTransform.anchorMax, parentSize) + offsetMax;

            Vector3 minAnchor = Vector2.one / 2f;
            Vector3 maxAnchor = Vector2.one / 2f;

            switch (nodeConstraints.horizontal)
            {
                case "LEFT_RIGHT":
                    minAnchor.x = 0f;
                    maxAnchor.x = 1f;
                    break;
                case "LEFT":
                    minAnchor.x = maxAnchor.x = 0f;
                    break;
                case "RIGHT":
                    minAnchor.x = maxAnchor.x = 1f;
                    break;
                case "CENTER":
                    minAnchor.x = maxAnchor.x = 0.5f;
                    break;
                case "SCALE":
                    minAnchor.x = rectTransform.anchorMin.x + rectTransform.offsetMin.x / parentTransform.rect.width;
                    maxAnchor.x = rectTransform.anchorMax.x + rectTransform.offsetMax.x / parentTransform.rect.width;
                    break;
                default:
                    Debug.LogError($"Unknown horizontal constraint {nodeConstraints.horizontal}");
                    break;
            }

            switch (nodeConstraints.vertical)
            {
                case "TOP_BOTTOM":
                    minAnchor.y = 0f;
                    maxAnchor.y = 1f;
                    break;
                case "BOTTOM":
                    minAnchor.y = maxAnchor.y = 0f;
                    break;
                case "TOP":
                    minAnchor.y = maxAnchor.y = 1f;
                    break;
                case "CENTER":
                    minAnchor.y = maxAnchor.y = 0.5f;
                    break;
                case "SCALE":
                    minAnchor.y = rectTransform.anchorMin.y + rectTransform.offsetMin.y / parentTransform.rect.height;
                    maxAnchor.y = rectTransform.anchorMax.y + rectTransform.offsetMax.y / parentTransform.rect.height;
                    break;
                default:
                    Debug.LogError($"Unknown horizontal constraint {nodeConstraints.horizontal}");
                    break;
            }

            rectTransform.anchorMin = minAnchor;
            rectTransform.anchorMax = maxAnchor;

            rectTransform.offsetMin = positionMin - Vector2.Scale(rectTransform.anchorMin, parentSize);
            rectTransform.offsetMax = positionMax - Vector2.Scale(rectTransform.anchorMax, parentSize);
        }
        
        public static Vector3 ConvertVector(RectTransform parent, Vector3 anchoredPosition)
        {
            if (parent == null || Mathf.Approximately(parent.rect.width, 0f) || Mathf.Approximately(parent.rect.height, 0f))
            {
                return anchoredPosition;
            }

            Vector3[] corners = new Vector3[4];
            parent.GetWorldCorners(corners);
            var deltaX = corners[3] - corners[0];
            var deltaY = corners[3] - corners[2];
            var posX = anchoredPosition.x * deltaX / parent.rect.width;
            var posY = anchoredPosition.y * deltaY / parent.rect.height;
            return posX + posY + corners[1];
        }
        
        public static void SetPosition(
            RectTransform parent,
            RectTransform rectTransform,
            Node node,
            AbsoluteBoundingBox boundingBox,
            FigmaImporter importer,
            Vector2 offset,
            bool applyPosition = true,
            Node parentNode = null)
        {
            if (rectTransform == null || importer == null)
            {
                return;
            }

            var renderableBounds = ResolveRenderableBounds(node, boundingBox);
            var resolvedSize = ResolveNodeSize(node, renderableBounds);
            var resolvedRotation = ResolveNodeRotation(node);
            var localTopLeft = ResolveLocalTopLeft(node, renderableBounds, offset, parentNode);
            var hasRotation = Mathf.Abs(resolvedRotation) > 0.001f;

            rectTransform.anchorMin = Vector2.up;
            rectTransform.anchorMax = Vector2.up;
            rectTransform.pivot = hasRotation ? new Vector2(0.5f, 0.5f) : Vector2.up;

            var width = Mathf.Max(0f, resolvedSize.x * importer.Scale);
            var height = Mathf.Max(0f, resolvedSize.y * importer.Scale);
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            if (applyPosition)
            {
                if (hasRotation && renderableBounds != null)
                {
                    var localCenter = ResolveLocalCenter(node, renderableBounds, localTopLeft);
                    var anchoredCenter = localCenter * importer.Scale;
                    rectTransform.anchoredPosition3D = new Vector3(anchoredCenter.x, -anchoredCenter.y, 0f);
                }
                else
                {
                    var anchoredTopLeft = localTopLeft * importer.Scale;
                    rectTransform.anchoredPosition3D = new Vector3(anchoredTopLeft.x, -anchoredTopLeft.y, 0f);
                }
            }
            else
            {
                rectTransform.anchoredPosition3D = Vector3.zero;
            }
        }

        public static void SetRotation(RectTransform rectTransform, float rotation)
        {
            if (rectTransform == null || float.IsNaN(rotation) || float.IsInfinity(rotation))
            {
                return;
            }

            // Figma angle lives in a Y-down screen space, while Unity UI uses Y-up.
            // Negating keeps visual rotation direction consistent after coordinate conversion.
            rectTransform.localRotation = Quaternion.Euler(0f, 0f, -rotation);
        }

        public static Vector2 ResolveNodeSize(Node node, AbsoluteBoundingBox boundingBox)
        {
            var renderableBounds = ResolveRenderableBounds(node, boundingBox);
            if (node?.size != null)
            {
                var resolvedFromNode = new Vector2(node.size.x, node.size.y);
                if (!IsPositiveFinite(resolvedFromNode.x) || resolvedFromNode.x <= CollapsedAxisEpsilon)
                {
                    resolvedFromNode.x = renderableBounds != null ? renderableBounds.width : resolvedFromNode.x;
                }

                if (!IsPositiveFinite(resolvedFromNode.y) || resolvedFromNode.y <= CollapsedAxisEpsilon)
                {
                    resolvedFromNode.y = renderableBounds != null ? renderableBounds.height : resolvedFromNode.y;
                }

                if (IsPositiveFinite(resolvedFromNode.x) && IsPositiveFinite(resolvedFromNode.y))
                {
                    return resolvedFromNode;
                }
            }

            if (renderableBounds != null &&
                IsPositiveFinite(renderableBounds.width) &&
                IsPositiveFinite(renderableBounds.height))
            {
                var resolvedRotation = ResolveNodeRotation(node);
                if (TryResolveUnrotatedSizeFromBoundingBox(renderableBounds.width, renderableBounds.height, resolvedRotation, out var unrotatedSize))
                {
                    return unrotatedSize;
                }
                return renderableBounds.GetSize();
            }

            return Vector2.zero;
        }

        public static float ResolveNodeRotation(Node node)
        {
            if (node == null)
            {
                return 0f;
            }

            var hasMatrixRotation = TryGetRotationFromRelativeTransform(node.relativeTransform, out var matrixRotationDegrees);
            if (IsFinite(node.rotation) && Mathf.Abs(node.rotation) > 0.001f)
            {
                return NormalizeRotationToDegrees(node, node.rotation, hasMatrixRotation, matrixRotationDegrees);
            }

            return hasMatrixRotation ? matrixRotationDegrees : 0f;
        }
        
        public static void SetParent(RectTransform parentT, RectTransform rectTransform)
        {
            if (rectTransform == null)
            {
                return;
            }

            rectTransform.SetParent(parentT, false);
            rectTransform.localScale = Vector3.one;
        }
        
        public static GameObject TryToFindPreviouslyCreatedObject(GameObject parent, string nodeId)
        {
            string id = $"[{nodeId}]";
            if (parent.name.Contains(id))
                return parent;
            foreach (Transform child in parent.transform)
            {
                if (child.name.Contains(id))
                    return child.gameObject;

                var nested = TryToFindPreviouslyCreatedObject(child.gameObject, nodeId);
                if (nested != null)
                {
                    return nested;
                }
            }
            return null;
        }
        
        public static GameObject InstantiateChild(GameObject nodeGo, string name)
        {
            GameObject go = new GameObject(name);
            go.transform.parent = nodeGo.transform;
            go.transform.localScale = Vector3.one;
            var rTransform = go.AddComponent<RectTransform>();
            rTransform.position = Vector3.zero;
            rTransform.anchorMin = Vector2.zero;
            rTransform.anchorMax = Vector2.one;
            rTransform.offsetMin = rTransform.offsetMax = Vector2.zero;
            return go;
        }

        private static RectTransform FindRectWrapper(GameObject go)
        {
            if (go == null)
            {
                return null;
            }

            var children = go.transform;
            for (var i = 0; i < children.childCount; i++)
            {
                var child = children.GetChild(i);
                if (child == null || !string.Equals(child.name, RectWrapperName, StringComparison.Ordinal))
                {
                    continue;
                }

                return child as RectTransform;
            }

            return null;
        }

        private static Vector2 ResolveLocalTopLeft(
            Node node,
            AbsoluteBoundingBox boundingBox,
            Vector2 offset,
            Node parentNode)
        {
            if (TryGetRelativeTranslation(node?.relativeTransform, out var localFromMatrix))
            {
                var localTopLeft = localFromMatrix;
                if (parentNode == null)
                {
                    // Top-level Figma transforms are page-local, while Unity imports under the selected root.
                    localTopLeft -= offset;
                }

                return localTopLeft + ResolveRenderableOriginOffset(node);
            }

            if (boundingBox == null)
            {
                return Vector2.zero;
            }

            var localTopLeftFromBounding = boundingBox.GetPosition() - offset;

            if (parentNode == null)
            {
                return localTopLeftFromBounding;
            }

            var parentRotation = ResolveNodeRotation(parentNode);
            if (Mathf.Abs(parentRotation) <= 0.001f)
            {
                return localTopLeftFromBounding;
            }

            var parentBounding = parentNode.absoluteBoundingBox;
            if (parentBounding == null)
            {
                return localTopLeftFromBounding;
            }

            var parentCenterAbs = parentBounding.GetPosition() + (parentBounding.GetSize() * 0.5f);
            Vector2 parentOriginUnrotated;
            if (!TryEstimateUnrotatedBoundsFromChildren(parentNode, parentBounding, parentRotation, out parentOriginUnrotated, out var estimatedParentSize) ||
                estimatedParentSize.x <= 0.001f ||
                estimatedParentSize.y <= 0.001f)
            {
                var parentSize = ResolveNodeSize(parentNode, parentBounding);
                if (parentSize.x <= 0.001f || parentSize.y <= 0.001f)
                {
                    parentSize = parentBounding.GetSize();
                }

                // In the rotated parent space, rebuild an approximate unrotated local origin.
                parentOriginUnrotated = parentCenterAbs - (parentSize * 0.5f);
            }

            var childSize = ResolveNodeSize(node, boundingBox);
            if (childSize.x <= 0.001f || childSize.y <= 0.001f)
            {
                childSize = boundingBox.GetSize();
            }

            var childCenterAbs = boundingBox.GetPosition() + (boundingBox.GetSize() * 0.5f);
            var childCenterUnrotated = RotatePointAround(childCenterAbs, parentCenterAbs, -parentRotation);
            var localCenter = childCenterUnrotated - parentOriginUnrotated;
            return localCenter - (childSize * 0.5f);
        }

        private static AbsoluteBoundingBox ResolveRenderableBounds(Node node, AbsoluteBoundingBox boundingBox)
        {
            var baseBounds = boundingBox ?? node?.absoluteBoundingBox;
            if (baseBounds == null)
            {
                return node?.absoluteRenderBounds;
            }

            var renderBounds = node?.absoluteRenderBounds;
            if (renderBounds != null &&
                IsPositiveFinite(renderBounds.width) &&
                IsPositiveFinite(renderBounds.height) &&
                ((baseBounds.width <= CollapsedAxisEpsilon && renderBounds.width > CollapsedAxisEpsilon) ||
                 (baseBounds.height <= CollapsedAxisEpsilon && renderBounds.height > CollapsedAxisEpsilon)))
            {
                return renderBounds;
            }

            return ExpandCollapsedStrokeBounds(node, baseBounds) ?? baseBounds;
        }

        private static AbsoluteBoundingBox ExpandCollapsedStrokeBounds(Node node, AbsoluteBoundingBox baseBounds)
        {
            if (node == null || baseBounds == null || !HasVisibleStroke(node))
            {
                return null;
            }

            var expandWidth = !IsPositiveFinite(baseBounds.width) || baseBounds.width <= CollapsedAxisEpsilon;
            var expandHeight = !IsPositiveFinite(baseBounds.height) || baseBounds.height <= CollapsedAxisEpsilon;
            if (!expandWidth && !expandHeight)
            {
                return null;
            }

            var strokeExtent = Mathf.Max(node.strokeWeight, 1f);
            var strokeOffset = ResolveStrokeOriginOffset(node.strokeAlign, strokeExtent);
            return new AbsoluteBoundingBox
            {
                x = expandWidth ? baseBounds.x - strokeOffset : baseBounds.x,
                y = expandHeight ? baseBounds.y - strokeOffset : baseBounds.y,
                width = expandWidth ? strokeExtent : baseBounds.width,
                height = expandHeight ? strokeExtent : baseBounds.height
            };
        }

        private static Vector2 ResolveRenderableOriginOffset(Node node)
        {
            var baseBounds = node?.absoluteBoundingBox;
            if (baseBounds == null)
            {
                return Vector2.zero;
            }

            var renderableBounds = ResolveRenderableBounds(node, baseBounds);
            if (renderableBounds == null)
            {
                return Vector2.zero;
            }

            return renderableBounds.GetPosition() - baseBounds.GetPosition();
        }

        private static bool HasVisibleStroke(Node node)
        {
            return node?.strokes != null &&
                   node.strokes.Length > 0 &&
                   IsPositiveFinite(node.strokeWeight) &&
                   node.strokeWeight > CollapsedAxisEpsilon;
        }

        private static float ResolveStrokeOriginOffset(string strokeAlign, float strokeExtent)
        {
            if (!IsPositiveFinite(strokeExtent))
            {
                return 0f;
            }

            if (string.Equals(strokeAlign, "OUTSIDE", StringComparison.OrdinalIgnoreCase))
            {
                return strokeExtent;
            }

            if (string.Equals(strokeAlign, "INSIDE", StringComparison.OrdinalIgnoreCase))
            {
                return 0f;
            }

            return strokeExtent * 0.5f;
        }

        private static Vector2 ResolveLocalCenter(Node node, AbsoluteBoundingBox boundingBox, Vector2 localTopLeft)
        {
            if (boundingBox != null)
            {
                var resolvedSize = ResolveNodeSize(node, boundingBox);
                if (resolvedSize.x > 0.001f && resolvedSize.y > 0.001f)
                {
                    return localTopLeft + (resolvedSize * 0.5f);
                }

                return localTopLeft + (boundingBox.GetSize() * 0.5f);
            }

            return localTopLeft;
        }

        private static Vector2 RotateVector(Vector2 value, float angleDegrees)
        {
            var radians = angleDegrees * Mathf.Deg2Rad;
            var cos = Mathf.Cos(radians);
            var sin = Mathf.Sin(radians);
            return new Vector2(
                value.x * cos - value.y * sin,
                value.x * sin + value.y * cos);
        }

        private static Vector2 RotatePointAround(Vector2 point, Vector2 pivot, float angleDegrees)
        {
            var delta = point - pivot;
            return pivot + RotateVector(delta, angleDegrees);
        }

        private static bool TryEstimateUnrotatedBoundsFromChildren(
            Node parentNode,
            AbsoluteBoundingBox parentBounding,
            float parentRotationDegrees,
            out Vector2 origin,
            out Vector2 size)
        {
            origin = Vector2.zero;
            size = Vector2.zero;

            if (parentNode?.children == null || parentNode.children.Length == 0 || parentBounding == null)
            {
                return false;
            }

            var parentCenter = parentBounding.GetPosition() + (parentBounding.GetSize() * 0.5f);
            var hasAnyPoint = false;
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

            for (var i = 0; i < parentNode.children.Length; i++)
            {
                var child = parentNode.children[i];
                var childBounding = child?.absoluteBoundingBox;
                if (childBounding == null)
                {
                    continue;
                }

                var corners = GetBoundingBoxCorners(childBounding);
                for (var c = 0; c < corners.Count; c++)
                {
                    var unrotated = RotatePointAround(corners[c], parentCenter, -parentRotationDegrees);
                    min.x = Mathf.Min(min.x, unrotated.x);
                    min.y = Mathf.Min(min.y, unrotated.y);
                    max.x = Mathf.Max(max.x, unrotated.x);
                    max.y = Mathf.Max(max.y, unrotated.y);
                    hasAnyPoint = true;
                }
            }

            if (!hasAnyPoint)
            {
                return false;
            }

            var resolvedSize = max - min;
            if (resolvedSize.x <= 0.001f || resolvedSize.y <= 0.001f)
            {
                return false;
            }

            origin = min;
            size = resolvedSize;
            return true;
        }

        private static List<Vector2> GetBoundingBoxCorners(AbsoluteBoundingBox boundingBox)
        {
            var xMin = boundingBox.x;
            var yMin = boundingBox.y;
            var xMax = boundingBox.x + boundingBox.width;
            var yMax = boundingBox.y + boundingBox.height;
            return new List<Vector2>(4)
            {
                new Vector2(xMin, yMin),
                new Vector2(xMax, yMin),
                new Vector2(xMax, yMax),
                new Vector2(xMin, yMax)
            };
        }

        private static float NormalizeRotationToDegrees(
            Node node,
            float rawRotation,
            bool hasMatrixRotation,
            float matrixRotationDegrees)
        {
            var absRaw = Mathf.Abs(rawRotation);
            if (absRaw <= 0.0001f)
            {
                return 0f;
            }

            var degreeCandidate = rawRotation;
            var radianCandidate = rawRotation * Mathf.Rad2Deg;

            if (hasMatrixRotation)
            {
                return ChooseClosestAngleCandidate(degreeCandidate, radianCandidate, matrixRotationDegrees);
            }

            if (absRaw > Mathf.PI + 0.0001f)
            {
                return degreeCandidate;
            }

            if (TryChooseAngleCandidateFromNodeSize(node, degreeCandidate, radianCandidate, out var chosenBySize))
            {
                return chosenBySize;
            }

            if (TryChooseAngleCandidateFromChildren(node, degreeCandidate, radianCandidate, out var chosenByChildren))
            {
                return chosenByChildren;
            }

            // Ambiguous inputs below PI commonly come from radian payloads when relativeTransform is absent.
            // Prioritize radians for container nodes, keep tiny leaf rotations conservative.
            if (node?.children != null && node.children.Length > 0)
            {
                return radianCandidate;
            }

            return degreeCandidate;
        }

        private static bool TryChooseAngleCandidateFromNodeSize(
            Node node,
            float degreeCandidate,
            float radianCandidate,
            out float chosenRotation)
        {
            chosenRotation = degreeCandidate;
            if (node?.size == null || node.absoluteBoundingBox == null)
            {
                return false;
            }

            var size = node.size.ToVector2();
            if (size.x <= 0.001f || size.y <= 0.001f)
            {
                return false;
            }

            var targetWidth = node.absoluteBoundingBox.width;
            var targetHeight = node.absoluteBoundingBox.height;
            if (!IsPositiveFinite(targetWidth) || !IsPositiveFinite(targetHeight))
            {
                return false;
            }

            var degreeScore = EvaluateRotatedBoundsError(size, targetWidth, targetHeight, degreeCandidate);
            var radianScore = EvaluateRotatedBoundsError(size, targetWidth, targetHeight, radianCandidate);
            if (!IsFinite(degreeScore) || !IsFinite(radianScore))
            {
                return false;
            }

            chosenRotation = radianScore + 0.01f < degreeScore ? radianCandidate : degreeCandidate;
            return true;
        }

        private static bool TryChooseAngleCandidateFromChildren(
            Node node,
            float degreeCandidate,
            float radianCandidate,
            out float chosenRotation)
        {
            chosenRotation = degreeCandidate;
            if (node?.children == null || node.children.Length == 0 || node.absoluteBoundingBox == null)
            {
                return false;
            }

            var degreeScore = EvaluateChildBoundsFitScore(node, degreeCandidate);
            var radianScore = EvaluateChildBoundsFitScore(node, radianCandidate);
            if (!IsFinite(degreeScore) || !IsFinite(radianScore))
            {
                return false;
            }

            chosenRotation = radianScore + 0.01f < degreeScore ? radianCandidate : degreeCandidate;
            return true;
        }

        private static float EvaluateRotatedBoundsError(
            Vector2 unrotatedSize,
            float targetWidth,
            float targetHeight,
            float rotationDegrees)
        {
            var radians = Mathf.Abs(rotationDegrees) * Mathf.Deg2Rad;
            var cos = Mathf.Abs(Mathf.Cos(radians));
            var sin = Mathf.Abs(Mathf.Sin(radians));
            var predictedWidth = (unrotatedSize.x * cos) + (unrotatedSize.y * sin);
            var predictedHeight = (unrotatedSize.x * sin) + (unrotatedSize.y * cos);
            return Mathf.Abs(predictedWidth - targetWidth) + Mathf.Abs(predictedHeight - targetHeight);
        }

        private static float EvaluateChildBoundsFitScore(Node parentNode, float rotationDegrees)
        {
            var parentBounding = parentNode.absoluteBoundingBox;
            if (parentBounding == null)
            {
                return float.PositiveInfinity;
            }

            var parentCenter = parentBounding.GetPosition() + (parentBounding.GetSize() * 0.5f);
            var hasPoint = false;
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

            for (var i = 0; i < parentNode.children.Length; i++)
            {
                var childBounding = parentNode.children[i]?.absoluteBoundingBox;
                if (childBounding == null)
                {
                    continue;
                }

                var corners = GetBoundingBoxCorners(childBounding);
                for (var c = 0; c < corners.Count; c++)
                {
                    var unrotated = RotatePointAround(corners[c], parentCenter, -rotationDegrees);
                    min.x = Mathf.Min(min.x, unrotated.x);
                    min.y = Mathf.Min(min.y, unrotated.y);
                    max.x = Mathf.Max(max.x, unrotated.x);
                    max.y = Mathf.Max(max.y, unrotated.y);
                    hasPoint = true;
                }
            }

            if (!hasPoint)
            {
                return float.PositiveInfinity;
            }

            var localCorners = new[]
            {
                new Vector2(min.x, min.y),
                new Vector2(max.x, min.y),
                new Vector2(max.x, max.y),
                new Vector2(min.x, max.y)
            };

            var predictedMin = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var predictedMax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (var i = 0; i < localCorners.Length; i++)
            {
                var world = RotatePointAround(localCorners[i], parentCenter, rotationDegrees);
                predictedMin.x = Mathf.Min(predictedMin.x, world.x);
                predictedMin.y = Mathf.Min(predictedMin.y, world.y);
                predictedMax.x = Mathf.Max(predictedMax.x, world.x);
                predictedMax.y = Mathf.Max(predictedMax.y, world.y);
            }

            var predictedWidth = predictedMax.x - predictedMin.x;
            var predictedHeight = predictedMax.y - predictedMin.y;
            var targetWidth = parentBounding.width;
            var targetHeight = parentBounding.height;
            var predictedCenter = (predictedMin + predictedMax) * 0.5f;
            var targetCenter = parentCenter;
            var centerError = Vector2.Distance(predictedCenter, targetCenter);

            return Mathf.Abs(predictedWidth - targetWidth) +
                   Mathf.Abs(predictedHeight - targetHeight) +
                   (centerError * 0.25f);
        }

        private static float ChooseClosestAngleCandidate(float degreeCandidate, float radianCandidate, float targetDegrees)
        {
            var degreeError = Mathf.Abs(Mathf.DeltaAngle(targetDegrees, degreeCandidate));
            var radianError = Mathf.Abs(Mathf.DeltaAngle(targetDegrees, radianCandidate));
            return radianError + 0.01f < degreeError ? radianCandidate : degreeCandidate;
        }

        private static bool TryGetRelativeTranslation(float[][] relativeTransform, out Vector2 translation)
        {
            translation = Vector2.zero;
            if (!TryGetMatrixEntry(relativeTransform, 0, 2, out var x) ||
                !TryGetMatrixEntry(relativeTransform, 1, 2, out var y))
            {
                return false;
            }

            if (!IsFinite(x) || !IsFinite(y))
            {
                return false;
            }

            translation = new Vector2(x, y);
            return true;
        }

        private static bool TryGetRotationFromRelativeTransform(float[][] relativeTransform, out float rotation)
        {
            rotation = 0f;
            if (!TryGetMatrixEntry(relativeTransform, 0, 0, out var m00) ||
                !TryGetMatrixEntry(relativeTransform, 1, 0, out var m10))
            {
                return false;
            }

            if (!IsFinite(m00) || !IsFinite(m10))
            {
                return false;
            }

            var magnitude = Mathf.Sqrt((m00 * m00) + (m10 * m10));
            if (magnitude <= 0.0001f)
            {
                return false;
            }

            rotation = Mathf.Atan2(m10, m00) * Mathf.Rad2Deg;
            return IsFinite(rotation);
        }

        private static bool TryGetMatrixEntry(float[][] matrix, int row, int column, out float value)
        {
            value = 0f;
            if (matrix == null || row < 0 || row >= matrix.Length)
            {
                return false;
            }

            var rowData = matrix[row];
            if (rowData == null || column < 0 || column >= rowData.Length)
            {
                return false;
            }

            value = rowData[column];
            return true;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsPositiveFinite(float value)
        {
            return IsFinite(value) && value > 0f;
        }

        private static bool TryResolveUnrotatedSizeFromBoundingBox(
            float boundingWidth,
            float boundingHeight,
            float rotationDegrees,
            out Vector2 size)
        {
            size = Vector2.zero;
            if (!IsPositiveFinite(boundingWidth) || !IsPositiveFinite(boundingHeight))
            {
                return false;
            }

            var angle = Mathf.Abs(rotationDegrees) * Mathf.Deg2Rad;
            var cos = Mathf.Abs(Mathf.Cos(angle));
            var sin = Mathf.Abs(Mathf.Sin(angle));
            var determinant = (cos * cos) - (sin * sin);
            if (Mathf.Abs(determinant) <= 0.0001f)
            {
                return false;
            }

            var width = ((boundingWidth * cos) - (boundingHeight * sin)) / determinant;
            var height = ((boundingHeight * cos) - (boundingWidth * sin)) / determinant;
            if (!IsPositiveFinite(width) || !IsPositiveFinite(height))
            {
                return false;
            }

            size = new Vector2(width, height);
            return true;
        }
    }
}

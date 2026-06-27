using UnityEngine;

namespace FigmaImporter.Editor
{
    internal enum FigmaMaskFlowDirection
    {
        Forward,
        Backward
    }

    internal static class FigmaMaskingUtils
    {
        public static bool IsMaskNode(Node node)
        {
            return node != null && node.isMask;
        }

        public static bool HasDirectMaskChildren(Node node)
        {
            if (node?.children == null || node.children.Length == 0)
            {
                return false;
            }

            for (var i = 0; i < node.children.Length; i++)
            {
                if (IsMaskNode(node.children[i]))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool ContainsMaskInSubtree(Node node)
        {
            if (node == null)
            {
                return false;
            }

            if (IsMaskNode(node))
            {
                return true;
            }

            if (node.children == null)
            {
                return false;
            }

            for (var i = 0; i < node.children.Length; i++)
            {
                if (ContainsMaskInSubtree(node.children[i]))
                {
                    return true;
                }
            }

            return false;
        }

        public static FigmaMaskFlowDirection ResolveMaskFlowDirection(Node parentNode)
        {
            if (parentNode?.children == null || parentNode.children.Length <= 1)
            {
                return FigmaMaskFlowDirection.Forward;
            }

            var hasForwardScope = false;
            var hasBackwardScope = false;
            var previousMaskIndex = -1;
            for (var i = 0; i < parentNode.children.Length; i++)
            {
                if (!IsMaskNode(parentNode.children[i]))
                {
                    continue;
                }

                if (HasForwardMaskScope(parentNode.children, i))
                {
                    hasForwardScope = true;
                }

                if (HasBackwardMaskScope(parentNode.children, previousMaskIndex, i))
                {
                    hasBackwardScope = true;
                }

                previousMaskIndex = i;
            }

            if (hasBackwardScope && !hasForwardScope)
            {
                return FigmaMaskFlowDirection.Backward;
            }

            return FigmaMaskFlowDirection.Forward;
        }

        private static bool HasForwardMaskScope(Node[] children, int maskIndex)
        {
            if (children == null || maskIndex < 0 || maskIndex >= children.Length - 1)
            {
                return false;
            }

            var hasScope = false;
            for (var i = maskIndex + 1; i < children.Length; i++)
            {
                if (IsMaskNode(children[i]))
                {
                    break;
                }

                hasScope = true;
            }

            return hasScope;
        }

        private static bool HasBackwardMaskScope(Node[] children, int previousMaskIndex, int maskIndex)
        {
            if (children == null || maskIndex <= 0)
            {
                return false;
            }

            var start = Mathf.Clamp(previousMaskIndex + 1, 0, maskIndex);
            var hasScope = false;
            for (var i = maskIndex - 1; i >= start; i--)
            {
                if (IsMaskNode(children[i]))
                {
                    break;
                }

                hasScope = true;
            }

            return hasScope;
        }
    }
}

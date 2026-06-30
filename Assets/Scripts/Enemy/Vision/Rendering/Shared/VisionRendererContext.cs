using UnityEngine;

namespace DuoCurtain.Vision
{
    public readonly struct VisionRendererContext
    {
        public readonly GameObject owner;
        public readonly Transform parent;
        public readonly int sortingLayerId;
        public readonly int sortingOrder;
        public readonly float zOffset;

        public VisionRendererContext(
            GameObject ownerObject,
            Transform rendererParent,
            int rendererSortingLayerId,
            int rendererSortingOrder,
            float rendererZOffset)
        {
            owner = ownerObject;
            parent = rendererParent;
            sortingLayerId = rendererSortingLayerId;
            sortingOrder = rendererSortingOrder;
            zOffset = rendererZOffset;
        }
    }
}

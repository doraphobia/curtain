using UnityEngine;

namespace DuoCurtain.Vision
{
    public enum VisibilitySegmentType
    {
        Wall,
        ClosedDoor,
        OpenDoor,
        ClosedWindow,
        OpenWindow,
        Portal,
        Unknown
    }

    public struct VisibilitySegment
    {
        public Vector2 a;
        public Vector2 b;
        public VisibilitySegmentType type;
        public GameObject sourceObject;
        public Component sourceComponent;
        public int sourceId;

        public bool BlocksVision => VisibilityWorld.IsBlockingType(type);
        public bool BlocksMovement => VisibilityWorld.IsMovementBlockingType(type);
        public bool IsPortal => VisibilityWorld.IsPortalType(type);

        public VisibilitySegment(
            Vector2 start,
            Vector2 end,
            VisibilitySegmentType segmentType,
            GameObject ownerObject,
            Component ownerComponent)
        {
            a = start;
            b = end;
            type = segmentType;
            sourceObject = ownerObject;
            sourceComponent = ownerComponent;
            sourceId = ownerObject != null ? ownerObject.GetInstanceID() : 0;
        }
    }
}

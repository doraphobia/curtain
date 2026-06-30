using UnityEngine;

namespace DuoCurtain.Vision
{
    public readonly struct VisionPortalExit
    {
        public readonly Vector2 origin;
        public readonly Vector2 forward;
        public readonly float maxDistance;
        public readonly int targetRoomId;

        public VisionPortalExit(Vector2 exitOrigin, Vector2 exitForward, float distance, int roomId)
        {
            origin = exitOrigin;
            forward = exitForward.sqrMagnitude > 0.000001f ? exitForward.normalized : Vector2.up;
            maxDistance = Mathf.Max(0.0001f, distance);
            targetRoomId = roomId;
        }
    }

    public interface IVisionPortal
    {
        bool IsPortalOpen { get; }
        Vector2 PortalA { get; }
        Vector2 PortalB { get; }
        Vector2 ForwardNormal { get; }
        Vector2 BackwardNormal { get; }
        int FrontRoomId { get; }
        int BackRoomId { get; }

        bool CanPassVision(Vector2 incomingOrigin, Vector2 incomingDirection);
        VisionPortalExit GetExit(Vector2 incomingOrigin, Vector2 incomingDirection);
    }
}

using UnityEngine;

namespace DuoCurtain.Vision
{
    /// <summary>
    /// Reserved contract for later window, glass, and linked-portal propagation.
    /// The first renderer does not recurse through portals.
    /// </summary>
    public interface IVisionPortal
    {
        bool IsVisionPortalOpen { get; }
        Collider2D EntryCollider { get; }
        bool TryTransformRay(Vector2 entryPoint, Vector2 incomingDirection, out Vector2 exitPoint, out Vector2 exitDirection);
    }
}

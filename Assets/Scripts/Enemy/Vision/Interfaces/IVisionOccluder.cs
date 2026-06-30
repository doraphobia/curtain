using UnityEngine;

namespace DuoCurtain.Vision
{
    public interface IVisionOccluder
    {
        bool BlocksVision { get; }
        Collider2D VisionCollider { get; }
    }
}

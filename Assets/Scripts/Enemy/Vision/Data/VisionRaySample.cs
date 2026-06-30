using System;
using UnityEngine;

namespace DuoCurtain.Vision
{
    [Serializable]
    public struct VisionRaySample
    {
        public float angleDegrees;
        public float normalizedAngle;
        public Vector2 direction;
        public Vector2 point;
        public Vector2 hitNormal;
        public float distance;
        public float normalizedDistance;
        public bool hit;
        public int colliderInstanceId;
    }
}

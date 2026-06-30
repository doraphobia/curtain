using System;
using UnityEngine;

namespace DuoCurtain.Vision
{
    public enum VisionUVMode
    {
        Polar,
        LocalBounds,
        WorldSpace
    }

    public enum VisionMaskMode
    {
        None,
        PolygonAlpha,
        DistanceFade
    }

    [Serializable]
    public sealed class VisionRenderParameters
    {
        public Color primaryColor = new Color(1f, 0.82f, 0.12f, 0.18f);
        public Color secondaryColor = new Color(1f, 0.2f, 0.08f, 0.42f);
        [Range(0f, 1f)]
        public float opacity = 1f;
        [Min(0f)]
        public float edgeWidth = 0.04f;
        public Vector2 gradientDirection = Vector2.up;
        [Min(0f)]
        public float animationSpeed = 1f;
        [Range(0f, 1f)]
        public float noiseAmount;
        public VisionUVMode uvMode = VisionUVMode.Polar;
        public VisionMaskMode maskMode = VisionMaskMode.DistanceFade;
    }
}

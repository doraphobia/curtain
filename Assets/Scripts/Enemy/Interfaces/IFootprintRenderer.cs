using UnityEngine;

/// <summary>
/// Abstraction for footprint rendering. Current implementation uses prefabs;
/// future shader/material renderers can implement this interface.
/// </summary>
public interface IFootprintRenderer
{
    FootprintInstance SpawnFootprint(FootprintSpawnData data);
}

public struct FootprintSpawnData
{
    public Vector3 position;
    public Quaternion rotation;
    public FootprintSide side;
    public bool isLatest;
    public Color color;
    public float alpha;
    public Transform parent;
    public object surfaceContext;
}

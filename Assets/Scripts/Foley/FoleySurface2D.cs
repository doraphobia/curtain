using UnityEngine;

[DisallowMultipleComponent]
public class FoleySurface2D : MonoBehaviour
{
    public string surfaceId = "Default";

    [Header("Mix Hints")]
    [Range(0f, 1f)]
    public float wetness = 0f;
    public bool resetNuisanceWhenEntered = true;
}

using UnityEngine;

[DisallowMultipleComponent]
public class UIRotate : MonoBehaviour
{
    public float degreesPerSecond = 90f;

    void Update()
    {
        transform.Rotate(0f, 0f, degreesPerSecond * Time.deltaTime);
    }
}

using UnityEngine;

[DisallowMultipleComponent]
public class WindowEventAnchor : MonoBehaviour
{
    public Transform panelAnchor;

    public Transform GetAnchor()
    {
        return panelAnchor != null ? panelAnchor : transform;
    }
}

using UnityEngine;

public class CursorHoverLog : MonoBehaviour
{
    public string message = "Cursor is over this object";

    void OnMouseEnter()
    {
        Debug.Log(message);
    }

    void OnMouseOver()
    {
        Debug.Log(message);
    }

    void OnMouseExit()
    {
        Debug.Log("Cursor left the object");
    }
}
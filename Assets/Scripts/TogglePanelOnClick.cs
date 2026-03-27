using UnityEngine;

[DisallowMultipleComponent]
public class TogglePanelOnClick : MonoBehaviour
{
    [Header("Target")]
    public GameObject targetPanel;

    public void TogglePanel()
    {
        if (targetPanel == null)
            return;

        targetPanel.SetActive(!targetPanel.activeSelf);
    }

    public void ShowPanel()
    {
        if (targetPanel == null)
            return;

        targetPanel.SetActive(true);
    }

    public void HidePanel()
    {
        if (targetPanel == null)
            return;

        targetPanel.SetActive(false);
    }
}

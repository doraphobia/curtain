using UnityEngine;

[DisallowMultipleComponent]
public class ActivateButtonOnPurchase : MonoBehaviour
{
    [Header("Target")]
    public GameObject targetButtonObject;

    public void ActivateTargetButton()
    {
        if (targetButtonObject == null)
            return;

        targetButtonObject.SetActive(true);
    }
}

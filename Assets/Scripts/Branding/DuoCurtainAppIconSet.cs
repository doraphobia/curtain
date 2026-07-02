using UnityEngine;

[CreateAssetMenu(menuName = "Duo Curtain/Branding/App Icon Set", fileName = "DuoCurtainAppIconSet")]
public sealed class DuoCurtainAppIconSet : ScriptableObject
{
    [Header("Standalone (Mac + Windows)")]
    public Texture2D standaloneIcon;

    [Header("WebGL")]
    public Texture2D webglIcon;
}


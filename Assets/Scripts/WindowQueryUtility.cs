using System.Collections.Generic;
using UnityEngine;

public static class WindowQueryUtility
{
    public static HoverScrollColorLerp2D[] RefreshWindowList(
        HoverScrollColorLerp2D[] currentWindows,
        bool useWindowTag,
        string windowTag,
        bool autoFindWindows)
    {
        if (useWindowTag && !string.IsNullOrWhiteSpace(windowTag))
            return FindTaggedWindows(windowTag);

        if (autoFindWindows)
            return Object.FindObjectsByType<HoverScrollColorLerp2D>(FindObjectsSortMode.None);

        return currentWindows ?? System.Array.Empty<HoverScrollColorLerp2D>();
    }

    public static HoverScrollColorLerp2D[] FindAllWindows()
    {
        return Object.FindObjectsByType<HoverScrollColorLerp2D>(FindObjectsSortMode.None);
    }

    private static HoverScrollColorLerp2D[] FindTaggedWindows(string windowTag)
    {
        GameObject[] taggedObjects = GameObject.FindGameObjectsWithTag(windowTag);
        List<HoverScrollColorLerp2D> taggedWindows = new List<HoverScrollColorLerp2D>();

        for (int i = 0; i < taggedObjects.Length; i++)
        {
            HoverScrollColorLerp2D hover = taggedObjects[i].GetComponent<HoverScrollColorLerp2D>();
            if (hover != null)
                taggedWindows.Add(hover);
        }

        return taggedWindows.ToArray();
    }
}

using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(FoleyProfile))]
public class FoleyProfileEditor : Editor
{
    private static MethodInfo playPreviewClipMethod;
    private static MethodInfo stopAllPreviewClipsMethod;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        FoleyProfile profile = target as FoleyProfile;
        if (profile == null)
            return;

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(!profile.HasAnyClips()))
            {
                if (GUILayout.Button("Preview"))
                    PreviewProfile(profile);
            }

            if (GUILayout.Button("Stop Preview"))
                StopPreview();
        }
    }

    private static void PreviewProfile(FoleyProfile profile)
    {
        AudioClip clip = GetFirstClip(profile);
        if (clip == null)
            return;

        StopPreview();
        PlayPreviewClip(clip);
    }

    private static AudioClip GetFirstClip(FoleyProfile profile)
    {
        if (profile == null)
            return null;

        FoleyProfile.SurfaceBank bank = profile.GetBank(profile.defaultSurfaceId);
        if (bank == null || bank.layers == null)
            return null;

        for (int layerIndex = 0; layerIndex < bank.layers.Count; layerIndex++)
        {
            FoleyProfile.FoleyLayer layer = bank.layers[layerIndex];
            if (layer == null || layer.clips == null)
                continue;

            for (int clipIndex = 0; clipIndex < layer.clips.Length; clipIndex++)
            {
                if (layer.clips[clipIndex] != null)
                    return layer.clips[clipIndex];
            }
        }

        return null;
    }

    private static void PlayPreviewClip(AudioClip clip)
    {
        MethodInfo method = GetPlayPreviewClipMethod();
        if (method == null)
        {
            Debug.LogWarning("[FoleyProfileEditor] Unity AudioUtil preview method was not found.");
            return;
        }

        ParameterInfo[] parameters = method.GetParameters();
        object[] args;
        if (parameters.Length == 3)
            args = new object[] { clip, 0, false };
        else if (parameters.Length == 2)
            args = new object[] { clip, 0 };
        else
            args = new object[] { clip };

        try
        {
            method.Invoke(null, args);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[FoleyProfileEditor] Audio preview failed: " + exception.Message);
        }
    }

    private static void StopPreview()
    {
        MethodInfo method = GetStopAllPreviewClipsMethod();
        if (method == null)
            return;

        try
        {
            method.Invoke(null, null);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[FoleyProfileEditor] Stop preview failed: " + exception.Message);
        }
    }

    private static MethodInfo GetPlayPreviewClipMethod()
    {
        if (playPreviewClipMethod != null)
            return playPreviewClipMethod;

        Type audioUtilType = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
        if (audioUtilType == null)
            return null;

        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        playPreviewClipMethod =
            audioUtilType.GetMethod("PlayPreviewClip", flags, null, new Type[] { typeof(AudioClip), typeof(int), typeof(bool) }, null) ??
            audioUtilType.GetMethod("PlayPreviewClip", flags, null, new Type[] { typeof(AudioClip), typeof(int) }, null) ??
            audioUtilType.GetMethod("PlayClip", flags, null, new Type[] { typeof(AudioClip), typeof(int), typeof(bool) }, null) ??
            audioUtilType.GetMethod("PlayClip", flags, null, new Type[] { typeof(AudioClip) }, null);

        return playPreviewClipMethod;
    }

    private static MethodInfo GetStopAllPreviewClipsMethod()
    {
        if (stopAllPreviewClipsMethod != null)
            return stopAllPreviewClipsMethod;

        Type audioUtilType = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
        if (audioUtilType == null)
            return null;

        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        stopAllPreviewClipsMethod =
            audioUtilType.GetMethod("StopAllPreviewClips", flags) ??
            audioUtilType.GetMethod("StopAllClips", flags);

        return stopAllPreviewClipsMethod;
    }
}

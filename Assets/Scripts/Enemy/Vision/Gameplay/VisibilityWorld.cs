using System.Collections.Generic;
using UnityEngine;

namespace DuoCurtain.Vision
{
    [DisallowMultipleComponent]
    public sealed class VisibilityWorld : MonoBehaviour
    {
        private static VisibilityWorld instance;

        [Header("Rebuild")]
        public bool autoFindSourcesOnRebuild = true;
        public bool rebuildWhenDirty = true;

        [Header("Debug")]
        public bool logRebuilds;
        public bool logSources;

        private readonly List<IVisibilitySegmentSource> sources =
            new List<IVisibilitySegmentSource>();
        private readonly List<VisibilitySegment> segments =
            new List<VisibilitySegment>(256);
        private readonly List<VisibilitySegment> sourceBuffer =
            new List<VisibilitySegment>(64);
        private bool dirty = true;

        public static VisibilityWorld Instance => instance;
        public IReadOnlyList<VisibilitySegment> Segments => segments;
        public bool IsDirty => dirty;

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Debug.LogWarning(
                    "[VisibilityWorld] Multiple visibility worlds exist. Keeping the first one and disabling this duplicate.",
                    this);
                enabled = false;
                return;
            }

            instance = this;
            MarkDirty();
        }

        void OnEnable()
        {
            if (instance == null)
                instance = this;
            MarkDirty();
        }

        void OnDisable()
        {
            if (instance == this)
                instance = null;
        }

        public static VisibilityWorld GetOrCreate()
        {
            if (instance != null)
                return instance;

            instance = FindFirstObjectByType<VisibilityWorld>();
            if (instance != null)
                return instance;

            GameObject worldObject = new GameObject("Visibility World");
            if (!Application.isPlaying)
                worldObject.hideFlags = HideFlags.HideAndDontSave;
            instance = worldObject.AddComponent<VisibilityWorld>();
            return instance;
        }

        public static void MarkActiveWorldDirty()
        {
            if (instance != null)
                instance.MarkDirty();
        }

        public static bool IsBlockingType(VisibilitySegmentType type)
        {
            switch (type)
            {
                case VisibilitySegmentType.OpenDoor:
                    return false;
                case VisibilitySegmentType.Wall:
                case VisibilitySegmentType.ClosedDoor:
                case VisibilitySegmentType.ClosedWindow:
                case VisibilitySegmentType.OpenWindow:
                case VisibilitySegmentType.Portal:
                case VisibilitySegmentType.Unknown:
                default:
                    return true;
            }
        }

        public static bool IsMovementBlockingType(VisibilitySegmentType type)
        {
            switch (type)
            {
                case VisibilitySegmentType.OpenDoor:
                    return false;
                case VisibilitySegmentType.Wall:
                case VisibilitySegmentType.ClosedDoor:
                case VisibilitySegmentType.ClosedWindow:
                case VisibilitySegmentType.OpenWindow:
                case VisibilitySegmentType.Portal:
                case VisibilitySegmentType.Unknown:
                default:
                    return true;
            }
        }

        public static bool IsPortalType(VisibilitySegmentType type)
        {
            return type == VisibilitySegmentType.OpenWindow ||
                   type == VisibilitySegmentType.Portal;
        }

        public void RegisterSource(IVisibilitySegmentSource source)
        {
            if (source == null || sources.Contains(source))
                return;

            sources.Add(source);
            MarkDirty();
        }

        public void UnregisterSource(IVisibilitySegmentSource source)
        {
            if (source == null)
                return;

            if (sources.Remove(source))
                MarkDirty();
        }

        public void MarkDirty()
        {
            dirty = true;
        }

        public void Clear()
        {
            segments.Clear();
            dirty = false;
        }

        public void RebuildIfDirty()
        {
            if (!dirty || !rebuildWhenDirty)
                return;

            Rebuild();
        }

        public void Rebuild()
        {
            if (autoFindSourcesOnRebuild)
                RefreshSceneSources();

            segments.Clear();
            int wallCount = 0;
            int doorCount = 0;
            int windowCount = 0;
            int unknownCount = 0;

            for (int i = sources.Count - 1; i >= 0; i--)
            {
                IVisibilitySegmentSource source = sources[i];
                if (!IsUsableSource(source))
                {
                    sources.RemoveAt(i);
                    continue;
                }

                sourceBuffer.Clear();
                source.CollectVisibilitySegments(sourceBuffer);
                if (logSources)
                    Debug.Log("[VisibilityWorld] Source " + GetSourceName(source) + " segments=" + sourceBuffer.Count);

                for (int j = 0; j < sourceBuffer.Count; j++)
                {
                    VisibilitySegment segment = sourceBuffer[j];
                    if ((segment.b - segment.a).sqrMagnitude <= 0.0000001f)
                        continue;

                    segments.Add(segment);
                    switch (segment.type)
                    {
                        case VisibilitySegmentType.Wall:
                            wallCount++;
                            break;
                        case VisibilitySegmentType.ClosedDoor:
                        case VisibilitySegmentType.OpenDoor:
                            doorCount++;
                            break;
                        case VisibilitySegmentType.ClosedWindow:
                        case VisibilitySegmentType.OpenWindow:
                            windowCount++;
                            break;
                        default:
                            unknownCount++;
                            break;
                    }
                }
            }

            dirty = false;
            if (logRebuilds)
            {
                Debug.Log(
                    "[VisibilityWorld] Rebuilt. Total segments=" + segments.Count +
                    " walls=" + wallCount +
                    " doors=" + doorCount +
                    " windows=" + windowCount +
                    " unknown=" + unknownCount);
            }
        }

        private void RefreshSceneSources()
        {
            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IVisibilitySegmentSource source && !sources.Contains(source))
                    sources.Add(source);
            }
        }

        private static bool IsUsableSource(IVisibilitySegmentSource source)
        {
            if (source == null)
                return false;

            Object unityObject = source as Object;
            if (unityObject == null)
                return true;

            MonoBehaviour behaviour = unityObject as MonoBehaviour;
            if (behaviour != null)
            {
                HideFlags flags = behaviour.gameObject.hideFlags;
                if ((flags & HideFlags.HideAndDontSave) == HideFlags.HideAndDontSave ||
                    (flags & HideFlags.DontSaveInEditor) == HideFlags.DontSaveInEditor ||
                    (flags & HideFlags.DontSaveInBuild) == HideFlags.DontSaveInBuild)
                {
                    return false;
                }

                return behaviour.isActiveAndEnabled && behaviour.gameObject.scene.IsValid();
            }

            return unityObject != null;
        }

        private static string GetSourceName(IVisibilitySegmentSource source)
        {
            Object unityObject = source as Object;
            return unityObject != null ? unityObject.name : source.GetType().Name;
        }
    }
}

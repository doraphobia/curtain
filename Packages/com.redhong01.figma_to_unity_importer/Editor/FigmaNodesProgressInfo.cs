using System;
using UnityEditor;
using UnityEngine;

namespace FigmaImporter.Editor
{
    public class FigmaNodesProgressInfo
    {
        public static int NodesCount;
        public static int CurrentNode;
        public static string CurrentTitle;
        public static string CurrentInfo;
        public static double LastProgressUpdateTime { get; private set; } = EditorApplication.timeSinceStartup;
        public static bool PauseRequested { get; private set; }
        public static bool CancelRequested { get; private set; }
        private static bool _showOverallProgress;
        private static float _overallProgress;
        private static string _overallProgressLabel = string.Empty;
        private static bool _showControls;
        private static Func<bool> _isPausedProvider;
        private static Action _togglePauseAction;
        private static Action _cancelAction;

        public static void ShowProgress(float progress)
        {
            var stageProgress = Mathf.Clamp01(progress);
            if (NodesCount != 0)
            {
                CurrentTitle = $"Generating node {CurrentNode}/{NodesCount}";
                var safeTotal = Mathf.Max(1, NodesCount);
                var safeCurrent = Mathf.Clamp(CurrentNode, 0, safeTotal);
                var completedBeforeCurrent = Mathf.Clamp(safeCurrent - 1, 0, safeTotal);
                _overallProgress = Mathf.Clamp01((completedBeforeCurrent + stageProgress) / safeTotal);
                _overallProgressLabel = $"Overall Progress ({safeCurrent}/{safeTotal})";
                _showOverallProgress = true;
            }
            else
            {
                _showOverallProgress = false;
                _overallProgress = 0f;
                _overallProgressLabel = string.Empty;
            }

            ShowProgressWindow(stageProgress);
        }

        public static void ShowNodeDataProgress(int loadedCount, int totalCount, string info = null)
        {
            CurrentTitle = totalCount > 0
                ? $"Loading node data {loadedCount}/{totalCount}"
                : "Loading node data";
            CurrentInfo = string.IsNullOrWhiteSpace(info) ? "Parsing node tree..." : info;
            var progress = totalCount > 0 ? Mathf.Clamp01((float) loadedCount / totalCount) : 0f;
            _showOverallProgress = totalCount > 0;
            _overallProgress = progress;
            _overallProgressLabel = totalCount > 0
                ? $"Overall Progress ({loadedCount}/{totalCount})"
                : "Overall Progress";
            ShowProgressWindow(progress);
        }

        public static void SetGenerationControls(Func<bool> isPausedProvider, Action togglePauseAction, Action cancelAction)
        {
            _showControls = true;
            _isPausedProvider = isPausedProvider;
            _togglePauseAction = togglePauseAction;
            _cancelAction = cancelAction;
            PauseRequested = _isPausedProvider?.Invoke() ?? false;
            CancelRequested = false;
        }

        public static void ClearGenerationControls()
        {
            _showControls = false;
            _isPausedProvider = null;
            _togglePauseAction = null;
            _cancelAction = null;
            PauseRequested = false;
            CancelRequested = false;
        }

        public static void MarkActivity(string info = null)
        {
            if (!string.IsNullOrWhiteSpace(info))
            {
                CurrentInfo = info;
            }

            LastProgressUpdateTime = EditorApplication.timeSinceStartup;
        }

        public static void TogglePauseRequest(bool invokeCallback = true)
        {
            if (invokeCallback && _togglePauseAction != null)
            {
                _togglePauseAction.Invoke();
                PauseRequested = _isPausedProvider?.Invoke() ?? PauseRequested;
                MarkActivity(PauseRequested ? "Pause requested" : "Resume requested");
                return;
            }

            PauseRequested = !PauseRequested;
            MarkActivity(PauseRequested ? "Pause requested" : "Resume requested");
        }

        public static void SetPauseRequested(bool value)
        {
            PauseRequested = value;
            MarkActivity(value ? "Paused" : "Running");
        }

        public static void RequestCancel(bool invokeCallback = true)
        {
            CancelRequested = true;
            MarkActivity("Cancel requested");
            if (invokeCallback)
            {
                _cancelAction?.Invoke();
            }
        }

        public static void SetCancelRequested(bool value)
        {
            CancelRequested = value;
            if (value)
            {
                MarkActivity("Cancel requested");
            }
        }

        public static void HideProgress()
        {
            EditorUtility.ClearProgressBar();
            FigmaNodesProgressWindow.CloseIfOpen();
            _showOverallProgress = false;
            _overallProgress = 0f;
            _overallProgressLabel = string.Empty;
        }

        private static void ShowProgressWindow(float progress)
        {
            LastProgressUpdateTime = EditorApplication.timeSinceStartup;
            EditorUtility.ClearProgressBar();
            var title = string.IsNullOrWhiteSpace(CurrentTitle) ? "Figma Importer" : CurrentTitle;
            var info = string.IsNullOrWhiteSpace(CurrentInfo) ? "Working..." : CurrentInfo;
            var isPaused = _isPausedProvider?.Invoke() ?? PauseRequested;
            FigmaNodesProgressWindow.ShowOrUpdate(
                title,
                info,
                Mathf.Clamp01(progress),
                _showOverallProgress,
                _overallProgress,
                _overallProgressLabel,
                _showControls,
                isPaused);
        }
    }
}

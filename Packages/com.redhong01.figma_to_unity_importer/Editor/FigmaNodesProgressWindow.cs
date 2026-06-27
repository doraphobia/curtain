using UnityEditor;
using UnityEngine;

namespace FigmaImporter.Editor
{
    internal class FigmaNodesProgressWindow : EditorWindow
    {
        private static FigmaNodesProgressWindow _instance;

        private string _displayTitle = "Figma Importer";
        private string _displayInfo = "Preparing...";
        private float _progress;
        private bool _showOverallProgress;
        private float _overallProgress;
        private string _overallProgressLabel = "Overall Progress";
        private bool _showControls;
        private bool _isPaused;

        public static void ShowOrUpdate(
            string title,
            string info,
            float progress,
            bool showOverallProgress,
            float overallProgress,
            string overallProgressLabel,
            bool showControls,
            bool isPaused)
        {
            if (_instance == null)
            {
                _instance = CreateInstance<FigmaNodesProgressWindow>();
                _instance.minSize = new Vector2(520f, 120f);
                _instance.maxSize = new Vector2(800f, 220f);
                _instance.titleContent = new GUIContent("Figma Importer");
                _instance.ShowUtility();
            }

            _instance._displayTitle = string.IsNullOrWhiteSpace(title) ? "Figma Importer" : title;
            _instance._displayInfo = string.IsNullOrWhiteSpace(info) ? "Working..." : info;
            _instance._progress = Mathf.Clamp01(progress);
            _instance._showOverallProgress = showOverallProgress;
            _instance._overallProgress = Mathf.Clamp01(overallProgress);
            _instance._overallProgressLabel = string.IsNullOrWhiteSpace(overallProgressLabel)
                ? "Overall Progress"
                : overallProgressLabel;
            _instance._showControls = showControls;
            _instance._isPaused = isPaused;
            _instance.Repaint();
        }

        public static void CloseIfOpen()
        {
            if (_instance == null)
            {
                return;
            }

            _instance.Close();
            _instance = null;
        }

        private void OnGUI()
        {
            GUILayout.Space(8f);
            EditorGUILayout.LabelField(_displayTitle, EditorStyles.boldLabel);
            GUILayout.Space(4f);

            if (_showOverallProgress)
            {
                var overallRect = GUILayoutUtility.GetRect(1f, 18f, GUILayout.ExpandWidth(true));
                EditorGUI.ProgressBar(overallRect, _overallProgress, $"{_overallProgressLabel}  {Mathf.RoundToInt(_overallProgress * 100f)}%");
                GUILayout.Space(6f);
            }

            var progressRect = GUILayoutUtility.GetRect(1f, 18f, GUILayout.ExpandWidth(true));
            EditorGUI.ProgressBar(progressRect, _progress, $"Current Task  {Mathf.RoundToInt(_progress * 100f)}%");

            GUILayout.Space(8f);
            EditorGUILayout.LabelField(_displayInfo, EditorStyles.wordWrappedLabel);

            if (!_showControls)
            {
                return;
            }

            GUILayout.Space(10f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(_isPaused ? "Continue" : "Pause", GUILayout.Height(24f)))
            {
                FigmaNodesProgressInfo.TogglePauseRequest();
            }

            if (GUILayout.Button("Cancel", GUILayout.Height(24f)))
            {
                FigmaNodesProgressInfo.RequestCancel();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void OnDisable()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}

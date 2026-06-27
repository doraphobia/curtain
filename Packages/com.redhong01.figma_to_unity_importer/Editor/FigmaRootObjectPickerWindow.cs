using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FigmaImporter.Editor
{
    internal static class FigmaRootObjectFilterUtils
    {
        public static bool IsCanvasRelated(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return false;
            }

            return gameObject.GetComponentInParent<Canvas>(true) != null;
        }

        public static string GetHierarchyPath(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return string.Empty;
            }

            var transform = gameObject.transform;
            var sb = new StringBuilder(transform.name);
            while (transform.parent != null)
            {
                transform = transform.parent;
                sb.Insert(0, transform.name + "/");
            }

            var sceneName = gameObject.scene.IsValid() ? gameObject.scene.name : "NoScene";
            return sceneName + "/" + sb;
        }
    }

    internal sealed class FigmaRootObjectPickerWindow : EditorWindow
    {
        private Action<GameObject> _onSelected;
        private Action<bool> _onFilterChanged;
        private GameObject _currentSelection;
        private bool _canvasOnly = true;
        private string _searchText = string.Empty;
        private Vector2 _scrollPosition;

        private readonly List<GameObject> _allSceneObjects = new List<GameObject>();
        private readonly List<GameObject> _visibleObjects = new List<GameObject>();
        private readonly Dictionary<GameObject, string> _pathByObject = new Dictionary<GameObject, string>();

        public static void Open(
            GameObject currentSelection,
            bool canvasOnly,
            Action<GameObject> onSelected,
            Action<bool> onFilterChanged = null)
        {
            var window = CreateInstance<FigmaRootObjectPickerWindow>();
            window.titleContent = new GUIContent("Pick Root Object");
            window.minSize = new Vector2(720f, 480f);
            window._currentSelection = currentSelection;
            window._canvasOnly = canvasOnly;
            window._onSelected = onSelected;
            window._onFilterChanged = onFilterChanged;
            window.RefreshObjects();
            window.ShowUtility();
            window.Focus();
        }

        private void OnHierarchyChange()
        {
            RefreshObjects();
            Repaint();
        }

        private void OnGUI()
        {
            DrawToolbar();
            DrawSummary();
            DrawList();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            var toggled = GUILayout.Toggle(_canvasOnly, "Canvas Related Only", EditorStyles.toolbarButton, GUILayout.Width(160f));
            if (toggled != _canvasOnly)
            {
                _canvasOnly = toggled;
                _onFilterChanged?.Invoke(_canvasOnly);
                RebuildVisibleObjects();
            }

            var searchStyle = GUI.skin.FindStyle("ToolbarSeachTextField") ?? EditorStyles.toolbarTextField;
            var cancelStyle = GUI.skin.FindStyle("ToolbarSeachCancelButton") ?? EditorStyles.toolbarButton;

            var updatedSearch = GUILayout.TextField(_searchText ?? string.Empty, searchStyle, GUILayout.ExpandWidth(true));
            if (!string.Equals(updatedSearch, _searchText, StringComparison.Ordinal))
            {
                _searchText = updatedSearch;
                RebuildVisibleObjects();
            }

            if (GUILayout.Button(string.Empty, cancelStyle, GUILayout.Width(20f)))
            {
                _searchText = string.Empty;
                GUI.FocusControl(null);
                RebuildVisibleObjects();
            }

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70f)))
            {
                RefreshObjects();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSummary()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(
                $"Visible Objects: {_visibleObjects.Count} / {_allSceneObjects.Count}",
                EditorStyles.miniBoldLabel);

            if (GUILayout.Button("None (Clear Selection)", GUILayout.Height(22f)))
            {
                Select(null);
            }
        }

        private void DrawList()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            for (var i = 0; i < _visibleObjects.Count; i++)
            {
                var gameObject = _visibleObjects[i];
                if (gameObject == null)
                {
                    continue;
                }

                var isSelected = gameObject == _currentSelection;
                var label = BuildDisplayLabel(gameObject);
                var style = isSelected ? EditorStyles.whiteLabel : EditorStyles.label;

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(label, style, GUILayout.ExpandWidth(true), GUILayout.Height(20f)))
                {
                    Select(gameObject);
                }

                if (isSelected)
                {
                    GUILayout.Label("Selected", EditorStyles.miniBoldLabel, GUILayout.Width(60f));
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        private string BuildDisplayLabel(GameObject gameObject)
        {
            var path = GetPath(gameObject);
            if (_canvasOnly)
            {
                return gameObject.name + "    [" + path + "]";
            }

            var canvasHint = FigmaRootObjectFilterUtils.IsCanvasRelated(gameObject) ? "Canvas-related" : "Non-canvas";
            return gameObject.name + "    [" + canvasHint + "] [" + path + "]";
        }

        private void RefreshObjects()
        {
            _allSceneObjects.Clear();
            _pathByObject.Clear();

            for (var sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                var scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    continue;
                }

                var roots = scene.GetRootGameObjects();
                for (var i = 0; i < roots.Length; i++)
                {
                    CollectRecursively(roots[i].transform);
                }
            }

            _allSceneObjects.Sort((left, right) =>
                string.Compare(GetPath(left), GetPath(right), StringComparison.OrdinalIgnoreCase));

            RebuildVisibleObjects();
        }

        private void CollectRecursively(Transform transform)
        {
            if (transform == null)
            {
                return;
            }

            var gameObject = transform.gameObject;
            if (!EditorUtility.IsPersistent(gameObject))
            {
                _allSceneObjects.Add(gameObject);
                _pathByObject[gameObject] = FigmaRootObjectFilterUtils.GetHierarchyPath(gameObject);
            }

            for (var i = 0; i < transform.childCount; i++)
            {
                CollectRecursively(transform.GetChild(i));
            }
        }

        private void RebuildVisibleObjects()
        {
            _visibleObjects.Clear();
            var normalizedSearch = (_searchText ?? string.Empty).Trim();

            for (var i = 0; i < _allSceneObjects.Count; i++)
            {
                var gameObject = _allSceneObjects[i];
                if (gameObject == null)
                {
                    continue;
                }

                if (_canvasOnly && !FigmaRootObjectFilterUtils.IsCanvasRelated(gameObject))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(normalizedSearch) && !MatchesSearch(gameObject, normalizedSearch))
                {
                    continue;
                }

                _visibleObjects.Add(gameObject);
            }
        }

        private bool MatchesSearch(GameObject gameObject, string normalizedSearch)
        {
            if (gameObject == null)
            {
                return false;
            }

            if (gameObject.name.IndexOf(normalizedSearch, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            var path = GetPath(gameObject);
            return path.IndexOf(normalizedSearch, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string GetPath(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return string.Empty;
            }

            if (_pathByObject.TryGetValue(gameObject, out var path) && !string.IsNullOrEmpty(path))
            {
                return path;
            }

            path = FigmaRootObjectFilterUtils.GetHierarchyPath(gameObject);
            _pathByObject[gameObject] = path;
            return path;
        }

        private void Select(GameObject gameObject)
        {
            _currentSelection = gameObject;
            _onSelected?.Invoke(gameObject);
            Close();
            GUIUtility.ExitGUI();
        }
    }
}

using System;
using System.Collections.Generic;
using FigmaImporter.Editor.EditorTree.TreeData;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.TreeViewExamples;
using UnityEngine;
using UnityEngine.Assertions;

#pragma warning disable 618, CS0618

namespace FigmaImporter.Editor.EditorTree
{
    internal class MultiColumnTreeView : TreeViewWithTreeModel<NodeTreeElement>
    {
        const float kRowHeights = 20f;
        const float kToggleWidth = 18f;

        private readonly Dictionary<string, Node> _nodeById = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);

        public event Action<string> OnItemClick = delegate(string s) {  };
        
        // All columns
        enum MyColumns
        {
            Name,
            ActionType,
            Sprite
        }

        public static void TreeToList(TreeViewItem root, IList<TreeViewItem> result)
        {
            if (root == null)
                throw new NullReferenceException("root");
            if (result == null)
                throw new NullReferenceException("result");

            result.Clear();

            if (root.children == null)
                return;

            Stack<TreeViewItem> stack = new Stack<TreeViewItem>();
            for (int i = root.children.Count - 1; i >= 0; i--)
                stack.Push(root.children[i]);

            while (stack.Count > 0)
            {
                TreeViewItem current = stack.Pop();
                result.Add(current);

                if (current.hasChildren && current.children[0] != null)
                {
                    for (int i = current.children.Count - 1; i >= 0; i--)
                    {
                        stack.Push(current.children[i]);
                    }
                }
            }
        }

        public MultiColumnTreeView(
            TreeViewState state,
            MultiColumnHeader multicolumnHeader,
            TreeModel<NodeTreeElement> model,
            IList<Node> nodes) : base(state, multicolumnHeader, model)
        {
            // Custom setup
            rowHeight = kRowHeights;
            columnIndexForTreeFoldouts = 0;
            showAlternatingRowBackgrounds = true;
            showBorder = true;
            customFoldoutYOffset =
                (kRowHeights - EditorGUIUtility.singleLineHeight) *
                0.5f; // center foldout in the row since we also center content. See RowGUI
            extraSpaceBeforeIconAndLabel = kToggleWidth;
            multicolumnHeader.sortingChanged += OnSortingChanged;
            BuildNodeLookup(nodes);

            Reload();
        }


        // Note we We only build the visible rows, only the backend has the full tree information. 
        // The treeview only creates info for the row list.
        protected override IList<TreeViewItem> BuildRows(TreeViewItem root)
        {
            var rows = base.BuildRows(root);
            SortIfNeeded(root, rows);
            return rows;
        }

        void OnSortingChanged(MultiColumnHeader multiColumnHeader)
        {
            SortIfNeeded(rootItem, GetRows());
        }

        void SortIfNeeded(TreeViewItem root, IList<TreeViewItem> rows)
        {
            if (rows.Count <= 1)
                return;

            if (multiColumnHeader.sortedColumnIndex == -1)
            {
                return; // No column to sort for (just use the order the data are in)
            }

            // Sort the roots of the existing tree items
            TreeToList(root, rows);
            Repaint();
        }



        protected override void RowGUI(RowGUIArgs args)
        {
            var item = (TreeData.TreeViewItem<NodeTreeElement>) args.item;

            for (int i = 0; i < args.GetNumVisibleColumns(); ++i)
            {
                CellGUI(args.GetCellRect(i), item, (MyColumns) args.GetColumn(i), ref args);
            }
        }

        void CellGUI(Rect cellRect, TreeData.TreeViewItem<NodeTreeElement> item, MyColumns column, ref RowGUIArgs args)
        {
            // Center cell rect vertically (makes it easier to place controls, icons etc in the cells)
            CenterRectUsingSingleLineHeight(ref cellRect);

            switch (column)
            {
                case MyColumns.Name:
                {
                    args.rowRect = cellRect;
                    base.RowGUI(args);
                }
                    break;
                
                case MyColumns.ActionType:
                    DrawActionTypeCell(cellRect, item);
                    break;
                case MyColumns.Sprite:
                    item.data.sprite = (Sprite)EditorGUI.ObjectField(cellRect, item.data.sprite, typeof(Sprite), false);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(column), column, null);
            }
        }

        private void DrawActionTypeCell(Rect cellRect, TreeData.TreeViewItem<NodeTreeElement> item)
        {
            _nodeById.TryGetValue(item.data.figmaId ?? string.Empty, out var node);
            var displayState = NodesAnalyzer.GetDisplayState(node, item.data, treeModel.Data);
            var selectedIndex = Mathf.Max(0, ActionDisplayStateDisplayNames.IndexOf(displayState));
            var newIndex = EditorGUI.Popup(cellRect, selectedIndex, ActionDisplayStateDisplayNames.OrderedLabels);
            var newDisplayState = ActionDisplayStateDisplayNames.OrderedValues[newIndex];
            if (newDisplayState == displayState || newDisplayState == ActionDisplayState.Customized)
            {
                return;
            }

            NodesAnalyzer.ApplyDisplayStateToSubtree(node, newDisplayState, treeModel.Data);
        }

        private void BuildNodeLookup(IList<Node> nodes)
        {
            _nodeById.Clear();
            if (nodes == null)
            {
                return;
            }

            foreach (var node in nodes)
            {
                IndexNodeRecursive(node);
            }
        }

        private void IndexNodeRecursive(Node node)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.id))
            {
                return;
            }

            _nodeById[node.id] = node;
            if (node.children == null)
            {
                return;
            }

            foreach (var child in node.children)
            {
                IndexNodeRecursive(child);
            }
        }

        // Rename
        //--------

        protected override bool CanRename(TreeViewItem item)
        {
            // Only allow rename if we can show the rename overlay with a certain width (label might be clipped by other columns)
            Rect renameRect = GetRenameRect(treeViewRect, 0, item);
            return renameRect.width > 30;
        }

        protected override void RenameEnded(RenameEndedArgs args)
        {
            // Set the backend name and reload the tree to reflect the new model
            if (args.acceptedRename)
            {
                var element = treeModel.Find(args.itemID);
                element.name = args.newName;
                Reload();
            }
        }

        protected override Rect GetRenameRect(Rect rowRect, int row, TreeViewItem item)
        {
            Rect cellRect = GetCellRectForTreeFoldouts(rowRect);
            CenterRectUsingSingleLineHeight(ref cellRect);
            return base.GetRenameRect(cellRect, row, item);
        }

        // Misc
        //--------

        protected override bool CanMultiSelect(TreeViewItem item)
        {
            return true;
        }

        public static MultiColumnHeaderState CreateDefaultMultiColumnHeaderState(float treeViewWidth)
        {
            var columns = new[]
            {
                new MultiColumnHeaderState.Column
                {
                    headerContent = new GUIContent("Name"),
                    headerTextAlignment = TextAlignment.Left,
                    sortedAscending = true,
                    sortingArrowAlignment = TextAlignment.Center,
                    width = 200,
                    minWidth = 60,
                    autoResize = true,
                    allowToggleVisibility = false
                },
                new MultiColumnHeaderState.Column
                {
                    headerContent = new GUIContent("Import Mode", "Choose how this node should be imported into Unity."),
                    headerTextAlignment = TextAlignment.Right,
                    sortedAscending = true,
                    sortingArrowAlignment = TextAlignment.Left,
                    width = 150,
                    minWidth = 30,
                    autoResize = false
                },
                new MultiColumnHeaderState.Column
                {
                    headerContent = new GUIContent("Override Sprite", "Optional manual sprite override. Leave empty to use the selected import mode."),
                    headerTextAlignment = TextAlignment.Right,
                    sortedAscending = true,
                    sortingArrowAlignment = TextAlignment.Left,
                    width = 100,
                    minWidth = 30,
                    autoResize = false,
                    allowToggleVisibility = true
                }
            };

            Assert.AreEqual(columns.Length, Enum.GetValues(typeof(MyColumns)).Length,
                "Number of columns should match number of enum values: You probably forgot to update one of them.");

            var state = new MultiColumnHeaderState(columns);
            return state;
        }

        protected override void SelectionChanged(IList<int> selectedIds)
        {
            base.SelectionChanged(selectedIds);
            if (selectedIds.Count == 0)
                return;
            var treeViewItem = treeModel.Find(selectedIds[0]);
            OnItemClick(treeViewItem.figmaId);
        }
    }
}

#pragma warning restore 618, CS0618

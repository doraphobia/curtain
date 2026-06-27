using System;
using UnityEditor.TreeViewExamples;
using UnityEngine;
using Random = UnityEngine.Random;


namespace FigmaImporter.Editor.EditorTree.TreeData
{

	[Serializable]
	public class NodeTreeElement : TreeElement
	{
		public string figmaId;
		public ActionType actionType;
		public Sprite sprite;

		public NodeTreeElement (string name, string figmaId, ActionType actionType, Sprite sprite, int depth, int id) : base (name, depth, id)
		{
			this.actionType = actionType;
			this.sprite = sprite;
			this.figmaId = figmaId;
		}
	}

	public enum ActionType
	{
		None,
		Render,
		Generate,
		Transform,
#if VECTOR_GRAHICS_IMPORTED
		SvgRender
#endif
	}

	public enum ActionDisplayState
	{
		Skip,
		Render,
		Generate,
		Transform,
#if VECTOR_GRAHICS_IMPORTED
		SvgRender,
#endif
		Customized
	}

	public static class ActionTypeDisplayNames
	{
		public static readonly ActionType[] OrderedValues =
		{
			ActionType.None,
			ActionType.Render,
			ActionType.Generate,
			ActionType.Transform,
#if VECTOR_GRAHICS_IMPORTED
			ActionType.SvgRender
#endif
		};

		public static readonly GUIContent[] OrderedLabels =
		{
			new GUIContent("Skip", "Do not create or render this node."),
			new GUIContent("PNG Render", "Render this node to a raster PNG sprite and apply it in Unity."),
			new GUIContent("Native Generate", "Build this node as editable Unity UI using text, fills, and children."),
			new GUIContent("Transform Only", "Create only the RectTransform container and let children provide the visuals."),
#if VECTOR_GRAHICS_IMPORTED
			new GUIContent("SVG Render", "Render this node as an SVG/vector sprite when Unity Vector Graphics can support it."),
#endif
		};

		public static int IndexOf(ActionType actionType)
		{
			for (int i = 0; i < OrderedValues.Length; i++)
			{
				if (OrderedValues[i] == actionType)
				{
					return i;
				}
			}

			return 0;
		}
	}

	public static class ActionDisplayStateDisplayNames
	{
		public static readonly ActionDisplayState[] OrderedValues =
		{
			ActionDisplayState.Skip,
			ActionDisplayState.Render,
			ActionDisplayState.Generate,
			ActionDisplayState.Transform,
#if VECTOR_GRAHICS_IMPORTED
			ActionDisplayState.SvgRender,
#endif
			ActionDisplayState.Customized
		};

		public static readonly GUIContent[] OrderedLabels =
		{
			new GUIContent("Skip", "Skip this node or subtree."),
			new GUIContent("PNG Render", "Render this whole subtree as PNG output."),
			new GUIContent("Native Generate", "Use the default editable generation logic for this subtree."),
			new GUIContent("Transform Only", "Keep this node as a transform container and use default generation for its children."),
#if VECTOR_GRAHICS_IMPORTED
			new GUIContent("SVG Render", "Prefer SVG/vector rendering for this subtree where supported."),
#endif
			new GUIContent("Customized", "This subtree has per-node custom import mode overrides.")
		};

		public static int IndexOf(ActionDisplayState actionDisplayState)
		{
			for (int i = 0; i < OrderedValues.Length; i++)
			{
				if (OrderedValues[i] == actionDisplayState)
				{
					return i;
				}
			}

			return 0;
		}

		public static ActionDisplayState FromActionType(ActionType actionType)
		{
			switch (actionType)
			{
				case ActionType.None:
					return ActionDisplayState.Skip;
				case ActionType.Render:
					return ActionDisplayState.Render;
				case ActionType.Generate:
					return ActionDisplayState.Generate;
				case ActionType.Transform:
					return ActionDisplayState.Transform;
#if VECTOR_GRAHICS_IMPORTED
				case ActionType.SvgRender:
					return ActionDisplayState.SvgRender;
#endif
				default:
					return ActionDisplayState.Customized;
			}
		}
	}
}

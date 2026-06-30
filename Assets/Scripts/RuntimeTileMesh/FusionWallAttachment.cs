using System.Collections.Generic;
using DuoCurtain.Vision;
using UnityEngine;

namespace DuoCurtain.RuntimeTileMesh
{
    [DisallowMultipleComponent]
    public sealed class FusionWallAttachment : MonoBehaviour, IVisibilitySegmentSource
    {
        private const string DefaultSpriteName = "Fusion Runtime Window Sprite";

        public enum AttachmentType
        {
            Window
        }

        [Header("Identity")]
        public AttachmentType attachmentType = AttachmentType.Window;
        public string attachmentId = "WINDOW";

        [Header("Placement")]
        public RuntimeTileMeshFusionDoor.DoorAxis axis = RuntimeTileMeshFusionDoor.DoorAxis.Vertical;
        public Vector2 edgeCenter;
        public Vector2 outwardNormal = Vector2.right;
        public Vector2 tangent = Vector2.up;
        [Min(0.01f)]
        public float gridSize = 1f;
        [Min(0.01f)]
        public float lengthInCells = 0.82f;
        [Min(0.01f)]
        public float thicknessInCells = 0.16f;
        public bool insetIntoFloor = true;
        [Min(0f)]
        public float insetDistanceInCells = 0.04f;

        [Header("Window Visual")]
        public Color closedColor = new Color(1f, 0.92f, 0.08f, 1f);
        public Color openColor = new Color(0.55f, 0.85f, 1f, 1f);
        public int sortingOrder = 35;

        [Header("Window Behavior")]
        public bool startsOpen;
        public float scrollStep = 0.08f;
        public HoverScrollColorLerp2D.SideType sunlightSide = HoverScrollColorLerp2D.SideType.None;
        public bool useLogicalCursorHover = true;
        public bool allowLocalHoverInput = true;

        [Header("Visibility")]
        public bool registerForVisibility = true;

        private static Sprite defaultSprite;
        private SpriteRenderer spriteRenderer;
        private BoxCollider2D windowCollider;
        private HoverScrollColorLerp2D hoverScroll;
        private WindowPortal windowPortal;

        public WindowPortal WindowPortal => windowPortal;
        public HoverScrollColorLerp2D HoverScroll => hoverScroll;

        void Awake()
        {
            EnsureWindowComponents();
            ApplyPlacement();
            ApplyWindowSettings();
        }

        void OnEnable()
        {
            if (!registerForVisibility)
                return;

            VisibilityWorld.GetOrCreate().RegisterSource(this);
            MarkVisibilityDirty();
        }

        void OnDisable()
        {
            if (VisibilityWorld.Instance != null)
                VisibilityWorld.Instance.UnregisterSource(this);
        }

        void OnValidate()
        {
            gridSize = Mathf.Max(0.01f, gridSize);
            lengthInCells = Mathf.Max(0.01f, lengthInCells);
            thicknessInCells = Mathf.Max(0.01f, thicknessInCells);
            insetDistanceInCells = Mathf.Max(0f, insetDistanceInCells);

            if (Application.isPlaying || spriteRenderer != null || windowCollider != null)
            {
                EnsureWindowComponents();
                ApplyPlacement();
                ApplyWindowSettings();
                MarkVisibilityDirty();
            }
        }

        public void ConfigureWindow(
            RuntimeTileMeshFusionSandbox.FusionWallEdgePlacement placement,
            float worldGridSize,
            float windowLengthInCells,
            float windowThicknessInCells,
            Color closedWindowColor,
            Color openWindowColor,
            bool initiallyOpen)
        {
            attachmentType = AttachmentType.Window;
            attachmentId = "WINDOW";
            axis = placement.axis;
            edgeCenter = placement.center;
            outwardNormal = placement.normal.sqrMagnitude > 0.0001f ? placement.normal.normalized : Vector2.right;
            tangent = placement.tangent.sqrMagnitude > 0.0001f ? placement.tangent.normalized : Vector2.up;
            gridSize = Mathf.Max(0.01f, worldGridSize);
            lengthInCells = Mathf.Max(0.01f, windowLengthInCells);
            thicknessInCells = Mathf.Max(0.01f, windowThicknessInCells);
            closedColor = closedWindowColor;
            openColor = openWindowColor;
            startsOpen = initiallyOpen;

            EnsureWindowComponents();
            ApplyPlacement();
            ApplyWindowSettings();
            MarkVisibilityDirty();
        }

        public void CollectVisibilitySegments(List<VisibilitySegment> results)
        {
            if (results == null)
                return;
            if (!registerForVisibility)
                return;

            Vector2 safeTangent = tangent.sqrMagnitude > 0.0001f ? tangent.normalized : Vector2.up;
            float halfLength = Mathf.Max(0.01f, lengthInCells) * Mathf.Max(0.01f, gridSize) * 0.5f;
            VisibilitySegmentType type = windowPortal != null && windowPortal.IsOpen
                ? VisibilitySegmentType.OpenWindow
                : VisibilitySegmentType.ClosedWindow;
            results.Add(new VisibilitySegment(
                edgeCenter - safeTangent * halfLength,
                edgeCenter + safeTangent * halfLength,
                type,
                gameObject,
                this));
        }

        private void EnsureWindowComponents()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer == null)
                spriteRenderer = gameObject.AddComponent<SpriteRenderer>();

            windowCollider = GetComponent<BoxCollider2D>();
            if (windowCollider == null)
                windowCollider = gameObject.AddComponent<BoxCollider2D>();

            hoverScroll = GetComponent<HoverScrollColorLerp2D>();
            if (hoverScroll == null)
                hoverScroll = gameObject.AddComponent<HoverScrollColorLerp2D>();

            windowPortal = GetComponent<WindowPortal>();
            if (windowPortal == null)
                windowPortal = gameObject.AddComponent<WindowPortal>();

            if (spriteRenderer.sprite == null)
                spriteRenderer.sprite = GetDefaultSprite();
        }

        private void ApplyPlacement()
        {
            Vector2 normal = outwardNormal.sqrMagnitude > 0.0001f ? outwardNormal.normalized : Vector2.right;
            Vector2 center = edgeCenter;
            if (insetIntoFloor)
                center -= normal * (Mathf.Max(0f, insetDistanceInCells) * gridSize);

            transform.position = new Vector3(center.x, center.y, -0.12f);

            float angle = axis == RuntimeTileMeshFusionDoor.DoorAxis.Vertical ? 90f : 0f;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
            transform.localScale = new Vector3(
                Mathf.Max(0.01f, lengthInCells * gridSize),
                Mathf.Max(0.01f, thicknessInCells * gridSize),
                1f);

            if (windowCollider != null)
            {
                windowCollider.isTrigger = true;
                windowCollider.size = Vector2.one;
                windowCollider.offset = Vector2.zero;
            }
        }

        private void ApplyWindowSettings()
        {
            if (spriteRenderer != null)
            {
                spriteRenderer.color = startsOpen ? openColor : closedColor;
                spriteRenderer.sortingOrder = sortingOrder;
            }

            if (hoverScroll != null)
            {
                hoverScroll.colorA = closedColor;
                hoverScroll.colorB = openColor;
                hoverScroll.stepPerScroll = Mathf.Clamp(scrollStep, 0.001f, 0.5f);
                hoverScroll.sideType = sunlightSide;
                hoverScroll.useLogicalCursorHover = useLogicalCursorHover;
                hoverScroll.allowLocalHoverInput = allowLocalHoverInput;
                hoverScroll.SetProgress(startsOpen ? 1f : 0f);
            }

            if (windowPortal != null)
            {
                windowPortal.curtain = hoverScroll;
                windowPortal.windowCollider = windowCollider;
                windowPortal.manualIsOpen = startsOpen;
            }

            MarkVisibilityDirty();
        }

        private static void MarkVisibilityDirty()
        {
            VisibilityWorld.MarkActiveWorldDirty();
        }

        public static Sprite GetDefaultWindowSprite()
        {
            return GetDefaultSprite();
        }

        private static Sprite GetDefaultSprite()
        {
            if (defaultSprite != null)
                return defaultSprite;

            Texture2D texture = new Texture2D(8, 8, TextureFormat.RGBA32, false)
            {
                name = DefaultSpriteName,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            Color[] pixels = new Color[8 * 8];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = Color.white;
            texture.SetPixels(pixels);
            texture.Apply();

            defaultSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                texture.width);
            defaultSprite.name = DefaultSpriteName;
            defaultSprite.hideFlags = HideFlags.HideAndDontSave;
            return defaultSprite;
        }
    }
}

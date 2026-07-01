using System.Collections.Generic;
using DuoCurtain.GameplayVisuals;
using DuoCurtain.Vision;
using UnityEngine;

namespace DuoCurtain.RuntimeTileMesh
{
    [DisallowMultipleComponent]
    public sealed class FusionWallAttachment : MonoBehaviour, IVisibilitySegmentSource, IVisibilityOpeningSource
    {
        private const string DefaultSpriteName = "Fusion Runtime Window Sprite";
        private static readonly List<FusionWallAttachment> activeWindowAttachments =
            new List<FusionWallAttachment>();

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
        public bool mergeAdjacentWindowsForVisibility = true;
        public bool mergeAdjacentWindowsForVisuals = true;
        public bool hideMergedWindowFollowers = true;
        [Min(0f)]
        public float mergeGapToleranceInCells = 0.25f;

        private static Sprite defaultSprite;
        private static bool refreshingWindowMerges;
        private SpriteRenderer spriteRenderer;
        private BoxCollider2D windowCollider;
        private HoverScrollColorLerp2D hoverScroll;
        private WindowPortal windowPortal;
        private GameplayVisualRenderer adaptiveVisualRenderer;
        private readonly List<WindowMergeCandidate> mergeCandidates = new List<WindowMergeCandidate>(16);

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
            if (!activeWindowAttachments.Contains(this))
                activeWindowAttachments.Add(this);

            VisibilityWorld world = VisibilityWorld.GetOrCreate();
            if (registerForVisibility)
            {
                world.RegisterSource(this);
                world.RegisterOpeningSource(this);
            }

            MarkVisibilityDirty();
            RefreshAllWindowMergeVisuals();
        }

        void OnDisable()
        {
            activeWindowAttachments.Remove(this);

            if (VisibilityWorld.Instance != null)
            {
                VisibilityWorld.Instance.UnregisterSource(this);
                VisibilityWorld.Instance.UnregisterOpeningSource(this);
            }

            RefreshAllWindowMergeVisuals();
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
                RefreshAllWindowMergeVisuals();
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
            RefreshAllWindowMergeVisuals();
        }

        public void CollectVisibilitySegments(List<VisibilitySegment> results)
        {
            if (results == null)
                return;
            if (!registerForVisibility)
                return;

            EnsureWindowComponents();
            Vector2 safeTangent = tangent.sqrMagnitude > 0.0001f ? tangent.normalized : Vector2.up;
            float halfLength = Mathf.Max(0.01f, lengthInCells) * Mathf.Max(0.01f, gridSize) * 0.5f;
            VisibilitySegmentType type = windowPortal != null && windowPortal.IsOpen
                ? VisibilitySegmentType.OpenWindow
                : VisibilitySegmentType.ClosedWindow;

            if (type == VisibilitySegmentType.OpenWindow)
                return;

            if (mergeAdjacentWindowsForVisibility &&
                TryGetMergedWindowSpan(
                    forVisualMerge: false,
                    matchOpenState: !mergeAdjacentWindowsForVisuals,
                    isOpen: type == VisibilitySegmentType.OpenWindow,
                    out Vector2 mergedStart,
                    out Vector2 mergedEnd,
                    out FusionWallAttachment representative))
            {
                if (representative != this)
                {
                    if (windowPortal != null)
                        windowPortal.ClearRuntimePortalOverride();
                    return;
                }

                Vector2 mergedCenter = (mergedStart + mergedEnd) * 0.5f;
                float mergedLength = Vector2.Distance(mergedStart, mergedEnd);
                if (windowPortal != null)
                    windowPortal.SetRuntimePortalOverride(mergedCenter, safeTangent, outwardNormal, mergedLength);

                results.Add(new VisibilitySegment(
                    mergedStart,
                    mergedEnd,
                    type,
                    gameObject,
                    windowPortal != null ? windowPortal : this));
                return;
            }

            if (windowPortal != null)
            {
                windowPortal.ClearRuntimePortalOverride();
                windowPortal.ConfigurePortal(
                    edgeCenter,
                    safeTangent,
                    outwardNormal,
                    Mathf.Max(0.01f, lengthInCells) * Mathf.Max(0.01f, gridSize));
            }

            results.Add(new VisibilitySegment(
                edgeCenter - safeTangent * halfLength,
                edgeCenter + safeTangent * halfLength,
                type,
                gameObject,
                windowPortal != null ? windowPortal : this));
        }

        public void CollectVisibilityOpenings(List<VisibilityOpening> results)
        {
            if (results == null || !registerForVisibility)
                return;

            EnsureWindowComponents();
            if (windowPortal == null || !windowPortal.IsOpen)
                return;

            Vector2 safeTangent = tangent.sqrMagnitude > 0.0001f ? tangent.normalized : Vector2.up;
            Vector2 safeNormal = outwardNormal.sqrMagnitude > 0.0001f ? outwardNormal.normalized : Vector2.right;

            if (mergeAdjacentWindowsForVisibility &&
                TryGetMergedWindowSpan(
                    forVisualMerge: false,
                    matchOpenState: false,
                    isOpen: true,
                    out Vector2 mergedStart,
                    out Vector2 mergedEnd,
                    out FusionWallAttachment representative))
            {
                if (representative != this)
                    return;

                Vector2 mergedCenter = (mergedStart + mergedEnd) * 0.5f;
                float mergedLength = Vector2.Distance(mergedStart, mergedEnd);
                windowPortal.SetRuntimePortalOverride(mergedCenter, safeTangent, safeNormal, mergedLength);
                AddOpening(results, mergedStart, mergedEnd, safeNormal);
                return;
            }

            float halfLength = Mathf.Max(0.01f, lengthInCells) * Mathf.Max(0.01f, gridSize) * 0.5f;
            windowPortal.ConfigurePortal(
                edgeCenter,
                safeTangent,
                safeNormal,
                halfLength * 2f);
            AddOpening(
                results,
                edgeCenter - safeTangent * halfLength,
                edgeCenter + safeTangent * halfLength,
                safeNormal);
        }

        private void AddOpening(List<VisibilityOpening> results, Vector2 start, Vector2 end, Vector2 normal)
        {
            results.Add(new VisibilityOpening(
                OpeningGeometry.Segment(start, end, normal),
                true,
                OpeningProjectionRule.ContinueIncomingRay,
                gameObject,
                windowPortal != null ? windowPortal : this));
        }

        public static void RefreshAllWindowMergeVisuals()
        {
            if (refreshingWindowMerges)
                return;

            refreshingWindowMerges = true;
            try
            {
                for (int i = activeWindowAttachments.Count - 1; i >= 0; i--)
                {
                    FusionWallAttachment attachment = activeWindowAttachments[i];
                    if (attachment == null)
                    {
                        activeWindowAttachments.RemoveAt(i);
                        continue;
                    }

                    if (attachment.isActiveAndEnabled)
                        attachment.ApplyMergedVisualState();
                }
            }
            finally
            {
                refreshingWindowMerges = false;
            }

            MarkVisibilityDirty();
        }

        private bool TryGetMergedWindowSpan(
            bool forVisualMerge,
            bool matchOpenState,
            bool isOpen,
            out Vector2 mergedStart,
            out Vector2 mergedEnd,
            out FusionWallAttachment representative)
        {
            mergedStart = edgeCenter;
            mergedEnd = edgeCenter;
            representative = this;

            mergeCandidates.Clear();
            float safeGridSize = Mathf.Max(0.01f, gridSize);
            float lineTolerance = safeGridSize * 0.05f;
            float gapTolerance = safeGridSize * Mathf.Max(0f, mergeGapToleranceInCells);
            float lineCoordinate = GetLineCoordinate();
            float selfStart = GetIntervalStart();
            float selfEnd = GetIntervalEnd();

            for (int i = activeWindowAttachments.Count - 1; i >= 0; i--)
            {
                FusionWallAttachment attachment = activeWindowAttachments[i];
                if (attachment == null)
                {
                    activeWindowAttachments.RemoveAt(i);
                    continue;
                }

                if (!CanMergeWith(
                        attachment,
                        forVisualMerge,
                        matchOpenState,
                        isOpen,
                        lineCoordinate,
                        lineTolerance))
                    continue;

                mergeCandidates.Add(new WindowMergeCandidate(
                    attachment,
                    attachment.GetIntervalStart(),
                    attachment.GetIntervalEnd()));
            }

            if (mergeCandidates.Count <= 1)
                return false;

            float mergedMin = selfStart;
            float mergedMax = selfEnd;
            bool changed;
            do
            {
                changed = false;
                for (int i = 0; i < mergeCandidates.Count; i++)
                {
                    WindowMergeCandidate candidate = mergeCandidates[i];
                    if (candidate.end < mergedMin - gapTolerance ||
                        candidate.start > mergedMax + gapTolerance)
                    {
                        continue;
                    }

                    float previousMin = mergedMin;
                    float previousMax = mergedMax;
                    mergedMin = Mathf.Min(mergedMin, candidate.start);
                    mergedMax = Mathf.Max(mergedMax, candidate.end);
                    if (!Mathf.Approximately(previousMin, mergedMin) ||
                        !Mathf.Approximately(previousMax, mergedMax))
                    {
                        changed = true;
                    }
                }
            } while (changed);

            int connectedCount = 0;
            representative = this;
            float representativeStart = selfStart;
            int representativeId = GetInstanceID();
            for (int i = 0; i < mergeCandidates.Count; i++)
            {
                WindowMergeCandidate candidate = mergeCandidates[i];
                if (candidate.end < mergedMin - gapTolerance ||
                    candidate.start > mergedMax + gapTolerance)
                {
                    continue;
                }

                connectedCount++;
                int candidateId = candidate.attachment.GetInstanceID();
                if (candidate.start < representativeStart - 0.0001f ||
                    (Mathf.Abs(candidate.start - representativeStart) <= 0.0001f && candidateId < representativeId))
                {
                    representative = candidate.attachment;
                    representativeStart = candidate.start;
                    representativeId = candidateId;
                }
            }

            if (connectedCount <= 1)
                return false;

            Vector2 safeTangent = tangent.sqrMagnitude > 0.0001f ? tangent.normalized : Vector2.up;
            float centerAlong = (mergedMin + mergedMax) * 0.5f;
            Vector2 center = axis == RuntimeTileMeshFusionDoor.DoorAxis.Vertical
                ? new Vector2(lineCoordinate, centerAlong)
                : new Vector2(centerAlong, lineCoordinate);
            float halfLength = Mathf.Max(0.01f, mergedMax - mergedMin) * 0.5f;
            mergedStart = center - safeTangent * halfLength;
            mergedEnd = center + safeTangent * halfLength;
            return true;
        }

        private bool CanMergeWith(
            FusionWallAttachment attachment,
            bool forVisualMerge,
            bool matchOpenState,
            bool isOpen,
            float lineCoordinate,
            float lineTolerance)
        {
            if (attachment == null ||
                attachment.attachmentType != AttachmentType.Window ||
                !attachment.isActiveAndEnabled ||
                attachment.axis != axis)
            {
                return false;
            }

            if (forVisualMerge)
            {
                if (!mergeAdjacentWindowsForVisuals || !attachment.mergeAdjacentWindowsForVisuals)
                    return false;
            }
            else
            {
                if (!registerForVisibility ||
                    !attachment.registerForVisibility ||
                    !mergeAdjacentWindowsForVisibility ||
                    !attachment.mergeAdjacentWindowsForVisibility)
                {
                    return false;
                }
            }

            attachment.EnsureWindowComponents();
            bool attachmentOpen = attachment.windowPortal != null && attachment.windowPortal.IsOpen;
            if (matchOpenState && attachmentOpen != isOpen)
                return false;

            if (Mathf.Abs(attachment.GetLineCoordinate() - lineCoordinate) > lineTolerance)
                return false;

            Vector2 safeNormal = outwardNormal.sqrMagnitude > 0.0001f ? outwardNormal.normalized : Vector2.right;
            Vector2 otherNormal = attachment.outwardNormal.sqrMagnitude > 0.0001f
                ? attachment.outwardNormal.normalized
                : Vector2.right;
            if (Vector2.Dot(safeNormal, otherNormal) < 0.95f)
                return false;

            Vector2 safeTangent = tangent.sqrMagnitude > 0.0001f ? tangent.normalized : Vector2.up;
            Vector2 otherTangent = attachment.tangent.sqrMagnitude > 0.0001f
                ? attachment.tangent.normalized
                : Vector2.up;
            return Mathf.Abs(Vector2.Dot(safeTangent, otherTangent)) >= 0.95f;
        }

        private void ApplyMergedVisualState()
        {
            EnsureWindowComponents();

            if (!mergeAdjacentWindowsForVisuals ||
                !TryGetMergedWindowSpan(
                    forVisualMerge: true,
                    matchOpenState: false,
                    isOpen: windowPortal != null && windowPortal.IsOpen,
                    out Vector2 mergedStart,
                    out Vector2 mergedEnd,
                    out FusionWallAttachment representative))
            {
                SetMergedFollowerState(false);
                ApplyPlacement();
                ApplyWindowSettingsWithoutDirty();
                return;
            }

            if (representative != this)
            {
                SetMergedFollowerState(hideMergedWindowFollowers);
                if (windowPortal != null)
                    windowPortal.ClearRuntimePortalOverride();
                return;
            }

            SetMergedFollowerState(false);
            Vector2 mergedCenter = (mergedStart + mergedEnd) * 0.5f;
            float mergedLength = Vector2.Distance(mergedStart, mergedEnd);
            ApplyPlacementOverride(mergedCenter, mergedLength);
            ApplyWindowSettingsWithoutDirty();

            if (windowPortal != null)
            {
                windowPortal.SetRuntimePortalOverride(
                    mergedCenter,
                    tangent.sqrMagnitude > 0.0001f ? tangent.normalized : Vector2.up,
                    outwardNormal.sqrMagnitude > 0.0001f ? outwardNormal.normalized : Vector2.right,
                    Mathf.Max(0.01f, mergedLength));
            }
        }

        private void SetMergedFollowerState(bool hiddenFollower)
        {
            if (spriteRenderer != null)
                spriteRenderer.enabled = !hiddenFollower;
            if (windowCollider != null)
                windowCollider.enabled = !hiddenFollower;
        }

        private float GetLineCoordinate()
        {
            return axis == RuntimeTileMeshFusionDoor.DoorAxis.Vertical ? edgeCenter.x : edgeCenter.y;
        }

        private float GetAlongCoordinate()
        {
            return axis == RuntimeTileMeshFusionDoor.DoorAxis.Vertical ? edgeCenter.y : edgeCenter.x;
        }

        private float GetIntervalStart()
        {
            return GetAlongCoordinate() - Mathf.Max(0.01f, lengthInCells) * Mathf.Max(0.01f, gridSize) * 0.5f;
        }

        private float GetIntervalEnd()
        {
            return GetAlongCoordinate() + Mathf.Max(0.01f, lengthInCells) * Mathf.Max(0.01f, gridSize) * 0.5f;
        }

        private readonly struct WindowMergeCandidate
        {
            public readonly FusionWallAttachment attachment;
            public readonly float start;
            public readonly float end;

            public WindowMergeCandidate(FusionWallAttachment attachment, float start, float end)
            {
                this.attachment = attachment;
                this.start = Mathf.Min(start, end);
                this.end = Mathf.Max(start, end);
            }
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

            if (adaptiveVisualRenderer == null)
            {
                adaptiveVisualRenderer = GameplayVisualRenderer.Ensure(
                    spriteRenderer,
                    GameplayVisualPriority.Interaction);
                if (adaptiveVisualRenderer != null)
                {
                    adaptiveVisualRenderer.collectTargetsAutomatically = false;
                    adaptiveVisualRenderer.renderers = new Renderer[] { spriteRenderer };
                    adaptiveVisualRenderer.contrastStrength = 1.05f;
                    adaptiveVisualRenderer.adaptiveBlend = 0.82f;
                    adaptiveVisualRenderer.edgeContrast = 0.5f;
                }
            }
            adaptiveVisualRenderer?.Refresh();
        }

        private void ApplyPlacement()
        {
            ApplyPlacementOverride(
                edgeCenter,
                Mathf.Max(0.01f, lengthInCells) * Mathf.Max(0.01f, gridSize));
        }

        private void ApplyPlacementOverride(Vector2 centerOnEdge, float worldLength)
        {
            Vector2 normal = outwardNormal.sqrMagnitude > 0.0001f ? outwardNormal.normalized : Vector2.right;
            Vector2 center = centerOnEdge;
            if (insetIntoFloor)
                center -= normal * (Mathf.Max(0f, insetDistanceInCells) * gridSize);

            transform.position = new Vector3(center.x, center.y, -0.12f);

            float angle = axis == RuntimeTileMeshFusionDoor.DoorAxis.Vertical ? 90f : 0f;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
            transform.localScale = new Vector3(
                Mathf.Max(0.01f, worldLength),
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
            ApplyWindowSettingsWithoutDirty();
            MarkVisibilityDirty();
        }

        private void ApplyWindowSettingsWithoutDirty()
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
                windowPortal.ConfigurePortal(
                    edgeCenter,
                    tangent,
                    outwardNormal,
                    Mathf.Max(0.01f, lengthInCells) * Mathf.Max(0.01f, gridSize));
            }
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

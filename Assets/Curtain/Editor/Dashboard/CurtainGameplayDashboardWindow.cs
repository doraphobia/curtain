using System;
using System.Collections.Generic;
using System.IO;
using Curtain.Editor;
using Curtain.Settings;
using DuoCurtain.RuntimeTileMesh;
using DuoCurtain.Vision;
using UnityEditor;
using UnityEngine;

namespace Curtain.Editor.Dashboard
{
    public sealed class CurtainGameplayDashboardWindow : EditorWindow
    {
        private enum RelatedAssetKind
        {
            ScriptableObject,
            Prefab,
            Script,
            SceneObject,
            Folder
        }

        private readonly struct RelatedAssetEntry
        {
            public readonly RelatedAssetKind kind;
            public readonly string label;
            public readonly UnityEngine.Object asset;
            public readonly Type sceneObjectType;
            public readonly string sceneObjectNameHint;

            public RelatedAssetEntry(RelatedAssetKind kind, string label, UnityEngine.Object asset)
            {
                this.kind = kind;
                this.label = label;
                this.asset = asset;
                sceneObjectType = null;
                sceneObjectNameHint = null;
            }

            public RelatedAssetEntry(RelatedAssetKind kind, string label, Type sceneObjectType, string nameHint = null)
            {
                this.kind = kind;
                this.label = label;
                asset = null;
                this.sceneObjectType = sceneObjectType;
                sceneObjectNameHint = nameHint;
            }
        }

        private enum Page
        {
            Gameplay,
            Enemy,
            Vision,
            Door,
            Camera,
            Sanity,
            Economy,
            Accessibility,
            Localization,
            Debug,
            Tools,
            Builds
        }

        private const string SettingsFolder = "Assets/Curtain/Settings";

        private const string EnemyAssetPath = SettingsFolder + "/EnemySettings.asset";
        private const string VisionAssetPath = SettingsFolder + "/VisionSettings.asset";
        private const string DoorAssetPath = SettingsFolder + "/DoorSettings.asset";
        private const string CameraAssetPath = SettingsFolder + "/CameraSettings.asset";
        private const string FootprintAssetPath = SettingsFolder + "/FootprintSettings.asset";
        private const string SanityAssetPath = SettingsFolder + "/SanitySettings.asset";
        private const string EconomyAssetPath = SettingsFolder + "/EconomySettings.asset";
        private const string LocalizationAssetPath = SettingsFolder + "/LocalizationSettings.asset";
        private const string AccessibilityAssetPath = SettingsFolder + "/AccessibilitySettings.asset";
        private const string DebugAssetPath = SettingsFolder + "/DebugSettings.asset";
        private const string BuildArchiveAssetPath = SettingsFolder + "/BuildArchiveSettings.asset";

        private Page currentPage = Page.Enemy;
        private Vector2 leftScroll;
        private Vector2 rightScroll;

        private GUIStyle navButton;
        private GUIStyle pageTitle;
        private GUIStyle card;
        private GUIStyle cardHeader;
        private GUIStyle wrapLabel;
        private GUIStyle singleLineLabel;
        private GUIStyle rowBackground;

        private float navWidth = 252f;
        private bool draggingSplitter;

        private bool showRelatedAssets = true;

        private EnemySettings enemySettings;
        private VisionSettings visionSettings;
        private DoorSettings doorSettings;
        private CameraSettings cameraSettings;
        private FootprintSettings footprintSettings;
        private SanitySettings sanitySettings;
        private EconomySettings economySettings;
        private LocalizationSettings localizationSettings;
        private AccessibilitySettings accessibilitySettings;
        private DebugSettings debugSettings;
        private BuildArchiveSettings buildArchiveSettings;

        [MenuItem("Tools/Curtain/Gameplay Dashboard")]
        private static void Open()
        {
            CurtainGameplayDashboardWindow window = GetWindow<CurtainGameplayDashboardWindow>("Curtain Dashboard");
            window.minSize = new Vector2(980f, 620f);
            window.Show();
        }

        private void OnEnable()
        {
            EnsureSettingsFolder();
            LoadOrCreateAllAssets();
#if UNITY_EDITOR
            CurtainSettingsBundleInstaller.EnsureBundle();
#endif
        }

        private void OnGUI()
        {
            if (!TryBuildStyles())
                return;

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawLeftNavResizable();
                DrawRightContent();
            }
        }

        private void DrawLeftNavResizable()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(Mathf.Clamp(navWidth, 180f, 420f))))
            {
                GUILayout.Space(10f);
                using (var scroll = new EditorGUILayout.ScrollViewScope(leftScroll))
                {
                    leftScroll = scroll.scrollPosition;

                    DrawNavButton(Page.Gameplay, "Gameplay");
                    GUILayout.Space(8f);
                    DrawNavButton(Page.Enemy, "Enemy");
                    DrawNavButton(Page.Vision, "Vision");
                    DrawNavButton(Page.Door, "Door");
                    DrawNavButton(Page.Camera, "Camera");
                    DrawNavButton(Page.Sanity, "Sanity");
                    DrawNavButton(Page.Economy, "Economy");
                    GUILayout.Space(8f);
                    DrawNavButton(Page.Accessibility, "Accessibility");
                    DrawNavButton(Page.Localization, "Localization");
                    DrawNavButton(Page.Debug, "Debug");
                    GUILayout.Space(8f);
                    DrawNavButton(Page.Tools, "Tools");
                    DrawNavButton(Page.Builds, "Builds");
                }
            }

            DrawNavSplitter();
        }

        private void DrawNavSplitter()
        {
            Rect rect = GUILayoutUtility.GetRect(4f, 0f, GUILayout.Width(4f), GUILayout.ExpandHeight(true));
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);
            Color c = EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.06f) : new Color(0f, 0f, 0f, 0.08f);
            EditorGUI.DrawRect(rect, c);

            Event e = Event.current;
            if (e == null)
                return;

            if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
            {
                draggingSplitter = true;
                e.Use();
            }
            else if (e.type == EventType.MouseUp)
            {
                draggingSplitter = false;
            }
            else if (draggingSplitter && e.type == EventType.MouseDrag)
            {
                navWidth = Mathf.Clamp(navWidth + e.delta.x, 180f, 420f);
                Repaint();
                e.Use();
            }
        }

        private void DrawNavButton(Page page, string label)
        {
            bool selected = currentPage == page;
            Color prev = GUI.backgroundColor;
            if (selected)
                GUI.backgroundColor = new Color(0.25f, 0.55f, 0.95f, 0.65f);

            if (GUILayout.Button(label, navButton))
                currentPage = page;

            GUI.backgroundColor = prev;
        }

        private void DrawRightContent()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                GUILayout.Space(10f);

                using (var scroll = new EditorGUILayout.ScrollViewScope(rightScroll))
                {
                    rightScroll = scroll.scrollPosition;

                    GUILayout.Label(currentPage.ToString(), pageTitle);
                    GUILayout.Space(12f);

                    if (currentPage != Page.Tools && currentPage != Page.Builds)
                    {
                        DrawRelatedAssetsSection();
                        GUILayout.Space(6f);
                    }

                    switch (currentPage)
                    {
                        case Page.Gameplay:
                            DrawGameplayOverview();
                            break;
                        case Page.Enemy:
                            DrawEnemyPage();
                            break;
                        case Page.Vision:
                            DrawVisionPage();
                            break;
                        case Page.Door:
                            DrawDoorPage();
                            break;
                        case Page.Camera:
                            DrawCameraPage();
                            break;
                        case Page.Sanity:
                            DrawSanityPage();
                            break;
                        case Page.Economy:
                            DrawEconomyPage();
                            break;
                        case Page.Accessibility:
                            DrawAccessibilityPage();
                            break;
                        case Page.Localization:
                            DrawLocalizationPage();
                            break;
                        case Page.Debug:
                            DrawDebugPage();
                            break;
                        case Page.Tools:
                            CurtainDashboardDuoCurtainTools.DrawToolsPage();
                            break;
                        case Page.Builds:
                            CurtainDashboardBuildsPage.DrawBuildsPage(buildArchiveSettings);
                            break;
                        default:
                            EditorGUILayout.HelpBox("Page not implemented.", MessageType.Info);
                            break;
                    }
                }
            }
        }

        private void DrawGameplayOverview()
        {
            DrawCard(
                "Purpose",
                () =>
                {
                    EditorGUILayout.HelpBox(
                        "This Dashboard centralizes gameplay tuning into ScriptableObject settings.\n" +
                        "Runtime state remains on components.\n" +
                        "Edit values here; systems that reference these assets can live-tune in Play Mode.",
                        MessageType.None);
                });

            DrawCard(
                "Assets",
                () =>
                {
                    DrawAssetRow("EnemySettings", enemySettings);
                    DrawAssetRow("VisionSettings", visionSettings);
                    DrawAssetRow("DoorSettings", doorSettings);
                    DrawAssetRow("CameraSettings", cameraSettings);
                    DrawAssetRow("FootprintSettings", footprintSettings);
                    DrawAssetRow("SanitySettings", sanitySettings);
                    DrawAssetRow("EconomySettings", economySettings);
                    DrawAssetRow("AccessibilitySettings", accessibilitySettings);
                    DrawAssetRow("LocalizationSettings", localizationSettings);
                    DrawAssetRow("DebugSettings", debugSettings);
                });
        }

        private void DrawEnemyPage()
        {
            if (enemySettings == null)
            {
                EditorGUILayout.HelpBox("EnemySettings asset missing.", MessageType.Warning);
                return;
            }

            SerializedObject so = new SerializedObject(enemySettings);
            DrawCard("Movement", () =>
            {
                DrawProp(so, "moveSpeed");
                DrawProp(so, "doorTargetingSpeedMultiplier");
                DrawProp(so, "rotationSpeed");
                DrawProp(so, "stoppingDistance");
                DrawProp(so, "doorApproachDistance");
            });

            DrawCard("Search", () =>
            {
                DrawProp(so, "searchInterval");
                DrawProp(so, "lostSightDelay");
                DrawProp(so, "investigateDuration");
                DrawProp(so, "enterRoomDelay");
                DrawProp(so, "roomMemoryDuration");
                DrawProp(so, "chaseLastKnownRoom");
            });

            DrawCard("Vision", () =>
            {
                DrawProp(so, "viewDistance");
                DrawProp(so, "viewAngle");
                DrawProp(so, "detectionConfirmTime");
                DrawProp(so, "requireOpenWindow");
                DrawProp(so, "windowVisionSampleCount");
                DrawProp(so, "windowVisionSamplePadding");
                DrawProp(so, "windowCheckInterval");
            });

            DrawCard("Attack", () =>
            {
                DrawProp(so, "attackRange");
                DrawProp(so, "attackDamage");
                DrawProp(so, "attackCooldown");
                DrawProp(so, "attackWindupTime");
            });

            DrawCard("Door Attack", () =>
            {
                DrawProp(so, "doorAttackDamage");
                DrawProp(so, "doorAttackInterval");
                DrawProp(so, "doorAttackWindup");
                DrawProp(so, "doorAttackRecovery");
                DrawProp(so, "doorAttackRange");
            });

            DrawCard("Footprints (Placeholder)", () =>
            {
                EditorGUILayout.HelpBox("Footprint tuning is on Footprint page (settings asset exists).", MessageType.None);
            });

            DrawCard("Debug", () =>
            {
                DrawProp(so, "drawVisionCone");
                DrawProp(so, "drawLineOfSight");
                DrawProp(so, "drawWindowVisionSamples");
                DrawProp(so, "logStateChanges");
            });

            so.ApplyModifiedProperties();
        }

        private void DrawVisionPage()
        {
            if (visionSettings == null)
            {
                EditorGUILayout.HelpBox("VisionSettings asset missing.", MessageType.Warning);
                return;
            }

            SerializedObject so = new SerializedObject(visionSettings);
            DrawCard("Detection", () =>
            {
                DrawProp(so, "useVisibilityWorld");
                DrawProp(so, "requireActualVisibilityPolygonContainment");
            });

            DrawCard("Cone Sampling", () =>
            {
                DrawProp(so, "baseRayCount");
                DrawProp(so, "maxRayCount");
                DrawProp(so, "edgeRefinementIterations");
                DrawProp(so, "edgeDistanceThreshold");
            });

            DrawCard("Portals", () =>
            {
                DrawProp(so, "requireOpenWindow");
                DrawProp(so, "windowVisionSampleCount");
                DrawProp(so, "windowVisionSamplePadding");
            });

            DrawCard("Debug", () =>
            {
                DrawProp(so, "debugLogDetectionSource");
            });

            so.ApplyModifiedProperties();
        }

        private void DrawDoorPage()
        {
            if (doorSettings == null)
            {
                EditorGUILayout.HelpBox("DoorSettings asset missing.", MessageType.Warning);
                return;
            }

            SerializedObject so = new SerializedObject(doorSettings);
            DrawCard("Health", () =>
            {
                DrawProp(so, "maxHealth");
                DrawProp(so, "invulnerable");
                DrawProp(so, "destroyDelay");
            });

            DrawCard("Interaction", () =>
            {
                DrawProp(so, "toggleCooldown");
                DrawProp(so, "openAngleDegrees");
                DrawProp(so, "doorwayPassableOpenAmount");
            });

            DrawCard("Animation", () =>
            {
                DrawProp(so, "animateDoor");
                DrawProp(so, "openDuration");
                DrawProp(so, "closeDuration");
                DrawProp(so, "swingCurve");
                DrawProp(so, "useEndWobble");
                DrawProp(so, "endWobbleDuration");
                DrawProp(so, "endWobbleAmplitudeDegrees");
                DrawProp(so, "endWobbleOscillations");
            });

            DrawCard("Visual (Debug Style)", () =>
            {
                DrawProp(so, "includeWallVisual");
                DrawProp(so, "useDefaultWallDebugVisual");
                DrawProp(so, "wallColor");
                DrawProp(so, "wallLineWidth");
                DrawProp(so, "wallDashLength");
                DrawProp(so, "wallGapLength");
            });

            DrawCard("Visibility", () =>
            {
                DrawProp(so, "registerForVisibility");
            });

            so.ApplyModifiedProperties();
        }

        private void DrawCameraPage()
        {
            if (cameraSettings == null)
            {
                EditorGUILayout.HelpBox("CameraSettings asset missing.", MessageType.Warning);
                return;
            }

            SerializedObject so = new SerializedObject(cameraSettings);
            DrawCard("Follow", () =>
            {
                DrawProp(so, "followSmoothTime");
                DrawProp(so, "maxFollowSpeed");
                DrawProp(so, "deadZoneRadius");
                DrawProp(so, "lookAheadDistance");
                DrawProp(so, "lookAheadSmoothTime");
            });

            DrawCard("Overview", () =>
            {
                DrawProp(so, "overviewSmoothTime");
                DrawProp(so, "overviewPadding");
                DrawProp(so, "minOverviewOrthographicSize");
                DrawProp(so, "maxOverviewOrthographicSize");
            });

            DrawCard("Transition", () =>
            {
                DrawProp(so, "defaultTransitionDuration");
                DrawProp(so, "transitionCurve");
            });

            so.ApplyModifiedProperties();
        }

        private void DrawSanityPage()
        {
            if (sanitySettings == null)
            {
                EditorGUILayout.HelpBox("SanitySettings asset missing.", MessageType.Warning);
                return;
            }

            SerializedObject so = new SerializedObject(sanitySettings);
            DrawCard("Recovery / Decay", () =>
            {
                DrawProp(so, "maxSanity");
                DrawProp(so, "startSanity");
                DrawProp(so, "nightOutdoorDrainPerSecond");
                DrawProp(so, "nightIndoorRecoveryPerSecond");
                DrawProp(so, "dayIndoorRecoveryPerSecond");
                DrawProp(so, "dayOutdoorRecoveryPerSecond");
            });

            DrawCard("Damage", () =>
            {
                DrawProp(so, "enemyTouchDamage");
                DrawProp(so, "windowDetectionDamage");
            });

            DrawCard("Death", () =>
            {
                DrawProp(so, "freezeOnDeath");
                DrawProp(so, "deathTint");
                DrawProp(so, "deathFadeDuration");
                DrawProp(so, "deathBlurDownsample");
                DrawProp(so, "deathBlurRadius");
                DrawProp(so, "deathBlurIterations");
            });

            so.ApplyModifiedProperties();
        }

        private void DrawEconomyPage()
        {
            if (economySettings == null)
            {
                EditorGUILayout.HelpBox("EconomySettings asset missing.", MessageType.Warning);
                return;
            }

            SerializedObject so = new SerializedObject(economySettings);
            DrawCard("Costs", () =>
            {
                DrawProp(so, "windowCost");
                DrawProp(so, "doorCost");
                DrawProp(so, "sanityRecoveryCost");
                DrawProp(so, "repairCost");
            });
            so.ApplyModifiedProperties();
        }

        private void DrawAccessibilityPage()
        {
            if (accessibilitySettings == null)
            {
                EditorGUILayout.HelpBox("AccessibilitySettings asset missing.", MessageType.Warning);
                return;
            }

            SerializedObject so = new SerializedObject(accessibilitySettings);
            DrawCard("Contrast", () =>
            {
                DrawProp(so, "gameplayContrast");
                DrawProp(so, "outlineStrength");
                DrawProp(so, "adaptiveBrightness");
            });

            DrawCard("Future", () =>
            {
                DrawProp(so, "reservedForColorBlindSupport");
            });

            so.ApplyModifiedProperties();
        }

        private void DrawLocalizationPage()
        {
            DrawCard("Placeholder", () =>
            {
                EditorGUILayout.HelpBox("Localization Dashboard will be implemented later. Category reserved.", MessageType.None);
                if (localizationSettings != null)
                {
                    SerializedObject so = new SerializedObject(localizationSettings);
                    DrawProp(so, "reserved");
                    so.ApplyModifiedProperties();
                }
            });
        }

        private void DrawDebugPage()
        {
            if (debugSettings == null)
            {
                EditorGUILayout.HelpBox("DebugSettings asset missing.", MessageType.Warning);
                return;
            }

            SerializedObject so = new SerializedObject(debugSettings);
            DrawCard("Enemy", () =>
            {
                DrawProp(so, "drawEnemyVision");
                DrawProp(so, "drawEnemySearchState");
                DrawProp(so, "drawAiState");
            });

            DrawCard("World / Building", () =>
            {
                DrawProp(so, "drawNavigation");
                DrawProp(so, "drawFootprints");
                DrawProp(so, "drawInteractionPoints");
                DrawProp(so, "drawOpenings");
                DrawProp(so, "drawOccluders");
            });

            DrawCard("Logging", () =>
            {
                DrawProp(so, "logEnemySpawns");
                DrawProp(so, "logStateChanges");
            });

            so.ApplyModifiedProperties();
        }

        private void DrawAssetRow(string label, UnityEngine.Object asset)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(160f));
                EditorGUILayout.ObjectField(asset, typeof(UnityEngine.Object), false);
                if (GUILayout.Button("Ping", GUILayout.Width(64f)) && asset != null)
                    EditorGUIUtility.PingObject(asset);
            }
        }

        private void DrawCard(string title, Action contents)
        {
            using (new EditorGUILayout.VerticalScope(card))
            {
                GUILayout.Label(title, cardHeader);
                GUILayout.Space(6f);
                contents?.Invoke();
            }
            GUILayout.Space(10f);
        }

        private void DrawProp(SerializedObject so, string propertyName)
        {
            SerializedProperty prop = so.FindProperty(propertyName);
            if (prop == null)
            {
                EditorGUILayout.HelpBox("Missing property: " + propertyName, MessageType.Warning);
                return;
            }

            DrawPropertyRow(prop);
        }

        private bool TryBuildStyles()
        {
            if (navButton != null)
                return true;

            // EditorStyles are not safe to access during OnEnable/domain reload.
            if (EditorStyles.miniButton == null || EditorStyles.helpBox == null)
                return false;

            navButton = new GUIStyle(EditorStyles.miniButton)
            {
                alignment = TextAnchor.MiddleLeft,
                fixedHeight = 28f,
                fontSize = 12,
                margin = new RectOffset(10, 10, 2, 2),
                padding = new RectOffset(10, 10, 6, 6)
            };

            pageTitle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 20,
                margin = new RectOffset(8, 8, 0, 0)
            };

            card = new GUIStyle(EditorStyles.helpBox)
            {
                margin = new RectOffset(12, 12, 8, 8),
                padding = new RectOffset(14, 14, 12, 14)
            };

            cardHeader = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                margin = new RectOffset(0, 0, 0, 6)
            };

            Color labelColor = EditorStyles.label.normal.textColor;
            if (labelColor.a <= 0.01f)
                labelColor = EditorGUIUtility.isProSkin ? new Color(0.92f, 0.92f, 0.92f) : new Color(0.1f, 0.1f, 0.1f);

            singleLineLabel = new GUIStyle(EditorStyles.label)
            {
                wordWrap = false,
                richText = false,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0)
            };
            singleLineLabel.normal.textColor = labelColor;

            wrapLabel = new GUIStyle(singleLineLabel)
            {
                wordWrap = true,
                alignment = TextAnchor.UpperLeft
            };
            wrapLabel.normal.textColor = labelColor;

            rowBackground = new GUIStyle
            {
                normal = { background = Texture2D.whiteTexture }
            };

            return true;
        }

        private void LoadOrCreateAllAssets()
        {
            enemySettings = LoadOrCreateAsset<EnemySettings>(EnemyAssetPath);
            visionSettings = LoadOrCreateAsset<VisionSettings>(VisionAssetPath);
            doorSettings = LoadOrCreateAsset<DoorSettings>(DoorAssetPath);
            cameraSettings = LoadOrCreateAsset<CameraSettings>(CameraAssetPath);
            footprintSettings = LoadOrCreateAsset<FootprintSettings>(FootprintAssetPath);
            sanitySettings = LoadOrCreateAsset<SanitySettings>(SanityAssetPath);
            economySettings = LoadOrCreateAsset<EconomySettings>(EconomyAssetPath);
            localizationSettings = LoadOrCreateAsset<LocalizationSettings>(LocalizationAssetPath);
            accessibilitySettings = LoadOrCreateAsset<AccessibilitySettings>(AccessibilityAssetPath);
            debugSettings = LoadOrCreateAsset<DebugSettings>(DebugAssetPath);
            buildArchiveSettings = LoadOrCreateAsset<BuildArchiveSettings>(BuildArchiveAssetPath);
        }

        private static T LoadOrCreateAsset<T>(string assetPath) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset != null)
                return asset;

            asset = CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<T>(assetPath);
        }

        private static void EnsureSettingsFolder()
        {
            EnsureFolder("Assets/Curtain");
            EnsureFolder(SettingsFolder);
            EnsureFolder("Assets/Curtain/Editor");
            EnsureFolder("Assets/Curtain/Editor/Dashboard");
            EnsureFolder("Assets/Curtain/Runtime");
            EnsureFolder("Assets/Curtain/Docs");
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
                return;

            string parent = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
            string name = Path.GetFileName(assetPath);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
                return;

            if (!AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, name);
        }

        private void DrawPropertyRow(SerializedProperty prop)
        {
            if (prop == null)
                return;

            float availableWidth = position.width - navWidth - 56f;
            float labelColumnWidth = Mathf.Clamp(availableWidth * 0.44f, 220f, 480f);
            float gutter = 16f;
            float valueColumnWidth = Mathf.Max(180f, availableWidth - labelColumnWidth - gutter);

            GUIContent labelContent = new GUIContent(prop.displayName, DashboardPropertyDocumentation.ResolveTooltip(prop));

            float singleLineLabelHeight = singleLineLabel.CalcHeight(labelContent, labelColumnWidth);
            float wrappedLabelHeight = wrapLabel.CalcHeight(labelContent, labelColumnWidth);
            bool useWrappedLabel = wrappedLabelHeight > singleLineLabelHeight + 1f;

            GUIStyle labelStyle = useWrappedLabel ? wrapLabel : singleLineLabel;
            float labelHeight = useWrappedLabel ? wrappedLabelHeight : EditorGUIUtility.singleLineHeight;
            bool includeChildren = prop.hasVisibleChildren && prop.propertyType != SerializedPropertyType.Float &&
                                   prop.propertyType != SerializedPropertyType.Integer &&
                                   prop.propertyType != SerializedPropertyType.Boolean &&
                                   prop.propertyType != SerializedPropertyType.Enum;
            float fieldHeight = EditorGUI.GetPropertyHeight(prop, includeChildren: includeChildren);
            float rowHeight = Mathf.Max(labelHeight, fieldHeight) + (useWrappedLabel ? 8f : 4f);

            Rect rowRect = EditorGUILayout.GetControlRect(false, rowHeight);

            Color tint = EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.03f) : new Color(0f, 0f, 0f, 0.035f);
            if ((prop.propertyPath.GetHashCode() & 1) == 0)
                EditorGUI.DrawRect(rowRect, tint);

            float labelY = useWrappedLabel
                ? rowRect.y + 3f
                : rowRect.y + (rowRect.height - labelHeight) * 0.5f;
            Rect labelRect = new Rect(rowRect.x, labelY, labelColumnWidth, labelHeight);

            float fieldY = rowRect.y + (rowRect.height - fieldHeight) * 0.5f;
            Rect fieldRect = new Rect(labelRect.xMax + gutter, fieldY, valueColumnWidth, fieldHeight);

            EditorGUI.LabelField(labelRect, labelContent, labelStyle);
            EditorGUI.PropertyField(fieldRect, prop, GUIContent.none, includeChildren: includeChildren);
        }

        private void DrawRelatedAssetsSection()
        {
            showRelatedAssets = EditorGUILayout.BeginFoldoutHeaderGroup(showRelatedAssets, "Related Assets");
            if (showRelatedAssets)
            {
                GUILayout.Space(6f);
                using (new EditorGUILayout.VerticalScope(card))
                {
                    List<RelatedAssetEntry> entries = new List<RelatedAssetEntry>(GetRelatedAssetsForPage(currentPage));
                    if (entries.Count == 0)
                    {
                        EditorGUILayout.LabelField("No related assets configured for this page.", EditorStyles.miniLabel);
                    }
                    else
                    {
                        for (int i = 0; i < entries.Count; i++)
                        {
                            DrawRelatedAssetEntry(entries[i]);
                            if (i < entries.Count - 1)
                                GUILayout.Space(4f);
                        }
                    }
                }
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
            GUILayout.Space(10f);
        }

        private System.Collections.Generic.IEnumerable<RelatedAssetEntry> GetRelatedAssetsForPage(Page page)
        {
            // Always show settings assets for relevant pages.
            switch (page)
            {
                case Page.Gameplay:
                    yield return new RelatedAssetEntry(RelatedAssetKind.Folder, "Assets/Curtain/Settings/", AssetDatabase.LoadAssetAtPath<DefaultAsset>(SettingsFolder));
                    yield return new RelatedAssetEntry(RelatedAssetKind.ScriptableObject, "EnemySettings.asset", enemySettings);
                    yield return new RelatedAssetEntry(RelatedAssetKind.ScriptableObject, "VisionSettings.asset", visionSettings);
                    yield return new RelatedAssetEntry(RelatedAssetKind.ScriptableObject, "DoorSettings.asset", doorSettings);
                    yield return new RelatedAssetEntry(RelatedAssetKind.ScriptableObject, "CameraSettings.asset", cameraSettings);
                    yield return new RelatedAssetEntry(RelatedAssetKind.ScriptableObject, "FootprintSettings.asset", footprintSettings);
                    yield return new RelatedAssetEntry(RelatedAssetKind.ScriptableObject, "SanitySettings.asset", sanitySettings);
                    yield return new RelatedAssetEntry(RelatedAssetKind.ScriptableObject, "EconomySettings.asset", economySettings);
                    yield return new RelatedAssetEntry(RelatedAssetKind.ScriptableObject, "AccessibilitySettings.asset", accessibilitySettings);
                    yield return new RelatedAssetEntry(RelatedAssetKind.ScriptableObject, "LocalizationSettings.asset", localizationSettings);
                    yield return new RelatedAssetEntry(RelatedAssetKind.ScriptableObject, "DebugSettings.asset", debugSettings);
                    break;

                case Page.Enemy:
                    yield return new RelatedAssetEntry(RelatedAssetKind.ScriptableObject, "EnemySettings.asset", enemySettings);
                    yield return ScriptEntry("EnemyController.cs", "Assets/Scripts/Enemy/EnemyController.cs");
                    yield return ScriptEntry("EnemyVision.cs", "Assets/Scripts/Enemy/EnemyVision.cs");
                    yield return ScriptEntry("EnemyFootprintTrace.cs", "Assets/Scripts/Enemy/Visual/EnemyFootprintTrace.cs");
                    yield return new RelatedAssetEntry(RelatedAssetKind.SceneObject, "EnemyController (Scene)", typeof(EnemyController));
                    break;

                case Page.Vision:
                    yield return new RelatedAssetEntry(RelatedAssetKind.ScriptableObject, "VisionSettings.asset", visionSettings);
                    yield return ScriptEntry("VisibilityWorld.cs", "Assets/Scripts/Enemy/Vision/Gameplay/VisibilityWorld.cs");
                    yield return ScriptEntry("VisionSensor2D.cs", "Assets/Scripts/Enemy/Vision/Gameplay/VisionSensor2D.cs");
                    yield return ScriptEntry("ProceduralMeshVisionRenderer.cs", "Assets/Scripts/Enemy/Vision/Rendering/Mesh/ProceduralMeshVisionRenderer.cs");
                    yield return ScriptEntry("VisionDebugView2D.cs", "Assets/Scripts/Enemy/Vision/Debug/VisionDebugView2D.cs");
                    yield return new RelatedAssetEntry(RelatedAssetKind.SceneObject, "VisionSensor2D (Scene)", typeof(VisionSensor2D));
                    break;

                case Page.Door:
                    yield return new RelatedAssetEntry(RelatedAssetKind.ScriptableObject, "DoorSettings.asset", doorSettings);
                    yield return ScriptEntry("RuntimeTileMeshFusionDoor.cs", "Assets/Scripts/RuntimeTileMesh/RuntimeTileMeshFusionDoor.cs");
                    yield return ScriptEntry("CombatHealth.cs", "Assets/Scripts/Combat/CombatHealth.cs");
                    yield return ScriptEntry("ImpactObjectFeedback.cs", "Assets/Scripts/Combat/DamageReceiverFeedback.cs");
                    yield return new RelatedAssetEntry(RelatedAssetKind.SceneObject, "Fusion Door (Scene)", typeof(RuntimeTileMeshFusionDoor));
                    break;

                case Page.Camera:
                    yield return new RelatedAssetEntry(RelatedAssetKind.ScriptableObject, "CameraSettings.asset", cameraSettings);
                    yield return ScriptEntry("FusionModeCameraRig.cs", "Assets/Scripts/RuntimeTileMesh/FusionModeCameraRig.cs");
                    yield return ScriptEntry("ImpactCameraFeedback.cs", "Assets/Scripts/Combat/ImpactCameraFeedback.cs");
                    yield return new RelatedAssetEntry(RelatedAssetKind.SceneObject, "FusionModeCameraRig (Scene)", typeof(FusionModeCameraRig));
                    break;

                case Page.Sanity:
                    yield return new RelatedAssetEntry(RelatedAssetKind.ScriptableObject, "SanitySettings.asset", sanitySettings);
                    yield return ScriptEntry("FusionSanityController.cs", "Assets/Scripts/RuntimeTileMesh/FusionSanityController.cs");
                    yield return ScriptEntry("SanitySystem.cs (Legacy)", "Assets/Scripts/SanitySystem.cs");
                    yield return new RelatedAssetEntry(RelatedAssetKind.SceneObject, "FusionSanityController (Scene)", typeof(FusionSanityController));
                    break;

                case Page.Economy:
                    yield return new RelatedAssetEntry(RelatedAssetKind.ScriptableObject, "EconomySettings.asset", economySettings);
                    yield return ScriptEntry("TimeCounterUI.cs", "Assets/Scripts/TimeCounterUI.cs");
                    yield return ScriptEntry("HoverScrollColorLerp2D.cs (Legacy)", "Assets/Scripts/HoverScrollColorLerp2D.cs");
                    break;

                case Page.Accessibility:
                    yield return new RelatedAssetEntry(RelatedAssetKind.ScriptableObject, "AccessibilitySettings.asset", accessibilitySettings);
                    yield return ScriptEntry("GameplayVisualRenderer.cs", "Assets/Scripts/GameplayVisuals/GameplayVisualRenderer.cs");
                    break;

                case Page.Localization:
                    yield return new RelatedAssetEntry(RelatedAssetKind.ScriptableObject, "LocalizationSettings.asset", localizationSettings);
                    yield return ScriptEntry("DuoCurtainLocalization.cs", "Assets/Scripts/UI/DuoCurtainLocalization.cs");
                    yield return ScriptEntry("LocalizedText.cs", "Assets/Scripts/UI/LocalizedText.cs");
                    break;

                case Page.Debug:
                    yield return new RelatedAssetEntry(RelatedAssetKind.ScriptableObject, "DebugSettings.asset", debugSettings);
                    yield return ScriptEntry("CombatDebugOverlay.cs", "Assets/Scripts/Combat/CombatDebugOverlay.cs");
                    yield return ScriptEntry("RuntimeTileMeshBlockInfoOverlay.cs", "Assets/Scripts/RuntimeTileMesh/RuntimeTileMeshBlockInfoOverlay.cs");
                    break;
            }
        }

        private static RelatedAssetEntry ScriptEntry(string label, string scriptPath)
        {
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
            return new RelatedAssetEntry(RelatedAssetKind.Script, label, script);
        }

        private void DrawRelatedAssetEntry(RelatedAssetEntry entry)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUIContent icon = GetIcon(entry);
                GUILayout.Label(icon, GUILayout.Width(18f), GUILayout.Height(18f));

                if (entry.kind == RelatedAssetKind.SceneObject)
                {
                    UnityEngine.Object sceneObject = FindSceneObject(entry.sceneObjectType, entry.sceneObjectNameHint);
                    using (new EditorGUI.DisabledScope(sceneObject == null))
                    {
                        if (GUILayout.Button(entry.label, EditorStyles.linkLabel))
                        {
                            SelectAndFrameSceneObject(sceneObject);
                        }
                    }
                    GUILayout.FlexibleSpace();
                    return;
                }

                UnityEngine.Object asset = entry.asset;
                using (new EditorGUI.DisabledScope(asset == null))
                {
                    if (GUILayout.Button(entry.label, EditorStyles.linkLabel))
                    {
                        ActivateAsset(entry.kind, asset);
                    }
                }
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(asset == null))
                {
                    if (GUILayout.Button("Ping", GUILayout.Width(52f)))
                        EditorGUIUtility.PingObject(asset);
                    if (GUILayout.Button("Select", GUILayout.Width(58f)))
                        Selection.activeObject = asset;
                }
            }
        }

        private static GUIContent GetIcon(RelatedAssetEntry entry)
        {
            switch (entry.kind)
            {
                case RelatedAssetKind.ScriptableObject:
                    return EditorGUIUtility.IconContent("ScriptableObject Icon");
                case RelatedAssetKind.Prefab:
                    return EditorGUIUtility.IconContent("Prefab Icon");
                case RelatedAssetKind.Script:
                    return EditorGUIUtility.IconContent("cs Script Icon");
                case RelatedAssetKind.SceneObject:
                    return EditorGUIUtility.IconContent("GameObject Icon");
                case RelatedAssetKind.Folder:
                    return EditorGUIUtility.IconContent("Folder Icon");
                default:
                    return EditorGUIUtility.IconContent("DefaultAsset Icon");
            }
        }

        private static void ActivateAsset(RelatedAssetKind kind, UnityEngine.Object asset)
        {
            if (asset == null)
                return;

            if (kind == RelatedAssetKind.Script && asset is MonoScript script)
            {
                AssetDatabase.OpenAsset(script);
                return;
            }

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        private static UnityEngine.Object FindSceneObject(Type type, string nameHint)
        {
            if (type == null)
                return null;

            // Prefer type search.
            UnityEngine.Object found = UnityEngine.Object.FindFirstObjectByType(type, FindObjectsInactive.Exclude);
            if (found != null)
                return found;

            if (!string.IsNullOrWhiteSpace(nameHint))
                return GameObject.Find(nameHint);

            return null;
        }

        private static void SelectAndFrameSceneObject(UnityEngine.Object sceneObject)
        {
            if (sceneObject == null)
                return;

            Selection.activeObject = sceneObject;
            EditorGUIUtility.PingObject(sceneObject);

            SceneView view = SceneView.lastActiveSceneView;
            if (view != null)
                view.FrameSelected();
        }
    }
}


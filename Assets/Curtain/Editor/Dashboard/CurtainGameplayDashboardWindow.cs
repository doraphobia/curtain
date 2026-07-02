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
            Footprint,
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
        private GUIStyle descriptionLabel;
        private GUIStyle relatedAssetKindLabel;
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
                    DrawNavButton(Page.Footprint, "Footprint");
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
                        case Page.Footprint:
                            DrawFootprintPage();
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
                        "Edit values here; connected systems live-tune immediately in Editor Play Mode.\n" +
                        "Shipping builds keep using scene/prefab inline values so editor-only settings do not enter Player Data.",
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
                EditorGUILayout.HelpBox("Open the Footprint page for footprint lifetime, spacing, and fade tuning.", MessageType.None);
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

        private void DrawFootprintPage()
        {
            if (footprintSettings == null)
            {
                EditorGUILayout.HelpBox("FootprintSettings asset missing.", MessageType.Warning);
                return;
            }

            SerializedObject so = new SerializedObject(footprintSettings);
            DrawCard("Lifetime", () =>
            {
                DrawProp(so, "lifetimeSeconds");
                DrawProp(so, "fadeSeconds");
            });

            DrawCard("Spacing", () =>
            {
                DrawProp(so, "spacing");
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
            GUIContent labelContent = new GUIContent(label);
            float estimatedWidth = Mathf.Max(340f, position.width - navWidth - 112f);
            float labelWidth = Mathf.Clamp(estimatedWidth * 0.35f, 160f, 280f);
            float labelHeight = Mathf.Max(EditorGUIUtility.singleLineHeight, wrapLabel.CalcHeight(labelContent, labelWidth));
            float rowHeight = Mathf.Max(labelHeight, EditorGUIUtility.singleLineHeight + 8f);
            Rect rowRect = EditorGUILayout.GetControlRect(false, rowHeight);

            Rect labelRect = new Rect(rowRect.x, rowRect.y + 4f, labelWidth, labelHeight);
            Rect fieldRect = new Rect(labelRect.xMax + 12f, rowRect.y + 2f, Mathf.Max(140f, rowRect.width - labelWidth - 168f), EditorGUIUtility.singleLineHeight);
            Rect pingRect = new Rect(fieldRect.xMax + 8f, rowRect.y + 1f, 64f, EditorGUIUtility.singleLineHeight + 2f);
            Rect selectRect = new Rect(pingRect.xMax + 6f, rowRect.y + 1f, 72f, EditorGUIUtility.singleLineHeight + 2f);

            EditorGUI.LabelField(labelRect, labelContent, wrapLabel);
            EditorGUI.ObjectField(fieldRect, asset, typeof(UnityEngine.Object), false);
            using (new EditorGUI.DisabledScope(asset == null))
            {
                if (GUI.Button(pingRect, "Ping"))
                    EditorGUIUtility.PingObject(asset);
                if (GUI.Button(selectRect, "Select"))
                {
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset);
                }
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
                margin = new RectOffset(12, 12, 10, 10),
                padding = new RectOffset(16, 16, 14, 16)
            };

            cardHeader = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                margin = new RectOffset(0, 0, 0, 8)
            };

            Color labelColor = EditorStyles.label.normal.textColor;
            if (labelColor.a <= 0.01f)
                labelColor = EditorGUIUtility.isProSkin ? new Color(0.92f, 0.92f, 0.92f) : new Color(0.1f, 0.1f, 0.1f);

            singleLineLabel = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true,
                richText = false,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Overflow,
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

            descriptionLabel = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
            {
                margin = new RectOffset(0, 0, 2, 0),
                padding = new RectOffset(0, 0, 0, 0),
                wordWrap = true,
                clipping = TextClipping.Overflow
            };

            relatedAssetKindLabel = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold,
                clipping = TextClipping.Overflow,
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0)
            };

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

            GUIContent labelContent = DashboardPropertyDocumentation.BuildLabelContent(prop);
            bool includeChildren = prop.hasVisibleChildren && prop.propertyType != SerializedPropertyType.Float &&
                                   prop.propertyType != SerializedPropertyType.Integer &&
                                   prop.propertyType != SerializedPropertyType.Boolean &&
                                   prop.propertyType != SerializedPropertyType.Enum;

            float estimatedWidth = Mathf.Max(340f, position.width - navWidth - 112f);
            CalculatePropertyColumns(estimatedWidth, out float labelColumnWidth, out float valueColumnWidth, out float gutter);

            float labelHeight = Mathf.Max(EditorGUIUtility.singleLineHeight, wrapLabel.CalcHeight(labelContent, labelColumnWidth));
            float fieldHeight = EditorGUI.GetPropertyHeight(prop, GUIContent.none, includeChildren: includeChildren);
            float rowHeight = Mathf.Max(labelHeight, fieldHeight) + 10f;

            Rect rowRect = EditorGUILayout.GetControlRect(false, rowHeight);
            CalculatePropertyColumns(rowRect.width, out labelColumnWidth, out valueColumnWidth, out gutter);

            Color tint = EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.03f) : new Color(0f, 0f, 0f, 0.035f);
            if ((prop.propertyPath.GetHashCode() & 1) == 0)
                EditorGUI.DrawRect(rowRect, tint);

            float labelY = rowRect.y + 5f;
            Rect labelRect = new Rect(rowRect.x, labelY, labelColumnWidth, labelHeight);

            float fieldY = rowRect.y + (rowRect.height - fieldHeight) * 0.5f;
            Rect fieldRect = new Rect(labelRect.xMax + gutter, fieldY, valueColumnWidth, fieldHeight);

            EditorGUI.LabelField(labelRect, labelContent, wrapLabel);
            EditorGUI.PropertyField(fieldRect, prop, GUIContent.none, includeChildren: includeChildren);
        }

        private static void CalculatePropertyColumns(float totalWidth, out float labelWidth, out float valueWidth, out float gutter)
        {
            totalWidth = Mathf.Max(320f, totalWidth);
            gutter = totalWidth < 520f ? 12f : 18f;
            valueWidth = Mathf.Clamp(totalWidth * 0.42f, 180f, 340f);
            if (totalWidth < 520f)
                valueWidth = Mathf.Clamp(totalWidth * 0.46f, 160f, 220f);

            labelWidth = Mathf.Max(128f, totalWidth - valueWidth - gutter);
        }

        private void DrawRelatedAssetsSection()
        {
            showRelatedAssets = EditorGUILayout.BeginFoldoutHeaderGroup(showRelatedAssets, "Related Assets");
            if (showRelatedAssets)
            {
                GUILayout.Space(6f);
                using (new EditorGUILayout.VerticalScope(card))
                {
                    EditorGUILayout.LabelField(
                        "Quick navigation for tuning assets, scripts, prefabs, folders, and scene objects related to this page.",
                        descriptionLabel);
                    GUILayout.Space(8f);

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
                                GUILayout.Space(5f);
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
                    yield return new RelatedAssetEntry(RelatedAssetKind.ScriptableObject, "FootprintSettings.asset", footprintSettings);
                    yield return PrefabEntry("Floorplan (2).prefab", "Assets/Frefab/Floorplan (2).prefab");
                    yield return ScriptEntry("EnemyController.cs", "Assets/Scripts/Enemy/EnemyController.cs");
                    yield return ScriptEntry("EnemyVision.cs", "Assets/Scripts/Enemy/EnemyVision.cs");
                    yield return ScriptEntry("EnemyFootprintTrace.cs", "Assets/Scripts/Enemy/Visual/EnemyFootprintTrace.cs");
                    yield return ScriptEntry("FusionNightFootprintEnemy.cs", "Assets/Scripts/RuntimeTileMesh/FusionNightFootprintEnemy.cs");
                    yield return ScriptEntry("EnemyFootstepAudio.cs", "Assets/Scripts/Enemy/Audio/EnemyFootstepAudio.cs");
                    yield return new RelatedAssetEntry(RelatedAssetKind.SceneObject, "Night Enemy Spawner (Scene)", typeof(FusionNightEnemySpawner));
                    yield return new RelatedAssetEntry(RelatedAssetKind.SceneObject, "EnemyController (Scene)", typeof(EnemyController));
                    break;

                case Page.Vision:
                    yield return new RelatedAssetEntry(RelatedAssetKind.ScriptableObject, "VisionSettings.asset", visionSettings);
                    yield return PrefabEntry("Window Area LEFT.prefab", "Assets/Frefab/windowareaLEFT.prefab");
                    yield return PrefabEntry("Window Area RIGHT.prefab", "Assets/Frefab/windowareaRIGHT.prefab");
                    yield return ScriptEntry("VisibilityWorld.cs", "Assets/Scripts/Enemy/Vision/Gameplay/VisibilityWorld.cs");
                    yield return ScriptEntry("VisionSensor2D.cs", "Assets/Scripts/Enemy/Vision/Gameplay/VisionSensor2D.cs");
                    yield return ScriptEntry("WindowPortal.cs", "Assets/Scripts/Windows/WindowPortal.cs");
                    yield return ScriptEntry("ProceduralMeshVisionRenderer.cs", "Assets/Scripts/Enemy/Vision/Rendering/Mesh/ProceduralMeshVisionRenderer.cs");
                    yield return ScriptEntry("VisionDebugView2D.cs", "Assets/Scripts/Enemy/Vision/Debug/VisionDebugView2D.cs");
                    yield return new RelatedAssetEntry(RelatedAssetKind.SceneObject, "VisionSensor2D (Scene)", typeof(VisionSensor2D));
                    yield return new RelatedAssetEntry(RelatedAssetKind.SceneObject, "VisibilityWorld (Scene)", typeof(VisibilityWorld));
                    break;

                case Page.Door:
                    yield return new RelatedAssetEntry(RelatedAssetKind.ScriptableObject, "DoorSettings.asset", doorSettings);
                    yield return PrefabEntry("Window Area LEFT.prefab", "Assets/Frefab/windowareaLEFT.prefab");
                    yield return PrefabEntry("Window Area RIGHT.prefab", "Assets/Frefab/windowareaRIGHT.prefab");
                    yield return ScriptEntry("RuntimeTileMeshFusionDoor.cs", "Assets/Scripts/RuntimeTileMesh/RuntimeTileMeshFusionDoor.cs");
                    yield return ScriptEntry("BreakableExteriorDoor.cs", "Assets/Scripts/Doors/BreakableExteriorDoor.cs");
                    yield return ScriptEntry("CombatHealth.cs", "Assets/Scripts/Combat/CombatHealth.cs");
                    yield return ScriptEntry("DoorBreakProgressBar.cs", "Assets/Scripts/Doors/DoorBreakProgressBar.cs");
                    yield return ScriptEntry("ImpactObjectFeedback.cs", "Assets/Scripts/Combat/DamageReceiverFeedback.cs");
                    yield return new RelatedAssetEntry(RelatedAssetKind.SceneObject, "Fusion Door (Scene)", typeof(RuntimeTileMeshFusionDoor));
                    break;

                case Page.Camera:
                    yield return new RelatedAssetEntry(RelatedAssetKind.ScriptableObject, "CameraSettings.asset", cameraSettings);
                    yield return PrefabEntry("Fusion_PlayerCamera.prefab", "Assets/Fusion/Prefabs/Fusion_PlayerCamera.prefab");
                    yield return PrefabEntry("Fusion_ManagementCamera.prefab", "Assets/Fusion/Prefabs/Fusion_ManagementCamera.prefab");
                    yield return ScriptEntry("FusionModeCameraRig.cs", "Assets/Scripts/RuntimeTileMesh/FusionModeCameraRig.cs");
                    yield return ScriptEntry("ImpactCameraFeedback.cs", "Assets/Scripts/Combat/ImpactCameraFeedback.cs");
                    yield return ScriptEntry("FusionBackgroundShaderController.cs", "Assets/Scripts/RuntimeTileMesh/FusionBackgroundShaderController.cs");
                    yield return new RelatedAssetEntry(RelatedAssetKind.SceneObject, "FusionModeCameraRig (Scene)", typeof(FusionModeCameraRig));
                    break;

                case Page.Sanity:
                    yield return new RelatedAssetEntry(RelatedAssetKind.ScriptableObject, "SanitySettings.asset", sanitySettings);
                    yield return ScriptEntry("FusionSanityController.cs", "Assets/Scripts/RuntimeTileMesh/FusionSanityController.cs");
                    yield return ScriptEntry("PlayerSanityDamageable.cs", "Assets/Scripts/Enemy/PlayerSanityDamageable.cs");
                    yield return ScriptEntry("SanitySystem.cs (Legacy)", "Assets/Scripts/SanitySystem.cs");
                    yield return new RelatedAssetEntry(RelatedAssetKind.SceneObject, "FusionSanityController (Scene)", typeof(FusionSanityController));
                    break;

                case Page.Footprint:
                    yield return new RelatedAssetEntry(RelatedAssetKind.ScriptableObject, "FootprintSettings.asset", footprintSettings);
                    yield return ScriptEntry("EnemyFootprintTrace.cs", "Assets/Scripts/Enemy/Visual/EnemyFootprintTrace.cs");
                    yield return ScriptEntry("FootprintInstance.cs", "Assets/Scripts/Enemy/Visual/FootprintInstance.cs");
                    yield return ScriptEntry("FootprintVisualProfile.cs", "Assets/Scripts/Enemy/Visual/FootprintVisualProfile.cs");
                    yield return ScriptEntry("PrefabFootprintRenderer.cs", "Assets/Scripts/Enemy/Visual/PrefabFootprintRenderer.cs");
                    yield return new RelatedAssetEntry(RelatedAssetKind.SceneObject, "EnemyFootprintTrace (Scene)", typeof(EnemyFootprintTrace));
                    break;

                case Page.Economy:
                    yield return new RelatedAssetEntry(RelatedAssetKind.ScriptableObject, "EconomySettings.asset", economySettings);
                    yield return ScriptEntry("TileShopPanelUI.cs", "Assets/Scripts/TileShopPanelUI.cs");
                    yield return ScriptEntry("TimeCounterUI.cs", "Assets/Scripts/TimeCounterUI.cs");
                    yield return ScriptEntry("HoverScrollColorLerp2D.cs (Legacy)", "Assets/Scripts/HoverScrollColorLerp2D.cs");
                    yield return new RelatedAssetEntry(RelatedAssetKind.SceneObject, "TimeCounterUI (Scene)", typeof(TimeCounterUI));
                    yield return new RelatedAssetEntry(RelatedAssetKind.SceneObject, "TileShopPanelUI (Scene)", typeof(TileShopPanelUI));
                    break;

                case Page.Accessibility:
                    yield return new RelatedAssetEntry(RelatedAssetKind.ScriptableObject, "AccessibilitySettings.asset", accessibilitySettings);
                    yield return ScriptEntry("GameplayVisualRenderer.cs", "Assets/Scripts/GameplayVisuals/GameplayVisualRenderer.cs");
                    yield return ScriptEntry("GameplayVisualSystem.cs", "Assets/Scripts/GameplayVisuals/GameplayVisualSystem.cs");
                    yield return ScriptEntry("GameplayVisualProfile.cs", "Assets/Scripts/GameplayVisuals/GameplayVisualProfile.cs");
                    yield return ScriptEntry("CjkUiFontUtility.cs", "Assets/Scripts/UI/CjkUiFontUtility.cs");
                    break;

                case Page.Localization:
                    yield return new RelatedAssetEntry(RelatedAssetKind.ScriptableObject, "LocalizationSettings.asset", localizationSettings);
                    yield return ScriptEntry("DuoCurtainLocalization.cs", "Assets/Scripts/UI/DuoCurtainLocalization.cs");
                    yield return ScriptEntry("LocalizedText.cs", "Assets/Scripts/UI/LocalizedText.cs");
                    yield return new RelatedAssetEntry(RelatedAssetKind.SceneObject, "LocalizedText (Scene)", typeof(LocalizedText));
                    break;

                case Page.Debug:
                    yield return new RelatedAssetEntry(RelatedAssetKind.ScriptableObject, "DebugSettings.asset", debugSettings);
                    yield return ScriptEntry("CombatDebugOverlay.cs", "Assets/Scripts/Combat/CombatDebugOverlay.cs");
                    yield return ScriptEntry("RuntimeTileMeshBlockInfoOverlay.cs", "Assets/Scripts/RuntimeTileMesh/RuntimeTileMeshBlockInfoOverlay.cs");
                    yield return ScriptEntry("RuntimeTileMeshFusionIntegrityMonitor.cs", "Assets/Scripts/RuntimeTileMesh/RuntimeTileMeshFusionIntegrityMonitor.cs");
                    yield return new RelatedAssetEntry(RelatedAssetKind.SceneObject, "RuntimeTileMeshBlockInfoOverlay (Scene)", typeof(RuntimeTileMeshBlockInfoOverlay));
                    break;
            }
        }

        private static RelatedAssetEntry ScriptEntry(string label, string scriptPath)
        {
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
            return new RelatedAssetEntry(RelatedAssetKind.Script, label, script);
        }

        private static RelatedAssetEntry PrefabEntry(string label, string prefabPath)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            return new RelatedAssetEntry(RelatedAssetKind.Prefab, label, prefab);
        }

        private void DrawRelatedAssetEntry(RelatedAssetEntry entry)
        {
            using (new EditorGUILayout.HorizontalScope(GUILayout.MinHeight(24f)))
            {
                GUILayout.Label(GetKindTag(entry.kind), relatedAssetKindLabel, GUILayout.Width(64f), GUILayout.Height(20f));

                if (entry.kind == RelatedAssetKind.SceneObject)
                {
                    UnityEngine.Object sceneObject = FindSceneObject(entry.sceneObjectType, entry.sceneObjectNameHint);
                    using (new EditorGUI.DisabledScope(sceneObject == null))
                    {
                        if (GUILayout.Button(entry.label, EditorStyles.linkLabel, GUILayout.MinWidth(160f)))
                        {
                            SelectAndFrameSceneObject(sceneObject);
                        }
                    }
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(sceneObject == null))
                    {
                        if (GUILayout.Button("Frame", GUILayout.Width(58f)))
                            SelectAndFrameSceneObject(sceneObject);
                    }
                    return;
                }

                UnityEngine.Object asset = entry.asset;
                using (new EditorGUI.DisabledScope(asset == null))
                {
                    if (GUILayout.Button(entry.label, EditorStyles.linkLabel, GUILayout.MinWidth(160f)))
                    {
                        ActivateAsset(entry.kind, asset);
                    }
                }
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(asset == null))
                {
                    if (entry.kind == RelatedAssetKind.Script)
                    {
                        if (GUILayout.Button("Open", GUILayout.Width(58f)))
                            ActivateAsset(entry.kind, asset);
                    }
                    else
                    {
                        if (GUILayout.Button("Ping", GUILayout.Width(52f)))
                            EditorGUIUtility.PingObject(asset);
                        if (GUILayout.Button("Select", GUILayout.Width(58f)))
                        {
                            Selection.activeObject = asset;
                            EditorGUIUtility.PingObject(asset);
                        }
                    }
                }
            }
        }

        private static string GetKindTag(RelatedAssetKind kind)
        {
            switch (kind)
            {
                case RelatedAssetKind.ScriptableObject:
                    return "[SO]";
                case RelatedAssetKind.Prefab:
                    return "[Prefab]";
                case RelatedAssetKind.Script:
                    return "[Script]";
                case RelatedAssetKind.SceneObject:
                    return "[Scene]";
                case RelatedAssetKind.Folder:
                    return "[Folder]";
                default:
                    return "[Asset]";
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
            if (view == null && SceneView.sceneViews != null && SceneView.sceneViews.Count > 0)
                view = SceneView.sceneViews[0] as SceneView;
            if (view != null)
                view.FrameSelected();
        }
    }
}


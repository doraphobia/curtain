#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Curtain.Settings;
using DuoCurtain.Editor;
using UnityEditor;
using UnityEngine;

namespace Curtain.Editor.Dashboard
{
    internal static class CurtainDashboardBuildsPage
    {
        private static bool showLatest = true;
        private static bool showArchives = true;
        private static List<DuoCurtainBuildArchiveService.BuildRecord> cachedRecords;
        private static double lastScanTime;

        internal static void DrawBuildsPage(BuildArchiveSettings settings)
        {
            if (settings == null)
            {
                EditorGUILayout.HelpBox("BuildArchiveSettings asset is missing. It will be created automatically when the Dashboard loads.", MessageType.Warning);
                return;
            }

            EditorGUILayout.HelpBox(
                "Latest builds live in Builds/Curtain_Mac, Builds/Curtain_Windows, and Builds/Curtain_Web. " +
                "Older packages are moved into each platform's Archive folder and trimmed to the retention limit below.",
                MessageType.Info);

            DrawRetentionCard(settings);
            GUILayout.Space(8f);
            DrawActionsRow(settings);
            GUILayout.Space(8f);
            DrawBuildLists();
        }

        private static void DrawRetentionCard(BuildArchiveSettings settings)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Archive Retention", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                int maxArchives = EditorGUILayout.IntField("Max Archives Per Platform", settings.maxArchivesPerPlatform);
                if (EditorGUI.EndChangeCheck())
                {
                    settings.maxArchivesPerPlatform = Mathf.Max(1, maxArchives);
                    EditorUtility.SetDirty(settings);
                    AssetDatabase.SaveAssets();
                }

                EditorGUILayout.LabelField(
                    "When a new build is published, the previous package is archived, then only the newest " +
                    settings.maxArchivesPerPlatform + " archive folders are kept per platform.",
                    EditorStyles.wordWrappedMiniLabel);
            }
        }

        private static void DrawActionsRow(BuildArchiveSettings settings)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh", GUILayout.Width(120f)))
                    ForceRefresh();

                if (GUILayout.Button("Prune Archives Now", GUILayout.Width(160f)))
                {
                    DuoCurtainBuildArchiveService.PruneAllPlatforms();
                    ForceRefresh();
                }

                if (GUILayout.Button("Reveal Builds Root", GUILayout.Width(160f)))
                {
                    string path = DuoCurtainBuildArchiveService.GetOutputRootPath();
                    if (!Directory.Exists(path))
                        Directory.CreateDirectory(path);
                    DuoCurtainBuildArchiveService.RevealInFileBrowser(path);
                }

                if (GUILayout.Button("Build All Platforms", GUILayout.Width(160f)))
                    EditorApplication.ExecuteMenuItem("Tools/Duo Curtain/Build/Build All Platforms");
            }
        }

        private static void DrawBuildLists()
        {
            EnsureScanned();

            showLatest = EditorGUILayout.BeginFoldoutHeaderGroup(showLatest, "Latest Builds");
            if (showLatest)
            {
                GUILayout.Space(4f);
                bool anyLatest = false;
                for (int i = 0; i < cachedRecords.Count; i++)
                {
                    DuoCurtainBuildArchiveService.BuildRecord record = cachedRecords[i];
                    if (!record.isLatest)
                        continue;

                    anyLatest = true;
                    DrawBuildRecord(record);
                }

                if (!anyLatest)
                    EditorGUILayout.LabelField("No latest builds found yet.", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            GUILayout.Space(8f);

            showArchives = EditorGUILayout.BeginFoldoutHeaderGroup(showArchives, "Archived Builds");
            if (showArchives)
            {
                GUILayout.Space(4f);
                bool anyArchive = false;
                for (int i = 0; i < cachedRecords.Count; i++)
                {
                    DuoCurtainBuildArchiveService.BuildRecord record = cachedRecords[i];
                    if (record.isLatest)
                        continue;

                    anyArchive = true;
                    DrawBuildRecord(record);
                }

                if (!anyArchive)
                    EditorGUILayout.LabelField("No archived builds found.", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private static void DrawBuildRecord(DuoCurtainBuildArchiveService.BuildRecord record)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    string roleLabel = record.isLatest ? "Latest" : "Archive";
                    EditorGUILayout.LabelField(record.displayName + " · " + roleLabel, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Reveal", GUILayout.Width(72f)))
                        DuoCurtainBuildArchiveService.RevealInFileBrowser(record.primaryArtifactPath);
                    if (GUILayout.Button("Folder", GUILayout.Width(72f)))
                        DuoCurtainBuildArchiveService.RevealInFileBrowser(record.folderPath);
                }

                EditorGUILayout.LabelField("Time (UTC)", record.timestampUtc.ToString("yyyy-MM-dd HH:mm:ss"));
                EditorGUILayout.LabelField("Path", DuoCurtainBuildArchiveService.ToProjectRelativePath(record.folderPath), EditorStyles.miniLabel);

                if (record.manifest != null)
                {
                    string commit = string.IsNullOrEmpty(record.manifest.gitCommitShort)
                        ? "(no git)"
                        : record.manifest.gitCommitShort;
                    string branch = string.IsNullOrEmpty(record.manifest.gitBranch) ? "?" : record.manifest.gitBranch;
                    string dirty = record.manifest.gitDirty ? " · dirty" : string.Empty;
                    string dev = record.manifest.developmentBuild ? " · development" : string.Empty;
                    EditorGUILayout.LabelField("Git", commit + " @ " + branch + dirty + dev, EditorStyles.miniLabel);

                    if (!string.IsNullOrEmpty(record.manifest.gitCommitHash))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.SelectableLabel(record.manifest.gitCommitHash, EditorStyles.miniLabel, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                            if (GUILayout.Button("Copy", GUILayout.Width(56f)))
                                EditorGUIUtility.systemCopyBuffer = record.manifest.gitCommitHash;
                        }
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("Git", "No manifest saved for this build.", EditorStyles.miniLabel);
                }
            }

            GUILayout.Space(4f);
        }

        private static void EnsureScanned()
        {
            if (cachedRecords != null && EditorApplication.timeSinceStartup - lastScanTime < 2.0)
                return;

            ForceRefresh();
        }

        private static void ForceRefresh()
        {
            cachedRecords = DuoCurtainBuildArchiveService.ScanAllBuilds();
            lastScanTime = EditorApplication.timeSinceStartup;
        }
    }
}
#endif

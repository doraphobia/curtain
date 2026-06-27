using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FigmaImporter.Editor
{
    public static class FigmaPathUtils
    {
        public const string LocalRootAssetPath = "Assets/FigmaImporter/_Local";
        public const string LocalEditorFolderAssetPath = LocalRootAssetPath + "/Editor";
        public const string LocalFontsFolderAssetPath = LocalRootAssetPath + "/Fonts";
        public const string LocalDiagnosticsFolderAssetPath = LocalRootAssetPath + "/Diagnostics";
        public const string LocalRegistryAssetPath = LocalEditorFolderAssetPath + "/Import Fallback Registry.asset";
        public const string LocalFontLinksAssetPath = LocalEditorFolderAssetPath + "/Font Links.asset";
        public const string LocalGradientsGeneratorAssetPath = LocalEditorFolderAssetPath + "/GradientsGenerator.asset";

        public const string LegacySharedRootAssetPath = "Assets/FigmaImporter";
        public const string LegacySharedEditorFolderAssetPath = LegacySharedRootAssetPath + "/Editor";
        public const string LegacySharedRegistryAssetPath = LegacySharedEditorFolderAssetPath + "/Import Fallback Registry.asset";
        public const string LegacySharedFontLinksAssetPath = LegacySharedEditorFolderAssetPath + "/Font Links.asset";

        private const string DefaultRendersFolder = "FigmaImporter/_Local/Renders";
        private static readonly string[] WindowsReservedNames =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        public static string NormalizeRendersFolder(string rendersPath)
        {
            var normalized = (rendersPath ?? string.Empty).Trim().Replace('\\', '/');

            if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring("Assets/".Length);
            }

            // Keep renders path project-relative and portable across Win/Mac.
            if (normalized.StartsWith("/") || normalized.Contains(":/"))
            {
                normalized = string.Empty;
            }

            var segments = normalized
                .Split(new[] {'/'}, StringSplitOptions.RemoveEmptyEntries);

            var sanitizedSegments = new System.Collections.Generic.List<string>();
            foreach (var segment in segments)
            {
                if (segment == "." || segment == "..")
                {
                    continue;
                }

                var safeSegment = SanitizePathSegment(segment, "segment");
                if (!string.IsNullOrWhiteSpace(safeSegment))
                {
                    sanitizedSegments.Add(safeSegment);
                }
            }

            normalized = string.Join("/", sanitizedSegments).Trim('/');

            if (string.IsNullOrEmpty(normalized))
            {
                normalized = DefaultRendersFolder;
            }

            return normalized;
        }

        public static string BuildAssetPath(string rendersPath, string fileName = null)
        {
            var folder = NormalizeRendersFolder(rendersPath);
            if (string.IsNullOrEmpty(fileName))
            {
                return $"Assets/{folder}";
            }

            return $"Assets/{folder}/{fileName}";
        }

        public static string SanitizeFileName(string fileName, string fallback = "figma_node")
        {
            var source = string.IsNullOrWhiteSpace(fileName) ? fallback : fileName.Trim();
            var invalidChars = Path.GetInvalidFileNameChars();
            var chars = source.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalidChars, chars[i]) >= 0 || chars[i] == '/' || chars[i] == '\\')
                {
                    chars[i] = '_';
                }
            }

            var sanitized = new string(chars).Trim();
            sanitized = SanitizePathSegment(sanitized, fallback);
            return string.IsNullOrEmpty(sanitized) ? fallback : sanitized;
        }

        public static string NormalizeAssetPath(string assetPath)
        {
            var normalized = (assetPath ?? string.Empty).Trim().Replace('\\', '/');
            while (normalized.Contains("//"))
            {
                normalized = normalized.Replace("//", "/");
            }
            return normalized;
        }

        public static string ToAbsolutePathFromAssetPath(string assetPath)
        {
            var normalizedAssetPath = NormalizeAssetPath(assetPath);
            if (!normalizedAssetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Asset path must start with 'Assets/': {assetPath}");
            }

            var relativeToAssets = normalizedAssetPath.Substring("Assets/".Length);
            var segments = relativeToAssets.Split(new[] {'/'}, StringSplitOptions.RemoveEmptyEntries);
            var absolute = Application.dataPath;
            foreach (var segment in segments)
            {
                absolute = Path.Combine(absolute, segment);
            }

            return absolute;
        }

        public static void EnsureRendersFolderExists(string rendersPath)
        {
            var folderAssetPath = BuildAssetPath(rendersPath);
            var folderAbsolutePath = ToAbsolutePathFromAssetPath(folderAssetPath);
            if (!Directory.Exists(folderAbsolutePath))
            {
                Directory.CreateDirectory(folderAbsolutePath);
                AssetDatabase.Refresh();
            }
        }

        public static void EnsureAssetFolderExists(string assetFolderPath)
        {
            var normalizedPath = NormalizeAssetFolderPath(assetFolderPath);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return;
            }

            if (AssetDatabase.IsValidFolder(normalizedPath))
            {
                return;
            }

            var parts = normalizedPath.Split('/');
            if (parts.Length == 0 || !string.Equals(parts[0], "Assets", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Asset folder path must start with Assets: {assetFolderPath}");
            }

            var current = "Assets";
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    var createdGuid = AssetDatabase.CreateFolder(current, parts[i]);
                    if (string.IsNullOrEmpty(createdGuid) || !AssetDatabase.IsValidFolder(next))
                    {
                        // Fallback for edge cases (for example hidden folder segments like ".Local")
                        // where AssetDatabase.CreateFolder can fail silently.
                        var absolute = ToAbsolutePathFromAssetPath(next);
                        if (!Directory.Exists(absolute))
                        {
                            Directory.CreateDirectory(absolute);
                            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                        }
                    }
                }
                current = next;
            }

            if (!AssetDatabase.IsValidFolder(normalizedPath))
            {
                throw new InvalidOperationException($"Failed to create asset folder path: {assetFolderPath}");
            }
        }

        public static string NormalizeAssetFolderPath(string assetFolderPath)
        {
            var normalizedPath = NormalizeAssetPath(assetFolderPath).Trim('/');
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return "Assets";
            }

            var parts = normalizedPath.Split('/');
            if (parts.Length == 0 || !string.Equals(parts[0], "Assets", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Asset folder path must start with Assets: {assetFolderPath}");
            }

            var sanitized = new List<string> {"Assets"};
            for (var i = 1; i < parts.Length; i++)
            {
                var segment = parts[i].Trim();
                if (string.IsNullOrWhiteSpace(segment) || segment == "." || segment == "..")
                {
                    continue;
                }

                if (segment.StartsWith(".", StringComparison.Ordinal))
                {
                    segment = "_" + segment.TrimStart('.');
                }

                segment = SanitizePathSegment(segment, "segment");
                if (!string.IsNullOrWhiteSpace(segment))
                {
                    sanitized.Add(segment);
                }
            }

            return string.Join("/", sanitized);
        }

        private static string SanitizePathSegment(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var chars = value.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                var c = chars[i];
                if (Array.IndexOf(invalidChars, c) >= 0 || c == '/' || c == '\\')
                {
                    chars[i] = '_';
                }
            }

            var sanitized = new string(chars).Trim().TrimEnd('.', ' ');
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                return fallback;
            }

            if (sanitized.StartsWith(".", StringComparison.Ordinal))
            {
                sanitized = "_" + sanitized.TrimStart('.');
                if (string.IsNullOrWhiteSpace(sanitized))
                {
                    return fallback;
                }
            }

            foreach (var reservedName in WindowsReservedNames)
            {
                if (string.Equals(sanitized, reservedName, StringComparison.OrdinalIgnoreCase))
                {
                    sanitized = "_" + sanitized;
                    break;
                }
            }

            return sanitized;
        }
    }
}

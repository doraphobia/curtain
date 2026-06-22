using System;
using System.IO;
using AE2Unity;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AE2Unity.Editor
{
    internal readonly struct Ae2ShaderGenerationResult
    {
        public readonly bool Success;
        public readonly string ShaderAssetPath;
        public readonly string MaterialAssetPath;
        public readonly string PrefabAssetPath;
        public readonly string Message;

        public Ae2ShaderGenerationResult(bool success, string shaderAssetPath, string materialAssetPath, string prefabAssetPath, string message)
        {
            Success = success;
            ShaderAssetPath = shaderAssetPath;
            MaterialAssetPath = materialAssetPath;
            PrefabAssetPath = prefabAssetPath;
            Message = message;
        }
    }

    internal static class Ae2ShaderAssetGenerator
    {
        private const int MaxAtlasSize = 4096;
        private const int MaxAtlasCount = 4;
        private const int PreferredFrameSize = 512;

        private sealed class FrameAtlasBuildResult
        {
            public bool Success;
            public string Message = string.Empty;
            public Texture2D[] Atlases = Array.Empty<Texture2D>();
            public int Columns;
            public int Rows;
            public int FramesPerAtlas;
            public int FrameCount;
            public float FrameRate;
            public float Duration;

            public bool HasFrames => Success && Atlases.Length > 0 && FrameCount > 0;
        }

        public static Ae2ShaderGenerationResult Generate(Ae2ShaderClip clip, string sourcePath, bool overwriteExisting)
        {
            if (clip == null)
            {
                return new Ae2ShaderGenerationResult(false, string.Empty, string.Empty, string.Empty, "Clip is null.");
            }

            var directory = Path.GetDirectoryName(sourcePath);
            if (string.IsNullOrEmpty(directory))
            {
                directory = "Assets";
            }

            var baseName = Path.GetFileNameWithoutExtension(sourcePath);
            var shaderAssetPath = Path.Combine(directory, $"{baseName}.generated.shader");
            var materialAssetPath = Path.Combine(directory, $"{baseName}.generated.mat");

            if (!overwriteExisting)
            {
                shaderAssetPath = AssetDatabase.GenerateUniqueAssetPath(shaderAssetPath);
                materialAssetPath = AssetDatabase.GenerateUniqueAssetPath(materialAssetPath);
            }

            var atlasResult = BuildFrameAtlases(clip, sourcePath, directory, baseName);
            if (!atlasResult.Success)
            {
                return new Ae2ShaderGenerationResult(false, string.Empty, string.Empty, string.Empty, atlasResult.Message);
            }

            var shaderName = $"AE2Unity/Generated/{SanitizeShaderName(baseName)}";
            var shaderCode = Ae2ShaderCodeGenerator.GeneratePreviewShader(clip, shaderName, atlasResult.HasFrames);
            WriteTextIfChanged(shaderAssetPath, shaderCode);
            AssetDatabase.ImportAsset(shaderAssetPath, ImportAssetOptions.ForceSynchronousImport);

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderAssetPath);
            if (shader == null)
            {
                return new Ae2ShaderGenerationResult(false, shaderAssetPath, materialAssetPath, string.Empty, $"Generated shader file, but Unity did not load it: {shaderAssetPath}");
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(materialAssetPath);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = $"{baseName}.generated"
                };
                AssetDatabase.CreateAsset(material, materialAssetPath);
            }
            else
            {
                material.shader = shader;
            }

            ApplyMaterialDefaults(material, clip, atlasResult);
            EditorUtility.SetDirty(material);

            var prefabAssetPath = CreateOrUpdatePlayablePrefab(clip, sourcePath, directory, baseName, overwriteExisting);
            AssetDatabase.SaveAssets();
            var message = string.IsNullOrEmpty(prefabAssetPath)
                ? "Generated shader and material."
                : $"Generated shader, material, and playable prefab: {prefabAssetPath}";
            return new Ae2ShaderGenerationResult(true, shaderAssetPath, materialAssetPath, prefabAssetPath, message);
        }

        private static string CreateOrUpdatePlayablePrefab(Ae2ShaderClip clip, string sourcePath, string directory, string baseName, bool overwriteExisting)
        {
            var document = clip?.Document;
            if (document?.comp == null)
            {
                return string.Empty;
            }

            if (document.vectorAnimation != null && document.vectorAnimation.enabled && document.vectorAnimation.vectorOnly)
            {
                return CreateOrUpdateVectorPrefab(clip, directory, baseName, overwriteExisting);
            }

            if (document.bakedFrames != null && document.bakedFrames.enabled && document.bakedFrames.frameCount > 0)
            {
                return CreateOrUpdateBakedPrefab(clip, sourcePath, directory, baseName, overwriteExisting);
            }

            return string.Empty;
        }

        private static string CreateOrUpdateVectorPrefab(Ae2ShaderClip clip, string directory, string baseName, bool overwriteExisting)
        {
            var prefabPath = NormalizeAssetPath(Path.Combine(directory, $"{baseName}.prefab"));
            if (!overwriteExisting)
            {
                prefabPath = AssetDatabase.GenerateUniqueAssetPath(prefabPath);
            }

            var root = CreateRectRoot(clip, baseName);
            var graphic = root.AddComponent<Ae2VectorCompositionGraphic>();
            graphic.Clip = clip;
            graphic.raycastTarget = false;
            graphic.color = Color.white;

            var saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return saved == null ? string.Empty : prefabPath;
        }

        private static string CreateOrUpdateBakedPrefab(Ae2ShaderClip clip, string sourcePath, string directory, string baseName, bool overwriteExisting)
        {
            var textures = LoadBakedFrameTextures(clip, sourcePath);
            if (textures.Length == 0)
            {
                return string.Empty;
            }

            var prefabPath = NormalizeAssetPath(Path.Combine(directory, $"{baseName}.prefab"));
            if (!overwriteExisting)
            {
                prefabPath = AssetDatabase.GenerateUniqueAssetPath(prefabPath);
            }

            var root = CreateRectRoot(clip, baseName);
            var rawImage = root.AddComponent<RawImage>();
            rawImage.raycastTarget = false;
            rawImage.color = Color.white;
            rawImage.texture = textures[0];

            var player = root.AddComponent<Ae2BakedFramePlayer>();
            player.Clip = clip;
            player.Frames = textures;
            player.FrameRate = Mathf.Max(1f, clip.Document.bakedFrames.frameRate);
            player.Duration = clip.Document.bakedFrames.duration > 0f
                ? clip.Document.bakedFrames.duration
                : textures.Length / Mathf.Max(1f, clip.Document.bakedFrames.frameRate);

            var saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return saved == null ? string.Empty : prefabPath;
        }

        private static GameObject CreateRectRoot(Ae2ShaderClip clip, string baseName)
        {
            var root = new GameObject(baseName, typeof(RectTransform), typeof(CanvasRenderer));
            var rectTransform = root.GetComponent<RectTransform>();
            var comp = clip.Document.comp;
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.sizeDelta = new Vector2(Mathf.Max(1, comp.width), Mathf.Max(1, comp.height));
            rectTransform.anchoredPosition3D = Vector3.zero;
            rectTransform.localScale = Vector3.one;
            rectTransform.localRotation = Quaternion.identity;
            return root;
        }

        private static Texture2D[] LoadBakedFrameTextures(Ae2ShaderClip clip, string sourcePath)
        {
            var frames = clip?.Document?.bakedFrames;
            if (frames == null || !frames.enabled || frames.frameCount <= 0)
            {
                return Array.Empty<Texture2D>();
            }

            if (!TryResolveFrameFolder(sourcePath, frames.relativePath, out var frameFolder, out _))
            {
                return Array.Empty<Texture2D>();
            }

            if (!Directory.Exists(frameFolder))
            {
                return Array.Empty<Texture2D>();
            }

            var extension = string.IsNullOrWhiteSpace(frames.fileExtension) ? "png" : frames.fileExtension.TrimStart('.');
            var prefix = frames.filePrefix ?? string.Empty;
            var files = Directory.GetFiles(frameFolder, $"{prefix}*.{extension}", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            var frameCount = Mathf.Min(frames.frameCount, files.Length);
            var textures = new Texture2D[frameCount];
            for (var i = 0; i < frameCount; i++)
            {
                var assetPath = NormalizeAssetPath(files[i]);
                ConfigureBakedTextureImporter(assetPath, frames.width, frames.height);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                textures[i] = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            }

            return textures;
        }

        private static void ConfigureBakedTextureImporter(string assetPath, int width, int height)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            var maxDimension = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(1, Mathf.Max(width, height))), 32, 16384);
            var changed = false;
            if (importer.textureType != TextureImporterType.Default)
            {
                importer.textureType = TextureImporterType.Default;
                changed = true;
            }

            if (importer.alphaSource != TextureImporterAlphaSource.FromInput)
            {
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                changed = true;
            }

            if (importer.maxTextureSize != maxDimension)
            {
                importer.maxTextureSize = maxDimension;
                changed = true;
            }

            if (importer.mipmapEnabled)
            {
                importer.mipmapEnabled = false;
                changed = true;
            }

            if (importer.alphaIsTransparency != true)
            {
                importer.alphaIsTransparency = true;
                changed = true;
            }

            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                changed = true;
            }

            if (importer.npotScale != TextureImporterNPOTScale.None)
            {
                importer.npotScale = TextureImporterNPOTScale.None;
                changed = true;
            }

            if (changed)
            {
                importer.SaveAndReimport();
            }

            if (!importer.DoesSourceTextureHaveAlpha())
            {
                Debug.LogWarning($"AE2Unity baked fallback frame has no source alpha channel: {assetPath}. Re-export from After Effects; generated compositions are expected to preserve transparency.");
            }
        }

        private static void ApplyMaterialDefaults(Material material, Ae2ShaderClip clip, FrameAtlasBuildResult atlasResult)
        {
            if (clip?.Document?.comp == null)
            {
                return;
            }

            var comp = clip.Document.comp;
            material.SetVector("_CompSize", new Vector4(
                comp.width,
                comp.height,
                1f / Mathf.Max(1, comp.width),
                1f / Mathf.Max(1, comp.height)));

            if (!atlasResult.HasFrames)
            {
                material.SetFloat("_BakedFrameCount", 0f);
                return;
            }

            for (var i = 0; i < MaxAtlasCount; i++)
            {
                var atlas = atlasResult.Atlases[Mathf.Min(i, atlasResult.Atlases.Length - 1)];
                material.SetTexture($"_FrameAtlas{i}", atlas);
            }

            material.SetFloat("_BakedFrameCount", atlasResult.FrameCount);
            material.SetFloat("_FramesPerAtlas", atlasResult.FramesPerAtlas);
            material.SetFloat("_AtlasColumns", atlasResult.Columns);
            material.SetFloat("_AtlasRows", atlasResult.Rows);
            material.SetFloat("_FrameRate", atlasResult.FrameRate);
            material.SetFloat("_Duration", atlasResult.Duration);
            material.SetFloat("_AutoPlay", 1f);
            material.SetFloat("_Loop", 1f);
            material.SetFloat("_PlaybackSpeed", 1f);
        }

        private static FrameAtlasBuildResult BuildFrameAtlases(
            Ae2ShaderClip clip,
            string sourcePath,
            string outputDirectory,
            string baseName)
        {
            var frames = clip?.Document?.bakedFrames;
            if (frames == null || !frames.enabled || frames.frameCount <= 0)
            {
                return new FrameAtlasBuildResult { Success = true };
            }

            if (!TryResolveFrameFolder(sourcePath, frames.relativePath, out var frameFolder, out var pathError))
            {
                return new FrameAtlasBuildResult { Message = pathError };
            }

            if (!Directory.Exists(frameFolder))
            {
                return new FrameAtlasBuildResult
                {
                    Message = $"Baked animation folder was not found: {frameFolder}. Re-export from After Effects with Bake animation enabled."
                };
            }

            var extension = string.IsNullOrWhiteSpace(frames.fileExtension) ? "png" : frames.fileExtension.TrimStart('.');
            var prefix = frames.filePrefix ?? string.Empty;
            var files = Directory.GetFiles(frameFolder, $"{prefix}*.{extension}", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            if (files.Length == 0)
            {
                return new FrameAtlasBuildResult
                {
                    Message = $"No baked animation frames were found in {frameFolder}. Re-export from After Effects with Bake animation enabled."
                };
            }

            var frameCount = Mathf.Min(frames.frameCount, files.Length);
            var firstFramePath = NormalizeAssetPath(files[0]);
            AssetDatabase.ImportAsset(firstFramePath, ImportAssetOptions.ForceSynchronousImport);
            var firstFrame = AssetDatabase.LoadAssetAtPath<Texture2D>(firstFramePath);
            if (firstFrame == null)
            {
                return new FrameAtlasBuildResult { Message = $"Unity could not import baked frame: {firstFramePath}" };
            }

            CalculateAtlasLayout(firstFrame.width, firstFrame.height, frameCount,
                out var tileWidth, out var tileHeight, out var columns, out var rows, out var framesPerAtlas);
            var atlasCount = Mathf.CeilToInt(frameCount / (float)framesPerAtlas);
            if (atlasCount > MaxAtlasCount)
            {
                return new FrameAtlasBuildResult
                {
                    Message = $"Animation needs {atlasCount} atlases, but AE2Unity supports {MaxAtlasCount}. Reduce duration or frame rate."
                };
            }

            var atlases = new Texture2D[atlasCount];
            for (var atlasIndex = 0; atlasIndex < atlasCount; atlasIndex++)
            {
                var atlas = CreateAtlas(files, frameCount, atlasIndex, tileWidth, tileHeight, columns, rows, framesPerAtlas);
                if (atlas == null)
                {
                    return new FrameAtlasBuildResult { Message = $"Failed to build baked frame atlas {atlasIndex}." };
                }

                atlas.name = $"{baseName}.frames{atlasIndex}";
                var atlasPath = NormalizeAssetPath(Path.Combine(outputDirectory, $"{baseName}.frames{atlasIndex}.asset"));
                var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);
                if (existing == null)
                {
                    AssetDatabase.CreateAsset(atlas, atlasPath);
                    atlases[atlasIndex] = atlas;
                }
                else
                {
                    EditorUtility.CopySerialized(atlas, existing);
                    UnityEngine.Object.DestroyImmediate(atlas);
                    EditorUtility.SetDirty(existing);
                    atlases[atlasIndex] = existing;
                }
            }

            AssetDatabase.SaveAssets();
            return new FrameAtlasBuildResult
            {
                Success = true,
                Atlases = atlases,
                Columns = columns,
                Rows = rows,
                FramesPerAtlas = framesPerAtlas,
                FrameCount = frameCount,
                FrameRate = Mathf.Max(1f, frames.frameRate),
                Duration = frames.duration > 0f ? frames.duration : frameCount / Mathf.Max(1f, frames.frameRate)
            };
        }

        private static Texture2D CreateAtlas(
            string[] files,
            int frameCount,
            int atlasIndex,
            int tileWidth,
            int tileHeight,
            int columns,
            int rows,
            int framesPerAtlas)
        {
            var atlas = new Texture2D(columns * tileWidth, rows * tileHeight, TextureFormat.RGBA32, false, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var previousActive = RenderTexture.active;
            var temporary = RenderTexture.GetTemporary(tileWidth, tileHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            try
            {
                var firstFrameIndex = atlasIndex * framesPerAtlas;
                var lastFrameIndex = Mathf.Min(frameCount, firstFrameIndex + framesPerAtlas);
                for (var frameIndex = firstFrameIndex; frameIndex < lastFrameIndex; frameIndex++)
                {
                    var assetPath = NormalizeAssetPath(files[frameIndex]);
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                    var source = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                    if (source == null)
                    {
                        UnityEngine.Object.DestroyImmediate(atlas);
                        return null;
                    }

                    Graphics.Blit(source, temporary);
                    RenderTexture.active = temporary;
                    var localIndex = frameIndex - firstFrameIndex;
                    var column = localIndex % columns;
                    var row = localIndex / columns;
                    atlas.ReadPixels(
                        new Rect(0, 0, tileWidth, tileHeight),
                        column * tileWidth,
                        row * tileHeight,
                        false);
                }

                atlas.Apply(false, false);
                if (SystemInfo.SupportsTextureFormat(TextureFormat.BC7))
                {
                    EditorUtility.CompressTexture(atlas, TextureFormat.BC7, TextureCompressionQuality.Best);
                }
                else if (SystemInfo.SupportsTextureFormat(TextureFormat.DXT5))
                {
                    EditorUtility.CompressTexture(atlas, TextureFormat.DXT5, TextureCompressionQuality.Best);
                }

                atlas.Apply(false, true);
                return atlas;
            }
            finally
            {
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(temporary);
            }
        }

        private static void CalculateAtlasLayout(
            int sourceWidth,
            int sourceHeight,
            int frameCount,
            out int tileWidth,
            out int tileHeight,
            out int columns,
            out int rows,
            out int framesPerAtlas)
        {
            var preferredScale = Mathf.Min(1f, PreferredFrameSize / (float)Mathf.Max(sourceWidth, sourceHeight));
            tileWidth = Mathf.Max(4, Mathf.RoundToInt(sourceWidth * preferredScale));
            tileHeight = Mathf.Max(4, Mathf.RoundToInt(sourceHeight * preferredScale));
            tileWidth -= tileWidth % 4;
            tileHeight -= tileHeight % 4;
            columns = Mathf.Max(1, MaxAtlasSize / tileWidth);
            rows = Mathf.Max(1, MaxAtlasSize / tileHeight);
            framesPerAtlas = columns * rows;

            if (Mathf.CeilToInt(frameCount / (float)framesPerAtlas) <= MaxAtlasCount)
            {
                return;
            }

            var requiredPerAtlas = Mathf.CeilToInt(frameCount / (float)MaxAtlasCount);
            var sourceAspect = sourceWidth / (float)Mathf.Max(1, sourceHeight);
            columns = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(requiredPerAtlas / sourceAspect)));
            rows = Mathf.Max(1, Mathf.CeilToInt(requiredPerAtlas / (float)columns));
            var fitScale = Mathf.Min(
                MaxAtlasSize / (float)(columns * sourceWidth),
                MaxAtlasSize / (float)(rows * sourceHeight));
            var scale = Mathf.Min(preferredScale, fitScale);
            tileWidth = Mathf.Max(4, Mathf.FloorToInt(sourceWidth * scale / 4f) * 4);
            tileHeight = Mathf.Max(4, Mathf.FloorToInt(sourceHeight * scale / 4f) * 4);
            framesPerAtlas = columns * rows;
        }

        private static bool TryResolveFrameFolder(string sourcePath, string relativePath, out string folder, out string error)
        {
            folder = string.Empty;
            error = string.Empty;
            var normalized = (relativePath ?? string.Empty).Replace('\\', '/').Trim('/');
            if (string.IsNullOrWhiteSpace(normalized) || Path.IsPathRooted(normalized) || normalized.Contains(".."))
            {
                error = $"Invalid baked animation path '{relativePath}'.";
                return false;
            }

            var directory = Path.GetDirectoryName(sourcePath) ?? "Assets";
            folder = NormalizeAssetPath(Path.Combine(directory, normalized));
            return folder == "Assets" || folder.StartsWith("Assets/", StringComparison.Ordinal);
        }

        private static string NormalizeAssetPath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/');
        }

        private static void WriteTextIfChanged(string path, string text)
        {
            if (File.Exists(path) && string.Equals(File.ReadAllText(path), text, StringComparison.Ordinal))
            {
                return;
            }

            File.WriteAllText(path, text);
        }

        private static string SanitizeShaderName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Untitled";
            }

            foreach (var invalidChar in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalidChar, '_');
            }

            return value.Replace("\\", "_").Replace("/", "_").Replace("\"", string.Empty);
        }
    }
}

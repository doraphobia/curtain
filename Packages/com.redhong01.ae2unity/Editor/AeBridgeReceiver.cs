using System;
using System.IO;
using AE2Unity;
using UnityEditor;
using UnityEngine;

namespace AE2Unity.Editor
{
    [InitializeOnLoad]
    internal static class AeBridgeReceiver
    {
        private const double PollIntervalSeconds = 1.0;
        private static double nextPollTime;

        public static bool IsProcessingBridgeJob { get; private set; }

        static AeBridgeReceiver()
        {
            EditorApplication.update += Update;
        }

        private static void Update()
        {
            if (!Ae2ShaderEditorSettings.instance.BridgeReceiverEnabled)
            {
                return;
            }

            if (EditorApplication.timeSinceStartup < nextPollTime)
            {
                return;
            }

            nextPollTime = EditorApplication.timeSinceStartup + PollIntervalSeconds;
            ProcessInbox();
        }

        public static void ProcessInbox()
        {
            AeBridgePaths.EnsureFolders();

            var jobs = Directory.GetFiles(AeBridgePaths.Inbox, "*.job", SearchOption.TopDirectoryOnly);
            Array.Sort(jobs, StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < jobs.Length; i++)
            {
                ProcessJobFile(jobs[i]);
            }
        }

        private static void ProcessJobFile(string inboxPath)
        {
            var fileName = Path.GetFileName(inboxPath);
            var processingPath = Path.Combine(AeBridgePaths.Processing, fileName);

            try
            {
                MoveReplace(inboxPath, processingPath);
                var jobJson = File.ReadAllText(processingPath);
                var job = JsonUtility.FromJson<AeBridgeJob>(jobJson);
                var result = ExecuteJob(job);
                WriteResult(result);
                MoveReplace(processingPath, Path.Combine(AeBridgePaths.Done, fileName));
            }
            catch (Exception exception)
            {
                var jobId = Path.GetFileNameWithoutExtension(fileName);
                WriteResult(new AeBridgeResult
                {
                    jobId = jobId,
                    status = "Failed",
                    completedAt = DateTime.UtcNow.ToString("o"),
                    message = exception.Message
                });

                if (File.Exists(processingPath))
                {
                    MoveReplace(processingPath, Path.Combine(AeBridgePaths.Failed, fileName));
                }
                else if (File.Exists(inboxPath))
                {
                    MoveReplace(inboxPath, Path.Combine(AeBridgePaths.Failed, fileName));
                }
            }
        }

        private static AeBridgeResult ExecuteJob(AeBridgeJob job)
        {
            if (job == null)
            {
                throw new InvalidOperationException("Bridge job JSON could not be parsed.");
            }

            if (!string.Equals(job.command, AeBridgeProtocol.CommandImportAe2Shader, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unsupported bridge command: {job.command}");
            }

            if (string.IsNullOrWhiteSpace(job.payloadFile))
            {
                throw new InvalidOperationException("Bridge job is missing payloadFile.");
            }

            var payloadPath = AeBridgePaths.ResolveBridgeRelativePath(job.payloadFile);
            if (!File.Exists(payloadPath))
            {
                throw new FileNotFoundException("Bridge payload was not found.", payloadPath);
            }

            var payloadJson = File.ReadAllText(payloadPath);
            var payloadDocument = JsonUtility.FromJson<Ae2ShaderDocument>(payloadJson);

            var outputAssetFolder = NormalizeAssetFolder(string.IsNullOrWhiteSpace(job.unityOutputPath)
                ? Ae2ShaderEditorSettings.instance.DefaultBridgeOutputPath
                : job.unityOutputPath);
            outputAssetFolder = AppendCompositionFolder(outputAssetFolder, payloadDocument?.comp?.name ?? job.compName);

            Directory.CreateDirectory(outputAssetFolder);
            var targetAssetPath = Path.Combine(outputAssetFolder, Path.GetFileName(payloadPath));
            if (!job.overwriteGeneratedAssets && File.Exists(targetAssetPath))
            {
                targetAssetPath = AeBridgePaths.MakeUniqueFilePath(outputAssetFolder, Path.GetFileName(payloadPath));
            }

            File.Copy(payloadPath, targetAssetPath, true);
            CopyBakedFrames(payloadDocument?.bakedFrames, payloadPath, outputAssetFolder);

            var unityAssetPath = AeBridgePaths.ToAssetPath(targetAssetPath);
            IsProcessingBridgeJob = true;
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(unityAssetPath, ImportAssetOptions.ForceSynchronousImport);

                var result = new AeBridgeResult
                {
                    jobId = job.jobId,
                    command = job.command,
                    status = "Imported",
                    completedAt = DateTime.UtcNow.ToString("o"),
                    message = "Imported .ae2shader payload.",
                    importedAssetPath = unityAssetPath
                };

                if (!job.generateShaderAndMaterial)
                {
                    return result;
                }

                var clip = AssetDatabase.LoadAssetAtPath<Ae2ShaderClip>(unityAssetPath);
                if (clip == null)
                {
                    throw new InvalidOperationException($"Imported payload did not produce an Ae2ShaderClip: {unityAssetPath}");
                }

                var generationResult = Ae2ShaderAssetGenerator.Generate(
                    clip,
                    unityAssetPath,
                    job.overwriteGeneratedAssets && Ae2ShaderEditorSettings.instance.OverwriteGeneratedAssets);

                if (!generationResult.Success)
                {
                    throw new InvalidOperationException(generationResult.Message);
                }

                result.status = "Completed";
                if (payloadDocument?.vectorAnimation != null && payloadDocument.vectorAnimation.enabled && payloadDocument.vectorAnimation.vectorOnly)
                {
                    result.message = "Imported vector AE composition and generated a playable prefab.";
                }
                else if (payloadDocument?.bakedFrames != null && payloadDocument.bakedFrames.enabled)
                {
                    result.message = "Imported baked AE animation at composition resolution and generated a playable prefab.";
                }
                else
                {
                    result.message = "Imported metadata-only .ae2shader payload and generated a preview shader/material.";
                }

                result.generatedShaderPath = generationResult.ShaderAssetPath;
                result.generatedMaterialPath = generationResult.MaterialAssetPath;
                result.generatedPrefabPath = generationResult.PrefabAssetPath;
                return result;
            }
            finally
            {
                IsProcessingBridgeJob = false;
            }
        }

        private static string NormalizeAssetFolder(string folder)
        {
            var normalized = (folder ?? string.Empty).Replace('\\', '/').Trim('/');
            if (string.IsNullOrWhiteSpace(normalized))
            {
                normalized = "Assets/AE2Unity/Exports";
            }

            if (!(normalized == "Assets" || normalized.StartsWith("Assets/", StringComparison.Ordinal)))
            {
                normalized = $"Assets/{normalized}";
            }

            return normalized;
        }

        private static string AppendCompositionFolder(string outputAssetFolder, string compName)
        {
            var safeName = SanitizeFolderName(string.IsNullOrWhiteSpace(compName) ? "Untitled" : compName);
            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = "Untitled";
            }

            var normalized = NormalizeAssetFolder(outputAssetFolder);
            var currentName = Path.GetFileName(normalized.TrimEnd('/'));
            if (string.Equals(currentName, safeName, StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            return NormalizeAssetFolder(Path.Combine(normalized, safeName));
        }

        private static string SanitizeFolderName(string value)
        {
            foreach (var invalidChar in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalidChar, '_');
            }

            return value.Replace("\\", "_").Replace("/", "_").Trim();
        }

        private static void WriteResult(AeBridgeResult result)
        {
            AeBridgePaths.EnsureFolders();
            if (string.IsNullOrWhiteSpace(result.jobId))
            {
                result.jobId = Guid.NewGuid().ToString("N");
            }

            result.completedAt = string.IsNullOrWhiteSpace(result.completedAt)
                ? DateTime.UtcNow.ToString("o")
                : result.completedAt;

            var resultPath = Path.Combine(AeBridgePaths.Outbox, $"{result.jobId}.result.json");
            File.WriteAllText(resultPath, JsonUtility.ToJson(result, true));
        }

        private static void CopyBakedFrames(
            Ae2ShaderBakedFrames bakedFrames,
            string payloadPath,
            string outputAssetFolder)
        {
            if (bakedFrames == null || !bakedFrames.enabled || bakedFrames.frameCount <= 0)
            {
                return;
            }

            var relativePath = (bakedFrames.relativePath ?? string.Empty).Replace('\\', '/').Trim('/');
            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath) || relativePath.Contains(".."))
            {
                throw new InvalidOperationException($"Invalid baked animation path: {bakedFrames.relativePath}");
            }

            var payloadDirectory = Path.GetDirectoryName(payloadPath) ?? AeBridgePaths.Payloads;
            var sourceFolder = Path.Combine(payloadDirectory, relativePath);
            if (!Directory.Exists(sourceFolder))
            {
                throw new DirectoryNotFoundException($"Baked animation folder was not found: {sourceFolder}");
            }

            var destinationFolder = Path.Combine(outputAssetFolder, relativePath);
            if (Directory.Exists(destinationFolder))
            {
                Directory.Delete(destinationFolder, true);
            }

            CopyDirectory(sourceFolder, destinationFolder);
        }

        private static void CopyDirectory(string sourceFolder, string destinationFolder)
        {
            Directory.CreateDirectory(destinationFolder);
            var files = Directory.GetFiles(sourceFolder, "*", SearchOption.TopDirectoryOnly);
            for (var i = 0; i < files.Length; i++)
            {
                File.Copy(files[i], Path.Combine(destinationFolder, Path.GetFileName(files[i])), true);
            }

            var directories = Directory.GetDirectories(sourceFolder, "*", SearchOption.TopDirectoryOnly);
            for (var i = 0; i < directories.Length; i++)
            {
                CopyDirectory(directories[i], Path.Combine(destinationFolder, Path.GetFileName(directories[i])));
            }
        }

        private static void MoveReplace(string sourcePath, string targetPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? AeBridgePaths.BridgeRoot);
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            File.Move(sourcePath, targetPath);
        }
    }
}

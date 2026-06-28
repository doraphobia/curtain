using System;

namespace AE2Unity.Editor
{
    internal static class AeBridgeProtocol
    {
        public const string ProtocolVersion = "0.1.0";
        public const string BridgeFolderName = ".ae2unitybridge";
        public const string InboxFolderName = "inbox";
        public const string ProcessingFolderName = "processing";
        public const string DoneFolderName = "done";
        public const string FailedFolderName = "failed";
        public const string OutboxFolderName = "outbox";
        public const string PayloadsFolderName = "payloads";
        public const string CommandImportAe2Shader = "ImportAe2Shader";
        public const string CommandImportAe2Motion = "ImportAe2Motion";
    }

    [Serializable]
    internal sealed class AeBridgeJob
    {
        public string protocolVersion = AeBridgeProtocol.ProtocolVersion;
        public string jobId = string.Empty;
        public string command = AeBridgeProtocol.CommandImportAe2Shader;
        public string createdAt = string.Empty;
        public string sender = "After Effects";
        public string compName = string.Empty;
        public string payloadFile = string.Empty;
        public string referenceFramesFolder = string.Empty;
        public string unityOutputPath = "Assets/AE2Unity/Exports";
        public bool overwriteGeneratedAssets = true;
        public bool generateShaderAndMaterial = true;
        public bool generateMotionRuntimeAssets = true;
        public bool generatePrefab = true;
        public bool refreshAssetDatabase = true;
    }

    [Serializable]
    internal sealed class AeBridgeResult
    {
        public string protocolVersion = AeBridgeProtocol.ProtocolVersion;
        public string jobId = string.Empty;
        public string command = AeBridgeProtocol.CommandImportAe2Shader;
        public string status = "Unknown";
        public string message = string.Empty;
        public string completedAt = string.Empty;
        public string importedAssetPath = string.Empty;
        public string generatedShaderPath = string.Empty;
        public string generatedMaterialPath = string.Empty;
        public string generatedPrefabPath = string.Empty;
        public string generatedMotionDataPath = string.Empty;
        public string[] warnings = Array.Empty<string>();
    }
}

using System.IO;

namespace AE2Unity.Editor
{
    internal static class AeBridgePaths
    {
        public static string ProjectRoot => Directory.GetCurrentDirectory();
        public static string BridgeRoot => Path.Combine(ProjectRoot, AeBridgeProtocol.BridgeFolderName);
        public static string Inbox => Path.Combine(BridgeRoot, AeBridgeProtocol.InboxFolderName);
        public static string Processing => Path.Combine(BridgeRoot, AeBridgeProtocol.ProcessingFolderName);
        public static string Done => Path.Combine(BridgeRoot, AeBridgeProtocol.DoneFolderName);
        public static string Failed => Path.Combine(BridgeRoot, AeBridgeProtocol.FailedFolderName);
        public static string Outbox => Path.Combine(BridgeRoot, AeBridgeProtocol.OutboxFolderName);
        public static string Payloads => Path.Combine(BridgeRoot, AeBridgeProtocol.PayloadsFolderName);

        public static void EnsureFolders()
        {
            Directory.CreateDirectory(BridgeRoot);
            Directory.CreateDirectory(Inbox);
            Directory.CreateDirectory(Processing);
            Directory.CreateDirectory(Done);
            Directory.CreateDirectory(Failed);
            Directory.CreateDirectory(Outbox);
            Directory.CreateDirectory(Payloads);
        }

        public static string ResolveBridgeRelativePath(string relativePath)
        {
            var normalized = (relativePath ?? string.Empty).Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            return Path.GetFullPath(Path.Combine(BridgeRoot, normalized));
        }

        public static string ToAssetPath(string path)
        {
            return path.Replace('\\', '/');
        }

        public static string MakeUniqueFilePath(string directory, string fileName)
        {
            var path = Path.Combine(directory, fileName);
            if (!File.Exists(path))
            {
                return path;
            }

            var name = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            var index = 1;
            do
            {
                path = Path.Combine(directory, $"{name} {index}{extension}");
                index++;
            }
            while (File.Exists(path));

            return path;
        }
    }
}

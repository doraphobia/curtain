namespace FigmaImporter.Editor
{
    /// <summary>
    /// Centralized menu taxonomy for FigmaImporter editor tools.
    /// Keep all new menu entries under one of the main categories:
    /// Importer / Diagnostics / Dependencies / Help.
    /// </summary>
    internal static class FigmaImporterMenuPaths
    {
        private const string Root = "Window/FigmaImporter";

        internal static class Importer
        {
            public const string OpenWindow = Root + "/Importer/Open Importer";
        }

        internal static class Diagnostics
        {
            private const string DiagnosticsRoot = Root + "/Diagnostics";
            private const string ErrorFixRoot = DiagnosticsRoot + "/Error Fix";

            public const string DiagnosticsHub = DiagnosticsRoot + "/Diagnostics Hub";
            public const string AutoLayoutDiagnostics = DiagnosticsRoot + "/AutoLayout Diagnostics";
            public const string FallbackResolver = ErrorFixRoot + "/Fallback Resolver";
            public const string ImporterErrorHandoff = ErrorFixRoot + "/Importer Error Handoff";
            public const string AnalyzeWithAgent = ErrorFixRoot + "/Analyze With Agent";
        }

        internal static class Dependencies
        {
            private const string DependenciesRoot = Root + "/Dependencies";

            public const string InitializeNow = DependenciesRoot + "/Initialize Dependencies Now";
            public const string AutoInitialize = DependenciesRoot + "/Auto Initialize Dependencies";
        }

        internal static class Help
        {
            private const string HelpRoot = Root + "/Help";

            public const string QuickStartTutorial = HelpRoot + "/Flow Studio";
            public const string OpenReadme = HelpRoot + "/Open README";
            public const string OpenDiagnosticsHub = HelpRoot + "/Open Diagnostics Hub";
        }
    }
}

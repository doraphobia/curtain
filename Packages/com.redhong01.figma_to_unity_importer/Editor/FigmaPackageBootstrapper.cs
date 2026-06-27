using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace FigmaImporter.Editor
{
    [InitializeOnLoad]
    internal static class FigmaPackageBootstrapper
    {
        private const string AutoInitMenuPath = FigmaImporterMenuPaths.Dependencies.AutoInitialize;
        private const string InitNowMenuPath = FigmaImporterMenuPaths.Dependencies.InitializeNow;
        private const string AutoInitEnabledKey = "FigmaImporter.AutoInitializeDependencies";
        private const string SessionCheckedKey = "FigmaImporter.AutoInitializeDependencies.SessionChecked";
        private const string SessionRunningKey = "FigmaImporter.AutoInitializeDependencies.SessionRunning";
        private const string SessionFontRepairCheckedKey = "FigmaImporter.AutoInitializeDependencies.FontRepairChecked";
        private const int PackageRequestTimeoutSeconds = 120;

        private static readonly PackageDescriptor[] RequiredPackages =
        {
            new PackageDescriptor("com.unity.nuget.newtonsoft-json", "com.unity.nuget.newtonsoft-json"),
            new PackageDescriptor("com.unity.ugui", "com.unity.ugui"),
            new PackageDescriptor("com.unity.textmeshpro", "com.unity.textmeshpro"),
            new PackageDescriptor("com.unity.vectorgraphics", "com.unity.vectorgraphics@2.0.0-preview.25", "com.unity.vectorgraphics")
        };

        static FigmaPackageBootstrapper()
        {
            if (Application.isBatchMode)
            {
                return;
            }

            // Run a one-shot repair immediately on editor domain load so broken TMP font assets
            // do not keep throwing in OnValidate/preview before delayCall jobs execute.
            RepairFontsOncePerSession();
            EditorApplication.delayCall += AutoInitializeOncePerSession;
        }

        [MenuItem(InitNowMenuPath)]
        private static void InitializeDependenciesNow()
        {
            InitializeDependencies(force: true);
        }

        internal static void InitializeDependencies(bool force)
        {
            _ = EnsureDependenciesInstalledAsync(force);
        }

        internal static bool GetAutoInitializeEnabled()
        {
            return IsAutoInitializeEnabled();
        }

        internal static void SetAutoInitializeEnabled(bool enabled)
        {
            EditorPrefs.SetBool(AutoInitEnabledKey, enabled);
            Menu.SetChecked(AutoInitMenuPath, enabled);
        }

        internal static Task EnsureDependenciesInstalledForImportAsync()
        {
            // Import flow should always attempt a dependency check even when
            // startup auto-initialization is disabled or skipped.
            return EnsureDependenciesInstalledAsync(force: true);
        }

        [MenuItem(AutoInitMenuPath)]
        private static void ToggleAutoInitialize()
        {
            var enabled = !IsAutoInitializeEnabled();
            EditorPrefs.SetBool(AutoInitEnabledKey, enabled);
            Menu.SetChecked(AutoInitMenuPath, enabled);
            Debug.Log($"[FigmaImporter] Auto dependency initialization {(enabled ? "enabled" : "disabled")}.");
        }

        [MenuItem(AutoInitMenuPath, true)]
        private static bool ToggleAutoInitializeValidate()
        {
            Menu.SetChecked(AutoInitMenuPath, IsAutoInitializeEnabled());
            return true;
        }

        private static void AutoInitializeOncePerSession()
        {
            if (Application.isBatchMode)
            {
                return;
            }

            RepairFontsOncePerSession();

            if (!IsAutoInitializeEnabled())
            {
                return;
            }

            if (SessionState.GetBool(SessionCheckedKey, false))
            {
                return;
            }

            SessionState.SetBool(SessionCheckedKey, true);
            _ = EnsureDependenciesInstalledAsync(force: false);
        }

        private static void RepairFontsOncePerSession()
        {
            if (SessionState.GetBool(SessionFontRepairCheckedKey, false))
            {
                return;
            }

            SessionState.SetBool(SessionFontRepairCheckedKey, true);
            try
            {
                var repairedFontAssets = FontAssetResolver.RepairImportedFontAssets();
                var repairedTextComponents = TMPUtils.RepairBrokenFontsInOpenScenes();
                if (repairedFontAssets > 0 || repairedTextComponents > 0)
                {
                    Debug.Log(
                        $"[FigmaImporter] Startup font repair completed. Font assets repaired: {repairedFontAssets}, scene text components repaired: {repairedTextComponents}.");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FigmaImporter] Startup font repair failed: {e.Message}");
            }
        }

        private static bool IsAutoInitializeEnabled()
        {
            return EditorPrefs.GetBool(AutoInitEnabledKey, false);
        }

        private static async Task EnsureDependenciesInstalledAsync(bool force)
        {
            if (SessionState.GetBool(SessionRunningKey, false))
            {
                var skippedChainId = FigmaImporterEventFlow.Start(
                    "Dependencies",
                    force ? "Initialize Dependencies (Force)" : "Auto Initialize Dependencies",
                    $"force={force}; existingSession=true");
                FigmaImporterEventFlow.End(
                    "Dependencies",
                    skippedChainId,
                    "Skipped",
                    "Dependency initialization already running");
                return;
            }

            var flowChainId = FigmaImporterEventFlow.Start(
                "Dependencies",
                force ? "Initialize Dependencies (Force)" : "Auto Initialize Dependencies",
                $"force={force}");
            var flowResult = "Completed";
            var flowDetails = string.Empty;
            SessionState.SetBool(SessionRunningKey, true);
            try
            {
                await WaitForEditorReadyAsync();

                var installedPackages = await GetInstalledPackagesAsync();
                if (installedPackages == null)
                {
                    if (force)
                    {
                        Debug.LogWarning("[FigmaImporter] Could not read installed packages from Package Manager.");
                    }

                    flowResult = "Skipped";
                    flowDetails = "Installed packages unavailable";
                    return;
                }

                var missingPackages = new List<PackageDescriptor>();
                foreach (var required in RequiredPackages)
                {
                    if (string.Equals(required.Name, "com.unity.textmeshpro", StringComparison.OrdinalIgnoreCase) &&
                        IsTextMeshProAvailable())
                    {
                        if (force)
                        {
                            Debug.Log("[FigmaImporter] Skip 'com.unity.textmeshpro' because TMPro types are already available.");
                        }
                        continue;
                    }

                    var found = false;
                    foreach (var installed in installedPackages)
                    {
                        if (string.Equals(installed.name, required.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        missingPackages.Add(required);
                    }
                }

                if (missingPackages.Count == 0)
                {
                    if (force)
                    {
                        Debug.Log("[FigmaImporter] All required dependencies are already installed.");
                    }

                    flowResult = "Skipped";
                    flowDetails = "No missing dependencies";
                    return;
                }

                Debug.Log(
                    $"[FigmaImporter] Initializing dependencies: {string.Join(", ", missingPackages.ConvertAll(x => x.Name))}");
                FigmaImporterEventFlow.Step(
                    "Dependencies",
                    flowChainId,
                    "MissingDependenciesDetected",
                    string.Join(", ", missingPackages.ConvertAll(x => x.Name)));

                var installedAny = false;
                foreach (var missing in missingPackages)
                {
                    await WaitForEditorReadyAsync();

                    // Re-check before each install attempt because previous installs may
                    // have already pulled this dependency transitively.
                    var latestInstalledPackages = await GetInstalledPackagesAsync();
                    if (IsPackageInstalled(latestInstalledPackages, missing.Name))
                    {
                        if (force)
                        {
                            Debug.Log($"[FigmaImporter] Skip '{missing.Name}' because it is already installed.");
                        }
                        continue;
                    }

                    var added = await TryInstallPackageWithFallbackAsync(missing);
                    installedAny |= added;
                    FigmaImporterEventFlow.Step(
                        "Dependencies",
                        flowChainId,
                        "DependencyInstallAttempt",
                        $"{missing.Name}: {(added ? "installed" : "not-installed")}",
                        allowDuplicate: true);
                }

                if (installedAny)
                {
                    Debug.Log(
                        "[FigmaImporter] Dependency initialization finished. Unity may recompile scripts after package installation.");
                    flowDetails = "Installed one or more dependencies";
                }
                else
                {
                    flowResult = "Skipped";
                    flowDetails = "No new dependencies installed";
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FigmaImporter] Dependency initialization failed: {e.Message}");
                flowResult = "Failed";
                flowDetails = e.Message;
            }
            finally
            {
                SessionState.SetBool(SessionRunningKey, false);
                FigmaImporterEventFlow.End("Dependencies", flowChainId, flowResult, flowDetails);
            }
        }

        private static async Task WaitForEditorReadyAsync()
        {
            while (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                await Task.Delay(200);
            }
        }

        private static async Task<PackageCollection> GetInstalledPackagesAsync()
        {
            ListRequest request;
            try
            {
                request = Client.List(offlineMode: false, includeIndirectDependencies: true);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FigmaImporter] Failed to query Package Manager: {e.Message}");
                return null;
            }

            var start = EditorApplication.timeSinceStartup;
            while (!request.IsCompleted)
            {
                if (EditorApplication.timeSinceStartup - start > PackageRequestTimeoutSeconds)
                {
                    Debug.LogWarning("[FigmaImporter] Package list request timed out.");
                    return null;
                }

                await Task.Delay(150);
            }

            if (request.Status == StatusCode.Success)
            {
                return request.Result;
            }

            Debug.LogWarning(
                $"[FigmaImporter] Failed to list packages: {request.Error?.message ?? "Unknown error"}");
            return null;
        }

        private static bool IsPackageInstalled(PackageCollection packages, string packageName)
        {
            if (packages == null || string.IsNullOrWhiteSpace(packageName))
            {
                return false;
            }

            foreach (var package in packages)
            {
                if (string.Equals(package.name, packageName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static async Task<bool> TryInstallPackageAsync(string packageId)
        {
            AddRequest request;
            try
            {
                request = Client.Add(packageId);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FigmaImporter] Failed to start install for '{packageId}': {e.Message}");
                return false;
            }

            var start = EditorApplication.timeSinceStartup;
            while (!request.IsCompleted)
            {
                if (EditorApplication.timeSinceStartup - start > PackageRequestTimeoutSeconds)
                {
                    Debug.LogWarning($"[FigmaImporter] Installing '{packageId}' timed out.");
                    return false;
                }

                await Task.Delay(150);
            }

            if (request.Status == StatusCode.Success)
            {
                Debug.Log($"[FigmaImporter] Installed package '{request.Result.name}' ({request.Result.version}).");
                return true;
            }

            Debug.LogWarning(
                $"[FigmaImporter] Could not install '{packageId}': {request.Error?.message ?? "Unknown error"}");
            return false;
        }

        private static async Task<bool> TryInstallPackageWithFallbackAsync(PackageDescriptor descriptor)
        {
            for (var i = 0; i < descriptor.InstallCandidates.Length; i++)
            {
                var packageId = descriptor.InstallCandidates[i];
                var installed = await TryInstallPackageAsync(packageId);
                if (installed)
                {
                    return true;
                }

                if (i < descriptor.InstallCandidates.Length - 1)
                {
                    Debug.LogWarning(
                        $"[FigmaImporter] Retry installing '{descriptor.Name}' with alternate id '{descriptor.InstallCandidates[i + 1]}'.");
                }
            }

            return false;
        }

        private static bool IsTextMeshProAvailable()
        {
            var tmpFontType = Type.GetType("TMPro.TMP_FontAsset, Unity.TextMeshPro");
            if (tmpFontType != null)
            {
                return true;
            }

            // Some editor/runtime combinations may use a different assembly name.
            return AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
            {
                try
                {
                    return assembly.GetType("TMPro.TMP_FontAsset", false) != null;
                }
                catch
                {
                    return false;
                }
            });
        }

        private readonly struct PackageDescriptor
        {
            public readonly string Name;
            public readonly string[] InstallCandidates;

            public PackageDescriptor(string name, params string[] installCandidates)
            {
                Name = name;
                InstallCandidates = installCandidates ?? Array.Empty<string>();
            }
        }
    }
}

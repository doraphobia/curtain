param(
    [string]$AfterEffectsSupportFiles = "C:\Program Files\Adobe\Adobe After Effects 2026\Support Files"
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceFile = Join-Path $scriptDir "AE2Unity.jsx"
$targetDir = Join-Path $AfterEffectsSupportFiles "Scripts\ScriptUI Panels"
$targetFile = Join-Path $targetDir "AE2Unity.jsx"
$legacyTargetFiles = @(
    (Join-Path $targetDir "ae2unityshader.jsx"),
    (Join-Path $targetDir "AE2UnityShaderExport.jsx")
)

if (-not (Test-Path $sourceFile)) {
    throw "Missing source file: $sourceFile"
}

New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
Copy-Item -Path $sourceFile -Destination $targetFile -Force
foreach ($legacyTargetFile in $legacyTargetFiles) {
    if (Test-Path $legacyTargetFile) {
        Remove-Item -Path $legacyTargetFile -Force
    }
}

Write-Host "Installed AE2Unity panel:"
Write-Host $targetFile
Write-Host "Restart After Effects, then open Window > AE2Unity.jsx"

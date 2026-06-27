param(
  [string]$ConfigPath = "$HOME\font-family-sync.json",
  [string]$PythonCommand = "py",
  [string]$TaskName = "FontFamilySync"
)

$ErrorActionPreference = "Stop"

$ScriptPath = Resolve-Path (Join-Path $PSScriptRoot "..\font_family_sync.py")

if (-not (Test-Path $ConfigPath)) {
  Write-Error "Config file not found: $ConfigPath. Create one first with: py `"$ScriptPath`" init --vault <shared-vault> --config-out `"$ConfigPath`""
}

$Argument = "`"$ScriptPath`" watch --config `"$ConfigPath`""
$Action = New-ScheduledTaskAction -Execute $PythonCommand -Argument $Argument
$Trigger = New-ScheduledTaskTrigger -AtLogOn
$Settings = New-ScheduledTaskSettingsSet `
  -AllowStartIfOnBatteries `
  -DontStopIfGoingOnBatteries `
  -RestartCount 3 `
  -RestartInterval (New-TimeSpan -Minutes 1)

Register-ScheduledTask `
  -TaskName $TaskName `
  -Action $Action `
  -Trigger $Trigger `
  -Settings $Settings `
  -Description "Synchronize user-installed font families through a shared vault." `
  -Force | Out-Null

Start-ScheduledTask -TaskName $TaskName

Write-Host "Installed and started Windows Scheduled Task: $TaskName"
Write-Host "Script: $ScriptPath"
Write-Host "Config: $ConfigPath"

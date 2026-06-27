# Font Family Sync

Font Family Sync keeps user-installed font families aligned across macOS and
Windows machines. It supports Mac-Mac, Win-Win, and Win-Mac setups by syncing
through one shared vault folder.

Use this only for fonts you are licensed to install on every synced device. The
tool copies font files you already have access to; it does not bypass font
licensing, DRM, activation systems, or cloud font services.

## How It Works

Each device runs `font_family_sync.py` and points to the same vault directory.
That vault can be inside Google Drive, OneDrive, iCloud Drive, Dropbox, Syncthing,
an SMB/NAS folder, or an external drive.

On every sync cycle the tool:

1. Scans this machine's user font folder.
2. Extracts font family names from the font's OpenType/TrueType `name` table.
3. Copies new font files into `vault/blobs`.
4. Updates `vault/index.json`.
5. Installs missing vault fonts into this machine's user font folder.

Deletion is intentionally not synced. Removing fonts automatically across
machines is risky for design projects, so the first version is add-only.

## Platform Defaults

macOS:

- Publishes from `~/Library/Fonts`
- Installs to `~/Library/Fonts`
- Checks installed fonts in `~/Library/Fonts`, `/Library/Fonts`, and
  `/System/Library/Fonts`

Windows:

- Publishes from `%LOCALAPPDATA%\Microsoft\Windows\Fonts`
- Installs to `%LOCALAPPDATA%\Microsoft\Windows\Fonts`
- Checks installed fonts in the user font folder and `C:\Windows\Fonts`
- Updates the current-user font registry and broadcasts `WM_FONTCHANGE`

## Quick Start

Create a shared vault on the first machine:

```bash
python3 Tools/FontFamilySync/font_family_sync.py init \
  --vault "$HOME/FontFamilyVault" \
  --config-out "$HOME/font-family-sync.json"
```

Run one sync:

```bash
python3 Tools/FontFamilySync/font_family_sync.py once \
  --config "$HOME/font-family-sync.json"
```

Run continuously:

```bash
python3 Tools/FontFamilySync/font_family_sync.py watch \
  --config "$HOME/font-family-sync.json"
```

On the second machine, point the config to the same shared vault folder and run
the same `watch` command.

Windows PowerShell example:

```powershell
py .\Tools\FontFamilySync\font_family_sync.py once --config "$HOME\font-family-sync.json"
```

## Status Check

Compare this device with the vault:

```bash
python3 Tools/FontFamilySync/font_family_sync.py status \
  --config "$HOME/font-family-sync.json"
```

Important fields:

- `vault_files`: font files known to the shared vault.
- `vault_families`: font families known to the shared vault.
- `installed_vault_files_here`: vault files already present on this device.
- `missing_vault_files_here`: vault files not present on this device.
- `missing_families_here`: entire families missing from this device.
- `partial_families_here`: families where this device has only part of the set.

## Config

Copy `config.example.json` and edit the paths:

```json
{
  "vault": "~/FontFamilyVault",
  "device_id": "my-macbook",
  "device_name": "Red MacBook",
  "interval_seconds": 30,
  "publish_roots": ["~/Library/Fonts"],
  "installed_roots": ["~/Library/Fonts", "/Library/Fonts", "/System/Library/Fonts"],
  "install_root": "~/Library/Fonts",
  "ignore_patterns": [".*", "__MACOSX", "*.download", "*.tmp", "*.bak"],
  "refresh_font_cache": false
}
```

For Windows, use paths such as:

```json
{
  "vault": "D:/FontFamilyVault",
  "publish_roots": ["%LOCALAPPDATA%/Microsoft/Windows/Fonts"],
  "installed_roots": [
    "%LOCALAPPDATA%/Microsoft/Windows/Fonts",
    "%WINDIR%/Fonts"
  ],
  "install_root": "%LOCALAPPDATA%/Microsoft/Windows/Fonts"
}
```

## Background Use

macOS LaunchAgent command:

```bash
python3 /path/to/Tools/FontFamilySync/font_family_sync.py watch \
  --config "$HOME/font-family-sync.json"
```

Or install the included LaunchAgent:

```bash
bash Tools/FontFamilySync/mac/install-launch-agent.sh "$HOME/font-family-sync.json"
```

Windows Task Scheduler action:

```powershell
py C:\path\to\Tools\FontFamilySync\font_family_sync.py watch --config "$HOME\font-family-sync.json"
```

Or install the included current-user task:

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\FontFamilySync\windows\install-startup-task.ps1 -ConfigPath "$HOME\font-family-sync.json"
```

Run it at login on every synced machine. Apps that were already open may need a
restart to pick up newly installed fonts.

## Notes

- The sync key is the font file SHA-256 hash, so duplicate files are not copied
  twice.
- Family matching is derived from internal font metadata, not just file names.
- Variable fonts and font collections are handled as font files; their family
  names are still read from the `name` table when available.
- Adobe Fonts and other subscription/cloud font services may store fonts outside
  normal user font folders or prevent copying; those are not targeted by this
  tool.

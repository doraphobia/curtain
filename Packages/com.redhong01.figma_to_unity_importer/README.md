# Figma To Unity Importer

Import Figma nodes into Unity UI with:

- Figma node fetching and node generation
- AutoLayout diagnostics and debug pack export
- Import fallback resolver (fonts/SVG/render fallbacks)
- Frame-level sync binding (check/apply/regenerate)
- Figma -> TMP typography mapping helpers

## Unity Version

- Tested in Unity 6 (`6000.x`)
- Package manifest compatibility floor: `6000.0`

## Installation

Add this dependency to your Unity project's `Packages/manifest.json`.

### Recommended: install from the standalone mirror repo root

```json
{
  "dependencies": {
    "com.redhong01.figma_to_unity_importer": "https://github.com/RedHong01/Figma-Importer.git#v0.2.0"
  }
}
```

This is the preferred consumer install path because it does not depend on the source monorepo layout.

### Source development only: install from the monorepo package path

```json
{
  "dependencies": {
    "com.redhong01.figma_to_unity_importer": "https://github.com/RedHong01/AltControl2_TeamC_MushroomGame.git?path=/Packages/com.redhong01.figma_to_unity_importer#v0.2.0"
  }
}
```

Use this option only when you intentionally want to consume the package from the source repository. If the source repository is private, users need their own access to that repository.

## Optional Sync-Back Interface

This repository can remain independent while still accepting **optional** sync proposals from consumer repositories.

- Receiver workflow: `.github/workflows/receive-consumer-sync.yml`
- Guide and sender example: `Documentation~/sync-back.md`
- Trigger model: manual by default, optional auto dispatch after secure setup
- Auto dispatch gate: valid dispatch token + matching `dispatch_secret` (allowlist optional)

The sync pipeline opens a PR for review instead of writing directly to `main`.

## Menu Entry Points

- `Window/FigmaImporter/Importer/Open Importer`
- `Window/FigmaImporter/Diagnostics/Diagnostics Hub`
- `Window/FigmaImporter/Diagnostics/AutoLayout Diagnostics`
- `Window/FigmaImporter/Diagnostics/Error Fix/Fallback Resolver`
- `Window/FigmaImporter/Diagnostics/Error Fix/Importer Error Handoff`
- `Window/FigmaImporter/Dependencies/Initialize Dependencies Now`
- `Window/FigmaImporter/Dependencies/Auto Initialize Dependencies`
- `Window/FigmaImporter/Help/Flow Studio`
- `Window/FigmaImporter/Help/Open README`
- `Window/FigmaImporter/Help/Open Diagnostics Hub`

## First-Time Onboarding

Open:

- `Window/FigmaImporter/Help/Flow Studio`

The tutorial walks new users through:

- Git prerequisite for Git URL package installs
- OAuth token setup on the current machine
- First import flow (`Fetch Figma Node Data` -> `Apply Selected Import Modes`)
- Quick links to Diagnostics Hub and Fallback Resolver

## Package Structure

```text
com.redhong01.figma_to_unity_importer/
  package.json
  README.md
  CHANGELOG.md
  LICENSE.md
  Documentation~/
  Editor/
  Runtime/
```

## Release Workflow

1. Update `package.json` version (SemVer).
2. Update `CHANGELOG.md`.
3. Commit and push.
4. Create Git tag, for example `v0.2.0`.
5. Publish release notes on GitHub.

## Notes

- This package depends on `com.unity.vectorgraphics`; if unavailable, SVG-specific paths fall back to raster rendering.
- If your repository uses Git LFS assets, consumers also need Git LFS installed.

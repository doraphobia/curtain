# Figma To Unity Importer - Documentation

## Overview

This package imports Figma nodes into Unity UI and provides tooling for diagnostics, fallbacks, and frame sync workflows.

## Main Windows

- `Window/FigmaImporter/Importer/Open Importer`
- `Window/FigmaImporter/Diagnostics/Diagnostics Hub`
- `Window/FigmaImporter/Diagnostics/AutoLayout Diagnostics`
- `Window/FigmaImporter/Diagnostics/Error Fix/Fallback Resolver`

## Typical Flow

1. Open importer window.
2. Authenticate and fetch node data from a Figma URL.
3. Select root object and generate nodes.
4. If rendering or font issues are detected, use:
   - Fallback Resolver
   - Diagnostics pack export
   - Error handoff tools

## Frame Sync

Generated frame roots can carry a sync binding component that allows:

- checking updates from Figma
- applying selected changes
- regenerating the current bound frame

## Dependencies

This package depends on:

- `com.unity.nuget.newtonsoft-json`
- `com.unity.textmeshpro`
- `com.unity.ugui`
- `com.unity.vectorgraphics`

## Support

Please open issues in the repository that hosts this package and include:

- Unity version
- package version/tag
- full Console logs
- problematic Figma URL structure (remove private token data)

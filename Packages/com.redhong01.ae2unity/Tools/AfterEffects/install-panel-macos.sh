#!/usr/bin/env bash
set -euo pipefail

AE_APP_PATH="${1:-/Applications/Adobe After Effects 2026}"
AE_USER_VERSIONS="${AE_USER_VERSIONS:-26.2 26.0}"
APP_TARGET_DIR="$AE_APP_PATH/Scripts/ScriptUI Panels"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
SOURCE_FILE="$SCRIPT_DIR/AE2Unity.jsx"
TARGET_FILE_NAME="AE2Unity.jsx"
LEGACY_FILE_NAMES=("ae2unityshader.jsx" "AE2UnityShaderExport.jsx")

if [[ ! -f "$SOURCE_FILE" ]]; then
  echo "Missing source file: $SOURCE_FILE" >&2
  exit 1
fi

INSTALLED=0

if mkdir -p "$APP_TARGET_DIR" 2>/dev/null && cp "$SOURCE_FILE" "$APP_TARGET_DIR/$TARGET_FILE_NAME" 2>/dev/null; then
  for LEGACY_FILE_NAME in "${LEGACY_FILE_NAMES[@]}"; do
    rm -f "$APP_TARGET_DIR/$LEGACY_FILE_NAME" 2>/dev/null || true
  done
  echo "Installed AE2Unity panel:"
  echo "$APP_TARGET_DIR/$TARGET_FILE_NAME"
  INSTALLED=1
fi

for AE_USER_VERSION in $AE_USER_VERSIONS; do
  for ROOT in "$HOME/Library/Preferences/Adobe/After Effects" "$HOME/Library/Application Support/Adobe/After Effects"; do
    TARGET_DIR="$ROOT/$AE_USER_VERSION/Scripts/ScriptUI Panels"
    mkdir -p "$TARGET_DIR"
    cp "$SOURCE_FILE" "$TARGET_DIR/$TARGET_FILE_NAME"
    for LEGACY_FILE_NAME in "${LEGACY_FILE_NAMES[@]}"; do
      rm -f "$TARGET_DIR/$LEGACY_FILE_NAME" 2>/dev/null || true
    done
    echo "Installed AE2Unity panel:"
    echo "$TARGET_DIR/$TARGET_FILE_NAME"
    INSTALLED=1
  done
done

if [[ "$INSTALLED" -eq 0 ]]; then
  echo "Could not install AE2Unity panel." >&2
  exit 1
fi

echo "Restart After Effects, then open Window > AE2Unity.jsx"

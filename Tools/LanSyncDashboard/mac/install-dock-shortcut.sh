#!/bin/zsh
set -euo pipefail

APP_DIR="$HOME/Applications/Red LAN Sync.app"
EXECUTABLE="$APP_DIR/Contents/MacOS/RedLanSync"
PLIST="$APP_DIR/Contents/Info.plist"
RESOURCES="$APP_DIR/Contents/Resources"
SCRIPT_DIR="${0:A:h}"
SOURCE_ICNS="$SCRIPT_DIR/RedLanSync.icns"
ICON_GENERATOR="$SCRIPT_DIR/generate_app_icon.py"
ICONSET="$RESOURCES/RedLanSync.iconset"
ICNS="$RESOURCES/RedLanSync.icns"

mkdir -p "$APP_DIR/Contents/MacOS" "$RESOURCES"

cat > "$PLIST" <<'EOF'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleExecutable</key>
    <string>RedLanSync</string>
    <key>CFBundleIdentifier</key>
    <string>local.red.lansync.dashboard</string>
    <key>CFBundleName</key>
    <string>Red LAN Sync</string>
    <key>CFBundleDisplayName</key>
    <string>Red LAN Sync</string>
    <key>CFBundleIconFile</key>
    <string>RedLanSync</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleVersion</key>
    <string>1.0</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0</string>
</dict>
</plist>
EOF

if [[ -f "$SOURCE_ICNS" ]]; then
    cp "$SOURCE_ICNS" "$ICNS"
elif [[ -f "$ICON_GENERATOR" ]] && command -v python3 >/dev/null 2>&1 && [[ -x /usr/bin/iconutil ]]; then
    rm -rf "$ICONSET"
    python3 "$ICON_GENERATOR" "$ICONSET"
    /usr/bin/iconutil -c icns "$ICONSET" -o "$ICNS"
    rm -rf "$ICONSET"
fi

cat > "$EXECUTABLE" <<'EOF'
#!/bin/zsh
/usr/bin/open "http://127.0.0.1:8765"
EOF

chmod +x "$EXECUTABLE"
touch "$APP_DIR"

if ! /usr/bin/defaults read com.apple.dock persistent-apps 2>/dev/null | /usr/bin/grep -q "Red LAN Sync.app"; then
    /usr/bin/defaults write com.apple.dock persistent-apps -array-add "<dict><key>tile-data</key><dict><key>file-data</key><dict><key>_CFURLString</key><string>$APP_DIR</string><key>_CFURLStringType</key><integer>0</integer></dict></dict><key>tile-type</key><string>file-tile</string></dict>"
fi

/usr/bin/killall Dock >/dev/null 2>&1 || true

echo "Dock shortcut installed: $APP_DIR"

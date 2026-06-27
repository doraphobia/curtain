#!/bin/zsh
set -euo pipefail

SCRIPT_DIR=${0:A:h}
INSTALL_DIR="$HOME/Library/Application Support/RedLanSyncDashboard"
PLIST="$HOME/Library/LaunchAgents/com.redwang.lansyncdashboard.plist"
LOG_DIR="$HOME/Library/Logs"
PYTHON_BIN="$(command -v python3)"
BACKUP_DIR="$(mktemp -d)"

mkdir -p "$HOME/Library/LaunchAgents" "$LOG_DIR" "$INSTALL_DIR"

for runtime_file in config.json runtime-state.json; do
    if [[ -f "$INSTALL_DIR/$runtime_file" ]]; then
        cp "$INSTALL_DIR/$runtime_file" "$BACKUP_DIR/$runtime_file"
    fi
done

/usr/bin/ditto "$SCRIPT_DIR" "$INSTALL_DIR"

for runtime_file in config.json runtime-state.json; do
    if [[ -f "$BACKUP_DIR/$runtime_file" ]]; then
        cp "$BACKUP_DIR/$runtime_file" "$INSTALL_DIR/$runtime_file"
    fi
done
rm -rf "$BACKUP_DIR"

cat > "$PLIST" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>com.redwang.lansyncdashboard</string>
    <key>ProgramArguments</key>
    <array>
        <string>$PYTHON_BIN</string>
        <string>$INSTALL_DIR/server.py</string>
    </array>
    <key>WorkingDirectory</key>
    <string>$INSTALL_DIR</string>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <true/>
    <key>StandardOutPath</key>
    <string>$LOG_DIR/LanSyncDashboard.log</string>
    <key>StandardErrorPath</key>
    <string>$LOG_DIR/LanSyncDashboard.error.log</string>
</dict>
</plist>
EOF

launchctl bootout "gui/$UID/com.redwang.lansyncdashboard" 2>/dev/null || true
launchctl bootstrap "gui/$UID" "$PLIST"
launchctl enable "gui/$UID/com.redwang.lansyncdashboard"
launchctl kickstart -k "gui/$UID/com.redwang.lansyncdashboard"

echo "LAN Sync Dashboard installed: http://127.0.0.1:8765"

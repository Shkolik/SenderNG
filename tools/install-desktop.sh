#!/bin/sh
# Installs (or removes) a desktop-menu entry for the SenderNG bundle this
# script ships in. The bundle is a portable zip, and a .desktop file cannot
# carry a relative Exec= or Icon=, so the absolute path is baked in here at
# install time rather than shipped as a static file.
set -eu

DATA="${XDG_DATA_HOME:-$HOME/.local/share}"
ICON="$DATA/icons/hicolor/512x512/apps/SenderNG.png"
DESKTOP="$DATA/applications/SenderNG.desktop"

case "${1:-}" in
  --uninstall)
    rm -f "$DESKTOP" "$ICON"
    echo "SenderNG desktop entry removed."
    exit 0
    ;;
  "") ;;
  *) echo "usage: $0 [--uninstall]" >&2; exit 2 ;;
esac

BUNDLE_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
EXEC="$BUNDLE_DIR/SenderNG"
[ -x "$EXEC" ] || { echo "chmod +x $EXEC first" >&2; exit 1; }
[ -f "$BUNDLE_DIR/appIcon.png" ] || { echo "appIcon.png missing from $BUNDLE_DIR" >&2; exit 1; }

mkdir -p "$(dirname "$ICON")" "$(dirname "$DESKTOP")"
cp "$BUNDLE_DIR/appIcon.png" "$ICON"

cat > "$DESKTOP" <<EOF
[Desktop Entry]
Type=Application
Name=SenderNG
Comment=Modern. Extensible. Cross-platform. grblHAL gcode sender
Exec="$EXEC"
Icon=SenderNG
Terminal=false
StartupWMClass=SenderNG
Categories=Development;
EOF
chmod +x "$DESKTOP"

echo "Installed. A DE restart (or update-desktop-database) may be needed."

#!/usr/bin/env bash
# Crea il pacchetto .deb di IP-DESK-CTRL.
# Uso: packaging/linux/make-deb.sh <rid> <versione> <cartella con l'eseguibile> <cartella di uscita>
set -euo pipefail
RID="$1"; VERSION="$2"; SRC="$3"; OUT="$4"
case "$RID" in
  linux-x64)   ARCH=amd64 ;;
  linux-arm64) ARCH=arm64 ;;
  linux-arm)   ARCH=armhf ;;
  *) echo "rid non gestito: $RID"; exit 1 ;;
esac
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PKG="$(mktemp -d)/ip-desk-ctrl_${VERSION}_${ARCH}"

install -d "$PKG/DEBIAN" "$PKG/opt/ip-desk-ctrl" "$PKG/usr/bin" "$PKG/usr/share/applications" \
           "$PKG/usr/share/icons/hicolor/256x256/apps" "$PKG/usr/share/doc/ip-desk-ctrl" \
           "$PKG/usr/lib/udev/rules.d" "$PKG/usr/lib/modules-load.d"
install -m 755 "$SRC/IP-DESK-CTRL" "$PKG/opt/ip-desk-ctrl/IP-DESK-CTRL"
ln -s /opt/ip-desk-ctrl/IP-DESK-CTRL "$PKG/usr/bin/ip-desk-ctrl"
install -m 644 "$ROOT/src/IpDeskCtrl/Assets/icon.png" "$PKG/usr/share/icons/hicolor/256x256/apps/ip-desk-ctrl.png"
{ printf "IP-DESK-CTRL - Copyright (C) 2026 IC8TEM\nLicenza: GNU GPL 3.0 o successiva (/usr/share/common-licenses/GPL-3)\n\n"; cat "$ROOT/THIRD-PARTY-NOTICES.txt"; } > "$PKG/usr/share/doc/ip-desk-ctrl/copyright"
chmod 644 "$PKG/usr/share/doc/ip-desk-ctrl/copyright"
install -m 644 "$ROOT/README.md" "$PKG/usr/share/doc/ip-desk-ctrl/README.md"

# Wayland: mouse e tastiera passano dal dispositivo virtuale /dev/uinput, che la regola dà all'utente seduto al PC
cat > "$PKG/usr/lib/udev/rules.d/70-ip-desk-ctrl-uinput.rules" <<'RULE'
KERNEL=="uinput", SUBSYSTEM=="misc", TAG+="uaccess", OPTIONS+="static_node=uinput"
RULE
echo "uinput" > "$PKG/usr/lib/modules-load.d/ip-desk-ctrl.conf"

cat > "$PKG/usr/share/applications/ip-desk-ctrl.desktop" <<DESK
[Desktop Entry]
Type=Application
Name=IP-DESK-CTRL
Comment=Desktop remoto cifrato: vedi e comanda un altro PC con un numero di 10 cifre
Exec=/opt/ip-desk-ctrl/IP-DESK-CTRL
Icon=ip-desk-ctrl
Terminal=false
Categories=Network;RemoteAccess;
DESK

SIZE=$(du -sk "$PKG" | cut -f1)
cat > "$PKG/DEBIAN/control" <<CTRL
Package: ip-desk-ctrl
Version: ${VERSION}
Section: net
Priority: optional
Architecture: ${ARCH}
Installed-Size: ${SIZE}
Depends: libfontconfig1, libx11-6, libxtst6, libxrandr2, libice6, libsm6
Recommends: pulseaudio-utils | pipewire-pulse, grim, wlr-randr, ffmpeg, x11-xserver-utils, gvfs-bin | libglib2.0-bin
Maintainer: ic8tem <ic8tem@users.noreply.github.com>
Homepage: https://github.com/ic8tem/IP-DESK-CTRL
Description: desktop remoto cifrato con numero fisso di 10 cifre
 Vedi e comanda un altro PC (Windows, macOS, Linux, Raspberry Pi) con
 cifratura AES-256-GCM, monitor multipli e audio del sistema. Ritrovo sui
 broker MQTT, porte aperte con UPnP, collegamento inverso e UDP.
CTRL

cat > "$PKG/DEBIAN/postinst" <<'POST'
#!/bin/sh
set -e
modprobe uinput 2>/dev/null || true
command -v udevadm >/dev/null 2>&1 && { udevadm control --reload-rules || true; udevadm trigger --name-match=uinput 2>/dev/null || true; }
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database -q || true
command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q /usr/share/icons/hicolor || true
exit 0
POST
chmod 755 "$PKG/DEBIAN/postinst"

mkdir -p "$OUT"
dpkg-deb --root-owner-group --build "$PKG" "$OUT/ip-desk-ctrl_${VERSION}_${ARCH}.deb"

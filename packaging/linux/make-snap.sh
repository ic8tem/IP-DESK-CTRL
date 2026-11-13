#!/usr/bin/env bash
# Crea il pacchetto Snap di IP-DESK-CTRL (confinamento «classic»: serve accedere a schermo, mouse e tastiera).
# Si installa con: sudo snap install --dangerous --classic ip-desk-ctrl_<versione>_<arch>.snap
# Uso: packaging/linux/make-snap.sh <rid> <versione> <cartella con l'eseguibile> <cartella di uscita>
set -euo pipefail
RID="$1"; VERSION="$2"; SRC="$(cd "$3" && pwd)"; mkdir -p "$4"; OUT="$(cd "$4" && pwd)"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
W="$(mktemp -d)"
mkdir -p "$W/files/bin" "$W/snap/gui"
install -m 755 "$SRC/IP-DESK-CTRL" "$W/files/bin/IP-DESK-CTRL"
cp "$ROOT/src/IpDeskCtrl/Assets/icon.png" "$W/snap/gui/ip-desk-ctrl.png"
cat > "$W/snap/gui/ip-desk-ctrl.desktop" <<'DESK'
[Desktop Entry]
Type=Application
Name=IP-DESK-CTRL
Comment=Desktop remoto cifrato: vedi e comanda un altro PC con un numero di 10 cifre
Exec=ip-desk-ctrl
Icon=${SNAP}/meta/gui/ip-desk-ctrl.png
Terminal=false
Categories=Network;RemoteAccess;
DESK
cat > "$W/snap/snapcraft.yaml" <<YAML
name: ip-desk-ctrl
version: '${VERSION}'
summary: Desktop remoto cifrato con numero fisso di 10 cifre
description: |
  Vedi e comanda un altro PC (Windows, macOS, Linux, Raspberry Pi, Android) con cifratura
  AES-256-GCM. Ritrovo sui broker MQTT, porte aperte con UPnP, collegamento inverso e UDP.
base: core24
grade: stable
confinement: classic
license: GPL-3.0-or-later
apps:
  ip-desk-ctrl:
    command: bin/IP-DESK-CTRL
parts:
  ip-desk-ctrl:
    plugin: dump
    source: files
YAML
cd "$W"
sudo snapcraft pack --destructive-mode --output "$OUT/"
sudo chown "$(id -u):$(id -g)" "$OUT"/*.snap
ls -l "$OUT"/*.snap

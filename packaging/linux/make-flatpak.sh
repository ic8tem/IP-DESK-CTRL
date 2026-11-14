#!/usr/bin/env bash
# Crea il pacchetto Flatpak di IP-DESK-CTRL (un file .flatpak da installare a mano).
# Si installa con: flatpak install --user ./IP-DESK-CTRL-<versione>-<arch>.flatpak
# Il programma ha bisogno di vedere lo schermo e di muovere mouse e tastiera: per questo la sandbox è larga
# (X11, tutti i dispositivi, la cartella home, la rete). Il permesso org.freedesktop.Flatpak serve solo
# all'aggiornamento automatico, che installa il nuovo .flatpak con «flatpak-spawn --host flatpak install».
# Uso: packaging/linux/make-flatpak.sh <rid> <versione> <cartella con l'eseguibile> <cartella di uscita>
set -euo pipefail
RID="$1"; VERSION="$2"; SRC="$(cd "$3" && pwd)"; mkdir -p "$4"; OUT="$(cd "$4" && pwd)"
case "$RID" in
  linux-x64)   ARCH=x86_64 ;;
  linux-arm64) ARCH=aarch64 ;;
  *) echo "rid non gestito: $RID"; exit 1 ;;
esac
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
ID=io.github.ic8tem.IpDeskCtrl
W="$(mktemp -d)"
mkdir -p "$W/files"
install -m 755 "$SRC/IP-DESK-CTRL" "$W/files/IP-DESK-CTRL"
cp "$ROOT/src/IpDeskCtrl/Assets/icon.png" "$W/files/$ID.png"
cat > "$W/files/$ID.desktop" <<DESK
[Desktop Entry]
Type=Application
Name=IP-DESK-CTRL
Comment=Desktop remoto cifrato: vedi e comanda un altro PC con un numero di 10 cifre
Exec=ip-desk-ctrl
Icon=$ID
Terminal=false
Categories=Network;RemoteAccess;
DESK
cat > "$W/files/$ID.metainfo.xml" <<XML
<?xml version="1.0" encoding="UTF-8"?>
<component type="desktop-application">
  <id>$ID</id>
  <name>IP-DESK-CTRL</name>
  <summary>Desktop remoto cifrato con numero fisso di 10 cifre</summary>
  <metadata_license>CC0-1.0</metadata_license>
  <project_license>GPL-3.0-or-later</project_license>
  <developer id="io.github.ic8tem"><name>Costantino IC8TEM</name></developer>
  <description>
    <p>Vedi e comanda un altro PC (Windows, macOS, Linux, Raspberry Pi, Android) con cifratura AES-256-GCM.
    Ritrovo sui broker MQTT, porte aperte con UPnP, collegamento inverso e UDP.</p>
  </description>
  <url type="homepage">https://github.com/ic8tem/IP-DESK-CTRL</url>
  <url type="donation">https://ko-fi.com/ic8tem</url>
  <launchable type="desktop-id">$ID.desktop</launchable>
  <content_rating type="oars-1.1"/>
  <releases><release version="$VERSION" date="$(date -u +%Y-%m-%d)"/></releases>
</component>
XML
cat > "$W/$ID.yml" <<YAML
app-id: $ID
runtime: org.freedesktop.Platform
runtime-version: '24.08'
sdk: org.freedesktop.Sdk
command: ip-desk-ctrl
finish-args:
  - --share=network
  - --share=ipc
  - --socket=x11
  - --socket=wayland
  - --socket=pulseaudio
  - --device=all
  - --filesystem=home
  - --filesystem=xdg-download
  - --talk-name=org.kde.StatusNotifierWatcher
  - --talk-name=org.freedesktop.Notifications
  - --talk-name=org.freedesktop.Flatpak
modules:
  - name: ip-desk-ctrl
    buildsystem: simple
    build-commands:
      - install -Dm755 IP-DESK-CTRL /app/lib/ip-desk-ctrl/IP-DESK-CTRL
      - mkdir -p /app/bin && ln -s /app/lib/ip-desk-ctrl/IP-DESK-CTRL /app/bin/ip-desk-ctrl
      - install -Dm644 $ID.png /app/share/icons/hicolor/256x256/apps/$ID.png
      - install -Dm644 $ID.desktop /app/share/applications/$ID.desktop
      - install -Dm644 $ID.metainfo.xml /app/share/metainfo/$ID.metainfo.xml
    sources:
      - type: dir
        path: files
YAML
cd "$W"
flatpak remote-add --user --if-not-exists flathub https://dl.flathub.org/repo/flathub.flatpakrepo
flatpak-builder --user --install-deps-from=flathub --disable-rofiles-fuse --force-clean --repo=repo build "$ID.yml"
flatpak build-bundle --runtime-repo=https://dl.flathub.org/repo/flathub.flatpakrepo repo "$OUT/IP-DESK-CTRL-${VERSION}-${ARCH}.flatpak" "$ID"
ls -l "$OUT/IP-DESK-CTRL-${VERSION}-${ARCH}.flatpak"

#!/usr/bin/env bash
# Crea l'AppImage di IP-DESK-CTRL: un solo file che gira su quasi tutte le distribuzioni Linux con glibc.
# Le impostazioni stanno in ~/.config/IP-DESK-CTRL come negli altri pacchetti; l'aggiornamento automatico
# sostituisce il file .AppImage stesso.
# Uso: packaging/linux/make-appimage.sh <rid> <versione> <cartella con l'eseguibile> <cartella di uscita>
set -euo pipefail
RID="$1"; VERSION="$2"; SRC="$3"; mkdir -p "$4"; OUT="$(cd "$4" && pwd)"
case "$RID" in
  linux-x64)   ARCH=x86_64 ;;
  linux-arm64) ARCH=aarch64 ;;
  *) echo "rid non gestito: $RID"; exit 1 ;;
esac
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
A="$(mktemp -d)/IP-DESK-CTRL.AppDir"
mkdir -p "$A/usr/bin" "$A/usr/share/icons/hicolor/256x256/apps"
install -m 755 "$SRC/IP-DESK-CTRL" "$A/usr/bin/IP-DESK-CTRL"
cp "$ROOT/src/IpDeskCtrl/Assets/icon.png" "$A/ip-desk-ctrl.png"
cp "$ROOT/src/IpDeskCtrl/Assets/icon.png" "$A/usr/share/icons/hicolor/256x256/apps/ip-desk-ctrl.png"
cat > "$A/ip-desk-ctrl.desktop" <<'DESK'
[Desktop Entry]
Type=Application
Name=IP-DESK-CTRL
Comment=Desktop remoto cifrato: vedi e comanda un altro PC con un numero di 10 cifre
Exec=IP-DESK-CTRL
Icon=ip-desk-ctrl
Terminal=false
Categories=Network;RemoteAccess;
DESK
cat > "$A/AppRun" <<'RUN'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/bin/IP-DESK-CTRL" "$@"
RUN
chmod 755 "$A/AppRun"
T="$(mktemp -d)"
curl -fsSL -o "$T/appimagetool" "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-$(uname -m).AppImage"
chmod +x "$T/appimagetool"
ARCH=$ARCH "$T/appimagetool" --appimage-extract-and-run "$A" "$OUT/IP-DESK-CTRL-${VERSION}-${ARCH}.AppImage"
ls -l "$OUT/IP-DESK-CTRL-${VERSION}-${ARCH}.AppImage"

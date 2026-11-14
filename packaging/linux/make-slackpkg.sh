#!/usr/bin/env bash
# Crea il pacchetto Slackware (.txz) di IP-DESK-CTRL: Slackware ARM64 (anche su Raspberry Pi) e Slackware64.
# Uso: packaging/linux/make-slackpkg.sh <rid> <versione> <cartella con l'eseguibile> <cartella di uscita>
# Si installa con «installpkg» e si aggiorna con «upgradepkg».
set -euo pipefail
RID="$1"; VERSION="$2"; SRC="$3"; mkdir -p "$4"; OUT="$(cd "$4" && pwd)"
case "$RID" in
  linux-x64)   ARCH=x86_64 ;;
  linux-arm64) ARCH=aarch64 ;;
  linux-arm)   ARCH=arm ;;
  *) echo "rid non gestito: $RID"; exit 1 ;;
esac
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PKG="$(mktemp -d)/pkg"

install -d "$PKG/opt/ip-desk-ctrl" "$PKG/usr/bin" "$PKG/usr/share/applications" "$PKG/usr/share/icons/hicolor/256x256/apps" \
           "$PKG/usr/doc/ip-desk-ctrl-$VERSION" "$PKG/lib/udev/rules.d" "$PKG/install"
install -m 755 "$SRC/IP-DESK-CTRL" "$PKG/opt/ip-desk-ctrl/IP-DESK-CTRL"
install -m 755 "$ROOT/packaging/linux/aggiorna-root.sh" "$PKG/opt/ip-desk-ctrl/aggiorna-root.sh"
install -D -m 644 "$ROOT/packaging/linux/io.github.ic8tem.ipdeskctrl.aggiorna.policy" "$PKG/usr/share/polkit-1/actions/io.github.ic8tem.ipdeskctrl.aggiorna.policy"
install -m 644 "$ROOT/src/IpDeskCtrl/Assets/icon.png" "$PKG/usr/share/icons/hicolor/256x256/apps/ip-desk-ctrl.png"
install -m 644 "$ROOT/README.md" "$ROOT/THIRD-PARTY-NOTICES.txt" "$ROOT/LICENSE" "$PKG/usr/doc/ip-desk-ctrl-$VERSION/"
echo 'KERNEL=="uinput", SUBSYSTEM=="misc", TAG+="uaccess", OPTIONS+="static_node=uinput"' > "$PKG/lib/udev/rules.d/70-ip-desk-ctrl-uinput.rules"
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

# slack-desc: 11 righe che cominciano col nome del pacchetto
cat > "$PKG/install/slack-desc" <<'DESC'
            |-----handy-ruler------------------------------------------------------|
ip-desk-ctrl: ip-desk-ctrl (desktop remoto cifrato)
ip-desk-ctrl:
ip-desk-ctrl: Vedi e comanda un altro PC (Windows, macOS, Linux, Raspberry Pi)
ip-desk-ctrl: con un numero fisso di 10 cifre e cifratura AES-256-GCM.
ip-desk-ctrl: Ritrovo sui broker MQTT, porte aperte con UPnP, collegamento
ip-desk-ctrl: inverso e UDP. Monitor multipli, audio, file, tunnel di porte.
ip-desk-ctrl: Video compresso con ffmpeg (consigliato).
ip-desk-ctrl:
ip-desk-ctrl: Autore: IC8TEM - https://github.com/ic8tem/IP-DESK-CTRL
ip-desk-ctrl: Licenza: GNU GPL 3.0 o successiva
ip-desk-ctrl:
DESC

# doinst.sh: i collegamenti simbolici li crea l'installazione (stile Slackware)
cat > "$PKG/install/doinst.sh" <<'INST'
( cd usr/bin ; rm -rf ip-desk-ctrl )
( cd usr/bin ; ln -sf /opt/ip-desk-ctrl/IP-DESK-CTRL ip-desk-ctrl )
if [ -x /usr/bin/update-desktop-database ]; then /usr/bin/update-desktop-database -q usr/share/applications >/dev/null 2>&1; fi
if [ -x /usr/bin/gtk-update-icon-cache ]; then /usr/bin/gtk-update-icon-cache -f -t -q usr/share/icons/hicolor >/dev/null 2>&1; fi
if [ -x /sbin/udevadm ]; then /sbin/udevadm control --reload-rules >/dev/null 2>&1; fi
/sbin/modprobe uinput >/dev/null 2>&1 || true
INST

NAME="ip-desk-ctrl-${VERSION}-${ARCH}-1_ic8tem.txz"
( cd "$PKG" && tar --owner=root --group=root -cJf "$OUT/$NAME" . )
ls -l "$OUT/$NAME"

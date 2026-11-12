#!/usr/bin/env bash
# Crea il pacchetto .rpm di IP-DESK-CTRL (Fedora, openSUSE, RHEL/Alma/Rocky, Raspberry con Fedora).
# Uso: packaging/linux/make-rpm.sh <rid> <versione> <cartella con l'eseguibile> <cartella di uscita>
# Serve rpmbuild (su Ubuntu: sudo apt-get install rpm).
set -euo pipefail
RID="$1"; VERSION="$2"; SRC="$(cd "$3" && pwd)"; OUT="$4"
case "$RID" in
  linux-x64)   ARCH=x86_64;  LIB="()(64bit)" ;;
  linux-arm64) ARCH=aarch64; LIB="()(64bit)" ;;
  linux-arm)   ARCH=armv7hl; LIB="" ;;
  *) echo "rid non gestito: $RID"; exit 1 ;;
esac
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
TOP="$(mktemp -d)"
mkdir -p "$TOP"/{BUILD,RPMS,SOURCES,SPECS,SRPMS} "$OUT"
OUT="$(cd "$OUT" && pwd)"

cat > "$TOP/SPECS/ip-desk-ctrl.spec" <<SPEC
%define __strip /bin/true
%define debug_package %{nil}
%define _build_id_links none
%define __os_install_post %{nil}

Name:           ip-desk-ctrl
Version:        ${VERSION}
Release:        1
Summary:        Desktop remoto cifrato con numero fisso di 10 cifre
License:        GPL-3.0-or-later
URL:            https://github.com/ic8tem/IP-DESK-CTRL
AutoReqProv:    no
Requires:       libfontconfig.so.1${LIB}, libX11.so.6${LIB}, libXtst.so.6${LIB}, libXrandr.so.2${LIB}, libICE.so.6${LIB}, libSM.so.6${LIB}
Recommends:     (ffmpeg or ffmpeg-free), xrandr, pulseaudio-utils, grim, glib2

%description
Vedi e comanda un altro PC (Windows, macOS, Linux, Raspberry Pi) con
cifratura AES-256-GCM, monitor multipli e audio del sistema. Ritrovo sui
broker MQTT, porte aperte con UPnP, collegamento inverso e UDP.

%install
install -d %{buildroot}/opt/ip-desk-ctrl %{buildroot}/usr/bin %{buildroot}/usr/share/applications \
           %{buildroot}/usr/share/icons/hicolor/256x256/apps %{buildroot}/usr/share/doc/ip-desk-ctrl \
           %{buildroot}/usr/lib/udev/rules.d %{buildroot}/usr/lib/modules-load.d
install -m 755 "${SRC}/IP-DESK-CTRL" %{buildroot}/opt/ip-desk-ctrl/IP-DESK-CTRL
ln -s /opt/ip-desk-ctrl/IP-DESK-CTRL %{buildroot}/usr/bin/ip-desk-ctrl
install -m 644 "${ROOT}/src/IpDeskCtrl/Assets/icon.png" %{buildroot}/usr/share/icons/hicolor/256x256/apps/ip-desk-ctrl.png
install -m 644 "${ROOT}/README.md" "${ROOT}/THIRD-PARTY-NOTICES.txt" "${ROOT}/LICENSE" %{buildroot}/usr/share/doc/ip-desk-ctrl/
echo 'KERNEL=="uinput", SUBSYSTEM=="misc", TAG+="uaccess", OPTIONS+="static_node=uinput"' > %{buildroot}/usr/lib/udev/rules.d/70-ip-desk-ctrl-uinput.rules
echo uinput > %{buildroot}/usr/lib/modules-load.d/ip-desk-ctrl.conf
cat > %{buildroot}/usr/share/applications/ip-desk-ctrl.desktop <<DESK
[Desktop Entry]
Type=Application
Name=IP-DESK-CTRL
Comment=Desktop remoto cifrato: vedi e comanda un altro PC con un numero di 10 cifre
Exec=/opt/ip-desk-ctrl/IP-DESK-CTRL
Icon=ip-desk-ctrl
Terminal=false
Categories=Network;RemoteAccess;
DESK

%post
modprobe uinput 2>/dev/null || true
command -v udevadm >/dev/null 2>&1 && { udevadm control --reload-rules || true; udevadm trigger --name-match=uinput 2>/dev/null || true; }
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database -q || true
command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q /usr/share/icons/hicolor || true
exit 0

%files
/opt/ip-desk-ctrl
/usr/bin/ip-desk-ctrl
/usr/share/applications/ip-desk-ctrl.desktop
/usr/share/icons/hicolor/256x256/apps/ip-desk-ctrl.png
/usr/share/doc/ip-desk-ctrl
/usr/lib/udev/rules.d/70-ip-desk-ctrl-uinput.rules
/usr/lib/modules-load.d/ip-desk-ctrl.conf
SPEC

rpmbuild --define "_topdir $TOP" --target "${ARCH}-linux" -bb "$TOP/SPECS/ip-desk-ctrl.spec" >/dev/null
cp "$TOP"/RPMS/*/ip-desk-ctrl-"${VERSION}"-1.*.rpm "$OUT/"
ls -l "$OUT"/*.rpm

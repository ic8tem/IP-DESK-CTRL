#!/usr/bin/env bash
# Crea il pacchetto per Arch Linux / Manjaro / EndeavourOS / Arch Linux ARM (.pkg.tar.zst), con gli stessi file
# del .deb dello stesso rid. Si costruisce con makepkg in un contenitore Docker archlinux.
# Si installa con: sudo pacman -U ip-desk-ctrl-<versione>-1-<arch>.pkg.tar.zst
# Uso: packaging/linux/make-arch.sh <rid> <versione> <file .deb> <cartella di uscita>
set -euo pipefail
RID="$1"; VERSION="$2"; DEB="$3"; mkdir -p "$4"; OUT="$(cd "$4" && pwd)"
case "$RID" in
  linux-x64)   ARCH=x86_64 ;;
  linux-arm64) ARCH=aarch64 ;;
  *) echo "rid non gestito: $RID"; exit 1 ;;
esac
W="$(mktemp -d)"
mkdir -p "$W/albero"
dpkg-deb -x "$DEB" "$W/albero"
PV="${VERSION//-/_}"
cat > "$W/PKGBUILD" <<PKG
pkgname=ip-desk-ctrl
pkgver=${PV}
pkgrel=1
pkgdesc="Desktop remoto cifrato con numero fisso di 10 cifre (IC8TEM)"
arch=('${ARCH}')
url="https://github.com/ic8tem/IP-DESK-CTRL"
license=('GPL-3.0-or-later')
depends=('fontconfig' 'libx11' 'libxtst' 'libxrandr' 'libice' 'libsm')
optdepends=('ffmpeg: video compresso H.264/H.265/AV1' 'xorg-xrandr: risoluzione adatta' 'libpulse: audio' 'glib2: cartella condivisa')
options=('!strip' '!debug')
package() {
  cp -a "\$startdir/albero/." "\$pkgdir/"
}
PKG
# il contenitore archlinux esiste solo per x86_64: sui runner ARM gira emulato con qemu
if [ "$(uname -m)" != "x86_64" ]; then docker run --privileged --rm tonistiigi/binfmt --install amd64 >/dev/null; fi
docker run --rm --platform linux/amd64 -v "$W:/w" archlinux:latest bash -c "
  set -e
  pacman --disable-sandbox -Sy --noconfirm --needed base-devel >/dev/null
  sed -i 's/^CARCH=.*/CARCH=\"${ARCH}\"/' /etc/makepkg.conf
  useradd -m b && chown -R b /w
  su b -c 'cd /w && makepkg --nodeps --skipinteg -f'
  chmod -R a+rwX /w
"
cp "$W"/ip-desk-ctrl-*.pkg.tar.zst "$OUT/"
ls -l "$OUT"/ip-desk-ctrl-*.pkg.tar.zst

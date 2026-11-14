#!/bin/sh
# IP-DESK-CTRL - aggiornamento dei pacchetti di sistema senza password di amministratore.
# Lo chiama il programma con pkexec; la regola polkit io.github.ic8tem.ipdeskctrl.aggiorna lo lascia girare
# senza password solo all'utente della sessione attiva (davanti al PC o che lo comanda da IP-DESK-CTRL).
#
# Per sicurezza NON installa un file passato dall'utente: riceve solo versione e nome del file, li controlla,
# scarica il pacchetto dalla release ufficiale su GitHub, ne verifica l'impronta SHA-256 dichiarata da GitHub
# e accetta solo versioni più nuove di quella installata.
# Uso: aggiorna-root.sh <versione> <nome del file della release>
set -eu
VER="${1:-}"; NAME="${2:-}"
REPO="ic8tem/IP-DESK-CTRL"
case "$VER" in ''|*[!0-9.]*) echo "versione non valida"; exit 2 ;; esac
case "$NAME" in
  ip-desk-ctrl_"$VER"_amd64.deb|ip-desk-ctrl_"$VER"_arm64.deb|ip-desk-ctrl_"$VER"_armhf.deb) KIND=deb ;;
  ip-desk-ctrl-"$VER"-1.x86_64.rpm|ip-desk-ctrl-"$VER"-1.aarch64.rpm|ip-desk-ctrl-"$VER"-1.armv7hl.rpm) KIND=rpm ;;
  ip-desk-ctrl-"$VER"-1-x86_64.pkg.tar.zst|ip-desk-ctrl-"$VER"-1-aarch64.pkg.tar.zst) KIND=arch ;;
  ip-desk-ctrl-"$VER"-x86_64-1_ic8tem.txz|ip-desk-ctrl-"$VER"-aarch64-1_ic8tem.txz) KIND=slack ;;
  *) echo "file non previsto: $NAME"; exit 2 ;;
esac

# solo versioni più nuove di quella installata
CUR="$(/opt/ip-desk-ctrl/IP-DESK-CTRL --versione 2>/dev/null || true)"
if [ -n "$CUR" ]; then
  NEWEST="$(printf '%s\n%s\n' "$CUR" "$VER" | sort -V | tail -n 1)"
  if [ "$NEWEST" != "$VER" ] || [ "$CUR" = "$VER" ]; then echo "la $VER non è più nuova della $CUR"; exit 3; fi
fi

get() { if command -v curl >/dev/null 2>&1; then curl -fsSL -A IP-DESK-CTRL -o "$2" "$1"; else wget -q -U IP-DESK-CTRL -O "$2" "$1"; fi; }
DIR="$(mktemp -d /tmp/ip-desk-ctrl-aggiorna.XXXXXX)"
trap 'rm -rf "$DIR"' EXIT
get "https://api.github.com/repos/$REPO/releases/tags/v$VER" "$DIR/release.json"
# impronta dichiarata da GitHub per quel file ("digest": "sha256:…" dell'asset con quel nome)
WANT="$(tr -d '\n\r\t ' < "$DIR/release.json" | sed 's/},{/}\n{/g' | grep "\"name\":\"$NAME\"" | sed -n 's/.*"digest":"sha256:\([0-9a-f]\{64\}\)".*/\1/p' | head -n 1)"
[ -n "$WANT" ] || { echo "impronta non trovata su GitHub"; exit 4; }
get "https://github.com/$REPO/releases/download/v$VER/$NAME" "$DIR/$NAME"
HAVE="$(sha256sum "$DIR/$NAME" | cut -d' ' -f1)"
[ "$HAVE" = "$WANT" ] || { echo "impronta diversa: file non installato"; exit 5; }

case "$KIND" in
  deb)   apt-get install -y "$DIR/$NAME" || dpkg -i "$DIR/$NAME" ;;
  rpm)   if command -v dnf >/dev/null 2>&1; then dnf install -y "$DIR/$NAME";
         elif command -v zypper >/dev/null 2>&1; then zypper --non-interactive install --allow-unsigned-rpm "$DIR/$NAME";
         else rpm -Uvh "$DIR/$NAME"; fi ;;
  arch)  pacman -U --noconfirm "$DIR/$NAME" ;;
  slack) /sbin/upgradepkg --install-new "$DIR/$NAME" ;;
esac

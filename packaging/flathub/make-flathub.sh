#!/usr/bin/env bash
# Prepara i file per Flathub (repository flathub/io.github.ic8tem.IpDeskCtrl):
#   io.github.ic8tem.IpDeskCtrl.yml   manifest: compila dai sorgenti con il .NET 8 dell'SDK di Flathub
#   nuget-sources.json                 pacchetti NuGet scaricati prima (sui server di Flathub non c'è rete)
# Uso: packaging/flathub/make-flathub.sh <versione> <commit | "locale"> <cartella di uscita>
#   con «locale» il manifest compila la cartella del repository (prova in CI), altrimenti il tag v<versione>.
set -euo pipefail
VERSION="$1"; COMMIT="$2"; mkdir -p "$3"; OUT="$(cd "$3" && pwd)"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
ID=io.github.ic8tem.IpDeskCtrl
PROJ=src/IpDeskCtrl.Desktop/IpDeskCtrl.Desktop.csproj

# ------------------------------------------------------------------ pacchetti NuGet, ripristinati con il .NET dell'SDK di Flathub
flatpak remote-add --user --if-not-exists flathub https://dl.flathub.org/repo/flathub.flatpakrepo
flatpak install --user -y --noninteractive flathub org.freedesktop.Sdk//24.08 org.freedesktop.Sdk.Extension.dotnet8//24.08 >/dev/null
PKGS="$(mktemp -d)"
for rid in linux-x64 linux-arm64; do
  flatpak run --env=DOTNET_CLI_TELEMETRY_OPTOUT=true --env=DOTNET_NOLOGO=true --command=sh \
    --runtime=org.freedesktop.Sdk//24.08 --share=network --filesystem=host --filesystem="$PKGS" \
    org.freedesktop.Sdk.Extension.dotnet8//24.08 -c \
    'PATH="$PATH:/usr/lib/sdk/dotnet8/bin" LD_LIBRARY_PATH="${LD_LIBRARY_PATH:-}:/usr/lib/sdk/dotnet8/lib" exec dotnet restore "$@"' \
    -- "$ROOT/$PROJ" -r "$rid" --packages "$PKGS"
done
python3 - "$PKGS" "$OUT/nuget-sources.json" <<'PY'
import base64, glob, json, os, sys
pk, out = sys.argv[1], sys.argv[2]
src = []
for f in sorted(glob.glob(os.path.join(pk, "*", "*", "*.nupkg.sha512"))):
    ver = os.path.basename(os.path.dirname(f)); name = os.path.basename(os.path.dirname(os.path.dirname(f)))
    sha = base64.b64decode(open(f).read().strip()).hex()
    src.append({"type": "file", "url": f"https://api.nuget.org/v3-flatcontainer/{name}/{ver}/{name}.{ver}.nupkg",
                "sha512": sha, "dest": "nuget-sources", "dest-filename": f"{name}.{ver}.nupkg"})
json.dump(src, open(out, "w"), indent=4)
print(len(src), "pacchetti NuGet")
PY

# ------------------------------------------------------------------ manifest
if [ "$COMMIT" = "locale" ]; then
  SOURCE="      - type: dir
        path: $ROOT"
else
  SOURCE="      - type: git
        url: https://github.com/ic8tem/IP-DESK-CTRL.git
        tag: v$VERSION
        commit: $COMMIT"
fi
cat > "$OUT/$ID.yml" <<YAML
id: $ID
runtime: org.freedesktop.Platform
runtime-version: '24.08'
sdk: org.freedesktop.Sdk
sdk-extensions:
  - org.freedesktop.Sdk.Extension.dotnet8
command: ip-desk-ctrl
finish-args:
  # rete: ritrovo sui broker MQTT, collegamento diretto e UDP tra i due PC
  - --share=network
  - --share=ipc
  # lo schermo si cattura e si comanda (XTest) solo nelle sessioni X11
  - --socket=x11
  - --device=dri
  # audio del PC remoto
  - --socket=pulseaudio
  # file mandati e ricevuti durante la sessione (cartella scelta dall'utente)
  - --filesystem=home
  - --talk-name=org.kde.StatusNotifierWatcher
  # aggiornamenti da Flathub: l'aggiornamento automatico interno resta spento
  - --env=IPDESKCTRL_FLATHUB=1
  - --env=DOTNET_CLI_TELEMETRY_OPTOUT=1
build-options:
  append-path: /usr/lib/sdk/dotnet8/bin
  append-ld-library-path: /usr/lib/sdk/dotnet8/lib
  env:
    DOTNET_CLI_TELEMETRY_OPTOUT: 'true'
    DOTNET_NOLOGO: 'true'
  arch:
    x86_64:
      env:
        RUNTIME: linux-x64
    aarch64:
      env:
        RUNTIME: linux-arm64
modules:
  - name: ip-desk-ctrl
    buildsystem: simple
    sources:
$SOURCE
      - nuget-sources.json
    build-commands:
      - dotnet publish $PROJ -c Release -r \$RUNTIME --source ./nuget-sources -p:Version=$VERSION -o out
      - install -Dm755 out/IP-DESK-CTRL /app/lib/ip-desk-ctrl/IP-DESK-CTRL
      - mkdir -p /app/bin && ln -s /app/lib/ip-desk-ctrl/IP-DESK-CTRL /app/bin/ip-desk-ctrl
      - install -Dm644 src/IpDeskCtrl/Assets/icon.png /app/share/icons/hicolor/256x256/apps/$ID.png
      - install -Dm644 packaging/flathub/$ID.desktop /app/share/applications/$ID.desktop
      - mkdir -p /app/share/metainfo
      - python3 packaging/flathub/releases.py docs/wiki/Storia-delle-versioni.md packaging/flathub/$ID.metainfo.xml > /app/share/metainfo/$ID.metainfo.xml
YAML
ls -l "$OUT"

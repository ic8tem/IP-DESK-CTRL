#!/usr/bin/env bash
# Scrive i tre file del manifesto winget (ic8tem.IP-DESK-CTRL) per una versione, con le impronte dei setup.
# La prima volta vanno proposti a mano a github.com/microsoft/winget-pkgs (manifests/i/ic8tem/IP-DESK-CTRL/<versione>);
# dopo si possono aggiornare con «wingetcreate update».
# Uso: packaging/winget/make-winget.sh <versione> <cartella con i setup .exe> <cartella di uscita>
set -euo pipefail
V="$1"; IN="$2"; OUT="$3/$V"; mkdir -p "$OUT"
ID=ic8tem.IP-DESK-CTRL
U="https://github.com/ic8tem/IP-DESK-CTRL/releases/download/v$V"
h() { sha256sum "$IN/IP-DESK-CTRL-$V-win-$1-setup.exe" | cut -d' ' -f1 | tr a-f A-F; }
cat > "$OUT/$ID.yaml" <<Y
PackageIdentifier: $ID
PackageVersion: $V
DefaultLocale: it-IT
ManifestType: version
ManifestVersion: 1.6.0
Y
cat > "$OUT/$ID.installer.yaml" <<Y
PackageIdentifier: $ID
PackageVersion: $V
InstallerType: inno
Scope: machine
InstallModes: [interactive, silent, silentWithProgress]
UpgradeBehavior: install
ProductCode: '{3E8B1C55-9A2D-4C71-B0F4-6D2E5A7C9B13}_is1'
Installers:
- Architecture: x64
  InstallerUrl: $U/IP-DESK-CTRL-$V-win-x64-setup.exe
  InstallerSha256: $(h x64)
- Architecture: x86
  InstallerUrl: $U/IP-DESK-CTRL-$V-win-x86-setup.exe
  InstallerSha256: $(h x86)
- Architecture: arm64
  InstallerUrl: $U/IP-DESK-CTRL-$V-win-arm64-setup.exe
  InstallerSha256: $(h arm64)
ManifestType: installer
ManifestVersion: 1.6.0
Y
cat > "$OUT/$ID.locale.it-IT.yaml" <<Y
PackageIdentifier: $ID
PackageVersion: $V
PackageLocale: it-IT
Publisher: ic8tem
PublisherUrl: https://github.com/ic8tem
PackageName: IP-DESK-CTRL
PackageUrl: https://github.com/ic8tem/IP-DESK-CTRL
License: GPL-3.0-or-later
LicenseUrl: https://github.com/ic8tem/IP-DESK-CTRL/blob/main/LICENSE
ShortDescription: Desktop remoto cifrato con numero fisso di 10 cifre
Description: Vedi e comanda un altro PC (Windows, macOS, Linux, Raspberry Pi, Android) con cifratura AES-256-GCM. Ritrovo sui broker MQTT, porte aperte con UPnP, collegamento inverso e UDP.
Tags: [remote-desktop, desktop-remoto, ham-radio, rigctld]
ReleaseNotesUrl: https://github.com/ic8tem/IP-DESK-CTRL/wiki/Storia-delle-versioni
ManifestType: defaultLocale
ManifestVersion: 1.6.0
Y
ls -l "$OUT"

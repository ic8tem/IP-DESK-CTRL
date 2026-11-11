#!/usr/bin/env bash
# Crea IP-DESK-CTRL.app e il .dmg per macOS.
# Uso: packaging/macos/make-dmg.sh <rid> <versione> <cartella con l'eseguibile> <cartella di uscita>
set -euo pipefail
RID="$1"; VERSION="$2"; SRC="$3"; OUT="$4"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
WORK="$(mktemp -d)"
APP="$WORK/dmg/IP-DESK-CTRL.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources" "$OUT"

cp "$SRC/IP-DESK-CTRL" "$APP/Contents/MacOS/"
chmod +x "$APP/Contents/MacOS/IP-DESK-CTRL"
cp "$ROOT/THIRD-PARTY-NOTICES.txt" "$ROOT/LICENSE" "$APP/Contents/Resources/"

# icona .icns
ICONSET="$WORK/icon.iconset"
mkdir -p "$ICONSET"
for s in 16 32 128 256 512; do
  sips -z $s $s "$ROOT/packaging/icon-1024.png" --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
  d=$((s * 2))
  sips -z $d $d "$ROOT/packaging/icon-1024.png" --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/IP-DESK-CTRL.icns"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>IP-DESK-CTRL</string>
  <key>CFBundleDisplayName</key><string>IP-DESK-CTRL</string>
  <key>CFBundleIdentifier</key><string>it.ic8tem.ipdeskctrl</string>
  <key>CFBundleExecutable</key><string>IP-DESK-CTRL</string>
  <key>CFBundleIconFile</key><string>IP-DESK-CTRL</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>${VERSION}</string>
  <key>CFBundleVersion</key><string>${VERSION}</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSMicrophoneUsageDescription</key><string>IP-DESK-CTRL registra dal dispositivo BlackHole per mandare l'audio di questo Mac al PC collegato e, se lo accendi, dal microfono per farti parlare nel PC remoto.</string>
</dict>
</plist>
PLIST

codesign --force --deep --sign - "$APP"
ln -s /Applications "$WORK/dmg/Applicazioni"
hdiutil create -volname "IP-DESK-CTRL" -srcfolder "$WORK/dmg" -ov -format UDZO "$OUT/IP-DESK-CTRL-${VERSION}-${RID}.dmg"
ditto -c -k --keepParent "$APP" "$OUT/IP-DESK-CTRL-${VERSION}-${RID}-app.zip"

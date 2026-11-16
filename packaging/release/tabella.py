#!/usr/bin/env python3
"""Tabella «Scarica la versione per il tuo sistema» per il testo della release, con i collegamenti ai file.
Uso: tabella.py <versione> [elenco dei file della release, uno per riga, su stdin con --solo-presenti]"""
import sys
V = sys.argv[1]
B = f"https://github.com/ic8tem/IP-DESK-CTRL/releases/download/v{V}/"
present = set(l.strip() for l in sys.stdin) if "--solo-presenti" in sys.argv else None

def link(name):
    if not name: return "-"
    names = name if isinstance(name, list) else [name]
    out = [f"[{n}]({B}{n})" for n in names if present is None or n in present]
    return " · ".join(out) if out else "-"

P = f"IP-DESK-CTRL-{V}"
p = f"ip-desk-ctrl"
rows = [
    ("Windows 10/11 64 bit", f"{P}-win-x64-setup.exe", f"{P}-win-x64-portatile.zip"),
    ("Windows 10/11 32 bit", f"{P}-win-x86-setup.exe", f"{P}-win-x86-portatile.zip"),
    ("Windows su ARM (Snapdragon)", f"{P}-win-arm64-setup.exe", f"{P}-win-arm64-portatile.zip"),
    ("macOS Apple Silicon (M1-M4)", f"{P}-osx-arm64.dmg", f"{P}-osx-arm64-app.zip"),
    ("macOS Intel", f"{P}-osx-x64.dmg", f"{P}-osx-x64-app.zip"),
    ("Debian, Ubuntu, Mint (x86_64)", f"{p}_{V}_amd64.deb", f"{P}-linux-x64-portatile.tar.gz"),
    ("Fedora, openSUSE (x86_64)", f"{p}-{V}-1.x86_64.rpm", f"{P}-linux-x64-portatile.tar.gz"),
    ("Arch, Manjaro, EndeavourOS (x86_64)", f"{p}-{V}-1-x86_64.pkg.tar.zst", f"{P}-linux-x64-portatile.tar.gz"),
    ("Slackware64", f"{p}-{V}-x86_64-1_ic8tem.txz", f"{P}-linux-x64-portatile.tar.gz"),
    ("Raspberry Pi OS 64 bit (e altri Linux ARM64)", f"{p}_{V}_arm64.deb", f"{P}-linux-arm64-portatile.tar.gz"),
    ("Raspberry Pi OS 32 bit (armv7l)", f"{p}_{V}_armhf.deb", f"{P}-linux-arm-portatile.tar.gz"),
    ("Linux ARM64 rpm (Fedora ARM…)", f"{p}-{V}-1.aarch64.rpm", f"{P}-linux-arm64-portatile.tar.gz"),
    ("Linux ARM 32 bit rpm", f"{p}-{V}-1.armv7hl.rpm", f"{P}-linux-arm-portatile.tar.gz"),
    ("Arch Linux ARM (aarch64)", f"{p}-{V}-1-aarch64.pkg.tar.zst", f"{P}-linux-arm64-portatile.tar.gz"),
    ("Slackware ARM64 (anche Raspberry Pi)", f"{p}-{V}-aarch64-1_ic8tem.txz", f"{P}-linux-arm64-portatile.tar.gz"),
    ("Qualsiasi Linux, un solo file (AppImage)", [f"{P}-x86_64.AppImage", f"{P}-aarch64.AppImage"], ""),
    ("Snap", [f"{p}_{V}_amd64.snap", f"{p}_{V}_arm64.snap"], ""),
    ("Flatpak (sessioni X11)", [f"{P}-x86_64.flatpak", f"{P}-aarch64.flatpak"], ""),
    ("Alpine / musl x64", "", f"{P}-linux-musl-x64-portatile.tar.gz"),
    ("Alpine / musl ARM64", "", f"{P}-linux-musl-arm64-portatile.tar.gz"),
    ("Windows con Chocolatey", f"{p}.{V}.nupkg", ""),
    ("Android 8.0 e successivi (anche Chromebook)", f"{P}-android.apk", ""),
]
print("## Scarica la versione per il tuo sistema\n")
print("| Sistema | Installatore | Zip o tar.gz (senza installare) |")
print("|---|---|---|")
for s, a, b in rows:
    la, lb = link(a), link(b)
    if la == "-" and lb == "-": continue
    print(f"| {s} | {la} | {lb} |")

#!/usr/bin/env python3
"""Riempie <!-- RELEASES --> del metainfo con le versioni della Storia delle versioni (wiki).
Uso: releases.py <Storia-delle-versioni.md> <metainfo.xml> > uscita.xml"""
import re, sys, html
MESI = {m: i + 1 for i, m in enumerate("gennaio febbraio marzo aprile maggio giugno luglio agosto settembre ottobre novembre dicembre".split())}
storia, meta = open(sys.argv[1], encoding="utf-8").read(), open(sys.argv[2], encoding="utf-8").read()
righe = []
for v, g, m, a in re.findall(r"^##+ (\d+\.\d+\.\d+) — (\d{1,2}) (\w+) (\d{4})", storia, re.M)[:15]:
    if m.lower() in MESI:
        righe.append(f'    <release version="{html.escape(v)}" date="{a}-{MESI[m.lower()]:02d}-{int(g):02d}">'
                     f'<url type="details">https://github.com/ic8tem/IP-DESK-CTRL/releases/tag/v{v}</url></release>')
if not righe: sys.exit("nessuna versione trovata")
sys.stdout.write(meta.replace("    <!-- RELEASES -->", "\n".join(righe)))

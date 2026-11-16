#!/usr/bin/env python3
"""Mette la tabella «Scarica la versione per il tuo sistema» in cima al testo di tutte le release già pubblicate
(al posto della tabella o dell'elenco di prima). Lo lancia il flusso «Testi delle release» (GitHub Actions).
Serve GH_TOKEN con permesso di scrittura sui contenuti."""
import json, os, subprocess, sys

REPO = os.environ.get("GITHUB_REPOSITORY", "ic8tem/IP-DESK-CTRL")
HERE = os.path.dirname(os.path.abspath(__file__))

def gh(*args, data=None):
    r = subprocess.run(["gh", "api", *args], input=data, capture_output=True, text=True)
    if r.returncode != 0: raise RuntimeError(r.stderr.strip())
    return r.stdout

releases = []
for line in gh("--paginate", f"repos/{REPO}/releases", "-q", ".[]|@json").splitlines():
    releases.append(json.loads(line))

for r in releases:
    v = r["tag_name"].lstrip("v")
    names = "\n".join(a["name"] for a in r["assets"])
    tab = subprocess.run([sys.executable, os.path.join(HERE, "tabella.py"), v, "--solo-presenti"],
                         input=names, capture_output=True, text=True).stdout
    lines = (r.get("body") or "").replace("\r\n", "\n").split("\n")
    i = 0
    if lines and lines[0].startswith("## Scarica la versione"):        # tabella nuova già presente: si rifà
        i = 1
        while i < len(lines) and (lines[i].strip() == "" or lines[i].startswith("|")): i += 1
    elif lines and lines[0].startswith("|"):                             # tabella di prima
        while i < len(lines) and lines[i].startswith("|"): i += 1
    while i < len(lines) and lines[i].startswith("**") and "`" in lines[i]: i += 1   # elenco delle prime versioni
    while i < len(lines) and lines[i].strip() == "": i += 1
    body = tab + "\n" + "\n".join(lines[i:])
    if body == (r.get("body") or ""): print(v, "già a posto"); continue
    gh("-X", "PATCH", f"repos/{REPO}/releases/{r['id']}", "--input", "-", data=json.dumps({"body": body}))
    print(v, "aggiornata")

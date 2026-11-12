# File e appunti

## Copia e incolla

Testi **e file** copiati su un PC si incollano sull'altro, **nei due versi**, con i soliti Ctrl+C e Ctrl+V (Cmd sul Mac).

## File dal tuo PC al PC remoto

- Copia file o cartelle **da qualsiasi posizione** di questo PC (Esplora file, desktop, Finder…) con Ctrl+C, poi clicca nella finestra del PC remoto dove vuoi incollarli e premi **Ctrl+V**: i file partono, e di là si incollano da soli appena arrivano.
- Oppure **trascinali** sulla finestra del PC remoto: vengono incollati lì dove li lasci.
- Oppure **File ▾ › Manda i file copiati su questo PC** o **File ▾ › Scegli file da mandare…**.

Un secondo Ctrl+V degli stessi file non li rimanda: incolla di là la copia già arrivata.

## File dal PC remoto al tuo PC

Sul PC remoto seleziona file o cartelle **in qualsiasi posizione** e premi **Ctrl+C**: arrivano da soli (fino a 2 GB) in `Scaricati/IP-DESK-CTRL` e finiscono negli appunti di questo PC. Poi incollali dove vuoi con **Ctrl+V**. La barra in basso nella finestra mostra l'avanzamento.

Sopra i 2 GB, o per file che erano già copiati prima di collegarti, usa **File ▾ › Prendi qui i file copiati sul PC remoto**.

## Cartella condivisa come disco

Da **File ▾ › Condividi una cartella come disco del PC remoto…** scegli una cartella di qui: sul PC remoto compare come un **disco** e i programmi di là possono aprire, modificare e salvare i file direttamente lì. Con **…in sola lettura** di là si possono solo leggere. Si toglie da **File ▾ › Togli la cartella condivisa**, e comunque alla fine del collegamento.

| PC remoto | Dove compare |
|---|---|
| Windows | Una lettera di unità (Z:, Y:…) in Esplora file. Serve il servizio **WebClient** (Client WebDAV), di solito già presente; i file oltre 50 MB vanno copiati con «File ▾». |
| Linux | Nel file manager (GVFS, con `gio`). |
| macOS | Nel Finder, in /Volumes. |

I file passano nel canale cifrato del collegamento: sul PC remoto c'è solo un piccolo server raggiungibile da quel PC stesso, con un indirizzo segreto. Dal telefono si condivide la cartella dei file ricevuti. Chi è al PC remoto può rifiutare le cartelle condivise (Impostazioni).

## Dove finiscono

I file ricevuti vanno nella cartella **`Scaricati/IP-DESK-CTRL`** e finiscono anche negli appunti, pronti per Ctrl+V. La cartella si apre da Impostazioni › **File ricevuti** o dalla plancia con **File ▾ › Apri la cartella dei file ricevuti**.

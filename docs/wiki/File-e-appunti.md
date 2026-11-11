# File e appunti

## Copia e incolla

Il testo copiato su un dispositivo si incolla sull'altro, **nei due versi**, senza fare niente di particolare: copia con Ctrl+C da una parte e incolla con Ctrl+V dall'altra.

## File dal tuo PC al PC remoto

- **Trascina** file o cartelle sulla finestra del PC remoto: vengono incollati lì dove li lasci (sul desktop, in una cartella aperta…).
- Oppure copia i file qui con Ctrl+C e scegli **File ▾ › Manda i file copiati su questo PC**.
- Oppure **File ▾ › Scegli file da mandare…**.

## File dal PC remoto al tuo PC

Sul PC remoto seleziona i file e premi **Ctrl+C**, poi sulla plancia scegli **File ▾ › Prendi qui i file copiati sul PC remoto**.

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

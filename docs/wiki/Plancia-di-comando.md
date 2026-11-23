# Plancia di comando

Nella finestra del PC remoto c'è una piccola plancia di comando: **in alto al centro**, oppure **a sinistra** o **a destra** se lo scegli in Impostazioni › «Plancia del collegamento» (sotto c'è la fascia arancione degli avvisi). È sempre **a scomparsa**: dopo qualche secondo si riduce a una linguetta col numero del PC remoto e ricompare passando col mouse sulla linguetta o sul bordo scelto. Mentre un suo menu è aperto non si nasconde.

| Comando | Cosa fa |
|---|---|
| **Finestra ⇄ Schermo intero** | Passa da finestra a schermo intero e viceversa. Scorciatoia: **Ctrl+Alt+Invio**. |
| **Zoom** | *Adatta* (lo schermo remoto riempie la finestra) oppure una percentuale da 25 % a 200 %. |
| **Monitor** | Se il PC remoto ha più schermi, scegli quale vedere oppure **tutti insieme**. |
| **Qualità** | *Automatica* (si adatta alla velocità della connessione; in rete locale passa a 50 fotogrammi al secondo), *Rete locale (bassa latenza)* a 60 fotogrammi al secondo, oppure fissa. |
| **Video automatico** | H.264, H.265, AV1 o riquadri JPEG: vedi [Video compresso](Video). |
| **Tunnel ▾** | Tunnel di porte verso il PC remoto: vedi [Tunnel di porte](Tunnel-di-porte). |
| **Risoluzione adatta** | Il PC remoto passa alla risoluzione più adatta a questa finestra (vedi sotto). |
| **Audio** | Accende o spegne l'audio del PC remoto (32 kHz stereo). |
| **Microfono** | Manda il tuo microfono al PC remoto (vedi sotto). |
| **✋** | (telefono e tablet) Tocco e gesti: vedi [Android](Android). |
| **Tasti ▾** | Ctrl+Alt+Canc, Alt+Tab, Tasto Windows / Cmd, Alt+F4, Ctrl+C, Ctrl+V, Stamp, Esc, **Volume del PC remoto +, −, muto**, *Pannello scorciatoie*, *Installa il servizio sul PC remoto*, *Avvia come amministratore* (vedi [Servizio di Windows](Servizio-di-Windows)), *Riavvia il PC remoto…* |
| **File ▾** | Prendi i file copiati sul PC remoto, manda quelli copiati qui, scegli file da mandare (vedi [File e appunti](File-e-appunti)). |
| **Chat** | Apre la chat con chi è davanti al PC remoto: di là si apre una finestra in primo piano. Si apre da sola quando arriva un messaggio. |
| **✎** | Lavagna: disegni in rosso sullo schermo del PC remoto (vedi sotto). |
| **⏺** | Registra la sessione in un video MP4 (vedi sotto). |
| **Radio (IP-TRX-CTRL)** | Se sul PC remoto c'è IP-TRX-CTRL, lo avvia lì (e anche qui, se installato). |
| **⟳** | Aggiorna l'immagine (richiede di nuovo l'intero schermo). |
| **▲** (◀ / ▶ di lato) | Nasconde subito la plancia. |
| **Disconnetti** | Chiude il collegamento. |

La plancia mostra anche la **strada** del collegamento: **TCP**, **UDP + TCP**, **solo UDP** o **relè MQTT** (vedi [Rete e porte](Rete-e-porte)).

## Microfono

Con **Microfono** acceso la tua voce arriva al PC remoto (anche dal telefono). Perché i programmi di là la vedano **come microfono** (videochiamate, IP-TRX-CTRL, programmi della radio) serve un **cavo audio virtuale** sul PC remoto:

- **Windows**: installa **VB-CABLE** (gratuito). IP-DESK-CTRL suona la voce su «CABLE Input»; nei programmi scegli come microfono «CABLE Output». In Impostazioni › *Voce di chi si collega su* puoi scegliere un'altra uscita.
- **macOS**: installa **BlackHole 2ch**; nei programmi scegli BlackHole come microfono.
- **Linux**: niente da installare: durante il collegamento compare il microfono «IP-DESK-CTRL-microfono».

Senza cavo virtuale la voce esce dalle casse del PC remoto. Chi è al PC remoto può rifiutare il microfono (Impostazioni › *Accetta il microfono di chi si collega*).

## Lavagna

Con **✎** il mouse non comanda più il PC remoto: col tasto sinistro disegni sopra l'immagine e i tratti rossi compaiono sullo schermo di là, per indicare un pulsante o una riga. **Clic destro** cancella. Ripremendo **✎** torni a comandare e i disegni spariscono. Di là la lavagna si toglie anche con un clic o da sola dopo 20 secondi senza nuovi tratti, quindi non blocca mai il PC.

## Registrazione

Con **⏺** l'immagine del PC remoto va in un video MP4 (H.264, 8 fotogrammi al secondo, senza audio) nella cartella **Video\IP-DESK-CTRL** di questo PC. Si ferma con **⏹ REC** o scollegandosi; se il PC remoto cambia risoluzione si continua in un secondo file («…_parte2.mp4»). Serve ffmpeg, lo stesso del [video compresso](Video).

**Privacy.** Il video riprende tutto quello che compare sullo schermo remoto (documenti, messaggi, dati personali, anche password se vengono mostrate). Prima di partire IP-DESK-CTRL chiede di confermare che chi usa il PC remoto è d'accordo; registra solo con il suo consenso e nel rispetto delle leggi sulla privacy (in Europa il GDPR). Chi è davanti al PC remoto vede un avviso rosso «Sessione registrata» finché la registrazione è attiva (dalla 0.5.23 su entrambi i PC). Il video resta solo su questo PC e non viene mandato da nessuna parte.

## Risoluzione adatta

Con **Risoluzione adatta** il PC remoto passa, tra le risoluzioni che il suo monitor supporta, a quella con le proporzioni più vicine alla tua finestra (o al telefono, anche in verticale). Ridimensionando la finestra si riadatta. A fine collegamento, o spegnendo il pulsante, torna com'era. Funziona su **Windows** (anche con il servizio, dalla 0.5.22) e **Linux con X11**; serve un solo schermo scelto (non «Tutti gli schermi»). Chi è al PC remoto può impedirlo (Impostazioni).

## Schermo intero

Anche a schermo intero la plancia ricompare portando il mouse sul bordo dove l'hai messa.

## Ctrl+Alt+Canc

Il tasto vero Ctrl+Alt+Canc lo intercetta sempre il tuo PC: per mandarlo al PC remoto usa **Tasti ▾ › Ctrl+Alt+Canc**. Su Windows funziona solo se sul PC remoto è installato il [servizio di Windows](Servizio-di-Windows).

## Riavvio del PC remoto

**Tasti ▾ › Riavvia il PC remoto…** riavvia il PC remoto. La plancia aspetta che torni in linea e **si ricollega da sola** (controlla ogni 15 secondi per 10 minuti). Perché il PC sia raggiungibile prima dell'accesso a Windows serve il [servizio](Servizio-di-Windows) e l'[accesso senza conferma](Accesso-senza-conferma).

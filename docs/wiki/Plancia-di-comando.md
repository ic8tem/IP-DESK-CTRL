# Plancia di comando

Nella finestra del PC remoto, **in alto al centro**, c'è una piccola plancia di comando.

| Comando | Cosa fa |
|---|---|
| **Finestra ⇄ Schermo intero** | Passa da finestra a schermo intero e viceversa. Scorciatoia: **Ctrl+Alt+Invio**. |
| **Zoom** | *Adatta* (lo schermo remoto riempie la finestra) oppure una percentuale da 25 % a 200 %. |
| **Monitor** | Se il PC remoto ha più schermi, scegli quale vedere oppure **tutti insieme**. |
| **Qualità** | *Automatica* (si adatta alla velocità della connessione) oppure fissa. |
| **Video automatico** | H.264, H.265, AV1 o riquadri JPEG: vedi [Video compresso](Video). |
| **Tunnel ▾** | Tunnel di porte verso il PC remoto: vedi [Tunnel di porte](Tunnel-di-porte). |
| **Risoluzione adatta** | Il PC remoto passa alla risoluzione più adatta a questa finestra (vedi sotto). |
| **Audio** | Accende o spegne l'audio del PC remoto (32 kHz stereo). |
| **Microfono** | Manda il tuo microfono al PC remoto (vedi sotto). |
| **✋** | (telefono e tablet) Tocco e gesti: vedi [Android](Android). |
| **Tasti ▾** | Ctrl+Alt+Canc, Alt+Tab, Tasto Windows / Cmd, Alt+F4, Ctrl+C, Ctrl+V, Stamp, Esc, **Volume del PC remoto +, −, muto**, *Pannello scorciatoie*, *Riavvia il PC remoto…* |
| **File ▾** | Prendi i file copiati sul PC remoto, manda quelli copiati qui, scegli file da mandare (vedi [File e appunti](File-e-appunti)). |
| **Radio (IP-TRX-CTRL)** | Se sul PC remoto c'è IP-TRX-CTRL, lo avvia lì (e anche qui, se installato). |
| **⟳** | Aggiorna l'immagine (richiede di nuovo l'intero schermo). |
| **▲** | Nasconde la plancia. |
| **Disconnetti** | Chiude il collegamento. |

La plancia mostra anche la **strada** del collegamento: **TCP**, **UDP + TCP** o **solo UDP** (vedi [Rete e porte](Rete-e-porte)).

## Microfono

Con **Microfono** acceso la tua voce arriva al PC remoto (anche dal telefono). Perché i programmi di là la vedano **come microfono** (videochiamate, IP-TRX-CTRL, programmi della radio) serve un **cavo audio virtuale** sul PC remoto:

- **Windows**: installa **VB-CABLE** (gratuito). IP-DESK-CTRL suona la voce su «CABLE Input»; nei programmi scegli come microfono «CABLE Output». In Impostazioni › *Voce di chi si collega su* puoi scegliere un'altra uscita.
- **macOS**: installa **BlackHole 2ch**; nei programmi scegli BlackHole come microfono.
- **Linux**: niente da installare: durante il collegamento compare il microfono «IP-DESK-CTRL-microfono».

Senza cavo virtuale la voce esce dalle casse del PC remoto. Chi è al PC remoto può rifiutare il microfono (Impostazioni › *Accetta il microfono di chi si collega*).

## Risoluzione adatta

Con **Risoluzione adatta** il PC remoto passa, tra le risoluzioni che il suo monitor supporta, a quella con le proporzioni più vicine alla tua finestra (o al telefono, anche in verticale). Ridimensionando la finestra si riadatta. A fine collegamento, o spegnendo il pulsante, torna com'era. Funziona su **Windows** e **Linux con X11**; serve un solo schermo scelto (non «Tutti gli schermi»). Chi è al PC remoto può impedirlo (Impostazioni).

## Schermo intero

A schermo intero la plancia si riduce a una **linguetta**: ricompare portando il mouse sul bordo alto dello schermo.

## Ctrl+Alt+Canc

Il tasto vero Ctrl+Alt+Canc lo intercetta sempre il tuo PC: per mandarlo al PC remoto usa **Tasti ▾ › Ctrl+Alt+Canc**. Su Windows funziona solo se sul PC remoto è installato il [servizio di Windows](Servizio-di-Windows).

## Riavvio del PC remoto

**Tasti ▾ › Riavvia il PC remoto…** riavvia il PC remoto. La plancia aspetta che torni in linea e **si ricollega da sola** (controlla ogni 15 secondi per 10 minuti). Perché il PC sia raggiungibile prima dell'accesso a Windows serve il [servizio](Servizio-di-Windows) e l'[accesso senza conferma](Accesso-senza-conferma).

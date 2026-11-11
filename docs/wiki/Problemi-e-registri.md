# Problemi e registri

## Messaggi frequenti

| Cosa vedi | Causa e rimedio |
|---|---|
| Schermo **nero** e «Windows ha bloccato mouse e tastiera» | Il PC remoto è bloccato, su una finestra UAC o su un programma da amministratore. Installa sul PC remoto il [servizio di Windows](Servizio-di-Windows) (opzione **Accesso completo**). |
| «Nessuno al PC per accettare» | Il PC è acceso ma nessuno ha fatto l'accesso. Entrano solo i numeri con [accesso senza conferma](Accesso-senza-conferma). |
| «Uscita audio non disponibile» / BadDeviceId | Sul tuo PC non c'è un'uscita audio attiva (casse o cuffie scollegate o disattivate). L'audio riparte da solo appena ne colleghi una. |
| ⚠ nello storico: «L'altro dispositivo ha la …» | Versioni diverse ma compatibili: si collegano, ma le funzioni nuove ci sono solo aggiornando il più vecchio. |
| Riducendo a icona la finestra del visore la qualità scendeva | Dalla 0.5.1 il PC remoto va in pausa (un fotogramma al secondo) e la qualità resta quella di prima. |
| Il video resta a riquadri | Serve ffmpeg su tutti e due i PC (vedi [Video compresso](Video)); nel `sessioni.log` la riga VIDEO dice quali codificatori ha trovato. |
| Tunnel: «la porta … è occupata» | Un altro programma usa già quella porta qui: scegli un'altra porta di qui. |
| «L'altro PC usa la versione … (protocollo …): aggiorna tutti e due» | I due dispositivi hanno protocolli diversi: aggiorna entrambi all'ultima versione. |
| Il collegamento non parte mai | Controlla che il PC remoto sia acceso con il programma aperto (o col servizio). Con NAT simmetrico/CGNAT su entrambi i lati può non esserci strada: attiva l'UPnP su almeno un router. |
| Mac: schermo nero o mouse fermo | Concedi **Registrazione schermo** e **Accessibilità** e riavvia il programma. |
| Linux Wayland: niente immagine | Usa la sessione **Xorg** (GNOME e KDE su Wayland non permettono la cattura). |
| Ho chiuso la finestra ma il PC è ancora raggiungibile | È voluto: la X nasconde soltanto. Per uscire: clic destro sull'icona vicino all'orologio › **Esci da IP-DESK-CTRL**. |
| La mia voce si sente dalle casse del PC remoto ma i programmi non la sentono | Serve un cavo audio virtuale sul PC remoto (VB-CABLE su Windows, BlackHole su Mac): vedi [Plancia di comando](Plancia-di-comando#microfono). |
| «Windows non ha montato la cartella» | Avvia il servizio **WebClient** (services.msc › Client WebDAV › Avvia, tipo di avvio Manuale o Automatico) sul PC remoto. |
| Risoluzione adatta: «Sul Mac la risoluzione non si adatta ancora» | Per ora funziona su Windows e Linux X11. |
| Mouse collegato al telefono: il tasto destro chiudeva il collegamento | Dalla 0.5 il tasto destro fa clic destro. |
| Linux: dopo la X non vedo l'icona | Il desktop non ha l'area di notifica (per esempio GNOME senza l'estensione «AppIndicator»). Riavvia IP-DESK-CTRL dal menu: si riapre la finestra; per uscire usa Impostazioni › **Esci dal programma**. |
| Android: tocchi che non arrivano | Attiva **Impostazioni › Accessibilità › IP-DESK-CTRL**. |

## Registri

Nella cartella dei dati (Impostazioni › **Apri cartella dati**):

- `sessioni.log` — collegamenti, porte, broker, UDP, monitor, audio;
- `errori.log` — errori del programma (se non c'è, non ci sono stati errori).

| Sistema | Cartella |
|---|---|
| Windows | `%APPDATA%\IP-DESK-CTRL` (il servizio scrive i suoi in `%ProgramData%\IP-DESK-CTRL`) |
| Linux, Raspberry Pi, macOS | `~/.config/IP-DESK-CTRL` |

Sul telefono: Impostazioni › **Informazioni e licenze** › **Condividi il registro** (dalla 0.5.2).

Per segnalare un problema apri una [issue](https://github.com/ic8tem/IP-DESK-CTRL/issues) allegando il `sessioni.log` di **entrambi** i PC.

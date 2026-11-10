# IP-DESK-CTRL

Desktop remoto cifrato tra Windows (64 e 32 bit), macOS (Apple Silicon e Intel), Linux 64 bit e Raspberry Pi (64 e 32 bit). Un unico programma: ogni PC è insieme **host** (si fa vedere e comandare) e **visore** (vede e comanda gli altri), senza che l'utente scelga niente.

Ogni PC ha un **numero fisso di 10 cifre** (es. `482 913 0675`) che non cambia mai: per collegarti a un PC scrivi il suo numero e premi **Collegati**. Sull'altro PC compare la richiesta da accettare.

## Funzioni

- **Numero fisso di 10 cifre**, creato al primo avvio e legato alla chiave crittografica del PC.
- **Storico dei collegamenti** nella finestra principale (in uscita e in arrivo), con **alias** per ricordare i numeri, data, durata, sistema operativo e pallino verde se il PC è in linea. Doppio clic per ricollegarsi.
- **Consenso** dell'utente del PC comandato a ogni collegamento (rifiuto automatico dopo 60 s), con la scelta di concedere mouse e tastiera o solo la visione. Per i PC di fiducia si può consentire l'**accesso senza conferma** (menu `···` dello storico).
- **Monitor multipli**: dalla plancia si sceglie quale schermo del PC remoto vedere, oppure tutti insieme.
- **Audio del sistema** del PC comandato riprodotto sul visore (32 kHz stereo).
- **Finestra adattabile**: zoom *Adatta* o in percentuale (25–200 %), schermo intero.
- **Plancia di comando in alto al centro**: finestra ⇄ schermo intero, zoom, monitor, qualità, audio, tasti speciali (Ctrl+Alt+Canc, Alt+Tab, Win/Cmd, Alt+F4, Stamp), aggiorna, disconnetti. A schermo intero si riduce a una linguetta: ricompare portando il mouse sul bordo alto. **Ctrl+Alt+Invio** passa da finestra a schermo intero e viceversa.
- Mentre il PC è comandato compare una piccola finestra sempre in primo piano con **Disconnetti** e l'interruttore di mouse e tastiera.

## Sicurezza

- Ogni PC ha una coppia di chiavi **ECDSA P-256** (Windows: DPAPI; macOS: Portachiavi; Linux: file cifrato con permessi 600).
- Il numero è ricavato dalla chiave pubblica (PBKDF2-SHA256, 100 000 iterazioni): chi si collega controlla che il PC che risponde abbia davvero la chiave di quel numero.
- A ogni collegamento: scambio **ECDH P-256 effimero** firmato da entrambi i PC, poi **AES-256-GCM** con due chiavi distinte (una per verso) e contatore anti-ripetizione; anche UDP è cifrato AES-256-GCM. Segretezza in avanti: le chiavi di sessione non si possono ricavare dopo.
- Sui broker MQTT passano solo indirizzo e porta, firmati e cifrati; i nomi dei canali derivano dal numero e non lo mostrano.

## Rete

Come IP-TRX-CTRL:

1. all'avvio il programma apre una porta casuale (TCP e UDP con lo stesso numero) sul router con **UPnP/NAT-PMP** e la **verifica dall'esterno** (ifconfig.co e portchecker.io); se non è raggiungibile ne prova un'altra;
2. lascia indirizzo pubblico, porta e indirizzo di rete locale, firmati e cifrati, sui **broker MQTT** pubblici (hivemq, mosquitto, emqx) e li aggiorna ogni 3 minuti;
3. il visore li legge e si collega **diretto** (prima la rete locale se i due PC sono nella stessa casa);
4. se non ci riesce chiede sul broker il **collegamento inverso**: è l'host a collegarsi alla porta del visore; se la rete del visore lascia uscire solo poche porte (443, 80…) l'host apre proprio quelle;
5. a collegamento fatto i due PC provano **UDP** con perforazione del NAT (STUN): audio e movimenti del mouse passano lì; lo schermo resta su TCP.

Serve UPnP (o una porta inoltrata a mano, o la DMZ) sul router di **almeno uno** dei due PC. Con IP condiviso dell'operatore (CGNAT) su entrambi i lati il collegamento diretto non è possibile.

## Note per sistema

- **Windows**: non si vedono la schermata di blocco e le finestre di sicurezza (UAC); Windows ignora il Ctrl+Alt+Canc simulato; le finestre dei programmi avviati come amministratore non ricevono il mouse se IP-DESK-CTRL non è a sua volta avviato come amministratore.
- **macOS**: al primo collegamento in arrivo concedi **Registrazione schermo** e **Accessibilità** (Impostazioni di Sistema › Privacy e sicurezza), poi riavvia il programma. Per mandare l'audio installa **BlackHole 2ch** e crea un *dispositivo a uscita multipla* (casse + BlackHole) in Configurazione MIDI Audio.
- **Linux e Raspberry Pi con X11**: tutto funziona (XGetImage, XTest, XRandR). Audio con `parec`/`pacat` (PulseAudio o PipeWire).
- **Wayland** (Raspberry Pi OS Bookworm, Sway…): schermo con `grim` (più lento, pochi fotogrammi al secondo), mouse e tastiera con `/dev/uinput` (il pacchetto .deb installa la regola udev: riavvia dopo l'installazione). GNOME e KDE su Wayland non permettono la cattura: scegli la sessione **Xorg** al login. Sul Raspberry si può passare a X11 con `sudo raspi-config` › Advanced Options › Wayland › X11.

## Scaricare

Gli installatori sono nella pagina **Releases**:

- Windows 64 / 32 bit: `…-win-x64-setup.exe` / `…-win-x86-setup.exe` (opzione per l'avvio automatico all'accesso);
- Linux 64 bit e Raspberry Pi: `ip-desk-ctrl_…_amd64.deb`, `…_arm64.deb`, `…_armhf.deb` (`sudo apt install ./<file>.deb`);
- macOS: `…-osx-arm64.dmg` / `…-osx-x64.dmg` (al primo avvio clic destro › Apri).

Per ogni sistema c'è anche una versione portatile.

## Registri

Nella cartella dei dati (Impostazioni › Apri cartella dati): `errori.log` e `sessioni.log` (collegamenti, porte, broker, UDP).

- Windows: `%APPDATA%\IP-DESK-CTRL`
- Linux, Raspberry Pi e macOS: `~/.config/IP-DESK-CTRL`

## Licenza

IP-DESK-CTRL è software libero, Copyright (C) 2026 Costantino IC8TEM, distribuito secondo la **GNU General Public License versione 3 o successiva** (file [`LICENSE`](LICENSE)), senza alcuna garanzia. Le licenze dei componenti di terze parti sono in `THIRD-PARTY-NOTICES.txt` e nella finestra «Informazioni e licenze».

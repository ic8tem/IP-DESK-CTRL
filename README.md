# IP-DESK-CTRL

[![Sostieni il progetto su Ko-fi](https://img.shields.io/badge/Ko--fi-Sostieni%20il%20progetto-FF5E5B?logo=ko-fi&logoColor=white)](https://ko-fi.com/ic8tem)

Desktop remoto cifrato tra Windows (64 bit, 32 bit e ARM64), macOS (Apple Silicon e Intel), Linux (x86_64, aarch64, armv7l: .deb, .rpm, Slackware), Raspberry Pi (64 e 32 bit) e **Android**. Un unico programma: ogni PC è insieme **host** (si fa vedere e comandare) e **visore** (vede e comanda gli altri), senza che l'utente scelga niente.

Ogni PC ha un **numero fisso di 10 cifre** (es. `482 913 0675`) che non cambia mai: per collegarti a un PC scrivi il suo numero e premi **Collegati**. Sull'altro PC compare la richiesta da accettare.

## Funzioni

- **Numero fisso di 10 cifre**, creato al primo avvio e legato alla chiave crittografica del PC.
- **Storico dei collegamenti** nella finestra principale (in uscita e in arrivo), con **alias** per ricordare i numeri, data, durata, sistema operativo e pallino verde se il PC è in linea. Doppio clic per ricollegarsi.
- **Consenso** dell'utente del PC comandato a ogni collegamento (rifiuto automatico dopo 60 s), con la scelta di concedere mouse e tastiera o solo la visione. Per i PC di fiducia si può consentire l'**accesso senza conferma** (menu `···` dello storico).
- **Monitor multipli**: dalla plancia si sceglie quale schermo del PC remoto vedere, oppure tutti insieme.
- **Audio del sistema** del PC comandato riprodotto sul visore (32 kHz stereo).
- **Finestra adattabile**: zoom *Adatta* o in percentuale (25–200 %), schermo intero.
- **Plancia di comando in alto al centro**: finestra ⇄ schermo intero, zoom, monitor, qualità, audio, tasti speciali (Ctrl+Alt+Canc, Alt+Tab, Win/Cmd, Alt+F4, Stamp), aggiorna, disconnetti. A schermo intero si riduce a una linguetta: ricompare portando il mouse sul bordo alto. **Ctrl+Alt+Invio** passa da finestra a schermo intero e viceversa.
- **Copia e incolla**: il testo copiato su un PC si incolla sull'altro, nei due versi.
- **File tra i PC**: trascina file o cartelle sulla finestra del PC remoto e vengono incollati lì dove li lasci; dalla plancia (**File ▾**) prendi qui i file copiati (Ctrl+C) sul PC remoto, mandi quelli copiati qui o scegli file da mandare. I file ricevuti finiscono in `Scaricati/IP-DESK-CTRL` e negli appunti, pronti per Ctrl+V.
- Mentre il PC è comandato compare una piccola finestra sempre in primo piano con **Disconnetti** e l'interruttore di mouse e tastiera.
- **Password per l'accesso senza conferma** (facoltativa, Impostazioni): chi entra senza conferma deve dare anche la password; il visore può ricordarla per quel PC. Dopo 5 password sbagliate quel numero è bloccato per 10 minuti.
- **Riavvio del PC remoto** (plancia › Tasti › *Riavvia il PC remoto…*): la plancia aspetta che il PC torni in linea e si ricollega da sola (controlla ogni 15 s per 10 minuti).
- **Wake-on-LAN**: dallo storico (`···` › *Accendi*) si accende un PC spento. Dalla stessa rete il pacchetto parte direttamente; da fuori lo manda un altro PC acceso della stessa rete, solo se questo PC può entrare lì senza conferma. Il PC da accendere deve avere il Wake-on-LAN attivo nel BIOS e nella scheda di rete.
- **QR del numero**: il pulsante *QR* accanto al numero lo mostra come codice QR (`ipdesk:1234567890`); sul telefono *Inquadra QR* lo legge con un'app lettore QR compatibile e si collega.
- **Radio (IP-TRX-CTRL)**: se sul PC remoto è installato IP-TRX-CTRL, la plancia ha un pulsante che lo avvia lì e, se c'è, anche su questo PC.
- **Microfono verso il PC remoto** (plancia › *Microfono*): parli nei programmi del PC remoto. Perché arrivi come microfono serve un cavo audio virtuale sul PC remoto (VB-CABLE su Windows, BlackHole su Mac); su Linux il microfono virtuale «IP-DESK-CTRL-microfono» si crea da solo. Senza, la voce esce dalle casse.
- **Volume del PC remoto** (Tasti › Volume +, −, muto).
- **Risoluzione adatta** (plancia): il PC remoto passa alla risoluzione più adatta alla tua finestra o al telefono e a fine collegamento torna com'era (Windows e Linux X11).
- **Cartella condivisa come disco** (File ▾ › *Condividi una cartella come disco del PC remoto…*): la cartella scelta compare di là come unità (Windows, serve il servizio «WebClient»), in GVFS (Linux) o nel Finder (macOS), anche in sola lettura. I file passano nel canale cifrato.
- **Video compresso H.264 / H.265 / AV1** (plancia › *Video automatico*): con ffmpeg su tutti e due i PC lo schermo viaggia come video, con i codificatori hardware della scheda video se ci sono (NVIDIA NVENC, Intel Quick Sync, AMD AMF, Media Foundation, VideoToolbox) o con x264; molta meno banda e più fluidità. Su Windows: Impostazioni › *Scarica ffmpeg*; su Linux `sudo apt install ffmpeg`; su Mac `brew install ffmpeg`. Senza ffmpeg si usano i riquadri JPEG.
- **Tunnel di porte** (plancia › *Tunnel ▾*): un programma di qui si collega a 127.0.0.1:porta e arriva a un indirizzo visto dal PC remoto, per esempio **rigctld** (4532), **rotctld** (4533), **flrig** (12345), **fldigi** (7362) o l'**interfaccia web della radio**. Si salvano per ogni PC e si possono avviare da soli.
- **Controllo preliminare delle versioni**: nello storico compare la versione di ogni PC (⚠ se diversa) e prima di collegarsi il programma avvisa; con protocollo diverso non si collega e dice cosa aggiornare.
- **Avviso di nuova versione** nella schermata principale (controllo su GitHub all'avvio e ogni 12 ore).
- **Sempre attivo**: la **X** della finestra la nasconde soltanto; IP-DESK-CTRL resta attivo con l'icona vicino all'orologio (barra dei menu su macOS), il PC resta raggiungibile e i collegamenti aperti continuano. Clic sull'icona per riaprirlo; per uscire davvero: clic destro sull'icona › **Esci da IP-DESK-CTRL**, oppure Impostazioni › **Esci dal programma**. Riavviarlo mentre è nascosto riapre la finestra di quello già attivo.

## Android

Lo stesso programma, per telefoni e tablet con Android 8.0 o successivo (`IP-DESK-CTRL-…-android.apk`).

- **Visore**: il dito fa da **touchpad** oppure, a scelta, **tocco diretto** (si clicca dove si tocca). Con il pulsante **✋** si sceglie il profilo dei gesti (Sofatap, Abitudini da touchpad, Pressione per il clic destro, Come la 0.4) e l'azione di ogni gesto, i tempi, la velocità del puntatore e i tasti della barra. **Pressione lunga** (profilo Sofatap): pannello scorciatoie, si sceglie il tasto scorrendo e si solleva il dito. **Due dita allargate**: zoom dell'immagine e spostamento. **Striscia sul bordo destro**: scorrimento. Il pulsante **Tastiera** apre la tastiera del telefono con la barra dei tasti speciali scelti (i tasti come frecce e Canc si ripetono tenendoli premuti); **🎤 Detta** manda al PC il testo dettato. Il testo arriva così com'è, qualunque sia la tastiera del PC.
- **Mouse e tastiera** Bluetooth o USB collegati al telefono o al tablet (anche Samsung DeX) vanno direttamente al PC remoto.
- **Host**: il telefono si fa vedere e comandare. Ad ogni collegamento Android chiede il consenso alla cattura dello schermo; per tocchi e testo va attivato una volta il servizio in Impostazioni › Accessibilità › IP-DESK-CTRL. I clic diventano tocchi, il trascinamento diventa scorrimento, il tasto destro ed Esc sono «Indietro», il tasto Win è «Home». L'audio del telefono (Android 10+) arriva al PC.
- Il telefono è raggiungibile col suo numero mentre l'app è aperta.
- **Inquadra QR** legge il QR del numero di un PC con un'app lettore QR compatibile (ZXing «Barcode Scanner» e simili); se non ce n'è, si scrive il numero a mano.

## Sicurezza

- Ogni PC ha una coppia di chiavi **ECDSA P-256** (Windows: DPAPI; macOS: Portachiavi; Linux: file cifrato con permessi 600).
- Il numero è ricavato dalla chiave pubblica (PBKDF2-SHA256, 100 000 iterazioni): chi si collega controlla che il PC che risponde abbia davvero la chiave di quel numero.
- A ogni collegamento: scambio **ECDH P-256 effimero** firmato da entrambi i PC, poi **AES-256-GCM** con due chiavi distinte (una per verso) e contatore anti-ripetizione; anche UDP è cifrato AES-256-GCM. Segretezza in avanti: le chiavi di sessione non si possono ricavare dopo.
- Sui broker MQTT passano solo indirizzo e porta (e, per il collegamento solo UDP e il Wake-on-LAN, candidati UDP, MAC e richieste), firmati con la chiave del PC e cifrati; i nomi dei canali derivano dal numero e non lo mostrano. La chiave dei datagrammi UDP nasce da uno scambio ECDH effimero firmato.
- Password dell'accesso senza conferma: il visore ricava dalla password una chiave PBKDF2-SHA256 (200 000 iterazioni, sale del PC) e la manda solo dentro il canale già cifrato; la password in chiaro non viaggia mai. Sul PC comandato restano solo il sale e un'impronta SHA-256 di quella chiave, che non basta per entrare. La password ricordata dal visore è protetta da un segreto che resta su quel dispositivo.
- Con il servizio di Windows la chiave privata del PC sta solo nel servizio (DPAPI di sistema): il programma gli chiede le firme e le verifica.

## Rete

Come IP-TRX-CTRL:

1. all'avvio il programma apre una porta casuale (TCP e UDP con lo stesso numero) sul router con **UPnP/NAT-PMP** e la **verifica dall'esterno** (ifconfig.co e portchecker.io); se non è raggiungibile ne prova un'altra;
2. lascia indirizzo pubblico, porta e indirizzo di rete locale, firmati e cifrati, sui **broker MQTT** pubblici (hivemq, mosquitto, emqx) e li aggiorna ogni 3 minuti;
3. il visore li legge e si collega **diretto** (prima la rete locale se i due PC sono nella stessa casa);
4. se non ci riesce chiede sul broker il **collegamento inverso**: è l'host a collegarsi alla porta del visore; se la rete del visore lascia uscire solo poche porte (443, 80…) l'host apre proprio quelle;
5. se anche il collegamento inverso non passa (due giri), i due PC si scambiano sul broker i **candidati UDP** (messaggi firmati) e fanno **tutto su UDP**: perforazione del NAT e un trasporto affidabile e ordinato (numeri di sequenza, conferme cumulative e selettive, ritrasmissione con tempo adattivo, finestra di congestione, pacchetti da 1150 byte) sotto lo stesso canale cifrato. La plancia mostra la strada: **TCP**, **UDP + TCP** o **solo UDP**;
6. con un collegamento TCP fatto, i due PC provano comunque **UDP** con perforazione del NAT (STUN): audio e movimenti del mouse passano lì; lo schermo resta su TCP.

Senza UPnP su nessuno dei due router di solito si arriva al collegamento **solo UDP**. Con NAT simmetrico (per esempio CGNAT dell'operatore) su entrambi i lati anche UDP può non passare.

## Note per sistema

- **Windows**: con l'installatore (come amministratore) e l'opzione **Accesso completo**, attiva di serie, si installa il servizio «IP-DESK-CTRL accesso completo». Durante i collegamenti accettati fa vedere e comandare anche le **richieste UAC**, la **schermata di blocco**, i **programmi avviati come amministratore** e permette **Ctrl+Alt+Canc**. Dalla versione 0.4 il servizio tiene anche **la rete**: il PC è raggiungibile **appena acceso, prima dell'accesso a Windows**, con lo stesso numero di prima (al primo avvio il programma passa la sua chiave al servizio). Se nessuno ha fatto l'accesso entrano solo i numeri «senza conferma» (con la password, se impostata); gli altri ricevono «nessuno al PC per accettare». Con un utente collegato la richiesta compare nel suo programma. Lo storico è condiviso in `%ProgramData%\IP-DESK-CTRL`. Senza il servizio (versione portatile o installazione per un solo utente) quelle schermate restano nere e il PC è raggiungibile solo con il programma aperto.
- **macOS**: al primo collegamento in arrivo concedi **Registrazione schermo** e **Accessibilità** (Impostazioni di Sistema › Privacy e sicurezza), poi riavvia il programma. Per mandare l'audio installa **BlackHole 2ch** e crea un *dispositivo a uscita multipla* (casse + BlackHole) in Configurazione MIDI Audio.
- **Linux e Raspberry Pi con X11**: tutto funziona (XGetImage, XTest, XRandR). Audio con `parec`/`pacat` (PulseAudio o PipeWire).
- **Wayland** (Raspberry Pi OS Bookworm, Sway…): schermo con `grim` (più lento, pochi fotogrammi al secondo), mouse e tastiera con `/dev/uinput` (il pacchetto .deb installa la regola udev: riavvia dopo l'installazione). GNOME e KDE su Wayland non permettono la cattura: scegli la sessione **Xorg** al login. Sul Raspberry si può passare a X11 con `sudo raspi-config` › Advanced Options › Wayland › X11.

## Scaricare

Gli installatori sono nella pagina **Releases**:

| Distribuzione | File |
|---|---|
| Windows 64 bit | `…-win-x64-setup.exe` |
| Windows 32 bit | `…-win-x86-setup.exe` |
| Windows ARM64 (PC con Snapdragon) | `…-win-arm64-setup.exe` |
| Linux x86_64 (64 bit) | `…-linux-x64-portatile.tar.gz` |
| Linux aarch64 (ARM 64 bit, PC e mini-PC non Raspberry) | `…-linux-arm64-portatile.tar.gz` |
| Debian / Ubuntu / Mint | `ip-desk-ctrl_…_amd64.deb` (`sudo apt install ./<file>.deb`) |
| Fedora / openSUSE | `ip-desk-ctrl-…-1.x86_64.rpm`, `…aarch64.rpm`, `…armv7hl.rpm` (`sudo dnf install ./<file>.rpm`) |
| Raspberry Pi OS 32 bit (armhf) | `ip-desk-ctrl_…_armhf.deb` |
| Raspberry Pi OS 64 bit (arm64) | `ip-desk-ctrl_…_arm64.deb` |
| Raspberry Pi Slackware ARM64 | `ip-desk-ctrl-…-aarch64-1_ic8tem.txz` (`installpkg`) |
| Linux armv7l | `…-linux-arm-portatile.tar.gz` |
| Arch / Manjaro / EndeavourOS / Arch Linux ARM | `ip-desk-ctrl-…-1-x86_64.pkg.tar.zst` · `…-aarch64.pkg.tar.zst` (`sudo pacman -U ./<file>`) |
| Qualsiasi Linux, un solo file (AppImage) | `…-x86_64.AppImage` · `…-aarch64.AppImage` |
| Snap | `ip-desk-ctrl_…_amd64.snap` · `…_arm64.snap` (`sudo snap install --dangerous --classic ./<file>`) |
| Alpine Linux (musl) | `…-linux-musl-x64-portatile.tar.gz` · `…-linux-musl-arm64-portatile.tar.gz` |
| Chromebook | l'APK Android, oppure il `.deb` nel Linux del Chromebook |
| macOS Apple Silicon (M1-M4) | `…-osx-arm64.dmg` (al primo avvio clic destro › Apri) |
| macOS Intel | `…-osx-x64.dmg` |
| macOS con Homebrew | `brew tap ic8tem/ip-desk-ctrl https://github.com/ic8tem/IP-DESK-CTRL` poi `brew install --cask ip-desk-ctrl` |
| Android 8.0 e successivi | `…-android.apk` |

L'installatore di Windows ha l'opzione per l'avvio automatico all'accesso, nascosto con la sola icona vicino all'orologio.
Per ogni sistema c'è anche una versione portatile.

## Aggiornamenti automatici

IP-DESK-CTRL controlla all'avvio e ogni 30 minuti se su GitHub c'è una versione nuova: in alto a destra compare **Nuova versione x.y.z** e, con l'opzione **Aggiornamenti automatici** (attiva di serie), dopo un minuto si scarica, si verifica (SHA-256) e si installa da sola, aspettando che non ci siano collegamenti aperti. Il programma si riapre e i collegamenti riprendono.

L'aggiornamento automatico c'è **per tutte le distribuzioni**, con queste eccezioni:

- **Android e Chromebook (APK)**: solo l'avviso; Android non lascia installare un'app da sola, l'APK si tocca a mano.
- **Pacchetti di sistema Linux** (.deb, .rpm, Arch, Slackware, Snap): si aggiornano da soli ma il sistema **chiede la password di amministratore**; senza qualcuno che la scriva l'aggiornamento resta in attesa.
- **Versioni portatili in una cartella di sistema** (non scrivibile dall'utente): solo l'avviso.

Del tutto senza domande: Windows con il servizio, Windows portatile, AppImage, Linux portatile e Alpine, macOS (salvo la password se l'app è in una cartella protetta). Dettagli nella [wiki](https://github.com/ic8tem/IP-DESK-CTRL/wiki/Installazione#aggiornamenti).

## Registri

Nella cartella dei dati (Impostazioni › Apri cartella dati): `errori.log` e `sessioni.log` (collegamenti, porte, broker, UDP).

- Windows: `%APPDATA%\IP-DESK-CTRL` (il servizio scrive i suoi in `%ProgramData%\IP-DESK-CTRL`)
- Linux, Raspberry Pi e macOS: `~/.config/IP-DESK-CTRL`

## Storia delle versioni

Le versioni con protocollo diverso non si collegano tra loro: aggiorna tutti i dispositivi alla stessa versione principale (la 0.4.1 si collega con la 0.4.0; la 0.5 si collega con la 0.4).

### 0.5.11 — 14 novembre 2026
- **Tunnel per la radio**: il modello rigctld usa di qua la porta **4542** (arriva alla 4532 del PC remoto), perché la 4532 di qua è spesso già occupata da un rigctld locale (IP-TRX-CTRL, WSJT-X…); scegliendolo compare l'avviso con l'indirizzo da impostare.
- **Installatore di Windows**: nuova opzione «Scarica ffmpeg» (attiva di serie), che mette ffmpeg nella cartella del programma: il video compresso H.264/H.265/AV1 c'è subito, anche per il servizio.
- **Collegamento più rapido nelle reti difficili**: dal giro 3 dei tentativi la strada solo UDP parte in parallelo ai giri con le porte invertite, e vince la prima che riesce (prima partiva solo dopo il giro 4).

### 0.5.10 — 14 novembre 2026
- **Aggiornamenti automatici spiegati** nel README, nella wiki e nella pagina della release: ci sono per tutte le distribuzioni, tranne Android/Chromebook (solo avviso); i pacchetti di sistema Linux chiedono la password di amministratore.
- **Altre distribuzioni**: pacchetti per **Arch, Manjaro, EndeavourOS e Arch Linux ARM** (`.pkg.tar.zst`, x86_64 e aarch64), **AppImage** (un solo file per qualsiasi Linux, x86_64 e aarch64), **Snap** (amd64 e arm64), **Alpine Linux** e altre distribuzioni con musl (x64 e arm64), **Homebrew** sul Mac (`brew install --cask ip-desk-ctrl` dopo il `brew tap`). Anche questi si aggiornano da soli (pacman, snap, file AppImage sostituito, Homebrew aggiornato a ogni release). Nella guida c'è anche il Chromebook (APK Android o `.deb` nel Linux interno).

### 0.5.9 — 13 novembre 2026
- **Plancia del collegamento in alto, a sinistra o a destra** (Impostazioni › «Plancia del collegamento»): sotto resta la fascia arancione degli avvisi. La plancia è **sempre a scomparsa**: compare passando sulla linguetta o sul bordo scelto e si nasconde da sola, ma non mentre un suo menu è aperto. Di lato i comandi stanno in colonna e la linguetta col numero è scritta in verticale. Le finestre già aperte si spostano subito.
- **Guida e Ko-fi in alto** nella finestra principale, accanto allo stato «In linea». La guida apre la wiki.
- Controllo delle nuove versioni: ogni 30 minuti (come dalla 0.5.4).

### 0.5.8 — 12 novembre 2026
- **Audio del PC comandato senza uscita predefinita**: invece dell'errore «Audio di Windows non catturabile: Impossibile trovare elemento (0x80070490)» la cattura prova la prima uscita attiva; se il PC non ne ha (casse o cuffie scollegate o disattivate) lo dice chiaramente e riprova ogni 5 secondi, così l'audio parte da solo appena colleghi qualcosa. Se l'uscita sparisce durante il collegamento si riapre da sola.

### 0.5.7 — 12 novembre 2026
- **Copia e incolla di file da qualsiasi posizione, nei due versi**: su Windows gli appunti dei file si leggono e si scrivono con le funzioni di Windows (prima passavano da Avalonia, che con i file copiati in Esplora file non funzionava). I file copiati sul PC remoto con Ctrl+C arrivano da soli negli appunti di qua (fino a 2 GB); i file copiati qui si mandano con Ctrl+V nella finestra del PC remoto e di là si incollano appena arrivano. Niente rimbalzi: i file appena passati non tornano indietro.

### 0.5.6 — 12 novembre 2026
- **Più distribuzioni**: Windows ARM64 (PC con Snapdragon, con installatore e servizio), pacchetti **.rpm** per Fedora e openSUSE (x86_64, aarch64, armv7hl) e pacchetti **Slackware** (.txz) per Slackware ARM64, anche su Raspberry Pi, e Slackware64. Anche questi si aggiornano da soli (dnf, zypper o upgradepkg). Su Windows ARM64 ffmpeg si scarica nella versione ARM.

### 0.5.5 — 12 novembre 2026
- **L'aggiornamento automatico non interrompe i collegamenti**: se esce una versione nuova mentre comandi un PC o qualcuno comanda il tuo, compare solo il pulsante «Nuova versione» in alto a destra e l'installazione parte da sola appena si chiude l'ultimo collegamento (dopo il solito minuto per fermarla). «Aggiorna ora» la fa subito, con circa 45 secondi di interruzione e ripresa automatica.

### 0.5.4 — 12 novembre 2026
- **Aggiornamenti automatici**: il programma guarda ogni 30 minuti (prima ogni 12 ore) se è uscita una nuova versione. In alto a destra, accanto a «In linea», compare il pulsante **Nuova versione x.y.z**. Con l'opzione **Aggiornamenti automatici** (Impostazioni, attiva di serie) il riquadro si apre da solo e dopo 60 secondi la versione nuova si scarica, si controlla e si installa; il programma si riapre e i collegamenti riprendono. **Più tardi** la ferma per quella versione. Android: solo l'avviso.

### 0.5.3 — 12 novembre 2026
- **Finestre UAC con il servizio di Windows**: quando compariva la richiesta UAC l'immagine diventava nera («Cattura dello schermo non riuscita (BitBlt, errore 6)») e non si poteva rispondere. Ora cattura, mouse e tastiera girano su un thread che, a ogni cambio di desktop (UAC, schermata di blocco, Ctrl+Alt+Canc), viene rifatto nuovo e spostato sul desktop attivo: la finestra UAC si vede e si comanda. Nel registro delle sessioni compaiono le righe «AIUTO desktop attivo: Winlogon» / «Default».

### 0.5.2 — 11 novembre 2026
- **Porte sul router** (come IP-TRX-CTRL 0.2.8): il router si cerca prima con UPnP e solo dopo con NAT-PMP. Alcuni router rispondono a NAT-PMP ma rifiutano tutte le porte («Failed to create portmap», «overflow» oltre la porta 32767): ora, dopo 5 rifiuti, si prova l'altro protocollo, e con NAT-PMP le porte restano sotto 32768. Così si aprono anche 443, 80, 8443, 8080 per i visori su reti che fanno uscire solo quelle.
- **Copia/incolla di testi e file con i PC che hanno il servizio di Windows**: dalla 0.4 i collegamenti in arrivo li riceve il servizio, che non vede gli appunti dell'utente; ora testi e file passano dal processo di supporto, nei due versi, e i file ricevuti finiscono in Scaricati/IP-DESK-CTRL dell'utente.
- **Telefono**: la plancia di comando sta su una sola riga che scorre col dito (prima occupava mezzo schermo); il pulsante dei gesti si chiama «Gesti» (il simbolo non si vedeva); il menu dei monitor compare solo se il PC remoto ne ha più di uno.
- **Registro condivisibile dal telefono**: Informazioni › «Condividi il registro» lo manda per e-mail, WhatsApp…, per capire i collegamenti non riusciti.

### 0.5.1 — 11 novembre 2026
- **Telefono**: il numero si vede sempre intero (si rimpicciolisce per stare nella larghezza), il campo per il numero da chiamare è su tutta la larghezza con «Inquadra QR» e «Collegati» sotto, i titoli non si tagliano più.
- **Più visori insieme**: mentre uno comanda, fino a 4 altri possono collegarsi per **guardare** (solo visione: niente mouse, tastiera, file o tunnel). Chi è al PC accetta ognuno, oppure entrano da soli i numeri con accesso senza conferma; la finestrella «comandato da» dice quanti guardano e «Disconnetti» chiude tutti. Si può spegnere in Impostazioni.
- **Aggiornamento dal programma aperto**: quando esce una nuova versione compare un riquadro con «Aggiorna ora». Il programma scarica il file giusto, controlla l'impronta SHA-256 pubblicata da GitHub, lo installa da solo, si chiude e si riapre. I collegamenti aperti riprendono da soli: il PC comandato lascia rientrare senza conferma, per 5 minuti, chi era collegato; il visore si ricollega appena l'altro torna. Su Windows con il servizio l'installazione non chiede nulla (va bene anche quando il PC è comandato da remoto); senza, Windows chiede il permesso di amministratore. Linux .deb: password di amministratore; macOS: l'app si sostituisce da sola; Android: si apre il browser con l'APK.
- **Finestra del visore ridotta a icona**: il PC remoto va in pausa (un fotogramma al secondo) e la qualità automatica non scende più; alla riapertura arriva subito l'immagine intera. Durante i collegamenti Windows non rallenta più il programma né ffmpeg (risparmio energetico), e la qualità automatica risale più in fretta.
- **Collegamento inverso in 4 giri** invece di 2: al 3° e al 4° le porte si invertono, è il PC che chiama ad aprirne una nuova (al 4° tra 443, 8443, 80 e 8080, che escono quasi da ogni rete) prima di richiedere il collegamento.
- **Linux**: il pacchetto .deb installa da solo ffmpeg (video compresso), xrandr (risoluzione adatta) e gio (cartella condivisa).

### 0.5.0 — 11 novembre 2026
Si collega con la 0.4: le funzioni nuove si attivano quando entrambi i dispositivi hanno la 0.5.
- **Video compresso H.264, H.265 e AV1** al posto dei riquadri JPEG, con ffmpeg e i codificatori della scheda video (NVIDIA, Intel, AMD, Media Foundation, VideoToolbox) o x264: molta meno banda, più fluido con video e scorrimenti. Su Windows ffmpeg si scarica con un clic dalle impostazioni.
- **Tunnel di porte**: rigctld, flrig, fldigi o l'interfaccia web della radio del PC remoto raggiungibili da qui, nel collegamento cifrato.
- **Controllo preliminare delle versioni**: prima di collegarsi, e nello storico, il programma dice se l'altro dispositivo ha una versione diversa e cosa comporta.
- **Telefono e tablet come visore** (idee da Sofatap e bVNC):
  - **gesti personalizzabili** con quattro profili pronti (Sofatap, Abitudini da touchpad, Pressione per il clic destro, Come la 0.4) e un'azione per ogni gesto: tocco, doppio tocco a sinistra o a destra, tocco con due o tre dita, pressione lunga, tocca e tieni, scorrimento con uno o due dita;
  - **pannello scorciatoie**: tieni il dito, scorri per scegliere il tasto (Spazio, Invio, Esc, frecce, Copia, Incolla, Annulla, Alt+Tab, volume…) e solleva;
  - **tocco diretto** (si clicca dove si tocca) oltre al touchpad;
  - **zoom con due dita** e spostamento sull'immagine ingrandita;
  - **striscia di scorrimento** lungo il bordo destro;
  - **velocità del puntatore** regolabile (0,25–3×), tempi della pressione e del doppio tocco;
  - **barra dei tasti personalizzabile**, con i tasti che si ripetono tenendoli premuti;
  - **dettatura vocale** con il riconoscimento del telefono;
  - **mouse e tastiera Bluetooth o USB** collegati al telefono o al tablet (anche Samsung DeX): il tasto destro non chiude più il collegamento.
- **Microfono** dal visore al PC remoto: con un cavo audio virtuale (VB-CABLE su Windows, BlackHole su Mac; su Linux il microfono virtuale si crea da solo) la tua voce diventa il microfono dei programmi di là, anche della radio.
- **Volume del PC remoto** (+, −, muto) dalla plancia, dal pannello scorciatoie e dalla barra dei tasti.
- **Risoluzione adatta**: il PC remoto passa alla risoluzione più adatta alla finestra o al telefono e a fine collegamento torna com'era (Windows e Linux X11).
- **Cartella condivisa**: una cartella del visore compare sul PC remoto come un disco (Windows: lettera di unità; Linux: GVFS; macOS: Finder), anche in sola lettura.

### 0.4.1 — 11 novembre 2026
- **La X non chiude più il programma**: nasconde la finestra e IP-DESK-CTRL resta attivo, con l'icona vicino all'orologio (barra dei menu su macOS). Il PC resta raggiungibile e i collegamenti aperti non si interrompono. Si riapre con un clic sull'icona o riavviandolo; si esce con clic destro sull'icona › «Esci da IP-DESK-CTRL» o con Impostazioni › «Esci dal programma».
- Un solo programma per utente: riavviarlo mentre è nascosto mostra la finestra di quello già attivo.
- Con l'avvio automatico all'accesso a Windows il programma parte nascosto, solo con l'icona.

### 0.4.0 — 11 novembre 2026 (protocollo 3)
- **Collegamento solo UDP**: se la connessione TCP non passa (nessuna porta aperta, niente UPnP), il collegamento va su un canale UDP affidabile e cifrato, attraverso il foro aperto nei NAT.
- **Rete nel servizio di Windows**: il PC è raggiungibile anche prima dell'accesso dell'utente, subito dopo l'accensione.
- **Riavvio del PC remoto** dalla plancia, con ricollegamento automatico.
- **Password facoltativa** per l'accesso senza conferma; il visore può ricordarla.
- **Wake-on-LAN** per accendere un PC della stessa rete dalla cronologia.
- **QR del numero**, da inquadrare con il telefono.
- **Avviso di nuova versione** con il link per scaricarla.
- **Legame con IP-TRX-CTRL**.
- **Pulsante «♥ Sostieni il progetto»** (Ko-fi) nelle impostazioni e nelle informazioni.
- **Sicurezza**: la chiave privata del PC resta dentro il servizio, che firma per conto del programma; sul PC si salva solo una verifica della password, che non basta per entrare.

### 0.3.1 — 11 novembre 2026
- **Audio su Windows**: riproduzione con WASAPI sull'uscita predefinita; se non va, prova ogni uscita waveOut.
- Se l'uscita audio sparisce o non c'è (errore «BadDeviceId»), il programma lo dice chiaramente e la riapre appena torna disponibile.

### 0.3.0 — 11 novembre 2026
- **Servizio di Windows** (facoltativo, scelto nell'installazione): il visore vede e comanda anche le finestre UAC, la schermata di blocco e i programmi avviati come amministratore.
- **Ctrl+Alt+Canc** dalla plancia.

### 0.2.1 — 11 novembre 2026
- Avvisi chiari quando il PC remoto è bloccato o lo schermo è nero.
- Nel registro delle sessioni: avvisi, monitor e fotogrammi ricevuti, per capire i problemi.

### 0.2.0 — 11 novembre 2026 (protocollo 2)
- **App Android** (APK firmato), sia come visore sia come host: il telefono si può comandare da un PC.
- **Copia/incolla** tra i dispositivi.
- **File tra i PC** con il trascinamento (drag and drop).
- Testo Unicode da tastiera (lettere accentate e simboli).

### 0.1.0 — 11 novembre 2026 (protocollo 1)
- Prima versione: desktop remoto cifrato (ECDH P-256, AES-256-GCM) per Windows a 64 e 32 bit, macOS, Linux e Raspberry Pi.
- Numero fisso di 10 cifre per ogni PC; ritrovo sui broker MQTT di IP-TRX-CTRL.
- Apertura delle porte come in IP-TRX-CTRL: UPnP/NAT-PMP, verifica dall'esterno, collegamento inverso, prova delle porte in uscita, UDP.
- Cronologia dei collegamenti con alias.
- Più monitor, audio del PC remoto.
- Zoom in percentuale e schermo intero, con la plancia di comando in alto al centro.

## Guida

La guida passo per passo è nella [wiki](https://github.com/ic8tem/IP-DESK-CTRL/wiki).

## Sostieni il progetto

IP-DESK-CTRL è gratuito e senza pubblicità. Se ti è utile puoi offrire un caffè su [Ko-fi](https://ko-fi.com/ic8tem): il pulsante «♥ Sostieni il progetto» è anche nelle impostazioni del programma.

## Licenza

IP-DESK-CTRL è software libero, Copyright (C) 2026 Costantino IC8TEM, distribuito secondo la **GNU General Public License versione 3 o successiva** (file [`LICENSE`](LICENSE)), senza alcuna garanzia. Le licenze dei componenti di terze parti sono in `THIRD-PARTY-NOTICES.txt` e nella finestra «Informazioni e licenze».

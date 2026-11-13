# Storia delle versioni


Le versioni con protocollo diverso non si collegano tra loro: aggiorna tutti i dispositivi alla stessa versione principale (la 0.4.1 si collega con la 0.4.0; la 0.5 si collega con la 0.4).

## 0.5.10 — 13 novembre 2026
- **Altre distribuzioni**: pacchetti per **Arch, Manjaro, EndeavourOS e Arch Linux ARM** (`.pkg.tar.zst`, x86_64 e aarch64), **AppImage** (un solo file per qualsiasi Linux, x86_64 e aarch64), **Snap** (amd64 e arm64), **Alpine Linux** e altre distribuzioni con musl (x64 e arm64), **Homebrew** sul Mac (`brew install --cask ip-desk-ctrl` dopo il `brew tap`). Anche questi si aggiornano da soli (pacman, snap, file AppImage sostituito, Homebrew aggiornato a ogni release). Nella guida c'è anche il Chromebook (APK Android o `.deb` nel Linux interno).

## 0.5.9 — 13 novembre 2026
- **Plancia del collegamento in alto, a sinistra o a destra** (Impostazioni › «Plancia del collegamento»): sotto resta la fascia arancione degli avvisi. La plancia è **sempre a scomparsa**: compare passando sulla linguetta o sul bordo scelto e si nasconde da sola, ma non mentre un suo menu è aperto. Di lato i comandi stanno in colonna e la linguetta col numero è scritta in verticale. Le finestre già aperte si spostano subito.
- **Guida e Ko-fi in alto** nella finestra principale, accanto allo stato «In linea». La guida apre la wiki.
- Controllo delle nuove versioni: ogni 30 minuti (come dalla 0.5.4).

## 0.5.8 — 12 novembre 2026
- **Audio del PC comandato senza uscita predefinita**: invece dell'errore «Audio di Windows non catturabile: Impossibile trovare elemento (0x80070490)» la cattura prova la prima uscita attiva; se il PC non ne ha (casse o cuffie scollegate o disattivate) lo dice chiaramente e riprova ogni 5 secondi, così l'audio parte da solo appena colleghi qualcosa. Se l'uscita sparisce durante il collegamento si riapre da sola.

## 0.5.7 — 12 novembre 2026
- **Copia e incolla di file da qualsiasi posizione, nei due versi**: su Windows gli appunti dei file si leggono e si scrivono con le funzioni di Windows (prima passavano da Avalonia, che con i file copiati in Esplora file non funzionava). I file copiati sul PC remoto con Ctrl+C arrivano da soli negli appunti di qua (fino a 2 GB); i file copiati qui si mandano con Ctrl+V nella finestra del PC remoto e di là si incollano appena arrivano. Niente rimbalzi: i file appena passati non tornano indietro.

## 0.5.6 — 12 novembre 2026
- **Più distribuzioni**: Windows ARM64 (PC con Snapdragon, con installatore e servizio), pacchetti **.rpm** per Fedora e openSUSE (x86_64, aarch64, armv7hl) e pacchetti **Slackware** (.txz) per Slackware ARM64, anche su Raspberry Pi, e Slackware64. Anche questi si aggiornano da soli (dnf, zypper o upgradepkg). Su Windows ARM64 ffmpeg si scarica nella versione ARM.

## 0.5.5 — 12 novembre 2026
- **L'aggiornamento automatico non interrompe i collegamenti**: se esce una versione nuova mentre comandi un PC o qualcuno comanda il tuo, compare solo il pulsante «Nuova versione» in alto a destra e l'installazione parte da sola appena si chiude l'ultimo collegamento (dopo il solito minuto per fermarla). «Aggiorna ora» la fa subito, con circa 45 secondi di interruzione e ripresa automatica.

## 0.5.4 — 12 novembre 2026
- **Aggiornamenti automatici**: il programma guarda ogni 30 minuti (prima ogni 12 ore) se è uscita una nuova versione. In alto a destra, accanto a «In linea», compare il pulsante **Nuova versione x.y.z**. Con l'opzione **Aggiornamenti automatici** (Impostazioni, attiva di serie) il riquadro si apre da solo e dopo 60 secondi la versione nuova si scarica, si controlla e si installa; il programma si riapre e i collegamenti riprendono. **Più tardi** la ferma per quella versione. Android: solo l'avviso.

## 0.5.3 — 12 novembre 2026
- **Finestre UAC con il servizio di Windows**: quando compariva la richiesta UAC l'immagine diventava nera («Cattura dello schermo non riuscita (BitBlt, errore 6)») e non si poteva rispondere. Ora cattura, mouse e tastiera girano su un thread che, a ogni cambio di desktop (UAC, schermata di blocco, Ctrl+Alt+Canc), viene rifatto nuovo e spostato sul desktop attivo: la finestra UAC si vede e si comanda. Nel registro delle sessioni compaiono le righe «AIUTO desktop attivo: Winlogon» / «Default».

## 0.5.2 — 11 novembre 2026
- **Porte sul router** (come IP-TRX-CTRL 0.2.8): il router si cerca prima con UPnP e solo dopo con NAT-PMP. Alcuni router rispondono a NAT-PMP ma rifiutano tutte le porte («Failed to create portmap», «overflow» oltre la porta 32767): ora, dopo 5 rifiuti, si prova l'altro protocollo, e con NAT-PMP le porte restano sotto 32768. Così si aprono anche 443, 80, 8443, 8080 per i visori su reti che fanno uscire solo quelle.
- **Copia/incolla di testi e file con i PC che hanno il servizio di Windows**: dalla 0.4 i collegamenti in arrivo li riceve il servizio, che non vede gli appunti dell'utente; ora testi e file passano dal processo di supporto, nei due versi, e i file ricevuti finiscono in Scaricati/IP-DESK-CTRL dell'utente.
- **Telefono**: la plancia di comando sta su una sola riga che scorre col dito (prima occupava mezzo schermo); il pulsante dei gesti si chiama «Gesti» (il simbolo non si vedeva); il menu dei monitor compare solo se il PC remoto ne ha più di uno.
- **Registro condivisibile dal telefono**: Informazioni › «Condividi il registro» lo manda per e-mail, WhatsApp…, per capire i collegamenti non riusciti.

## 0.5.1 — 11 novembre 2026
- **Telefono**: il numero si vede sempre intero (si rimpicciolisce per stare nella larghezza), il campo per il numero da chiamare è su tutta la larghezza con «Inquadra QR» e «Collegati» sotto, i titoli non si tagliano più.
- **Più visori insieme**: mentre uno comanda, fino a 4 altri possono collegarsi per **guardare** (solo visione: niente mouse, tastiera, file o tunnel). Chi è al PC accetta ognuno, oppure entrano da soli i numeri con accesso senza conferma; la finestrella «comandato da» dice quanti guardano e «Disconnetti» chiude tutti. Si può spegnere in Impostazioni.
- **Aggiornamento dal programma aperto**: quando esce una nuova versione compare un riquadro con «Aggiorna ora». Il programma scarica il file giusto, controlla l'impronta SHA-256 pubblicata da GitHub, lo installa da solo, si chiude e si riapre. I collegamenti aperti riprendono da soli: il PC comandato lascia rientrare senza conferma, per 5 minuti, chi era collegato; il visore si ricollega appena l'altro torna. Su Windows con il servizio l'installazione non chiede nulla (va bene anche quando il PC è comandato da remoto); senza, Windows chiede il permesso di amministratore. Linux .deb: password di amministratore; macOS: l'app si sostituisce da sola; Android: si apre il browser con l'APK.
- **Finestra del visore ridotta a icona**: il PC remoto va in pausa (un fotogramma al secondo) e la qualità automatica non scende più; alla riapertura arriva subito l'immagine intera. Durante i collegamenti Windows non rallenta più il programma né ffmpeg (risparmio energetico), e la qualità automatica risale più in fretta.
- **Collegamento inverso in 4 giri** invece di 2: al 3° e al 4° le porte si invertono, è il PC che chiama ad aprirne una nuova (al 4° tra 443, 8443, 80 e 8080, che escono quasi da ogni rete) prima di richiedere il collegamento.
- **Linux**: il pacchetto .deb installa da solo ffmpeg (video compresso), xrandr (risoluzione adatta) e gio (cartella condivisa).

## 0.5.0 — 11 novembre 2026
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

## 0.4.1 — 11 novembre 2026
- **La X non chiude più il programma**: nasconde la finestra e IP-DESK-CTRL resta attivo, con l'icona vicino all'orologio (barra dei menu su macOS). Il PC resta raggiungibile e i collegamenti aperti non si interrompono. Si riapre con un clic sull'icona o riavviandolo; si esce con clic destro sull'icona › «Esci da IP-DESK-CTRL» o con Impostazioni › «Esci dal programma».
- Un solo programma per utente: riavviarlo mentre è nascosto mostra la finestra di quello già attivo.
- Con l'avvio automatico all'accesso a Windows il programma parte nascosto, solo con l'icona.

## 0.4.0 — 11 novembre 2026 (protocollo 3)
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

## 0.3.1 — 11 novembre 2026
- **Audio su Windows**: riproduzione con WASAPI sull'uscita predefinita; se non va, prova ogni uscita waveOut.
- Se l'uscita audio sparisce o non c'è (errore «BadDeviceId»), il programma lo dice chiaramente e la riapre appena torna disponibile.

## 0.3.0 — 11 novembre 2026
- **Servizio di Windows** (facoltativo, scelto nell'installazione): il visore vede e comanda anche le finestre UAC, la schermata di blocco e i programmi avviati come amministratore.
- **Ctrl+Alt+Canc** dalla plancia.

## 0.2.1 — 11 novembre 2026
- Avvisi chiari quando il PC remoto è bloccato o lo schermo è nero.
- Nel registro delle sessioni: avvisi, monitor e fotogrammi ricevuti, per capire i problemi.

## 0.2.0 — 11 novembre 2026 (protocollo 2)
- **App Android** (APK firmato), sia come visore sia come host: il telefono si può comandare da un PC.
- **Copia/incolla** tra i dispositivi.
- **File tra i PC** con il trascinamento (drag and drop).
- Testo Unicode da tastiera (lettere accentate e simboli).

## 0.1.0 — 11 novembre 2026 (protocollo 1)
- Prima versione: desktop remoto cifrato (ECDH P-256, AES-256-GCM) per Windows a 64 e 32 bit, macOS, Linux e Raspberry Pi.
- Numero fisso di 10 cifre per ogni PC; ritrovo sui broker MQTT di IP-TRX-CTRL.
- Apertura delle porte come in IP-TRX-CTRL: UPnP/NAT-PMP, verifica dall'esterno, collegamento inverso, prova delle porte in uscita, UDP.
- Cronologia dei collegamenti con alias.
- Più monitor, audio del PC remoto.
- Zoom in percentuale e schermo intero, con la plancia di comando in alto al centro.

I file di ogni versione sono nella pagina [Releases](https://github.com/ic8tem/IP-DESK-CTRL/releases).

# Storia delle versioni


Le versioni con protocollo diverso non si collegano tra loro: aggiorna tutti i dispositivi alla stessa versione principale (la 0.4.1 si collega con la 0.4.0; la 0.5 si collega con la 0.4).

## 0.5.1 — in preparazione
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

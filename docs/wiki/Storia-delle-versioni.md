# Storia delle versioni


Le versioni con protocollo diverso non si collegano tra loro: aggiorna tutti i dispositivi alla stessa versione.

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

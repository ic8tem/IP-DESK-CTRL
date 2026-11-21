# Storia delle versioni


Le versioni con protocollo diverso non si collegano tra loro: aggiorna tutti i dispositivi alla stessa versione principale (la 0.4.1 si collega con la 0.4.0; la 0.5 si collega con la 0.4).

## 0.5.25 — 21 novembre 2026
- **Aggiornamento che non riparte, corretto**: dalla 0.5.21 l'installatore, per chiudere il programma rimasto aperto, chiudeva anche tutti i processi «figli» di IP-DESK-CTRL. Ma l'installatore stesso è figlio di IP-DESK-CTRL (del programma che lo lancia, o del servizio quando si aggiorna da solo): si chiudeva a metà, senza finire l'installazione né riaprire il programma, e il PC restava fuori linea finché qualcuno non lo riavviava a mano. Ora chiude solo il programma e i suoi processi di sistema, non sé stesso.
- **Riavvio del PC remoto anche con un account senza diritti**: senza il servizio di Windows il riavvio passava solo da shutdown.exe, e se Windows lo negava all'account (utente senza diritti di arresto o criterio dell'organizzazione) non succedeva niente. Ora si provano tre strade: il servizio (funziona sempre, qualunque sia l'account), shutdown.exe e le API di Windows con il privilegio di arresto. Se nessuna funziona il visore lo dice chiaramente e propone di installare il servizio sul PC remoto. Ogni tentativo va nel registro con il motivo.

## 0.5.24 — 21 novembre 2026
- **Appunti nel registro**: quando chi comanda preme Ctrl+C, Ctrl+X o Ctrl+V, sessioni.log del PC comandato scrive cosa c'era negli appunti prima e dopo (testo, quanti file, niente) e dove andrà: verso chi comanda, oppure perché non è cambiato niente (niente selezionato, programma da amministratore senza servizio, schermata protetta di UAC o di blocco). In tutti e due i PC si vede anche quando il testo copiato parte, arriva e finisce negli appunti, quando i file copiati vengono annunciati e se il visore li prende o perché no, e gli errori di lettura degli appunti, che prima venivano ignorati in silenzio.

## 0.5.23 — 21 novembre 2026
- **Avviso UAC nella plancia**: quando sul PC remoto si apre una richiesta UAC, il visore lo dice chiaramente: con il servizio «la vedi sullo schermo, rispondi Sì o No», senza il servizio «non si vede da qui, deve rispondere chi è al PC».
- **Servizio installabile a distanza**: se il PC remoto ha IP-DESK-CTRL installato con l'installatore ma senza il servizio, da Tasti ▾ (o dall'avviso UAC) si chiede di installarlo. Chi è al PC risponde Sì alla richiesta di Windows una volta sola, il programma si riavvia lì con lo stesso numero e il collegamento riprende da solo con l'accesso completo (UAC, schermata di blocco, Ctrl+Alt+Canc, programmi da amministratore).
- **Avvia come amministratore** (Tasti ▾): con il servizio, apre sul PC remoto Prompt dei comandi, PowerShell, Gestione attività, Editor del Registro, Servizi, Gestione computer o Gestione dispositivi già da amministratore, senza la richiesta UAC. È spento di serie: lo permette chi è al PC remoto in Impostazioni, e solo se il suo account è amministratore. Ogni avvio va nel registro.
- **Registrazione e privacy**: prima di registrare una finestra spiega che il video riprende tutto lo schermo remoto e chiede di confermare il consenso di chi usa quel PC; chi è davanti al PC remoto vede un avviso rosso «Sessione registrata» finché la registrazione è attiva.
- **«Adatta la risoluzione» rispettato anche dal servizio**: se chi è al PC toglieva il permesso nelle Impostazioni, il servizio non lo sapeva e la risoluzione cambiava lo stesso.

## 0.5.22 — 21 novembre 2026
- **Gestione file a due pannelli** (File ▾ › «Gestione file a due pannelli…»): a sinistra le cartelle di questo PC, a destra quelle del PC remoto, con dischi e cartelle utente (Desktop, Documenti, Scaricati, Immagini) a portata di mano. Si trasferisce nei due versi con i pulsanti → e ←, con il doppio clic, trascinando da un pannello all'altro o da Esplora risorse sul pannello remoto; si creano cartelle, si rinomina (F2) e si elimina (Canc, con conferma). I trasferimenti vanno in coda, uno alla volta, con avanzamento e velocità, e si possono fermare. Sul PC remoto si lavora con i permessi dell'utente collegato, anche quando il collegamento lo riceve il servizio. È anche l'alternativa al trascinamento quando Windows lo blocca.
- **Chat**: pulsante «Chat» nella plancia. Chi è davanti al PC comandato vede aprirsi una finestra in primo piano e risponde da lì (anche dal pulsante «Chat» nel riquadro del collegamento in arrivo). I messaggi restano nel registro.
- **Lavagna** (✎ nella plancia): si disegna con il mouse sopra l'immagine e i tratti rossi compaiono sullo schermo del PC remoto, per indicare un pulsante o una riga. Clic destro cancella; ripremendo ✎ si torna a comandare e i disegni spariscono. Sul PC remoto la lavagna si toglie anche con un clic o da sola dopo 20 secondi senza nuovi tratti.
- **Registrazione della sessione** (⏺ nella plancia): l'immagine del PC remoto va in un video MP4 nella cartella Video\IP-DESK-CTRL, finché non si preme ⏹ o ci si scollega. Serve ffmpeg (lo stesso del video compresso).
- **Rubrica**: i collegamenti precedenti si possono mettere in gruppi (··· › «Metti in un gruppo…»), cercare per nome, numero o gruppo e ordinare per ultimo collegamento, nome o «in linea prima»; per ogni gruppo si vede quanti PC sono in linea.
- **Relè sul broker MQTT**: quando tra i due PC non passa niente (né diretto, né inverso, né solo UDP: per esempio con l'IP condiviso dell'operatore da entrambe le parti) il collegamento passa dal broker MQTT, sempre cifrato da un capo all'altro. È lento e l'immagine si alleggerisce, ma ci si collega lo stesso. Funziona tra PC entrambi alla 0.5.22.
- **Risoluzione adattata anche con il servizio**: «Adatta risoluzione» non cambiava lo schermo dei PC con il servizio di Windows («Monitor DISPLAY1 non trovato»), perché il servizio non vede gli schermi dell'utente. Ora la cambia il processo di sistema nella sessione dell'utente, e a fine collegamento la rimette com'era.

## 0.5.21 — 20 novembre 2026
- **Mai come amministratore**: se IP-DESK-CTRL viene avviato come amministratore su un PC con il servizio, si riavvia da solo come utente normale. Tutto quello che richiede i privilegi (UAC, schermata di blocco, programmi da amministratore, Ctrl+Alt+Canc) lo fa già il servizio, mentre un programma avviato come amministratore non riceve i file trascinati da Esplora risorse: Windows lo impedisce. Nella versione portatile, senza il servizio, resta come lo si avvia.
- **Trascinamento dei file nel registro**: inizio sopra la finestra del visore, uscita senza rilasciare, rilascio con il numero di file, e sul PC remoto l'esito dell'incolla, per capire dove si ferma quando non funziona.
- **Aggiornamento con il servizio senza «timed out»**: se un altro PC aveva appena chiesto a questo di aggiornarsi, il servizio stava già installando la versione nuova e la finestra di aggiornamento del programma, chiedendo la stessa cosa, si fermava dopo 5 secondi con «The operation has timed out». Ora il servizio risponde che l'aggiornamento è già in corso, il programma aspetta fino a un minuto se il servizio è occupato e non apre la sua finestra quando l'aggiornamento lo sta già facendo il servizio: anzi si chiude subito, e lo riapre il servizio a installazione finita. In più l'installatore stesso chiude il programma rimasto aperto: chiudendo la finestra il programma si nasconde vicino all'orologio invece di uscire, e l'installatore restava fermo ad aspettarlo finché qualcuno non lo chiudeva a mano.
- **Intestazione in ordine**: un messaggio di stato lungo (per esempio «… ha una versione più nuova: il servizio aggiorna IP-DESK-CTRL…») allargava la fila in alto a destra fino a coprire il titolo e i pulsanti. Ora lo stato in alto resta su una riga corta, con il testo intero al passaggio del mouse, e il messaggio dell'aggiornamento va nella riga di stato sotto il numero.

## 0.5.20 — 20 novembre 2026
- **Registro in diretta nel collegamento**: la finestra «Collegamento a …» mostra sotto lo stato una finestrella scura, come quella dell'abbinamento di IP-TRX-CTRL, con ogni passo del collegamento, l'ora al millesimo e un segno colorato (▶ inviato, ◀ ricevuto, ✓ riuscito, ✗ non riuscito): broker, porte, collegamento inverso, UDP, aggiornamento dell'altro PC. Se il collegamento non riesce resta visibile per vedere dove si è fermato; «Copia registro» copia tutto negli appunti.
- **Processo di sistema che riparte da solo**: su un PC con il servizio, quando la sessione di Windows cambia (utente scollegato, cambio utente, desktop remoto) il processo di sistema che fa vedere e comandare lo schermo si chiudeva e per il resto della sessione mouse e tastiera non arrivavano più. Ora riparte da solo nella sessione attiva entro pochi secondi.
- **Arresti del servizio nel registro**: se il servizio si chiude da solo, al riavvio copia da Windows (Visualizzatore eventi) il motivo dell'arresto in sessioni.log come riga «ERRORE», così non serve cercarlo a mano.
- **Avvisi senza ripetizioni**: due avvisi che si alternavano (PC bloccato / processo di sistema chiuso) finivano nel registro due volte al secondo; ora ogni avviso al massimo una volta al minuto.
- **Registro più leggibile**: «nessuna uscita audio attiva» compare una volta sola invece di una riga ogni 5 secondi; gli avvisi normali di ffmpeg all'avvio del video diventano una riga con il conteggio; i collegamenti di prova dei servizi che verificano la porta non risultano più come «handshake non riuscito».
- **Spegnimento del PC nel registro**: quando il PC si spegne o si riavvia il servizio di Windows scrive «SERVIZIO fermato: il PC si spegne o si riavvia». Prima mancava, e un normale riavvio sembrava un arresto improvviso del servizio.

## 0.5.19 — 18 novembre 2026
- **Windows 7 SP1, 8 e 8.1**: nuovi pacchetti `win7` a 64 e 32 bit (installatore e versione senza installazione), compilati con .NET 6 come in YA-MS, perché .NET 8 su quei sistemi non parte. Si aggiornano da soli con i pacchetti `win7`; quelli per Windows 10 e 11 restano come prima.

## 0.5.18 — 16 novembre 2026
- **Più persone comandano insieme**: se due o più si collegano allo stesso PC, ora possono comandarlo tutti (fino a 5 in tutto), non più uno solo con gli altri a guardare. Gli altri entrano con le stesse regole del primo: richiesta da accettare oppure accesso senza conferma, con la password se è impostata. L'interruttore di mouse e tastiera e «Disconnetti» valgono per tutti; se il primo esce, la finestrella «comandato da» passa al successivo.

## 0.5.17 — 16 novembre 2026
- **Due porte scelte da te**: in Impostazioni › «Porte aperte sul router» si possono scrivere due porte aperte a mano sul router (inoltro TCP e UDP verso il PC, stesso numero dentro e fuori). Si provano per prime, anche con l'UPnP; vale la prima raggiungibile da internet. Con il servizio di Windows le apre il servizio.
- **Tipo di NAT riconosciuto meglio**: per capire se il NAT è «a cono» (perforabile) o simmetrico si interrogavano due server STUN di Google che a volte hanno lo stesso indirizzo, e così un NAT simmetrico (frequente con le chiavette e i router 4G/5G) sembrava «a cono», la previsione della porta non partiva e la perforazione UDP falliva. Ora il secondo server è di un altro gestore (Cloudflare) e due server con lo stesso indirizzo non valgono come due prove.
- **Porte che rispondono ma non sono del programma**: se i servizi di verifica trovano la porta aperta ma al programma non arriva nessun collegamento, a rispondere è un altro apparecchio (spesso la pagina del router sulla porta 80 o 443): la porta ora si considera chiusa, invece di mandare l'altro PC a bussare alla porta sbagliata.

## 0.5.16 — 16 novembre 2026
- **UPnP dall'apparecchio sbagliato**: in alcune case all'UPnP non risponde il router che porta a internet ma un altro apparecchio della rete (un ripetitore o nodo mesh in modalità router, un decoder, un router collegato a un altro router). Si riconosce perché il suo «IP esterno» sta nella stessa rete del PC (per esempio PC 192.168.1.186, «router» 192.168.1.157). Le porte che apre restano dentro quell'apparecchio, perciò ora il programma non lo usa e scrive nel registro di attivare l'UPnP sul router principale. Prima provava 6 porte inutili e si collegava solo con il collegamento inverso o con UDP.

## 0.5.15 — 15 novembre 2026
- **Dati nella cartella del programma**: impostazioni, chiavi, storico e registri stanno ora nella sottocartella `dati` di dove è installato IP-DESK-CTRL (Windows: `C:\Program Files\IP-DESK-CTRL\dati`; Linux: `/opt/ip-desk-ctrl/dati`; versione portatile e AppImage: accanto al programma). Al primo avvio tutto quello che c'era in AppData, in `~/.config` o in ProgramData si sposta lì da solo e la cartella vecchia si cancella: in AppData non si scrive più niente. Ogni utente ha la sua sottocartella `dati/utenti/<nome>`, leggibile solo da lui; la chiave del servizio di Windows sta in `dati/servizio`, leggibile solo dal sistema e dagli amministratori. Restano nelle cartelle del sistema solo macOS (l'app si sostituisce intera a ogni aggiornamento), Snap, Flatpak e Android.
- **Un solo registro**: `sessioni.log` e `errori.log` sono fusi in `sessioni.log`, in ordine di tempo; gli errori compaiono come righe «ERRORE». Anche il servizio di Windows scrive nello stesso file, così un problema si legge tutto di seguito.
- **Collegamento più rapido nelle reti chiuse**: 2 giri invece di 4. Al primo giro si chiede subito il collegamento inverso e parte in parallelo la strada solo UDP; al secondo le porte si invertono su 443, 8443, 80 o 8080.
- **Doppio NAT / CGNAT**: se l'IP che vede internet non è quello del router, si prova una sola porta invece di 6 (le altre sarebbero bloccate allo stesso modo dal router davanti) e le verifiche esterne non vengono più rifiutate per troppe richieste.
- **Aggiornamento prima della sessione**: collegandoti a un PC con una versione più vecchia, IP-DESK-CTRL gli chiede di aggiornarsi (fino a 3 volte, aspettando ogni volta che ricompaia con la versione nuova) e poi si collega. Su Windows con il servizio l'aggiornamento lo scarica e lo installa il servizio, anche se al PC non c'è nessuno; altrimenti il programma si aggiorna da solo dopo 5 secondi. Vale per i PC che hanno già almeno la 0.5.15: con le versioni prima la richiesta non si capisce e il collegamento parte subito. Se il PC remoto non può aggiornarsi da solo (Android, installazione da Flathub) lo dice subito e il collegamento parte senza aspettare.

## 0.5.14 — 14 novembre 2026
- **Tasto Windows e le sue combinazioni** (Win+R, Win+E, Win+D, Win+X…): con la finestra del PC remoto in primo piano ora vanno al PC remoto invece di aprire «Esegui» o il menu Start su questo. Restano a questo PC solo Win+L e Ctrl+Alt+Canc, che Windows non lascia intercettare (Ctrl+Alt+Canc si manda dal menu «Tasti»).
- **Windows 7 e 8**: l'installatore ora si ferma subito con un avviso («serve Windows 10 o successivo») invece di installare un programma che poi non parte con l'errore «GetSystemTimePreciseAsFileTime».

## 0.5.13 — 14 novembre 2026
- **Video compresso (H.264, H.265, AV1) che non partiva**: il visore buttava il primo fotogramma intero mentre riconosceva il flusso, quindi non decodificava niente e il PC comandato ripartiva ogni 4 secondi. Ora il primo fotogramma arriva; e se il visore non restituisce fotogrammi per due volte di fila, si torna da soli ai riquadri JPEG con un avviso.
- **Lingua automatica giusta**: con «Automatico» il programma per PC sceglieva sempre l'inglese; ora legge la lingua di Windows, di Linux (LANG/LANGUAGE) e di macOS.
- **Registro sempre in italiano**: alcune righe di `sessioni.log` comparivano tradotte (anche a metà); ora il registro resta in italiano qualunque sia la lingua scelta.

## 0.5.12 — 14 novembre 2026
- **36 lingue** (Impostazioni › «Lingua»): italiano, inglese, tedesco, francese, spagnolo, portoghese (anche brasiliano), olandese, svedese, norvegese, danese, islandese, estone, lettone, lituano, polacco, ceco, ungherese, rumeno, greco, russo, ucraino, turco, afrikaans, arabo, ebraico, urdu, hindi, bengalese, thailandese, indonesiano, filippino, cinese semplificato e tradizionale, giapponese, coreano. «Automatico» segue la lingua del sistema; la nuova lingua vale dal riavvio (pulsante «Riavvia ora»). Arabo, ebraico e urdu si leggono da destra a sinistra; lo schermo del PC remoto resta com'è.
- **Pacchetti Linux aggiornati senza password**: .deb, .rpm, Arch e Slackware installano una regola di sistema (polkit) che lascia girare senza password solo lo script di aggiornamento di IP-DESK-CTRL. Lo script non installa file passati dal programma: scarica da solo il pacchetto dalla release ufficiale su GitHub, ne controlla l'impronta SHA-256 e accetta solo versioni più nuove. Vale per l'utente della sessione attiva (anche quando il PC è comandato da IP-DESK-CTRL). Lo Snap chiede ancora la password.
- **Broker MQTT a scelta** (Impostazioni › «Broker MQTT»): uno per riga, anche cifrati con TLS (`mqtts://host:8883` o la porta 8883). Valgono subito, senza riavviare; con il servizio di Windows li usa anche lui. «Predefiniti» rimette hivemq, mosquitto ed emqx.
- **Nuovi modi di installare**: **Scoop** (bucket in questo repository: `scoop bucket add ip-desk-ctrl https://github.com/ic8tem/IP-DESK-CTRL`, poi `scoop install ip-desk-ctrl`), **Chocolatey** (pacchetto `.nupkg` allegato alla release), **Flatpak** (`.flatpak` per x86_64 e aarch64, sessioni X11) e i manifesti per **winget** (nel catalogo quando Microsoft li accetterà). Tutti si aggiornano da soli come le altre versioni.
- Su Android le frasi con «dispositivo» ora si traducono anche loro.

## 0.5.11 — 14 novembre 2026
- **Tunnel per la radio**: il modello rigctld usa di qua la porta **4542** (arriva alla 4532 del PC remoto), perché la 4532 di qua è spesso già occupata da un rigctld locale (IP-TRX-CTRL, WSJT-X…); scegliendolo compare l'avviso con l'indirizzo da impostare.
- **Installatore di Windows**: nuova opzione «Scarica ffmpeg» (attiva di serie), che mette ffmpeg nella cartella del programma: il video compresso H.264/H.265/AV1 c'è subito, anche per il servizio.
- **Collegamento più rapido nelle reti difficili**: dal giro 3 dei tentativi la strada solo UDP parte in parallelo ai giri con le porte invertite, e vince la prima che riesce (prima partiva solo dopo il giro 4).

## 0.5.10 — 14 novembre 2026
- **Aggiornamenti automatici spiegati** nel README, nella wiki e nella pagina della release: ci sono per tutte le distribuzioni, tranne Android/Chromebook (solo avviso); i pacchetti di sistema Linux chiedono la password di amministratore.
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

# Sicurezza

## Chiavi e numero

- Ogni dispositivo ha una coppia di chiavi **ECDSA P-256**, custodita da Windows (DPAPI), dal Portachiavi di macOS o, su Linux, in un file cifrato con permessi 600.
- Il **numero di 10 cifre** è ricavato dalla chiave pubblica (PBKDF2-SHA256, 100 000 iterazioni): chi si collega controlla che il PC che risponde abbia davvero la chiave di quel numero. Un estraneo non può spacciarsi per il tuo PC.
- Con il [servizio di Windows](Servizio-di-Windows) la chiave privata sta solo nel servizio: il programma gli chiede le firme e le verifica.
- **Chiave ricordata** (dalla 0.5.36): al primo collegamento il visore ricorda la chiave intera del PC chiamato. Se un'altra volta risponde lo stesso numero con un'altra chiave, il collegamento si ferma subito, prima di mandare qualunque cosa (password ricordata compresa), e nel registro compare una riga «SICUREZZA».

## Collegamento

- Scambio di chiavi **ECDH P-256 effimero**, firmato da entrambi i dispositivi.
- Cifratura **AES-256-GCM** con due chiavi distinte (una per verso) e contatore anti-ripetizione; anche UDP è cifrato AES-256-GCM.
- **Segretezza in avanti**: le chiavi di sessione non si possono ricavare dopo, nemmeno rubando le chiavi del PC.

## Broker MQTT

Sui broker pubblici passano solo indirizzo e porta (e, per il collegamento solo UDP e il Wake-on-LAN, candidati UDP, MAC e richieste), **firmati e cifrati**. I nomi dei canali derivano dal numero ma non lo mostrano. Dalla 0.5.37 il collegamento ai broker è anche **cifrato con TLS** (hivemq ed emqx sulla 8883, mosquitto sulla 8886), così chi osserva la rete non vede nemmeno i nomi dei canali; dove la porta cifrata è bloccata si ripiega sulla 1883, e i messaggi restano comunque firmati e cifrati da un PC all'altro. Le versioni vecchie (in chiaro) e quelle nuove (cifrate) si parlano lo stesso sugli stessi broker.

## Consenso e password

- Ogni collegamento va **accettato** da chi è al PC, salvo i numeri con [accesso senza conferma](Accesso-senza-conferma).
- La password dell'accesso senza conferma non viaggia mai in chiaro: il visore ne ricava una chiave PBKDF2-SHA256 (200 000 iterazioni) e la manda solo dentro il canale già cifrato. Sul PC comandato restano solo il sale e un'impronta di quella chiave, che non basta per entrare.
- Dopo 5 password sbagliate il numero è bloccato per 10 minuti.
- **Elenco protetto dei PC «senza conferma»** (dalla 0.5.36): con il servizio di Windows l'elenco dei numeri che entrano senza conferma sta nella cartella del servizio, leggibile e modificabile solo da sistema e amministratori. Lo storico condiviso (che ogni utente del PC può scrivere) serve solo per la rubrica: scriverci «senza conferma» a mano non basta più per entrare. I numeri già autorizzati prima della 0.5.36 passano da soli nell'elenco protetto, e il registro li elenca.
- **Freno alle richieste** (dalla 0.5.36): un numero a cui hai detto No (o che non ha avuto risposta) non può richiedere per 2 minuti; se in un minuto arrivano più di 5 richieste da numeri sconosciuti, per 3 minuti quelli sconosciuti vengono respinti da soli. Chi è già nella rubrica passa sempre. Così nessuno può riempirti lo schermo di richieste sperando in un Sì per sbaglio.
- Chi è al PC vede sempre la finestrella con **Disconnetti** e può togliere mouse e tastiera.

## Su GitHub

- **CodeQL** analizza il codice C# a ogni modifica e ogni settimana, cercando falle di sicurezza.
- **Dependabot** segnala le librerie con vulnerabilità note e propone gli aggiornamenti.
- Le falle si segnalano in privato (scheda *Security* › *Report a vulnerability*): vedi `SECURITY.md`.

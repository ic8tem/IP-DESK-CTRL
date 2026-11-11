# Sicurezza

## Chiavi e numero

- Ogni dispositivo ha una coppia di chiavi **ECDSA P-256**, custodita da Windows (DPAPI), dal Portachiavi di macOS o, su Linux, in un file cifrato con permessi 600.
- Il **numero di 10 cifre** è ricavato dalla chiave pubblica (PBKDF2-SHA256, 100 000 iterazioni): chi si collega controlla che il PC che risponde abbia davvero la chiave di quel numero. Un estraneo non può spacciarsi per il tuo PC.
- Con il [servizio di Windows](Servizio-di-Windows) la chiave privata sta solo nel servizio: il programma gli chiede le firme e le verifica.

## Collegamento

- Scambio di chiavi **ECDH P-256 effimero**, firmato da entrambi i dispositivi.
- Cifratura **AES-256-GCM** con due chiavi distinte (una per verso) e contatore anti-ripetizione; anche UDP è cifrato AES-256-GCM.
- **Segretezza in avanti**: le chiavi di sessione non si possono ricavare dopo, nemmeno rubando le chiavi del PC.

## Broker MQTT

Sui broker pubblici passano solo indirizzo e porta (e, per il collegamento solo UDP e il Wake-on-LAN, candidati UDP, MAC e richieste), **firmati e cifrati**. I nomi dei canali derivano dal numero ma non lo mostrano.

## Consenso e password

- Ogni collegamento va **accettato** da chi è al PC, salvo i numeri con [accesso senza conferma](Accesso-senza-conferma).
- La password dell'accesso senza conferma non viaggia mai in chiaro: il visore ne ricava una chiave PBKDF2-SHA256 (200 000 iterazioni) e la manda solo dentro il canale già cifrato. Sul PC comandato restano solo il sale e un'impronta di quella chiave, che non basta per entrare.
- Dopo 5 password sbagliate il numero è bloccato per 10 minuti.
- Chi è al PC vede sempre la finestrella con **Disconnetti** e può togliere mouse e tastiera.

# Sicurezza · Security

## Segnalare una vulnerabilità · Reporting a vulnerability

**Italiano** — Se trovi una falla di sicurezza in IP-DESK-CTRL, per favore **non aprire una issue pubblica**.
Usa il pulsante *Report a vulnerability* nella scheda *Security* del repository (segnalazione privata),
oppure scrivi a ccerrotta@gmail.com. Indica la versione, il sistema operativo e i passi per riprodurla.
Rispondo appena possibile e pubblico la correzione in una nuova versione, citandoti se lo desideri.

**English** — If you find a security issue in IP-DESK-CTRL, please **do not open a public issue**.
Use *Report a vulnerability* in the repository's *Security* tab (private report), or write to
ccerrotta@gmail.com. Include the version, operating system and steps to reproduce.

## Versioni supportate · Supported versions

Solo l'ultima versione pubblicata riceve le correzioni (il programma si aggiorna da solo).
Only the latest release receives fixes (the program updates itself).

## Come è protetto il collegamento · How connections are protected

- Ogni PC ha una chiave ECDSA P-256; il numero di 10 cifre deriva dalla chiave e il visore controlla che
  chi risponde abbia proprio quel numero. Dalla 0.5.36 il visore ricorda anche la chiave intera e rifiuta
  un PC che si presenta con lo stesso numero ma un'altra chiave.
- Scambio di chiavi ECDH P-256 effimero (segretezza in avanti), cifratura AES-256-GCM con contatore
  anti-ripetizione, chiavi diverse per ogni verso e per UDP.
- La password dell'accesso senza conferma non viaggia mai in chiaro (PBKDF2-SHA256, 200 000 iterazioni);
  l'host conserva solo un verificatore; 5 errori bloccano quel numero per 10 minuti.
- Gli aggiornamenti si installano solo se l'impronta SHA-256 coincide con quella che GitHub calcola per
  il file della release.

- Each PC has an ECDSA P-256 key; the 10-digit number is derived from it and the viewer checks it. Since
  0.5.36 the viewer also pins the full key and refuses a PC that shows the same number with another key.
- Ephemeral ECDH P-256 key exchange (forward secrecy), AES-256-GCM with replay counter.
- The unattended-access password never travels in clear (PBKDF2-SHA256); the host keeps only a verifier;
  5 failures block that number for 10 minutes.
- Updates are installed only when their SHA-256 matches the digest GitHub computes for the release asset.

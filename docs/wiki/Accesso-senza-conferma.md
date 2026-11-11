# Accesso senza conferma

Per i PC di fiducia (i tuoi PC, quello della stazione…) puoi evitare che ogni volta qualcuno debba accettare la richiesta.

## Come si attiva

Sul **PC da comandare**, nello storico, apri il menu **`···`** della riga del visore e scegli **Consenti di entrare in questo PC senza conferma**. Si può fare anche dalla richiesta di consenso, spuntando **Non chiedere più per questo numero**. Da quel momento quel numero entra subito, con mouse e tastiera.

## Password (facoltativa)

In **Impostazioni › Password per l'accesso senza conferma…** puoi impostare una password. Chi entra senza conferma deve anche conoscerla:

- al primo collegamento il visore la chiede e può **ricordarla** per quel PC;
- dopo **5 password sbagliate** quel numero è bloccato per **10 minuti**.

Se cambi la password, i visori che la ricordavano la chiederanno di nuovo.

## Riavvio del PC remoto

Dalla plancia, **Tasti ▾ › Riavvia il PC remoto…**. La plancia si ricollega da sola appena il PC torna in linea. Con il [servizio di Windows](Servizio-di-Windows) il PC torna raggiungibile prima ancora dell'accesso, ma solo per i numeri con accesso senza conferma.

## Wake-on-LAN (accendere un PC spento)

Dallo storico: **`···` › Accendi (Wake-on-LAN)**.

- Se sei **nella stessa rete** del PC spento, il pacchetto parte direttamente dal tuo dispositivo.
- Se sei **fuori**, lo manda un altro PC **acceso della stessa rete** con IP-DESK-CTRL, ma solo se tu puoi entrare in quel PC senza conferma.

Il PC da accendere deve avere il Wake-on-LAN attivo nel **BIOS/UEFI** e nelle proprietà della **scheda di rete** (di solito funziona solo via cavo).

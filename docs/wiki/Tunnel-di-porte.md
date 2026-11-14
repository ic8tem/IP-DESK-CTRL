# Tunnel di porte

Un **tunnel** fa arrivare un programma di qui a un servizio raggiungibile dal PC remoto, passando nel collegamento cifrato. È pensato per la radio: il programma di log, WSJT-X o il comando del rotore sul tuo PC parla con **rigctld**, **flrig** o l'**interfaccia web della radio** che stanno vicino alla radio.

## Come si usa

1. Collegati al PC remoto (serve il permesso di mouse e tastiera).
2. Nella plancia premi **Tunnel ▾**.
3. Scegli un modello o scrivi tu:
   - **Porta di qui**: il programma di qui si collegherà a `127.0.0.1:` questa porta;
   - **Indirizzo dal PC remoto**: `127.0.0.1` se il servizio gira sul PC remoto stesso, oppure l'indirizzo di un apparato della sua rete (es. `192.168.1.50` per l'interfaccia web della radio);
   - **Porta**: quella del servizio.
4. **Aggiungi e avvia**. Nel programma di qui imposta `127.0.0.1` e la porta di qui.

I tunnel si salvano per ogni PC; con **Avvia questi tunnel da soli a ogni collegamento** ripartono quando ti ricolleghi.

## Modelli pronti

| Servizio | Porta di qui | Indirizzo e porta dal PC remoto |
|---|---|---|
| rigctld (Hamlib) | **4542** | 127.0.0.1:4532 |
| rotctld (rotore, Hamlib) | 4533 | 127.0.0.1:4533 |
| flrig | 12345 | 127.0.0.1:12345 |
| fldigi (XML-RPC) | 7362 | 127.0.0.1:7362 |
| Interfaccia web (radio, router…) | 8080 | 192.168.1.1:80 (cambia l'indirizzo) |

Per l'interfaccia web apri nel browser di qui `http://127.0.0.1:8080`.

Per **rigctld** la porta di qui è **4542** e non 4532: sul PC da cui comandi la 4532 è spesso già occupata da un rigctld locale (IP-TRX-CTRL, WSJT-X…). Nel programma di qui imposta quindi `127.0.0.1:4542`, che arriva alla 4532 del PC remoto.

## Note

- Solo TCP. I programmi di qui si collegano solo da questo dispositivo (127.0.0.1), non da altri della tua rete.
- Se la porta di qui è occupata (per esempio rigctld gira anche qui), scegline un'altra e impostala nel programma.
- Chi è al PC remoto può impedire i tunnel: Impostazioni › *Chi si collega può aprire tunnel di porte*.
- Il PC remoto deve avere la 0.5 o successiva.

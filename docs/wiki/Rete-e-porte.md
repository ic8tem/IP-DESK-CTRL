# Rete e porte

IP-DESK-CTRL usa lo stesso sistema di **IP-TRX-CTRL**: di norma non devi aprire porte a mano.

## Come i PC si trovano

1. All'avvio il programma apre una porta casuale (TCP e UDP con lo stesso numero) sul router con **UPnP/NAT-PMP** e la **verifica dall'esterno**. Se non è raggiungibile ne prova un'altra.
2. Lascia indirizzo pubblico, porta e indirizzo locale, **firmati e cifrati**, sui **broker MQTT** pubblici (hivemq, mosquitto, emqx) e li aggiorna ogni 3 minuti.
3. Il visore li legge e si collega **diretto** (prima in rete locale, se i due PC sono nella stessa casa).
4. Se non ci riesce chiede il **collegamento inverso**: è l'host a collegarsi alla porta del visore. Se la rete del visore lascia uscire solo poche porte (443, 80…) l'host usa proprio quelle.
5. Se anche il collegamento inverso non passa, i due PC si scambiano i **candidati UDP**, forano i NAT e fanno **tutto su UDP**, con un trasporto affidabile e ordinato sotto lo stesso canale cifrato.
6. Con un collegamento TCP riuscito i due PC provano comunque **UDP**: audio e mouse passano lì, lo schermo resta su TCP.

La plancia mostra la strada usata: **TCP**, **UDP + TCP** o **solo UDP**.

## Casi tipici

| Situazione | Risultato |
|---|---|
| Almeno un router con UPnP attivo | TCP diretto o inverso |
| Nessun router con UPnP | di solito **solo UDP**, funziona |
| NAT simmetrico (es. CGNAT dell'operatore) su **entrambi** i lati | anche UDP può non passare |

Se hai un router con UPnP disattivato, attivarlo migliora la velocità ma non è indispensabile.

## Aprire una porta a mano

Non serve quasi mai. Se vuoi farlo, guarda nel `sessioni.log` la porta scelta dal programma e inoltrala (TCP e UDP) verso il PC nel router.

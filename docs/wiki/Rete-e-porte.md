# Rete e porte

IP-DESK-CTRL usa lo stesso sistema di **IP-TRX-CTRL**: di norma non devi aprire porte a mano.

## Come i PC si trovano

1. All'avvio il programma apre una porta casuale (TCP e UDP con lo stesso numero) sul router con **UPnP/NAT-PMP** e la **verifica dall'esterno**. Se non è raggiungibile ne prova un'altra.
2. Lascia indirizzo pubblico, porta e indirizzo locale, **firmati e cifrati**, sui **broker MQTT** (di serie hivemq, mosquitto ed emqx; si cambiano in Impostazioni › «Broker MQTT», anche cifrati con `mqtts://host:8883`) e li aggiorna ogni 3 minuti.
3. Il visore li legge e si collega **diretto** (prima in rete locale, se i due PC sono nella stessa casa).
4. Se non ci riesce chiede il **collegamento inverso**: è l'host a collegarsi alla porta del visore. Se la rete del visore lascia uscire solo poche porte (443, 80…) l'host usa proprio quelle. I giri sono **2**: nel primo chiede all'altro PC di collegarsi; nel secondo le porte si **invertono**: è il PC che chiama ad aprirne una nuova tra 443, 8443, 80 e 8080 prima di richiedere di nuovo il collegamento.
5. Fin dal **primo giro**, in parallelo, i due PC si scambiano anche i **candidati UDP**, forano i NAT e fanno **tutto su UDP**, con un trasporto affidabile e ordinato sotto lo stesso canale cifrato. Vince la prima strada che riesce, TCP inverso o solo UDP.
6. Se non passa niente, ultima strada: il **relè sul broker MQTT**. I dati del collegamento (sempre cifrati da un capo all'altro: il broker non li può leggere) passano dal broker su due argomenti usa e getta. È lento e l'immagine si alleggerisce, ma ci si collega anche con l'IP condiviso dell'operatore su entrambi i lati. Serve la 0.5.22 su tutti e due i PC.
7. Con un collegamento TCP riuscito i due PC provano comunque **UDP**: audio e mouse passano lì, lo schermo resta su TCP.

La plancia mostra la strada usata: **TCP**, **UDP + TCP**, **solo UDP** o **relè MQTT**.

## Casi tipici

| Situazione | Risultato |
|---|---|
| Almeno un router con UPnP attivo | TCP diretto o inverso |
| Nessun router con UPnP | di solito **solo UDP**, funziona |
| NAT simmetrico (es. CGNAT dell'operatore) su **entrambi** i lati | anche UDP può non passare: **relè MQTT**, lento ma funziona |

Se hai un router con UPnP disattivato, attivarlo migliora la velocità ma non è indispensabile.

## Aprire una porta a mano

Non serve quasi mai. Se vuoi farlo, guarda nel `sessioni.log` la porta scelta dal programma e inoltrala (TCP e UDP) verso il PC nel router.

### L'UPnP risponde ma le porte restano chiuse

Se nel registro compare «l'UPnP l'ha aperta un altro apparecchio della rete», il «router» trovato non è quello che porta a internet: il suo IP esterno sta nella stessa rete del PC (per esempio 192.168.1.157 con il PC a 192.168.1.186). Di solito è un ripetitore o un nodo mesh in modalità router, un decoder o un secondo router collegato in cascata. Rimedi: attivare l'UPnP sul router principale (di solito `http://192.168.1.1` o `http://192.168.0.1`), oppure mettere quell'apparecchio in modalità «access point» o «bridge».

### Porte aperte a mano

Se l'UPnP non c'è o non funziona, apri sul router una o due porte (inoltro TCP e UDP verso l'indirizzo locale del PC, stesso numero dentro e fuori, per esempio 47000) e scrivile in Impostazioni › «Porte aperte sul router». Il programma le prova per prime e controlla da internet che portino davvero a lui; il risultato è nel registro («uso la porta scelta…» oppure «non è raggiungibile: controlla l'inoltro»).

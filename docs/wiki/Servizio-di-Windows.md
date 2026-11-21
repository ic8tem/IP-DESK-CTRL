# Servizio di Windows

Con l'installatore (come amministratore) e l'opzione **Accesso completo**, attiva di serie, si installa il servizio **«IP-DESK-CTRL accesso completo»**.

## Cosa permette

Durante i collegamenti accettati il visore vede e comanda anche:

- le **richieste UAC** («Vuoi consentire a questa app di apportare modifiche…»);
- la **schermata di blocco** e quella di accesso;
- i **programmi avviati come amministratore**;
- **Ctrl+Alt+Canc** dalla plancia.

Senza il servizio queste schermate restano **nere** e mouse e tastiera vengono **bloccati** da Windows.

## Richieste UAC nella plancia

Quando sul PC remoto si apre una richiesta UAC, nella finestra del visore compare un avviso: con il servizio «la vedi sullo schermo, rispondi Sì o No»; senza il servizio «non si vede da qui, deve rispondere chi è al PC», con il pulsante per installarlo.

## Installarlo a distanza

Se il PC remoto ha IP-DESK-CTRL installato con l'installatore (in Programmi) ma senza il servizio, da **Tasti ▾ › Installa il servizio sul PC remoto (accesso completo)…** (o dal pulsante nell'avviso UAC) glielo chiedi: sul PC remoto Windows mostra la richiesta UAC e chi è davanti deve rispondere **Sì**, una volta sola. Poi IP-DESK-CTRL si riavvia lì, passa la sua chiave al servizio (il numero resta lo stesso) e il collegamento riprende da solo, con l'accesso completo.

Non funziona con la versione portatile né con l'installazione per un solo utente: il servizio gira come sistema e non deve mai avviare un programma che un utente normale potrebbe sostituire. Lì serve reinstallare con l'installatore come amministratore.

## Avvia come amministratore

Con il servizio attivo, **Tasti ▾ › Avvia come amministratore** apre sul PC remoto, già da amministratore e **senza la richiesta UAC**: Prompt dei comandi, PowerShell, Gestione attività, Editor del Registro di sistema, Servizi, Gestione computer, Gestione dispositivi. Nient'altro: niente percorsi a scelta.

È **spento di serie**: lo accende chi è al PC remoto in Impostazioni › «Chi comanda questo PC può avviare programmi come amministratore senza la richiesta UAC». Salta una protezione di Windows, quindi va acceso solo per chi si conosce. Funziona solo se l'utente collegato a Windows è amministratore: un utente normale non ha diritti da amministratore da prestare. Ogni avvio finisce nel registro.

## PC raggiungibile appena acceso

Dalla versione 0.4 il servizio tiene anche **la rete**: il PC è raggiungibile **appena acceso, prima dell'accesso a Windows**, con lo stesso numero di sempre (al primo avvio il programma passa la sua chiave al servizio, che da lì in poi la custodisce e firma per suo conto).

- Se **nessuno ha fatto l'accesso** entrano solo i numeri con [accesso senza conferma](Accesso-senza-conferma) (con la password, se impostata); gli altri ricevono «nessuno al PC per accettare».
- Con un utente collegato la richiesta compare nel suo programma come sempre.

Lo storico è condiviso in `dati\storico.json` nella cartella del programma (`C:\Program Files\IP-DESK-CTRL\dati`); il servizio scrive nel registro unico `dati\sessioni.log` e tiene la sua chiave in `dati\servizio`, leggibile solo dal sistema e dagli amministratori. Se un PC con una versione più nuova vuole collegarsi, il servizio scarica e installa l'aggiornamento da solo, anche prima dell'accesso.

## Controllare che sia attivo

Apri **Servizi** (`services.msc`) e cerca «IP-DESK-CTRL accesso completo»: deve essere **In esecuzione** con avvio **Automatico**. Se manca, reinstalla il programma lasciando spuntato **Accesso completo**, oppure installalo a distanza (vedi sopra).

Il servizio accetta richieste solo dallo stesso eseguibile di IP-DESK-CTRL installato, e si toglie disinstallando il programma.

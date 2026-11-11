# Servizio di Windows

Con l'installatore (come amministratore) e l'opzione **Accesso completo**, attiva di serie, si installa il servizio **«IP-DESK-CTRL accesso completo»**.

## Cosa permette

Durante i collegamenti accettati il visore vede e comanda anche:

- le **richieste UAC** («Vuoi consentire a questa app di apportare modifiche…»);
- la **schermata di blocco** e quella di accesso;
- i **programmi avviati come amministratore**;
- **Ctrl+Alt+Canc** dalla plancia.

Senza il servizio queste schermate restano **nere** e mouse e tastiera vengono **bloccati** da Windows.

## PC raggiungibile appena acceso

Dalla versione 0.4 il servizio tiene anche **la rete**: il PC è raggiungibile **appena acceso, prima dell'accesso a Windows**, con lo stesso numero di sempre (al primo avvio il programma passa la sua chiave al servizio, che da lì in poi la custodisce e firma per suo conto).

- Se **nessuno ha fatto l'accesso** entrano solo i numeri con [accesso senza conferma](Accesso-senza-conferma) (con la password, se impostata); gli altri ricevono «nessuno al PC per accettare».
- Con un utente collegato la richiesta compare nel suo programma come sempre.

Lo storico è condiviso nella cartella `%ProgramData%\IP-DESK-CTRL`, dove il servizio scrive anche i suoi registri.

## Controllare che sia attivo

Apri **Servizi** (`services.msc`) e cerca «IP-DESK-CTRL accesso completo»: deve essere **In esecuzione** con avvio **Automatico**. Se manca, reinstalla il programma lasciando spuntato **Accesso completo**.

Il servizio accetta richieste solo dallo stesso eseguibile di IP-DESK-CTRL installato, e si toglie disinstallando il programma.

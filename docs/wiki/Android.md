# Android

Lo stesso programma per telefoni e tablet con **Android 8.0** o successivo: file `IP-DESK-CTRL-…-android.apk` nella pagina [Releases](https://github.com/ic8tem/IP-DESK-CTRL/releases/latest). Consenti l'installazione da origini sconosciute quando Android lo chiede.

Il telefono ha il suo numero di 10 cifre ed è raggiungibile **mentre l'app è aperta**.

## Il telefono come visore

Il dito fa da **touchpad** (sposta il puntatore di quanto si muove) oppure, a scelta, **tocco diretto** (si clicca dove si tocca). Tutto si regola con il pulsante **✋** della plancia o da Impostazioni › **Tocco e gesti…**.

### Gesti (profilo Sofatap, quello di serie)

| Gesto | Effetto |
|---|---|
| Tocco | Clic |
| Doppio tocco nella metà sinistra | Doppio clic |
| Doppio tocco nella metà destra | Clic destro |
| Tocco con due dita | Clic destro |
| Tocco con tre dita | Apre o chiude la tastiera |
| Pressione lunga | **Pannello scorciatoie** |
| Tocca e poi tieni (o trascina) | Trascinamento |
| Un dito che scorre | Muove il puntatore |
| Due dita su e giù | Rotella |
| Due dita che si allargano o stringono | **Zoom** dell'immagine e spostamento |
| Striscia lungo il bordo destro | Scorrimento (dito in giù = pagina in giù) |

Altri profili pronti: **Abitudini da touchpad** (due dita = clic destro, pressione = trascina), **Pressione per il clic destro** (due dita = Invio, tre dita = tastiera), **Come la 0.4**. Ogni gesto si può cambiare: clic, clic destro, clic centrale, doppio clic, trascina, Invio, Esc, tastiera, dettatura, pannello scorciatoie o niente. Si regolano anche la **durata della pressione lunga**, l'**intervallo del doppio tocco**, la **velocità del puntatore** (0,25–3×), lo scorrimento naturale, la striscia e lo zoom.

### Pannello scorciatoie

Tieni il dito: compare il pannello con Spazio, Invio, Esc, ⌫, frecce, Tab, Copia, Incolla, Annulla, Seleziona tutto, Alt+Tab e volume. **Senza staccare il dito** scorri a destra o a sinistra per scegliere e **solleva** per mandare il tasto. Scorri in su o in giù per annullare. Se sollevi senza scorrere il pannello resta aperto e scegli toccando.

### Tastiera, barra dei tasti e dettatura

Il pulsante **Tastiera** apre la tastiera del telefono con la **barra dei tasti speciali**: scegli quali tasti avere (Ctrl, Alt, Win, Maiusc, Esc, Tab, frecce, ⌫, Canc, Invio, Spazio, Home, Fine, Pag↑, Pag↓, Ins, Stamp, F1–F12, Copia, Incolla, Taglia, Annulla, Seleziona tutto, Alt+Tab, Alt+F4, volume, 🎤 Detta). Frecce, Canc, ⌫, Invio, Tab, Pag↑/Pag↓ e volume **si ripetono tenendoli premuti**. Il testo arriva così com'è, qualunque sia la tastiera del PC.

**🎤 Detta** (nella barra, o assegnata a un gesto) apre il riconoscimento vocale del telefono e manda al PC il testo dettato.

### Mouse e tastiera collegati al telefono

Un mouse o una tastiera **Bluetooth o USB** collegati al telefono o al tablet (anche con **Samsung DeX**) vanno direttamente al PC remoto: il puntatore va dove punti, la rotella scorre, il tasto destro fa clic destro (non chiude più il collegamento).

### Microfono

Il pulsante **Microfono** della plancia manda la tua voce al PC remoto (la prima volta Android chiede il permesso). Vedi [Plancia di comando](Plancia-di-comando#microfono).

**Inquadra QR** legge il QR del numero di un PC con un'app lettore QR compatibile (ZXing «Barcode Scanner» e simili). Se non ce n'è, scrivi il numero a mano.

## Il telefono come host (comandato dal PC)

1. A ogni collegamento Android chiede il consenso alla **cattura dello schermo**.
2. Per tocchi e testo attiva **una volta** il servizio in **Impostazioni › Accessibilità › IP-DESK-CTRL**.

Dal PC:

- i clic diventano **tocchi**, il trascinamento diventa **scorrimento**;
- tasto destro ed **Esc** sono «**Indietro**», il tasto **Win** è «**Home**»;
- l'audio del telefono (Android 10 e successivi) arriva al PC.

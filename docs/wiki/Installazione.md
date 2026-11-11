# Installazione

Tutti i file sono nella pagina **[Releases](https://github.com/ic8tem/IP-DESK-CTRL/releases/latest)**. Al posto di `…` c'è il numero di versione.

> Tutti i dispositivi che si collegano tra loro devono avere la **stessa versione principale** (per esempio tutti 0.4.x). Vedi [Storia delle versioni](Storia-delle-versioni).

## Windows

| Sistema | File |
|---|---|
| Windows 64 bit | `IP-DESK-CTRL-…-win-x64-setup.exe` |
| Windows 32 bit | `IP-DESK-CTRL-…-win-x86-setup.exe` |

1. Avvia l'installatore e accetta la richiesta di Windows (serve l'amministratore).
2. Lascia attiva l'opzione **Accesso completo**: installa il [servizio di Windows](Servizio-di-Windows), che permette di vedere e comandare le finestre UAC, la schermata di blocco e i programmi da amministratore, e rende il PC raggiungibile appena acceso.
3. Se vuoi, scegli anche **l'avvio automatico all'accesso**: il programma parte nascosto, con la sola icona vicino all'orologio, e il PC è subito raggiungibile.

Esiste anche la versione **portatile** (`…-portatile.zip`), che non si installa: in quel caso il servizio non c'è, quindi UAC e schermata di blocco restano nere e il PC è raggiungibile solo con il programma aperto.

Windows SmartScreen può avvisare che il programma non è riconosciuto: clic su **Ulteriori informazioni › Esegui comunque**.

## Linux e Raspberry Pi

| Sistema | File |
|---|---|
| Linux 64 bit (PC) | `ip-desk-ctrl_…_amd64.deb` |
| Raspberry Pi OS 64 bit | `ip-desk-ctrl_…_arm64.deb` |
| Raspberry Pi OS 32 bit | `ip-desk-ctrl_…_armhf.deb` |

```
sudo apt install ./ip-desk-ctrl_…deb
```

- Con **X11** funziona tutto.
- Con **Wayland** (Raspberry Pi OS Bookworm, Sway…) lo schermo si cattura con `grim` (pochi fotogrammi al secondo) e mouse e tastiera passano da `/dev/uinput`: il pacchetto installa la regola necessaria, ma **devi riavviare** dopo l'installazione.
- GNOME e KDE su Wayland non permettono la cattura: al login scegli la sessione **Xorg**. Sul Raspberry puoi passare a X11 con `sudo raspi-config` › Advanced Options › Wayland › X11.
- L'audio passa da PulseAudio o PipeWire (`parec`/`pacat`).

Per ogni architettura c'è anche la versione portatile `…-portatile.tar.gz`.

## macOS

| Mac | File |
|---|---|
| Apple Silicon (M1, M2…) | `IP-DESK-CTRL-…-osx-arm64.dmg` |
| Intel | `IP-DESK-CTRL-…-osx-x64.dmg` |

1. Apri il `.dmg` e trascina IP-DESK-CTRL in Applicazioni.
2. Al primo avvio: **clic destro › Apri** (il programma non è firmato da Apple).
3. Al primo collegamento in arrivo concedi **Registrazione schermo** e **Accessibilità** in Impostazioni di Sistema › Privacy e sicurezza, poi riavvia il programma.
4. Per mandare l'audio del Mac installa **BlackHole 2ch** e crea in Configurazione MIDI Audio un *dispositivo a uscita multipla* (casse + BlackHole).

## Android

File `IP-DESK-CTRL-…-android.apk`, per Android 8.0 o successivo. Consenti l'installazione da origini sconosciute. Dettagli nella pagina [Android](Android).

## Aggiornamenti

Quando esce una nuova versione, la schermata principale mostra un avviso con il link per scaricarla (il controllo avviene all'avvio e ogni 12 ore). Per aggiornare basta installare sopra la versione precedente: numero, storico e impostazioni restano.

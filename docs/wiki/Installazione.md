# Installazione

Tutti i file sono nella pagina **[Releases](https://github.com/ic8tem/IP-DESK-CTRL/releases/latest)**. Al posto di `…` c'è il numero di versione.

> Tutti i dispositivi che si collegano tra loro devono avere la **stessa versione principale** (per esempio tutti 0.4.x). Vedi [Storia delle versioni](Storia-delle-versioni).

## Windows

| Sistema | File |
|---|---|
| Windows 64 bit | `IP-DESK-CTRL-…-win-x64-setup.exe` |
| Windows 32 bit | `IP-DESK-CTRL-…-win-x86-setup.exe` |
| Windows ARM64 (PC con Snapdragon) | `IP-DESK-CTRL-…-win-arm64-setup.exe` |

1. Avvia l'installatore e accetta la richiesta di Windows (serve l'amministratore).
2. Lascia attiva l'opzione **Accesso completo**: installa il [servizio di Windows](Servizio-di-Windows), che permette di vedere e comandare le finestre UAC, la schermata di blocco e i programmi da amministratore, e rende il PC raggiungibile appena acceso.
3. Se vuoi, scegli anche **l'avvio automatico all'accesso**: il programma parte nascosto, con la sola icona vicino all'orologio, e il PC è subito raggiungibile.

Esiste anche la versione **portatile** (`…-portatile.zip`), che non si installa: in quel caso il servizio non c'è, quindi UAC e schermata di blocco restano nere e il PC è raggiungibile solo con il programma aperto.

Windows SmartScreen può avvisare che il programma non è riconosciuto: clic su **Ulteriori informazioni › Esegui comunque**.

## Linux e Raspberry Pi

| Sistema | File |
|---|---|
| Distribuzione | File | Installazione |
|---|---|---|
| Debian / Ubuntu / Mint (PC) | `ip-desk-ctrl_…_amd64.deb` | `sudo apt install ./ip-desk-ctrl_…deb` |
| Raspberry Pi OS 64 bit (arm64) | `ip-desk-ctrl_…_arm64.deb` | come sopra |
| Raspberry Pi OS 32 bit (armhf) | `ip-desk-ctrl_…_armhf.deb` | come sopra |
| Fedora (x86_64, aarch64, armv7hl) | `ip-desk-ctrl-…-1.x86_64.rpm` · `…aarch64.rpm` · `…armv7hl.rpm` | `sudo dnf install ./ip-desk-ctrl-…rpm` |
| openSUSE | gli stessi `.rpm` | `sudo zypper install --allow-unsigned-rpm ./ip-desk-ctrl-…rpm` |
| Slackware ARM64 (anche Raspberry Pi) | `ip-desk-ctrl-…-aarch64-1_ic8tem.txz` | `installpkg ip-desk-ctrl-…txz` (da root) |
| Slackware64 | `ip-desk-ctrl-…-x86_64-1_ic8tem.txz` | come sopra |
| Linux x86_64, qualsiasi distribuzione | `IP-DESK-CTRL-…-linux-x64-portatile.tar.gz` | si scompatta e si avvia `IP-DESK-CTRL` |
| Linux aarch64 (PC e mini-PC non Raspberry) | `IP-DESK-CTRL-…-linux-arm64-portatile.tar.gz` | come sopra |
| Linux armv7l | `IP-DESK-CTRL-…-linux-arm-portatile.tar.gz` | come sopra |
| Arch / Manjaro / EndeavourOS / Arch Linux ARM | `ip-desk-ctrl-…-1-x86_64.pkg.tar.zst` · `…-aarch64.pkg.tar.zst` | `sudo pacman -U ./ip-desk-ctrl-…pkg.tar.zst` |
| Qualsiasi Linux (AppImage) | `IP-DESK-CTRL-…-x86_64.AppImage` · `…-aarch64.AppImage` | `chmod +x` sul file e avvialo; si aggiorna da solo sostituendo il file |
| Snap | `ip-desk-ctrl_…_amd64.snap` · `…_arm64.snap` | `sudo snap install --dangerous --classic ./ip-desk-ctrl_…snap` |
| Alpine Linux (musl), x64 e arm64 | `IP-DESK-CTRL-…-linux-musl-x64-portatile.tar.gz` · `…-linux-musl-arm64-portatile.tar.gz` | `sudo apk add libstdc++ fontconfig libx11 libxtst libxrandr`, poi si scompatta e si avvia |

### Chromebook

Sui Chromebook con Google Play installa l'**APK Android** (per comandare altri PC dal Chromebook). Se hai attivato l'ambiente Linux del Chromebook, puoi usare anche il **`.deb`** (`amd64` o `arm64` secondo il processore): `sudo apt install ./ip-desk-ctrl_…deb`.

apt installa da solo anche ffmpeg (video compresso), xrandr (risoluzione adatta) e gio (cartella condivisa); dnf e zypper li propongono come consigliati (su Fedora ffmpeg si chiama `ffmpeg-free`). Su Slackware e con le versioni portatili installali tu, se mancano.

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

Con **Homebrew** (sceglie da solo la versione giusta per il tuo Mac):

```
brew tap ic8tem/ip-desk-ctrl https://github.com/ic8tem/IP-DESK-CTRL
brew install --cask ip-desk-ctrl
```

1. Apri il `.dmg` e trascina IP-DESK-CTRL in Applicazioni.
2. Al primo avvio: **clic destro › Apri** (il programma non è firmato da Apple).
3. Al primo collegamento in arrivo concedi **Registrazione schermo** e **Accessibilità** in Impostazioni di Sistema › Privacy e sicurezza, poi riavvia il programma.
4. Per mandare l'audio del Mac installa **BlackHole 2ch** e crea in Configurazione MIDI Audio un *dispositivo a uscita multipla* (casse + BlackHole).

## Android

File `IP-DESK-CTRL-…-android.apk`, per Android 8.0 o successivo. Consenti l'installazione da origini sconosciute. Dettagli nella pagina [Android](Android).

## Aggiornamenti

Il programma controlla all'avvio e poi **ogni 30 minuti** se c'è una versione nuova. Se c'è, in alto a destra, accanto a «In linea», compare il pulsante **Nuova versione x.y.z**, che apre il riquadro con **Aggiorna ora**.

Con **Aggiornamenti automatici** (Impostazioni, attivo di serie) il riquadro si apre da solo e l'aggiornamento parte dopo 60 secondi; se ci sono collegamenti aperti aspetta che si chiudano, così non interrompe nessuno; **Più tardi** lo ferma per quella versione, e resta il pulsante in alto a destra. Su Android si apre solo l'avviso: l'APK va installato a mano.

L'aggiornamento:

1. scarica il file giusto per il tuo sistema e controlla che sia identico a quello pubblicato (impronta SHA-256 di GitHub);
2. avvisa gli altri PC collegati;
3. lo installa, si chiude e **si riapre da solo**;
4. i **collegamenti aperti riprendono**: il PC comandato lascia rientrare senza conferma, per 5 minuti, chi era collegato; il visore si ricollega appena l'altro torna.

| Sistema | Cosa succede |
|---|---|
| Windows con il servizio | Installazione senza domande, anche se il PC è comandato da remoto |
| Windows senza servizio | Windows chiede il permesso di amministratore (UAC): risponde chi è al PC |
| Windows portatile | Sostituisce i file nella cartella del programma |
| Linux (.deb, .rpm, Slackware, Arch, Snap) | Chiede la password di amministratore |
| AppImage | Sostituisce il file .AppImage con quello nuovo |
| Linux portatile | Sostituisce i file nella cartella del programma |
| macOS | Sostituisce l'app (se serve, chiede la password) |
| Android | Apre il browser per scaricare l'APK, da installare sopra |

Si può sempre aggiornare anche a mano installando sopra la versione precedente: numero, storico e impostazioni restano.

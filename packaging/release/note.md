macOS: al primo avvio clic destro › Apri. Android: consenti l'installazione da origini sconosciute; per farti comandare dal PC attiva il servizio in Impostazioni › Accessibilità › IP-DESK-CTRL.

Linux e Raspberry: `sudo apt install ./ip-desk-ctrl_…deb` · Fedora `sudo dnf install ./ip-desk-ctrl-…rpm` · openSUSE `sudo zypper install --allow-unsigned-rpm ./ip-desk-ctrl-…rpm` · Slackware `installpkg ip-desk-ctrl-…txz` · Arch `sudo pacman -U ./ip-desk-ctrl-…pkg.tar.zst` · AppImage: rendilo eseguibile e avvialo · Snap `sudo snap install --dangerous --classic ./ip-desk-ctrl_…snap` · Alpine: `apk add libstdc++ fontconfig libx11`, poi scompatta e avvia (con la sessione Wayland riavvia dopo l'installazione: serve la regola per mouse e tastiera).
macOS con Homebrew: `brew tap ic8tem/ip-desk-ctrl https://github.com/ic8tem/IP-DESK-CTRL` e poi `brew install --cask ip-desk-ctrl`.
macOS: concedi «Registrazione schermo» e «Accessibilità» in Impostazioni di Sistema › Privacy e sicurezza; per l'audio installa BlackHole 2ch.
Le versioni *portatile* non richiedono installazione.

**Aggiornamenti automatici** per tutte le distribuzioni (controllo ogni 30 minuti, installazione quando nessuno è collegato), tranne Android e Chromebook (solo avviso, l'APK si installa a mano). I pacchetti .deb, .rpm, Arch e Slackware si aggiornano senza password; lo Snap chiede la password di amministratore.
Windows con Scoop: `scoop bucket add ip-desk-ctrl https://github.com/ic8tem/IP-DESK-CTRL` e poi `scoop install ip-desk-ctrl`. Chocolatey: il pacchetto `ip-desk-ctrl.…nupkg` è allegato qui (`choco install ip-desk-ctrl -s .`). Flatpak: `flatpak install --user ./IP-DESK-CTRL-…flatpak`.
Lingue: 36, in Impostazioni › «Lingua».
Video compresso H.264/H.265/AV1: serve ffmpeg su tutti e due i PC (Windows: Impostazioni › Scarica ffmpeg; Linux: sudo apt install ffmpeg o sudo dnf install ffmpeg-free; Mac: brew install ffmpeg).
La X della finestra nasconde soltanto: IP-DESK-CTRL resta attivo con l'icona vicino all'orologio; per uscire clic destro sull'icona › «Esci da IP-DESK-CTRL».

**Compatibilità**: la 0.5 si collega con la 0.4 (protocollo 3); microfono, volume, risoluzione adatta e cartella condivisa servono la 0.5 da entrambe le parti.

Guida: https://github.com/ic8tem/IP-DESK-CTRL/wiki · Novità di ogni versione: https://github.com/ic8tem/IP-DESK-CTRL/wiki/Storia-delle-versioni

IP-DESK-CTRL è gratuito e senza pubblicità: se ti è utile puoi sostenerlo su [Ko-fi](https://ko-fi.com/ic8tem) ♥

# Video compresso (H.264, H.265, AV1)

Dalla 0.5 lo schermo del PC remoto può viaggiare come **video compresso** invece che a riquadri JPEG: a parità di qualità consuma molta meno banda ed è molto più fluido con video, scorrimenti e finestre che si muovono.

## Cosa serve

**ffmpeg** su tutti e due i PC (il visore e quello comandato):

| Sistema | Come averlo |
|---|---|
| Windows 64 bit | Impostazioni › **Scarica ffmpeg** (circa 150 MB, una volta sola) |
| Linux, Raspberry Pi | `sudo apt install ffmpeg` |
| macOS | `brew install ffmpeg` |

Senza ffmpeg da una delle due parti si usano i riquadri JPEG come prima. Su Android, per ora, sempre riquadri.

## Codec e scheda video

Il PC comandato prova all'inizio i codificatori disponibili e usa, in quest'ordine:

1. **AV1** o **H.265** con la scheda video (NVIDIA NVENC, Intel Quick Sync, AMD AMF, Media Foundation di Windows, VideoToolbox del Mac);
2. **H.264** con la scheda video;
3. **H.264** software (x264, preimpostazione ultraveloce a bassa latenza).

Nella plancia, il menu **Video automatico** permette di forzare **H.264**, **H.265**, **AV1** o i **Riquadri JPEG**. Le statistiche in alto mostrano il codec e il codificatore in uso.

## Qualità

| Qualità | Velocità del video |
|---|---|
| Rete locale (bassa latenza) | 20 Mbit/s, 60 fotogrammi al secondo |
| Alta | 12 Mbit/s |
| Automatica | 6 Mbit/s (10 Mbit/s e 50 fotogrammi al secondo se il collegamento è diretto e il ping sta sotto i 12 ms) |
| Media | 4 Mbit/s |
| Bassa | 1,5 Mbit/s a metà risoluzione |

Le altre qualità vanno a 25 fotogrammi al secondo.

## Immagine adattata alla finestra

Con lo zoom su **Adatta**, il PC remoto non manda più pixel di quelli che la finestra di chi guarda può mostrare: uno schermo 4K visto in una finestra da 1280×720 viaggia a 1280×720. Si risparmia banda e lavoro su entrambi i PC, senza perdere niente a video. Con uno zoom fisso (100 %, 150 %…) o ingrandendo con le dita sul telefono torna l'immagine a risoluzione piena. Funziona anche con chi ha solo la visione, perché non cambia la risoluzione del PC remoto (quella la cambia il pulsante **Risoluzione adatta**). Serve la versione 0.5.32 su entrambi i PC.

Se ffmpeg si ferma, il programma torna da solo ai riquadri e lo dice.

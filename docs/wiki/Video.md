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
| Alta | 12 Mbit/s |
| Automatica | 6 Mbit/s |
| Media | 4 Mbit/s |
| Bassa | 1,5 Mbit/s a metà risoluzione |

Se ffmpeg si ferma, il programma torna da solo ai riquadri e lo dice.

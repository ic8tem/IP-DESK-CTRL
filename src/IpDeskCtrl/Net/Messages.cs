using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace IpDeskCtrl.Net
{
    /// <summary>Tipi di messaggio (primo byte del testo in chiaro di ogni frame cifrato).</summary>
    public static class Msg
    {
        // ---- sessione
        public const byte Hello = 0x01;          // str versione | str sistema operativo | str nome del PC | bool IP-TRX-CTRL installato | str funzioni (0.5+, vedi AppVersion.Caps)
        public const byte Accept = 0x02;         // u8 stato (AcceptState) | str motivo
        public const byte Bye = 0x03;            // str motivo
        public const byte Ping = 0x04;           // i64 ticks
        public const byte Pong = 0x05;           // i64 ticks
        public const byte AuthChallenge = 0x06;  // host -> visore: i32 n | sale | i32 iterazioni | i32 n | nonce | str avviso
        public const byte AuthResponse = 0x07;   // visore -> host: i32 n | chiave PBKDF2 nel canale cifrato (0 byte = annulla: decide chi è al PC)

        // ---- schermo (host -> visore, salvo dove indicato)
        public const byte Monitors = 0x10;       // i32 n | n × (str nome | i32 x y w h | bool principale) | i32 scelto (-1 = tutti)
        public const byte SelectMonitor = 0x11;  // visore -> host: i32 indice (-1 = tutti)
        public const byte SetQuality = 0x12;     // visore -> host: str qualità
        public const byte SetAudio = 0x13;       // visore -> host: bool
        public const byte Refresh = 0x14;        // visore -> host: rimanda tutta l'immagine
        public const byte SetMic = 0x15;         // visore -> host: bool (0.5+: il visore manda il suo microfono)
        public const byte Resize = 0x16;         // visore -> host: i32 larghezza | i32 altezza in pixel (0, 0 = risoluzione originale) (0.5+)
        public const byte FrameStart = 0x20;     // i32 id | i32 larghezza | i32 altezza | bool completo
        public const byte Tile = 0x21;           // i32 id | i32 x y w h | u8 formato | i32 n | n byte
        public const byte FrameEnd = 0x22;       // i32 id | i32 ms di cattura+codifica
        public const byte FrameAck = 0x23;       // visore -> host: i32 id
        public const byte Cursor = 0x24;         // i32 x | i32 y | bool visibile (coordinate dell'immagine)
        public const byte ScreenNote = 0x25;     // str avviso dell'host (permessi mancanti, Wayland…)

        // ---- ingresso (visore -> host)
        public const byte MouseMove = 0x30;      // i32 x | i32 y
        public const byte MouseButton = 0x31;    // u8 tasto | bool giù | i32 x | i32 y
        public const byte Wheel = 0x32;          // i32 dx | i32 dy (120 = uno scatto)
        public const byte Key = 0x33;            // u16 codice evdev | bool giù
        public const byte ReleaseAll = 0x34;     // rilascia tasti e pulsanti rimasti premuti
        public const byte Combo = 0x35;          // str combinazione speciale (es. «ctrl-alt-del»)

        // ---- audio e UDP
        public const byte Audio = 0x40;          // u32 seq | ADPCM (quando UDP non passa); dal visore all'host è il microfono (0.5+)
        public const byte UdpOffer = 0x50;       // str candidati "ip:porta;…" | i32 passo NAT simmetrico
        public const byte UdpOn = 0x51;          // u8 1 = ricevo su UDP

        // ---- testo digitato (tastiera del telefono): caratteri Unicode, non tasti
        public const byte Text = 0x36;           // str testo

        // ---- appunti e file (nei due versi)
        public const byte Clipboard = 0x60;      // str testo degli appunti
        public const byte FileOffer = 0x61;      // i32 id | u8 scopo | i32 n | n × (str percorso relativo | i64 byte)
        public const byte FileChunk = 0x62;      // i32 id | i32 indice | i32 n | n byte
        public const byte FileDone = 0x63;       // i32 id
        public const byte FileFetch = 0x64;      // chiede all'altro PC i file che ha negli appunti
        public const byte FilesAvailable = 0x65; // i32 numero | i64 byte | str primo nome (file negli appunti dell'altro PC)
        public const byte FileCancel = 0x66;     // i32 id | str motivo

        // ---- comandi del PC (visore -> host)
        public const byte Reboot = 0x70;         // riavvia il PC remoto (serve il permesso di comandarlo)
        public const byte StartTrx = 0x71;       // avvia IP-TRX-CTRL sul PC remoto
        public const byte Volume = 0x72;         // u8 0 = giù, 1 = su, 2 = muto sì/no (0.5+)

        // ---- cartella condivisa: una cartella del visore come disco del PC remoto (0.5+)
        public const byte DriveOpen = 0x80;      // visore -> host: str nome della cartella | bool sola lettura
        public const byte DriveClose = 0x81;     // nei due versi: str motivo
        public const byte DriveReq = 0x82;       // host -> visore: i32 id | u8 operazione | str percorso | str secondo percorso | i64 posizione | i32 n | n byte
        public const byte DriveResp = 0x83;      // visore -> host: i32 id | i32 stato (0 = ok, altrimenti codice HTTP) | i32 n | n byte
        public const byte DriveState = 0x84;     // host -> visore: bool montato | str dove (lettera, percorso) o motivo
    }

    public enum AcceptState : byte { Waiting = 0, Accepted = 1, Rejected = 2, Busy = 3, Disabled = 4 }

    public sealed class MsgWriter
    {
        private readonly MemoryStream _ms = new();
        private readonly BinaryWriter _w;
        public MsgWriter(byte type) { _w = new BinaryWriter(_ms, Encoding.UTF8); _w.Write(type); }
        public MsgWriter I32(int v) { _w.Write(v); return this; }
        public MsgWriter U32(uint v) { _w.Write(v); return this; }
        public MsgWriter I64(long v) { _w.Write(v); return this; }
        public MsgWriter F32(float v) { _w.Write(v); return this; }
        public MsgWriter U8(byte v) { _w.Write(v); return this; }
        public MsgWriter U16(ushort v) { _w.Write(v); return this; }
        public MsgWriter Bool(bool v) { _w.Write((byte)(v ? 1 : 0)); return this; }
        public MsgWriter Str(string? v) { _w.Write(v ?? ""); return this; }
        public MsgWriter Bytes(byte[] v) { _w.Write(v); return this; }
        public MsgWriter Bytes(byte[] v, int offset, int count) { _w.Write(v, offset, count); return this; }
        public byte[] Build() { _w.Flush(); return _ms.ToArray(); }
    }

    public sealed class MsgReader
    {
        private readonly BinaryReader _r;
        public byte Type { get; }
        public MsgReader(byte[] data)
        {
            _r = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
            Type = _r.ReadByte();
        }
        public bool AtEnd => _r.BaseStream.Position >= _r.BaseStream.Length;
        public int I32() => _r.ReadInt32();
        public uint U32() => _r.ReadUInt32();
        public long I64() => _r.ReadInt64();
        public float F32() => _r.ReadSingle();
        public byte U8() => _r.ReadByte();
        public ushort U16() => _r.ReadUInt16();
        public bool Bool() => _r.ReadByte() == 1;
        public string Str() => _r.ReadString();
        public byte[] Bytes(int count) => _r.ReadBytes(count);
        public byte[] Rest() => _r.ReadBytes((int)(_r.BaseStream.Length - _r.BaseStream.Position));
    }
}

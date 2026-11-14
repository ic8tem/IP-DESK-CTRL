using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IpDeskCtrl.Net;

namespace IpDeskCtrl.Core
{
    /// <summary>Un file da mandare: percorso relativo (con le sottocartelle), dimensione e come aprirlo.</summary>
    public sealed record TransferItem(string RelPath, long Size, Func<Task<Stream>> Open);

    /// <summary>Scopo di un invio di file.</summary>
    public enum TransferPurpose : byte
    {
        Drop = 0,        // trascinati sulla finestra del visore: di là si incollano dove sono stati lasciati
        Paste = 1,       // copiati negli appunti di qua e mandati con il pulsante della plancia
        Fetch = 2,       // chiesti dall'altro PC (i file che ho negli appunti)
        PasteKeys = 3    // Ctrl+V nella finestra del visore con file negli appunti di qua: di là si incollano subito
    }

    /// <summary>Avanzamento di un trasferimento, per l'interfaccia.</summary>
    public sealed record TransferProgress(int Id, bool Outgoing, int Files, long Done, long Total, bool Finished, string Message);

    /// <summary>
    /// Appunti e file tra i due PC.
    ///   • Testo degli appunti: chi lo cambia lo manda all'altro, che lo mette nei suoi appunti (nei due versi).
    ///   • File: viaggiano a pezzi da 256 kB sul canale cifrato; chi li riceve li salva in Scaricati/IP-DESK-CTRL
    ///     (stessi nomi e sottocartelle, senza sovrascrivere) e li mette negli appunti come file, pronti per
    ///     Ctrl+V nella cartella o nel programma che si vuole.
    /// </summary>
    public abstract partial class SessionBase
    {
        private const int Chunk = 256 * 1024;
        private string? _lastLocalClip, _lastRemoteClip;
        private int _nextTransferId;
        private readonly ConcurrentDictionary<int, Incoming> _incoming = new();
        private readonly ConcurrentDictionary<int, CancellationTokenSource> _outgoing = new();

        public event Action<TransferProgress>? TransferChanged;

        /// <summary>Gli appunti e i file sono permessi in questa sessione (l'host li permette solo con mouse e tastiera).</summary>
        protected virtual bool FilesAllowed => true;

        /// <summary>File ricevuti e messi negli appunti (o solo salvati).</summary>
        protected virtual void OnFilesReceived(TransferPurpose purpose, string folder, List<string> topLevel, bool onClipboard) { }

        private sealed class Incoming
        {
            public TransferPurpose Purpose;
            public List<(string rel, long size)> Files = new();
            public string Folder = "";
            public List<string> TopLevel = new();
            public Dictionary<string, string> Renamed = new();   // primo livello originale → nome libero
            public int Index = -1;
            public FileStream? Stream;
            public long Done, Total;
            public DateTime LastReport;
        }

        protected void Report(TransferProgress p) => TransferChanged?.Invoke(p);

        // ================================================================== testo degli appunti

        /// <summary>Ogni secondo guarda gli appunti di questo PC; se il testo è cambiato lo manda all'altro.</summary>
        protected async Task ClipboardLoopAsync()
        {
            var clip = Platform.Clipboard;
            if (clip == null) return;
            try { _lastLocalClip = await clip.GetTextAsync(); } catch { }
            while (!Cts.IsCancellationRequested)
            {
                try { await Task.Delay(1000, Cts.Token); } catch { break; }
                if (!FilesAllowed) continue;
                try
                {
                    string? t = await clip.GetTextAsync();
                    if (t == null || t == _lastLocalClip) continue;
                    _lastLocalClip = t;
                    if (t == _lastRemoteClip || t.Length == 0 || t.Length > 1_000_000) continue;
                    await SendAsync(new MsgWriter(Msg.Clipboard).Str(t).Build());
                }
                catch { }
            }
        }

        private void OnRemoteClipboard(string text)
        {
            var clip = Platform.Clipboard;
            if (clip == null || !FilesAllowed) return;
            _lastRemoteClip = text;
            _lastLocalClip = text;
            _ = Task.Run(async () => { try { await clip.SetTextAsync(text); } catch { } });
        }

        // ================================================================== invio

        /// <summary>File e cartelle locali → elenco di file (le cartelle diventano percorsi relativi).</summary>
        public static List<TransferItem> FromPaths(IEnumerable<string> paths)
        {
            var list = new List<TransferItem>();
            foreach (string p in paths)
            {
                try
                {
                    if (File.Exists(p))
                    {
                        string fp = p;
                        list.Add(new TransferItem(Path.GetFileName(p), new FileInfo(p).Length, () => Task.FromResult<Stream>(File.OpenRead(fp))));
                    }
                    else if (Directory.Exists(p))
                    {
                        string root = Path.GetDirectoryName(Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? "";
                        foreach (string f in Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories))
                        {
                            string ff = f;
                            string rel = Path.GetRelativePath(root, f).Replace('\\', '/');
                            list.Add(new TransferItem(rel, new FileInfo(f).Length, () => Task.FromResult<Stream>(File.OpenRead(ff))));
                        }
                    }
                }
                catch (Exception ex) { SessionLog.Write($"FILE {p} non leggibile: {ex.Message}"); }
            }
            return list;
        }

        public async Task SendFilesAsync(List<TransferItem> items, TransferPurpose purpose)
        {
            if (items.Count == 0 || IsClosed) return;
            int id = Interlocked.Increment(ref _nextTransferId);
            var cts = CancellationTokenSource.CreateLinkedTokenSource(Cts.Token);
            _outgoing[id] = cts;
            long total = items.Sum(i => i.Size), done = 0;
            try
            {
                var w = new MsgWriter(Msg.FileOffer).I32(id).U8((byte)purpose).I32(items.Count);
                foreach (var i in items) w.Str(i.RelPath).I64(i.Size);
                await Link.SendAsync(w.Build(), cts.Token);
                SessionLog.Write($"FILE invio {items.Count} file ({total / 1024} kB) a {Identity.Format(PeerNumber)}");
                var buf = new byte[Chunk];
                var last = DateTime.MinValue;
                for (int k = 0; k < items.Count; k++)
                {
                    await using var s = await items[k].Open();
                    while (true)
                    {
                        int n = await s.ReadAsync(buf, cts.Token);
                        if (n <= 0) break;
                        await Link.SendAsync(new MsgWriter(Msg.FileChunk).I32(id).I32(k).I32(n).Bytes(buf, 0, n).Build(), cts.Token);
                        done += n;
                        if (DateTime.UtcNow - last > TimeSpan.FromMilliseconds(300))
                        {
                            last = DateTime.UtcNow;
                            Report(new TransferProgress(id, true, items.Count, done, total, false, L.F("Invio {0}", items[k].RelPath)));
                        }
                    }
                }
                await Link.SendAsync(new MsgWriter(Msg.FileDone).I32(id).Build(), cts.Token);
                Report(new TransferProgress(id, true, items.Count, total, total, true, items.Count == 1 ? L.F("Inviato {0}", items[0].RelPath) : L.F("Inviati {0} file", items.Count)));
            }
            catch (Exception ex)
            {
                if (!IsClosed) { try { await SendAsync(new MsgWriter(Msg.FileCancel).I32(id).Str(ex.Message).Build()); } catch { } }
                Report(new TransferProgress(id, true, items.Count, done, total, true, L.T("Invio interrotto: ") + ex.Message));
            }
            finally
            {
                _outgoing.TryRemove(id, out _);
                cts.Dispose();
            }
        }

        public void CancelTransfers()
        {
            foreach (var c in _outgoing.Values) c.Cancel();
            foreach (var id in _incoming.Keys.ToList()) DropIncoming(id, "annullato");
        }

        // ================================================================== ricezione

        private async Task OnFileMessageAsync(MsgReader r)
        {
            switch (r.Type)
            {
                case Msg.FileOffer:
                {
                    int id = r.I32();
                    var purpose = (TransferPurpose)r.U8();
                    int n = Math.Clamp(r.I32(), 0, 100_000);
                    var inc = new Incoming { Purpose = purpose };
                    for (int i = 0; i < n; i++) inc.Files.Add((Safe(r.Str()), r.I64()));
                    if (!FilesAllowed)
                    {
                        await SendAsync(new MsgWriter(Msg.FileCancel).I32(id).Str(L.T("su quel PC i file non sono permessi (solo visione)")).Build());
                        return;
                    }
                    inc.Total = inc.Files.Sum(f => f.size);
                    inc.Folder = Platform.Received;
                    _incoming[id] = inc;
                    SessionLog.Write($"FILE ricevo {n} file ({inc.Total / 1024} kB) da {Identity.Format(PeerNumber)} in {inc.Folder}");
                    Report(new TransferProgress(id, false, n, 0, inc.Total, false, L.T("Ricevo file…")));
                    if (n == 0) { await FinishAsync(id, inc); }
                    break;
                }
                case Msg.FileChunk:
                {
                    int id = r.I32(), idx = r.I32();
                    byte[] data = r.Bytes(r.I32());
                    if (!_incoming.TryGetValue(id, out var inc)) return;
                    try
                    {
                        if (idx != inc.Index)
                        {
                            inc.Stream?.Dispose();
                            inc.Index = idx;
                            string path = Target(inc, inc.Files[idx].rel);
                            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                            inc.Stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, true);
                        }
                        await inc.Stream!.WriteAsync(data);
                        inc.Done += data.Length;
                        if (DateTime.UtcNow - inc.LastReport > TimeSpan.FromMilliseconds(300))
                        {
                            inc.LastReport = DateTime.UtcNow;
                            Report(new TransferProgress(id, false, inc.Files.Count, inc.Done, inc.Total, false, L.T("Ricevo ") + inc.Files[idx].rel));
                        }
                    }
                    catch (Exception ex)
                    {
                        DropIncoming(id, ex.Message);
                        await SendAsync(new MsgWriter(Msg.FileCancel).I32(id).Str(L.T("errore di scrittura: ") + ex.Message).Build());
                    }
                    break;
                }
                case Msg.FileDone:
                {
                    int id = r.I32();
                    if (_incoming.TryGetValue(id, out var inc)) await FinishAsync(id, inc);
                    break;
                }
                case Msg.FileCancel:
                {
                    int id = r.I32();
                    string why = r.Str();
                    if (_outgoing.TryGetValue(id, out var c)) c.Cancel();
                    DropIncoming(id, why);
                    Report(new TransferProgress(id, false, 0, 0, 0, true, L.T("Trasferimento annullato: ") + why));
                    break;
                }
            }
        }

        private async Task FinishAsync(int id, Incoming inc)
        {
            _incoming.TryRemove(id, out _);
            inc.Stream?.Dispose();
            inc.Stream = null;
            // file vuoti (nessun pezzo ricevuto): vanno creati lo stesso
            for (int i = 0; i < inc.Files.Count; i++)
            {
                if (inc.Files[i].size != 0) continue;
                try
                {
                    string p = Target(inc, inc.Files[i].rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                    if (!File.Exists(p)) File.Create(p).Dispose();
                }
                catch { }
            }
            var top = inc.TopLevel.Distinct().Select(t => Path.Combine(inc.Folder, t)).ToList();
            bool onClip = false;
            var clip = Platform.Clipboard;
            if (clip != null && top.Count > 0)
            {
                try { onClip = await clip.SetFilesAsync(top); } catch { }
            }
            SessionLog.Write($"FILE ricevuti {inc.Files.Count} file in {inc.Folder}" + (onClip ? " (negli appunti)" : ""));
            Report(new TransferProgress(id, false, inc.Files.Count, inc.Total, inc.Total, true,
                (inc.Files.Count == 1 ? L.T("Ricevuto 1 file") : L.F("Ricevuti {0} file", inc.Files.Count)) + L.F(" in {0}", inc.Folder) + (onClip ? L.T(" · negli appunti: Ctrl+V per incollarli") : "")));
            OnFilesReceived(inc.Purpose, inc.Folder, top, onClip);
        }

        /// <summary>Percorso di arrivo: il primo livello (file o cartella) prende un nome libero, il resto segue.</summary>
        private static string Target(Incoming inc, string rel)
        {
            var parts = rel.Split('/', StringSplitOptions.RemoveEmptyEntries);
            string first = parts[0];
            if (!inc.Renamed.TryGetValue(first, out var free))
            {
                free = first;
                string stem = Path.GetFileNameWithoutExtension(first), ext = parts.Length == 1 ? Path.GetExtension(first) : "";
                if (parts.Length > 1) stem = first;
                for (int k = 2; File.Exists(Path.Combine(inc.Folder, free)) || Directory.Exists(Path.Combine(inc.Folder, free)); k++)
                    free = $"{stem} ({k}){ext}";
                inc.Renamed[first] = free;
                inc.TopLevel.Add(free);
            }
            parts[0] = free;
            return Path.Combine(new[] { inc.Folder }.Concat(parts).ToArray());
        }

        /// <summary>Nessun «..», nessun percorso assoluto, niente caratteri vietati: il file resta nella cartella di arrivo.</summary>
        private static string Safe(string rel)
        {
            var bad = Path.GetInvalidFileNameChars().Concat(new[] { ':', '*', '?', '"', '<', '>', '|' }).ToHashSet();
            var parts = rel.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => new string(p.Select(c => bad.Contains(c) || c < 32 ? '_' : c).ToArray()).Trim())
                .Where(p => p.Length > 0 && p != "." && p != "..")
                .ToList();
            return parts.Count == 0 ? "file" : string.Join('/', parts);
        }

        private void DropIncoming(int id, string why)
        {
            if (!_incoming.TryRemove(id, out var inc)) return;
            try { inc.Stream?.Dispose(); } catch { }
            SessionLog.Write($"FILE ricezione interrotta: {why}");
        }

        private void AbortTransfers()
        {
            foreach (var c in _outgoing.Values) { try { c.Cancel(); } catch { } }
            foreach (var id in _incoming.Keys.ToList()) DropIncoming(id, L.T("collegamento chiuso"));
        }
    }
}

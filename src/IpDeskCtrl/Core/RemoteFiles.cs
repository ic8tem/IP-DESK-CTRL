using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IpDeskCtrl.Net;

namespace IpDeskCtrl.Core
{
    /// <summary>
    /// Gestione file a due pannelli: il visore sfoglia i dischi del PC comandato e trasferisce file e cartelle nei
    /// due versi (<see cref="Msg.FsReq"/> → <see cref="Msg.FsResp"/>, stesso formato della cartella condivisa).
    /// I percorsi sono assoluti; «» è la radice: su Windows l'elenco dei dischi, altrove «/».
    /// Il PC comandato legge e scrive come l'utente collegato, mai come sistema (con il servizio di Windows si
    /// impersona l'utente della console: <see cref="Platform.AsUser"/>), e solo se chi comanda ha mouse e tastiera.
    /// </summary>
    public static class FsOp
    {
        /// <summary>Cartelle di partenza: home, Desktop, Documenti, Scaricati (elenco di voci con il percorso intero).</summary>
        public const byte Places = 20;
    }

    public static class RemoteFiles
    {
        public static (int status, byte[] data) Handle(byte op, string path, string path2, long pos, byte[] data)
        {
            Func<(int, byte[])> work = () => Do(op, path, path2, pos, data);
            try { return Platform.AsUser != null ? Platform.AsUser(work) : work(); }
            catch (Exception ex) { SessionLog.Write("FILE gestione file: " + ex.Message); return (DriveOp.Error, Array.Empty<byte>()); }
        }

        private static bool IsRoot(string p) => p.Length == 0;

        private static (int, byte[]) Do(byte op, string path, string path2, long pos, byte[] data)
        {
            try
            {
                if (op == FsOp.Places) return (DriveOp.Ok, Places());
                if (op == DriveOp.List && IsRoot(path)) return (DriveOp.Ok, Roots());
                if (IsRoot(path) || !Path.IsPathRooted(path)) return (DriveOp.Forbidden, Array.Empty<byte>());
                string p = Path.GetFullPath(path);
                switch (op)
                {
                    case DriveOp.Stat:
                        if (Directory.Exists(p)) return (DriveOp.Ok, Entry(new DirectoryInfo(p), null));
                        if (File.Exists(p)) return (DriveOp.Ok, Entry(new FileInfo(p), null));
                        return (DriveOp.NotFound, Array.Empty<byte>());
                    case DriveOp.List:
                    {
                        var di = new DirectoryInfo(p);
                        if (!di.Exists) return (DriveOp.NotFound, Array.Empty<byte>());
                        var items = new List<FileSystemInfo>();
                        try { items.AddRange(di.EnumerateFileSystemInfos()); } catch (UnauthorizedAccessException) { return (DriveOp.Forbidden, Array.Empty<byte>()); }
                        var w = new MsgWriter(0).I32(0);
                        int n = 0;
                        var body = new MsgWriter(0);
                        foreach (var i in items)
                        {
                            try
                            {
                                if ((i.Attributes & FileAttributes.System) != 0 && (i.Attributes & FileAttributes.Hidden) != 0) continue;
                                bool dir = i is DirectoryInfo;
                                body.Str(i.Name).Bool(dir).I64(dir ? 0 : ((FileInfo)i).Length).I64(i.LastWriteTimeUtc.Ticks);
                                n++;
                            }
                            catch { }
                        }
                        var head = new MsgWriter(0).I32(n).Build()[1..];
                        return (DriveOp.Ok, head.Concat(body.Build()[1..]).ToArray());
                    }
                    case DriveOp.Read:
                    {
                        using var f = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        f.Position = pos;
                        var buf = new byte[DriveOp.Chunk];
                        int got = 0;
                        while (got < buf.Length) { int r = f.Read(buf, got, buf.Length - got); if (r <= 0) break; got += r; }
                        return (DriveOp.Ok, got == buf.Length ? buf : buf[..got]);
                    }
                    case DriveOp.Create:
                    {
                        if (Directory.Exists(p)) return (DriveOp.Conflict, Array.Empty<byte>());
                        using (new FileStream(p, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { }
                        return (DriveOp.Ok, Array.Empty<byte>());
                    }
                    case DriveOp.Write:
                    {
                        using var f = new FileStream(p, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
                        f.Position = pos;
                        f.Write(data, 0, data.Length);
                        return (DriveOp.Ok, Array.Empty<byte>());
                    }
                    case DriveOp.MkDir:
                        if (Directory.Exists(p)) return (DriveOp.Ok, Array.Empty<byte>());
                        if (File.Exists(p)) return (DriveOp.Exists, Array.Empty<byte>());
                        Directory.CreateDirectory(p);
                        return (DriveOp.Ok, Array.Empty<byte>());
                    case DriveOp.Delete:
                        if (Path.GetPathRoot(p) == p) return (DriveOp.Forbidden, Array.Empty<byte>());
                        if (Directory.Exists(p)) Directory.Delete(p, true);
                        else if (File.Exists(p)) File.Delete(p);
                        else return (DriveOp.NotFound, Array.Empty<byte>());
                        return (DriveOp.Ok, Array.Empty<byte>());
                    case DriveOp.Move:
                    {
                        if (!Path.IsPathRooted(path2)) return (DriveOp.Forbidden, Array.Empty<byte>());
                        string d = Path.GetFullPath(path2);
                        if (Directory.Exists(d) || File.Exists(d)) return (DriveOp.Exists, Array.Empty<byte>());
                        if (Directory.Exists(p)) Directory.Move(p, d); else File.Move(p, d);
                        return (DriveOp.Ok, Array.Empty<byte>());
                    }
                }
                return (DriveOp.Error, Array.Empty<byte>());
            }
            catch (UnauthorizedAccessException) { return (DriveOp.Forbidden, Array.Empty<byte>()); }
            catch (FileNotFoundException) { return (DriveOp.NotFound, Array.Empty<byte>()); }
            catch (DirectoryNotFoundException) { return (DriveOp.NotFound, Array.Empty<byte>()); }
            catch (IOException) { return (DriveOp.Conflict, Array.Empty<byte>()); }
        }

        /// <summary>Radice: i dischi pronti (Windows) oppure «/».</summary>
        private static byte[] Roots()
        {
            var body = new MsgWriter(0);
            int n = 0;
            if (OperatingSystem.IsWindows())
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    try { if (!d.IsReady) continue; } catch { continue; }
                    body.Str(d.Name.TrimEnd('\\') + "\\").Bool(true).I64(0).I64(0);
                    n++;
                }
            }
            else { body.Str("/").Bool(true).I64(0).I64(0); n++; }
            return new MsgWriter(0).I32(n).Build()[1..].Concat(body.Build()[1..]).ToArray();
        }

        /// <summary>Cartelle di partenza dell'utente: nome e percorso intero.</summary>
        private static byte[] Places()
        {
            var list = new List<(string, string)>();
            string home = Platform.UserHome?.Invoke() ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            void Add(string name, string p) { if (p.Length > 0 && Directory.Exists(p) && list.All(x => x.Item2 != p)) list.Add((name, p)); }
            Add("Home", home);
            foreach (var (n, sub) in new[] { ("Desktop", "Desktop"), ("Documenti", "Documents"), ("Scaricati", "Downloads"), ("Immagini", "Pictures") })
                Add(n, Path.Combine(home, sub));
            var w = new MsgWriter(0).I32(list.Count);
            foreach (var (n, p) in list) w.Str(n).Str(p);
            return w.Build()[1..];
        }

        private static byte[] Entry(FileSystemInfo i, string? name)
        {
            bool dir = i is DirectoryInfo;
            return new MsgWriter(0).Str(name ?? i.Name).Bool(dir).I64(dir ? 0 : ((FileInfo)i).Length).I64(i.LastWriteTimeUtc.Ticks).Build()[1..];
        }
    }
}

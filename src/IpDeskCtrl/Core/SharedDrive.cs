using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using IpDeskCtrl.Net;

namespace IpDeskCtrl.Core
{
    /// <summary>
    /// Cartella condivisa: una cartella del visore compare sul PC comandato come un disco.
    ///
    /// Sul PC comandato gira un piccolo server WebDAV solo su 127.0.0.1 (porta casuale, percorso segreto); il
    /// sistema lo monta come disco con i suoi strumenti (Windows: «net use», servizio WebClient; Linux: gio/GVFS;
    /// macOS: Finder). Ogni richiesta del server diventa un messaggio cifrato verso il visore
    /// (<see cref="Msg.DriveReq"/>), che legge o scrive nella cartella scelta e risponde (<see cref="Msg.DriveResp"/>).
    /// I file non passano mai in chiaro in rete e nessun altro PC può raggiungere il server.
    /// </summary>
    public static class DriveOp
    {
        public const byte Stat = 1, List = 2, Read = 3, Write = 4, Create = 5, MkDir = 6, Delete = 7, Move = 8, Copy = 9;
        public const int Ok = 0, NotFound = 404, Forbidden = 403, Conflict = 409, Exists = 412, Error = 500;
        public const int Chunk = 512 * 1024;
    }

    public sealed record DriveEntry(string Name, bool IsDir, long Size, DateTime Modified);

    // ===================================================================== visore: la cartella vera

    /// <summary>Lato visore: esegue le richieste del PC comandato dentro la cartella condivisa (mai fuori).</summary>
    public sealed class DriveShare
    {
        public string Root { get; }
        public bool ReadOnly { get; }
        public string Name => Path.GetFileName(Root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { Length: > 0 } n ? n : "Cartella";

        public DriveShare(string root, bool readOnly)
        {
            Root = Path.GetFullPath(root);
            ReadOnly = readOnly;
        }

        /// <summary>Percorso relativo («a/b.txt») → percorso vero; null se esce dalla cartella.</summary>
        private string? Map(string rel)
        {
            var parts = rel.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Any(p => p == ".." || p == "." || p.Contains(':') || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)) return null;
            string full = Path.GetFullPath(Path.Combine(new[] { Root }.Concat(parts).ToArray()));
            string root = Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full == Root || full.StartsWith(root, StringComparison.Ordinal) ? full : null;
        }

        public (int status, byte[] data) Handle(byte op, string path, string path2, long pos, byte[] data)
        {
            try
            {
                string? p = Map(path);
                if (p == null) return (DriveOp.Forbidden, Array.Empty<byte>());
                bool write = op is DriveOp.Write or DriveOp.Create or DriveOp.MkDir or DriveOp.Delete or DriveOp.Move or DriveOp.Copy;
                if (write && ReadOnly) return (DriveOp.Forbidden, Array.Empty<byte>());
                switch (op)
                {
                    case DriveOp.Stat:
                    {
                        if (Directory.Exists(p)) return (DriveOp.Ok, Entry(new DirectoryInfo(p), path.Length == 0 ? Name : null));
                        if (File.Exists(p)) return (DriveOp.Ok, Entry(new FileInfo(p), null));
                        return (DriveOp.NotFound, Array.Empty<byte>());
                    }
                    case DriveOp.List:
                    {
                        if (!Directory.Exists(p)) return (DriveOp.NotFound, Array.Empty<byte>());
                        var w = new MsgWriter(0);
                        var items = new DirectoryInfo(p).EnumerateFileSystemInfos().Take(5000).ToList();
                        w.I32(items.Count);
                        foreach (var i in items)
                        {
                            bool dir = i is DirectoryInfo;
                            w.Str(i.Name).Bool(dir).I64(dir ? 0 : ((FileInfo)i).Length).I64(i.LastWriteTimeUtc.Ticks);
                        }
                        return (DriveOp.Ok, w.Build()[1..]);
                    }
                    case DriveOp.Read:
                    {
                        if (!File.Exists(p)) return (DriveOp.NotFound, Array.Empty<byte>());
                        using var f = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        if (pos >= f.Length) return (DriveOp.Ok, Array.Empty<byte>());
                        f.Position = pos;
                        int n = (int)Math.Min(DriveOp.Chunk, f.Length - pos);
                        var buf = new byte[n];
                        int got = 0;
                        while (got < n) { int r = f.Read(buf, got, n - got); if (r <= 0) break; got += r; }
                        return (DriveOp.Ok, got == n ? buf : buf[..got]);
                    }
                    case DriveOp.Create:
                    {
                        if (Directory.Exists(p)) return (DriveOp.Conflict, Array.Empty<byte>());
                        string? dir = Path.GetDirectoryName(p);
                        if (dir == null || !Directory.Exists(dir)) return (DriveOp.Conflict, Array.Empty<byte>());
                        bool existed = File.Exists(p);
                        using (new FileStream(p, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { }
                        return (DriveOp.Ok, new[] { (byte)(existed ? 1 : 0) });
                    }
                    case DriveOp.Write:
                    {
                        using var f = new FileStream(p, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
                        f.Position = pos;
                        f.Write(data, 0, data.Length);
                        return (DriveOp.Ok, Array.Empty<byte>());
                    }
                    case DriveOp.MkDir:
                    {
                        if (Directory.Exists(p) || File.Exists(p)) return (DriveOp.Exists, Array.Empty<byte>());
                        string? parent = Path.GetDirectoryName(p);
                        if (parent == null || !Directory.Exists(parent)) return (DriveOp.Conflict, Array.Empty<byte>());
                        Directory.CreateDirectory(p);
                        return (DriveOp.Ok, Array.Empty<byte>());
                    }
                    case DriveOp.Delete:
                    {
                        if (p == Root) return (DriveOp.Forbidden, Array.Empty<byte>());
                        if (Directory.Exists(p)) Directory.Delete(p, true);
                        else if (File.Exists(p)) File.Delete(p);
                        else return (DriveOp.NotFound, Array.Empty<byte>());
                        return (DriveOp.Ok, Array.Empty<byte>());
                    }
                    case DriveOp.Move:
                    case DriveOp.Copy:
                    {
                        string? d = Map(path2);
                        if (d == null || p == Root || d == Root) return (DriveOp.Forbidden, Array.Empty<byte>());
                        bool overwrite = pos != 0;
                        bool srcDir = Directory.Exists(p);
                        if (!srcDir && !File.Exists(p)) return (DriveOp.NotFound, Array.Empty<byte>());
                        bool existed = Directory.Exists(d) || File.Exists(d);
                        if (existed && !overwrite) return (DriveOp.Exists, Array.Empty<byte>());
                        if (existed) { if (Directory.Exists(d)) Directory.Delete(d, true); else File.Delete(d); }
                        if (op == DriveOp.Move)
                        {
                            if (srcDir) Directory.Move(p, d); else File.Move(p, d);
                        }
                        else if (srcDir) CopyDir(p, d);
                        else File.Copy(p, d);
                        return (DriveOp.Ok, new[] { (byte)(existed ? 1 : 0) });
                    }
                }
                return (DriveOp.Error, Array.Empty<byte>());
            }
            catch (UnauthorizedAccessException) { return (DriveOp.Forbidden, Array.Empty<byte>()); }
            catch (FileNotFoundException) { return (DriveOp.NotFound, Array.Empty<byte>()); }
            catch (DirectoryNotFoundException) { return (DriveOp.Conflict, Array.Empty<byte>()); }
            catch (IOException) { return (DriveOp.Conflict, Array.Empty<byte>()); }
            catch (Exception ex) { ErrorLog.Write("Cartella condivisa: " + ex.Message); return (DriveOp.Error, Array.Empty<byte>()); }
        }

        private static void CopyDir(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
            foreach (var d in Directory.GetDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
        }

        private static byte[] Entry(FileSystemInfo i, string? name)
        {
            bool dir = i is DirectoryInfo;
            return new MsgWriter(0).Str(name ?? i.Name).Bool(dir).I64(dir ? 0 : ((FileInfo)i).Length).I64(i.LastWriteTimeUtc.Ticks).Build()[1..];
        }

        public static DriveEntry ReadEntry(MsgReader r) => new(r.Str(), r.Bool(), r.I64(), new DateTime(r.I64(), DateTimeKind.Utc));
    }

    // ===================================================================== PC comandato: richieste verso il visore

    /// <summary>Lato PC comandato: manda le richieste al visore e aspetta le risposte.</summary>
    public sealed class DriveClient
    {
        private readonly Func<byte[], Task> _send;
        private readonly ConcurrentDictionary<int, TaskCompletionSource<(int, byte[])>> _pending = new();
        private int _id;

        public DriveClient(Func<byte[], Task> send) { _send = send; }

        public async Task<(int status, byte[] data)> CallAsync(byte op, string path, string path2 = "", long pos = 0, byte[]? data = null)
        {
            int id = Interlocked.Increment(ref _id);
            var tcs = new TaskCompletionSource<(int, byte[])>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = tcs;
            try
            {
                data ??= Array.Empty<byte>();
                await _send(new MsgWriter(Msg.DriveReq).I32(id).U8(op).Str(path).Str(path2).I64(pos).I32(data.Length).Bytes(data).Build());
                var done = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(60)));
                return done == tcs.Task ? tcs.Task.Result : (DriveOp.Error, Array.Empty<byte>());
            }
            finally { _pending.TryRemove(id, out _); }
        }

        public void OnResponse(MsgReader r)
        {
            int id = r.I32();
            int status = r.I32();
            byte[] data = r.Bytes(r.I32());
            if (_pending.TryGetValue(id, out var t)) t.TrySetResult((status, data));
        }

        public void CancelAll()
        {
            foreach (var t in _pending.Values) t.TrySetResult((DriveOp.Error, Array.Empty<byte>()));
        }

        public async Task<DriveEntry?> StatAsync(string path)
        {
            var (s, d) = await CallAsync(DriveOp.Stat, path);
            return s == DriveOp.Ok ? DriveShare.ReadEntry(Reader(d)) : null;
        }

        public async Task<List<DriveEntry>?> ListAsync(string path)
        {
            var (s, d) = await CallAsync(DriveOp.List, path);
            if (s != DriveOp.Ok) return null;
            var r = Reader(d);
            int n = r.I32();
            var list = new List<DriveEntry>(n);
            for (int i = 0; i < n; i++) list.Add(DriveShare.ReadEntry(r));
            return list;
        }

        private static MsgReader Reader(byte[] d)
        {
            var b = new byte[d.Length + 1];
            Buffer.BlockCopy(d, 0, b, 1, d.Length);
            return new MsgReader(b);
        }
    }

    // ===================================================================== PC comandato: server WebDAV locale

    /// <summary>
    /// Server WebDAV minimo (classe 1 e 2: PROPFIND, GET, HEAD, PUT, DELETE, MKCOL, MOVE, COPY, LOCK finti, PROPPATCH)
    /// quanto basta al redirettore WebDAV di Windows, a GVFS e al Finder. Solo su 127.0.0.1.
    /// </summary>
    public sealed class DavServer : IDisposable
    {
        private readonly DriveClient _drive;
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        public string Token { get; }
        public int Port { get; }
        public bool ReadOnly { get; }

        public DavServer(DriveClient drive, string name, bool readOnly)
        {
            _drive = drive;
            ReadOnly = readOnly;
            string clean = new string(name.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').Take(24).ToArray());
            Token = (clean.Length > 0 ? clean : "Cartella") + "-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(AcceptLoop);
        }

        public string Url => $"http://127.0.0.1:{Port}/{Token}/";

        private async Task AcceptLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient c;
                try { c = await _listener.AcceptTcpClientAsync(_cts.Token); }
                catch { break; }
                _ = Task.Run(() => Serve(c));
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { }
        }

        // ------------------------------------------------------------------ HTTP

        private sealed class Request
        {
            public string Method = "", Path = "", Version = "HTTP/1.1";
            public Dictionary<string, string> H = new(StringComparer.OrdinalIgnoreCase);
            public string Header(string n) => H.TryGetValue(n, out var v) ? v : "";
        }

        /// <summary>Lettura con buffer: righe dell'intestazione e corpo.</summary>
        private sealed class Reader
        {
            private readonly Stream _s;
            private readonly byte[] _buf = new byte[64 * 1024];
            private int _pos, _len;
            public Reader(Stream s) { _s = s; }

            private async Task<bool> Fill()
            {
                if (_pos < _len) return true;
                _pos = 0;
                _len = await _s.ReadAsync(_buf.AsMemory(0, _buf.Length));
                return _len > 0;
            }

            public async Task<string?> Line()
            {
                var sb = new StringBuilder();
                while (true)
                {
                    if (!await Fill()) return sb.Length > 0 ? sb.ToString() : null;
                    byte b = _buf[_pos++];
                    if (b == '\n') return sb.ToString().TrimEnd('\r');
                    sb.Append((char)b);
                    if (sb.Length > 16384) throw new IOException("riga troppo lunga");
                }
            }

            public async Task<int> Read(byte[] into, int off, int count)
            {
                if (!await Fill()) return 0;
                int n = Math.Min(count, _len - _pos);
                Buffer.BlockCopy(_buf, _pos, into, off, n);
                _pos += n;
                return n;
            }

            public async Task<bool> ReadExactly(byte[] into, int count)
            {
                int got = 0;
                while (got < count) { int n = await Read(into, got, count - got); if (n <= 0) return false; got += n; }
                return true;
            }
        }

        private async Task Serve(TcpClient c)
        {
            using (c)
            {
                try
                {
                    c.NoDelay = true;
                    var ns = c.GetStream();
                    var rd = new Reader(ns);
                    while (!_cts.IsCancellationRequested)
                    {
                        string? first = await rd.Line();
                        if (first == null) return;
                        if (first.Length == 0) continue;
                        var parts = first.Split(' ');
                        if (parts.Length < 3) return;
                        var req = new Request { Method = parts[0].ToUpperInvariant(), Path = parts[1], Version = parts[2] };
                        string? line;
                        while (!string.IsNullOrEmpty(line = await rd.Line()))
                        {
                            int i = line.IndexOf(':');
                            if (i > 0) req.H[line[..i].Trim()] = line[(i + 1)..].Trim();
                        }
                        if (req.Header("Expect").Equals("100-continue", StringComparison.OrdinalIgnoreCase))
                            await Raw(ns, "HTTP/1.1 100 Continue\r\n\r\n");
                        bool keep = !req.Header("Connection").Equals("close", StringComparison.OrdinalIgnoreCase) && req.Version != "HTTP/1.0";
                        await Dispatch(req, rd, ns);
                        if (!keep) return;
                    }
                }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
                catch (Exception ex) { ErrorLog.Write("Cartella condivisa, server: " + ex.Message); }
            }
        }

        private static Task Raw(Stream s, string text) => s.WriteAsync(Encoding.ASCII.GetBytes(text)).AsTask();

        private static async Task Reply(Stream s, int code, string reason, byte[]? body = null, string? type = null, params (string, string)[] headers)
        {
            var sb = new StringBuilder();
            sb.Append($"HTTP/1.1 {code} {reason}\r\n");
            sb.Append("Server: IP-DESK-CTRL\r\n");
            sb.Append("Date: ").Append(DateTime.UtcNow.ToString("r", CultureInfo.InvariantCulture)).Append("\r\n");
            sb.Append("Content-Length: ").Append(body?.Length ?? 0).Append("\r\n");
            if (type != null) sb.Append("Content-Type: ").Append(type).Append("\r\n");
            foreach (var (k, v) in headers) sb.Append(k).Append(": ").Append(v).Append("\r\n");
            sb.Append("\r\n");
            await Raw(s, sb.ToString());
            if (body != null && body.Length > 0) await s.WriteAsync(body);
        }

        /// <summary>Corpo della richiesta (Content-Length o chunked), un blocco alla volta.</summary>
        private static async IAsyncEnumerable<byte[]> Body(Request req, Reader rd)
        {
            if (req.Header("Transfer-Encoding").Contains("chunked", StringComparison.OrdinalIgnoreCase))
            {
                while (true)
                {
                    string? size = await rd.Line();
                    if (size == null) yield break;
                    int semi = size.IndexOf(';');
                    long n = long.Parse((semi >= 0 ? size[..semi] : size).Trim(), NumberStyles.HexNumber);
                    if (n == 0) { while (!string.IsNullOrEmpty(await rd.Line())) { } yield break; }
                    while (n > 0)
                    {
                        var b = new byte[(int)Math.Min(n, DriveOp.Chunk)];
                        if (!await rd.ReadExactly(b, b.Length)) yield break;
                        n -= b.Length;
                        yield return b;
                    }
                    await rd.Line();
                }
            }
            long left = long.TryParse(req.Header("Content-Length"), out long cl) ? cl : 0;
            while (left > 0)
            {
                var b = new byte[(int)Math.Min(left, DriveOp.Chunk)];
                if (!await rd.ReadExactly(b, b.Length)) yield break;
                left -= b.Length;
                yield return b;
            }
        }

        private static async Task<byte[]> ReadAll(Request req, Reader rd)
        {
            using var ms = new MemoryStream();
            await foreach (var b in Body(req, rd)) { ms.Write(b); if (ms.Length > 1 << 20) break; }
            return ms.ToArray();
        }

        /// <summary>Percorso della richiesta → percorso relativo nella cartella; null = fuori dal disco condiviso.</summary>
        private string? Rel(string urlPath)
        {
            int q = urlPath.IndexOf('?');
            if (q >= 0) urlPath = urlPath[..q];
            if (urlPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || urlPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                int slash = urlPath.IndexOf('/', urlPath.IndexOf("//", StringComparison.Ordinal) + 2);
                urlPath = slash >= 0 ? urlPath[slash..] : "/";
            }
            string p = Uri.UnescapeDataString(urlPath).Replace('\\', '/');
            string prefix = "/" + Token;
            if (!p.Equals(prefix, StringComparison.OrdinalIgnoreCase) && !p.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)) return null;
            return p[prefix.Length..].Trim('/');
        }

        private string Href(string rel, bool dir)
        {
            var segs = rel.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString);
            string h = "/" + Token + (rel.Length > 0 ? "/" + string.Join("/", segs) : "");
            return dir ? h + "/" : h;
        }

        private async Task Dispatch(Request req, Reader rd, Stream ns)
        {
            string? rel = Rel(req.Path);
            if (req.Method == "OPTIONS")
            {
                await ReadAll(req, rd);
                await Reply(ns, 200, "OK", null, null, ("DAV", "1, 2"), ("MS-Author-Via", "DAV"),
                    ("Allow", "OPTIONS, GET, HEAD, PUT, DELETE, PROPFIND, PROPPATCH, MKCOL, MOVE, COPY, LOCK, UNLOCK"));
                return;
            }
            if (rel == null)
            {
                await ReadAll(req, rd);
                await Reply(ns, 404, "Not Found");
                return;
            }
            switch (req.Method)
            {
                case "PROPFIND": await PropFind(req, rd, ns, rel); break;
                case "GET":
                case "HEAD": await ReadAll(req, rd); await Get(req, ns, rel, req.Method == "HEAD"); break;
                case "PUT": await Put(req, rd, ns, rel); break;
                case "DELETE":
                {
                    await ReadAll(req, rd);
                    if (ReadOnly) { await Reply(ns, 403, "Forbidden"); break; }
                    var (s, _) = await _drive.CallAsync(DriveOp.Delete, rel);
                    await Status(ns, s, 204);
                    break;
                }
                case "MKCOL":
                {
                    await ReadAll(req, rd);
                    if (ReadOnly) { await Reply(ns, 403, "Forbidden"); break; }
                    var (s, _) = await _drive.CallAsync(DriveOp.MkDir, rel);
                    await Status(ns, s == DriveOp.Exists ? 405 : s, 201);
                    break;
                }
                case "MOVE":
                case "COPY":
                {
                    await ReadAll(req, rd);
                    if (ReadOnly) { await Reply(ns, 403, "Forbidden"); break; }
                    string? dest = Rel(req.Header("Destination"));
                    if (dest == null) { await Reply(ns, 502, "Bad Gateway"); break; }
                    bool overwrite = !req.Header("Overwrite").Equals("F", StringComparison.OrdinalIgnoreCase);
                    var (s, d) = await _drive.CallAsync(req.Method == "MOVE" ? DriveOp.Move : DriveOp.Copy, rel, dest, overwrite ? 1 : 0);
                    await Status(ns, s, d.Length > 0 && d[0] == 1 ? 204 : 201);
                    break;
                }
                case "LOCK": await Lock(req, rd, ns, rel); break;
                case "UNLOCK": await ReadAll(req, rd); await Reply(ns, 204, "No Content"); break;
                case "PROPPATCH": await PropPatch(req, rd, ns, rel); break;
                default: await ReadAll(req, rd); await Reply(ns, 405, "Method Not Allowed"); break;
            }
        }

        private static Task Status(Stream ns, int s, int okCode) => s switch
        {
            DriveOp.Ok => Reply(ns, okCode, okCode == 201 ? "Created" : okCode == 204 ? "No Content" : "OK"),
            DriveOp.NotFound => Reply(ns, 404, "Not Found"),
            DriveOp.Forbidden => Reply(ns, 403, "Forbidden"),
            DriveOp.Conflict => Reply(ns, 409, "Conflict"),
            DriveOp.Exists => Reply(ns, 412, "Precondition Failed"),
            405 => Reply(ns, 405, "Method Not Allowed"),
            _ => Reply(ns, 500, "Internal Server Error"),
        };

        // ------------------------------------------------------------------ PROPFIND

        private async Task PropFind(Request req, Reader rd, Stream ns, string rel)
        {
            await ReadAll(req, rd);
            var self = await _drive.StatAsync(rel);
            if (self == null) { await Reply(ns, 404, "Not Found"); return; }
            var list = new List<(string rel, DriveEntry e)> { (rel, self) };
            string depth = req.Header("Depth");
            if (self.IsDir && depth != "0")
            {
                var children = await _drive.ListAsync(rel) ?? new List<DriveEntry>();
                foreach (var c in children) list.Add(((rel.Length > 0 ? rel + "/" : "") + c.Name, c));
            }
            var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<D:multistatus xmlns:D=\"DAV:\">");
            foreach (var (r, e) in list)
            {
                string name = SecurityElement.Escape(r.Length == 0 ? e.Name : e.Name);
                sb.Append("<D:response><D:href>").Append(SecurityElement.Escape(Href(r, e.IsDir))).Append("</D:href><D:propstat><D:prop>");
                sb.Append("<D:displayname>").Append(name).Append("</D:displayname>");
                sb.Append(e.IsDir ? "<D:resourcetype><D:collection/></D:resourcetype>" : "<D:resourcetype/>");
                if (!e.IsDir)
                {
                    sb.Append("<D:getcontentlength>").Append(e.Size).Append("</D:getcontentlength>");
                    sb.Append("<D:getcontenttype>application/octet-stream</D:getcontenttype>");
                    sb.Append("<D:getetag>\"").Append(e.Modified.Ticks.ToString("x")).Append('-').Append(e.Size.ToString("x")).Append("\"</D:getetag>");
                }
                sb.Append("<D:getlastmodified>").Append(e.Modified.ToString("r", CultureInfo.InvariantCulture)).Append("</D:getlastmodified>");
                sb.Append("<D:creationdate>").Append(e.Modified.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)).Append("</D:creationdate>");
                sb.Append("<D:supportedlock><D:lockentry><D:lockscope><D:exclusive/></D:lockscope><D:locktype><D:write/></D:locktype></D:lockentry></D:supportedlock>");
                sb.Append("<D:lockdiscovery/>");
                sb.Append("</D:prop><D:status>HTTP/1.1 200 OK</D:status></D:propstat></D:response>");
            }
            sb.Append("</D:multistatus>");
            await Reply(ns, 207, "Multi-Status", Encoding.UTF8.GetBytes(sb.ToString()), "text/xml; charset=\"utf-8\"");
        }

        private async Task PropPatch(Request req, Reader rd, Stream ns, string rel)
        {
            byte[] body = await ReadAll(req, rd);
            // si accetta tutto (date e attributi di Windows) senza salvarlo: basta per non far fallire le copie
            var props = new StringBuilder();
            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(Encoding.UTF8.GetString(body));
                var nsm = new XmlNamespaceManager(doc.NameTable);
                nsm.AddNamespace("D", "DAV:");
                var nodes = doc.SelectNodes("//D:prop/*", nsm);
                if (nodes != null)
                    foreach (XmlNode n in nodes)
                        props.Append('<').Append(n.LocalName).Append(" xmlns=\"").Append(SecurityElement.Escape(n.NamespaceURI)).Append("\"/>");
            }
            catch { }
            string xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<D:multistatus xmlns:D=\"DAV:\"><D:response><D:href>" +
                         SecurityElement.Escape(Href(rel, false)) + "</D:href><D:propstat><D:prop>" + props +
                         "</D:prop><D:status>HTTP/1.1 200 OK</D:status></D:propstat></D:response></D:multistatus>";
            await Reply(ns, 207, "Multi-Status", Encoding.UTF8.GetBytes(xml), "text/xml; charset=\"utf-8\"");
        }

        private async Task Lock(Request req, Reader rd, Stream ns, string rel)
        {
            await ReadAll(req, rd);
            // blocchi finti (un solo utente sulla cartella): servono a Windows e al Finder per scrivere
            int code = 200;
            if (!ReadOnly && await _drive.StatAsync(rel) == null)
            {
                var (s, _) = await _drive.CallAsync(DriveOp.Create, rel);
                if (s != DriveOp.Ok) { await Status(ns, s, 201); return; }
                code = 201;
            }
            string token = "opaquelocktoken:" + Guid.NewGuid();
            string xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<D:prop xmlns:D=\"DAV:\"><D:lockdiscovery><D:activelock>" +
                         "<D:locktype><D:write/></D:locktype><D:lockscope><D:exclusive/></D:lockscope><D:depth>0</D:depth>" +
                         "<D:owner>IP-DESK-CTRL</D:owner><D:timeout>Second-3600</D:timeout><D:locktoken><D:href>" + token +
                         "</D:href></D:locktoken></D:activelock></D:lockdiscovery></D:prop>";
            await Reply(ns, code, code == 201 ? "Created" : "OK", Encoding.UTF8.GetBytes(xml), "text/xml; charset=\"utf-8\"", ("Lock-Token", "<" + token + ">"));
        }

        // ------------------------------------------------------------------ GET e PUT

        private async Task Get(Request req, Stream ns, string rel, bool head)
        {
            var e = await _drive.StatAsync(rel);
            if (e == null) { await Reply(ns, 404, "Not Found"); return; }
            if (e.IsDir) { await Reply(ns, 200, "OK", head ? null : Encoding.UTF8.GetBytes("IP-DESK-CTRL"), "text/plain"); return; }
            long from = 0, to = e.Size - 1;
            bool partial = false;
            string range = req.Header("Range");
            if (range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) && e.Size > 0)
            {
                var r = range[6..].Split(',')[0].Split('-');
                if (r.Length == 2)
                {
                    if (r[0].Length > 0) { from = long.Parse(r[0]); if (r[1].Length > 0) to = Math.Min(long.Parse(r[1]), e.Size - 1); }
                    else if (r[1].Length > 0) from = Math.Max(0, e.Size - long.Parse(r[1]));
                    partial = from <= to;
                }
            }
            long len = e.Size == 0 ? 0 : to - from + 1;
            var hdr = new StringBuilder();
            hdr.Append(partial ? "HTTP/1.1 206 Partial Content\r\n" : "HTTP/1.1 200 OK\r\n");
            hdr.Append("Content-Length: ").Append(len).Append("\r\n");
            hdr.Append("Content-Type: application/octet-stream\r\nAccept-Ranges: bytes\r\n");
            hdr.Append("Last-Modified: ").Append(e.Modified.ToString("r", CultureInfo.InvariantCulture)).Append("\r\n");
            if (partial) hdr.Append($"Content-Range: bytes {from}-{to}/{e.Size}\r\n");
            hdr.Append("\r\n");
            await Raw(ns, hdr.ToString());
            if (head) return;
            long pos = from;
            while (pos <= to && len > 0)
            {
                var (s, d) = await _drive.CallAsync(DriveOp.Read, rel, "", pos);
                if (s != DriveOp.Ok || d.Length == 0) throw new IOException("lettura interrotta");
                int n = (int)Math.Min(d.Length, to - pos + 1);
                await ns.WriteAsync(d.AsMemory(0, n));
                pos += n;
            }
        }

        private async Task Put(Request req, Reader rd, Stream ns, string rel)
        {
            if (ReadOnly)
            {
                await foreach (var _ in Body(req, rd)) { }
                await Reply(ns, 403, "Forbidden");
                return;
            }
            var (s, d) = await _drive.CallAsync(DriveOp.Create, rel);
            if (s != DriveOp.Ok)
            {
                await foreach (var _ in Body(req, rd)) { }
                await Status(ns, s, 201);
                return;
            }
            bool existed = d.Length > 0 && d[0] == 1;
            long pos = 0;
            int failed = DriveOp.Ok;
            await foreach (var b in Body(req, rd))
            {
                if (failed != DriveOp.Ok) continue;          // si legge comunque tutto il corpo
                var (ws, _) = await _drive.CallAsync(DriveOp.Write, rel, "", pos, b);
                if (ws != DriveOp.Ok) failed = ws;
                pos += b.Length;
            }
            await Status(ns, failed, existed ? 204 : 201);
        }
    }

    // ===================================================================== montaggio come disco

    /// <summary>Monta e smonta il server WebDAV locale con gli strumenti del sistema.</summary>
    public static class DriveMount
    {
        /// <summary>Monta; restituisce (true, dove) o (false, motivo).</summary>
        public static (bool ok, string where) Mount(DavServer dav)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    if (Environment.UserName.Equals("SYSTEM", StringComparison.OrdinalIgnoreCase) || Environment.UserName.EndsWith("$"))
                        return (false, "nessun utente collegato al PC remoto: la cartella si può montare solo con qualcuno connesso");
                    var used = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
                    char letter = "ZYXWVUTSRQPONMLKJIHGFE".FirstOrDefault(l => !used.Contains(l));
                    if (letter == default) return (false, "nessuna lettera di disco libera");
                    var (code, output) = Run("net", $"use {letter}: \"\\\\127.0.0.1@{dav.Port}\\{dav.Token}\" /persistent:no");
                    if (code != 0)
                        return (false, "Windows non ha montato la cartella (" + output.Trim() + "). Serve il servizio «WebClient» (Client WebDAV): avvialo da services.msc.");
                    try { Process.Start(new ProcessStartInfo("explorer.exe", letter + ":\\") { UseShellExecute = true }); } catch { }
                    return (true, letter + ":");
                }
                if (OperatingSystem.IsMacOS())
                {
                    var (code, output) = Run("osascript", $"-e \"mount volume \\\"{dav.Url}\\\"\"");
                    return code == 0 ? (true, "/Volumes/" + dav.Token) : (false, "il Finder non ha montato la cartella: " + output.Trim());
                }
                if (Screen.WaylandScreen.Which("gio"))
                {
                    string url = $"dav://127.0.0.1:{dav.Port}/{dav.Token}/";
                    var (code, output) = Run("gio", $"mount {url}");
                    if (code != 0) return (false, "gio non ha montato la cartella: " + output.Trim());
                    try { Process.Start(new ProcessStartInfo("xdg-open", url) { UseShellExecute = false }); } catch { }
                    return (true, url);
                }
                return (false, $"manca «gio» (GVFS) per montarla: aprila nel file manager con dav://127.0.0.1:{dav.Port}/{dav.Token}/");
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        public static void Unmount(DavServer dav, string where)
        {
            try
            {
                if (OperatingSystem.IsWindows()) Run("net", $"use {where} /delete /y");
                else if (OperatingSystem.IsMacOS()) Run("umount", $"\"/Volumes/{dav.Token}\"");
                else Run("gio", $"mount -u dav://127.0.0.1:{dav.Port}/{dav.Token}/");
            }
            catch { }
        }

        private static (int code, string output) Run(string exe, string args)
        {
            using var p = Process.Start(new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            })!;
            var o = p.StandardOutput.ReadToEndAsync();
            var e = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(30000)) { try { p.Kill(); } catch { } return (-1, "tempo scaduto"); }
            return (p.ExitCode, (o.Result + " " + e.Result).Trim());
        }
    }
}

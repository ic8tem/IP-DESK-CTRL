using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IpDeskCtrl.Screen;

namespace IpDeskCtrl.Core
{
    /// <summary>
    /// Windows: il programma dell'utente quando il servizio «IPDeskCtrl» è installato.
    /// Il servizio tiene identità, porta, MQTT e collegamenti in arrivo (il PC è raggiungibile anche prima
    /// dell'accesso); il programma resta collegato al servizio con una pipe e:
    ///   • mostra all'utente le richieste di collegamento (ASK → ANS);
    ///   • riceve l'inizio e la fine delle sessioni in arrivo (START, END) e può chiuderle (KICK) o
    ///     togliere mouse e tastiera (INPUT);
    ///   • manda al servizio le impostazioni «accetta», «mouse e tastiera», «audio» (SET).
    /// Righe di testo UTF-8, un comando per riga.
    /// </summary>
    public sealed class ServiceClient : IDisposable
    {
        private readonly NamedPipeClientStream _pipe;
        private readonly StreamReader _r;
        private readonly StreamWriter _w;
        private readonly object _wl = new();
        private readonly CancellationTokenSource _cts = new();

        /// <summary>Domanda all'utente; impostata dall'interfaccia.</summary>
        public Func<IncomingInfo, CancellationToken, Task<IncomingDecision>>? AskAccept;
        public event Action<string, bool>? HostStarted;      // numero, mouse e tastiera consentiti
        public event Action<string, string>? HostEnded;      // numero, motivo
        public event Action? Lost;

        private readonly ConcurrentDictionary<string, CancellationTokenSource> _asks = new();

        private ServiceClient(NamedPipeClientStream pipe)
        {
            _pipe = pipe;
            _r = new StreamReader(pipe, Encoding.UTF8);
            _w = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
        }

        /// <summary>
        /// Identità del servizio. Se il servizio non ha ancora una chiave, gli passa quella dell'utente (che
        /// così mantiene il proprio numero). null = servizio assente, vecchio o non disponibile.
        /// </summary>
        public static Identity? ServiceIdentity()
        {
            if (!OperatingSystem.IsWindows()) return null;
            try
            {
                string reply = WinHelper.AskService("IDENTITY");
                if (reply.StartsWith("OK ")) return Identity.FromPkcs8(Convert.FromHexString(reply[3..].Trim()));
                if (reply.Trim() != "NEED") return null;
                var mine = Identity.LoadOrCreate();
                string set = WinHelper.AskService("SETKEY " + Convert.ToHexString(mine.ExportPkcs8()), 5000);
                if (set.StartsWith("OK"))
                {
                    SessionLog.Write($"SERVIZIO chiave di questo utente passata al servizio: il numero {Identity.Format(mine.Number)} resta lo stesso");
                    return mine;
                }
                SessionLog.Write("SERVIZIO chiave non accettata: " + set);
            }
            catch (TimeoutException) { }
            catch (Exception ex) { SessionLog.Write("SERVIZIO non raggiungibile: " + ex.Message); }
            return null;
        }

        /// <summary>Cartella dati del servizio (storico condiviso, registri).</summary>
        public static string ServiceFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "IP-DESK-CTRL");

        /// <summary>Collegamento permanente al servizio; null se non riesce.</summary>
        public static ServiceClient? Attach()
        {
            try
            {
                var p = new NamedPipeClientStream(".", WinHelper.ServicePipe, PipeDirection.InOut, PipeOptions.Asynchronous);
                p.Connect(2000);
                var c = new ServiceClient(p);
                c.Write("ATTACH");
                string first = c._r.ReadLine() ?? "";
                if (!first.StartsWith("OK")) { p.Dispose(); SessionLog.Write("SERVIZIO collegamento rifiutato: " + first); return null; }
                _ = Task.Run(c.ReadLoopAsync);
                return c;
            }
            catch (Exception ex)
            {
                SessionLog.Write("SERVIZIO collegamento non riuscito: " + ex.Message);
                return null;
            }
        }

        private void Write(string line)
        {
            lock (_wl)
            {
                try { _w.WriteLine(line); } catch { }
            }
        }

        public void SendSettings(bool accept, bool input, bool audio) => Write($"SET {(accept ? 1 : 0)} {(input ? 1 : 0)} {(audio ? 1 : 0)}");
        public void Kick() => Write("KICK");
        public void SetInput(bool on) => Write("INPUT " + (on ? 1 : 0));
        public void Send(string line) => Write(line);

        private async Task ReadLoopAsync()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    string? line = await _r.ReadLineAsync(_cts.Token);
                    if (line == null) break;
                    string[] p = line.Split(' ', 3);
                    switch (p[0])
                    {
                        case "ASK" when p.Length == 3:
                            _ = Task.Run(() => AnswerAsync(p[1], p[2]));
                            break;
                        case "CANCEL" when p.Length >= 2:
                            if (_asks.TryRemove(p[1], out var c)) { try { c.Cancel(); } catch { } }
                            break;
                        case "START" when p.Length >= 3:
                            HostStarted?.Invoke(p[1], p[2] == "1");
                            break;
                        case "TRX":
                        {
                            // il servizio (sessione 0) non può aprire finestre: IP-TRX-CTRL lo avvia il programma dell'utente
                            string err = TrxCtrl.Launch();
                            Write("TRXDONE " + (err.Length == 0 ? "OK" : err));
                            break;
                        }
                        case "END" when p.Length >= 2:
                            HostEnded?.Invoke(p[1], p.Length == 3 ? p[2] : "");
                            break;
                    }
                }
            }
            catch { }
            if (!_cts.IsCancellationRequested) { SessionLog.Write("SERVIZIO collegamento con il servizio perso"); Lost?.Invoke(); }
        }

        private async Task AnswerAsync(string id, string json)
        {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            _asks[id] = cts;
            IncomingDecision d = new(false, false, false);
            try
            {
                var info = JsonSerializer.Deserialize<IncomingInfo>(Encoding.UTF8.GetString(Convert.FromBase64String(json)));
                var f = AskAccept;
                if (info != null && f != null) d = await f(info, cts.Token);
            }
            catch { }
            finally { _asks.TryRemove(id, out _); }
            Write($"ANS {id} {(d.Accept ? 1 : 0)} {(d.AllowInput ? 1 : 0)} {(d.AlwaysAllow ? 1 : 0)}");
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _pipe.Dispose(); } catch { }
        }
    }
}

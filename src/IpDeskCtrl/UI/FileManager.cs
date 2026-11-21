using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using IpDeskCtrl.Core;
using IpDeskCtrl.Net;

namespace IpDeskCtrl.UI
{
    /// <summary>
    /// Gestione file a due pannelli: a sinistra questo PC, a destra il PC remoto. Si sfoglia, si trasferisce nei due
    /// versi (pulsanti, doppio clic, trascinamento da un pannello all'altro o da Esplora risorse sul pannello remoto),
    /// si creano cartelle, si rinomina e si elimina. I trasferimenti vanno in coda, uno alla volta, con avanzamento.
    /// I file passano nel collegamento cifrato; sul PC remoto si lavora con i permessi dell'utente collegato.
    /// </summary>
    public sealed class FileManager
    {
        // ------------------------------------------------------------------ i due file system

        public interface IFs
        {
            string Title { get; }
            Task<(List<DriveEntry>? list, string err)> ListAsync(string path);
            Task<List<(string name, string path)>> PlacesAsync();
            Task<string> MkDirAsync(string path);
            Task<string> DeleteAsync(string path);
            Task<string> MoveAsync(string from, string to);
            /// <summary>Crea (svuota) il file.</summary>
            Task<string> CreateAsync(string path);
            Task<string> WriteAsync(string path, long pos, byte[] data);
            Task<(byte[]? data, string err)> ReadAsync(string path, long pos);
        }

        private static string Err(int status) => status switch
        {
            DriveOp.Ok => "",
            DriveOp.Forbidden => L.T("accesso negato"),
            DriveOp.NotFound => L.T("non trovato"),
            DriveOp.Exists => L.T("esiste già"),
            DriveOp.Conflict => L.T("in uso o percorso non valido"),
            _ => L.T("errore"),
        };

        public sealed class LocalFs : IFs
        {
            public string Title => L.T("Questo PC");
            public Task<(List<DriveEntry>?, string)> ListAsync(string path) => Task.Run<(List<DriveEntry>?, string)>(() =>
            {
                try
                {
                    if (path.Length == 0)
                        return (OperatingSystem.IsWindows()
                            ? DriveInfo.GetDrives().Where(d => { try { return d.IsReady; } catch { return false; } }).Select(d => new DriveEntry(d.Name, true, 0, default)).ToList()
                            : new List<DriveEntry> { new("/", true, 0, default) }, "");
                    var list = new List<DriveEntry>();
                    foreach (var i in new DirectoryInfo(path).EnumerateFileSystemInfos())
                    {
                        try
                        {
                            if ((i.Attributes & FileAttributes.System) != 0 && (i.Attributes & FileAttributes.Hidden) != 0) continue;
                            list.Add(new DriveEntry(i.Name, i is DirectoryInfo, i is FileInfo f ? f.Length : 0, i.LastWriteTimeUtc));
                        }
                        catch { }
                    }
                    return (list, "");
                }
                catch (UnauthorizedAccessException) { return (null, L.T("accesso negato")); }
                catch (Exception ex) { return (null, ex.Message); }
            });

            public Task<List<(string, string)>> PlacesAsync()
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var l = new List<(string, string)> { ("Home", home) };
                foreach (var (n, sub) in new[] { ("Desktop", "Desktop"), ("Documenti", "Documents"), ("Scaricati", "Downloads"), ("Immagini", "Pictures") })
                    if (Directory.Exists(Path.Combine(home, sub))) l.Add((n, Path.Combine(home, sub)));
                return Task.FromResult(l);
            }

            private static Task<string> Do(Action a) => Task.Run(() => { try { a(); return ""; } catch (Exception ex) { return ex.Message; } });
            public Task<string> MkDirAsync(string p) => Do(() => Directory.CreateDirectory(p));
            public Task<string> DeleteAsync(string p) => Do(() => { if (Directory.Exists(p)) Directory.Delete(p, true); else File.Delete(p); });
            public Task<string> MoveAsync(string a, string b) => Do(() => { if (Directory.Exists(a)) Directory.Move(a, b); else File.Move(a, b); });
            public Task<string> CreateAsync(string p) => Do(() => { using (new FileStream(p, FileMode.Create, FileAccess.Write)) { } });
            public Task<string> WriteAsync(string p, long pos, byte[] d) => Do(() => { using var f = new FileStream(p, FileMode.OpenOrCreate, FileAccess.Write); f.Position = pos; f.Write(d); });
            public Task<(byte[]?, string)> ReadAsync(string p, long pos) => Task.Run<(byte[]?, string)>(() =>
            {
                try
                {
                    using var f = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    f.Position = pos;
                    var buf = new byte[DriveOp.Chunk];
                    int got = 0;
                    while (got < buf.Length) { int r = f.Read(buf, got, buf.Length - got); if (r <= 0) break; got += r; }
                    return (got == buf.Length ? buf : buf[..got], "");
                }
                catch (Exception ex) { return (null, ex.Message); }
            });
        }

        public sealed class RemoteFs : IFs
        {
            private readonly DriveClient _c;
            public string Title { get; }
            public RemoteFs(DriveClient c, string title) { _c = c; Title = title; }

            public async Task<(List<DriveEntry>?, string)> ListAsync(string path)
            {
                var (s, d) = await _c.CallAsync(DriveOp.List, path);
                if (s != DriveOp.Ok) return (null, Err(s));
                var r = Reader(d);
                int n = r.I32();
                var list = new List<DriveEntry>(n);
                for (int i = 0; i < n; i++) list.Add(DriveShare.ReadEntry(r));
                return (list, "");
            }

            public async Task<List<(string, string)>> PlacesAsync()
            {
                var (s, d) = await _c.CallAsync(FsOp.Places, "");
                var l = new List<(string, string)>();
                if (s != DriveOp.Ok) return l;
                var r = Reader(d);
                int n = r.I32();
                for (int i = 0; i < n; i++) l.Add((r.Str(), r.Str()));
                return l;
            }

            public async Task<string> MkDirAsync(string p) => Err((await _c.CallAsync(DriveOp.MkDir, p)).status);
            public async Task<string> DeleteAsync(string p) => Err((await _c.CallAsync(DriveOp.Delete, p)).status);
            public async Task<string> MoveAsync(string a, string b) => Err((await _c.CallAsync(DriveOp.Move, a, b)).status);
            public async Task<string> CreateAsync(string p) => Err((await _c.CallAsync(DriveOp.Create, p)).status);
            public async Task<string> WriteAsync(string p, long pos, byte[] d) => Err((await _c.CallAsync(DriveOp.Write, p, "", pos, d)).status);
            public async Task<(byte[]?, string)> ReadAsync(string p, long pos)
            {
                var (s, d) = await _c.CallAsync(DriveOp.Read, p, "", pos);
                return s == DriveOp.Ok ? (d, "") : (null, Err(s));
            }

            private static MsgReader Reader(byte[] d)
            {
                var b = new byte[d.Length + 1];
                Buffer.BlockCopy(d, 0, b, 1, d.Length);
                return new MsgReader(b);
            }
        }

        // ------------------------------------------------------------------ percorsi (dei due sistemi, anche diversi)

        private static char Sep(string p) => p.Contains('\\') || (p.Length >= 2 && p[1] == ':') ? '\\' : '/';

        public static string Combine(string dir, string name)
        {
            if (dir.Length == 0) return name;
            char s = Sep(dir);
            return dir.EndsWith(s) ? dir + name : dir + s + name;
        }

        public static string Parent(string p)
        {
            if (p.Length == 0) return "";
            char s = Sep(p);
            string t = p.TrimEnd(s);
            if (t.Length == 0 || (t.Length == 2 && t[1] == ':')) return "";          // «/» o «C:\» → radice
            int i = t.LastIndexOf(s);
            if (i < 0) return "";
            string up = t[..i];
            if (up.Length == 0) return "/";
            if (up.Length == 2 && up[1] == ':') return up + "\\";
            return up;
        }

        // ------------------------------------------------------------------ finestra

        private sealed class Row
        {
            public DriveEntry E = null!;
            public override string ToString() => E.Name;
        }

        private sealed class Pane
        {
            public IFs Fs = null!;
            public string Path = "";
            public readonly TextBox PathBox = new() { FontSize = 12 };
            public readonly ListBox List = new() { SelectionMode = SelectionMode.Multiple, MinHeight = 200 };
            public readonly ComboBox Places = new() { MinWidth = 120, FontSize = 12 };
            public readonly TextBlock Status = new() { FontSize = 11, Opacity = 0.7 };
            public Border Box = null!;
            public List<Row> Rows = new();
            public IEnumerable<Row> Selected => List.SelectedItems?.OfType<Row>() ?? Enumerable.Empty<Row>();
        }

        private readonly Pane _l = new(), _r = new();
        private Pane _active;
        private readonly StackPanel _queueList = new() { Spacing = 4 };
        private readonly Queue<Func<CancellationToken, Task>> _queue = new();
        private CancellationTokenSource _cts = new();
        private bool _running;
        private Window _win = null!;
        private readonly string _who;

        private FileManager(IFs local, IFs remote, string who)
        {
            _l.Fs = local; _r.Fs = remote; _who = who;
            _active = _r;
        }

        public static void Open(Window? owner, DriveClient client, string who)
        {
            var fm = new FileManager(new LocalFs(), new RemoteFs(client, L.F("PC remoto {0}", who)), who);
            fm.Show(owner);
        }

        private void Show(Window? owner)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*"), RowDefinitions = new RowDefinitions("*,Auto,Auto"), Margin = new Thickness(12) };
            var left = BuildPane(_l);
            var right = BuildPane(_r);
            grid.Children.Add(left);
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);

            var toRemote = Ui.Btn("→", true);
            var toLocal = Ui.Btn("←", true);
            toRemote.FontSize = toLocal.FontSize = 18;
            ToolTip.SetTip(toRemote, L.T("Manda i file scelti a sinistra nella cartella aperta a destra"));
            ToolTip.SetTip(toLocal, L.T("Prendi i file scelti a destra nella cartella aperta a sinistra"));
            toRemote.Click += (_, _) => Transfer(_l, _r, _l.Selected.ToList());
            toLocal.Click += (_, _) => Transfer(_r, _l, _r.Selected.ToList());
            var mid = new StackPanel { Spacing = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0), Children = { toRemote, toLocal } };
            Grid.SetColumn(mid, 1);
            grid.Children.Add(mid);

            var newDir = Ui.Btn(L.T("Nuova cartella"));
            var rename = Ui.Btn(L.T("Rinomina"));
            var delete = Ui.Btn(L.T("Elimina"));
            newDir.Click += (_, _) => _ = NewFolderAsync(_active);
            rename.Click += (_, _) => _ = RenameAsync(_active);
            delete.Click += (_, _) => _ = DeleteAsync(_active);
            var tools = Ui.Row(8, newDir, rename, delete, Ui.Sub(L.T("(sul pannello evidenziato)"), 11));
            tools.Margin = new Thickness(0, 10, 0, 0);
            Grid.SetRow(tools, 1);
            Grid.SetColumnSpan(tools, 3);
            grid.Children.Add(tools);

            var cancel = Ui.Btn(L.T("Ferma i trasferimenti"));
            cancel.Click += (_, _) => { _cts.Cancel(); lock (_queue) _queue.Clear(); };
            var queueBox = new Border
            {
                Margin = new Thickness(0, 10, 0, 0),
                Child = Ui.Col(6, Ui.Row(10, Ui.Lbl(L.T("TRASFERIMENTI")), cancel),
                    new ScrollViewer { MaxHeight = 150, Content = _queueList })
            };
            Grid.SetRow(queueBox, 2);
            Grid.SetColumnSpan(queueBox, 3);
            grid.Children.Add(queueBox);

            _win = new Window
            {
                Title = L.F("File · {0}", _who),
                Width = 1100, Height = 720, MinWidth = 700, MinHeight = 450,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = grid,
            };
            _win.Closed += (_, _) => _cts.Cancel();
            if (owner != null) _win.Show(owner); else _win.Show();
            SetActive(_r);
            _ = InitAsync(_l);
            _ = InitAsync(_r);
        }

        private Control BuildPane(Pane p)
        {
            var up = Ui.Btn("↑");
            var refresh = Ui.Btn("⟳");
            ToolTip.SetTip(up, L.T("Cartella superiore"));
            ToolTip.SetTip(refresh, L.T("Aggiorna"));
            up.Click += (_, _) => _ = Load(p, Parent(p.Path));
            refresh.Click += (_, _) => _ = Load(p, p.Path);
            p.PathBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) _ = Load(p, (p.PathBox.Text ?? "").Trim()); };
            p.Places.SelectionChanged += (_, _) =>
            {
                if (p.Places.SelectedItem is ComboBoxItem { Tag: string path }) _ = Load(p, path);
            };
            p.List.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<Row>((row, _) =>
            {
                if (row == null) return new TextBlock();
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,90,130") };
                g.Children.Add(new TextBlock { Text = row.E.IsDir ? "📁" : "📄", Margin = new Thickness(0, 0, 6, 0) });
                var name = new TextBlock { Text = row.E.Name, TextTrimming = TextTrimming.CharacterEllipsis };
                Grid.SetColumn(name, 1); g.Children.Add(name);
                var size = new TextBlock { Text = row.E.IsDir ? "" : Size(row.E.Size), HorizontalAlignment = HorizontalAlignment.Right, Opacity = 0.7, FontSize = 12 };
                Grid.SetColumn(size, 2); g.Children.Add(size);
                var date = new TextBlock { Text = row.E.Modified == default ? "" : row.E.Modified.ToLocalTime().ToString("dd/MM/yyyy HH:mm"), Margin = new Thickness(12, 0, 0, 0), Opacity = 0.7, FontSize = 12 };
                Grid.SetColumn(date, 3); g.Children.Add(date);
                return g;
            });
            p.List.DoubleTapped += (_, _) =>
            {
                if (p.List.SelectedItem is Row { E.IsDir: true } r) _ = Load(p, Combine(p.Path, r.E.Name));
                else if (p.List.SelectedItem is Row f) Transfer(p, p == _l ? _r : _l, new List<Row> { f });
            };
            p.List.GotFocus += (_, _) => SetActive(p);
            p.List.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Delete) _ = DeleteAsync(p);
                else if (e.Key == Key.F2) _ = RenameAsync(p);
                else if (e.Key == Key.Back) _ = Load(p, Parent(p.Path));
                else if (e.Key == Key.F5) _ = Load(p, p.Path);
            };

            // trascinamento: da un pannello all'altro, e da Esplora risorse sul pannello remoto
            Point? down = null;
            p.List.AddHandler(InputElement.PointerPressedEvent, (_, e) => { if (e.GetCurrentPoint(p.List).Properties.IsLeftButtonPressed) down = e.GetPosition(p.List); }, RoutingStrategies.Tunnel);
            p.List.AddHandler(InputElement.PointerReleasedEvent, (_, _) => down = null, RoutingStrategies.Tunnel);
            p.List.AddHandler(InputElement.PointerMovedEvent, async (_, e) =>
            {
                if (down == null || !p.Selected.Any()) return;
                var pos = e.GetPosition(p.List);
                if (Math.Abs(pos.X - down.Value.X) + Math.Abs(pos.Y - down.Value.Y) < 12) return;
                down = null;
                var data = new DataObject();
                data.Set("ipdesk-fm", p == _l ? "L" : "R");
                await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
            }, RoutingStrategies.Tunnel);
            DragDrop.SetAllowDrop(p.List, true);
            p.List.AddHandler(DragDrop.DragOverEvent, (_, e) =>
            {
                bool ok = (e.Data.Get("ipdesk-fm") is string from && from != (p == _l ? "L" : "R")) || (p == _r && e.Data.Contains(DataFormats.Files));
                e.DragEffects = ok ? DragDropEffects.Copy : DragDropEffects.None;
            });
            p.List.AddHandler(DragDrop.DropEvent, (_, e) =>
            {
                if (e.Data.Get("ipdesk-fm") is string from && from != (p == _l ? "L" : "R"))
                {
                    var src = from == "L" ? _l : _r;
                    Transfer(src, p, src.Selected.ToList());
                }
                else if (p == _r && e.Data.Contains(DataFormats.Files))
                {
                    var paths = (e.Data.GetFiles() ?? Enumerable.Empty<IStorageItem>()).Select(i => i.TryGetLocalPath()).Where(x => x != null).Cast<string>().ToList();
                    SessionLog.Write($"FILE gestione file: {paths.Count} trascinati da Esplora risorse sul PC remoto");
                    foreach (var path in paths)
                    {
                        bool dir = Directory.Exists(path);
                        var e2 = new DriveEntry(System.IO.Path.GetFileName(path.TrimEnd('\\', '/')), dir, dir ? 0 : new FileInfo(path).Length, default);
                        string srcDir = System.IO.Path.GetDirectoryName(path.TrimEnd('\\', '/')) ?? "";
                        Enqueue(_l, srcDir, _r, _r.Path, e2);
                    }
                }
            });

            var head = new DockPanel { LastChildFill = true };
            var title = new TextBlock { Text = p.Fs.Title, FontWeight = FontWeight.Bold, FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            DockPanel.SetDock(title, Dock.Left); head.Children.Add(title);
            DockPanel.SetDock(p.Places, Dock.Right); head.Children.Add(p.Places);
            head.Children.Add(new Panel());
            var nav = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(up, Dock.Left); nav.Children.Add(up);
            DockPanel.SetDock(refresh, Dock.Right); nav.Children.Add(refresh);
            p.PathBox.Margin = new Thickness(6, 0);
            nav.Children.Add(p.PathBox);
            var body = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(head, Dock.Top); body.Children.Add(head);
            nav.Margin = new Thickness(0, 8);
            DockPanel.SetDock(nav, Dock.Top); body.Children.Add(nav);
            DockPanel.SetDock(p.Status, Dock.Bottom); body.Children.Add(p.Status);
            body.Children.Add(p.List);
            p.Box = new Border { BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(8), Padding = new Thickness(8), Child = body };
            p.Box.PointerPressed += (_, _) => SetActive(p);
            return p.Box;
        }

        private void SetActive(Pane p)
        {
            _active = p;
            _l.Box.BorderBrush = p == _l ? Brushes.DodgerBlue : Brushes.Transparent;
            _r.Box.BorderBrush = p == _r ? Brushes.DodgerBlue : Brushes.Transparent;
        }

        private async Task InitAsync(Pane p)
        {
            var places = await p.Fs.PlacesAsync();
            Dispatcher.UIThread.Post(() =>
            {
                p.Places.Items.Clear();
                p.Places.Items.Add(new ComboBoxItem { Content = L.T("Dischi"), Tag = "" });
                foreach (var (n, path) in places) p.Places.Items.Add(new ComboBoxItem { Content = L.T(n), Tag = path });
            });
            await Load(p, places.FirstOrDefault().path ?? "");
        }

        private async Task Load(Pane p, string path)
        {
            Dispatcher.UIThread.Post(() => p.Status.Text = L.T("Leggo…"));
            var (list, err) = await p.Fs.ListAsync(path);
            Dispatcher.UIThread.Post(() =>
            {
                if (list == null) { p.Status.Text = L.F("Non riesco ad aprire {0}: {1}", path.Length == 0 ? L.T("i dischi") : path, err); return; }
                p.Path = path;
                p.PathBox.Text = path;
                p.Rows = list.OrderByDescending(e => e.IsDir).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).Select(e => new Row { E = e }).ToList();
                p.List.ItemsSource = p.Rows;
                int files = list.Count(e => !e.IsDir);
                p.Status.Text = L.F("{0} cartelle, {1} file, {2}", list.Count - files, files, Size(list.Where(e => !e.IsDir).Sum(e => e.Size)));
            });
        }

        private static string Size(long b) => b >= 1L << 30 ? $"{b / (double)(1L << 30):F1} GB" : b >= 1 << 20 ? $"{b / (double)(1 << 20):F1} MB" : b >= 1024 ? $"{b / 1024.0:F0} kB" : $"{b} B";

        // ------------------------------------------------------------------ comandi

        private async Task<string?> Ask(string title, string text, string value)
        {
            var box = new TextBox { Text = value };
            var ok = Ui.Btn(L.T("OK"), true);
            var no = Ui.Btn(L.T("Annulla"));
            var dlg = Ui.Dialog(title, 420);
            string? result = null;
            ok.Click += (_, _) => { result = box.Text; dlg.Close(); };
            no.Click += (_, _) => dlg.Close();
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { result = box.Text; dlg.Close(); } };
            dlg.Content = Ui.Col(12, Ui.Sub(text), box, Ui.Row(8, ok, no));
            ((Control)dlg.Content).Margin = new Thickness(16);
            dlg.Opened += (_, _) => { box.Focus(); box.SelectAll(); };
            await dlg.ShowDialog(_win);
            return string.IsNullOrWhiteSpace(result) ? null : result.Trim();
        }

        private async Task<bool> Confirm(string title, string text)
        {
            var ok = Ui.Btn(L.T("Sì"), true);
            var no = Ui.Btn(L.T("No"));
            var dlg = Ui.Dialog(title, 440);
            bool result = false;
            ok.Click += (_, _) => { result = true; dlg.Close(); };
            no.Click += (_, _) => dlg.Close();
            dlg.Content = Ui.Col(12, Ui.Sub(text), Ui.Row(8, ok, no));
            ((Control)dlg.Content).Margin = new Thickness(16);
            await dlg.ShowDialog(_win);
            return result;
        }

        private async Task NewFolderAsync(Pane p)
        {
            if (p.Path.Length == 0) return;
            string? name = await Ask(L.T("Nuova cartella"), L.F("Nuova cartella in {0}", p.Path), L.T("Nuova cartella"));
            if (name == null) return;
            string err = await p.Fs.MkDirAsync(Combine(p.Path, name));
            if (err.Length > 0) p.Status.Text = L.F("Cartella non creata: {0}", err);
            await Load(p, p.Path);
        }

        private async Task RenameAsync(Pane p)
        {
            var row = p.Selected.FirstOrDefault();
            if (row == null || p.Path.Length == 0) return;
            string? name = await Ask(L.T("Rinomina"), L.F("Nuovo nome per «{0}»", row.E.Name), row.E.Name);
            if (name == null || name == row.E.Name) return;
            string err = await p.Fs.MoveAsync(Combine(p.Path, row.E.Name), Combine(p.Path, name));
            if (err.Length > 0) p.Status.Text = L.F("Non rinominato: {0}", err);
            await Load(p, p.Path);
        }

        private async Task DeleteAsync(Pane p)
        {
            var rows = p.Selected.ToList();
            if (rows.Count == 0 || p.Path.Length == 0) return;
            string what = rows.Count == 1 ? "«" + rows[0].E.Name + "»" : L.F("{0} elementi", rows.Count);
            if (!await Confirm(L.T("Elimina"), L.F("Elimino {0} da {1}? Non si può annullare.", what, p.Fs.Title))) return;
            foreach (var r in rows)
            {
                string err = await p.Fs.DeleteAsync(Combine(p.Path, r.E.Name));
                if (err.Length > 0) p.Status.Text = L.F("«{0}» non eliminato: {1}", r.E.Name, err);
            }
            await Load(p, p.Path);
        }

        // ------------------------------------------------------------------ trasferimenti

        private void Transfer(Pane from, Pane to, List<Row> rows)
        {
            if (rows.Count == 0) return;
            if (to.Path.Length == 0) { to.Status.Text = L.T("Apri prima una cartella di destinazione."); return; }
            foreach (var r in rows) Enqueue(from, from.Path, to, to.Path, r.E);
        }

        private void Enqueue(Pane from, string srcDir, Pane to, string dstDir, DriveEntry e)
        {
            var line = new TextBlock { FontSize = 12, Text = $"{(from == _l ? "→" : "←")}  {e.Name}  ·  " + L.T("in coda") };
            var bar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 6 };
            Dispatcher.UIThread.Post(() => { _queueList.Children.Add(Ui.Col(2, line, bar)); if (_queueList.Children.Count > 60) _queueList.Children.RemoveAt(0); });
            lock (_queue)
            {
                _queue.Enqueue(async ct =>
                {
                    var sw = Stopwatch.StartNew();
                    long done = 0, total = e.Size;
                    void Progress(string name)
                    {
                        double mbs = sw.Elapsed.TotalSeconds > 0.5 ? done / sw.Elapsed.TotalSeconds / (1 << 20) : 0;
                        Dispatcher.UIThread.Post(() =>
                        {
                            line.Text = $"{(from == _l ? "→" : "←")}  {name}  ·  {Size(done)}" + (total > 0 ? " / " + Size(total) : "") + (mbs > 0 ? $"  ·  {mbs:F1} MB/s" : "");
                            bar.Value = total > 0 ? Math.Min(100, done * 100.0 / total) : 0;
                        });
                    }
                    string err = await CopyAsync(from.Fs, Combine(srcDir, e.Name), e.IsDir, to.Fs, Combine(dstDir, e.Name), n => { done += n; Progress(e.Name); }, ct);
                    SessionLog.Write($"FILE gestione file: {(from == _l ? "mandato" : "preso")} {e.Name} ({Size(done)} in {sw.Elapsed.TotalSeconds:F1} s)" + (err.Length > 0 ? " · non riuscito: " + err : ""));
                    Dispatcher.UIThread.Post(() =>
                    {
                        line.Text = $"{(from == _l ? "→" : "←")}  {e.Name}  ·  " + (err.Length > 0 ? L.F("non riuscito: {0}", err) : L.F("fatto, {0} in {1:F1} s", Size(done), sw.Elapsed.TotalSeconds));
                        bar.Value = err.Length > 0 ? 0 : 100;
                    });
                    if (to.Path == dstDir) await Load(to, to.Path);
                });
            }
            _ = RunQueueAsync();
        }

        private async Task RunQueueAsync()
        {
            lock (_queue) { if (_running) return; _running = true; }
            try
            {
                while (true)
                {
                    Func<CancellationToken, Task>? job;
                    lock (_queue) { if (_queue.Count == 0) { _running = false; return; } job = _queue.Dequeue(); }
                    if (_cts.IsCancellationRequested) _cts = new CancellationTokenSource();
                    try { await job(_cts.Token); } catch (Exception ex) { SessionLog.Write("FILE gestione file: " + ex.Message); }
                }
            }
            finally { lock (_queue) _running = false; }
        }

        /// <summary>Copia un file o una cartella intera da un file system all'altro, a pezzi da 512 kB.</summary>
        private static async Task<string> CopyAsync(IFs src, string sp, bool dir, IFs dst, string dp, Action<long> step, CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return L.T("fermato");
            if (dir)
            {
                string e = await dst.MkDirAsync(dp);
                if (e.Length > 0) return e;
                var (list, err) = await src.ListAsync(sp);
                if (list == null) return err;
                foreach (var c in list)
                {
                    string r = await CopyAsync(src, Combine(sp, c.Name), c.IsDir, dst, Combine(dp, c.Name), step, ct);
                    if (r.Length > 0) return r;
                }
                return "";
            }
            string ce = await dst.CreateAsync(dp);
            if (ce.Length > 0) return ce;
            long pos = 0;
            while (true)
            {
                if (ct.IsCancellationRequested) return L.T("fermato");
                var (data, err) = await src.ReadAsync(sp, pos);
                if (data == null) return err;
                if (data.Length == 0) break;
                string we = await dst.WriteAsync(dp, pos, data);
                if (we.Length > 0) return we;
                pos += data.Length;
                step(data.Length);
                if (data.Length < DriveOp.Chunk) break;
            }
            return "";
        }
    }
}

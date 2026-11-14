using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Threading;
using IpDeskCtrl.Core;

namespace IpDeskCtrl.UI
{
    /// <summary>
    /// Un solo programma per utente: chiudendo la finestra IP-DESK-CTRL resta attivo nascosto (icona vicino all'orologio),
    /// quindi riaprirlo dal menu Start o dal desktop non deve avviarne un secondo, ma mostrare la finestra di quello già attivo.
    /// Funziona con un file bloccato nella cartella dei dati (blocco esclusivo, anche su Linux e macOS) e un file «segnale».
    /// </summary>
    public static class SingleInstance
    {
        private static FileStream? _lock;
        private static DispatcherTimer? _timer;

        private static string LockPath => Path.Combine(AppSettings.Folder, "programma.lock");
        private static string SignalPath => Path.Combine(AppSettings.Folder, "mostra-finestra");

        /// <summary>true = questo è il primo programma avviato; false = ce n'è già uno (gli si è chiesto di mostrarsi).</summary>
        public static bool Acquire()
        {
            try
            {
                _lock = new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                try { File.Delete(SignalPath); } catch { }
                return true;
            }
            catch (IOException)
            {
                try { File.WriteAllText(SignalPath, Environment.ProcessId.ToString()); } catch { }
                return false;
            }
            catch (Exception ex)
            {
                // cartella non scrivibile o altro: meglio un programma in più che nessuno
                ErrorLog.Write("Controllo del programma già avviato: " + ex.Message);
                return true;
            }
        }

        /// <summary>Chiama <paramref name="show"/> quando un altro avvio chiede di mostrare la finestra.</summary>
        public static void Watch(Action show)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _timer.Tick += (_, _) =>
            {
                try
                {
                    if (!File.Exists(SignalPath)) return;
                    File.Delete(SignalPath);
                    show();
                }
                catch { }
            };
            _timer.Start();
        }
    }

    /// <summary>Icona vicino all'orologio (area di notifica su Windows, barra dei menu su macOS, vassoio su Linux).</summary>
    public static class Tray
    {
        private static TrayIcon? _icon;

        public static void Install(Avalonia.Application app, MainWindow main, Action quit)
        {
            try
            {
                var open = new NativeMenuItem(L.T("Apri IP-DESK-CTRL"));
                open.Click += (_, _) => main.ShowMain();
                var exit = new NativeMenuItem(L.T("Esci da IP-DESK-CTRL"));
                exit.Click += (_, _) => quit();
                var menu = new NativeMenu();
                menu.Items.Add(open);
                menu.Items.Add(new NativeMenuItemSeparator());
                menu.Items.Add(exit);
                _icon = new TrayIcon { Icon = main.Icon, ToolTipText = main.Title, Menu = menu, IsVisible = true };
                _icon.Clicked += (_, _) => main.ShowMain();
                main.TitleUpdated += t => { if (_icon != null) _icon.ToolTipText = t; };
                TrayIcon.SetIcons(app, new TrayIcons { _icon });
            }
            catch (Exception ex) { ErrorLog.Write("Icona vicino all'orologio: " + ex.Message); }
        }

        public static void Remove()
        {
            try { if (_icon != null) _icon.IsVisible = false; } catch { }
        }
    }
}

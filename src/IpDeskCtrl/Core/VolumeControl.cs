using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IpDeskCtrl.Core
{
    /// <summary>
    /// Volume dell'uscita predefinita del PC comandato, chiesto dal visore (Volume +, Volume −, Muto).
    /// Windows: tasti multimediali; Linux: pactl (PulseAudio/PipeWire); macOS: AppleScript; Android: AudioManager.
    /// </summary>
    public static class VolumeControl
    {
        public const int Down = 0, Up = 1, Mute = 2;

        /// <summary>Android: lo imposta la piattaforma. "" = fatto, altrimenti il motivo.</summary>
        public static Func<int, string>? Platform { get; set; }

        public static string Change(int action)
        {
            try
            {
                if (Platform != null) return Platform(action);
                if (OperatingSystem.IsWindows())
                {
                    byte vk = action == Up ? (byte)0xAF : action == Down ? (byte)0xAE : (byte)0xAD;   // VK_VOLUME_UP / DOWN / MUTE
                    keybd_event(vk, 0, 0, UIntPtr.Zero);
                    keybd_event(vk, 0, 2 /* KEYEVENTF_KEYUP */, UIntPtr.Zero);
                    return "";
                }
                if (OperatingSystem.IsMacOS())
                {
                    string script = action == Mute
                        ? "set volume output muted not (output muted of (get volume settings))"
                        : $"set volume output volume ((output volume of (get volume settings)) {(action == Up ? "+" : "-")} 6)";
                    return Run("osascript", "-e \"" + script.Replace("\"", "\\\"") + "\"");
                }
                if (!Screen.WaylandScreen.Which("pactl")) return "Per il volume serve «pactl» (sudo apt install pulseaudio-utils)";
                return action == Mute
                    ? Run("pactl", "set-sink-mute @DEFAULT_SINK@ toggle")
                    : Run("pactl", $"set-sink-volume @DEFAULT_SINK@ {(action == Up ? "+5%" : "-5%")}");
            }
            catch (Exception ex) { return "Volume non cambiato: " + ex.Message; }
        }

        private static string Run(string exe, string args)
        {
            using var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true });
            if (p == null) return exe + " non avviabile";
            p.WaitForExit(3000);
            return p.HasExited && p.ExitCode == 0 ? "" : "Volume non cambiato: " + p.StandardError.ReadToEnd().Trim();
        }

        [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    }
}

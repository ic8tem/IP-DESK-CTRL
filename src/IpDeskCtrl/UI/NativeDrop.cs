using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using IpDeskCtrl.Core;

namespace IpDeskCtrl.UI
{
    /// <summary>
    /// Windows: controllo e riserva per i file trascinati da Esplora risorse sulla finestra del visore.
    /// Nel registro: se la finestra ha il bersaglio di trascinamento OLE di Avalonia, a che livello di integrità gira
    /// il programma (un programma da amministratore non riceve il trascinamento OLE da Esplora risorse) e il thread.
    /// Se il bersaglio OLE manca, o il programma è da amministratore, si accettano i file anche con il vecchio
    /// messaggio WM_DROPFILES, che Windows lascia passare se lo si permette esplicitamente.
    /// </summary>
    public static class NativeDrop
    {
        private delegate IntPtr WndProc(IntPtr h, uint msg, IntPtr w, IntPtr l);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetProp(IntPtr hwnd, string name);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint msg, uint action, IntPtr info);
        [DllImport("shell32.dll")] private static extern void DragAcceptFiles(IntPtr hwnd, bool accept);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint DragQueryFileW(IntPtr hdrop, uint i, StringBuilder? sb, uint cch);
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        [DllImport("shell32.dll")] private static extern bool DragQueryPoint(IntPtr hdrop, out POINT pt);
        [DllImport("shell32.dll")] private static extern void DragFinish(IntPtr hdrop);
        [DllImport("ole32.dll")] private static extern int RevokeDragDrop(IntPtr hwnd);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr h, int idx, IntPtr v);
        [DllImport("user32.dll", EntryPoint = "CallWindowProcW")] private static extern IntPtr CallWindowProc(IntPtr prev, IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(IntPtr token, int cls, IntPtr info, int len, out int ret);
        [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint n);
        [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

        private const uint WM_DROPFILES = 0x0233, WM_COPYDATA = 0x004A, WM_COPYGLOBALDATA = 0x0049;
        private static readonly Dictionary<IntPtr, (IntPtr prev, WndProc proc, Action<List<string>, Point> onDrop)> _hooked = new();

        /// <summary>Livello di integrità del processo: 0x2000 medio (normale), 0x3000 alto (amministratore), 0x4000 sistema.</summary>
        public static int Integrity()
        {
            if (!OperatingSystem.IsWindows()) return 0;
            if (!OpenProcessToken(GetCurrentProcess(), 0x0008, out IntPtr tok)) return 0;
            IntPtr buf = Marshal.AllocHGlobal(256);
            try
            {
                if (!GetTokenInformation(tok, 25 /* TokenIntegrityLevel */, buf, 256, out _)) return 0;
                IntPtr sid = Marshal.ReadIntPtr(buf);
                int n = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                return Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(n - 1)));
            }
            catch { return 0; }
            finally { Marshal.FreeHGlobal(buf); CloseHandle(tok); }
        }

        private static string IntegrityName(int il) => il switch
        {
            >= 0x4000 => "sistema",
            >= 0x3000 => "alto (amministratore)",
            >= 0x2000 => "medio (utente normale)",
            > 0 => "basso",
            _ => "sconosciuto",
        };

        /// <summary>onDrop(percorsi, punto nella finestra in unità di Avalonia), chiamato sul thread grafico.</summary>
        public static void Attach(TopLevel top, Action<List<string>, Point> onDrop)
        {
            if (!OperatingSystem.IsWindows()) return;
            try
            {
                IntPtr hwnd = top.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                if (hwnd == IntPtr.Zero || _hooked.ContainsKey(hwnd)) return;
                bool ole = GetProp(hwnd, "OleDropTargetInterface") != IntPtr.Zero;
                int il = Integrity();
                bool high = il >= 0x3000;
                SessionLog.Write($"TRASCINAMENTO finestra del visore: bersaglio OLE {(ole ? "registrato" : "MANCANTE")} · livello del programma {IntegrityName(il)} · thread {Thread.CurrentThread.GetApartmentState()}");
                if (ole && !high) return;           // caso normale: ci pensa Avalonia
                // riserva: WM_DROPFILES, permesso anche da un programma meno privilegiato (Esplora risorse)
                foreach (uint m in new[] { WM_DROPFILES, WM_COPYDATA, WM_COPYGLOBALDATA }) ChangeWindowMessageFilterEx(hwnd, m, 1 /* MSGFLT_ALLOW */, IntPtr.Zero);
                if (ole && high)
                {
                    // da amministratore Windows blocca il trascinamento OLE da Esplora risorse: senza bersaglio OLE
                    // Esplora risorse ripiega su WM_DROPFILES
                    RevokeDragDrop(hwnd);
                    SessionLog.Write("TRASCINAMENTO programma da amministratore: tolgo il bersaglio OLE e uso WM_DROPFILES, che Windows lascia passare");
                }
                WndProc proc = (h, msg, w, l) =>
                {
                    if (msg == WM_DROPFILES && _hooked.TryGetValue(h, out var e))
                    {
                        try { Dropped(top, w, e.onDrop); } catch (Exception ex) { SessionLog.Write("TRASCINAMENTO WM_DROPFILES non letto: " + ex.Message); }
                        return IntPtr.Zero;
                    }
                    return _hooked.TryGetValue(h, out var x) ? CallWindowProc(x.prev, h, msg, w, l) : IntPtr.Zero;
                };
                IntPtr prev = SetWindowLongPtr(hwnd, -4 /* GWLP_WNDPROC */, Marshal.GetFunctionPointerForDelegate(proc));
                _hooked[hwnd] = (prev, proc, onDrop);
                DragAcceptFiles(hwnd, true);
                SessionLog.Write("TRASCINAMENTO riserva WM_DROPFILES attiva sulla finestra del visore");
            }
            catch (Exception ex) { SessionLog.Write("TRASCINAMENTO controllo della finestra non riuscito: " + ex.Message); }
        }

        private static void Dropped(TopLevel top, IntPtr hdrop, Action<List<string>, Point> onDrop)
        {
            var list = new List<string>();
            try
            {
                uint n = DragQueryFileW(hdrop, 0xFFFFFFFF, null, 0);
                for (uint i = 0; i < n; i++)
                {
                    uint len = DragQueryFileW(hdrop, i, null, 0);
                    var sb = new StringBuilder((int)len + 1);
                    DragQueryFileW(hdrop, i, sb, len + 1);
                    list.Add(sb.ToString());
                }
                DragQueryPoint(hdrop, out POINT pt);
                double sc = top.RenderScaling > 0 ? top.RenderScaling : 1;
                SessionLog.Write($"TRASCINAMENTO WM_DROPFILES: {list.Count} file rilasciati sulla finestra");
                onDrop(list, new Point(pt.X / sc, pt.Y / sc));
            }
            finally { DragFinish(hdrop); }
        }
    }
}

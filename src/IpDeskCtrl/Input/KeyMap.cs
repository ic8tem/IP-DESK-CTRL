using System;
using System.Collections.Generic;

namespace IpDeskCtrl.Input
{
    /// <summary>
    /// I tasti viaggiano come codici «evdev» di Linux, che indicano la POSIZIONE del tasto sulla tastiera
    /// (non il carattere): il carattere lo decide la disposizione della tastiera del PC comandato.
    /// Con la stessa disposizione sui due PC (per esempio italiana) si ottiene quello che si digita.
    ///
    /// Tabella: nomi dei tasti di Avalonia (PhysicalKey e Key) → evdev; evdev → codice di scansione di Windows
    /// (0xE0xx = tasto esteso) e codice virtuale di macOS.
    /// </summary>
    public static class KeyMap
    {
        private sealed record K(int Evdev, int Win, int Mac);

        // nome → (evdev, scancode Windows, keycode macOS); -1 = non esiste
        private static readonly Dictionary<string, K> ByName = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, K> ByEvdev = new();

        private static void A(int evdev, int win, int mac, params string[] names)
        {
            var k = new K(evdev, win, mac);
            ByEvdev[evdev] = k;
            foreach (var n in names) ByName.TryAdd(n, k);
        }

        static KeyMap()
        {
            A(1, 0x01, 0x35, "Escape");
            A(2, 0x02, 0x12, "Digit1", "D1"); A(3, 0x03, 0x13, "Digit2", "D2"); A(4, 0x04, 0x14, "Digit3", "D3");
            A(5, 0x05, 0x15, "Digit4", "D4"); A(6, 0x06, 0x17, "Digit5", "D5"); A(7, 0x07, 0x16, "Digit6", "D6");
            A(8, 0x08, 0x1A, "Digit7", "D7"); A(9, 0x09, 0x1C, "Digit8", "D8"); A(10, 0x0A, 0x19, "Digit9", "D9");
            A(11, 0x0B, 0x1D, "Digit0", "D0");
            A(12, 0x0C, 0x1B, "Minus", "OemMinus"); A(13, 0x0D, 0x18, "Equal", "OemPlus");
            A(14, 0x0E, 0x33, "Backspace", "Back"); A(15, 0x0F, 0x30, "Tab");
            A(16, 0x10, 0x0C, "Q"); A(17, 0x11, 0x0D, "W"); A(18, 0x12, 0x0E, "E"); A(19, 0x13, 0x0F, "R");
            A(20, 0x14, 0x11, "T"); A(21, 0x15, 0x10, "Y"); A(22, 0x16, 0x20, "U"); A(23, 0x17, 0x22, "I");
            A(24, 0x18, 0x1F, "O"); A(25, 0x19, 0x23, "P");
            A(26, 0x1A, 0x21, "BracketLeft", "OemOpenBrackets"); A(27, 0x1B, 0x1E, "BracketRight", "OemCloseBrackets");
            A(28, 0x1C, 0x24, "Enter", "Return");
            A(29, 0x1D, 0x3B, "ControlLeft", "LeftCtrl");
            A(30, 0x1E, 0x00, "A"); A(31, 0x1F, 0x01, "S"); A(32, 0x20, 0x02, "D"); A(33, 0x21, 0x03, "F");
            A(34, 0x22, 0x05, "G"); A(35, 0x23, 0x04, "H"); A(36, 0x24, 0x26, "J"); A(37, 0x25, 0x28, "K");
            A(38, 0x26, 0x25, "L");
            A(39, 0x27, 0x29, "Semicolon", "OemSemicolon"); A(40, 0x28, 0x27, "Quote", "OemQuotes");
            A(41, 0x29, 0x32, "Backquote", "OemTilde");
            A(42, 0x2A, 0x38, "ShiftLeft", "LeftShift");
            A(43, 0x2B, 0x2A, "Backslash", "OemPipe", "OemBackslash");
            A(44, 0x2C, 0x06, "Z"); A(45, 0x2D, 0x07, "X"); A(46, 0x2E, 0x08, "C"); A(47, 0x2F, 0x09, "V");
            A(48, 0x30, 0x0B, "B"); A(49, 0x31, 0x2D, "N"); A(50, 0x32, 0x2E, "M");
            A(51, 0x33, 0x2B, "Comma", "OemComma"); A(52, 0x34, 0x2F, "Period", "OemPeriod"); A(53, 0x35, 0x2C, "Slash", "OemQuestion");
            A(54, 0x36, 0x3C, "ShiftRight", "RightShift");
            A(55, 0x37, 0x43, "NumPadMultiply", "Multiply");
            A(56, 0x38, 0x3A, "AltLeft", "LeftAlt");
            A(57, 0x39, 0x31, "Space");
            A(58, 0x3A, 0x39, "CapsLock", "Capital");
            A(59, 0x3B, 0x7A, "F1"); A(60, 0x3C, 0x78, "F2"); A(61, 0x3D, 0x63, "F3"); A(62, 0x3E, 0x76, "F4");
            A(63, 0x3F, 0x60, "F5"); A(64, 0x40, 0x61, "F6"); A(65, 0x41, 0x62, "F7"); A(66, 0x42, 0x64, "F8");
            A(67, 0x43, 0x65, "F9"); A(68, 0x44, 0x6D, "F10");
            A(69, 0xE045, 0x47, "NumLock");
            A(70, 0x46, -1, "ScrollLock", "Scroll");
            A(71, 0x47, 0x59, "NumPad7"); A(72, 0x48, 0x5B, "NumPad8"); A(73, 0x49, 0x5C, "NumPad9");
            A(74, 0x4A, 0x4E, "NumPadSubtract", "Subtract");
            A(75, 0x4B, 0x56, "NumPad4"); A(76, 0x4C, 0x57, "NumPad5"); A(77, 0x4D, 0x58, "NumPad6");
            A(78, 0x4E, 0x45, "NumPadAdd", "Add");
            A(79, 0x4F, 0x53, "NumPad1"); A(80, 0x50, 0x54, "NumPad2"); A(81, 0x51, 0x55, "NumPad3");
            A(82, 0x52, 0x52, "NumPad0"); A(83, 0x53, 0x41, "NumPadDecimal", "Decimal");
            A(86, 0x56, 0x0A, "IntlBackslash", "Oem102");
            A(87, 0x57, 0x67, "F11"); A(88, 0x58, 0x6F, "F12");
            A(89, 0x73, 0x5E, "IntlRo");
            A(96, 0xE01C, 0x4C, "NumPadEnter");
            A(97, 0xE01D, 0x3E, "ControlRight", "RightCtrl");
            A(98, 0xE035, 0x4B, "NumPadDivide", "Divide");
            A(99, 0xE037, 0x69, "PrintScreen", "Snapshot", "Print");
            A(100, 0xE038, 0x3D, "AltRight", "RightAlt");
            A(102, 0xE047, 0x73, "Home"); A(103, 0xE048, 0x7E, "ArrowUp", "Up");
            A(104, 0xE049, 0x74, "PageUp", "Prior"); A(105, 0xE04B, 0x7B, "ArrowLeft", "Left");
            A(106, 0xE04D, 0x7C, "ArrowRight", "Right"); A(107, 0xE04F, 0x77, "End");
            A(108, 0xE050, 0x7D, "ArrowDown", "Down"); A(109, 0xE051, 0x79, "PageDown", "Next");
            A(110, 0xE052, 0x72, "Insert"); A(111, 0xE053, 0x75, "Delete");
            A(113, 0xE020, 0x4A, "AudioVolumeMute", "VolumeMute");
            A(114, 0xE02E, 0x49, "AudioVolumeDown", "VolumeDown");
            A(115, 0xE030, 0x48, "AudioVolumeUp", "VolumeUp");
            A(117, 0x59, 0x51, "NumPadEqual");
            A(119, 0xE11D, -1, "Pause");
            A(121, 0x7E, 0x5F, "NumPadComma");
            A(124, 0x7D, 0x5D, "IntlYen");
            A(125, 0xE05B, 0x37, "MetaLeft", "LWin");
            A(126, 0xE05C, 0x36, "MetaRight", "RWin");
            A(127, 0xE05D, 0x6E, "ContextMenu", "Apps");
            A(163, 0xE019, -1, "MediaTrackNext", "MediaNextTrack");
            A(164, 0xE022, -1, "MediaPlayPause");
            A(165, 0xE010, -1, "MediaTrackPrevious", "MediaPreviousTrack");
            A(166, 0xE024, -1, "MediaStop");
            int[] macF13 = { 0x69, 0x6B, 0x71, 0x6A, 0x40, 0x4F, 0x50, 0x5A };
            int[] winF13 = { 0x64, 0x65, 0x66, 0x67, 0x68, 0x69, 0x6A, 0x6B, 0x6C, 0x6D, 0x6E, 0x76 };
            for (int i = 0; i < 12; i++)
                A(183 + i, winF13[i], i < macF13.Length ? macF13[i] : -1, "F" + (13 + i));
        }

        /// <summary>Codice evdev dal nome del tasto fisico (preferito) o del tasto logico di Avalonia; 0 = sconosciuto.</summary>
        public static int ToEvdev(string physicalKeyName, string keyName)
        {
            if (physicalKeyName.Length > 0 && physicalKeyName != "None" && ByName.TryGetValue(physicalKeyName, out var k)) return k.Evdev;
            if (keyName.Length > 0 && ByName.TryGetValue(keyName, out k)) return k.Evdev;
            return 0;
        }

        /// <summary>Scancode di Windows (0xE0xx per i tasti estesi); -1 se non c'è.</summary>
        public static int ToWindows(int evdev) => ByEvdev.TryGetValue(evdev, out var k) ? k.Win : -1;

        /// <summary>Codice virtuale di macOS; -1 se non c'è.</summary>
        public static int ToMac(int evdev) => ByEvdev.TryGetValue(evdev, out var k) ? k.Mac : -1;

        public const int LeftCtrl = 29, LeftShift = 42, LeftAlt = 56, RightAlt = 100, RightCtrl = 97, RightShift = 54,
                         LeftMeta = 125, RightMeta = 126, Delete = 111, Tab = 15, Escape = 1, F4 = 62, PrintScreen = 99;
    }
}

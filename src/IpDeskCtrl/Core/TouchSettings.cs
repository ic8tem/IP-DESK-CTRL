using System;
using System.Collections.Generic;
using System.Linq;

namespace IpDeskCtrl.Core
{
    /// <summary>
    /// Telefono e tablet come visore: modo del dito, gesti assegnabili, tempi, velocità del puntatore, barra dei tasti.
    /// Idee riprese da Sofatap (gesti con profili pronti, pannello scorciatoie, striscia di scorrimento) e da bVNC
    /// (tocco diretto, zoom con due dita, barra dei tasti personalizzabile).
    /// </summary>
    public sealed class TouchSettings
    {
        /// <summary>"touchpad" = il dito sposta il puntatore di quanto si muove; "diretto" = si clicca dove si tocca.</summary>
        public string Mode { get; set; } = "touchpad";
        public string Preset { get; set; } = "sofatap";
        /// <summary>Gesto → azione (chiavi in <see cref="TouchGestures"/>, valori in <see cref="TouchActions"/>).</summary>
        public Dictionary<string, string> Map { get; set; } = TouchPresets.Get("sofatap");
        public int HoldMs { get; set; } = 300;              // pressione lunga (200–1200)
        public int DoubleTapMs { get; set; } = 300;         // intervallo del doppio tocco (150–500)
        public double PointerSpeed { get; set; } = 1.0;     // 0,25–3
        public bool NaturalScroll { get; set; } = true;     // due dita in giù = si torna su, come sul telefono
        public bool ScrollStrip { get; set; } = true;       // striscia lungo il bordo destro per scorrere
        public bool PinchZoom { get; set; } = true;         // zoom con due dita e spostamento dell'immagine ingrandita
        /// <summary>Tasti della barra sopra la tastiera del telefono (identificativi di <see cref="SpecialKeys"/>).</summary>
        public List<string> Keys { get; set; } = SpecialKeys.Default.ToList();

        public string Action(string gesture) =>
            Map.TryGetValue(gesture, out var a) && TouchActions.All.Contains(a) ? a : TouchPresets.Get(Preset).GetValueOrDefault(gesture, TouchActions.None);

        public void ApplyPreset(string preset)
        {
            Preset = TouchPresets.Names.ContainsKey(preset) ? preset : "sofatap";
            Map = TouchPresets.Get(Preset);
            HoldMs = Preset == "sofatap" ? 300 : Preset == "classico" ? 600 : 500;
            DoubleTapMs = 300;
            NaturalScroll = true;
        }

        public void Normalize()
        {
            HoldMs = Math.Clamp(HoldMs, 200, 1200);
            DoubleTapMs = Math.Clamp(DoubleTapMs, 150, 500);
            PointerSpeed = Math.Clamp(PointerSpeed, 0.25, 3);
            if (Mode != "diretto") Mode = "touchpad";
            Map ??= TouchPresets.Get(Preset);
            Keys = (Keys ?? new List<string>()).Where(k => SpecialKeys.ById.ContainsKey(k)).Distinct().ToList();
        }
    }

    /// <summary>Gesti riconosciuti sullo schermo remoto.</summary>
    public static class TouchGestures
    {
        public const string Tap = "tocco", DoubleLeft = "doppio-sinistra", DoubleRight = "doppio-destra", TwoTap = "tocco-2-dita",
            ThreeTap = "tocco-3-dita", LongPress = "pressione-lunga", TapHold = "tocco-e-tieni", Slide1 = "scorri-1-dito", Slide2 = "scorri-2-dita";

        public static readonly (string key, string label, bool hold, bool slide)[] All =
        {
            (Tap, "Tocco", false, false),
            (DoubleLeft, "Doppio tocco, metà sinistra", false, false),
            (DoubleRight, "Doppio tocco, metà destra", false, false),
            (TwoTap, "Tocco con due dita", false, false),
            (ThreeTap, "Tocco con tre dita", false, false),
            (LongPress, "Pressione lunga", true, false),
            (TapHold, "Tocca e poi tieni premuto", true, false),
            (Slide1, "Un dito che scorre", false, true),
            (Slide2, "Due dita che scorrono", false, true),
        };
    }

    /// <summary>Azioni assegnabili ai gesti.</summary>
    public static class TouchActions
    {
        public const string None = "nessuna", Left = "clic", Right = "clic-destro", Middle = "clic-centrale", Double = "doppio-clic",
            Drag = "trascina", Enter = "invio", Escape = "esc", Keyboard = "tastiera", Dictation = "dettatura", Shortcuts = "scorciatoie",
            Move = "muovi", Scroll = "scorri";

        public static readonly HashSet<string> All = new() { None, Left, Right, Middle, Double, Drag, Enter, Escape, Keyboard, Dictation, Shortcuts, Move, Scroll };

        public static readonly Dictionary<string, string> Labels = new()
        {
            [None] = "Niente", [Left] = "Clic", [Right] = "Clic destro", [Middle] = "Clic centrale", [Double] = "Doppio clic",
            [Drag] = "Trascina (tasto tenuto)", [Enter] = "Invio", [Escape] = "Esc", [Keyboard] = "Apri/chiudi la tastiera",
            [Dictation] = "Dettatura", [Shortcuts] = "Pannello scorciatoie", [Move] = "Muovi il puntatore", [Scroll] = "Scorri (rotella)",
        };

        /// <summary>Azioni che ha senso dare a ogni tipo di gesto.</summary>
        public static string[] For(bool hold, bool slide) =>
            slide ? new[] { Move, Scroll, None }
            : hold ? new[] { Drag, Shortcuts, Right, Left, Middle, Double, Enter, Escape, Keyboard, Dictation, None }
            : new[] { Left, Right, Middle, Double, Enter, Escape, Keyboard, Dictation, Shortcuts, None };
    }

    public static class TouchPresets
    {
        public static readonly Dictionary<string, string> Names = new()
        {
            ["sofatap"] = "Sofatap: doppio tocco a destra = clic destro, pressione = scorciatoie",
            ["touchpad"] = "Abitudini da touchpad: due dita = clic destro, pressione = trascina",
            ["scorciatoie"] = "Pressione per il clic destro: due dita = Invio, tre dita = tastiera",
            ["classico"] = "Come la 0.4: dito fermo = clic destro, due tocchi e trascina",
        };

        public static Dictionary<string, string> Get(string preset)
        {
            var m = new Dictionary<string, string>
            {
                [TouchGestures.Tap] = TouchActions.Left,
                [TouchGestures.Slide1] = TouchActions.Move,
                [TouchGestures.Slide2] = TouchActions.Scroll,
                [TouchGestures.TapHold] = TouchActions.Drag,
            };
            switch (preset)
            {
                case "touchpad":
                    m[TouchGestures.DoubleLeft] = TouchActions.Double; m[TouchGestures.DoubleRight] = TouchActions.Double;
                    m[TouchGestures.TwoTap] = TouchActions.Right; m[TouchGestures.ThreeTap] = TouchActions.Middle;
                    m[TouchGestures.LongPress] = TouchActions.Drag;
                    break;
                case "scorciatoie":
                    m[TouchGestures.DoubleLeft] = TouchActions.Double; m[TouchGestures.DoubleRight] = TouchActions.Double;
                    m[TouchGestures.TwoTap] = TouchActions.Enter; m[TouchGestures.ThreeTap] = TouchActions.Keyboard;
                    m[TouchGestures.LongPress] = TouchActions.Right;
                    break;
                case "classico":
                    m[TouchGestures.DoubleLeft] = TouchActions.Double; m[TouchGestures.DoubleRight] = TouchActions.Double;
                    m[TouchGestures.TwoTap] = TouchActions.Right; m[TouchGestures.ThreeTap] = TouchActions.Keyboard;
                    m[TouchGestures.LongPress] = TouchActions.Right;
                    break;
                default:   // sofatap
                    m[TouchGestures.DoubleLeft] = TouchActions.Double; m[TouchGestures.DoubleRight] = TouchActions.Right;
                    m[TouchGestures.TwoTap] = TouchActions.Right; m[TouchGestures.ThreeTap] = TouchActions.Keyboard;
                    m[TouchGestures.LongPress] = TouchActions.Shortcuts;
                    break;
            }
            return m;
        }
    }

    /// <summary>
    /// Tasti speciali per la barra sopra la tastiera del telefono e per il pannello scorciatoie.
    /// Keys = codici evdev premuti insieme; Repeat = si ripete tenendolo premuto.
    /// </summary>
    public sealed record SpecialKey(string Id, string Label, int[] Keys, bool Repeat = false, string Kind = "");

    public static class SpecialKeys
    {
        // codici evdev (vedi Input.KeyMap)
        private const int Ctrl = 29, Alt = 56, Shift = 42, Meta = 125;

        public static readonly SpecialKey[] All =
        {
            new("ctrl", "Ctrl", new[] { Ctrl }, false, "mod"),
            new("alt", "Alt", new[] { Alt }, false, "mod"),
            new("win", "Win", new[] { Meta }, false, "mod"),
            new("shift", "Maiusc", new[] { Shift }, false, "mod"),
            new("esc", "Esc", new[] { 1 }),
            new("tab", "Tab", new[] { 15 }, true),
            new("left", "←", new[] { 105 }, true),
            new("up", "↑", new[] { 103 }, true),
            new("down", "↓", new[] { 108 }, true),
            new("right", "→", new[] { 106 }, true),
            new("bksp", "⌫", new[] { 14 }, true),
            new("del", "Canc", new[] { 111 }, true),
            new("enter", "Invio", new[] { 28 }, true),
            new("space", "Spazio", new[] { 57 }, true),
            new("home", "Home", new[] { 102 }),
            new("end", "Fine", new[] { 107 }),
            new("pgup", "Pag↑", new[] { 104 }, true),
            new("pgdn", "Pag↓", new[] { 109 }, true),
            new("ins", "Ins", new[] { 110 }),
            new("prtsc", "Stamp", new[] { 99 }),
            new("f1", "F1", new[] { 59 }), new("f2", "F2", new[] { 60 }), new("f3", "F3", new[] { 61 }), new("f4", "F4", new[] { 62 }),
            new("f5", "F5", new[] { 63 }), new("f6", "F6", new[] { 64 }), new("f7", "F7", new[] { 65 }), new("f8", "F8", new[] { 66 }),
            new("f9", "F9", new[] { 67 }), new("f10", "F10", new[] { 68 }), new("f11", "F11", new[] { 87 }), new("f12", "F12", new[] { 88 }),
            new("copy", "Copia", new[] { Ctrl, 46 }, false, "combo"),
            new("paste", "Incolla", new[] { Ctrl, 47 }, false, "combo"),
            new("cut", "Taglia", new[] { Ctrl, 45 }, false, "combo"),
            new("undo", "Annulla", new[] { Ctrl, 44 }, true, "combo"),
            new("selall", "Seleziona tutto", new[] { Ctrl, 30 }, false, "combo"),
            new("alttab", "Alt+Tab", new[] { Alt, 15 }, false, "combo"),
            new("altf4", "Alt+F4", new[] { Alt, 62 }, false, "combo"),
            new("volup", "Volume +", Array.Empty<int>(), true, "vol+"),
            new("voldn", "Volume −", Array.Empty<int>(), true, "vol-"),
            new("mute", "Muto", Array.Empty<int>(), false, "mute"),
            new("dict", "🎤 Detta", Array.Empty<int>(), false, "dict"),
        };

        public static readonly Dictionary<string, SpecialKey> ById = All.ToDictionary(k => k.Id);

        public static readonly string[] Default = { "ctrl", "alt", "win", "shift", "esc", "tab", "left", "up", "down", "right", "del", "home", "end" };

        /// <summary>Tasti del pannello scorciatoie (pressione lunga), nell'ordine in cui scorrono.</summary>
        public static readonly string[] Panel = { "space", "enter", "esc", "bksp", "left", "right", "up", "down", "tab", "copy", "paste", "undo", "selall", "alttab", "volup", "voldn", "mute" };
    }
}

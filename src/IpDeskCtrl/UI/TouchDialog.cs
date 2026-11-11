using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using IpDeskCtrl.Core;

namespace IpDeskCtrl.UI
{
    /// <summary>
    /// Impostazioni del tocco (telefono, tablet, schermi touch): modo del dito, profilo dei gesti e azione di ogni
    /// gesto, tempi, velocità del puntatore, scorrimento, zoom con due dita, tasti della barra sopra la tastiera.
    /// </summary>
    public static class TouchDialog
    {
        public static void Show(AppSettings s, Action? saved = null)
        {
            var t = s.TouchInput;
            t.Normalize();

            var mode = new ComboBox { ItemsSource = new[] { "Touchpad: il dito sposta il puntatore", "Tocco diretto: si clicca dove si tocca" }, SelectedIndex = t.Mode == "diretto" ? 1 : 0 };

            var presetKeys = TouchPresets.Names.Keys.ToList();
            var preset = new ComboBox { ItemsSource = presetKeys.Select(k => TouchPresets.Names[k]).ToList(), SelectedIndex = Math.Max(0, presetKeys.IndexOf(t.Preset)) };

            // un menu per ogni gesto
            var map = new Dictionary<string, string>(t.Map);
            var combos = new Dictionary<string, (ComboBox box, string[] actions)>();
            var gestures = new StackPanel { Spacing = 6 };
            foreach (var (key, label, isHold, isSlide) in TouchGestures.All)
            {
                var actions = TouchActions.For(isHold, isSlide);
                var box = new ComboBox { ItemsSource = actions.Select(a => TouchActions.Labels[a]).ToList(), MinWidth = 210 };
                combos[key] = (box, actions);
                gestures.Children.Add(new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    Children =
                    {
                        new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap },
                        WithColumn(box, 1)
                    }
                });
            }
            bool loading = false;
            void Load()
            {
                loading = true;
                foreach (var (key, (box, actions)) in combos)
                {
                    string a = map.TryGetValue(key, out var v) ? v : TouchActions.None;
                    box.SelectedIndex = Math.Max(0, Array.IndexOf(actions, a));
                }
                loading = false;
            }
            Load();
            foreach (var (key, (box, actions)) in combos)
            {
                string k = key;
                var acts = actions;
                box.SelectionChanged += (_, _) => { if (!loading && box.SelectedIndex >= 0) map[k] = acts[box.SelectedIndex]; };
            }

            var hold = MakeSlider(200, 1200, t.HoldMs, 50);
            var holdText = new TextBlock { MinWidth = 70 };
            var dbl = MakeSlider(150, 500, t.DoubleTapMs, 25);
            var dblText = new TextBlock { MinWidth = 70 };
            var speed = MakeSlider(0.25, 3, t.PointerSpeed, 0.05);
            var speedText = new TextBlock { MinWidth = 70 };
            void Texts()
            {
                holdText.Text = $"{hold.Value:F0} ms";
                dblText.Text = $"{dbl.Value:F0} ms";
                speedText.Text = $"{speed.Value:F2}×";
            }
            hold.ValueChanged += (_, _) => Texts();
            dbl.ValueChanged += (_, _) => Texts();
            speed.ValueChanged += (_, _) => Texts();
            Texts();

            preset.SelectionChanged += (_, _) =>
            {
                if (preset.SelectedIndex < 0) return;
                var tmp = new TouchSettings();
                tmp.ApplyPreset(presetKeys[preset.SelectedIndex]);
                map = new Dictionary<string, string>(tmp.Map);
                hold.Value = tmp.HoldMs;
                dbl.Value = tmp.DoubleTapMs;
                Load();
            };

            var natural = new CheckBox { Content = "Scorrimento naturale (dita in giù = si torna su, come sul telefono)", IsChecked = t.NaturalScroll };
            var strip = new CheckBox { Content = "Striscia di scorrimento lungo il bordo destro", IsChecked = t.ScrollStrip };
            var pinch = new CheckBox { Content = "Zoom con due dita e spostamento dell'immagine ingrandita", IsChecked = t.PinchZoom };

            // tasti della barra sopra la tastiera del telefono
            var keys = new WrapPanel();
            var checks = new List<(CheckBox box, string id)>();
            foreach (var k in SpecialKeys.All)
            {
                if (k.Kind == "dict" && Platform.Dictate == null) continue;
                var c = new CheckBox { Content = k.Label, IsChecked = t.Keys.Contains(k.Id), Margin = new Thickness(0, 0, 12, 4), MinWidth = 90 };
                checks.Add((c, k.Id));
                keys.Children.Add(c);
            }

            var save = Ui.Btn("Salva", true);
            var reset = Ui.Btn("Profilo pronto");
            var cancel = Ui.Btn("Annulla");
            var content = new ScrollViewer
            {
                MaxHeight = 640,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Content = Ui.Col(10,
                    Ui.H1("Tocco e gesti"),
                    Ui.Sub("Valgono quando comandi un PC dal telefono, dal tablet o da uno schermo touch."),
                    Ui.Lbl("MODO DEL DITO"), mode,
                    Ui.Lbl("PROFILO"), preset,
                    Ui.Lbl("GESTI"), gestures,
                    Ui.Sub("Pannello scorciatoie: tieni il dito, scorri a destra o a sinistra per scegliere il tasto e solleva per mandarlo; scorri in su o in giù per annullare. Se lo apri con un tocco, scegli il tasto toccandolo.", 12),
                    Ui.Lbl("TEMPI E VELOCITÀ"),
                    Row("Pressione lunga", hold, holdText),
                    Row("Intervallo del doppio tocco", dbl, dblText),
                    Row("Velocità del puntatore", speed, speedText),
                    natural, strip, pinch,
                    Ui.Lbl("BARRA DEI TASTI SOPRA LA TASTIERA"),
                    Ui.Sub("Frecce, Canc, Invio, Tab, Pag↑/Pag↓, Annulla e il volume si ripetono tenendoli premuti.", 12),
                    keys,
                    Ui.Row(10, save, reset, cancel))
            };
            var h = Shell.Current.ShowDialog("IP-DESK-CTRL · tocco e gesti", content, 560, false);
            cancel.Click += (_, _) => h.Close();
            reset.Click += (_, _) =>
            {
                var tmp = new TouchSettings();
                tmp.ApplyPreset(presetKeys[Math.Max(0, preset.SelectedIndex)]);
                map = new Dictionary<string, string>(tmp.Map);
                hold.Value = tmp.HoldMs;
                dbl.Value = tmp.DoubleTapMs;
                Load();
            };
            save.Click += (_, _) =>
            {
                t.Mode = mode.SelectedIndex == 1 ? "diretto" : "touchpad";
                t.Preset = presetKeys[Math.Max(0, preset.SelectedIndex)];
                t.Map = map;
                t.HoldMs = (int)Math.Round(hold.Value);
                t.DoubleTapMs = (int)Math.Round(dbl.Value);
                t.PointerSpeed = Math.Round(speed.Value, 2);
                t.NaturalScroll = natural.IsChecked == true;
                t.ScrollStrip = strip.IsChecked == true;
                t.PinchZoom = pinch.IsChecked == true;
                t.Keys = checks.Where(c => c.box.IsChecked == true).Select(c => c.id).ToList();
                t.Normalize();
                s.Save();
                h.Close();
                saved?.Invoke();
            };
        }

        private static Slider MakeSlider(double min, double max, double value, double step) =>
            new() { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), SmallChange = step, LargeChange = step * 4, TickFrequency = step, IsSnapToTickEnabled = true, MinWidth = 180 };

        private static Control Row(string label, Control slider, Control value) =>
            new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,2*,Auto"),
                Children =
                {
                    new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap },
                    WithColumn(slider, 1),
                    WithColumn(value, 2)
                }
            };

        private static Control WithColumn(Control c, int col)
        {
            Grid.SetColumn(c, col);
            c.VerticalAlignment = VerticalAlignment.Center;
            return c;
        }
    }
}

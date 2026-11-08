using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace DeskMeter
{
    // Notes パネルの中身を編集する小さなダイアログ。メモは 1 行 1 件、カウントダウンは「日付 名前」を 1 行 1 件
    sealed class NotesDialog : Window
    {
        readonly Settings cfg;
        readonly TextBox notes = new TextBox();
        readonly TextBox countdowns = new TextBox();
        readonly TextBlock status = new TextBlock();

        public NotesDialog(Settings cfg)
        {
            this.cfg = cfg;
            Title = "メモとカウントダウン";
            Width = 460;
            Height = 480;
            MinWidth = 380;
            MinHeight = 360;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.CanResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontSize = 13;

            var root = new Grid();
            root.Margin = new Thickness(12);
            for (int i = 0; i < 6; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
            root.RowDefinitions[3].Height = new GridLength(1, GridUnitType.Star);
            Content = root;

            Put(root, Label("メモ (1 行に 1 件。Notes パネルにそのまま出ます)"), 0);
            notes.AcceptsReturn = true;
            notes.TextWrapping = TextWrapping.NoWrap;
            notes.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            notes.Margin = new Thickness(0, 0, 0, 10);
            notes.Text = string.Join(Environment.NewLine, cfg.Notes.ToArray());
            Put(root, notes, 1);

            Put(root, Label("カウントダウン (1 行に 1 件、「2026-12-24 旅行」のように日付と名前)"), 2);
            countdowns.AcceptsReturn = true;
            countdowns.TextWrapping = TextWrapping.NoWrap;
            countdowns.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            countdowns.Margin = new Thickness(0, 0, 0, 6);
            var sb = new StringBuilder();
            foreach (Settings.Countdown c in cfg.Countdowns) sb.AppendLine((c.Date ?? "").Trim() + " " + (c.Name ?? "").Trim());
            countdowns.Text = sb.ToString().TrimEnd();
            Put(root, countdowns, 3);

            status.Foreground = SystemColors.GrayTextBrush;
            status.TextWrapping = TextWrapping.Wrap;
            status.Margin = new Thickness(0, 0, 0, 8);
            status.Text = "Notes パネルが「表示する項目」でオフのときは、保存すると自動でオンになります。";
            Put(root, status, 4);

            var buttons = new StackPanel();
            buttons.Orientation = Orientation.Horizontal;
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            var ok = new Button();
            ok.Content = "OK";
            ok.Width = 84;
            ok.IsDefault = false;   // Enter は改行に使う
            ok.Click += delegate { Accept(); };
            var cancel = new Button();
            cancel.Content = "キャンセル";
            cancel.Width = 84;
            cancel.Margin = new Thickness(8, 0, 0, 0);
            cancel.IsCancel = true;
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            Put(root, buttons, 5);

            Loaded += delegate { notes.Focus(); };
        }

        static TextBlock Label(string text)
        {
            var t = new TextBlock();
            t.Text = text;
            t.TextWrapping = TextWrapping.Wrap;
            t.Margin = new Thickness(0, 0, 0, 4);
            return t;
        }

        static void Put(Grid g, UIElement e, int row)
        {
            Grid.SetRow(e, row);
            g.Children.Add(e);
        }

        void Accept()
        {
            var n = new List<string>();
            foreach (string line in notes.Text.Split('\n'))
            {
                string t = line.TrimEnd('\r').Trim();
                if (t.Length > 0) n.Add(t);
            }
            var cds = new List<Settings.Countdown>();
            foreach (string line in countdowns.Text.Split('\n'))
            {
                string t = line.TrimEnd('\r').Trim();
                if (t.Length == 0) continue;
                int sp = t.IndexOfAny(new[] { ' ', '\t', '\u3000' });   // 半角・タブ・全角スペースのどれでも
                string date = sp < 0 ? t : t.Substring(0, sp);
                string name = sp < 0 ? "" : t.Substring(sp + 1).Trim();
                DateTime d;
                if (!DateTime.TryParseExact(date.Replace('/', '-'), new[] { "yyyy-MM-dd", "yyyy-M-d" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                {
                    status.Text = "日付は「2026-12-24 名前」の形で書いてください: " + t;
                    status.Foreground = SystemColors.ControlTextBrush;
                    countdowns.Focus();
                    return;
                }
                var c = new Settings.Countdown();
                c.Date = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                c.Name = name;
                cds.Add(c);
            }
            cfg.Notes = n;
            cfg.Countdowns = cds;
            DialogResult = true;
            Close();
        }
    }
}

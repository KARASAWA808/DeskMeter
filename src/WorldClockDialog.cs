using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace DeskMeter
{
    // 時計パネルに出す世界時計を選ぶダイアログ。Windows のタイムゾーン一覧から選んで表示名を付ける
    sealed class WorldClockDialog : Window
    {
        readonly Settings cfg;
        readonly ComboBox zones = new ComboBox();
        readonly TextBox label = new TextBox();
        readonly ListBox list = new ListBox();
        readonly List<string> entries = new List<string>();   // "ID|表示名"

        public WorldClockDialog(Settings cfg)
        {
            this.cfg = cfg;
            Title = "世界時計";
            Width = 480;
            Height = 420;
            MinWidth = 400;
            MinHeight = 320;
            Topmost = true;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontSize = 13;

            var root = new Grid();
            root.Margin = new Thickness(12);
            for (int i = 0; i < 5; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Star);
            Content = root;

            var hint = new TextBlock();
            hint.Text = "タイムゾーンを選び、表示名を付けて「追加」を押してください。時計パネルの日付の下に並びます。";
            hint.TextWrapping = TextWrapping.Wrap;
            hint.Margin = new Thickness(0, 0, 0, 8);
            Put(root, hint, 0);

            var row = new DockPanel();
            var add = new Button();
            add.Content = "追加";
            add.Width = 72;
            add.Margin = new Thickness(6, 0, 0, 0);
            add.Click += delegate { Add(); };
            DockPanel.SetDock(add, Dock.Right);
            row.Children.Add(add);
            label.Width = 120;
            label.Margin = new Thickness(6, 0, 0, 0);
            DockPanel.SetDock(label, Dock.Right);
            row.Children.Add(label);
            foreach (TimeZoneInfo tz in TimeZoneInfo.GetSystemTimeZones()) zones.Items.Add(tz);
            zones.DisplayMemberPath = "DisplayName";
            zones.SelectionChanged += delegate
            {
                var tz = zones.SelectedItem as TimeZoneInfo;
                if (tz != null && label.Text.Trim().Length == 0) label.Text = ShortName(tz);
            };
            row.Children.Add(zones);
            row.Margin = new Thickness(0, 0, 0, 6);
            Put(root, row, 1);

            entries.AddRange(cfg.WorldClocks);
            RefreshList();
            list.Margin = new Thickness(0, 0, 0, 6);
            Put(root, list, 2);

            var remove = new Button();
            remove.Content = "選んだものを削除";
            remove.HorizontalAlignment = HorizontalAlignment.Left;
            remove.Margin = new Thickness(0, 0, 0, 10);
            remove.Click += delegate
            {
                if (list.SelectedIndex >= 0 && list.SelectedIndex < entries.Count) { entries.RemoveAt(list.SelectedIndex); RefreshList(); }
            };
            Put(root, remove, 3);

            var buttons = new StackPanel();
            buttons.Orientation = Orientation.Horizontal;
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            var ok = new Button();
            ok.Content = "OK";
            ok.Width = 84;
            ok.IsDefault = true;
            ok.Click += delegate { cfg.WorldClocks = new List<string>(entries); DialogResult = true; Close(); };
            var cancel = new Button();
            cancel.Content = "キャンセル";
            cancel.Width = 84;
            cancel.Margin = new Thickness(8, 0, 0, 0);
            cancel.IsCancel = true;
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            Put(root, buttons, 4);
        }

        // "(UTC-05:00) 東部標準時 (米国およびカナダ)" のような表示名から、短い名前を作る
        static string ShortName(TimeZoneInfo tz)
        {
            string d = tz.DisplayName;
            int close = d.IndexOf(')');
            if (d.StartsWith("(") && close > 0) d = d.Substring(close + 1).Trim();
            int paren = d.IndexOf(" (");
            if (paren > 0) d = d.Substring(0, paren);
            return d.Length > 16 ? d.Substring(0, 16) : d;
        }

        void Add()
        {
            var tz = zones.SelectedItem as TimeZoneInfo;
            if (tz == null) return;
            string name = label.Text.Trim();
            if (name.Length == 0) name = ShortName(tz);
            entries.Add(tz.Id + "|" + name.Replace('|', ' '));
            label.Text = "";
            RefreshList();
        }

        void RefreshList()
        {
            list.Items.Clear();
            foreach (string e in entries)
            {
                int bar = e.IndexOf('|');
                string id = bar >= 0 ? e.Substring(0, bar) : e, name = bar >= 0 ? e.Substring(bar + 1) : e;
                string now = "";
                try { now = TimeZoneInfo.ConvertTime(DateTime.Now, TimeZoneInfo.FindSystemTimeZoneById(id)).ToString("HH:mm"); }
                catch { now = "ID が不明"; }
                list.Items.Add(name + "  (" + id + ")  " + now);
            }
        }

        static void Put(Grid g, UIElement e, int row)
        {
            Grid.SetRow(e, row);
            g.Children.Add(e);
        }
    }
}

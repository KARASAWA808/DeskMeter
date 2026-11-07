using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DeskMeter
{
    // 天気の場所を設定するダイアログ。地名で検索 (Open-Meteo ジオコーディング) するか、緯度・経度を直接入力する。
    // 本体はツールウィンドウなので Owner は設定しない。OK で Settings に書き込み、DialogResult = true を返す。
    sealed class LocationDialog : Window
    {
        sealed class Place
        {
            public string Name, Admin1, Country;
            public double Lat, Lon;

            public string Display
            {
                get
                {
                    string a = string.IsNullOrEmpty(Admin1) ? "" : Admin1 + " ";
                    return a + Name;
                }
            }

            public override string ToString()
            {
                return string.Format(CultureInfo.InvariantCulture, "{0}{1}  ({2}, {3:0.###}, {4:0.###})",
                    string.IsNullOrEmpty(Admin1) ? "" : Admin1 + " ", Name, Country, Lat, Lon);
            }
        }

        readonly Settings cfg;
        readonly TextBox query = new TextBox();
        readonly Button searchBtn = new Button();
        readonly ListBox list = new ListBox();
        readonly TextBox latBox = new TextBox(), lonBox = new TextBox(), nameBox = new TextBox();
        readonly CheckBox disabled = new CheckBox();
        readonly TextBlock status = new TextBlock();
        int searchSeq;

        public LocationDialog(Settings cfg)
        {
            this.cfg = cfg;
            Title = "天気の場所を設定";
            Width = 460;
            Height = 470;
            MinWidth = 380;
            MinHeight = 400;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.CanResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontSize = 13;

            var root = new Grid();
            root.Margin = new Thickness(12);
            for (int i = 0; i < 7; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Star);   // 候補一覧が伸びる
            Content = root;

            // 1行目: 説明
            var hint = new TextBlock();
            hint.Text = "地名を入力して検索し、候補を選ぶか、緯度・経度を直接入力してください。";
            hint.TextWrapping = TextWrapping.Wrap;
            hint.Margin = new Thickness(0, 0, 0, 8);
            Put(root, hint, 0);

            // 2行目: 入力欄 + 検索ボタン
            var row = new DockPanel();
            searchBtn.Content = "検索";
            searchBtn.Width = 72;
            searchBtn.Margin = new Thickness(6, 0, 0, 0);
            searchBtn.Click += delegate { Search(); };
            DockPanel.SetDock(searchBtn, Dock.Right);
            row.Children.Add(searchBtn);
            query.Text = "";
            query.KeyDown += delegate(object s, System.Windows.Input.KeyEventArgs e)
            {
                if (e.Key == System.Windows.Input.Key.Enter) { Search(); e.Handled = true; }
            };
            row.Children.Add(query);
            row.Margin = new Thickness(0, 0, 0, 6);
            Put(root, row, 1);

            // 3行目: 候補一覧
            list.Margin = new Thickness(0, 0, 0, 6);
            list.MinHeight = 120;
            list.SelectionChanged += delegate
            {
                var p = list.SelectedItem as Place;
                if (p == null) return;
                latBox.Text = p.Lat.ToString("0.####", CultureInfo.InvariantCulture);
                lonBox.Text = p.Lon.ToString("0.####", CultureInfo.InvariantCulture);
                nameBox.Text = p.Display;
            };
            Put(root, list, 2);

            // 4行目: 状態表示
            status.Margin = new Thickness(0, 0, 0, 6);
            status.Foreground = SystemColors.GrayTextBrush;
            Put(root, status, 3);

            // 5行目: 緯度・経度・表示名
            var form = new Grid();
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddLabel(form, "緯度:", 0, 0);
            latBox.Text = cfg.Latitude.ToString("0.####", CultureInfo.InvariantCulture);
            AddField(form, latBox, 0, 1);
            AddLabel(form, "経度:", 0, 2);
            lonBox.Text = cfg.Longitude.ToString("0.####", CultureInfo.InvariantCulture);
            AddField(form, lonBox, 0, 3);
            AddLabel(form, "表示名:", 1, 0);
            nameBox.Text = cfg.LocationName ?? "";
            AddField(form, nameBox, 1, 1);
            Grid.SetColumnSpan(nameBox, 3);
            Put(root, form, 4);

            // 6行目: 無効化チェック
            disabled.Content = "天気を取得しない (通信しない)";
            disabled.IsChecked = !cfg.WeatherEnabled;
            disabled.Margin = new Thickness(0, 8, 0, 10);
            Put(root, disabled, 5);

            // 7行目: OK / キャンセル
            var buttons = new StackPanel();
            buttons.Orientation = Orientation.Horizontal;
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            var ok = new Button();
            ok.Content = "OK";
            ok.Width = 84;
            ok.IsDefault = false;   // Enter は検索に使う
            ok.Click += delegate { Accept(); };
            var cancel = new Button();
            cancel.Content = "キャンセル";
            cancel.Width = 84;
            cancel.Margin = new Thickness(8, 0, 0, 0);
            cancel.IsCancel = true;
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            Put(root, buttons, 6);

            Loaded += delegate { query.Focus(); };
        }

        static void Put(Grid g, UIElement e, int row)
        {
            Grid.SetRow(e, row);
            g.Children.Add(e);
        }

        static void AddLabel(Grid g, string text, int row, int col)
        {
            var t = new TextBlock();
            t.Text = text;
            t.VerticalAlignment = VerticalAlignment.Center;
            t.Margin = new Thickness(0, 0, 6, 4);
            Grid.SetRow(t, row);
            Grid.SetColumn(t, col);
            g.Children.Add(t);
        }

        static void AddField(Grid g, TextBox box, int row, int col)
        {
            box.Margin = new Thickness(0, 0, 10, 4);
            Grid.SetRow(box, row);
            Grid.SetColumn(box, col);
            g.Children.Add(box);
        }

        // ---------------------------------------------------------------- search (thread pool)

        void Search()
        {
            string q = query.Text.Trim();
            if (q.Length == 0) { status.Text = "地名を入力してください。"; return; }
            int seq = Interlocked.Increment(ref searchSeq);
            status.Text = "検索中...";
            searchBtn.IsEnabled = false;
            Dispatcher disp = Dispatcher;
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<Place> found = null;
                string error = null;
                try { found = Geocode(q); }
                catch (Exception ex) { error = ex.Message; }
                disp.BeginInvoke(new Action(delegate
                {
                    if (seq != searchSeq) return;   // 古い検索の結果は捨てる
                    searchBtn.IsEnabled = true;
                    list.Items.Clear();
                    if (error != null) { status.Text = "検索できません: " + error; return; }
                    foreach (Place p in found) list.Items.Add(p);
                    status.Text = found.Count == 0 ? "見つかりませんでした。" : found.Count + " 件の候補。選ぶと緯度・経度に入ります。";
                }));
            });
        }

        static List<Place> Geocode(string q)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            string url = "https://geocoding-api.open-meteo.com/v1/search?name=" + Uri.EscapeDataString(q) + "&language=ja&count=10";
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = 10000;
            req.UserAgent = "DeskMeter/1.0";
            string json;
            using (WebResponse resp = req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                json = sr.ReadToEnd();

            var result = new List<Place>();
            var root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            object arr;
            if (root == null || !root.TryGetValue("results", out arr)) return result;
            object[] items = arr as object[];
            if (items == null) return result;
            foreach (object o in items)
            {
                var d = o as Dictionary<string, object>;
                if (d == null) continue;
                var p = new Place();
                p.Name = Str(d, "name");
                p.Admin1 = Str(d, "admin1");
                p.Country = Str(d, "country");
                p.Lat = Num(d, "latitude");
                p.Lon = Num(d, "longitude");
                if (p.Name.Length == 0 || double.IsNaN(p.Lat) || double.IsNaN(p.Lon)) continue;
                result.Add(p);
            }
            return result;
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            object o;
            return d.TryGetValue(key, out o) && o != null ? Convert.ToString(o, CultureInfo.InvariantCulture) : "";
        }

        static double Num(Dictionary<string, object> d, string key)
        {
            object o;
            if (!d.TryGetValue(key, out o) || o == null) return double.NaN;
            try { return Convert.ToDouble(o, CultureInfo.InvariantCulture); }
            catch { return double.NaN; }
        }

        // ---------------------------------------------------------------- accept

        void Accept()
        {
            double lat, lon;
            if (!double.TryParse(latBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out lat) || lat < -90 || lat > 90)
            {
                status.Text = "緯度は -90〜90 の数値で入力してください。";
                latBox.Focus();
                return;
            }
            if (!double.TryParse(lonBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out lon) || lon < -180 || lon > 180)
            {
                status.Text = "経度は -180〜180 の数値で入力してください。";
                lonBox.Focus();
                return;
            }
            cfg.Latitude = lat;
            cfg.Longitude = lon;
            cfg.LocationName = nameBox.Text.Trim();
            cfg.WeatherEnabled = disabled.IsChecked != true;
            DialogResult = true;
            Close();
        }
    }
}

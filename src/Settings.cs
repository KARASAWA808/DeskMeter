using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace DeskMeter
{
    public class Settings
    {
        // ウィンドウ位置: どのモニターの、どの端から何ピクセルか (モニター構成が変わっても追従できる)
        public string Monitor;            // 例 "\\.\DISPLAY1"。無い/見つからないときはメインモニター
        public string AnchorH = "right";  // "left" / "right"
        public string AnchorV = "top";    // "top" / "bottom"
        public int MonX = 8;              // 基準の端からの距離 (ピクセル)
        public int MonY = 8;
        // 旧形式 (仮想スクリーン座標)。読み込み時に上の形式へ変換する
        public double? Left;
        public double? Top;
        // 計測間隔 (ミリ秒)。250〜10000
        public int IntervalMs = 1000;
        // グラフの時間幅 (秒)。30〜1800。更新間隔を変えてもグラフが表す時間は変わらない
        public int HistorySeconds = 120;
        // 表示/非表示のグローバルホットキー (例 "Ctrl+Alt+D")。空文字で無効
        public string Hotkey = "";
        // スキン名: Classic / Cyberpunk / Retro / Light
        public string Skin = "Classic";
        // 表示倍率 (1.0 = 100%)
        public double Scale = 1.0;
        // 背景の不透明度 0.0 - 1.0
        public double BackgroundOpacity = 0.82;
        // "desktop" = デスクトップに固定 / "topmost" = 常に最前面 / "normal" = 通常
        public string Mode = "desktop";
        public bool Locked = false;
        public bool ClickThrough = false;

        // パネルの配置: 列ごとに上から順にパネルIDを並べる
        // ID: gpu, network, drives, topmem, cpu, topcpu, clock, calendar, datetile, weather
        public List<List<string>> Layout;
        // コアごとの使用率を2列に詰めて表示する
        public bool CompactCores = false;

        public bool ShowIp = true;
        // Ping の宛先 (空文字で無効)
        public string PingHost = "1.1.1.1";
        // 天気 (Open-Meteo, APIキー不要)。WeatherEnabled = false で通信しない
        public bool WeatherEnabled = true;
        public double Latitude = 35.08;
        public double Longitude = 137.16;
        public string LocationName = "愛知県 豊田市";

        public static string FilePath
        {
            get { return Path.Combine(AppPaths.Dir, "settings.json"); }
        }

        // 起動時の読み込み。ファイルが壊れていたら settings.broken-<日時>.json に退避してから既定値で動く
        public static Settings Load()
        {
            bool broken;
            Settings s = TryLoad(out broken);
            if (s != null) return s;
            if (broken) BackupBroken();
            var fresh = new Settings();
            fresh.Normalize();
            return fresh;
        }

        // ファイルを読んで返す。無ければ null (broken = false)、パースに失敗したら null (broken = true)
        static Settings TryLoad(out bool broken)
        {
            broken = false;
            try
            {
                if (!File.Exists(FilePath)) return null;
                var s = new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(FilePath, Encoding.UTF8));
                if (s == null) { broken = true; return null; }
                s.Normalize();
                return s;
            }
            catch (Exception ex)
            {
                Log.Write("Settings.Load", ex);
                broken = true;
                return null;
            }
        }

        static void BackupBroken()
        {
            try
            {
                string dest = Path.Combine(AppPaths.Dir, "settings.broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
                File.Copy(FilePath, dest, true);
                Log.Write("Settings.Load", new InvalidOperationException("settings.json が読めないので退避しました: " + dest));
            }
            catch (Exception ex) { Log.Write("Settings.Backup", ex); }
        }

        public static readonly string[] PanelIds = { "gpu", "network", "drives", "topmem", "cpu", "topcpu", "clock", "calendar", "datetile", "weather" };
        public const int MaxColumns = 6;

        public static string PanelName(string id)
        {
            switch (id)
            {
                case "gpu": return "GPU";
                case "network": return "ネットワーク";
                case "drives": return "ドライブ";
                case "topmem": return "メモリ上位プロセス";
                case "cpu": return "CPU・メモリ";
                case "topcpu": return "CPU上位プロセス";
                case "clock": return "時計";
                case "calendar": return "カレンダー";
                case "datetile": return "日付タイル";
                case "weather": return "天気";
                default: return id;
            }
        }

        // 標準の配置 (3列)
        public static List<List<string>> DefaultLayout()
        {
            var l = new List<List<string>>();
            l.Add(new List<string>(new[] { "gpu", "network", "drives", "topmem" }));
            l.Add(new List<string>(new[] { "cpu", "topcpu" }));
            l.Add(new List<string>(new[] { "clock", "calendar", "datetile", "weather" }));
            return l;
        }

        static int DefaultColumn(string id)
        {
            List<List<string>> d = DefaultLayout();
            for (int i = 0; i < d.Count; i++) if (d[i].Contains(id)) return i;
            return d.Count - 1;
        }

        public bool HasPanel(string id)
        {
            foreach (List<string> col in Layout) if (col.Contains(id)) return true;
            return false;
        }

        // 表示/非表示の切り替え。表示するときは標準の列 (無ければ最後の列) の末尾に足す
        public void TogglePanel(string id)
        {
            if (HasPanel(id))
            {
                foreach (List<string> col in Layout) col.Remove(id);
                CleanLayout();
                return;
            }
            if (Layout.Count == 0) Layout.Add(new List<string>());
            int c = Math.Min(DefaultColumn(id), Layout.Count - 1);
            Layout[c].Add(id);
        }

        // 未知のID・重複・空の列を取り除く
        public void CleanLayout()
        {
            if (Layout == null) { Layout = DefaultLayout(); return; }   // 空の配置 (全て非表示) は許す
            var seen = new HashSet<string>();
            var cleaned = new List<List<string>>();
            foreach (List<string> col in Layout)
            {
                if (col == null) continue;
                var c = new List<string>();
                foreach (string id in col)
                    if (id != null && Array.IndexOf(PanelIds, id) >= 0 && seen.Add(id)) c.Add(id);
                if (c.Count == 0) continue;
                // 列数の上限を超えた分は捨てずに最後の列の末尾へ寄せる
                if (cleaned.Count < MaxColumns) cleaned.Add(c);
                else cleaned[cleaned.Count - 1].AddRange(c);
            }
            Layout = cleaned;
        }

        void Normalize()
        {
            CleanLayout();
            if (double.IsNaN(Scale) || Scale < 0.5 || Scale > 3) Scale = 1.0;
            if (double.IsNaN(BackgroundOpacity)) BackgroundOpacity = 0.82;
            BackgroundOpacity = Math.Max(0, Math.Min(1, BackgroundOpacity));
            if (Mode != "topmost" && Mode != "normal") Mode = "desktop";
            if (IntervalMs < 250 || IntervalMs > 10000) IntervalMs = 1000;
            HistorySeconds = Math.Max(30, Math.Min(1800, HistorySeconds));
            if (Hotkey == null) Hotkey = "";
            if (string.IsNullOrEmpty(Skin)) Skin = "Classic";
            if (AnchorH != "left") AnchorH = "right";
            if (AnchorV != "bottom") AnchorV = "top";
            if (PingHost == null) PingHost = "";
            if (LocationName == null) LocationName = "";
        }

        // 設定ファイルを読み直して、このインスタンスに上書きする (他のオブジェクトが同じ参照を持っているため)。
        // パースに失敗したときは何も変えずに false を返す (次の Save で壊れたファイルが既定値に戻らないように)
        public bool Reload()
        {
            bool broken;
            Settings fresh = TryLoad(out broken);
            if (fresh == null)
            {
                if (broken) return false;
                fresh = new Settings();   // ファイルが無いときは既定値
                fresh.Normalize();
            }
            foreach (FieldInfo f in typeof(Settings).GetFields(BindingFlags.Public | BindingFlags.Instance))
                f.SetValue(this, f.GetValue(fresh));
            return true;
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(FilePath, Pretty(new JavaScriptSerializer().Serialize(this)), new UTF8Encoding(false));
            }
            catch (Exception ex) { Log.Write("Settings.Save", ex); }
        }

        static string Pretty(string json)
        {
            var sb = new StringBuilder();
            int indent = 0;
            bool inStr = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (inStr)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < json.Length) sb.Append(json[++i]);
                    else if (c == '"') inStr = false;
                    continue;
                }
                switch (c)
                {
                    case '"': inStr = true; sb.Append(c); break;
                    case '{':
                    case '[':
                        sb.Append(c); sb.AppendLine(); indent++; sb.Append(' ', indent * 2); break;
                    case '}':
                    case ']':
                        sb.AppendLine(); indent--; sb.Append(' ', indent * 2); sb.Append(c); break;
                    case ',':
                        sb.Append(c); sb.AppendLine(); sb.Append(' ', indent * 2); break;
                    case ':': sb.Append(": "); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }
}

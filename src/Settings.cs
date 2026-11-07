using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace DeskMeter
{
    public partial class Settings
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
        // メモリパネルを CPU から分けたときの移行済みフラグ (古い設定に memory パネルを足すため)
        public bool MemoryPanelAdded = false;
        // 文字の影・縁取り: "auto" (背景の不透明度が 50% 未満のとき) / "on" / "off"
        public string TextShadow = "auto";
        // 画像 (絶対パス、または exe のフォルダーからの相対パス)
        public string BackgroundImage = "";
        public double BackgroundImageOpacity = 0.35;   // 0〜1
        public string MascotImage = "";                // 指定するとスキンの手描きマスコットの代わりに描く
        public string MascotPlacement = "free";        // free (空きスペース) / right (右に並べる) / left
        public int MascotSize = 220;                   // 画像の最大の高さ (px)
        // 切り離して独立ウィンドウにしたパネルと、その位置 (モニターの作業領域の左上からのピクセル)
        public List<string> Floating = new List<string>();
        public List<FloatPos> FloatPositions = new List<FloatPos>();
        // スキン名: Classic / Cyberpunk / Retro / Light、または skins\*.json のファイル名 (拡張子なし)
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
        // 天気パネルに明日から3日分の予報を出す
        public bool ShowForecast = true;
        // カレンダーに日本の祝日を表示する
        public bool ShowHolidays = true;

        public static string FilePath
        {
            get { return Path.Combine(AppPaths.Dir, "settings.json"); }
        }

        // 最後に読み書きしたときのファイルの更新時刻。外部 (メモ帳など) で編集されたかの判定に使う
        static long fileStamp;
        // 起動時に壊れたファイルを退避したなら、その退避先 (起動後にトレイで知らせる)
        public static string BrokenBackupPath;

        static void RememberStamp()
        {
            try { fileStamp = File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath).Ticks : 0; }
            catch { fileStamp = 0; }
        }

        // 最後に読み書きしたあとに、ほかのプログラムがファイルを書き換えたか
        public static bool ExternallyModified()
        {
            try
            {
                long now = File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath).Ticks : 0;
                return now != fileStamp;
            }
            catch { return false; }
        }

        // 起動時の読み込み。ファイルが壊れていたら settings.broken-<日時>.json に退避してから既定値で動く
        public static Settings Load()
        {
            bool broken;
            Settings s = TryLoad(out broken);
            if (s != null) return s;
            if (broken) BrokenBackupPath = BackupBroken();
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
                if (!File.Exists(FilePath)) { RememberStamp(); return null; }
                var s = new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(FilePath, Encoding.UTF8));
                RememberStamp();
                if (s == null) { broken = true; return null; }
                s.Normalize();
                if (s.pendingSave) { s.pendingSave = false; s.Save(); }   // 移行で配置を変えたらすぐ保存
                return s;
            }
            catch (Exception ex)
            {
                Log.Write("Settings.Load", ex);
                RememberStamp();
                broken = true;
                return null;
            }
        }

        static string BackupBroken()
        {
            try
            {
                string dest = Path.Combine(AppPaths.Dir, "settings.broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
                File.Copy(FilePath, dest, true);
                Log.Write("Settings.Load", new InvalidOperationException("settings.json が読めないので退避しました: " + dest));
                return dest;
            }
            catch (Exception ex) { Log.Write("Settings.Backup", ex); return null; }
        }

        public static readonly string[] PanelIds = { "gpu", "network", "drives", "topmem", "cpu", "topcpu", "clock", "calendar", "datetile", "weather", "rings", "memory" };
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
                case "rings": return "リングゲージ";
                case "memory": return "メモリ (詳細)";
                default: return id;
            }
        }

        // 標準の配置 (3列)
        public static List<List<string>> DefaultLayout()
        {
            var l = new List<List<string>>();
            l.Add(new List<string>(new[] { "gpu", "network", "drives", "topmem" }));
            l.Add(new List<string>(new[] { "cpu", "memory", "topcpu" }));
            l.Add(new List<string>(new[] { "clock", "calendar", "datetile", "weather" }));
            return l;
        }

        static int DefaultColumn(string id)
        {
            if (id == "rings" || id == "memory") return 1;
            List<List<string>> d = DefaultLayout();
            for (int i = 0; i < d.Count; i++) if (d[i].Contains(id)) return i;
            return d.Count - 1;
        }

        public bool HasPanel(string id)
        {
            if (Floating != null && Floating.Contains(id)) return true;
            foreach (List<string> col in Layout) if (col.Contains(id)) return true;
            return false;
        }

        // 表示/非表示の切り替え。表示するときは標準の列 (無ければ最後の列) の末尾に足す
        public void TogglePanel(string id)
        {
            if (HasPanel(id))
            {
                foreach (List<string> col in Layout) col.Remove(id);
                Floating.Remove(id);
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
            MigrateMemoryPanel();
            if (double.IsNaN(Scale) || Scale < 0.5 || Scale > 3) Scale = 1.0;
            if (double.IsNaN(BackgroundOpacity)) BackgroundOpacity = 0.82;
            BackgroundOpacity = Math.Max(0, Math.Min(1, BackgroundOpacity));
            if (Mode != "topmost" && Mode != "normal") Mode = "desktop";
            if (IntervalMs < 250 || IntervalMs > 10000) IntervalMs = 1000;
            HistorySeconds = Math.Max(30, Math.Min(1800, HistorySeconds));
            // 手編集で変な値が入っても画面外や NaN にならないように
            MonX = Math.Max(0, Math.Min(20000, MonX));
            MonY = Math.Max(0, Math.Min(20000, MonY));
            if (double.IsNaN(Latitude) || Latitude < -90 || Latitude > 90) Latitude = 35.08;
            if (double.IsNaN(Longitude) || Longitude < -180 || Longitude > 180) Longitude = 137.16;
            if (Hotkey == null) Hotkey = "";
            if (string.IsNullOrEmpty(Skin)) Skin = "Classic";
            if (AnchorH != "left") AnchorH = "right";
            if (AnchorV != "bottom") AnchorV = "top";
            if (TextShadow != "on" && TextShadow != "off") TextShadow = "auto";
            if (MascotPlacement != "right" && MascotPlacement != "left") MascotPlacement = "free";
            if (MascotSize < 60 || MascotSize > 1200) MascotSize = 220;
            if (double.IsNaN(BackgroundImageOpacity)) BackgroundImageOpacity = 0.35;
            BackgroundImageOpacity = Math.Max(0, Math.Min(1, BackgroundImageOpacity));
            if (BackgroundImage == null) BackgroundImage = "";
            if (MascotImage == null) MascotImage = "";
            NormalizeFloating();
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

        // 一時ファイルに書いてから差し替える (書き込み途中で落ちても settings.json が途中切れにならない)
        public void Save()
        {
            try
            {
                string json = Pretty(new JavaScriptSerializer().Serialize(this));
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
                RememberStamp();
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

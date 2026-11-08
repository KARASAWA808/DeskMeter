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
        public string MonitorRect;        // そのモニターの位置と大きさ "左,上,幅,高さ"。名前でも ID でも見つからないときの照合用
        public string MonitorId;          // モニター固有の ID (番号の振り直しや配置の入れ替えでも変わらない)。これを最優先で照合する
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
        public double Latitude = 35.68;      // 既定は東京駅付近 (利用者の場所とは無関係な中立の地点)
        public double Longitude = 139.77;
        public string LocationName = "東京都 千代田区";
        // 天気パネルに明日から3日分の予報を出す
        public bool ShowForecast = true;
        // カレンダーに日本の祝日を表示する
        public bool ShowHolidays = true;
        // 時計を 24 時間制にする (false で 12 時間制 + AM/PM)
        public bool Clock24h = true;
        // カレンダーの週を月曜から始める
        public bool WeekStartMonday = false;
        // 天気の単位: "metric" (°C, km/h) / "metric-ms" (°C, m/s) / "imperial" (°F, mph)
        public string WeatherUnits = "metric";
        // 1 分ごとに主な値を deskmeter-日付.csv に追記する
        public bool LogCsv = false;
        // 通知 (トレイのバルーン)。0 で無効
        public int AlertCpuPct = 0;        // CPU 使用率がこの % 以上の状態が AlertSeconds 続いたら
        public int AlertMemPct = 0;        // メモリ使用率 (同上)
        public int AlertDiskFreePct = 0;   // 空きがこの % 未満のドライブがあれば
        public int AlertTempC = 0;         // CPU (HWiNFO) または GPU の温度がこの °C 以上なら
        public int AlertBatteryPct = 0;    // バッテリー駆動中に残量がこの % 以下なら
        public int AlertSeconds = 60;      // CPU・メモリの「続いたら」の長さ (秒)
        // 追加のホットキー (空で無効): 配置の編集の切り替え / クリック透過の切り替え
        public string EditHotkey = "";
        public string ClickThroughHotkey = "";
        // 同じモニターで最大化したウィンドウが前面の間は隠す (最前面・通常モード向け)
        public bool HideWhenMaximized = false;
        // 普段の不透明度 (%)。100 未満にすると薄くなり、マウスを載せている間だけ 100% になる
        public int HoverOpacity = 100;
        // メモ・カウントダウンパネル (notes): メモは 1 行ずつ、カウントダウンは {"Name": "旅行", "Date": "2026-12-24"}
        public List<string> Notes = new List<string>();
        public List<Countdown> Countdowns = new List<Countdown>();
        // 時計パネルに足す世界時計: "Windows のタイムゾーン ID|表示名" (ID は tzutil /l で一覧できる。例 "Eastern Standard Time|New York")
        public List<string> WorldClocks = new List<string>();

        public static string FilePath
        {
            get { return Path.Combine(AppPaths.DataDir, "settings.json"); }
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
        // settings.json が無い状態で起動した (初回)。起動後の案内と、画面に合わせた初期値に使う
        public static bool IsFirstRun;

        public static Settings Load()
        {
            bool broken;
            Settings s = TryLoad(out broken);
            if (s != null) return s;
            if (broken) BrokenBackupPath = BackupBroken();
            else IsFirstRun = true;
            var fresh = new Settings();
            fresh.Normalize();
            return fresh;
        }

        // ファイルを読んで返す。無ければ null (broken = false)、パースに失敗したら null (broken = true)
        // 最後の読み込みで型や形式が合わずに既定値へ戻した項目名 (起動後や再読み込み後にトレイで知らせる)
        public static List<string> LastLoadWarnings = new List<string>();

        static Settings TryLoad(out bool broken)
        {
            broken = false;
            var warnings = new List<string>();
            try
            {
                if (!File.Exists(FilePath)) { RememberStamp(); return null; }
                string json = File.ReadAllText(FilePath, Encoding.UTF8);
                RememberStamp();
                // JSON として読めないときだけ「壊れている」扱い。項目ごとの型違い (数値の欄に文字など) は
                // その項目だけ既定値にして、ほかの設定は残す
                var ser = new JavaScriptSerializer();
                var dict = ser.DeserializeObject(json) as Dictionary<string, object>;
                if (dict == null) { broken = true; return null; }
                lastFileDict = dict;
                var s = new Settings();
                foreach (FieldInfo f in typeof(Settings).GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (f.IsDefined(typeof(ScriptIgnoreAttribute), false)) continue;
                    object v;
                    if (!dict.TryGetValue(f.Name, out v)) continue;
                    try { f.SetValue(s, ser.ConvertToType(v, f.FieldType)); }
                    catch (Exception ex)
                    {
                        warnings.Add(f.Name);
                        Log.Write("Settings." + f.Name + " (既定値を使います)", ex);
                    }
                }
                s.Normalize();
                if (warnings.Count > 0) s.pendingSave = true;   // 直した値で書き戻しておく
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
            finally { LastLoadWarnings = warnings; }
        }

        static string BackupBroken()
        {
            try
            {
                string dest = Path.Combine(AppPaths.DataDir, "settings.broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
                File.Copy(FilePath, dest, true);
                Log.Write("Settings.Load", new InvalidOperationException("settings.json が読めないので退避しました: " + dest));
                return dest;
            }
            catch (Exception ex) { Log.Write("Settings.Backup", ex); return null; }
        }

        public static readonly string[] PanelIds = { "gpu", "network", "drives", "topmem", "cpu", "topcpu", "clock", "calendar", "datetile", "weather", "rings", "memory", "battery", "notes" };
        public const int MaxColumns = 6;

        public static string PanelName(string id)
        {
            switch (id)
            {
                case "gpu": return "GPU";
                case "network": return "ネットワーク";
                case "drives": return "ドライブ";
                case "topmem": return "メモリ上位プロセス";
                case "cpu": return "CPU";
                case "topcpu": return "CPU上位プロセス";
                case "clock": return "時計";
                case "calendar": return "カレンダー";
                case "datetile": return "日付タイル";
                case "weather": return "天気";
                case "rings": return "リングゲージ";
                case "memory": return "メモリ (詳細)";
                case "battery": return "バッテリー";
                case "notes": return "メモ・カウントダウン";
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
            if (id == "battery") return 0;
            if (id == "notes") return 2;
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
            if (double.IsNaN(Latitude) || Latitude < -90 || Latitude > 90) Latitude = 35.68;
            if (double.IsNaN(Longitude) || Longitude < -180 || Longitude > 180) Longitude = 139.77;
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
            if (WeatherUnits != "metric-ms" && WeatherUnits != "imperial") WeatherUnits = "metric";
            AlertCpuPct = Math.Max(0, Math.Min(100, AlertCpuPct));
            AlertMemPct = Math.Max(0, Math.Min(100, AlertMemPct));
            AlertDiskFreePct = Math.Max(0, Math.Min(100, AlertDiskFreePct));
            AlertTempC = Math.Max(0, Math.Min(150, AlertTempC));
            AlertBatteryPct = Math.Max(0, Math.Min(100, AlertBatteryPct));
            if (AlertSeconds < 5 || AlertSeconds > 3600) AlertSeconds = 60;
            if (EditHotkey == null) EditHotkey = "";
            if (ClickThroughHotkey == null) ClickThroughHotkey = "";
            if (HoverOpacity < 20 || HoverOpacity > 100) HoverOpacity = 100;
            if (Notes == null) Notes = new List<string>();
            Notes.RemoveAll(delegate(string n) { return n == null; });
            if (Countdowns == null) Countdowns = new List<Countdown>();
            Countdowns.RemoveAll(delegate(Countdown c) { return c == null || string.IsNullOrWhiteSpace(c.Date); });
            if (WorldClocks == null) WorldClocks = new List<string>();
            WorldClocks.RemoveAll(delegate(string w) { return string.IsNullOrWhiteSpace(w); });
        }

        public class Countdown
        {
            public string Name;
            public string Date;   // "yyyy-MM-dd"
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

        // 最後に読んだ / 書いたときのファイルの中身 (項目ごと)。手編集との突き合わせに使う
        static Dictionary<string, object> lastFileDict;
        static bool mergedExternal;

        // 直前の Save で手編集を取り込んだか (一度読むと消える)。取り込んだ後はファイルを読み直して画面にも反映する
        public static bool TakeMergedExternal()
        {
            bool m = mergedExternal;
            mergedExternal = false;
            return m;
        }

        // 一時ファイルに書いてから差し替える (書き込み途中で落ちても settings.json が途中切れにならない)
        public void Save()
        {
            try
            {
                var ser = new JavaScriptSerializer();
                if (ExternallyModified()) MergeExternal(ser);
                string json = Pretty(ser.Serialize(this));
                WriteAtomic(FilePath, json);
                try { lastFileDict = ser.DeserializeObject(json) as Dictionary<string, object>; } catch { }
                RememberStamp();
            }
            catch (Exception ex) { Log.Write("Settings.Save", ex); }
        }

        // メモ帳などで保存した直後 (再読み込みの前) にメニューを操作したとき、手編集を上書きして消さないよう、
        // ファイルで変わった項目だけを取り込んでから書く
        void MergeExternal(JavaScriptSerializer ser)
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                var ext = ser.DeserializeObject(File.ReadAllText(FilePath, Encoding.UTF8)) as Dictionary<string, object>;
                if (ext == null) return;   // 壊れている: 再読み込みの側で知らせる
                var last = lastFileDict ?? new Dictionary<string, object>();
                int merged = 0;
                foreach (FieldInfo f in typeof(Settings).GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (f.IsDefined(typeof(ScriptIgnoreAttribute), false)) continue;
                    object v, old;
                    if (!ext.TryGetValue(f.Name, out v)) continue;
                    if (last.TryGetValue(f.Name, out old) && ser.Serialize(old) == ser.Serialize(v)) continue;   // 手編集で変わっていない
                    if (last.ContainsKey(f.Name) && ser.Serialize(f.GetValue(this)) != ser.Serialize(old)) continue;   // メニューでいま変えた項目は、その操作を優先する
                    try { f.SetValue(this, ser.ConvertToType(v, f.FieldType)); merged++; }
                    catch (Exception ex) { Log.Write("Settings." + f.Name + " (手編集の値が読めないので今の値のまま)", ex); }
                }
                if (merged == 0) return;
                Normalize();
                mergedExternal = true;
            }
            catch (Exception ex) { Log.Write("Settings.MergeExternal", ex); }
        }

        // 一時ファイルに書いてから差し替える。差し替えが拒まれたとき (ウイルス対策や共有フォルダー) は少し待って、それでもだめなら上書きコピー
        internal static void WriteAtomic(string path, string text)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            if (!File.Exists(path)) { File.Move(tmp, path); return; }
            try { File.Replace(tmp, path, null); return; }
            catch (IOException) { }
            System.Threading.Thread.Sleep(100);
            try { File.Replace(tmp, path, null); return; }
            catch (IOException) { }
            File.Copy(tmp, path, true);
            File.Delete(tmp);
        }

        internal static string Pretty(string json)
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

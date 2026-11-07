using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace DeskMeter
{
    // 切り離しパネルの位置、配置プリセット、テーマ (見た目一式の保存)
    public partial class Settings
    {
        public class FloatPos
        {
            public string Id;
            public string Monitor;
            public int X, Y;
        }

        void NormalizeFloating()
        {
            if (Floating == null) Floating = new List<string>();
            if (FloatPositions == null) FloatPositions = new List<FloatPos>();
            var clean = new List<string>();
            foreach (string id in Floating)
                if (id != null && Array.IndexOf(PanelIds, id) >= 0 && !clean.Contains(id)) clean.Add(id);
            Floating = clean;
            // 切り離したパネルは列の中には置かない
            foreach (List<string> col in Layout) col.RemoveAll(delegate(string id) { return Floating.Contains(id); });
            Layout.RemoveAll(delegate(List<string> col) { return col.Count == 0; });
            FloatPositions.RemoveAll(delegate(FloatPos p) { return p == null || p.Id == null; });
        }

        // CPU パネルからメモリを分離したとき、古い配置には memory パネルが無いので CPU の直後に足す。
        // 列が画面に収まるよう、コアは 2 列表示にする (メニューで戻せる)
        [System.Web.Script.Serialization.ScriptIgnore]
        public bool pendingSave;   // 読み込み時の移行で変更があった (ファイルには書かない)

        void MigrateMemoryPanel()
        {
            if (MemoryPanelAdded) return;
            MemoryPanelAdded = true;
            pendingSave = true;
            if (HasPanel("memory")) return;
            bool placed = false;
            foreach (List<string> col in Layout)
            {
                int i = col.IndexOf("cpu");
                if (i >= 0) { col.Insert(i + 1, "memory"); placed = true; CompactCores = true; break; }
            }
            if (!placed && Layout.Count > 0) Layout[Math.Min(1, Layout.Count - 1)].Add("memory");
        }

        public FloatPos GetFloatPos(string id)
        {
            foreach (FloatPos p in FloatPositions) if (p.Id == id) return p;
            return null;
        }

        public FloatPos EnsureFloatPos(string id)
        {
            FloatPos p = GetFloatPos(id);
            if (p == null)
            {
                // 初期位置は左上から階段状に
                p = new FloatPos();
                p.Id = id;
                p.X = 24 + FloatPositions.Count * 40;
                p.Y = 24 + FloatPositions.Count * 40;
                FloatPositions.Add(p);
            }
            return p;
        }

        // パネルを列から外して独立ウィンドウにする
        public void Detach(string id)
        {
            foreach (List<string> col in Layout) col.Remove(id);
            Layout.RemoveAll(delegate(List<string> col) { return col.Count == 0; });
            if (!Floating.Contains(id)) Floating.Add(id);
            EnsureFloatPos(id);
        }

        // 独立ウィンドウをやめて列に戻す
        public void Attach(string id)
        {
            Floating.Remove(id);
            foreach (List<string> col in Layout) col.Remove(id);
            if (Layout.Count == 0) Layout.Add(new List<string>());
            int c = Math.Min(DefaultColumn(id), Layout.Count - 1);
            Layout[c].Add(id);
        }

        // ---------------------------------------------------------------- 配置プリセット

        public static readonly string[] PresetNames = { "standard", "wide", "sidebar", "compact", "dashboard", "floating" };

        public static string PresetDisplayName(string name)
        {
            switch (name)
            {
                case "standard": return "標準 (3列)";
                case "wide": return "ワイド (5列・コア2列表示)";
                case "sidebar": return "サイドバー (1列: 時計・リング・ネット・ドライブ・天気)";
                case "compact": return "コンパクト (1列: 時計・リング・ネット)";
                case "dashboard": return "ダッシュボード (4列・全パネル)";
                case "floating": return "すべて切り離す (パネルごとに独立ウィンドウ)";
                default: return name;
            }
        }

        static List<List<string>> Cols(params string[][] cols)
        {
            var l = new List<List<string>>();
            foreach (string[] c in cols) l.Add(new List<string>(c));
            return l;
        }

        // プリセットを適用する。切り離しは解除される ("floating" を除く)
        public void ApplyPreset(string name)
        {
            Floating.Clear();
            switch (name)
            {
                case "wide":
                    Layout = Cols(new[] { "gpu", "drives" }, new[] { "cpu" }, new[] { "network", "memory", "topmem" }, new[] { "clock", "calendar" }, new[] { "datetile", "weather", "topcpu" });
                    CompactCores = true;
                    break;
                case "sidebar":
                    Layout = Cols(new[] { "clock", "rings", "network", "drives", "weather" });
                    break;
                case "compact":
                    Layout = Cols(new[] { "clock", "rings", "network" });
                    break;
                case "dashboard":
                    Layout = Cols(new[] { "gpu", "network", "drives" }, new[] { "cpu", "memory" }, new[] { "rings", "topcpu", "topmem" }, new[] { "clock", "calendar", "datetile", "weather" });
                    CompactCores = true;
                    break;
                case "floating":
                    foreach (List<string> col in Layout) foreach (string id in col) { Floating.Add(id); EnsureFloatPos(id); }
                    Layout = new List<List<string>>();
                    break;
                default:
                    Layout = DefaultLayout();
                    CompactCores = false;
                    break;
            }
            CleanLayout();
        }

        // ---------------------------------------------------------------- テーマ (見た目一式)

        public class ThemeFile
        {
            public string Skin;
            public List<List<string>> Layout;
            public bool? CompactCores;
            public double? Scale, BackgroundOpacity, BackgroundImageOpacity;
            public string TextShadow, BackgroundImage, MascotImage, MascotPlacement, Mode;
            public int? HistorySeconds, MascotSize;
            public List<string> Floating;
            public List<FloatPos> FloatPositions;
        }

        public static string ThemeDir { get { return Path.Combine(AppPaths.Dir, "themes"); } }

        public static string[] ThemeNames()
        {
            try
            {
                if (!Directory.Exists(ThemeDir)) return new string[0];
                var names = new List<string>();
                foreach (string f in Directory.GetFiles(ThemeDir, "*.json")) names.Add(Path.GetFileNameWithoutExtension(f));
                names.Sort(StringComparer.CurrentCultureIgnoreCase);
                return names.ToArray();
            }
            catch (Exception ex) { Log.Write("themes", ex); return new string[0]; }
        }

        public ThemeFile ToTheme()
        {
            var t = new ThemeFile();
            t.Skin = Skin;
            t.Layout = Layout;
            t.CompactCores = CompactCores;
            t.Scale = Scale;
            t.BackgroundOpacity = BackgroundOpacity;
            t.BackgroundImageOpacity = BackgroundImageOpacity;
            t.TextShadow = TextShadow;
            t.BackgroundImage = BackgroundImage;
            t.MascotImage = MascotImage;
            t.MascotPlacement = MascotPlacement;
            t.Mode = Mode;
            t.HistorySeconds = HistorySeconds;
            t.MascotSize = MascotSize;
            t.Floating = Floating;
            t.FloatPositions = FloatPositions;
            return t;
        }

        // 指定の項目だけ上書きする (位置やモニター、天気の場所などは変えない)
        public void ApplyTheme(ThemeFile t)
        {
            if (t.Skin != null) Skin = t.Skin;
            if (t.Layout != null) Layout = t.Layout;
            if (t.CompactCores.HasValue) CompactCores = t.CompactCores.Value;
            if (t.Scale.HasValue) Scale = t.Scale.Value;
            if (t.BackgroundOpacity.HasValue) BackgroundOpacity = t.BackgroundOpacity.Value;
            if (t.BackgroundImageOpacity.HasValue) BackgroundImageOpacity = t.BackgroundImageOpacity.Value;
            if (t.TextShadow != null) TextShadow = t.TextShadow;
            if (t.BackgroundImage != null) BackgroundImage = t.BackgroundImage;
            if (t.MascotImage != null) MascotImage = t.MascotImage;
            if (t.MascotPlacement != null) MascotPlacement = t.MascotPlacement;
            if (t.Mode != null) Mode = t.Mode;
            if (t.HistorySeconds.HasValue) HistorySeconds = t.HistorySeconds.Value;
            if (t.MascotSize.HasValue) MascotSize = t.MascotSize.Value;
            Floating = t.Floating ?? new List<string>();
            if (t.FloatPositions != null) FloatPositions = t.FloatPositions;
            Normalize();
        }

        public bool SaveTheme(string name)
        {
            try
            {
                Directory.CreateDirectory(ThemeDir);
                string path = Path.Combine(ThemeDir, name + ".json");
                File.WriteAllText(path, Pretty(new JavaScriptSerializer().Serialize(ToTheme())), new UTF8Encoding(false));
                return true;
            }
            catch (Exception ex) { Log.Write("theme save " + name, ex); return false; }
        }

        public static ThemeFile LoadTheme(string name)
        {
            try
            {
                string path = Path.Combine(ThemeDir, name + ".json");
                return new JavaScriptSerializer().Deserialize<ThemeFile>(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception ex) { Log.Write("theme load " + name, ex); return null; }
        }
    }
}

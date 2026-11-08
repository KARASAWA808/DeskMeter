using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Media;

namespace DeskMeter
{
    // 配色・フォント・形状の定義。MeterView はここにある値だけを使って描く。
    sealed partial class Skin
    {
        public string Name;
        public int Warnings;             // skins\*.json から読んだときに見つかった問題の数 (組み込みは 0)
        public string FontFamily = "Segoe UI, Yu Gothic UI, Meiryo UI";
        public double CornerRadius = 10;
        public Color Background = Hex("#08090C");
        public Pen Border;             // ウィンドウ外枠 (null なら無し)
        public bool Scanlines;         // CRT 風の走査線
        public Brush TileOverride;     // 見出しアイコンの背景を全パネル共通にする場合

        public Brush Title, Text1, Text2, Accent1, Accent2, Lime, Warn, Danger, Muted, Dim,
            Magenta, Violet, Blue, Track, GraphBg, Separator,
            CalHead, Today, CalSun, CalSat, DateTile, DateTileText,
            Sun, CloudLight, CloudDark, Rain, Snow, MoonLit, MoonDark;
        public Pen GraphBorder, GraphGrid;
        public Brush[] Palette;        // コア / プロセスの色 (順に循環)
        public Brush[] Tiles = new Brush[6];   // GPU, CPU, Network, Drives, TopCpu, TopMemory

        // 見た目の拡張 (SkinsExtra.cs のスキンが使う)
        public Brush BackgroundBrush;    // null なら Background の単色。グラデーション等
        public double TileRadius = 6;    // 見出しアイコンの角丸
        public string BarStyle = "flat"; // flat / pill / segment
        public bool GraphArea;           // 折れ線の下を塗る
        public string Decor = "";        // 背景の飾り: sakura / hearts / stars / bubbles / grid / rain / dots / lines
        public Color DecorColor = Colors.White;
        public string Mascot = "";       // 空きスペースに描くキャラクター: cat / bunny / bear
        public Brush MascotBody, MascotAccent, MascotEye;
        public Brush CardBrush;          // パネルごとのカード背景 (null なら無し)
        public Pen CardBorder;
        public double CardRadius = 12;
        public bool Sharp;               // 角丸を一切使わない (バー・日付タイル・配置編集の枠も直角)

        public static Color Hex(string s) { return (Color)ColorConverter.ConvertFromString(s); }

        public static Brush B(string hex)
        {
            var b = new SolidColorBrush(Hex(hex));
            b.Freeze();
            return b;
        }

        public static Pen P(Brush b, double t)
        {
            var p = new Pen(b, t);
            p.LineJoin = PenLineJoin.Round;
            p.StartLineCap = PenLineCap.Round;
            p.EndLineCap = PenLineCap.Round;
            p.Freeze();
            return p;
        }

        static Brush Gradient(string a, string b)
        {
            var g = new LinearGradientBrush(Hex(a), Hex(b), 90);
            g.Freeze();
            return g;
        }

        static Brush[] Brushes(params string[] hex)
        {
            var r = new Brush[hex.Length];
            for (int i = 0; i < hex.Length; i++) r[i] = B(hex[i]);
            return r;
        }

        public static readonly string[] BuiltIn = { "Classic", "ClassicSharp", "Cyberpunk", "Retro", "Light", "KawaiiPink", "KawaiiMint", "Synthwave", "Matrix", "Nord", "Dracula", "Frost", "Notebook" };

        // 組み込み + skins フォルダーのスキン名 (呼ぶたびにフォルダーを見直す)
        public static string[] Names
        {
            get
            {
                var all = new List<string>(BuiltIn);
                all.AddRange(FileNames());
                return all.ToArray();
            }
        }

        public static string SkinDir { get { return Path.Combine(AppPaths.Dir, "skins"); } }

        // skins\*.json のファイル名 (拡張子なし) をスキン名として並べる。組み込みと同名のものは除く
        public static string[] FileNames()
        {
            var names = new List<string>();
            try
            {
                if (Directory.Exists(SkinDir))
                    foreach (string f in Directory.GetFiles(SkinDir, "*.json"))
                    {
                        string n = Path.GetFileNameWithoutExtension(f);
                        if (string.IsNullOrEmpty(n) || IsBuiltIn(n)) continue;
                        names.Add(n);
                    }
            }
            catch (Exception ex) { Log.Write("Skin.FileNames", ex); }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names.ToArray();
        }

        // 組み込み、または skins フォルダーにあるか (テーマが指すスキンが消えていないかの確認用)
        public static bool Exists(string name)
        {
            if (IsBuiltIn(name)) return true;
            foreach (string f in FileNames()) if (string.Equals(f, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static bool IsBuiltIn(string name)
        {
            foreach (string b in BuiltIn) if (string.Equals(b, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static string DisplayName(string name)
        {
            switch (name)
            {
                case "Classic": return "Classic";
                case "ClassicSharp": return "Classic Sharp (角なし)";
                case "Cyberpunk": return "Cyberpunk (ネオン)";
                case "Retro": return "Retro (アンバーCRT)";
                case "Light": return "Light (明るい壁紙向け)";
                case "KawaiiPink": return "Kawaii Pink (さくら・うさぎ)";
                case "KawaiiMint": return "Kawaii Mint (ミント・ねこ)";
                case "Synthwave": return "Synthwave (夕焼けグリッド)";
                case "Matrix": return "Matrix (緑の端末)";
                case "Nord": return "Nord (北欧パレット)";
                case "Dracula": return "Dracula (紫と桃)";
                case "Frost": return "Frost (すりガラス)";
                case "Notebook": return "Notebook (紙のノート)";
                default: return name;
            }
        }

        public static Skin Get(string name)
        {
            switch (name)
            {
                case "Cyberpunk": return Cyberpunk();
                case "Retro": return Retro();
                case "Light": return Light();
                case "Classic": return Classic();
                case "ClassicSharp": return ClassicSharp();
                case "KawaiiPink": return KawaiiPink();
                case "KawaiiMint": return KawaiiMint();
                case "Synthwave": return Synthwave();
                case "Matrix": return Matrix();
                case "Nord": return Nord();
                case "Dracula": return Dracula();
                case "Frost": return Frost();
                case "Notebook": return Notebook();
            }
            // 組み込みに無ければ skins\<名前>.json。読めなければ Classic
            if (!string.IsNullOrEmpty(name) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
            {
                string path = Path.Combine(SkinDir, name + ".json");
                if (File.Exists(path))
                {
                    Skin s = FromFile(path);
                    if (s != null) return s;
                }
            }
            return Classic();
        }

        // ---------------------------------------------------------------- ユーザー定義スキン (skins\*.json)

        // JSON の各項目。色は "#RRGGBB" (または "#AARRGGBB")。未指定 (null) の項目は Classic の値になる
        public class SkinFile
        {
            public string FontFamily;
            public double? CornerRadius;
            public string Background;
            public string BorderColor;      // 無指定なら枠なし
            public double? BorderWidth;     // 既定 1
            public bool? Scanlines;
            public string TileOverride;     // 見出しアイコンの背景を全パネル共通にする色
            public string Title, Text1, Text2, Accent1, Accent2, Lime, Warn, Danger, Muted, Dim,
                Magenta, Violet, Blue, Track, GraphBg, Separator, GraphBorder, GraphGrid,
                CalHead, Today, CalSun, CalSat, DateTile, DateTileText,
                Sun, CloudLight, CloudDark, Rain, Snow, MoonLit, MoonDark;
            public string[] Palette;        // コア / プロセスの色 (1 つ以上)
            public string[] Tiles;          // 見出しアイコンの背景 6 色: GPU, CPU, Network, Drives, TopCpu, TopMemory
            // 見た目の拡張
            public string BackgroundGradient;   // "#上の色,#下の色"
            public double? TileRadius;
            public string BarStyle;             // flat / pill / segment
            public bool? GraphArea;
            public string Decor;                // sakura / hearts / stars / bubbles / grid / rain / dots / lines
            public string DecorColor;
            public string Mascot;               // cat / bunny / bear
            public string MascotBody, MascotAccent, MascotEye;
            public string Card;                 // パネルごとのカード背景色 (#AARRGGBB 可)
            public string CardBorder;
            public double? CardRadius;
            public bool? Sharp;                 // true で角丸を一切使わない
        }

        // 壊れた JSON は deskmeter.log に書いて null を返す。色の書き間違いは項目ごとに Classic の値で代用する
        static readonly string[] BarStyles = { "flat", "pill", "segment" };
        static readonly string[] Decors = { "", "sakura", "hearts", "stars", "bubbles", "grid", "rain", "dots", "lines" };
        static readonly string[] Mascots = { "", "cat", "bunny", "bear" };

        // 選択肢から選ぶ項目。候補に無い値はログに残して既定のまま
        static string Choice(FileCtx ctx, string field, string text, string[] allowed, string fallback)
        {
            if (text == null) return fallback;
            string v = text.Trim().ToLowerInvariant();
            if (Array.IndexOf(allowed, v) >= 0) return v;
            ctx.Warn(field, "\"" + text + "\" は使えません。候補: " + string.Join(" / ", allowed).Trim(' ', '/'));
            return fallback;
        }

        // 書かれたフォント名のどれか 1 つでも入っているか (無ければ WPF が既定フォントで代用するので、その旨をログに残す)
        // 入っているフォント名の一覧。列挙は数百 ms かかるので起動後 1 回だけ (WPF 自体も起動時の一覧を使う)
        static HashSet<string> installedFonts;

        static bool FontInstalled(string families)
        {
            try
            {
                if (installedFonts == null)
                {
                    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (FontFamily ff in Fonts.SystemFontFamilies)
                    {
                        set.Add(ff.Source);
                        foreach (string n in ff.FamilyNames.Values) set.Add(n);
                    }
                    installedFonts = set;
                }
                foreach (string raw in families.Split(','))
                {
                    string want = raw.Trim();
                    if (want.Length > 0 && installedFonts.Contains(want)) return true;
                }
                return false;
            }
            catch { return true; }
        }

        public static Skin FromFile(string path)
        {
            SkinFile f;
            Dictionary<string, object> raw = null;
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                var ser = new JavaScriptSerializer();
                f = ser.Deserialize<SkinFile>(json);
                if (f == null) throw new FormatException("JSON が空です");
                raw = ser.DeserializeObject(json) as Dictionary<string, object>;
            }
            catch (Exception ex)
            {
                Log.Write("Skin " + Path.GetFileName(path), ex);
                return null;
            }
            try
            {
                var ctx = new FileCtx(Path.GetFileName(path));
                Skin s = Classic();
                s.Name = Path.GetFileNameWithoutExtension(path);
                // 綴りを間違えたキーは黙って無視されるので、知らせる
                if (raw != null)
                {
                    foreach (string key in raw.Keys)
                        if (!key.StartsWith("_") && typeof(SkinFile).GetField(key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) == null) ctx.Warn(key, "知らないキーです (綴りを確認してください)");   // 大文字小文字の違いは読み込みと同じく許す   // "_comment" などメモ用のキーは許す
                }
                if (!string.IsNullOrWhiteSpace(f.FontFamily))
                {
                    s.FontFamily = f.FontFamily.Trim();
                    if (!FontInstalled(s.FontFamily)) ctx.Warn("FontFamily", "\"" + s.FontFamily + "\" はこの PC に入っていないので既定のフォントで表示します");
                }
                if (f.CornerRadius.HasValue) s.CornerRadius = Math.Max(0, Math.Min(40, f.CornerRadius.Value));
                s.Background = ctx.Color("Background", f.Background, s.Background);
                if (!string.IsNullOrEmpty(f.BorderColor))
                {
                    Brush bb = ctx.Brush("BorderColor", f.BorderColor, null);
                    if (bb != null) s.Border = P(bb, f.BorderWidth.HasValue ? Math.Max(0.5, Math.Min(6, f.BorderWidth.Value)) : 1);
                }
                if (f.Scanlines.HasValue) s.Scanlines = f.Scanlines.Value;
                s.TileOverride = ctx.Brush("TileOverride", f.TileOverride, null);
                s.Title = ctx.Brush("Title", f.Title, s.Title); s.Text1 = ctx.Brush("Text1", f.Text1, s.Text1); s.Text2 = ctx.Brush("Text2", f.Text2, s.Text2);
                s.Accent1 = ctx.Brush("Accent1", f.Accent1, s.Accent1); s.Accent2 = ctx.Brush("Accent2", f.Accent2, s.Accent2); s.Lime = ctx.Brush("Lime", f.Lime, s.Lime);
                s.Warn = ctx.Brush("Warn", f.Warn, s.Warn); s.Danger = ctx.Brush("Danger", f.Danger, s.Danger);
                s.Muted = ctx.Brush("Muted", f.Muted, s.Muted); s.Dim = ctx.Brush("Dim", f.Dim, s.Dim);
                s.Magenta = ctx.Brush("Magenta", f.Magenta, s.Magenta); s.Violet = ctx.Brush("Violet", f.Violet, s.Violet); s.Blue = ctx.Brush("Blue", f.Blue, s.Blue);
                s.Track = ctx.Brush("Track", f.Track, s.Track); s.GraphBg = ctx.Brush("GraphBg", f.GraphBg, s.GraphBg); s.Separator = ctx.Brush("Separator", f.Separator, s.Separator);
                Brush gb = ctx.Brush("GraphBorder", f.GraphBorder, null); if (gb != null) s.GraphBorder = P(gb, 1);
                Brush gg = ctx.Brush("GraphGrid", f.GraphGrid, null); if (gg != null) s.GraphGrid = P(gg, 1);
                s.CalHead = ctx.Brush("CalHead", f.CalHead, s.CalHead); s.Today = ctx.Brush("Today", f.Today, s.Today);
                s.CalSun = ctx.Brush("CalSun", f.CalSun, s.CalSun); s.CalSat = ctx.Brush("CalSat", f.CalSat, s.CalSat);
                s.DateTile = ctx.Brush("DateTile", f.DateTile, s.DateTile); s.DateTileText = ctx.Brush("DateTileText", f.DateTileText, s.DateTileText);
                s.Sun = ctx.Brush("Sun", f.Sun, s.Sun); s.CloudLight = ctx.Brush("CloudLight", f.CloudLight, s.CloudLight); s.CloudDark = ctx.Brush("CloudDark", f.CloudDark, s.CloudDark);
                s.Rain = ctx.Brush("Rain", f.Rain, s.Rain); s.Snow = ctx.Brush("Snow", f.Snow, s.Snow);
                s.MoonLit = ctx.Brush("MoonLit", f.MoonLit, s.MoonLit); s.MoonDark = ctx.Brush("MoonDark", f.MoonDark, s.MoonDark);
                Brush[] pal = ctx.Brushes("Palette", f.Palette, 0);
                if (pal != null) s.Palette = pal;
                Brush[] tiles = ctx.Brushes("Tiles", f.Tiles, 6);
                if (tiles != null) s.Tiles = tiles;
                // 見た目の拡張
                s.BackgroundBrush = ctx.Brush("BackgroundGradient", f.BackgroundGradient, null);
                if (f.TileRadius.HasValue) s.TileRadius = Math.Max(0, Math.Min(18, f.TileRadius.Value));
                s.BarStyle = Choice(ctx, "BarStyle", f.BarStyle, BarStyles, s.BarStyle);
                if (f.GraphArea.HasValue) s.GraphArea = f.GraphArea.Value;
                if (f.Sharp.HasValue) s.Sharp = f.Sharp.Value;
                s.Decor = Choice(ctx, "Decor", f.Decor, Decors, s.Decor);
                s.DecorColor = ctx.Color("DecorColor", f.DecorColor, s.DecorColor);
                s.Mascot = Choice(ctx, "Mascot", f.Mascot, Mascots, s.Mascot);
                s.MascotBody = ctx.Brush("MascotBody", f.MascotBody, s.MascotBody);
                s.MascotAccent = ctx.Brush("MascotAccent", f.MascotAccent, s.MascotAccent);
                s.MascotEye = ctx.Brush("MascotEye", f.MascotEye, s.MascotEye);
                s.CardBrush = ctx.Brush("Card", f.Card, null);
                Brush cb = ctx.Brush("CardBorder", f.CardBorder, null); if (cb != null) s.CardBorder = P(cb, 1);
                if (f.CardRadius.HasValue) s.CardRadius = Math.Max(0, Math.Min(30, f.CardRadius.Value));
                s.Warnings = ctx.Warnings;
                return s;
            }
            catch (Exception ex)
            {
                Log.Write("Skin " + Path.GetFileName(path), ex);
                return null;
            }
        }

        // 色文字列の変換。失敗したらログに残して既定値を返す
        sealed class FileCtx
        {
            readonly string file;
            public FileCtx(string file) { this.file = file; }
            public int Warnings;   // ログに残した問題の数 (メニューで選んだときに知らせる)

            public void Warn(string field, string message)
            {
                Warnings++;
                Log.Write("Skin " + file + " " + field, new FormatException(message));
            }

            public Color Color(string field, string text, Color fallback)
            {
                if (string.IsNullOrWhiteSpace(text)) return fallback;
                try { return Hex(text.Trim()); }
                catch (Exception ex) { Warnings++; Log.Write("Skin " + file + " " + field + " = " + text, ex); return fallback; }
            }

            // "#RRGGBB" で単色、"#RRGGBB,#RRGGBB" で上下のグラデーション
            public Brush Brush(string field, string text, Brush fallback)
            {
                if (string.IsNullOrWhiteSpace(text)) return fallback;
                try
                {
                    string[] parts = text.Split(',');
                    if (parts.Length >= 2) return Gradient(parts[0].Trim(), parts[1].Trim());
                    return B(text.Trim());
                }
                catch (Exception ex) { Warnings++; Log.Write("Skin " + file + " " + field + " = " + text, ex); return fallback; }
            }

            // 配列。count > 0 ならその個数が必要。ダメなら null (既定のまま)
            public Brush[] Brushes(string field, string[] items, int count)
            {
                if (items == null || items.Length == 0) return null;
                if (count > 0 && items.Length != count)
                {
                    Warnings++;
                    Log.Write("Skin " + file + " " + field, new FormatException(count + " 色必要ですが " + items.Length + " 色です"));
                    return null;
                }
                var r = new Brush[items.Length];
                for (int i = 0; i < items.Length; i++)
                {
                    r[i] = Brush(field + "[" + i + "]", items[i], null);
                    if (r[i] == null) return null;
                }
                return r;
            }
        }

        // 最初に作ったデザイン
        public static Skin Classic()
        {
            var s = new Skin();
            s.Name = "Classic";
            s.Title = B("#F4F6F8"); s.Text1 = B("#F5E14A"); s.Text2 = B("#5CC8FF");
            s.Accent1 = B("#7ED957"); s.Accent2 = B("#FFA630"); s.Lime = B("#A8F070");
            s.Warn = B("#FFA630"); s.Danger = B("#FF5C6A"); s.Muted = B("#9AA2AB"); s.Dim = B("#5B636C");
            s.Magenta = B("#E06BE0"); s.Violet = B("#A98BFF"); s.Blue = B("#4C8DFF");
            s.Track = B("#26292E"); s.GraphBg = B("#0D0F12"); s.Separator = B("#2C6AA0");
            s.GraphBorder = P(B("#30353B"), 1); s.GraphGrid = P(B("#1C2025"), 1);
            s.CalHead = B("#1D4A2A"); s.Today = B("#F26A1B"); s.CalSun = s.Danger; s.CalSat = s.Blue;
            s.DateTile = Gradient("#FF8A2A", "#D9480F"); s.DateTileText = s.Title;
            s.Sun = B("#FFC93C"); s.CloudLight = B("#D5DAE0"); s.CloudDark = B("#8C949E"); s.Rain = B("#5CC8FF");
            s.Snow = s.Title; s.MoonLit = B("#EDE9D6"); s.MoonDark = B("#2B2F36");
            s.Palette = Brushes("#7ED957", "#F2E205", "#FF9F1A", "#F0505A", "#E06BE0", "#A98BFF", "#4C8DFF",
                "#38C6E8", "#B5E61D", "#D6E000", "#FFC300", "#E0705A", "#C77DFF", "#5CE0C0");
            s.Tiles = Brushes("#3F9B2F", "#1F6FD0", "#12827A", "#56627A", "#7A4FC0", "#B8456B");
            return s;
        }

        // Classic の色のまま、角丸を全部なくしたもの (窓・見出しアイコン・バー・日付タイル)
        public static Skin ClassicSharp()
        {
            Skin s = Classic();
            s.Name = "ClassicSharp";
            s.CornerRadius = 0;
            s.TileRadius = 0;
            s.CardRadius = 0;
            s.BarStyle = "flat";
            s.Sharp = true;
            return s;
        }

        // ネオンのシアン / マゼンタ、角ばった枠
        public static Skin Cyberpunk()
        {
            var s = new Skin();
            s.Name = "Cyberpunk";
            s.FontFamily = "Bahnschrift, Yu Gothic UI, Meiryo UI";
            s.CornerRadius = 2;
            s.Background = Hex("#07020F");
            s.Border = P(B("#00E5FF"), 1.2);
            s.Title = B("#F2F6FF"); s.Text1 = B("#FFE600"); s.Text2 = B("#00E5FF");
            s.Accent1 = B("#FF2BD6"); s.Accent2 = B("#FF7A00"); s.Lime = B("#3DFFB3");
            s.Warn = B("#FF7A00"); s.Danger = B("#FF2E63"); s.Muted = B("#8F84B5"); s.Dim = B("#4A4068");
            s.Magenta = B("#FF2BD6"); s.Violet = B("#B388FF"); s.Blue = B("#3D7BFF");
            s.Track = B("#1A0F33"); s.GraphBg = B("#0C0520"); s.Separator = B("#FF2BD6");
            s.GraphBorder = P(B("#3B2A6B"), 1); s.GraphGrid = P(B("#1C1240"), 1);
            s.CalHead = B("#2A1052"); s.Today = B("#FF2BD6"); s.CalSun = B("#FF2E63"); s.CalSat = B("#00E5FF");
            s.DateTile = Gradient("#FF2BD6", "#5B10B8"); s.DateTileText = s.Title;
            s.Sun = B("#FFE600"); s.CloudLight = B("#B9B3E0"); s.CloudDark = B("#6E6496"); s.Rain = B("#00E5FF");
            s.Snow = s.Title; s.MoonLit = B("#E8E4FF"); s.MoonDark = B("#2A1A4A");
            s.Palette = Brushes("#00E5FF", "#FF2BD6", "#FFE600", "#3DFFB3", "#B388FF", "#FF7A00", "#3D7BFF",
                "#FF2E63", "#7CFFEA", "#FF8AF0", "#C6FF00", "#9D4DFF", "#00B8FF", "#FFB800");
            s.TileOverride = B("#4A0D8F");
            return s;
        }

        // アンバー単色の端末風。走査線つき、角丸なし
        public static Skin Retro()
        {
            var s = new Skin();
            s.Name = "Retro";
            s.FontFamily = "Consolas, MS Gothic, Yu Gothic UI";
            s.CornerRadius = 0;
            s.Background = Hex("#060400");
            s.Border = P(B("#B07A1A"), 1);
            s.Scanlines = true;
            s.Title = B("#FFD27F"); s.Text1 = B("#FFB000"); s.Text2 = B("#E09A1A");
            s.Accent1 = B("#FFC84A"); s.Accent2 = B("#FF8C00"); s.Lime = B("#FFD27F");
            s.Warn = B("#FF8C00"); s.Danger = B("#FF4D1F"); s.Muted = B("#A8782A"); s.Dim = B("#5A3F10");
            s.Magenta = B("#F0A030"); s.Violet = B("#D9A040"); s.Blue = B("#E0A848");
            s.Track = B("#2A1C00"); s.GraphBg = B("#0C0800"); s.Separator = B("#B07A1A");
            s.GraphBorder = P(B("#5A3F10"), 1); s.GraphGrid = P(B("#1E1400"), 1);
            s.CalHead = B("#3A2800"); s.Today = B("#FFB000"); s.CalSun = B("#FF6A2A"); s.CalSat = B("#E09A1A");
            s.DateTile = B("#FFB000"); s.DateTileText = B("#1A1000");
            s.Sun = B("#FFC84A"); s.CloudLight = B("#D9A040"); s.CloudDark = B("#8A6020"); s.Rain = B("#FFB000");
            s.Snow = B("#FFD27F"); s.MoonLit = B("#FFD27F"); s.MoonDark = B("#2A1C00");
            s.Palette = Brushes("#FFB000", "#FFD27F", "#E09A1A", "#FF8C00", "#FFC84A", "#C07818", "#FFE3A8");
            s.TileOverride = B("#5A3F10");
            return s;
        }

        // 明るい壁紙向け。濃い文字に薄い背景
        public static Skin Light()
        {
            var s = new Skin();
            s.Name = "Light";
            s.CornerRadius = 12;
            s.Background = Hex("#F4F6FA");
            s.Border = P(B("#C9D1DC"), 1);
            s.Title = B("#1F2933"); s.Text1 = B("#A86A00"); s.Text2 = B("#0B7285");
            s.Accent1 = B("#2B8A3E"); s.Accent2 = B("#D9480F"); s.Lime = B("#2B8A3E");
            s.Warn = B("#E8590C"); s.Danger = B("#C92A2A"); s.Muted = B("#6B7684"); s.Dim = B("#B0B8C2");
            s.Magenta = B("#A61E8E"); s.Violet = B("#6741D9"); s.Blue = B("#1C64D1");
            s.Track = B("#DDE3EA"); s.GraphBg = B("#FFFFFF"); s.Separator = B("#9DB5D1");
            s.GraphBorder = P(B("#CBD3DC"), 1); s.GraphGrid = P(B("#EEF1F4"), 1);
            s.CalHead = B("#D3EAD8"); s.Today = B("#F76707"); s.CalSun = B("#C92A2A"); s.CalSat = B("#1C64D1");
            s.DateTile = Gradient("#FF922B", "#E8590C"); s.DateTileText = B("#FFFFFF");
            s.Sun = B("#F59F00"); s.CloudLight = B("#B8C2CC"); s.CloudDark = B("#7C8792"); s.Rain = B("#1C7ED6");
            s.Snow = B("#5C7CFA"); s.MoonLit = B("#495057"); s.MoonDark = B("#DEE2E6");
            s.Palette = Brushes("#2B8A3E", "#C29200", "#E8590C", "#C92A2A", "#A61E8E", "#6741D9", "#1C64D1",
                "#0B7285", "#5C940D", "#868E96", "#D9480F", "#B02C6C", "#7048E8", "#099268");
            s.Tiles = Brushes("#2B8A3E", "#1C64D1", "#0B7285", "#5F6B7A", "#6741D9", "#A61E8E");
            return s;
        }
    }
}

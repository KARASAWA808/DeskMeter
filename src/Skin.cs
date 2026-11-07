using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace DeskMeter
{
    // 配色・フォント・形状の定義。MeterView はここにある値だけを使って描く。
    sealed class Skin
    {
        public string Name;
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

        public static readonly string[] Names = { "Classic", "Cyberpunk", "Retro", "Light" };

        public static string DisplayName(string name)
        {
            switch (name)
            {
                case "Cyberpunk": return "Cyberpunk (ネオン)";
                case "Retro": return "Retro (アンバーCRT)";
                case "Light": return "Light (明るい壁紙向け)";
                default: return "Classic";
            }
        }

        public static Skin Get(string name)
        {
            switch (name)
            {
                case "Cyberpunk": return Cyberpunk();
                case "Retro": return Retro();
                case "Light": return Light();
                default: return Classic();
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

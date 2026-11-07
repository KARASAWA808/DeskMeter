using System;
using System.Windows.Media;

namespace DeskMeter
{
    // 追加スキン。配色だけでなく、グラデーション背景・パネルのカード・バーの形・背景の飾り・マスコットも指定する
    sealed partial class Skin
    {
        static Brush Argb(string hex) { return B(hex); }

        static Pen Thin(string hex) { return P(B(hex), 1); }

        // パステルピンク。白いカードにさくらの花びら、うさぎのマスコット
        public static Skin KawaiiPink()
        {
            var s = new Skin();
            s.Name = "KawaiiPink";
            s.FontFamily = "UD Digi Kyokasho NK-R, Yu Gothic UI, Meiryo UI";
            s.CornerRadius = 18;
            s.Background = Hex("#FFD6E4");
            s.BackgroundBrush = Gradient("#FFE4EE", "#FFC4D8");
            s.Border = Thin("#F5A6C5");
            s.CardBrush = Argb("#C8FFFFFF"); s.CardBorder = Thin("#F7B9D0"); s.CardRadius = 14;
            s.TileRadius = 18; s.BarStyle = "pill"; s.GraphArea = true;
            s.Decor = "sakura"; s.DecorColor = Hex("#FF8FB8");
            s.Mascot = "bunny"; s.MascotBody = B("#FFFFFF"); s.MascotAccent = B("#FFB3CC"); s.MascotEye = B("#4A3340");
            s.Title = B("#5A3A4A"); s.Text1 = B("#D9488A"); s.Text2 = B("#6B5BD2");
            s.Accent1 = B("#3FAF80"); s.Accent2 = B("#FF8A5C"); s.Lime = B("#3FAF80");
            s.Warn = B("#FF8A5C"); s.Danger = B("#E8365D"); s.Muted = B("#9A8A92"); s.Dim = B("#D8CBD1");
            s.Magenta = B("#D94FB2"); s.Violet = B("#8C6BE0"); s.Blue = B("#5B8DEF");
            s.Track = B("#F6E1E9"); s.GraphBg = B("#FFF7FA"); s.Separator = B("#F2A7C3");
            s.GraphBorder = Thin("#F3CFDC"); s.GraphGrid = Thin("#FBE9F0");
            s.CalHead = B("#FFD3E1"); s.Today = B("#FF6FA3"); s.CalSun = B("#E8365D"); s.CalSat = B("#5B8DEF");
            s.DateTile = Gradient("#FFA3C7", "#FF6FA3"); s.DateTileText = B("#FFFFFF");
            s.Sun = B("#FFC93C"); s.CloudLight = B("#E6E1F0"); s.CloudDark = B("#B8AFC8"); s.Rain = B("#5B8DEF");
            s.Snow = B("#BFD4FF"); s.MoonLit = B("#FFE9A8"); s.MoonDark = B("#E2D6EA");
            s.Palette = Brushes("#FF6FA3", "#FFB347", "#3FAF80", "#5B8DEF", "#B57BEE", "#FF8A5C", "#2BBFC2",
                "#E8365D", "#8DC63F", "#F2B705", "#7B8CFF", "#FF7EB6", "#48C2A0", "#C97BFF");
            s.Tiles = Brushes("#FF8CB8", "#7FA8FF", "#5AC8B4", "#B8A0D8", "#C58BFF", "#FF9E7A");
            return s;
        }

        // ミント×水色。白いカードにきらきら、ねこのマスコット
        public static Skin KawaiiMint()
        {
            var s = new Skin();
            s.Name = "KawaiiMint";
            s.FontFamily = "UD Digi Kyokasho NK-R, Yu Gothic UI, Meiryo UI";
            s.CornerRadius = 18;
            s.Background = Hex("#D7F3EE");
            s.BackgroundBrush = Gradient("#E2F9F3", "#C9EBF6");
            s.Border = Thin("#8FD9C8");
            s.CardBrush = Argb("#C8FFFFFF"); s.CardBorder = Thin("#A9E3D6"); s.CardRadius = 14;
            s.TileRadius = 18; s.BarStyle = "pill"; s.GraphArea = true;
            s.Decor = "stars"; s.DecorColor = Hex("#2EC4A5");
            s.Mascot = "cat"; s.MascotBody = B("#FFFFFF"); s.MascotAccent = B("#FFC2D1"); s.MascotEye = B("#33474A");
            s.Title = B("#2F4A4A"); s.Text1 = B("#E58A2E"); s.Text2 = B("#2B9EB3");
            s.Accent1 = B("#3DB36D"); s.Accent2 = B("#F5742E"); s.Lime = B("#3DB36D");
            s.Warn = B("#F5742E"); s.Danger = B("#E85C7A"); s.Muted = B("#86999A"); s.Dim = B("#CFE0DE");
            s.Magenta = B("#D96BC0"); s.Violet = B("#7E7BE0"); s.Blue = B("#3C87E0");
            s.Track = B("#E3F2EE"); s.GraphBg = B("#F6FCFA"); s.Separator = B("#8FD9C8");
            s.GraphBorder = Thin("#CFEAE3"); s.GraphGrid = Thin("#EAF6F2");
            s.CalHead = B("#C8F0E6"); s.Today = B("#2EC4A5"); s.CalSun = B("#E85C7A"); s.CalSat = B("#3C87E0");
            s.DateTile = Gradient("#7FDCC2", "#2EC4A5"); s.DateTileText = B("#FFFFFF");
            s.Sun = B("#FFC93C"); s.CloudLight = B("#E3EAF0"); s.CloudDark = B("#AFBCC8"); s.Rain = B("#3C87E0");
            s.Snow = B("#9FC6FF"); s.MoonLit = B("#FFE9A8"); s.MoonDark = B("#D5E3E8");
            s.Palette = Brushes("#2EC4A5", "#F5A623", "#3C87E0", "#E85C7A", "#A26BE8", "#FF8C69", "#3BBFC9",
                "#8BC34A", "#F2B705", "#6C8CFF", "#F078B0", "#37B8A0", "#C56BFF", "#FFB347");
            s.Tiles = Brushes("#4FD1B3", "#5CA8FF", "#45C1C8", "#9FB3C8", "#A98BFF", "#FF9E9E");
            return s;
        }

        // 80年代風。紫の夕空に太陽とグリッド
        public static Skin Synthwave()
        {
            var s = new Skin();
            s.Name = "Synthwave";
            s.FontFamily = "Bahnschrift, Yu Gothic UI, Meiryo UI";
            s.CornerRadius = 6;
            s.Background = Hex("#1A0B3D");
            s.BackgroundBrush = Gradient("#140833", "#3A0F5C");
            s.Border = Thin("#FF4FD8");
            s.GraphArea = true;
            s.Decor = "grid"; s.DecorColor = Hex("#FF4FD8");
            s.Title = B("#FFFFFF"); s.Text1 = B("#FFD166"); s.Text2 = B("#4CE0FF");
            s.Accent1 = B("#FF4FD8"); s.Accent2 = B("#FF8C42"); s.Lime = B("#7CFFCB");
            s.Warn = B("#FF8C42"); s.Danger = B("#FF3B6B"); s.Muted = B("#A48CC8"); s.Dim = B("#5A4A80");
            s.Magenta = B("#FF4FD8"); s.Violet = B("#B98BFF"); s.Blue = B("#5B8CFF");
            s.Track = B("#2A1850"); s.GraphBg = Argb("#B0150A30"); s.Separator = B("#FF4FD8");
            s.GraphBorder = Thin("#4B2E80"); s.GraphGrid = Thin("#261545");
            s.CalHead = B("#3B1C6E"); s.Today = B("#FF4FD8"); s.CalSun = B("#FF3B6B"); s.CalSat = B("#4CE0FF");
            s.DateTile = Gradient("#FF4FD8", "#7A1FA2"); s.DateTileText = B("#FFFFFF");
            s.Sun = B("#FFD166"); s.CloudLight = B("#C9B8F0"); s.CloudDark = B("#7E67B0"); s.Rain = B("#4CE0FF");
            s.Snow = B("#FFFFFF"); s.MoonLit = B("#FFE9A8"); s.MoonDark = B("#2A1850");
            s.Palette = Brushes("#FF4FD8", "#4CE0FF", "#FFD166", "#7CFFCB", "#B98BFF", "#FF8C42", "#5B8CFF",
                "#FF3B6B", "#9BFF8A", "#FFB3F0", "#C6FF00", "#9D4DFF", "#00C8FF", "#FFB800");
            s.TileOverride = B("#5C1F99");
            return s;
        }

        // 緑の端末。文字の雨と走査線、ブロック状のバー
        public static Skin Matrix()
        {
            var s = new Skin();
            s.Name = "Matrix";
            s.FontFamily = "Consolas, MS Gothic, Yu Gothic UI";
            s.CornerRadius = 0;
            s.Background = Hex("#020A03");
            s.Border = Thin("#1FD65A");
            s.Scanlines = true;
            s.BarStyle = "segment";
            s.Decor = "rain"; s.DecorColor = Hex("#39FF6E");
            s.Title = B("#CFFFD8"); s.Text1 = B("#39FF6E"); s.Text2 = B("#1FD65A");
            s.Accent1 = B("#7CFF9B"); s.Accent2 = B("#A8FF3E"); s.Lime = B("#B6FFC0");
            s.Warn = B("#E0FF4F"); s.Danger = B("#FF5C5C"); s.Muted = B("#2E8A4A"); s.Dim = B("#16472A");
            s.Magenta = B("#5CFFB0"); s.Violet = B("#8AFFC4"); s.Blue = B("#3CE8C8");
            s.Track = B("#0B2A14"); s.GraphBg = Argb("#C003110A"); s.Separator = B("#1FD65A");
            s.GraphBorder = Thin("#16472A"); s.GraphGrid = Thin("#0B2A14");
            s.CalHead = B("#0F3D1F"); s.Today = B("#39FF6E"); s.CalSun = B("#FF5C5C"); s.CalSat = B("#3CE8C8");
            s.DateTile = B("#1A7A3A"); s.DateTileText = B("#CFFFD8");
            s.Sun = B("#A8FF3E"); s.CloudLight = B("#7CFF9B"); s.CloudDark = B("#2E8A4A"); s.Rain = B("#39FF6E");
            s.Snow = B("#CFFFD8"); s.MoonLit = B("#CFFFD8"); s.MoonDark = B("#0B2A14");
            s.Palette = Brushes("#39FF6E", "#A8FF3E", "#1FD65A", "#7CFF9B", "#E0FF4F", "#3CE8C8", "#B6FFC0", "#2E8A4A");
            s.TileOverride = B("#0F3D1F");
            return s;
        }

        // 北欧パレット。落ち着いた青灰色とカード
        public static Skin Nord()
        {
            var s = new Skin();
            s.Name = "Nord";
            s.CornerRadius = 8;
            s.Background = Hex("#2E3440");
            s.CardBrush = B("#3B4252"); s.CardRadius = 8;
            s.TileRadius = 8;
            s.Title = B("#ECEFF4"); s.Text1 = B("#EBCB8B"); s.Text2 = B("#88C0D0");
            s.Accent1 = B("#A3BE8C"); s.Accent2 = B("#D08770"); s.Lime = B("#A3BE8C");
            s.Warn = B("#D08770"); s.Danger = B("#BF616A"); s.Muted = B("#9DA5B4"); s.Dim = B("#5A6476");
            s.Magenta = B("#B48EAD"); s.Violet = B("#B48EAD"); s.Blue = B("#81A1C1");
            s.Track = B("#4C566A"); s.GraphBg = B("#2E3440"); s.Separator = B("#5E81AC");
            s.GraphBorder = Thin("#4C566A"); s.GraphGrid = Thin("#353B49");
            s.CalHead = B("#4C566A"); s.Today = B("#BF616A"); s.CalSun = B("#BF616A"); s.CalSat = B("#81A1C1");
            s.DateTile = Gradient("#5E81AC", "#4C6A94"); s.DateTileText = B("#ECEFF4");
            s.Sun = B("#EBCB8B"); s.CloudLight = B("#D8DEE9"); s.CloudDark = B("#8A94A6"); s.Rain = B("#88C0D0");
            s.Snow = B("#ECEFF4"); s.MoonLit = B("#E5E9F0"); s.MoonDark = B("#4C566A");
            s.Palette = Brushes("#A3BE8C", "#EBCB8B", "#D08770", "#BF616A", "#B48EAD", "#81A1C1", "#88C0D0", "#8FBCBB");
            s.Tiles = Brushes("#A3BE8C", "#81A1C1", "#88C0D0", "#5E6B80", "#B48EAD", "#BF616A");
            return s;
        }

        // Dracula 配色。紫とピンク
        public static Skin Dracula()
        {
            var s = new Skin();
            s.Name = "Dracula";
            s.CornerRadius = 8;
            s.Background = Hex("#282A36");
            s.Border = Thin("#44475A");
            s.TileRadius = 8; s.GraphArea = true;
            s.Title = B("#F8F8F2"); s.Text1 = B("#F1FA8C"); s.Text2 = B("#8BE9FD");
            s.Accent1 = B("#50FA7B"); s.Accent2 = B("#FFB86C"); s.Lime = B("#50FA7B");
            s.Warn = B("#FFB86C"); s.Danger = B("#FF5555"); s.Muted = B("#8A93C4"); s.Dim = B("#4D5068");
            s.Magenta = B("#FF79C6"); s.Violet = B("#BD93F9"); s.Blue = B("#8BE9FD");
            s.Track = B("#44475A"); s.GraphBg = B("#21222C"); s.Separator = B("#BD93F9");
            s.GraphBorder = Thin("#44475A"); s.GraphGrid = Thin("#343746");
            s.CalHead = B("#44475A"); s.Today = B("#FF79C6"); s.CalSun = B("#FF5555"); s.CalSat = B("#8BE9FD");
            s.DateTile = Gradient("#BD93F9", "#7B4FD9"); s.DateTileText = B("#F8F8F2");
            s.Sun = B("#F1FA8C"); s.CloudLight = B("#D6D8E8"); s.CloudDark = B("#6272A4"); s.Rain = B("#8BE9FD");
            s.Snow = B("#F8F8F2"); s.MoonLit = B("#F8F8F2"); s.MoonDark = B("#44475A");
            s.Palette = Brushes("#50FA7B", "#F1FA8C", "#FFB86C", "#FF5555", "#FF79C6", "#BD93F9", "#8BE9FD", "#6272A4");
            s.Tiles = Brushes("#50FA7B", "#8BE9FD", "#BD93F9", "#6272A4", "#FF79C6", "#FFB86C");
            return s;
        }

        // すりガラス。半透明の白いカードと泡
        public static Skin Frost()
        {
            var s = new Skin();
            s.Name = "Frost";
            s.CornerRadius = 16;
            s.Background = Hex("#1E2630");
            s.BackgroundBrush = Gradient("#243040", "#141A24");
            s.Border = Thin("#55FFFFFF");
            s.CardBrush = Argb("#1AFFFFFF"); s.CardBorder = Thin("#40FFFFFF"); s.CardRadius = 14;
            s.TileRadius = 12; s.BarStyle = "pill"; s.GraphArea = true;
            s.Decor = "bubbles"; s.DecorColor = Hex("#FFFFFF");
            s.Title = B("#F2F6FA"); s.Text1 = B("#FFD98A"); s.Text2 = B("#9ED8FF");
            s.Accent1 = B("#8CE0B0"); s.Accent2 = B("#FFB080"); s.Lime = B("#A8F0C0");
            s.Warn = B("#FFB080"); s.Danger = B("#FF7A8A"); s.Muted = B("#AFC0D0"); s.Dim = B("#6A7C90");
            s.Magenta = B("#F0A0E8"); s.Violet = B("#C4B0FF"); s.Blue = B("#8FB4FF");
            s.Track = Argb("#2AFFFFFF"); s.GraphBg = Argb("#60101822"); s.Separator = Argb("#66FFFFFF");
            s.GraphBorder = P(Argb("#40FFFFFF"), 1); s.GraphGrid = P(Argb("#18FFFFFF"), 1);
            s.CalHead = Argb("#30FFFFFF"); s.Today = B("#5CC8FF"); s.CalSun = B("#FF7A8A"); s.CalSat = B("#8FB4FF");
            s.DateTile = Argb("#40FFFFFF"); s.DateTileText = B("#F2F6FA");
            s.Sun = B("#FFD98A"); s.CloudLight = B("#E8EEF5"); s.CloudDark = B("#A0B0C0"); s.Rain = B("#9ED8FF");
            s.Snow = B("#FFFFFF"); s.MoonLit = B("#F2F6FA"); s.MoonDark = Argb("#50FFFFFF");
            s.Palette = Brushes("#8CE0B0", "#FFD98A", "#FFB080", "#FF7A8A", "#F0A0E8", "#C4B0FF", "#8FB4FF",
                "#9ED8FF", "#C6F08A", "#FFE6A0", "#FFC080", "#FFA0B0", "#D0C0FF", "#A0E8E0");
            s.TileOverride = Argb("#33FFFFFF");
            return s;
        }

        // 紙のノート。罫線と手書き風の文字
        public static Skin Notebook()
        {
            var s = new Skin();
            s.Name = "Notebook";
            s.FontFamily = "Segoe Print, UD Digi Kyokasho NK-R, Yu Gothic UI";
            s.CornerRadius = 4;
            s.Background = Hex("#FBF7EC");
            s.Border = Thin("#D8CFBC");
            s.TileRadius = 4;
            s.Decor = "lines"; s.DecorColor = Hex("#9EC3E6");
            s.Title = B("#3A3530"); s.Text1 = B("#B5651D"); s.Text2 = B("#2F6F9F");
            s.Accent1 = B("#3C8D5A"); s.Accent2 = B("#D2691E"); s.Lime = B("#3C8D5A");
            s.Warn = B("#D2691E"); s.Danger = B("#C0392B"); s.Muted = B("#8A8075"); s.Dim = B("#B8AE9E");
            s.Magenta = B("#B04A8A"); s.Violet = B("#6C5CB7"); s.Blue = B("#2F6F9F");
            s.Track = B("#EFE7D6"); s.GraphBg = B("#FFFDF6"); s.Separator = B("#C9BFAA");
            s.GraphBorder = Thin("#D8CFBC"); s.GraphGrid = Thin("#EEE6D6");
            s.CalHead = B("#EBDFC2"); s.Today = B("#E0564B"); s.CalSun = B("#C0392B"); s.CalSat = B("#2F6F9F");
            s.DateTile = Gradient("#F2D8A0", "#E5C27A"); s.DateTileText = B("#3A3530");
            s.Sun = B("#E8A317"); s.CloudLight = B("#D9D2C4"); s.CloudDark = B("#A69D8E"); s.Rain = B("#2F6F9F");
            s.Snow = B("#7FA7D0"); s.MoonLit = B("#6C5CB7"); s.MoonDark = B("#E6DFCF");
            s.Palette = Brushes("#3C8D5A", "#C8961E", "#D2691E", "#C0392B", "#B04A8A", "#6C5CB7", "#2F6F9F",
                "#2E8B8B", "#7A9A2E", "#8A8075", "#B5651D", "#A3426E", "#5C4FB0", "#2E7F6E");
            s.Tiles = Brushes("#6FA882", "#7A9BC2", "#6EAFAF", "#A79F93", "#9A8CC9", "#C98A86");
            return s;
        }
    }
}

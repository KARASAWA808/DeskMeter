using System;
using System.Windows.Media;

namespace DeskMeter
{
    // 追加スキン その 2。定番のダーク配色 (Tokyo Night / Gruvbox / Mocha)、雰囲気のあるもの (夜桜 / オーロラ / 設計図 / 携帯ゲーム機 / 黒と金)、
    // 見やすさ優先のもの (白黒と赤 / ハイコントラスト)。
    // 文字の色は背景に対してコントラスト比 4.5 以上 (Dim だけは 3 前後) になるように選んでいる。
    // 見出しアイコンとカレンダーの「今日」の文字は Title の色で描かれるので、Tiles / Today は Title と見分けがつく明るさにする。
    sealed partial class Skin
    {
        // 紺の夜景。淡い青紫と緑、オレンジ
        public static Skin TokyoNight()
        {
            var s = new Skin();
            s.Name = "TokyoNight";
            s.CornerRadius = 8;
            s.Background = Hex("#1A1B26");
            s.Border = Thin("#2F334D");
            s.TileRadius = 8; s.GraphArea = true;
            s.Title = B("#C0CAF5"); s.Text1 = B("#E0AF68"); s.Text2 = B("#7AA2F7");
            s.Accent1 = B("#9ECE6A"); s.Accent2 = B("#FF9E64"); s.Lime = B("#9ECE6A");
            s.Warn = B("#FF9E64"); s.Danger = B("#F7768E"); s.Muted = B("#8E96BD"); s.Dim = B("#59628F");
            s.Magenta = B("#BB9AF7"); s.Violet = B("#A990E8"); s.Blue = B("#7DCFFF");
            s.Track = B("#292E42"); s.GraphBg = B("#16161E"); s.Separator = B("#3D59A1");
            s.GraphBorder = Thin("#2F334D"); s.GraphGrid = Thin("#1F2335");
            s.CalHead = B("#292E42"); s.Today = B("#3D59A1"); s.CalSun = B("#F7768E"); s.CalSat = B("#7DCFFF");
            s.DateTile = Gradient("#4F6FD0", "#2F3F86"); s.DateTileText = B("#FFFFFF");
            s.DateTileFont = "Bahnschrift, Segoe UI"; s.DateTileWeight = "semibold";
            s.Sun = B("#E0AF68"); s.CloudLight = B("#C0CAF5"); s.CloudDark = B("#59628F"); s.Rain = B("#7DCFFF");
            s.Snow = B("#C0CAF5"); s.MoonLit = B("#C0CAF5"); s.MoonDark = B("#292E42");
            s.Palette = Brushes("#7AA2F7", "#9ECE6A", "#E0AF68", "#F7768E", "#BB9AF7", "#7DCFFF", "#FF9E64",
                "#2AC3DE", "#73DACA", "#B4F9F8", "#C0CAF5", "#A990E8");
            s.Tiles = Brushes("#3D59A1", "#5A4A9C", "#2A7F8F", "#414868", "#7C4D8F", "#9A4A5E");
            return s;
        }

        // 暖色のレトロ配色。こげ茶に黄・橙・若草色
        public static Skin Gruvbox()
        {
            var s = new Skin();
            s.Name = "Gruvbox";
            s.CornerRadius = 4;
            s.Background = Hex("#282828");
            s.Border = Thin("#504945");
            s.TileRadius = 4;
            s.Title = B("#EBDBB2"); s.Text1 = B("#FABD2F"); s.Text2 = B("#83A598");
            s.Accent1 = B("#B8BB26"); s.Accent2 = B("#FE8019"); s.Lime = B("#B8BB26");
            s.Warn = B("#FE8019"); s.Danger = B("#FB4934"); s.Muted = B("#A89984"); s.Dim = B("#7C6F64");
            s.Magenta = B("#D3869B"); s.Violet = B("#C9A0C4"); s.Blue = B("#458588");
            s.Track = B("#3C3836"); s.GraphBg = B("#1D2021"); s.Separator = B("#665C54");
            s.GraphBorder = Thin("#504945"); s.GraphGrid = Thin("#32302F");
            s.CalHead = B("#504945"); s.Today = B("#AF3A03"); s.CalSun = B("#FB4934"); s.CalSat = B("#83A598");
            s.DateTile = Gradient("#D79921", "#B57614"); s.DateTileText = B("#282828");
            s.DateTileFont = "Bahnschrift, Segoe UI"; s.DateTileWeight = "semibold";
            s.Sun = B("#FABD2F"); s.CloudLight = B("#D5C4A1"); s.CloudDark = B("#7C6F64"); s.Rain = B("#83A598");
            s.Snow = B("#EBDBB2"); s.MoonLit = B("#EBDBB2"); s.MoonDark = B("#3C3836");
            s.Palette = Brushes("#B8BB26", "#FABD2F", "#FE8019", "#FB4934", "#D3869B", "#83A598", "#8EC07C",
                "#D79921", "#A9AB2E", "#E0685A", "#C9A0C4", "#6FA3A6", "#7FB08A", "#E8843A");
            s.Tiles = Brushes("#79740E", "#076678", "#427B58", "#665C54", "#8F3F71", "#9D0006");
            return s;
        }

        // ダークパステル。紺のカードにやわらかい色
        public static Skin Mocha()
        {
            var s = new Skin();
            s.Name = "Mocha";
            s.CornerRadius = 14;
            s.Background = Hex("#1E1E2E");
            s.CardBrush = B("#27273A"); s.CardRadius = 12;
            s.TileRadius = 10; s.BarStyle = "pill"; s.GraphArea = true;
            s.Title = B("#CDD6F4"); s.Text1 = B("#F9E2AF"); s.Text2 = B("#89DCEB");
            s.Accent1 = B("#A6E3A1"); s.Accent2 = B("#FAB387"); s.Lime = B("#A6E3A1");
            s.Warn = B("#FAB387"); s.Danger = B("#F38BA8"); s.Muted = B("#A6ADC8"); s.Dim = B("#7F849C");
            s.Magenta = B("#F5C2E7"); s.Violet = B("#CBA6F7"); s.Blue = B("#89B4FA");
            s.Track = B("#45475A"); s.GraphBg = B("#181825"); s.Separator = B("#585B70");
            s.GraphBorder = Thin("#45475A"); s.GraphGrid = Thin("#252536");
            s.CalHead = B("#45475A"); s.Today = B("#7F5FD9"); s.CalSun = B("#F38BA8"); s.CalSat = B("#89B4FA");
            s.DateTile = Gradient("#CBA6F7", "#A583EE"); s.DateTileText = B("#1E1E2E");
            s.DateTileFont = "Bahnschrift, Segoe UI"; s.DateTileWeight = "semibold";
            s.Sun = B("#F9E2AF"); s.CloudLight = B("#CDD6F4"); s.CloudDark = B("#7F849C"); s.Rain = B("#89DCEB");
            s.Snow = B("#CDD6F4"); s.MoonLit = B("#F5E0DC"); s.MoonDark = B("#45475A");
            s.Palette = Brushes("#A6E3A1", "#F9E2AF", "#FAB387", "#F38BA8", "#F5C2E7", "#CBA6F7", "#89B4FA",
                "#89DCEB", "#94E2D5", "#F2CDCD", "#B4BEFE", "#EBA0AC", "#74C7EC", "#F5E0DC");
            s.Tiles = Brushes("#4E7A52", "#3F5C99", "#2F7F86", "#565970", "#6E4FA8", "#9A4763");
            return s;
        }

        // 夜桜。深い紫紺に桜の花びら、金と桜色
        public static Skin Yozakura()
        {
            var s = new Skin();
            s.Name = "Yozakura";
            s.CornerRadius = 12;
            s.Background = Hex("#201638");
            s.BackgroundBrush = Gradient("#1B1433", "#2E1A47");
            s.Border = Thin("#6B4A7A");
            s.TileRadius = 18; s.BarStyle = "pill"; s.GraphArea = true;
            s.Decor = "sakura"; s.DecorColor = Hex("#FF9EC4");
            s.Title = B("#FBEFF5"); s.Text1 = B("#F4C77B"); s.Text2 = B("#F7A8C8");
            s.Accent1 = B("#A8D8B0"); s.Accent2 = B("#F2A65A"); s.Lime = B("#C6E8B0");
            s.Warn = B("#F2A65A"); s.Danger = B("#FF6B8B"); s.Muted = B("#B9A6C9"); s.Dim = B("#77648E");
            s.Magenta = B("#F78FB3"); s.Violet = B("#C3A6F2"); s.Blue = B("#9DB7F5");
            s.Track = B("#3A2A55"); s.GraphBg = Argb("#A0150E28"); s.Separator = B("#8A5A8F");
            s.GraphBorder = Thin("#4A3566"); s.GraphGrid = Thin("#2A1D42");
            s.CalHead = B("#45305F"); s.Today = B("#C23E72"); s.CalSun = B("#FF6B8B"); s.CalSat = B("#9DB7F5");
            s.DateTile = Gradient("#D9487D", "#8A2A5A"); s.DateTileText = B("#FFFFFF");
            s.DateTileFont = "Yu Mincho, BIZ UDMincho, Segoe UI"; s.DateTileWeight = "bold";
            s.Sun = B("#F4C77B"); s.CloudLight = B("#E6D8EE"); s.CloudDark = B("#8E7AA8"); s.Rain = B("#9DB7F5");
            s.Snow = B("#FFFFFF"); s.MoonLit = B("#FFF1C9"); s.MoonDark = B("#3A2A55");
            s.Palette = Brushes("#F78FB3", "#F4C77B", "#A8D8B0", "#9DB7F5", "#C3A6F2", "#F2A65A", "#7FD1D1",
                "#FF6B8B", "#E8E08A", "#B5A1F0", "#F7B7D2", "#8FD9A8", "#D98AE0", "#F2D0A0");
            s.Tiles = Brushes("#B5446E", "#5A5AA8", "#3F8A8A", "#5E4B78", "#8A4FA8", "#C2703D");
            return s;
        }

        // オーロラ。濃い青緑の夜空に星、緑と紫の光
        public static Skin Aurora()
        {
            var s = new Skin();
            s.Name = "Aurora";
            s.CornerRadius = 14;
            s.Background = Hex("#0A2233");
            s.BackgroundBrush = Gradient("#071A2B", "#0E3B3F");
            s.Border = Thin("#2FA88A");
            s.TileRadius = 10; s.BarStyle = "pill"; s.GraphArea = true;
            s.Decor = "stars"; s.DecorColor = Hex("#9FF5D0");
            s.Title = B("#EAF7F5"); s.Text1 = B("#B9F27C"); s.Text2 = B("#6FE3D0");
            s.Accent1 = B("#57E389"); s.Accent2 = B("#C792EA"); s.Lime = B("#A6F7C1");
            s.Warn = B("#FFCB6B"); s.Danger = B("#FF6E8A"); s.Muted = B("#9DB8C0"); s.Dim = B("#5C7D8A");
            s.Magenta = B("#E58CE0"); s.Violet = B("#B79BFF"); s.Blue = B("#6FB8FF");
            s.Track = B("#153443"); s.GraphBg = Argb("#90061018"); s.Separator = B("#2FA88A");
            s.GraphBorder = Thin("#1F4A58"); s.GraphGrid = Thin("#0F2A37");
            s.CalHead = B("#174552"); s.Today = B("#178066"); s.CalSun = B("#FF6E8A"); s.CalSat = B("#6FB8FF");
            s.DateTile = Gradient("#178A78", "#4A3FB0"); s.DateTileText = B("#FFFFFF");
            s.DateTileFont = "Bahnschrift, Segoe UI"; s.DateTileWeight = "semibold";
            s.Sun = B("#FFCB6B"); s.CloudLight = B("#D6ECEC"); s.CloudDark = B("#6F93A0"); s.Rain = B("#6FB8FF");
            s.Snow = B("#FFFFFF"); s.MoonLit = B("#EAF7F5"); s.MoonDark = B("#153443");
            s.Palette = Brushes("#57E389", "#6FE3D0", "#B9F27C", "#B79BFF", "#6FB8FF", "#E58CE0", "#FFCB6B",
                "#3DD6B0", "#8AF0A0", "#9AD0FF", "#D0A0FF", "#F0E080", "#FF9EB0", "#5CE0E0");
            s.Tiles = Brushes("#178A78", "#3A5FB8", "#2A8FA0", "#3F5A68", "#6A4FC0", "#A04A80");
            return s;
        }

        // 設計図。青地に白い線と方眼、等幅の文字
        public static Skin Blueprint()
        {
            var s = new Skin();
            s.Name = "Blueprint";
            s.FontFamily = "Consolas, MS Gothic, Yu Gothic UI";
            s.CornerRadius = 0;
            s.Background = Hex("#0E3A6B");
            s.BackgroundBrush = Gradient("#114A85", "#0B2F5A");
            s.Border = Thin("#CFE6FF");
            s.TileRadius = 0; s.Sharp = true;
            s.Decor = "squares"; s.DecorColor = Hex("#CFE6FF");
            s.Title = B("#F2F8FF"); s.Text1 = B("#FFE08A"); s.Text2 = B("#9FD4FF");
            s.Accent1 = B("#7CF0C8"); s.Accent2 = B("#FFB36B"); s.Lime = B("#B6F5D8");
            s.Warn = B("#FFB36B"); s.Danger = B("#FF9A9A"); s.Muted = B("#A9C6E8"); s.Dim = B("#6F95C6");
            s.Magenta = B("#F0A8E0"); s.Violet = B("#C4B8FF"); s.Blue = B("#8FC0FF");
            s.Track = B("#1B4F8A"); s.GraphBg = Argb("#70082445"); s.Separator = B("#8FBCE8");
            s.GraphBorder = Thin("#8FBCE8"); s.GraphGrid = Thin("#2A5F9A");
            s.CalHead = B("#1B4F8A"); s.Today = B("#C0504D"); s.CalSun = B("#FF9A9A"); s.CalSat = B("#9FD4FF");
            s.DateTile = Argb("#30FFFFFF"); s.DateTileText = B("#F2F8FF");
            s.DateTileWeight = "bold";
            s.Sun = B("#FFE08A"); s.CloudLight = B("#F2F8FF"); s.CloudDark = B("#8FBCE8"); s.Rain = B("#9FD4FF");
            s.Snow = B("#FFFFFF"); s.MoonLit = B("#F2F8FF"); s.MoonDark = B("#1B4F8A");
            s.Palette = Brushes("#F2F8FF", "#9FD4FF", "#FFE08A", "#7CF0C8", "#FFB36B", "#C4B8FF", "#F0A8E0", "#8FC0FF");
            s.TileOverride = Argb("#30FFFFFF");
            return s;
        }

        // 昔の携帯ゲーム機の液晶。黄緑の濃淡だけ、ブロック状のバー
        public static Skin Pocket()
        {
            var s = new Skin();
            s.Name = "Pocket";
            s.FontFamily = "Consolas, MS Gothic, Yu Gothic UI";
            s.CornerRadius = 0;
            s.Background = Hex("#9BBC0F");
            s.Border = P(B("#0F380F"), 2);
            s.TileRadius = 0; s.BarStyle = "segment"; s.Sharp = true;
            s.Title = B("#0F380F"); s.Text1 = B("#0F380F"); s.Text2 = B("#1E4A1E");
            s.Accent1 = B("#234F16"); s.Accent2 = B("#0F380F"); s.Lime = B("#1E4A1E");
            s.Warn = B("#0F380F"); s.Danger = B("#0F380F"); s.Muted = B("#224E1C"); s.Dim = B("#456A18");
            s.Magenta = B("#1E4A1E"); s.Violet = B("#234F16"); s.Blue = B("#0F380F");
            s.Track = B("#8BAC0F"); s.GraphBg = B("#8BAC0F"); s.Separator = B("#306230");
            s.GraphBorder = Thin("#306230"); s.GraphGrid = Thin("#7E9E0C");
            s.CalHead = B("#8BAC0F"); s.Today = B("#CADC5A"); s.CalSun = B("#0F380F"); s.CalSat = B("#1E4A1E");
            s.DateTile = B("#234F16"); s.DateTileText = B("#9BBC0F");
            s.DateTileWeight = "bold";
            s.Sun = B("#0F380F"); s.CloudLight = B("#306230"); s.CloudDark = B("#0F380F"); s.Rain = B("#0F380F");
            s.Snow = B("#306230"); s.MoonLit = B("#0F380F"); s.MoonDark = B("#8BAC0F");
            s.Palette = Brushes("#0F380F", "#1E4A1E", "#16401A", "#234F16");
            s.TileOverride = B("#8BAC0F");
            return s;
        }

        // 黒と金。細い金の枠、金・銀・ブロンズの濃淡
        public static Skin NoirGold()
        {
            var s = new Skin();
            s.Name = "NoirGold";
            s.CornerRadius = 2;
            s.Background = Hex("#0B0B0D");
            s.Border = Thin("#C9A24A");
            s.TileRadius = 2;
            s.Title = B("#F3E9D2"); s.Text1 = B("#E6C36A"); s.Text2 = B("#C8CCD4");
            s.Accent1 = B("#E6C36A"); s.Accent2 = B("#C99A45"); s.Lime = B("#F0D890");
            s.Warn = B("#E0933A"); s.Danger = B("#E0564B"); s.Muted = B("#A39A86"); s.Dim = B("#6A6355");
            s.Magenta = B("#D9B8A0"); s.Violet = B("#BFC3CC"); s.Blue = B("#8FA3B8");
            s.Track = B("#1E1C18"); s.GraphBg = B("#060607"); s.Separator = B("#7A6330");
            s.GraphBorder = Thin("#3A3220"); s.GraphGrid = Thin("#17150F");
            s.CalHead = B("#2A2416"); s.Today = B("#86661F"); s.CalSun = B("#E0564B"); s.CalSat = B("#8FA3B8");
            s.DateTile = Gradient("#E6C36A", "#B8893A"); s.DateTileText = B("#0B0B0D");
            s.DateTileFont = "Palatino Linotype, Times New Roman, Segoe UI"; s.DateTileWeight = "bold";
            s.Sun = B("#E6C36A"); s.CloudLight = B("#D8D2C2"); s.CloudDark = B("#7A7466"); s.Rain = B("#8FA3B8");
            s.Snow = B("#F3E9D2"); s.MoonLit = B("#F3E9D2"); s.MoonDark = B("#2A2416");
            s.Palette = Brushes("#E6C36A", "#C8CCD4", "#C99A45", "#F0D890", "#8FA3B8", "#D9B8A0", "#A39A86",
                "#E0933A", "#F3E9D2", "#B08A38", "#BFC3CC", "#D4A94E");
            s.TileOverride = B("#3A2F14");
            return s;
        }

        // 白黒と赤だけ。明るい背景、直線だけの構成
        public static Skin Mono()
        {
            var s = new Skin();
            s.Name = "Mono";
            s.CornerRadius = 0;
            s.Background = Hex("#F5F5F2");
            s.Border = P(B("#1A1A1A"), 1.5);
            s.TileRadius = 0; s.Sharp = true;
            s.Title = B("#111111"); s.Text1 = B("#111111"); s.Text2 = B("#333333");
            s.Accent1 = B("#111111"); s.Accent2 = B("#D0021B"); s.Lime = B("#444444");
            s.Warn = B("#D0021B"); s.Danger = B("#D0021B"); s.Muted = B("#5C5C5C"); s.Dim = B("#8E8E8E");
            s.Magenta = B("#4A4A4A"); s.Violet = B("#5C5C5C"); s.Blue = B("#333333");
            s.Track = B("#DEDEDA"); s.GraphBg = B("#FFFFFF"); s.Separator = B("#111111");
            s.GraphBorder = Thin("#111111"); s.GraphGrid = Thin("#E6E6E2");
            s.CalHead = B("#DEDEDA"); s.Today = B("#F0605A"); s.CalSun = B("#D0021B"); s.CalSat = B("#333333");
            s.DateTile = B("#111111"); s.DateTileText = B("#F5F5F2");
            s.DateTileFont = "Bahnschrift, Segoe UI"; s.DateTileWeight = "bold";
            s.Sun = B("#D0021B"); s.CloudLight = B("#BDBDBD"); s.CloudDark = B("#6B6B6B"); s.Rain = B("#333333");
            s.Snow = B("#8E8E8E"); s.MoonLit = B("#111111"); s.MoonDark = B("#DEDEDA");
            s.Palette = Brushes("#111111", "#3A3A3A", "#5A5A5A", "#D0021B", "#222222", "#4A4A4A", "#666666", "#B00018");
            s.TileOverride = B("#DEDEDA");
            return s;
        }

        // 見やすさ優先。真っ黒に白・黄・水色、太い枠、角丸なし
        public static Skin HighContrast()
        {
            var s = new Skin();
            s.Name = "HighContrast";
            s.CornerRadius = 0;
            s.Background = Hex("#000000");
            s.Border = P(B("#FFFFFF"), 2);
            s.TileRadius = 0; s.Sharp = true;
            s.Title = B("#FFFFFF"); s.Text1 = B("#FFFF00"); s.Text2 = B("#00FFFF");
            s.Accent1 = B("#00FF00"); s.Accent2 = B("#FF9900"); s.Lime = B("#7CFF7C");
            s.Warn = B("#FF9900"); s.Danger = B("#FF6060"); s.Muted = B("#D0D0D0"); s.Dim = B("#8C8C8C");
            s.Magenta = B("#FF80FF"); s.Violet = B("#C0A0FF"); s.Blue = B("#66A3FF");
            s.Track = B("#3A3A3A"); s.GraphBg = B("#000000"); s.Separator = B("#FFFFFF");
            s.GraphBorder = Thin("#FFFFFF"); s.GraphGrid = Thin("#3A3A3A");
            s.CalHead = B("#003C9E"); s.Today = B("#B00020"); s.CalSun = B("#FF6060"); s.CalSat = B("#66A3FF");
            s.DateTile = B("#FFFFFF"); s.DateTileText = B("#000000");
            s.DateTileFont = "Bahnschrift, Segoe UI"; s.DateTileWeight = "bold";
            s.Sun = B("#FFFF00"); s.CloudLight = B("#FFFFFF"); s.CloudDark = B("#A0A0A0"); s.Rain = B("#00FFFF");
            s.Snow = B("#FFFFFF"); s.MoonLit = B("#FFFFFF"); s.MoonDark = B("#3A3A3A");
            s.Palette = Brushes("#00FF00", "#FFFF00", "#00FFFF", "#FF80FF", "#FF9900", "#66A3FF", "#FFFFFF", "#FF6060");
            s.Tiles = Brushes("#006400", "#003C9E", "#006C6C", "#4A4A4A", "#5A2DA0", "#9E0030");
            return s;
        }
    }
}

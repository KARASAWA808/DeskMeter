using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace DeskMeter
{
    // 組み込みスキンの見やすさの仕上げ。
    // 文字の色は下地 (背景・カード) に対してコントラスト比 4.5 以上になるようにする。足りない色は、色味を保ったまま
    // 明るさだけ動かした「文字用の色」を TextMap に入れる (バーやグラフの塗りは元の色のまま)。
    // 見出しアイコンや「今日」のように色付きの面に載る文字は、その面に対して読める色を選ぶ。
    // 自作スキン (skins\*.json) には手を加えない。
    sealed partial class Skin
    {
        public Brush TileIcon;      // 見出しアイコンの色 (null なら Title)
        public Brush TodayText;     // カレンダーの「今日」と今日の曜日の文字 (null なら Title)
        public Dictionary<Brush, Brush> TextMap;   // 文字として描くときの色の読み替え (null なら無し)

        static double Lin(byte v) { double c = v / 255.0; return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        internal static double Lum(Color c) { return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B); }

        internal static double Ratio(Color a, Color b)
        {
            double la = Lum(a), lb = Lum(b);
            return la >= lb ? (la + 0.05) / (lb + 0.05) : (lb + 0.05) / (la + 0.05);
        }

        internal static Color Over(Color fg, Color bg)
        {
            if (fg.A == 255) return fg;
            double a = fg.A / 255.0;
            return Color.FromRgb((byte)Math.Round(fg.R * a + bg.R * (1 - a)), (byte)Math.Round(fg.G * a + bg.G * (1 - a)), (byte)Math.Round(fg.B * a + bg.B * (1 - a)));
        }

        static Color Mix(Color a, Color b, double t)
        {
            return Color.FromRgb((byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
        }

        internal static List<Color> ColorsOf(Brush b)
        {
            var r = new List<Color>();
            var s = b as SolidColorBrush;
            if (s != null) { r.Add(s.Color); return r; }
            var g = b as GradientBrush;
            if (g != null) foreach (GradientStop st in g.GradientStops) r.Add(st.Color);
            return r;
        }

        // 文字の下地になる色: カードがあればカード、無ければ背景 (グラデーションなら両端)。半透明は黒い壁紙の上として計算
        internal List<Color> Surfaces()
        {
            var bases = new List<Color>();
            if (BackgroundBrush != null) foreach (Color c in ColorsOf(BackgroundBrush)) bases.Add(Over(c, Colors.Black));
            if (bases.Count == 0) bases.Add(Over(Background, Colors.Black));
            if (CardBrush == null) return bases;
            return Layer(CardBrush, bases);
        }

        // 面 (brush) を下地に重ねたときの色
        internal static List<Color> Layer(Brush brush, List<Color> under)
        {
            var r = new List<Color>();
            foreach (Color u in under) foreach (Color c in ColorsOf(brush)) r.Add(Over(c, u));
            return r.Count > 0 ? r : under;
        }

        internal static double MinRatio(Color fg, List<Color> bgs)
        {
            double min = double.MaxValue;
            foreach (Color b in bgs) min = Math.Min(min, Ratio(Over(fg, b), b));
            return min;
        }

        internal static double MinRatio(Brush fg, List<Color> bgs)
        {
            double min = double.MaxValue;
            foreach (Color c in ColorsOf(fg)) min = Math.Min(min, MinRatio(c, bgs));
            return min;
        }

        // 下地に対して need 以上になるまで、暗い下地なら白へ、明るい下地なら黒へ少しずつ寄せる
        static Brush Fit(Brush b, List<Color> bgs, double need)
        {
            var s = b as SolidColorBrush;
            if (s == null || MinRatio(s.Color, bgs) >= need) return b;
            double avg = 0;
            foreach (Color c in bgs) avg += Lum(c);
            Color target = avg / bgs.Count < 0.2 ? Colors.White : Colors.Black;
            Color from = Over(s.Color, bgs[0]), best = target;
            for (int i = 1; i <= 50; i++)
            {
                Color c = Mix(from, target, i / 50.0);
                if (MinRatio(c, bgs) >= need) { best = c; break; }
            }
            var nb = new SolidColorBrush(best);
            nb.Freeze();
            return nb;
        }

        // 色付きの面に載せる文字の色。いまの色で読めればそのまま、だめならスキンの地の色、それでもだめなら白か黒の読めるほう
        static Brush OnSurface(Brush current, List<Color> surface, Color ink, double need)
        {
            if (MinRatio(current, surface) >= need) return current;
            Color pick = ink;
            if (MinRatio(ink, surface) < need)
            {
                Color dark = Color.FromRgb(0x14, 0x16, 0x1A);
                pick = MinRatio(Colors.White, surface) >= MinRatio(dark, surface) ? Colors.White : dark;
            }
            var nb = new SolidColorBrush(pick);
            nb.Freeze();
            return nb;
        }

        static Skin Polish(Skin s)
        {
            List<Color> bgs = s.Surfaces();
            var map = new Dictionary<Brush, Brush>();
            // グラフの中の小さな文字は Muted で描くので、グラフの下地でも読めるようにする
            var mutedBgs = new List<Color>(bgs);
            mutedBgs.AddRange(Layer(s.GraphBg, bgs));
            AddText(map, s.Muted, mutedBgs);
            foreach (Brush b in new[] { s.Title, s.Text1, s.Text2, s.Accent1, s.Accent2, s.Lime, s.Warn, s.Danger, s.Magenta, s.Violet, s.Blue, s.CalSun, s.CalSat })
                AddText(map, b, bgs);
            foreach (Brush b in s.Palette) AddText(map, b, bgs);
            s.TextMap = map.Count > 0 ? map : null;
            s.Dim = Fit(s.Dim, bgs, 2.4);       // 前後の月の日付。薄くてよいが、見えないほどにはしない
            s.Track = Fit(s.Track, bgs, 1.3);   // バーの下地。どこまでが 100% か分かる程度に
            var tiles = new List<Color>();
            if (s.TileOverride != null) tiles.AddRange(Layer(s.TileOverride, bgs));
            else foreach (Brush t in s.Tiles) tiles.AddRange(Layer(t, bgs));
            if (s.TileIcon == null) s.TileIcon = OnSurface(s.Title, tiles, bgs[0], 3.0);
            if (s.TodayText == null) s.TodayText = OnSurface(s.Title, Layer(s.Today, bgs), bgs[0], 4.5);
            s.DateTileText = OnSurface(s.DateTileText, Layer(s.DateTile, bgs), bgs[0], 3.3);
            return s;
        }

        static void AddText(Dictionary<Brush, Brush> map, Brush b, List<Color> bgs)
        {
            if (b == null || map.ContainsKey(b)) return;
            Brush f = Fit(b, bgs, 4.5);
            if (!ReferenceEquals(f, b)) map[b] = f;
        }
    }
}

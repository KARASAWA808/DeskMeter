using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace DeskMeter
{
    // スキンの飾り: グラデーション背景、パネルごとのカード、背景の模様、マスコット
    sealed partial class MeterView
    {
        readonly List<PanelBox> prevBoxes = new List<PanelBox>();

        static Brush Alpha(Color c, byte a)
        {
            var b = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));
            b.Freeze();
            return b;
        }

        Brush bgCache;
        double bgCacheOp = -1;

        // 背景 (単色 or グラデーション) に不透明度を掛けて描く。ブラシは不透明度が変わるまで使い回す
        void DrawBackground(DrawingContext dc)
        {
            double op = Math.Max(1.0 / 255, Math.Min(1, cfg.BackgroundOpacity));
            if (bgCache == null || op != bgCacheOp)
            {
                if (sk.BackgroundBrush != null)
                {
                    Brush bg = sk.BackgroundBrush.Clone();
                    bg.Opacity = op;
                    bg.Freeze();
                    bgCache = bg;
                }
                else bgCache = Alpha(sk.Background, (byte)Math.Round(op * 255));
                bgCacheOp = op;
            }
            dc.DrawRoundedRectangle(bgCache, null, new Rect(0, 0, contentW, contentH), sk.CornerRadius, sk.CornerRadius);
        }

        // 前のフレームのパネル矩形を使ってカードを敷く (配置はめったに変わらない)
        void DrawCards(DrawingContext dc)
        {
            if (sk.CardBrush == null) return;
            foreach (PanelBox b in prevBoxes)
            {
                var r = new Rect(b.R.X - 7, b.R.Y - 5, b.R.Width + 14, b.R.Height + 10);
                dc.DrawRoundedRectangle(sk.CardBrush, sk.CardBorder, r, sk.CardRadius, sk.CardRadius);
            }
        }

        // 背景グラデーションの、上から t (0〜1) の位置の色。単色ならそのまま
        Color BackgroundAt(double t)
        {
            var g = sk.BackgroundBrush as LinearGradientBrush;
            if (g == null || g.GradientStops.Count == 0) return sk.Background;
            GradientStop lo = null, hi = null;
            foreach (GradientStop gs in g.GradientStops)
            {
                if (gs.Offset <= t && (lo == null || gs.Offset >= lo.Offset)) lo = gs;
                if (gs.Offset >= t && (hi == null || gs.Offset <= hi.Offset)) hi = gs;
            }
            if (lo == null) return hi.Color;
            if (hi == null || hi.Offset <= lo.Offset) return lo.Color;
            double f = (t - lo.Offset) / (hi.Offset - lo.Offset);
            return Color.FromRgb((byte)(lo.Color.R + (hi.Color.R - lo.Color.R) * f), (byte)(lo.Color.G + (hi.Color.G - lo.Color.G) * f), (byte)(lo.Color.B + (hi.Color.B - lo.Color.B) * f));
        }

        // ---------------------------------------------------------------- 背景の模様

        Drawing decorCache;
        double decorW, decorH, decorOp = -1;
        string decorBgImage;

        // 模様は動かないので、窓の大きさが変わるまでは一度描いたものを貼るだけにする
        void DrawDecor(DrawingContext dc)
        {
            if (string.IsNullOrEmpty(sk.Decor)) return;
            if (decorCache == null || decorW != contentW || decorH != contentH || decorOp != cfg.BackgroundOpacity || decorBgImage != cfg.BackgroundImage)
            {
                var dv = new DrawingVisual();
                using (DrawingContext d2 = dv.RenderOpen()) DrawDecorNow(d2);
                Drawing d = dv.Drawing;
                if (d != null && d.CanFreeze) d.Freeze();
                decorCache = d;
                decorW = contentW;
                decorH = contentH;
                decorOp = cfg.BackgroundOpacity;
                decorBgImage = cfg.BackgroundImage;
            }
            if (decorCache != null) dc.DrawDrawing(decorCache);
        }

        void DrawDecorNow(DrawingContext dc)
        {
            var rnd = new Random(1234);   // いつも同じ配置
            Color c = sk.DecorColor;
            // 窓の外にはみ出さないように切り抜く
            var clip = new RectangleGeometry(new Rect(0, 0, contentW, contentH), sk.CornerRadius, sk.CornerRadius);
            clip.Freeze();
            dc.PushClip(clip);
            switch (sk.Decor)
            {
                case "sakura": DecorSakura(dc, rnd, c); break;
                case "hearts": DecorHearts(dc, rnd, c); break;
                case "stars": DecorStars(dc, rnd, c); break;
                case "bubbles": DecorBubbles(dc, rnd, c); break;
                case "dots": DecorDots(dc, rnd, c); break;
                case "grid": DecorGrid(dc, c); break;
                case "rain": DecorRain(dc, rnd, c); break;
                case "lines": DecorLines(dc, c); break;
            }
            dc.Pop();
        }

        void DecorSakura(DrawingContext dc, Random rnd, Color c)
        {
            int n = (int)(contentW * contentH / 22000);
            for (int i = 0; i < n; i++)
            {
                double cx = rnd.NextDouble() * contentW, cy = rnd.NextDouble() * contentH;
                double size = 7 + rnd.NextDouble() * 9;
                double rot = rnd.NextDouble() * 72;
                Brush b = Alpha(c, (byte)(50 + rnd.Next(70)));
                for (int k = 0; k < 5; k++)
                {
                    dc.PushTransform(new RotateTransform(rot + k * 72, cx, cy));
                    dc.DrawEllipse(b, null, new Point(cx, cy - size * 0.55), size * 0.3, size * 0.5);
                    dc.Pop();
                }
                dc.DrawEllipse(Alpha(Colors.White, 120), null, new Point(cx, cy), size * 0.18, size * 0.18);
            }
        }

        static StreamGeometry HeartGeometry(double cx, double cy, double s)
        {
            var g = new StreamGeometry();
            using (StreamGeometryContext ctx = g.Open())
            {
                ctx.BeginFigure(new Point(cx, cy + s * 0.45), true, true);
                ctx.BezierTo(new Point(cx - s * 0.15, cy + s * 0.25), new Point(cx - s * 0.55, cy + s * 0.05), new Point(cx - s * 0.5, cy - s * 0.25), true, true);
                ctx.BezierTo(new Point(cx - s * 0.45, cy - s * 0.55), new Point(cx - s * 0.08, cy - s * 0.5), new Point(cx, cy - s * 0.25), true, true);
                ctx.BezierTo(new Point(cx + s * 0.08, cy - s * 0.5), new Point(cx + s * 0.45, cy - s * 0.55), new Point(cx + s * 0.5, cy - s * 0.25), true, true);
                ctx.BezierTo(new Point(cx + s * 0.55, cy + s * 0.05), new Point(cx + s * 0.15, cy + s * 0.25), new Point(cx, cy + s * 0.45), true, true);
            }
            g.Freeze();
            return g;
        }

        void DecorHearts(DrawingContext dc, Random rnd, Color c)
        {
            int n = (int)(contentW * contentH / 30000);
            for (int i = 0; i < n; i++)
            {
                double cx = rnd.NextDouble() * contentW, cy = rnd.NextDouble() * contentH;
                double size = 10 + rnd.NextDouble() * 12;
                dc.PushTransform(new RotateTransform(-20 + rnd.NextDouble() * 40, cx, cy));
                dc.DrawGeometry(Alpha(c, (byte)(40 + rnd.Next(60))), null, HeartGeometry(cx, cy, size));
                dc.Pop();
            }
        }

        static StreamGeometry SparkleGeometry(double cx, double cy, double s)
        {
            var g = new StreamGeometry();
            using (StreamGeometryContext ctx = g.Open())
            {
                ctx.BeginFigure(new Point(cx, cy - s), true, true);
                ctx.LineTo(new Point(cx + s * 0.22, cy - s * 0.22), true, false);
                ctx.LineTo(new Point(cx + s, cy), true, false);
                ctx.LineTo(new Point(cx + s * 0.22, cy + s * 0.22), true, false);
                ctx.LineTo(new Point(cx, cy + s), true, false);
                ctx.LineTo(new Point(cx - s * 0.22, cy + s * 0.22), true, false);
                ctx.LineTo(new Point(cx - s, cy), true, false);
                ctx.LineTo(new Point(cx - s * 0.22, cy - s * 0.22), true, false);
            }
            g.Freeze();
            return g;
        }

        void DecorStars(DrawingContext dc, Random rnd, Color c)
        {
            int n = (int)(contentW * contentH / 16000);
            for (int i = 0; i < n; i++)
            {
                double cx = rnd.NextDouble() * contentW, cy = rnd.NextDouble() * contentH;
                double size = 3 + rnd.NextDouble() * 7;
                dc.DrawGeometry(Alpha(c, (byte)(50 + rnd.Next(80))), null, SparkleGeometry(cx, cy, size));
            }
        }

        void DecorBubbles(DrawingContext dc, Random rnd, Color c)
        {
            int n = (int)(contentW * contentH / 30000);
            for (int i = 0; i < n; i++)
            {
                double cx = rnd.NextDouble() * contentW, cy = rnd.NextDouble() * contentH;
                double r = 6 + rnd.NextDouble() * 22;
                var pen = new Pen(Alpha(c, (byte)(30 + rnd.Next(40))), 1.2);
                pen.Freeze();
                dc.DrawEllipse(Alpha(c, 10), pen, new Point(cx, cy), r, r);
                dc.DrawEllipse(Alpha(c, 70), null, new Point(cx - r * 0.35, cy - r * 0.35), r * 0.18, r * 0.12);
            }
        }

        void DecorDots(DrawingContext dc, Random rnd, Color c)
        {
            int n = (int)(contentW * contentH / 12000);
            for (int i = 0; i < n; i++)
                dc.DrawEllipse(Alpha(c, 32), null, new Point(rnd.NextDouble() * contentW, rnd.NextDouble() * contentH), 3 + rnd.NextDouble() * 3, 3 + rnd.NextDouble() * 3);
        }

        // シンセウェイブ: 下 1/3 に遠近グリッド、その地平線に太陽
        void DecorGrid(DrawingContext dc, Color c)
        {
            double horizon = contentH * 0.64, bottom = contentH;
            double cx = contentW / 2;
            double sunR = Math.Min(contentW, contentH) * 0.2;
            var sun = new LinearGradientBrush(Color.FromArgb(90, 255, 209, 102), Color.FromArgb(90, 255, 79, 216), 90);
            sun.Freeze();
            var clipTop = new RectangleGeometry(new Rect(0, 0, contentW, horizon));
            clipTop.Freeze();
            dc.PushClip(clipTop);
            dc.DrawEllipse(sun, null, new Point(cx, horizon), sunR, sunR);
            // 太陽の下側に横縞の切れ目。背景画像があるときは背景色で塗ると帯になって写真を隠すので描かない
            for (int i = 0; i < 5 && string.IsNullOrWhiteSpace(cfg.BackgroundImage); i++)
            {
                double yy = horizon - sunR * (0.1 + i * 0.12);
                // 背景と同じ色・同じ不透明度で塗って「切れ目」に見せる (グラデーションならその高さの色)
                byte a = (byte)Math.Round(Math.Max(1.0 / 255, Math.Min(1, cfg.BackgroundOpacity)) * 255);
                dc.DrawRectangle(Alpha(BackgroundAt(yy / contentH), a), null, new Rect(cx - sunR, yy, sunR * 2, 1.5 + i * 0.8));
            }
            dc.Pop();
            var pen = new Pen(Alpha(c, 55), 1);
            pen.Freeze();
            var penH = new Pen(Alpha(c, 110), 1.2);
            penH.Freeze();
            dc.DrawLine(penH, new Point(0, horizon), new Point(contentW, horizon));
            for (int i = 1; i <= 9; i++)
            {
                double t = i / 9.0;
                double yy = horizon + (bottom - horizon) * t * t;
                dc.DrawLine(pen, new Point(0, yy), new Point(contentW, yy));
            }
            for (int i = -8; i <= 8; i++)
            {
                double xb = cx + i * contentW * 0.18;
                dc.DrawLine(pen, new Point(cx + i * contentW * 0.035, horizon), new Point(xb, bottom));
            }
        }

        // マトリックス: 薄い緑の文字の列
        void DecorRain(DrawingContext dc, Random rnd, Color c)
        {
            const string chars = "ｱｲｳｴｵｶｷｸｹｺｻｼｽｾｿﾀﾁﾂﾃﾄﾅﾆﾇﾈﾉ0123456789Z";
            int cols = Math.Max(1, (int)(contentW / 48));
            for (int i = 0; i < cols; i++)
            {
                double x = 10 + i * 48 + rnd.NextDouble() * 20;
                double y = rnd.NextDouble() * contentH * 0.8;
                int len = 5 + rnd.Next(7);
                for (int k = 0; k < len; k++)
                {
                    byte a = (byte)(k == len - 1 ? 150 : 25 + (int)(60.0 * k / len));
                    string ch = chars[rnd.Next(chars.Length)].ToString();
                    dc.DrawText(new FormattedText(ch, Ja, FlowDirection.LeftToRight, TfN, 11, Alpha(c, a), dip), new Point(x, y + k * 13));
                }
            }
        }

        // ノート: 横罫線と赤い縦線
        void DecorLines(DrawingContext dc, Color c)
        {
            var pen = new Pen(Alpha(c, 70), 1);
            pen.Freeze();
            for (double y = 30; y < contentH; y += 24)
                dc.DrawLine(pen, new Point(4, Math.Round(y) + 0.5), new Point(contentW - 4, Math.Round(y) + 0.5));
            var red = new Pen(Alpha(Color.FromRgb(224, 86, 75), 70), 1);
            red.Freeze();
            dc.DrawLine(red, new Point(Gap - 4.5, 4), new Point(Gap - 4.5, contentH - 4));
        }

        // ---------------------------------------------------------------- マスコット

        // いちばん空いている列の下にキャラクターを描く
        void DrawMascot(DrawingContext dc)
        {
            if (SinglePanel != null) return;
            if (DrawMascotImage(dc)) return;   // 画像が設定されていればそちらを使う
            if (string.IsNullOrEmpty(sk.Mascot) || cfg.Layout.Count == 0) return;
            int cols = cfg.Layout.Count;
            var bottoms = new double[cols];
            foreach (PanelBox b in boxes) if (b.Col < cols) bottoms[b.Col] = Math.Max(bottoms[b.Col], b.R.Bottom);
            int best = -1;
            double bestFree = 0;
            for (int c = 0; c < cols; c++)
            {
                double free = contentH - 12 - bottoms[c];
                if (free > bestFree) { bestFree = free; best = c; }
            }
            if (best < 0 || bestFree < 64) return;
            double size = Math.Min(130, bestFree - 10);
            double x = Gap + best * (ColW + Gap) + (ColW - size) / 2 + (cfg.MascotPlacement == "left" ? sideExtra : 0);
            double y = contentH - 10 - size;
            DrawMascotAt(dc, x, y, size, sk.Mascot);
        }

        static readonly Pen MascotOutline = Skin.P(Alpha(Color.FromRgb(60, 40, 50), 70), 1.4);
        static readonly Pen MascotWhisker = Skin.P(Alpha(Color.FromRgb(60, 40, 50), 110), 1);

        void DrawMascotAt(DrawingContext dc, double x, double y, double s, string kind)
        {
            Brush body = sk.MascotBody ?? Brushes.White;
            Brush accent = sk.MascotAccent ?? sk.Accent1;
            Brush eye = sk.MascotEye ?? Skin.B("#3A2A33");
            Pen outline = MascotOutline;
            double cx = x + s / 2, cy = y + s * 0.6;
            double rx = s * 0.4, ry = s * 0.35;

            // 耳
            if (kind == "cat")
            {
                dc.DrawGeometry(body, outline, Tri(cx - rx * 0.75, cy - ry * 0.55, cx - rx * 0.55, cy - ry * 1.55, cx - rx * 0.1, cy - ry * 0.9));
                dc.DrawGeometry(body, outline, Tri(cx + rx * 0.75, cy - ry * 0.55, cx + rx * 0.55, cy - ry * 1.55, cx + rx * 0.1, cy - ry * 0.9));
                dc.DrawGeometry(accent, null, Tri(cx - rx * 0.62, cy - ry * 0.7, cx - rx * 0.52, cy - ry * 1.3, cx - rx * 0.25, cy - ry * 0.9));
                dc.DrawGeometry(accent, null, Tri(cx + rx * 0.62, cy - ry * 0.7, cx + rx * 0.52, cy - ry * 1.3, cx + rx * 0.25, cy - ry * 0.9));
            }
            else if (kind == "bear")
            {
                dc.DrawEllipse(body, outline, new Point(cx - rx * 0.72, cy - ry * 0.85), s * 0.13, s * 0.13);
                dc.DrawEllipse(body, outline, new Point(cx + rx * 0.72, cy - ry * 0.85), s * 0.13, s * 0.13);
                dc.DrawEllipse(accent, null, new Point(cx - rx * 0.72, cy - ry * 0.85), s * 0.07, s * 0.07);
                dc.DrawEllipse(accent, null, new Point(cx + rx * 0.72, cy - ry * 0.85), s * 0.07, s * 0.07);
            }
            else   // bunny
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    double ex = cx + side * rx * 0.42, ey = cy - ry * 1.25;
                    dc.PushTransform(new RotateTransform(side * 12, ex, ey));
                    dc.DrawEllipse(body, outline, new Point(ex, ey), s * 0.1, s * 0.27);
                    dc.DrawEllipse(accent, null, new Point(ex, ey + s * 0.02), s * 0.05, s * 0.18);
                    dc.Pop();
                }
            }
            // 顔
            dc.DrawEllipse(body, outline, new Point(cx, cy), rx, ry);
            // ほっぺ
            Brush blush = Alpha(((SolidColorBrush)(accent is SolidColorBrush ? accent : Skin.B("#FFB3CC"))).Color, 150);
            dc.DrawEllipse(blush, null, new Point(cx - rx * 0.6, cy + ry * 0.25), s * 0.075, s * 0.045);
            dc.DrawEllipse(blush, null, new Point(cx + rx * 0.6, cy + ry * 0.25), s * 0.075, s * 0.045);
            // 目
            for (int side = -1; side <= 1; side += 2)
            {
                double ex = cx + side * rx * 0.38, ey = cy - ry * 0.05;
                dc.DrawEllipse(eye, null, new Point(ex, ey), s * 0.06, s * 0.085);
                dc.DrawEllipse(Brushes.White, null, new Point(ex - s * 0.02, ey - s * 0.03), s * 0.022, s * 0.022);
                dc.DrawEllipse(Brushes.White, null, new Point(ex + s * 0.015, ey + s * 0.02), s * 0.01, s * 0.01);
            }
            // 鼻と口 (ω)
            var mouth = new Pen(eye, Math.Max(1.2, s * 0.018));
            mouth.StartLineCap = PenLineCap.Round;
            mouth.EndLineCap = PenLineCap.Round;
            mouth.Freeze();
            double my = cy + ry * 0.38, mw = s * 0.05;
            if (kind != "cat") dc.DrawEllipse(eye, null, new Point(cx, my - s * 0.035), s * 0.022, s * 0.015);
            var g = new StreamGeometry();
            using (StreamGeometryContext ctx = g.Open())
            {
                ctx.BeginFigure(new Point(cx - mw * 2, my - mw * 0.6), false, false);
                ctx.ArcTo(new Point(cx, my), new Size(mw, mw), 0, false, SweepDirection.Counterclockwise, true, false);
                ctx.ArcTo(new Point(cx + mw * 2, my - mw * 0.6), new Size(mw, mw), 0, false, SweepDirection.Counterclockwise, true, false);
            }
            g.Freeze();
            dc.DrawGeometry(null, mouth, g);
            if (kind == "cat")
            {
                Pen whisker = MascotWhisker;
                for (int side = -1; side <= 1; side += 2)
                {
                    dc.DrawLine(whisker, new Point(cx + side * rx * 0.8, cy + ry * 0.1), new Point(cx + side * rx * 1.25, cy - ry * 0.05));
                    dc.DrawLine(whisker, new Point(cx + side * rx * 0.8, cy + ry * 0.3), new Point(cx + side * rx * 1.25, cy + ry * 0.4));
                }
            }
            if (kind == "bunny")
            {
                // 右耳のリボン
                double bx = cx + rx * 0.42, by = cy - ry * 0.95;
                Brush bow = sk.Danger;
                dc.DrawGeometry(bow, null, Tri(bx, by, bx - s * 0.11, by - s * 0.07, bx - s * 0.11, by + s * 0.07));
                dc.DrawGeometry(bow, null, Tri(bx, by, bx + s * 0.11, by - s * 0.07, bx + s * 0.11, by + s * 0.07));
                dc.DrawEllipse(bow, null, new Point(bx, by), s * 0.03, s * 0.03);
            }
            // きらきら
            Brush sp = Alpha(((SolidColorBrush)(sk.Text1 is SolidColorBrush ? sk.Text1 : Brushes.Gold)).Color, 200);
            dc.DrawGeometry(sp, null, SparkleGeometry(x + s * 0.08, y + s * 0.3, s * 0.05));
            dc.DrawGeometry(sp, null, SparkleGeometry(x + s * 0.94, y + s * 0.5, s * 0.04));
            dc.DrawGeometry(sp, null, SparkleGeometry(x + s * 0.88, y + s * 0.18, s * 0.03));
        }

        static StreamGeometry Tri(double x1, double y1, double x2, double y2, double x3, double y3)
        {
            var g = new StreamGeometry();
            using (StreamGeometryContext ctx = g.Open())
            {
                ctx.BeginFigure(new Point(x1, y1), true, true);
                ctx.LineTo(new Point(x2, y2), true, false);
                ctx.LineTo(new Point(x3, y3), true, false);
            }
            g.Freeze();
            return g;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskMeter
{
    // 透明モードの文字の縁取り、画像 (背景・マスコット)、リングゲージ
    sealed partial class MeterView
    {
        // ---------------------------------------------------------------- 文字の影・縁取り

        Skin shadowSkin;
        Brush shadowBrush;

        bool ShadowOn()
        {
            if (cfg.TextShadow == "on") return true;
            if (cfg.TextShadow == "off") return false;
            return cfg.BackgroundOpacity < 0.5;
        }

        // 文字が暗い色なら白い縁取り、明るい色なら黒い縁取り
        Brush ShadowBrush()
        {
            if (shadowBrush != null && shadowSkin == sk) return shadowBrush;
            shadowSkin = sk;
            var sc = sk.Title as SolidColorBrush;
            double lum = sc == null ? 255 : 0.299 * sc.Color.R + 0.587 * sc.Color.G + 0.114 * sc.Color.B;
            shadowBrush = Alpha(lum < 128 ? Colors.White : Colors.Black, lum < 128 ? (byte)200 : (byte)215);
            return shadowBrush;
        }

        // 上下左右 1px にずらして描き、縁取りにする
        void DrawTextWithShadow(DrawingContext dc, FormattedText ft, Point p, Brush fg)
        {
            if (ShadowOn())
            {
                ft.SetForegroundBrush(ShadowBrush());
                dc.DrawText(ft, new Point(p.X + 1, p.Y));
                dc.DrawText(ft, new Point(p.X - 1, p.Y));
                dc.DrawText(ft, new Point(p.X, p.Y + 1));
                dc.DrawText(ft, new Point(p.X, p.Y - 1));
                dc.DrawText(ft, new Point(p.X + 1, p.Y + 1));
                ft.SetForegroundBrush(fg);
            }
            dc.DrawText(ft, p);
        }

        // ---------------------------------------------------------------- 画像

        sealed class ImageSlot
        {
            public string Path;
            public BitmapSource Image;
            public bool Failed;
        }

        readonly ImageSlot bgSlot = new ImageSlot();
        readonly ImageSlot mascotSlot = new ImageSlot();
        double sideExtra;   // 立ち絵を横に並べるときに増える幅

        string ResolvePath(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) return null;
            try
            {
                p = p.Trim();
                if (!Path.IsPathRooted(p)) p = Path.Combine(AppPaths.Dir, p);
                return p;
            }
            catch { return null; }
        }

        // パスが変わったときだけ読み直す。失敗はログに 1 回だけ書く
        BitmapSource GetImage(ImageSlot slot, string setting, int decodeHeight)
        {
            string path = ResolvePath(setting);
            if (path == null) { slot.Path = null; slot.Image = null; slot.Failed = false; return null; }
            if (path == slot.Path) return slot.Failed ? null : slot.Image;
            slot.Path = path;
            slot.Image = null;
            slot.Failed = false;
            try
            {
                if (!File.Exists(path)) throw new FileNotFoundException("画像がありません", path);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                if (decodeHeight > 0) bmp.DecodePixelHeight = decodeHeight;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                slot.Image = bmp;
            }
            catch (Exception ex)
            {
                slot.Failed = true;
                Log.Write("image " + path, ex);
            }
            return slot.Image;
        }

        // 背景画像を窓いっぱいに (cover) 敷く
        void DrawBackgroundImage(DrawingContext dc)
        {
            BitmapSource img = GetImage(bgSlot, cfg.BackgroundImage, 1400);
            if (img == null) return;
            double scale = Math.Max(contentW / img.PixelWidth, contentH / img.PixelHeight);
            double w = img.PixelWidth * scale, h = img.PixelHeight * scale;
            var clip = new RectangleGeometry(new Rect(0, 0, contentW, contentH), sk.CornerRadius, sk.CornerRadius);
            clip.Freeze();
            dc.PushClip(clip);
            dc.PushOpacity(cfg.BackgroundImageOpacity);
            dc.DrawImage(img, new Rect((contentW - w) / 2, (contentH - h) / 2, w, h));
            dc.Pop();
            dc.Pop();
        }

        // 立ち絵を横に並べるときの幅 (画像の表示幅 + 余白)。描画前に計算して窓の幅に足す
        double SideExtra()
        {
            if (string.IsNullOrWhiteSpace(cfg.MascotImage) || cfg.MascotPlacement == "free") return 0;
            BitmapSource img = GetImage(mascotSlot, cfg.MascotImage, Math.Min(2400, cfg.MascotSize * 2));
            if (img == null) return 0;
            double h = Math.Min(cfg.MascotSize, Math.Max(40, contentH - 24));
            double w = img.PixelWidth * h / img.PixelHeight;
            return Math.Min(w, 600) + Gap;
        }

        // マスコット画像。true を返したら手描きマスコットは描かない
        bool DrawMascotImage(DrawingContext dc)
        {
            if (string.IsNullOrWhiteSpace(cfg.MascotImage)) return false;
            BitmapSource img = GetImage(mascotSlot, cfg.MascotImage, Math.Min(2400, cfg.MascotSize * 2));
            if (img == null) return true;
            double aspect = (double)img.PixelWidth / img.PixelHeight;
            if (cfg.MascotPlacement == "free")
            {
                // いちばん空いている列の下に収める
                int cols = cfg.Layout.Count;
                if (cols == 0) return true;
                var bottoms = new double[cols];
                foreach (PanelBox b in boxes) if (b.Col < cols) bottoms[b.Col] = Math.Max(bottoms[b.Col], b.R.Bottom);
                int best = -1;
                double bestFree = 0;
                for (int c = 0; c < cols; c++)
                {
                    double free = contentH - 12 - bottoms[c];
                    if (free > bestFree) { bestFree = free; best = c; }
                }
                if (best < 0 || bestFree < 64) return true;
                double h = Math.Min(cfg.MascotSize, bestFree - 8), w = h * aspect;
                if (w > ColW) { w = ColW; h = w / aspect; }
                double x = Gap + best * (ColW + Gap) + (ColW - w) / 2 + (cfg.MascotPlacement == "left" ? sideExtra : 0);
                dc.DrawImage(img, new Rect(x, contentH - 10 - h, w, h));
                return true;
            }
            double ih = Math.Min(cfg.MascotSize, Math.Max(40, contentH - 24));
            double iw = Math.Min(ih * aspect, 600);
            double ix = cfg.MascotPlacement == "right" ? contentW - Gap - iw : Gap;
            dc.DrawImage(img, new Rect(ix, contentH - 12 - ih, iw, ih));
            return true;
        }

        // ---------------------------------------------------------------- メモリ (タスクマネージャーの「メモリ」風)

        double DrawMemory(DrawingContext dc, double x, double y)
        {
            double used = st.RamTotal > st.RamAvail ? (double)(st.RamTotal - st.RamAvail) : 0;
            double frac = st.RamTotal > 0 ? used / st.RamTotal : 0;
            Header(dc, x, y, "", TileTeal, "Memory Meter", Pct(frac * 100));
            string sub;
            if (!st.HwStaticReady) sub = "取得中...";
            else
            {
                var parts = new System.Collections.Generic.List<string>();
                if (st.MemType != null) parts.Add(st.MemType);
                if (st.MemSpeedMHz > 0) parts.Add(st.MemSpeedMHz.ToString(Inv) + "MHz");
                if (st.MemSlotsTotal > 0) parts.Add(string.Format(Inv, "{0}/{1}枚", st.MemSlotsUsed, st.MemSlotsTotal));
                if (st.MemFormFactor != null) parts.Add(st.MemFormFactor);
                sub = parts.Count > 0 ? string.Join(" ", parts.ToArray()) : Bytes(st.RamTotal);
            }
            T(dc, sub, x + 46, y + 19, 12.5, Yellow, 0, false, ColW - 46);
            y += 44;

            Brush ramBrush = Level(frac, 0.85, 0.93, Cyan);   // 逼迫したら色で知らせる
            Bar(dc, x, y + 4, ColW, 9, frac, ramBrush);
            y += 20;
            bool comp = !double.IsNaN(st.Compressed) && st.Compressed > 0;
            y = KeyValue(dc, x, y, comp ? "使用中 (圧縮)" : "使用中", Cyan, Bytes(used) + (comp ? " (" + BytesShort(st.Compressed) + ")" : ""), ramBrush);
            y = KeyValue(dc, x, y, "使用可能", Green, Bytes(st.RamAvail), Green);
            y = KeyValue(dc, x, y, "コミット済み", Yellow, Bytes(st.CommitUsed) + " / " + Bytes(st.CommitLimit), Yellow);
            y = KeyValue(dc, x, y, "キャッシュ済み", Violet, Bytes(st.Cached), Violet);
            y = KeyValue(dc, x, y, "ページプール", Orange, Bytes(st.PoolPaged), Orange);
            y = KeyValue(dc, x, y, "非ページプール", Orange, Bytes(st.PoolNonpaged), Orange);
            y = KeyValue(dc, x, y, "ハードウェア予約", Gray, Bytes(st.HwReserved), Gray);
            y = KeyValue(dc, x, y, "ページファイル", Magenta, Bytes(st.PageUsed) + " / " + Bytes(st.PageTotal), Magenta);

            // メモリ構成: 使用中 | 変更済み | スタンバイ | 空き
            T(dc, "メモリ構成", x, y, 11.5, Gray);
            y += 15;
            double total = st.RamTotal;
            double standby = double.IsNaN(st.Standby) ? 0 : st.Standby, modified = double.IsNaN(st.Modified) ? 0 : st.Modified, free = double.IsNaN(st.FreeMem) ? 0 : st.FreeMem;
            double inUse = Math.Max(0, total - standby - modified - free);
            double[] segs = { inUse, modified, standby, free };
            Brush[] segBrush = { ramBrush, Violet, Blue, Track };
            string[] segName = { "使用中", "変更済み", "スタンバイ", "空き" };
            dc.DrawRectangle(Track, null, new Rect(x, y, ColW, 14));
            double sx = x;
            for (int i = 0; i < 4 && total > 0; i++)
            {
                double w = ColW * segs[i] / total;
                if (w > 0.5) dc.DrawRectangle(segBrush[i], null, new Rect(sx, y, Math.Min(w, x + ColW - sx), 14));
                sx += w;
            }
            dc.DrawRectangle(null, GraphBorder, new Rect(x + 0.5, y + 0.5, ColW - 1, 13));
            y += 18;
            // 凡例は 2 行 × 2 項目 (1 行だと列の幅に収まらない)
            for (int i = 0; i < 4; i++)
            {
                double lx = x + (i % 2) * (ColW / 2), ly = y + (i / 2) * 15;
                dc.DrawRectangle(segBrush[i], GraphBorder, new Rect(lx, ly + 3, 8, 8));
                T(dc, segName[i] + " " + BytesShort(segs[i]), lx + 11, ly, 10.5, Gray, 0, false, ColW / 2 - 14);
            }
            y += 32;
            Graph(dc, new Rect(x, y, ColW, 40), new[] { st.RamHist }, new[] { PenCyan }, 100);
            return y + 40;
        }

        // ---------------------------------------------------------------- リングゲージ

        double DrawRings(DrawingContext dc, double x, double y)
        {
            Header(dc, x, y, "", TileBlue, "Ring Gauges", null);
            T(dc, "CPU / RAM / GPU / VRAM", x + 46, y + 19, 12.5, Gray);
            y += 46;
            double ramUsed = st.RamTotal > st.RamAvail ? st.RamTotal - st.RamAvail : 0;
            double ram = st.RamTotal > 0 ? ramUsed / st.RamTotal * 100 : 0;
            double vram = st.VramTotalMB > 0 ? st.VramUsedMB / st.VramTotalMB * 100 : double.NaN;
            double page = st.PageTotal > 0 ? (double)st.PageUsed / st.PageTotal * 100 : 0;
            string[] labels = { "CPU", "RAM", st.GpuOk ? "GPU" : "Disk", st.GpuOk ? "VRAM" : "Page" };
            double[] values = { st.CpuTotal, ram, st.GpuOk ? st.GpuUtil : st.DiskActive, st.GpuOk ? vram : page };
            Brush[] colors = { Cyan, Yellow, Green, Magenta };
            const double cell = ColW / 2, r = 36, thick = 9, labelH = 18;
            for (int i = 0; i < 4; i++)
            {
                double cx = x + (i % 2) * cell + cell / 2, cy = y + (i / 2) * (r * 2 + labelH + 10) + r + 4;
                Ring(dc, cx, cy, r, thick, double.IsNaN(values[i]) ? 0 : values[i] / 100.0, colors[i]);
                T(dc, double.IsNaN(values[i]) ? "--" : Math.Round(values[i]).ToString(Inv) + "%", cx, cy - 11, 17, White, 2, true);
                T(dc, labels[i], cx, cy + r + 4, 12, colors[i], 2);
            }
            return y + 2 * (r * 2 + labelH + 10);
        }

        // リング用のペンはスキンごとに 1 回だけ作る (ClearCaches で捨てる)
        readonly Dictionary<Brush, Pen> ringPens = new Dictionary<Brush, Pen>();
        Pen ringTrack;

        static Pen RoundPen(Brush b, double thick)
        {
            var pen = new Pen(b, thick);
            pen.StartLineCap = PenLineCap.Round;
            pen.EndLineCap = PenLineCap.Round;
            pen.Freeze();
            return pen;
        }

        // 左下から時計回りに 270° のリング
        void Ring(DrawingContext dc, double cx, double cy, double r, double thick, double frac, Brush color)
        {
            if (ringTrack == null) ringTrack = RoundPen(Track, thick);
            dc.DrawGeometry(null, ringTrack, Arc(cx, cy, r, 135, 270));
            frac = Math.Max(0, Math.Min(1, frac));
            if (frac < 0.005) return;
            Pen pen;
            if (!ringPens.TryGetValue(color, out pen)) { pen = RoundPen(color, thick); ringPens[color] = pen; }
            dc.DrawGeometry(null, pen, Arc(cx, cy, r, 135, 270 * frac));
        }

        static StreamGeometry Arc(double cx, double cy, double r, double startDeg, double sweepDeg)
        {
            double a0 = startDeg * Math.PI / 180, a1 = (startDeg + sweepDeg) * Math.PI / 180;
            var g = new StreamGeometry();
            using (StreamGeometryContext ctx = g.Open())
            {
                ctx.BeginFigure(new Point(cx + r * Math.Cos(a0), cy + r * Math.Sin(a0)), false, false);
                ctx.ArcTo(new Point(cx + r * Math.Cos(a1), cy + r * Math.Sin(a1)), new Size(r, r), 0, sweepDeg > 180, SweepDirection.Clockwise, true, false);
            }
            g.Freeze();
            return g;
        }
    }
}

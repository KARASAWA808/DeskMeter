using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace DeskMeter
{
    // 全メーターを1つの要素に直接描画する (1秒に1回だけ再描画)
    sealed partial class MeterView : FrameworkElement
    {
        const double ColW = 220, Gap = 12, RowH = 18, FS = 13, PanelGap = 16;

        readonly MeterState st;
        readonly Settings cfg;
        double contentH = 900, contentW;
        double dip = 1.0;
        // 表示しているモニターの作業領域の高さ (倍率を掛ける前)。配置編集で画面外になる列を示すのに使う。0 = 不明
        public double ScreenLimitH;
        double editHintH = 40;   // 配置編集中の案内ボックスの高さ (描いたときに測る)

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly CultureInfo Ja = new CultureInfo("ja-JP");
        static readonly Typeface TfIcon = new Typeface(new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        // 以下はすべて ApplySkin() で現在のスキンから設定される
        Skin sk;
        Typeface TfN, TfB, TfL;
        Brush White, Yellow, Orange, Cyan, Green, Lime, Red, Gray, Dim, Magenta, Violet, Blue, Track, GraphBg,
            TileGreen, TileBlue, TileTeal, TileSlate, TilePurple, TileRose, CalHead, TodayOrange, TileGradient,
            SunBrush, CloudLight, CloudDark, RainBrush, SnowBrush, MoonLit, MoonDark, Scanline;
        Pen GraphBorder, GridPen, SepPen, PenYellow, PenGreen, PenCyan, SunPen, RainPen, FogPen;
        Brush[] Palette;
        Pen[] PalettePens;

        // 毎フレーム同じ文字 (ラベル類) の整形結果を使い回す。スキンや DPI が変わったら捨てる
        readonly Dictionary<string, FormattedText> textCache = new Dictionary<string, FormattedText>(512);
        readonly Dictionary<Brush, int> brushIds = new Dictionary<Brush, int>();
        readonly Dictionary<Pen, Brush> areaBrushes = new Dictionary<Pen, Brush>();   // グラフの面塗り用 (折れ線の色を薄くしたもの)
        double cacheDip = -1;

        void ClearCaches()
        {
            textCache.Clear();
            brushIds.Clear();
            areaBrushes.Clear();
            ringPens.Clear();
            ringTrack = null;
            decorCache = null;
            bgCache = null;
        }

        public MeterView(MeterState state, Settings cfg)
        {
            st = state;
            this.cfg = cfg;
            contentW = WidthNow();
            SnapsToDevicePixels = true;
            ApplySkin();
        }

        // 列数に応じた幅 (倍率を掛ける前)
        public static double WidthFor(Settings c)
        {
            int cols = c.Layout == null ? 1 : Math.Max(1, c.Layout.Count);
            return Gap + cols * (ColW + Gap);
        }

        // 設定変更後にサイズを計算し直す
        public void Relayout()
        {
            contentW = WidthNow();
            InvalidateMeasure();
            InvalidateVisual();
        }

        // ---------------------------------------------------------------- layout edit mode

        sealed class PanelBox
        {
            public string Id;
            public int Col, Index;
            public Rect R;
        }

        readonly List<PanelBox> boxes = new List<PanelBox>();
        bool editMode;
        string dragId;
        Point dragStart, dragPos;
        bool dragging;
        int dropCol, dropIndex;      // dropCol = -1: 左に新しい列, Layout.Count: 右に新しい列
        public event Action LayoutChanged;
        // パネルのダブルクリック (パネルID, 補助情報: ドライブ名など)。配置編集中は起きない
        public event Action<string, string> PanelActivated;
        readonly List<KeyValuePair<Rect, string>> driveBoxes = new List<KeyValuePair<Rect, string>>();   // ドライブ行の矩形

        // 独立ウィンドウ用: このパネルだけを描く (配置編集・マスコット・立ち絵は無し)
        public string SinglePanel;
        List<List<string>> singleLayout;

        List<List<string>> SingleLayout()
        {
            if (singleLayout == null || singleLayout[0][0] != SinglePanel)
            {
                singleLayout = new List<List<string>>();
                singleLayout.Add(new List<string>(new[] { SinglePanel }));
            }
            return singleLayout;
        }

        double WidthNow()
        {
            return SinglePanel != null ? Gap * 2 + ColW : WidthFor(cfg);
        }

        public bool EditMode
        {
            get { return editMode; }
            set
            {
                editMode = value;
                dragId = null;
                dragging = false;
                if (IsMouseCaptured) ReleaseMouseCapture();
                InvalidateVisual();
            }
        }

        // ドラッグ中に捕捉を失ったら (他のウィンドウがマウスを取った等) ドラッグを中断する
        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            if (dragId == null && !dragging) return;
            dragId = null;
            dragging = false;
            InvalidateVisual();
        }

        PanelBox BoxAt(Point p)
        {
            foreach (PanelBox b in boxes) if (b.R.Contains(p)) return b;
            return null;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (!editMode)
            {
                // ダブルクリックで関連アプリを開く (シングルクリックはウィンドウの移動に使う)
                if (e.ClickCount == 2 && PanelActivated != null)
                {
                    Point p = e.GetPosition(this);
                    PanelBox hit = BoxAt(p);
                    if (hit != null)
                    {
                        string arg = null;
                        if (hit.Id == "drives")
                            foreach (KeyValuePair<Rect, string> kv in driveBoxes) if (kv.Key.Contains(p)) arg = kv.Value;
                        PanelActivated(hit.Id, arg);
                        e.Handled = true;   // 2 回目のクリックでウィンドウを掴まない
                    }
                }
                return;
            }
            PanelBox b = BoxAt(e.GetPosition(this));
            if (b == null) return;
            dragId = b.Id;
            dragStart = dragPos = e.GetPosition(this);
            dragging = false;
            CaptureMouse();
            e.Handled = true;   // ウィンドウ自体のドラッグ移動はさせない
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragId == null) return;
            dragPos = e.GetPosition(this);
            if (!dragging && (Math.Abs(dragPos.X - dragStart.X) > 4 || Math.Abs(dragPos.Y - dragStart.Y) > 4)) dragging = true;
            if (dragging)
            {
                ComputeDrop(dragPos);
                InvalidateVisual();
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (dragId == null) return;
            // ReleaseMouseCapture() は OnLostMouseCapture を同期で呼ぶので、先にドラッグ情報を取り出しておく
            string id = dragId;
            bool wasDragging = dragging;
            Point p = e.GetPosition(this);
            dragId = null;
            dragging = false;
            ReleaseMouseCapture();
            if (wasDragging)
            {
                ComputeDrop(p);
                ApplyDrop(id);
            }
            InvalidateVisual();
            e.Handled = true;
        }

        void ComputeDrop(Point p)
        {
            int cols = cfg.Layout.Count;
            // 列数が上限なら新しい列は作らず、最寄りの列へ入れる
            bool canAdd = cols < Settings.MaxColumns;
            // 立ち絵の分だけ列がずれているので補正する
            double px = p.X - (cfg.MascotPlacement == "left" ? sideExtra : 0);
            double w = contentW - sideExtra;
            if (canAdd && px < Gap * 0.75) { dropCol = -1; dropIndex = 0; return; }
            if (canAdd && px > w - Gap * 0.75) { dropCol = cols; dropIndex = 0; return; }
            dropCol = Math.Max(0, Math.Min(cols - 1, (int)((px - Gap) / (ColW + Gap))));
            dropIndex = 0;
            foreach (PanelBox b in boxes)
            {
                if (b.Col != dropCol || b.Id == dragId) continue;
                if (p.Y > b.R.Y + b.R.Height / 2) dropIndex++;
            }
        }

        void ApplyDrop(string id)
        {
            List<List<string>> layout = cfg.Layout;
            int src = -1;
            for (int i = 0; i < layout.Count; i++) if (layout[i].Remove(id)) src = i;
            if (dropCol < 0) layout.Insert(0, new List<string>(new[] { id }));
            else if (dropCol >= layout.Count) layout.Add(new List<string>(new[] { id }));
            else layout[dropCol].Insert(Math.Min(dropIndex, layout[dropCol].Count), id);
            cfg.CleanLayout();
            Relayout();
            if (LayoutChanged != null) LayoutChanged();
        }

        double DrawPanel(DrawingContext dc, string id, double x, double y)
        {
            switch (id)
            {
                case "gpu": return DrawGpu(dc, x, y);
                case "network": return DrawNet(dc, x, y);
                case "drives": return DrawDrives(dc, x, y);
                case "topmem": return DrawTopMem(dc, x, y);
                case "cpu": return DrawCpu(dc, x, y);
                case "topcpu": return DrawTop(dc, x, y);
                case "clock": return DrawClock(dc, x, y - 4);
                case "calendar": return DrawCalendar(dc, x, y);
                case "datetile": return DrawDateTile(dc, x, y);
                case "weather": return DrawWeather(dc, x, y);
                case "rings": return DrawRings(dc, x, y);
                case "memory": return DrawMemory(dc, x, y);
                default: return y;
            }
        }

        // 編集モードの枠・ラベル・ドロップ位置の表示
        void DrawEditOverlay(DrawingContext dc)
        {
            var outline = new Pen(sk.Text2, 1);
            outline.DashStyle = DashStyles.Dash;
            outline.Freeze();
            var fill = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255));
            fill.Freeze();
            var dragFill = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));
            dragFill.Freeze();
            foreach (PanelBox b in boxes)
            {
                Rect r = new Rect(b.R.X - 4, b.R.Y - 4, b.R.Width + 8, b.R.Height + 8);
                dc.DrawRoundedRectangle(b.Id == dragId ? dragFill : fill, outline, r, 4, 4);
                FormattedText label = Make(Settings.PanelName(b.Id), 11.5, sk.Title, TfB);
                dc.DrawRoundedRectangle(sk.Text2, null, new Rect(r.X, r.Y, label.Width + 12, 18), 3, 3);
                dc.DrawText(Make(Settings.PanelName(b.Id), 11.5, sk.Background.A == 0 ? sk.Title : Brushes.Black, TfB), new Point(r.X + 6, r.Y + 1));
            }
            if (dragging && dragId != null)
            {
                // どのスキンでも目立つよう、暗い縁取りの上に太めの線
                var markerBack = new Pen(Alpha(Colors.Black, 150), 9);
                markerBack.StartLineCap = PenLineCap.Round;
                markerBack.EndLineCap = PenLineCap.Round;
                markerBack.Freeze();
                var marker = new Pen(sk.Accent1, 5);
                marker.StartLineCap = PenLineCap.Round;
                marker.EndLineCap = PenLineCap.Round;
                marker.Freeze();
                int cols = cfg.Layout.Count;
                // 立ち絵を左右に並べているときは、列がその分ずれている
                double offL = cfg.MascotPlacement == "left" ? sideExtra : 0, offR = cfg.MascotPlacement == "right" ? sideExtra : 0;
                if (dropCol < 0)
                {
                    dc.DrawLine(markerBack, new Point(offL + 5, 12), new Point(offL + 5, contentH - 12));
                    dc.DrawLine(marker, new Point(offL + 5, 12), new Point(offL + 5, contentH - 12));
                }
                else if (dropCol >= cols)
                {
                    double rx = contentW - offR - 5;
                    dc.DrawLine(markerBack, new Point(rx, 12), new Point(rx, contentH - 12));
                    dc.DrawLine(marker, new Point(rx, 12), new Point(rx, contentH - 12));
                }
                else
                {
                    double x = Gap + offL + dropCol * (ColW + Gap), yy = 12 - PanelGap / 2;
                    int n = 0;
                    foreach (PanelBox b in boxes)
                    {
                        if (b.Col != dropCol || b.Id == dragId) continue;
                        if (n == dropIndex) { yy = b.R.Y - PanelGap / 2; break; }
                        yy = b.R.Bottom + PanelGap / 2;
                        n++;
                    }
                    if (yy < 6) yy = 6;
                    dc.DrawLine(markerBack, new Point(x, yy), new Point(x + ColW, yy));
                    dc.DrawLine(marker, new Point(x, yy), new Point(x + ColW, yy));
                }
                // ドラッグ中のパネル名をカーソルに追従させる
                FormattedText ghost = Make(Settings.PanelName(dragId), 13, Brushes.Black, TfB);
                var gr = new Rect(dragPos.X + 14, dragPos.Y + 10, ghost.Width + 14, 22);
                dc.DrawRoundedRectangle(sk.Accent1, null, gr, 4, 4);
                dc.DrawText(ghost, new Point(gr.X + 7, gr.Y + 2));
            }
            // 画面 (作業領域) の下端。ここより下に伸びた列は画面外になるので赤い線と枠で知らせる
            bool over = false;
            if (ScreenLimitH > 0 && SinglePanel == null)
            {
                double limit = ScreenLimitH;
                var limitPen = new Pen(sk.Danger, 2);
                limitPen.DashStyle = DashStyles.Dash;
                limitPen.Freeze();
                if (limit < contentH)
                {
                    dc.DrawLine(limitPen, new Point(0, limit), new Point(contentW, limit));
                    T(dc, "▲ ここから下は画面外", contentW - 6, limit - 16, 11, sk.Danger, 1, true);
                    var overPen = new Pen(sk.Danger, 2);
                    overPen.Freeze();
                    foreach (PanelBox b in boxes)
                        if (b.R.Bottom > limit)
                        {
                            over = true;
                            dc.DrawRoundedRectangle(null, overPen, new Rect(b.R.X - 4, b.R.Y - 4, b.R.Width + 8, b.R.Height + 8), 4, 4);
                        }
                }
            }
            // 案内 (先頭のパネルに重ならないよう下端に出す)
            string hint = "配置編集中: ドラッグで移動 / 左右の外側へ落とすと新しい列 / 右クリック→「配置の編集」で終了";
            if (over) hint += "  ※ 赤枠のパネルは画面に収まりません (CPU の 2 列表示や別の列へ)";
            if (dragging && dragId != null) hint += string.Format(CultureInfo.InvariantCulture, "  [列 {0} / 位置 {1}]", dropCol < 0 ? "新(左)" : dropCol >= cfg.Layout.Count ? "新(右)" : (dropCol + 1).ToString(CultureInfo.InvariantCulture), dropIndex + 1);
            FormattedText ht = Make(hint, 12, Brushes.White, TfB);
            ht.MaxTextWidth = Math.Max(60, contentW - 24);
            editHintH = Math.Ceiling(ht.Height + 16);   // 次のフレームで窓の高さに足される
            var hr = new Rect((contentW - Math.Min(ht.Width, contentW - 24)) / 2 - 8, contentH - ht.Height - 12, Math.Min(ht.Width, contentW - 24) + 16, ht.Height + 8);
            var hb = new SolidColorBrush(Color.FromArgb(220, 30, 30, 36));
            hb.Freeze();
            dc.DrawRoundedRectangle(hb, null, hr, 5, 5);
            dc.DrawText(ht, new Point(hr.X + 8, hr.Y + 4));
        }

        // 設定のスキン名から配色・フォントを読み込む
        public void ApplySkin()
        {
            sk = Skin.Get(cfg.Skin);
            var font = new FontFamily(sk.FontFamily);
            TfN = new Typeface(font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            TfB = new Typeface(font, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
            TfL = new Typeface(font, FontStyles.Normal, FontWeights.Light, FontStretches.Normal);
            White = sk.Title; Yellow = sk.Text1; Orange = sk.Accent2; Cyan = sk.Text2; Green = sk.Accent1; Lime = sk.Lime;
            Red = sk.Danger; Gray = sk.Muted; Dim = sk.Dim; Magenta = sk.Magenta; Violet = sk.Violet; Blue = sk.Blue;
            Track = sk.Track; GraphBg = sk.GraphBg;
            Brush[] t = sk.Tiles;
            Brush o = sk.TileOverride;
            TileGreen = o ?? t[0]; TileBlue = o ?? t[1]; TileTeal = o ?? t[2]; TileSlate = o ?? t[3]; TilePurple = o ?? t[4]; TileRose = o ?? t[5];
            CalHead = sk.CalHead; TodayOrange = sk.Today; TileGradient = sk.DateTile;
            SunBrush = sk.Sun; CloudLight = sk.CloudLight; CloudDark = sk.CloudDark; RainBrush = sk.Rain; SnowBrush = sk.Snow;
            MoonLit = sk.MoonLit; MoonDark = sk.MoonDark;
            GraphBorder = sk.GraphBorder; GridPen = sk.GraphGrid; SepPen = Skin.P(sk.Separator, 1);
            PenYellow = Skin.P(Yellow, 1.3); PenGreen = Skin.P(Green, 1.3); PenCyan = Skin.P(Cyan, 1.3);
            ClearCaches();
            SunPen = Skin.P(SunBrush, 2); RainPen = Skin.P(RainBrush, 2); FogPen = Skin.P(CloudDark, 2.5);
            Palette = sk.Palette;
            PalettePens = new Pen[Palette.Length];
            for (int i = 0; i < Palette.Length; i++) PalettePens[i] = Skin.P(Palette[i], 1);
            Scanline = null;
            if (sk.Scanlines)
            {
                // 2px ごとに 1px の薄い黒線を敷き詰めるブラシ
                var line = new GeometryDrawing(new SolidColorBrush(Color.FromArgb(46, 0, 0, 0)), null, new RectangleGeometry(new Rect(0, 1, 1, 1)));
                var db = new DrawingBrush(line);
                db.TileMode = TileMode.Tile;
                db.Viewport = new Rect(0, 0, 1, 2);
                db.ViewportUnits = BrushMappingMode.Absolute;
                db.Viewbox = new Rect(0, 0, 1, 2);
                db.ViewboxUnits = BrushMappingMode.Absolute;
                db.Freeze();
                Scanline = db;
            }
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize) { return new Size(contentW, contentH); }

        // ---------------------------------------------------------------- render

        protected override void OnRender(DrawingContext dc)
        {
            long pt0 = Profiler.Enabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            RenderCore(dc);
            if (Profiler.Enabled) Profiler.Render(System.Diagnostics.Stopwatch.GetTimestamp() - pt0);
        }

        void RenderCore(DrawingContext dc)
        {
            dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            if (dip != cacheDip) { cacheDip = dip; ClearCaches(); }   // 文字の整形結果は DPI 込みなので作り直す
            // 立ち絵を横に並べる設定なら、その分だけ窓を広げる
            sideExtra = SinglePanel != null ? 0 : SideExtra();
            double wantW = WidthNow() + sideExtra;
            if (Math.Abs(wantW - contentW) > 0.5)
            {
                contentW = wantW;
                Dispatcher.BeginInvoke(new Action(delegate { InvalidateMeasure(); InvalidateVisual(); }));
            }
            DrawBackground(dc);
            DrawBackgroundImage(dc);
            DrawDecor(dc);
            DrawCards(dc);

            const double top = 12;
            double x = Gap + (cfg.MascotPlacement == "left" ? sideExtra : 0), h = 0, y;
            prevBoxes.Clear();
            prevBoxes.AddRange(boxes);
            boxes.Clear();
            lock (st)
            {
                List<List<string>> layout = SinglePanel != null ? SingleLayout() : cfg.Layout;
                for (int c = 0; c < layout.Count; c++)
                {
                    y = top;
                    for (int i = 0; i < layout[c].Count; i++)
                    {
                        string id = layout[c][i];
                        double y0 = y;
                        y = DrawPanel(dc, id, x, y);
                        var box = new PanelBox();
                        box.Id = id;
                        box.Col = c;
                        box.Index = i;
                        box.R = new Rect(x, y0 - 2, ColW, Math.Max(20, y - y0 + 2));
                        boxes.Add(box);
                        y += PanelGap;
                    }
                    h = Math.Max(h, y - PanelGap);
                    x += ColW + Gap;
                }
                if (h == 0)
                {
                    T(dc, "表示する項目がありません", Gap, top, FS, Gray);
                    T(dc, "右クリック → 表示する項目", Gap, top + RowH, FS, Gray);
                    h = top + RowH * 2;
                }
            }
            // カードは前のフレームの矩形で敷いているので、配置が変わったフレームではもう 1 回描いて合わせる
            if (sk.CardBrush != null && BoxesChanged()) Dispatcher.BeginInvoke(new Action(InvalidateVisual));
            DrawMascot(dc);
            if (editMode) DrawEditOverlay(dc);
            if (Scanline != null)
 dc.DrawRectangle(Scanline, null, new Rect(0, 0, contentW, contentH));
            if (sk.Border != null) dc.DrawRoundedRectangle(null, sk.Border, new Rect(0.5, 0.5, contentW - 1, contentH - 1), sk.CornerRadius, sk.CornerRadius);
            h = Math.Ceiling(h + 12 + (editMode ? editHintH : 0));   // 編集中は下端の案内の分だけ伸ばす
            if (Math.Abs(h - contentH) > 0.5)
            {
                contentH = h;
                Dispatcher.BeginInvoke(new Action(delegate { InvalidateMeasure(); InvalidateVisual(); }));
            }
        }

        bool BoxesChanged()
        {
            if (prevBoxes.Count != boxes.Count) return true;
            for (int i = 0; i < boxes.Count; i++)
                if (prevBoxes[i].Id != boxes[i].Id || prevBoxes[i].R != boxes[i].R) return true;
            return false;
        }

        // ---------------------------------------------------------------- primitives

        FormattedText Make(string s, double size, Brush b, Typeface tf)
        {
            return new FormattedText(s, Ja, FlowDirection.LeftToRight, tf, size, b, dip);
        }

        // 文字の整形 (FormattedText の生成) は描画の中でいちばん重いので、同じ文字・大きさ・色の組は使い回す
        FormattedText Cached(string s, double size, Brush b, Typeface tf, double maxW)
        {
            int bid;
            if (!brushIds.TryGetValue(b, out bid)) { bid = brushIds.Count; brushIds[b] = bid; }
            string face = ReferenceEquals(tf, TfN) ? "N" : ReferenceEquals(tf, TfB) ? "B" : ReferenceEquals(tf, TfL) ? "L" : "I";
            string key = string.Concat(s, "\u0001", size.ToString(Inv), face, bid.ToString(Inv), "\u0001", maxW.ToString(Inv));
            FormattedText ft;
            if (textCache.TryGetValue(key, out ft)) return ft;
            // 数値のように毎回変わる文字で際限なく増えないように、ある程度たまったら全部捨てる
            if (textCache.Count >= 4000) textCache.Clear();
            ft = new FormattedText(s, Ja, FlowDirection.LeftToRight, tf, size, b, dip);
            if (maxW > 0)
            {
                ft.MaxTextWidth = maxW;
                ft.MaxLineCount = 1;
                ft.Trimming = TextTrimming.CharacterEllipsis;
            }
            textCache[key] = ft;
            return ft;
        }

        // align: 0 = 左, 1 = 右, 2 = 中央。戻り値は描画幅
        double T(DrawingContext dc, string s, double x, double y, double size, Brush b, int align = 0, bool bold = false, double maxW = 0)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            FormattedText ft = Cached(s, size, b, bold ? TfB : TfN, maxW);
            double w = ft.Width;
            double dx = align == 1 ? -w : align == 2 ? -w / 2 : 0;
            DrawTextWithShadow(dc, ft, new Point(x + dx, y), b);
            return w;
        }

        void Bar(DrawingContext dc, double x, double y, double w, double h, double frac, Brush fill)
        {
            double rad = sk.BarStyle == "pill" ? h / 2 : 2;
            dc.DrawRoundedRectangle(Track, null, new Rect(x, y, w, h), rad, rad);
            if (double.IsNaN(frac)) return;
            frac = Math.Max(0, Math.Min(1, frac));
            if (frac <= 0.004) return;
            if (sk.BarStyle == "segment")
            {
                // 4px のブロックを 1px 空けて並べる
                double filled = w * frac;
                for (double bx = 0; bx < filled; bx += 5)
                    dc.DrawRectangle(fill, null, new Rect(x + bx, y + 1, Math.Min(4, filled - bx), h - 2));
                return;
            }
            // 幅は値どおりにし、角丸だけ幅に合わせて小さくする (以前は最低でも高さ分を塗っていたので 1〜9% が同じ長さに見えた)
            double fw = Math.Max(1.5, w * frac);
            double fr = Math.Min(rad, fw / 2);
            dc.DrawRoundedRectangle(fill, null, new Rect(x, y, fw, h), fr, fr);
        }

        double BarRow(DrawingContext dc, double x, double y, double frac, Brush fill, string label, Brush labelBrush, string value, Brush valueBrush)
        {
            Bar(dc, x, y + 5, 98, 9, frac, fill);
            T(dc, label, x + 106, y, FS, labelBrush);
            T(dc, value, x + ColW, y, FS, valueBrush, 1);
            return y + RowH;
        }

        double KeyValue(DrawingContext dc, double x, double y, string key, Brush keyBrush, string value, Brush valueBrush)
        {
            T(dc, key, x, y, FS, keyBrush);
            T(dc, value, x + ColW, y, FS, valueBrush, 1);
            return y + RowH;
        }

        void Header(DrawingContext dc, double x, double y, string glyph, Brush tile, string title, string right)
        {
            dc.DrawRoundedRectangle(tile, null, new Rect(x, y, 36, 36), sk.TileRadius, sk.TileRadius);
            FormattedText icon = Cached(glyph, 20, White, TfIcon, 0);
            dc.DrawText(icon, new Point(x + 18 - icon.Width / 2, y + 18 - icon.Height / 2));
            T(dc, title, x + 46, y - 3, 15.5, White, 0, true);
            if (right != null) T(dc, right, x + ColW, y - 3, 15.5, White, 1, true);
        }

        void Sep(DrawingContext dc, double x, double y)
        {
            dc.DrawLine(SepPen, new Point(x, y + 0.5), new Point(x + ColW, y + 0.5));
        }

        // Used / Free / Total の3列
        double Triple(DrawingContext dc, double x, double y, string a, string b, string c, Brush brush)
        {
            T(dc, a, x + 36, y, FS, brush, 2);
            T(dc, b, x + 110, y, FS, brush, 2);
            T(dc, c, x + 184, y, FS, brush, 2);
            return y + 17;
        }

        readonly List<Point> graphPts = new List<Point>(512);

        // max <= 0 のときは履歴の最大値に合わせて自動スケール。戻り値は使った上限値
        double Graph(DrawingContext dc, Rect r, Hist[] series, Pen[] pens, double max)
        {
            dc.DrawRectangle(GraphBg, null, r);
            dc.DrawLine(GridPen, new Point(r.X, Math.Round(r.Y + r.Height / 3) + 0.5), new Point(r.Right, Math.Round(r.Y + r.Height / 3) + 0.5));
            dc.DrawLine(GridPen, new Point(r.X, Math.Round(r.Y + r.Height * 2 / 3) + 0.5), new Point(r.Right, Math.Round(r.Y + r.Height * 2 / 3) + 0.5));
            if (max <= 0)
            {
                foreach (Hist h in series) max = Math.Max(max, h.Max());
                max = Math.Max(max * 1.15, 1024);
            }
            var clip = new RectangleGeometry(r);
            clip.Freeze();
            dc.PushClip(clip);
            for (int s = 0; s < series.Length; s++)
            {
                Hist h = series[s];
                if (h.Count < 2) continue;
                double step = r.Width / (h.Cap - 1);
                double x0 = r.Right - (h.Count - 1) * step;
                // 時間幅が長く 1px に何本も入るときは、その区間の最大値を 1 点にまとめる (ピークを潰さない)
                int stride = Math.Max(1, (int)Math.Ceiling(h.Cap / (r.Width * 2)));
                graphPts.Clear();
                for (int i = 0; i < h.Count; i += stride)
                {
                    int end = Math.Min(h.Count, i + stride);
                    float mv = 0;
                    for (int j = i; j < end; j++) { float sv = h.At(j); if (sv > mv) mv = sv; }
                    double v = Math.Max(0, Math.Min(1, mv / max));
                    graphPts.Add(new Point(x0 + (end - 1) * step, r.Bottom - 1.5 - v * (r.Height - 3)));
                }
                var g = new StreamGeometry();
                using (StreamGeometryContext ctx = g.Open())
                {
                    ctx.BeginFigure(graphPts[0], false, false);
                    for (int i = 1; i < graphPts.Count; i++) ctx.LineTo(graphPts[i], true, true);
                }
                g.Freeze();
                Pen pen = pens[s % pens.Length];
                if (sk.GraphArea)
                {
                    // 折れ線の下を薄く塗る (同じ点列を使う)
                    var area = new StreamGeometry();
                    using (StreamGeometryContext actx = area.Open())
                    {
                        actx.BeginFigure(new Point(graphPts[0].X, r.Bottom), true, true);
                        for (int i = 0; i < graphPts.Count; i++) actx.LineTo(graphPts[i], false, false);
                        actx.LineTo(new Point(r.Right, r.Bottom), false, false);
                    }
                    area.Freeze();
                    Brush fillArea;
                    if (!areaBrushes.TryGetValue(pen, out fillArea))
                    {
                        fillArea = pen.Brush.Clone();
                        fillArea.Opacity = 0.22;
                        fillArea.Freeze();
                        areaBrushes[pen] = fillArea;
                    }
                    dc.DrawGeometry(fillArea, null, area);
                }
                dc.DrawGeometry(null, pen, g);
            }
            dc.Pop();
            dc.DrawRectangle(null, GraphBorder, new Rect(r.X + 0.5, r.Y + 0.5, r.Width - 1, r.Height - 1));
            // 右上にグラフが表す時間幅
            T(dc, SpanText(cfg.HistorySeconds), r.Right - 4, r.Y + 1, 10, Gray, 1);
            return max;
        }

        // グラフの時間幅の表示 ("30秒", "2分", "1分30秒")
        public static string SpanText(int secs)
        {
            if (secs < 60) return secs.ToString(Inv) + "秒";
            if (secs % 60 == 0) return (secs / 60).ToString(Inv) + "分";
            return (secs / 60).ToString(Inv) + "分" + (secs % 60).ToString(Inv) + "秒";
        }

        // 自動スケールのグラフに上限値を小さく書く
        void ScaleLabel(DrawingContext dc, Rect r, string text)
        {
            T(dc, text, r.X + 4, r.Y + 1, 10, Gray);
        }

        Brush Level(double value, double warn, double danger, Brush normal)
        {
            return value >= danger ? sk.Danger : value >= warn ? sk.Warn : normal;
        }

        // ---------------------------------------------------------------- formatting

        static string Sig(double v)
        {
            // 丸めで桁が上がる手前で書式を切り替える (999.96 を "1000.0" にしない)
            if (v >= 999.95) return v.ToString("0", Inv);
            if (v >= 99.995) return v.ToString("0.0", Inv);
            if (v >= 9.9995) return v.ToString("0.00", Inv);
            return v.ToString("0.000", Inv);
        }

        static readonly string[] ByteUnits = { "B", "kB", "MB", "GB", "TB", "PB" };

        static string Bytes(double b)
        {
            if (double.IsNaN(b)) return "--";   // nvidia-smi が [N/A] を返す環境など
            int i = 0;
            while (b >= 1023.5 && i < ByteUnits.Length - 1) { b /= 1024; i++; }   // 1023.6 を "1024 MB" にしない
            return (i == 0 ? b.ToString("0", Inv) : Sig(b)) + " " + ByteUnits[i];
        }

        static string BytesShort(double b)
        {
            if (double.IsNaN(b)) return "--";
            int i = 0;
            while (b >= 1023.95 && i < ByteUnits.Length - 1) { b /= 1024; i++; }
            return b.ToString(i == 0 ? "0" : "0.0", Inv) + " " + ByteUnits[i];
        }

        static string Bits(double bytesPerSec)
        {
            if (double.IsNaN(bytesPerSec)) return "--";
            string[] u = { "bit/s", "kbit/s", "Mbit/s", "Gbit/s" };
            double v = bytesPerSec * 8;
            int i = 0;
            while (v >= 1000 && i < u.Length - 1) { v /= 1000; i++; }
            return (i == 0 ? v.ToString("0", Inv) : Sig(v)) + " " + u[i];
        }

        static string Pct(double v) { return double.IsNaN(v) ? "--%" : Math.Round(v, MidpointRounding.AwayFromZero).ToString("0", Inv) + "%"; }   // 12.5 → 13% (銀行丸めにしない)

        static string N(double v, string fmt, string unit)
        {
            return double.IsNaN(v) ? "--" : v.ToString(fmt, Inv) + unit;
        }

        static string Span(TimeSpan t)
        {
            return string.Format(Inv, "{0}d {1}h {2:00}m {3:00}s", (int)t.TotalDays, t.Hours, t.Minutes, t.Seconds);
        }

        // ---------------------------------------------------------------- GPU

        double DrawGpu(DrawingContext dc, double x, double y)
        {
            Header(dc, x, y, "", TileGreen, "GPU Meter", st.GpuOk ? Pct(st.GpuUtil) : null);
            if (!st.GpuOk)
            {
                T(dc, "GPU情報を取得できません", x + 46, y + 19, 12.5, Gray, 0, false, ColW - 46);
                return y + 44;
            }
            T(dc, st.GpuName, x + 46, y + 19, 12.5, Yellow, 0, false, ColW - 46);
            // nvidia-smi が無い環境では Windows のカウンターから使用率と VRAM だけ取る (クロック・温度・電力は無い)
            bool basic = float.IsNaN(st.GpuClock);
            T(dc, basic ? "使用率と VRAM のみ (Windows の GPU カウンター)" : "Clock: " + N(st.GpuClock, "0", " MHz"), x + 46, y + 35, basic ? 11.5 : 12.5, basic ? Gray : Yellow, 0, false, ColW - 46);
            y += 58;

            double used = st.VramUsedMB * 1048576.0, total = st.VramTotalMB * 1048576.0;
            y = Triple(dc, x, y, "Used", "Free", "Total", White);
            y = Triple(dc, x, y, Bytes(used), Bytes(Math.Max(0, total - used)), Bytes(total), Cyan) + 2;
            double vram = total > 0 ? used / total : 0;
            Brush vramBrush = Level(vram, 0.9, 0.97, Cyan);
            y = BarRow(dc, x, y, vram, vramBrush, "VRAM", Cyan, Pct(vram * 100), vramBrush);
            if (!float.IsNaN(st.GpuMemClock)) y = KeyValue(dc, x, y, "Memory Clock:", Orange, N(st.GpuMemClock, "0", " MHz"), Orange);
            Sep(dc, x, y + 3);
            y += 8;
            if (!float.IsNaN(st.GpuTemp))
            {
                Brush tempBrush = Level(st.GpuTemp, 78, 86, Green);   // 高温になったら色で知らせる
                y = BarRow(dc, x, y, st.GpuTemp / 100.0, tempBrush, "Temp", Green, N(st.GpuTemp, "0", " °C"), tempBrush);
            }
            y = BarRow(dc, x, y, st.GpuUtil / 100.0, Yellow, "GPU", Yellow, Pct(st.GpuUtil), Yellow);
            if (!float.IsNaN(st.GpuPowerW))
            {
                string pw = st.GpuPowerW.ToString("0", Inv) + " W";
                if (st.GpuPowerLimitW > 0) pw += "  " + (st.GpuPowerW / st.GpuPowerLimitW * 100).ToString("0.0", Inv) + "%";
                y = KeyValue(dc, x, y, "消費電力:", Orange, pw, Orange);
            }
            if (!float.IsNaN(st.GpuFan)) y = KeyValue(dc, x, y, "Fan:", Violet, Pct(st.GpuFan), Violet);
            y += 4;
            Graph(dc, new Rect(x, y, ColW, 44), new[] { st.GpuTempHist, st.GpuUtilHist }, new[] { PenGreen, PenYellow }, 100);
            return y + 44;
        }

        // ---------------------------------------------------------------- network

        double DrawNet(DrawingContext dc, double x, double y)
        {
            Header(dc, x, y, "", TileTeal, "Network Meter", null);
            T(dc, "Peak: " + Bits(st.PeakBps), x + 46, y + 19, 12.5, Lime);
            y += 44;
            if (cfg.ShowIp) y = KeyValue(dc, x, y, "Int IP:", Cyan, st.IntIp ?? "未接続", Cyan);
            y = KeyValue(dc, x, y, "Ping:", Yellow, st.PingMs >= 0 ? st.PingMs.ToString(Inv) + " ms" : "--", Yellow);
            y = UpDown(dc, x, y, Bits(st.UpBps), Bytes(st.UpBps) + "/s", Bits(st.DownBps), Bytes(st.DownBps) + "/s") + 4;
            var netRect = new Rect(x, y, ColW, 44);
            ScaleLabel(dc, netRect, Bits(Graph(dc, netRect, new[] { st.UpHist, st.DownHist }, new[] { PenGreen, PenYellow }, 0)));
            y += 50;
            T(dc, "Current", x + 55, y, FS, Cyan, 2);
            T(dc, "Total", x + 165, y, FS, Cyan, 2);
            y += 17;
            return UpDown(dc, x, y, Bytes(st.SessUp), Bytes(st.TotUp), Bytes(st.SessDown), Bytes(st.TotDown));
        }

        // 上り(緑▲) と 下り(黄▼) を2列で表示
        double UpDown(DrawingContext dc, double x, double y, string upL, string upR, string downL, string downR)
        {
            T(dc, "▲", x, y + 1, 11, Green);
            T(dc, upL, x + 15, y, 12.5, Yellow);
            T(dc, "▲", x + 116, y + 1, 11, Green);
            T(dc, upR, x + ColW, y, 12.5, Yellow, 1);
            y += RowH;
            T(dc, "▼", x, y + 1, 11, Yellow);
            T(dc, downL, x + 15, y, 12.5, Cyan);
            T(dc, "▼", x + 116, y + 1, 11, Yellow);
            T(dc, downR, x + ColW, y, 12.5, Cyan, 1);
            return y + RowH;
        }

        // ---------------------------------------------------------------- drives

        double DrawDrives(DrawingContext dc, double x, double y)
        {
            Header(dc, x, y, "", TileSlate, "Drives Meter", Pct(st.DiskActive));
            T(dc, "Activity", x + 46, y + 19, 12.5, Gray);
            y += 44;
            driveBoxes.Clear();
            foreach (DriveRow d in st.Drives)
            {
                double usedFrac = d.Total > 0 ? (double)(d.Total - d.Free) / d.Total : 0;
                driveBoxes.Add(new KeyValuePair<Rect, string>(new Rect(x, y, ColW, RowH + 16 + RowH + 2), d.Name));
                T(dc, d.Name + " " + d.Label, x, y, FS, Cyan, 0, false, ColW - 44);
                T(dc, Pct(usedFrac * 100), x + ColW, y, FS, White, 1);
                y += RowH;
                T(dc, "Used: " + Bytes(d.Total - d.Free), x, y, 12, Cyan);
                T(dc, "Free: " + Bytes(d.Free), x + ColW, y, 12, Cyan, 1);
                y += 16;
                T(dc, "Total: " + Bytes(d.Total), x, y, 12, Cyan);
                Bar(dc, x + 112, y + 5, ColW - 112, 8, usedFrac, usedFrac > 0.9 ? Red : Cyan);
                y += RowH + 2;
            }
            T(dc, "▲", x, y + 1, 11, Green);
            T(dc, Bytes(st.ReadBps) + "/s", x + 15, y, 12.5, Lime);
            T(dc, "▼", x + 116, y + 1, 11, Yellow);
            T(dc, Bytes(st.WriteBps) + "/s", x + ColW, y, 12.5, Yellow, 1);
            y += RowH + 4;
            var ioRect = new Rect(x, y, ColW, 44);
            ScaleLabel(dc, ioRect, Bytes(Graph(dc, ioRect, new[] { st.ReadHist, st.WriteHist }, new[] { PenGreen, PenYellow }, 0)) + "/s");
            return y + 44;
        }

        // ---------------------------------------------------------------- top memory

        double DrawTopMem(DrawingContext dc, double x, double y)
        {
            Header(dc, x, y, "", TileRose, "Top Memory Meter", null);
            T(dc, "プロセス別のメモリ", x + 46, y + 19, 12.5, Gray);
            y += 44;
            long max = 1;
            foreach (ProcRow p in st.MemProcs) if (p.Mem > max) max = p.Mem;
            for (int i = 0; i < st.MemProcs.Count; i++)
            {
                ProcRow p = st.MemProcs[i];
                Brush b = Palette[i % Palette.Length];
                T(dc, p.Name, x, y, FS, b, 0, false, 92);
                Bar(dc, x + 98, y + 6, 54, 7, (double)p.Mem / max, b);
                T(dc, BytesShort(p.Mem), x + ColW, y, FS, b, 1);
                y += RowH;
            }
            return y;
        }

        // ---------------------------------------------------------------- CPU / RAM

        double DrawCpu(DrawingContext dc, double x, double y)
        {
            Header(dc, x, y, "", TileBlue, "CPU Meter", Pct(st.CpuTotal));
            T(dc, st.CpuName, x + 46, y + 19, 12.5, Yellow, 0, false, ColW - 46);
            T(dc, "Clock: " + st.CpuMHz.ToString("0", Inv) + " MHz", x + 46, y + 35, 12.5, Yellow);
            y += 58;
            // HWiNFO の値があるときだけ 1 行足す: "Temp 48 °C  45 W  1200 rpm" (無いときは高さを変えない)
            if (st.HwInfoOk && (!float.IsNaN(st.CpuTemp) || !float.IsNaN(st.CpuPowerW) || !float.IsNaN(st.CpuFanRpm)))
            {
                double hx = x + 46, hy = y - 7;
                if (!float.IsNaN(st.CpuTemp))
                {
                    Brush tb = Level(st.CpuTemp, 80, 90, Green);   // 高温になったら色で知らせる
                    hx += T(dc, "Temp " + N(st.CpuTemp, "0", " °C"), hx, hy, 12.5, tb) + 8;
                }
                if (!float.IsNaN(st.CpuPowerW)) hx += T(dc, N(st.CpuPowerW, "0", " W"), hx, hy, 12.5, Orange) + 8;
                if (!float.IsNaN(st.CpuFanRpm)) T(dc, N(st.CpuFanRpm, "0", " rpm"), hx, hy, 12.5, Violet);
                y += 16;
            }

            // タスクマネージャー風の情報行 (メモリは別パネルへ分けた)
            T(dc, string.Format(Inv, "プロセス {0}  スレッド {1}  ハンドル {2}", st.ProcessCount, st.ThreadCount, st.HandleCount), x, y, 11.5, Gray, 0, false, ColW);
            y += 17;
            string topo = st.CpuCores > 0
                ? string.Format(Ja, "物理 {0} コア / 論理 {1}{2}", st.CpuCores, st.Cores.Length, st.CpuL3KB > 0 ? "   L3 " + (st.CpuL3KB / 1024.0).ToString("0.#", Inv) + " MB" : "")   // WMI が L3 を返さない PC では出さない
                : string.Format(Ja, "論理プロセッサ {0}", st.Cores.Length);
            T(dc, topo, x, y, 12, Gray, 0, false, ColW);
            y += 17;
            Sep(dc, x, y + 3);
            y += 8;

            // 論理プロセッサが多いときは縦に収まらないので自動で列を増やす (32 まで 1 列、64 まで 2 列、96 まで 3 列、それ以上 4 列)
            int ccols = st.Cores.Length > 32 ? Math.Min(4, (st.Cores.Length + 31) / 32) : cfg.CompactCores ? 2 : 1;
            if (ccols > 1)
            {
                // 左右に並ぶものは (P コアなら) 同じ物理コアのスレッド。3 列以上は幅が足りないのでバーだけにする
                double cellW = (ColW - 10 * (ccols - 1)) / ccols;
                for (int i = 0; i < st.Cores.Length; i++)
                {
                    Brush b = Palette[i % Palette.Length];
                    double cx = x + (i % ccols) * (cellW + 10), cy = y + (i / ccols) * RowH;
                    if (ccols >= 3) { Bar(dc, cx, cy + 5, cellW, 9, st.Cores[i] / 100.0, b); continue; }
                    Bar(dc, cx, cy + 5, 44, 9, st.Cores[i] / 100.0, b);
                    T(dc, (i + 1).ToString(Inv), cx + 49, cy + 1, 12, b);
                    T(dc, Pct(st.Cores[i]), cx + cellW, cy, FS, b, 1);
                }
                y += (st.Cores.Length + ccols - 1) / ccols * RowH;
            }
            else
            {
                for (int i = 0; i < st.Cores.Length; i++)
                {
                    Brush b = Palette[i % Palette.Length];
                    y = BarRow(dc, x, y, st.Cores[i] / 100.0, b, "Core " + (i + 1).ToString(Inv), b, Pct(st.Cores[i]), b);
                }
            }
            y += 4;
            Graph(dc, new Rect(x, y, ColW, 46), st.CoreHist, PalettePens, 100);
            return y + 46;
        }

        double DrawTop(DrawingContext dc, double x, double y)
        {
            Header(dc, x, y, "", TilePurple, "Top CPU Meter", null);
            T(dc, "プロセス別の使用率", x + 46, y + 19, 12.5, Gray);
            y += 44;
            // 真ん中はメモリ量ではなく CPU 使用率のバー (一番使っているプロセスを 100% とした相対値)
            float max = 0.01f;
            foreach (ProcRow p in st.Procs) if (p.Cpu > max) max = p.Cpu;
            for (int i = 0; i < st.Procs.Count; i++)
            {
                ProcRow p = st.Procs[i];
                Brush b = Palette[i % Palette.Length];
                T(dc, p.Name, x, y, FS, b, 0, false, 92);
                Bar(dc, x + 98, y + 6, 66, 7, p.Cpu / max, b);
                T(dc, p.Cpu.ToString("0.0", Inv) + "%", x + ColW, y, FS, b, 1);
                y += RowH;
            }
            return y - 4;   // 一番下のグラフは無し (CPU 全体の推移は CPU Meter にある)
        }

        // ---------------------------------------------------------------- clock / calendar

        double DrawClock(DrawingContext dc, double x, double y)
        {
            DateTime now = DateTime.Now;
            FormattedText hm = Make(now.ToString("HH:mm", Inv), 40, Cyan, TfB);
            FormattedText ss = Make(now.ToString("ss", Inv), 23, Cyan, TfB);
            double sx = x + (ColW - (hm.Width + 5 + ss.Width)) / 2;
            dc.DrawText(hm, new Point(sx, y));
            dc.DrawText(ss, new Point(sx + hm.Width + 5, y + hm.Baseline - ss.Baseline));
            y += 52;
            T(dc, now.ToString("yyyy年M月d日 (ddd)", Ja), x + ColW / 2, y, 13.5, Lime, 2);
            y += 20;
            // ISO 8601 の週番号。.NET の GetWeekOfYear は年末年始で 1 週ずれるので、その週の木曜日で数える
            DateTime thu = now.AddDays(3 - ((int)now.DayOfWeek + 6) % 7).Date;
            int week = Inv.Calendar.GetWeekOfYear(thu, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
            T(dc, string.Format(Inv, "Day: {0}    Week: {1}    Q{2}", now.DayOfYear, week, (now.Month - 1) / 3 + 1), x + ColW / 2, y, 12.5, Yellow, 2);
            y += 22;
            Sep(dc, x, y);
            y += 6;
            y = KeyValue(dc, x, y, "Uptime:", Orange, Span(TimeSpan.FromMilliseconds(Native.GetTickCount64())), Orange);
            DateTime newYear = new DateTime(now.Year + 1, 1, 1);
            y = KeyValue(dc, x, y, (now.Year + 1).ToString(Inv) + "年まで:", Cyan, Span(newYear - now), Red);
            Sep(dc, x, y + 4);
            return y + 12;
        }

        double DrawCalendar(DrawingContext dc, double x, double y)
        {
            DateTime today = DateTime.Today;
            T(dc, today.ToString("yyyy年 M月", Ja), x + ColW / 2, y, 14.5, White, 2, true);
            // 今日が祝日なら見出しの右に祝日名を小さく出す
            string todayHol = cfg.ShowHolidays ? Holidays.Name(today) : null;
            if (todayHol != null) T(dc, todayHol, x + ColW, y + 4, 11, sk.CalSun, 1, true, ColW / 3);
            y += 25;
            double cw = ColW / 7, ch = 23;
            string[] names = { "日", "月", "火", "水", "木", "金", "土" };
            int dow = (int)today.DayOfWeek;
            for (int i = 0; i < 7; i++)
            {
                dc.DrawRectangle(i == dow ? TodayOrange : CalHead, null, new Rect(x + i * cw + 1, y, cw - 2, 20));
                T(dc, names[i], x + i * cw + cw / 2, y + 1, 12.5, White, 2);
            }
            y += 24;
            DateTime first = new DateTime(today.Year, today.Month, 1);
            DateTime start = first.AddDays(-(int)first.DayOfWeek);
            for (int r = 0; r < 6; r++)
            {
                for (int c = 0; c < 7; c++)
                {
                    DateTime d = start.AddDays(r * 7 + c);
                    double cx = x + c * cw + cw / 2, cy = y + r * ch;
                    if (d == today)
                    {
                        dc.DrawRectangle(TodayOrange, null, new Rect(x + c * cw + 3, cy, cw - 6, ch - 2));
                        T(dc, d.Day.ToString(Inv), cx, cy + 1, 13.5, White, 2, true);
                    }
                    else
                    {
                        bool hol = cfg.ShowHolidays && Holidays.IsHoliday(d);
                        Brush b = d.Month != today.Month ? Dim : c == 0 || hol ? sk.CalSun : c == 6 ? sk.CalSat : Cyan;
                        T(dc, d.Day.ToString(Inv), cx, cy + 1, 13.5, b, 2);
                    }
                }
            }
            return y + 6 * ch;
        }

        double DrawDateTile(DrawingContext dc, double x, double y)
        {
            DateTime today = DateTime.Today;
            const double th = 158;
            dc.DrawRoundedRectangle(TileGradient, null, new Rect(x, y, ColW, th), 4, 4);
            T(dc, today.ToString("dddd", Ja), x + ColW / 2, y + 8, 17, sk.DateTileText, 2);
            FormattedText day = Make(today.Day.ToString(Inv), 88, sk.DateTileText, TfL);
            dc.DrawText(day, new Point(x + (ColW - day.Width) / 2, y + 18));
            T(dc, today.ToString("M月 yyyy", Ja), x + ColW / 2, y + th - 28, 15, sk.DateTileText, 2);
            return y + th;
        }

        // ---------------------------------------------------------------- weather

        double DrawWeather(DrawingContext dc, double x, double y)
        {
            T(dc, "Weather Meter", x + ColW / 2, y, 15.5, White, 2, true);
            y += 26;
            if (!cfg.WeatherEnabled)
            {
                T(dc, "天気は無効です (settings.json)", x + ColW / 2, y, 12.5, Gray, 2);
                return y + RowH;
            }
            Weather w = st.Weather;
            if (w == null)
            {
                T(dc, st.WeatherError == null ? "取得中..." : "取得できません: " + st.WeatherError, x + ColW / 2, y, 12.5, Gray, 2, false, ColW);
                return y + RowH;
            }

            double moonPhase = MoonAge(DateTime.UtcNow) / Synodic;
            WeatherIcon(dc, x + 2, y, 76, w.Code, w.IsDay, moonPhase);
            T(dc, N(w.Temp, "0", "°C"), x + ColW, y - 8, 36, Lime, 1, true);
            T(dc, "Hi: " + N(w.Hi, "0", "°") + "   Lo: " + N(w.Lo, "0", "°"), x + ColW, y + 38, 12.5, Cyan, 1);
            T(dc, "体感: " + N(w.Feels, "0", "°"), x + ColW, y + 55, 12.5, Cyan, 1);
            y += 78;
            T(dc, Describe(w.Code), x + ColW, y, FS, Yellow, 1);
            y += 17;
            T(dc, cfg.LocationName, x + ColW, y, FS, Lime, 1, false, ColW);
            y += 22;

            y = KeyValue(dc, x, y, "湿度:", Yellow, N(w.Humidity, "0", "%"), Yellow);
            y = KeyValue(dc, x, y, "気圧:", Red, N(w.Pressure, "0.0", " hPa"), Red);
            y = KeyValue(dc, x, y, "風:", Magenta, N(w.Wind, "0", " km/h") + "  " + Compass(w.WindDir), Magenta);
            y = KeyValue(dc, x, y, "UV指数:", Violet, N(w.Uv, "0.0", "") + "  " + UvLabel(w.Uv), Violet);
            y = KeyValue(dc, x, y, "降水確率:", Cyan, N(w.PrecipProb, "0", "%"), Cyan);
            if (w.Sunrise > DateTime.MinValue && w.Sunset > DateTime.MinValue)
            {
                y = KeyValue(dc, x, y, "日の出:", Orange, w.Sunrise.ToString("H:mm", Inv), Orange);
                y = KeyValue(dc, x, y, "日の入:", Orange, w.Sunset.ToString("H:mm", Inv), Orange);
                TimeSpan len = w.Sunset - w.Sunrise;
                y = KeyValue(dc, x, y, "昼の長さ:", Yellow, string.Format(Inv, "{0}:{1:00}", (int)len.TotalHours, len.Minutes), Yellow);
            }
            double age = moonPhase * Synodic;
            T(dc, "月齢:", x, y, FS, Gray);
            double tw = T(dc, age.ToString("0.0", Inv) + "  " + MoonName(age), x + ColW, y, FS, Gray, 1);
            MoonDisc(dc, x + ColW - tw - 14, y + 9.5, 7, moonPhase);
            y += RowH + 3;
            if (cfg.ShowForecast && w.Days.Count > 0)
            {
                Sep(dc, x, y);
                y += 6;
                y = DrawForecast(dc, x, y, w.Days, moonPhase);
            }
            Sep(dc, x, y);
            y += 4;
            T(dc, "更新: " + w.Updated.ToString("M/d H:mm", Inv) + "  (Open-Meteo)", x + ColW / 2, y, 11.5, Cyan, 2);
            return y + 14;
        }

        // 明日からの予報を3列で表示: 曜日 / 小さい天気アイコン / 最高・最低 / 降水確率
        double DrawForecast(DrawingContext dc, double x, double y, List<DayForecast> days, double moonPhase)
        {
            const double iconS = 30;
            int n = Math.Min(3, days.Count);
            double cw = ColW / 3;
            for (int i = 0; i < n; i++)
            {
                DayForecast f = days[i];
                double cx = x + i * cw + cw / 2;
                string hol = cfg.ShowHolidays ? Holidays.Name(f.Date) : null;
                Brush dayBrush = f.Date.DayOfWeek == DayOfWeek.Sunday || hol != null ? sk.CalSun
                    : f.Date.DayOfWeek == DayOfWeek.Saturday ? sk.CalSat : White;
                T(dc, f.Date.ToString("M/d (ddd)", Ja), cx, y, 11.5, dayBrush, 2, true);
                WeatherIcon(dc, cx - iconS / 2, y + 15, iconS, f.Code, true, moonPhase);
                T(dc, N(f.Hi, "0", "°") + " / " + N(f.Lo, "0", "°"), cx, y + 47, 11.5, Cyan, 2);
                T(dc, N(f.PrecipProb, "0", "%"), cx, y + 61, 11, Lime, 2);
            }
            return y + 78;
        }

        const double Synodic = 29.530588853;

        static double MoonAge(DateTime utc)
        {
            double jd = utc.ToOADate() + 2415018.5;
            double age = (jd - 2451550.1) % Synodic;
            return age < 0 ? age + Synodic : age;
        }

        static string MoonName(double age)
        {
            if (age < 1.5 || age >= 28.0) return "新月";
            if (age < 6.0) return "三日月";
            if (age < 9.0) return "上弦";
            if (age < 13.5) return "十三夜";
            if (age < 16.5) return "満月";
            if (age < 21.0) return "寝待月";
            if (age < 24.0) return "下弦";
            return "有明月";
        }

        static string Describe(int code)
        {
            switch (code)
            {
                case 0: return "快晴";
                case 1: return "晴れ";
                case 2: return "所により曇り";
                case 3: return "曇り";
                case 45: case 48: return "霧";
                case 51: case 53: case 55: return "霧雨";
                case 56: case 57: return "着氷性の霧雨";
                case 61: return "弱い雨";
                case 63: return "雨";
                case 65: return "強い雨";
                case 66: case 67: return "着氷性の雨";
                case 71: return "弱い雪";
                case 73: return "雪";
                case 75: return "強い雪";
                case 77: return "霧雪";
                case 80: case 81: return "にわか雨";
                case 82: return "激しいにわか雨";
                case 85: case 86: return "にわか雪";
                case 95: return "雷雨";
                case 96: case 99: return "雷雨 (ひょう)";
                default: return "--";
            }
        }

        static string Compass(double deg)
        {
            if (double.IsNaN(deg)) return "";
            string[] d = { "北", "北北東", "北東", "東北東", "東", "東南東", "南東", "南南東", "南", "南南西", "南西", "西南西", "西", "西北西", "北西", "北北西" };
            return d[(int)Math.Round(((deg % 360) + 360) % 360 / 22.5) % 16];
        }

        static string UvLabel(double uv)
        {
            if (double.IsNaN(uv)) return "";
            if (uv < 3) return "弱い";
            if (uv < 6) return "中程度";
            if (uv < 8) return "強い";
            if (uv < 11) return "非常に強い";
            return "極端に強い";
        }

        // phase: 0 = 新月, 0.5 = 満月 (北半球での見え方)
        void MoonDisc(DrawingContext dc, double cx, double cy, double r, double phase)
        {
            dc.DrawEllipse(MoonDark, null, new Point(cx, cy), r, r);
            double c = Math.Cos(2 * Math.PI * phase);
            bool waxing = phase < 0.5, crescent = c > 0;
            var top = new Point(cx, cy - r);
            var bottom = new Point(cx, cy + r);
            var g = new StreamGeometry();
            using (StreamGeometryContext ctx = g.Open())
            {
                ctx.BeginFigure(top, true, true);
                ctx.ArcTo(bottom, new Size(r, r), 0, false, waxing ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true, false);
                SweepDirection back = waxing == crescent ? SweepDirection.Counterclockwise : SweepDirection.Clockwise;
                ctx.ArcTo(top, new Size(Math.Max(0.01, Math.Abs(c) * r), r), 0, false, back, true, false);
            }
            g.Freeze();
            dc.DrawGeometry(MoonLit, null, g);
        }

        void WeatherIcon(DrawingContext dc, double x, double y, double s, int code, bool day, double moonPhase)
        {
            bool cloud = code >= 2;
            bool sky = code <= 2;
            bool thunder = code >= 95;
            bool snow = (code >= 71 && code <= 77) || code == 85 || code == 86;
            bool rain = (code >= 51 && code <= 67) || (code >= 80 && code <= 82) || thunder;
            bool fog = code == 45 || code == 48;

            if (sky)
            {
                double cx = cloud ? x + s * 0.64 : x + s * 0.5, cy = cloud ? y + s * 0.34 : y + s * 0.5;
                double r = cloud ? s * 0.17 : s * 0.24;
                if (day)
                {
                    dc.DrawEllipse(SunBrush, null, new Point(cx, cy), r, r);
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4;
                        dc.DrawLine(SunPen, new Point(cx + Math.Cos(a) * r * 1.35, cy + Math.Sin(a) * r * 1.35),
                            new Point(cx + Math.Cos(a) * r * 1.75, cy + Math.Sin(a) * r * 1.75));
                    }
                }
                else MoonDisc(dc, cx, cy, r * 1.25, moonPhase);
            }
            if (cloud || fog)
            {
                Brush b = code >= 3 ? CloudDark : CloudLight;
                double oy = (rain || snow) ? -s * 0.08 : 0;
                dc.DrawEllipse(b, null, new Point(x + s * 0.34, y + s * 0.58 + oy), s * 0.16, s * 0.16);
                dc.DrawEllipse(b, null, new Point(x + s * 0.52, y + s * 0.48 + oy), s * 0.21, s * 0.21);
                dc.DrawEllipse(b, null, new Point(x + s * 0.69, y + s * 0.60 + oy), s * 0.14, s * 0.14);
                dc.DrawRoundedRectangle(b, null, new Rect(x + s * 0.18, y + s * 0.56 + oy, s * 0.65, s * 0.20), s * 0.1, s * 0.1);
            }
            if (rain)
            {
                for (int i = 0; i < 3; i++)
                {
                    double rx = x + s * (0.36 + i * 0.16);
                    dc.DrawLine(RainPen, new Point(rx, y + s * 0.76), new Point(rx - s * 0.05, y + s * 0.92));
                }
            }
            if (snow)
            {
                for (int i = 0; i < 3; i++)
                    dc.DrawEllipse(SnowBrush, null, new Point(x + s * (0.34 + i * 0.17), y + s * (0.80 + (i % 2) * 0.08)), 2.4, 2.4);
            }
            if (thunder)
            {
                var g = new StreamGeometry();
                using (StreamGeometryContext ctx = g.Open())
                {
                    ctx.BeginFigure(new Point(x + s * 0.58, y + s * 0.62), true, true);
                    ctx.LineTo(new Point(x + s * 0.44, y + s * 0.80), true, false);
                    ctx.LineTo(new Point(x + s * 0.53, y + s * 0.80), true, false);
                    ctx.LineTo(new Point(x + s * 0.46, y + s * 0.98), true, false);
                    ctx.LineTo(new Point(x + s * 0.66, y + s * 0.74), true, false);
                    ctx.LineTo(new Point(x + s * 0.56, y + s * 0.74), true, false);
                }
                g.Freeze();
                dc.DrawGeometry(SunBrush, null, g);
            }
            if (fog)
            {
                for (int i = 0; i < 2; i++)
                    dc.DrawLine(FogPen, new Point(x + s * 0.2, y + s * (0.84 + i * 0.09)), new Point(x + s * 0.8, y + s * (0.84 + i * 0.09)));
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using WF = System.Windows.Forms;

namespace DeskMeter
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // --snapshot <png> [--delay 秒] : 描画内容をPNGに保存して終了する (表示確認用)
            string snapshot = null;
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] == "--snapshot") snapshot = args[i + 1];
                if (args[i] == "--delay") int.TryParse(args[i + 1], out MainWindow.SnapshotDelay);
            }
            if (Array.IndexOf(args, "--edit") >= 0) MainWindow.SnapshotEdit = true;   // 編集モードの見た目を確認する用
            // 起動中のインスタンスへの命令 (スクリプトや自動テスト用): 配置編集の切り替え / 設定の再読み込み
            if (Array.IndexOf(args, "--toggle-edit") >= 0 || Array.IndexOf(args, "--reload") >= 0)
            {
                try
                {
                    using (EventWaitHandle ev = EventWaitHandle.OpenExisting(Array.IndexOf(args, "--toggle-edit") >= 0 ? MainWindow.EditEventName : MainWindow.ReloadEventName)) ev.Set();
                }
                catch (Exception ex) { Log.Write("command", ex); }
                return;
            }

            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Exception ex = e.ExceptionObject as Exception;
                if (ex != null) Log.Write("unhandled", ex);
            };

            // モニターごとに拡大率が違っても、そのモニターの拡大率でくっきり描く
            try
            {
                AppContext.SetSwitch("Switch.System.Windows.DoNotScaleForDpiChanges", false);
                Native.SetProcessDpiAwarenessContext(Native.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
            }
            catch { }

            bool created;
            using (var mutex = new Mutex(true, "DeskMeter_SingleInstance_" + (snapshot == null ? "app" : "snapshot"), out created))
            {
                if (!created)
                {
                    // 二重起動: 黙って終わらず、起動中のインスタンスに「表示して」と知らせる
                    if (snapshot == null) MainWindow.SignalShow();
                    return;
                }
                WF.Application.EnableVisualStyles();
                // 1秒に1回しか描かない透過ウィンドウなので、GPUデバイスを作らずソフトウェア描画にして常駐メモリを抑える
                RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
                var app = new Application();
                app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                app.DispatcherUnhandledException += delegate(object s, DispatcherUnhandledExceptionEventArgs e)
                {
                    Log.Write("dispatcher", e.Exception);
                    e.Handled = true;
                };
                app.Run(new MainWindow(snapshot));
            }
        }
    }

    sealed partial class MainWindow : Window
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string AppName = "DeskMeter";
        const int EdgeMargin = 8, SnapDistance = 16;
        const string ShowEventName = @"Local\DeskMeter_Show";   // 2 つ目のプロセスがセットする名前付きイベント
        const int HotkeyId = 0x4D45;
        public static int SnapshotDelay = 8;
        public static bool SnapshotEdit;

        readonly Settings cfg;
        readonly Sampler sampler;
        readonly MeterView view;
        readonly string snapshotPath;
        readonly DispatcherTimer timer = new DispatcherTimer();
        readonly DispatcherTimer displayChangeTimer = new DispatcherTimer();

        IntPtr hwnd;
        bool showDesktop;
        bool dragging;
        bool sessionLocked;
        Native.WinEventProc winEventProc;   // GC されないよう保持
        IntPtr winEventHook;
        bool hotkeyRegistered;
        EventWaitHandle showEvent;
        internal const string EditEventName = @"Local\DeskMeter_Cmd_ToggleEdit";   // --toggle-edit (自動テスト・スクリプト用)
        internal const string ReloadEventName = @"Local\DeskMeter_Cmd_Reload";    // --reload
        EventWaitHandle editEvent, reloadEvent;
        readonly ManualResetEvent showStop = new ManualResetEvent(false);

        WF.NotifyIcon tray;
        WF.ContextMenuStrip menu;
        WF.ToolStripMenuItem miDesktop, miTopmost, miNormal, miLock, miClick, miAuto, miOpacity, miScale, miPanels, miVisible,
            miInterval, miSkin, miMonitor, miEdit, miHistory, miHotkey, miShadow, miImages, miMascotPlace, miMascotSize, miBgImageOpacity;

        public MainWindow(string snapshotPath)
        {
            this.snapshotPath = snapshotPath;
            cfg = Settings.Load();

            Title = AppName;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            UseLayoutRounding = true;
            Left = 0;
            Top = 0;

            sampler = new Sampler(cfg);
            view = new MeterView(sampler.S, cfg);
            ApplyScaleAll();
            Content = view;
            view.LayoutChanged += delegate { cfg.Save(); };
            view.PanelActivated += OpenPanelApp;

            MouseLeftButtonDown += delegate
            {
                if (cfg.Locked || view.EditMode) return;
                dragging = true;
                try { DragMove(); } catch { }
                dragging = false;
                SavePosition();      // 端に近ければ吸着させたうえで、モニター基準の位置として保存
                RestorePosition();
            };
            MouseRightButtonUp += delegate { ShowMenu(); };
            Closed += delegate { Cleanup(); };
            // 内容の高さが決まった / 変わったときに、右端・下端基準の位置を保つ
            SizeChanged += delegate { if (!dragging) RestorePosition(); };

            // 再描画は計測が終わるたびに 1 回。時計の秒表示のためだけに、間隔が 1 秒より長いときは timer で秒の変わり目にも描く
            sampler.Sampled += delegate
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(delegate
                {
                    InvalidateAll();
                    if (!timer.IsEnabled) ScheduleNextTick();
                }));
            };
            timer.Tick += delegate
            {
                InvalidateAll();
                ScheduleNextTick();
            };
            ScheduleNextTick();

            displayChangeTimer.Interval = TimeSpan.FromMilliseconds(1200);
            displayChangeTimer.Tick += delegate
            {
                displayChangeTimer.Stop();
                RestorePosition();
                foreach (PanelWindow f in floats) f.RestorePosition();   // 切り離したパネルも画面内に戻す
            };

            sampler.Start();
            timer.Start();
            SyncFloating();

            if (snapshotPath == null)
            {
                BuildMenu();
                BuildTray();
                SyncAutoStartPath();
                SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
                SystemEvents.SessionSwitch += OnSessionSwitch;
                StartShowListener();
                StartSettingsWatcher();
                if (Settings.BrokenBackupPath != null && tray != null)
                    tray.ShowBalloonTip(8000, AppName, "settings.json が読めなかったので既定の設定で起動しました。元のファイルは " + Path.GetFileName(Settings.BrokenBackupPath) + " に退避してあります。", WF.ToolTipIcon.Warning);
                else WarnSettingsProblems();
                WarnSkinProblems();
                MeterView.ImageFailed += OnImageFailed;
            }
            else
            {
                var once = new DispatcherTimer();
                if (SnapshotEdit) view.EditMode = true;
                once.Interval = TimeSpan.FromSeconds(Math.Max(1, SnapshotDelay));
                once.Tick += delegate { once.Stop(); SaveSnapshot(); Close(); };
                once.Start();
            }
        }

        // 時計 (秒表示) が出ていて計測間隔が 1 秒より長いときだけ、秒の変わり目にも再描画する。それ以外は計測のたびに描くだけ
        void ScheduleNextTick()
        {
            if (cfg.IntervalMs <= 1000 || !ClockShown() || sampler.Paused)
            {
                timer.Stop();
                return;
            }
            timer.Interval = TimeSpan.FromMilliseconds(1010 - DateTime.Now.Millisecond);
            timer.Start();
        }

        bool ClockShown()
        {
            if (cfg.Layout != null && !hiddenByFloat)
                foreach (List<string> col in cfg.Layout) if (col.Contains("clock")) return true;
            foreach (PanelWindow f in floats) if (f.Id == "clock") return true;
            return false;
        }

        void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            // モニターの追加・取り外し・解像度変更の直後は配置が落ち着くまで少し待つ
            displayChangeTimer.Stop();
            displayChangeTimer.Start();
        }

        // ---------------------------------------------------------------- window placement (per monitor)

        internal static WF.Screen FindScreen(string device)
        {
            WF.Screen[] all = WF.Screen.AllScreens;
            if (device != null)
                foreach (WF.Screen s in all)
                    if (string.Equals(s.DeviceName, device, StringComparison.OrdinalIgnoreCase)) return s;
            return WF.Screen.PrimaryScreen ?? all[0];
        }

        // 設定 (モニター + 端からの距離) からウィンドウの位置を決める
        void RestorePosition()
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                MigrateLegacyPosition();
                WF.Screen scr = FindScreen(cfg.Monitor);
                System.Drawing.Rectangle wa = scr.WorkingArea;
                double scale = Native.DpiScale(scr.Bounds);
                double dipW = ActualWidth > 0 ? ActualWidth : MeterView.WidthFor(cfg) * cfg.Scale;
                double dipH = ActualHeight > 0 ? ActualHeight : 900 * cfg.Scale;
                int w = (int)Math.Round(dipW * scale), h = (int)Math.Round(dipH * scale);

                int x = cfg.AnchorH == "left" ? wa.Left + cfg.MonX : wa.Right - cfg.MonX - w;
                int y = cfg.AnchorV == "bottom" ? wa.Bottom - cfg.MonY - h : wa.Top + cfg.MonY;
                // 全体が作業領域に収まるように (別のモニターへ退避したときなど)。窓のほうが大きければ左上を合わせる
                x = ClampToArea(x, w, wa.Left, wa.Right);
                y = ClampToArea(y, h, wa.Top, wa.Bottom);
                Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                // 配置編集で「ここから下は画面外」と示すための、窓の上端から作業領域の下端までの高さ (倍率を掛ける前の値)
                view.ScreenLimitH = (wa.Bottom - y) / scale / Math.Max(0.5, cfg.Scale);
            }
            catch (Exception ex) { Log.Write("restore position", ex); }
        }

        // 位置 pos・長さ len の窓を [lo, hi) に収める。収まらない大きさなら lo に合わせる
        public static int ClampToArea(int pos, int len, int lo, int hi)
        {
            if (len >= hi - lo) return lo;
            return Math.Max(lo, Math.Min(hi - len, pos));
        }

        // いまの位置を「どのモニターの、どの端から何px」として保存する。端に近ければ吸着させる
        void SavePosition()
        {
            if (snapshotPath != null || hwnd == IntPtr.Zero) return;
            try
            {
                Native.RECT r;
                Native.GetWindowRect(hwnd, out r);
                WF.Screen scr = WF.Screen.FromHandle(hwnd);
                System.Drawing.Rectangle wa = scr.WorkingArea;
                int dl = r.Left - wa.Left, dr = wa.Right - r.Right, dt = r.Top - wa.Top, db = wa.Bottom - r.Bottom;
                cfg.Monitor = scr.DeviceName;
                cfg.AnchorH = dl <= dr ? "left" : "right";
                cfg.AnchorV = dt <= db ? "top" : "bottom";
                cfg.MonX = Math.Min(dl, dr);
                cfg.MonY = Math.Min(dt, db);
                if (cfg.MonX < SnapDistance) cfg.MonX = EdgeMargin;
                if (cfg.MonY < SnapDistance) cfg.MonY = EdgeMargin;
                cfg.Left = null;
                cfg.Top = null;
                cfg.Save();
            }
            catch (Exception ex) { Log.Write("save position", ex); }
        }

        // 旧形式 (仮想スクリーン座標の Left/Top) を、モニター基準の位置に変換する
        void MigrateLegacyPosition()
        {
            if (cfg.Monitor != null || !cfg.Left.HasValue || !cfg.Top.HasValue) return;
            var pt = new System.Drawing.Point((int)cfg.Left.Value + 40, (int)cfg.Top.Value + 40);
            WF.Screen scr = WF.Screen.FromPoint(pt);
            System.Drawing.Rectangle wa = scr.WorkingArea;
            int w = (int)Math.Round(MeterView.WidthFor(cfg) * cfg.Scale * Native.DpiScale(scr.Bounds));
            int dl = (int)cfg.Left.Value - wa.Left, dr = wa.Right - ((int)cfg.Left.Value + w);
            cfg.Monitor = scr.DeviceName;
            cfg.AnchorH = dl <= dr ? "left" : "right";   // 近いほうの端を基準にする
            cfg.AnchorV = "top";
            cfg.MonX = Math.Max(0, Math.Min(dl, dr));
            cfg.MonY = Math.Max(0, (int)cfg.Top.Value - wa.Top);
            cfg.Left = null;
            cfg.Top = null;
            if (snapshotPath == null) cfg.Save();
        }

        void MoveToMonitor(string device)
        {
            cfg.Monitor = device;
            cfg.AnchorH = "right";
            cfg.AnchorV = "top";
            cfg.MonX = EdgeMargin;
            cfg.MonY = EdgeMargin;
            cfg.Left = null;
            cfg.Top = null;
            RestorePosition();
            cfg.Save();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            hwnd = new WindowInteropHelper(this).Handle;
            long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
            Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex | Native.WS_EX_TOOLWINDOW));   // Alt+Tab に出さない
            HwndSource.FromHwnd(hwnd).AddHook(WndProc);

            winEventProc = OnForegroundChanged;
            winEventHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, winEventProc, 0, 0, 0);

            RestorePosition();
            ApplyMode();
            ApplyClickThrough();
            ApplyHotkey();
        }

        IntPtr WndProc(IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            {
                ToggleVisible();
                handled = true;
                return IntPtr.Zero;
            }
            // 「デスクトップに固定」中は常に Z オーダーの最背面に留める
            if (msg == Native.WM_WINDOWPOSCHANGING && cfg.Mode == "desktop" && !showDesktop)
            {
                var wp = (Native.WINDOWPOS)Marshal.PtrToStructure(lParam, typeof(Native.WINDOWPOS));
                if ((wp.flags & Native.SWP_NOZORDER) == 0)
                {
                    wp.hwndInsertAfter = Native.HWND_BOTTOM;
                    Marshal.StructureToPtr(wp, lParam, false);
                }
            }
            return IntPtr.Zero;
        }

        // 「デスクトップを表示」(Win+D) ではデスクトップ自体が全ウィンドウの手前に来るため、
        // その間だけ一時的に最前面にして隠れないようにする。
        void OnForegroundChanged(IntPtr hook, uint evt, IntPtr fg, int idObject, int idChild, uint thread, uint time)
        {
            try
            {
                if (cfg.Mode != "desktop" || hwnd == IntPtr.Zero || IsOurWindow(fg)) return;
                string cls = Native.ClassOf(fg);
                if (!showDesktop)
                {
                    if (cls == "WorkerW" && IsBelow(fg))
                    {
                        showDesktop = true;
                        foreach (IntPtr wh in AllHwnds()) Native.SetWindowPos(wh, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                    }
                }
                else if (cls != "WorkerW" && cls != "Progman" && cls != "Shell_TrayWnd" && !cls.StartsWith("WindowsForms", StringComparison.Ordinal))
                {
                    showDesktop = false;
                    foreach (IntPtr wh in AllHwnds())
                    {
                        Native.SetWindowPos(wh, Native.HWND_NOTOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                        Native.SetWindowPos(wh, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                    }
                }
            }
            catch (Exception ex) { Log.Write("foreground", ex); }
        }

        // 自分のウィンドウが other より Z オーダーで下にあるか
        bool IsBelow(IntPtr other)
        {
            IntPtr h = other;
            for (int i = 0; i < 2000 && h != IntPtr.Zero; i++)
            {
                h = Native.GetWindow(h, Native.GW_HWNDNEXT);
                if (h == hwnd) return true;
            }
            return false;
        }

        void ApplyMode()
        {
            foreach (PanelWindow f in floats) f.ApplyMode();
            showDesktop = false;
            Topmost = cfg.Mode == "topmost";
            if (hwnd == IntPtr.Zero) return;
            if (cfg.Mode != "topmost")
            {
                // Win+D 対策で一時的に最前面にした WS_EX_TOPMOST が残らないよう、必ず解除してから
                Native.SetWindowPos(hwnd, Native.HWND_NOTOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                if (cfg.Mode == "desktop")
                    Native.SetWindowPos(hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }
        }

        // クリック透過は配置編集中には一時的に外す (そうしないとパネルをつかめない)
        void ApplyClickThrough()
        {
            foreach (PanelWindow f in floats) f.ApplyClickThrough();
            if (hwnd == IntPtr.Zero) return;
            long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
            bool through = cfg.ClickThrough && snapshotPath == null && !view.EditMode;
            ex = through ? ex | Native.WS_EX_TRANSPARENT : ex & ~Native.WS_EX_TRANSPARENT;
            Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
        }

        void SetEditMode(bool on)
        {
            view.EditMode = on;
            ApplyClickThrough();
        }

        // ---------------------------------------------------------------- menu / tray

        WF.ToolStripMenuItem Item(string text, Action onClick)
        {
            var mi = new WF.ToolStripMenuItem(text);
            mi.Click += delegate { onClick(); };
            return mi;
        }

        // 「スキン」の中身: 組み込み、区切り線、skins\*.json の順
        // skins\*.json に書き間違いがあれば (色・キー名・候補に無い値・無いフォント)、選んだときに知らせる
        void WarnSkinProblems()
        {
            try
            {
                Skin s = Skin.Get(cfg.Skin);
                if (s.Warnings > 0 && tray != null)
                    tray.ShowBalloonTip(6000, AppName, "スキン「" + cfg.Skin + "」に " + s.Warnings + " 件の問題があります (その項目は既定の値で表示)。deskmeter.log に内容があります。", WF.ToolTipIcon.Warning);
            }
            catch (Exception ex) { Log.Write("skin warn", ex); }
        }

        // settings.json の一部の項目が読めず既定値にしたときに知らせる
        void WarnSettingsProblems()
        {
            if (Settings.LastLoadWarnings.Count == 0 || tray == null) return;
            tray.ShowBalloonTip(8000, AppName, "settings.json の " + string.Join(", ", Settings.LastLoadWarnings.ToArray()) + " が読めなかったので既定値にしました (ほかの設定はそのまま)。", WF.ToolTipIcon.Warning);
        }

        void FillSkinMenu()
        {
            miSkin.DropDownItems.Clear();
            foreach (string name in Skin.BuiltIn) miSkin.DropDownItems.Add(SkinItem(name));
            string[] files = Skin.FileNames();
            if (files.Length > 0)
            {
                miSkin.DropDownItems.Add(new WF.ToolStripSeparator());
                foreach (string name in files) miSkin.DropDownItems.Add(SkinItem(name));
            }
        }

        WF.ToolStripMenuItem SkinItem(string name)
        {
            string n = name;
            var mi = Item(Skin.DisplayName(n), delegate { cfg.Skin = n; ApplySkinAll(); cfg.Save(); WarnSkinProblems(); });
            mi.Tag = n;
            return mi;
        }

        void BuildMenu()
        {
            menu = new WF.ContextMenuStrip();

            miSkin = new WF.ToolStripMenuItem("スキン");
            FillSkinMenu();
            menu.Items.Add(miSkin);

            var mode = new WF.ToolStripMenuItem("表示モード");
            miDesktop = Item("デスクトップに固定 (他のウィンドウの背面)", delegate { SetMode("desktop"); });
            miTopmost = Item("常に最前面", delegate { SetMode("topmost"); });
            miNormal = Item("通常のウィンドウ", delegate { SetMode("normal"); });
            mode.DropDownItems.Add(miDesktop);
            mode.DropDownItems.Add(miTopmost);
            mode.DropDownItems.Add(miNormal);
            menu.Items.Add(mode);

            miMonitor = new WF.ToolStripMenuItem("モニターへ移動");
            menu.Items.Add(miMonitor);

            miScale = new WF.ToolStripMenuItem("サイズ");
            foreach (int p in new[] { 70, 80, 90, 100, 110, 125, 150 })
            {
                int pct = p;
                var mi = Item(pct + "%", delegate { cfg.Scale = pct / 100.0; ApplyScaleAll(); cfg.Save(); });
                mi.Tag = pct;
                miScale.DropDownItems.Add(mi);
            }
            menu.Items.Add(miScale);

            miOpacity = new WF.ToolStripMenuItem("背景の不透明度");
            foreach (int p in new[] { 100, 90, 80, 70, 60, 50, 30, 0 })
            {
                int pct = p;
                var mi = Item(pct + "%", delegate { cfg.BackgroundOpacity = pct / 100.0; InvalidateAll(); cfg.Save(); });
                mi.Tag = pct;
                miOpacity.DropDownItems.Add(mi);
            }
            menu.Items.Add(miOpacity);

            miShadow = new WF.ToolStripMenuItem("文字の影・縁取り");
            foreach (string[] opt in new[] { new[] { "auto", "自動 (背景が薄いとき)" }, new[] { "on", "常に" }, new[] { "off", "なし" } })
            {
                string v = opt[0];
                var mi = Item(opt[1], delegate { cfg.TextShadow = v; InvalidateAll(); cfg.Save(); });
                mi.Tag = v;
                miShadow.DropDownItems.Add(mi);
            }
            menu.Items.Add(miShadow);

            miImages = new WF.ToolStripMenuItem("画像");
            miImages.DropDownItems.Add(Item("マスコット画像を選ぶ...", delegate { PickImage(true); }));
            miImages.DropDownItems.Add(Item("マスコット画像を外す", delegate { cfg.MascotImage = ""; RelayoutAll(); cfg.Save(); }));
            miMascotPlace = new WF.ToolStripMenuItem("マスコット画像の位置");
            foreach (string[] opt in new[] { new[] { "free", "空きスペースに収める" }, new[] { "right", "右に並べる (立ち絵向け)" }, new[] { "left", "左に並べる (立ち絵向け)" } })
            {
                string v = opt[0];
                var mi = Item(opt[1], delegate { cfg.MascotPlacement = v; RelayoutAll(); cfg.Save(); });
                mi.Tag = v;
                miMascotPlace.DropDownItems.Add(mi);
            }
            miImages.DropDownItems.Add(miMascotPlace);
            miMascotSize = new WF.ToolStripMenuItem("マスコット画像の大きさ");
            foreach (int px in new[] { 120, 160, 220, 320, 480, 720 })
            {
                int v = px;
                var mi = Item(v + " px", delegate { cfg.MascotSize = v; RelayoutAll(); cfg.Save(); });
                mi.Tag = v;
                miMascotSize.DropDownItems.Add(mi);
            }
            miImages.DropDownItems.Add(miMascotSize);
            miImages.DropDownItems.Add(new WF.ToolStripSeparator());
            miImages.DropDownItems.Add(Item("背景画像を選ぶ...", delegate { PickImage(false); }));
            miImages.DropDownItems.Add(Item("背景画像を外す", delegate { cfg.BackgroundImage = ""; InvalidateAll(); cfg.Save(); }));
            miBgImageOpacity = new WF.ToolStripMenuItem("背景画像の濃さ");
            foreach (int p in new[] { 20, 35, 50, 75, 100 })
            {
                int pct = p;
                var mi = Item(pct + "%", delegate { cfg.BackgroundImageOpacity = pct / 100.0; InvalidateAll(); cfg.Save(); });
                mi.Tag = pct;
                miBgImageOpacity.DropDownItems.Add(mi);
            }
            miImages.DropDownItems.Add(miBgImageOpacity);
            menu.Items.Add(miImages);

            miInterval = new WF.ToolStripMenuItem("更新間隔");
            foreach (int ms in new[] { 250, 500, 1000, 2000, 3000, 5000, 10000 })
            {
                int v = ms;
                var mi = Item((v / 1000.0).ToString("0.##") + " 秒", delegate { cfg.IntervalMs = v; ScheduleNextTick(); sampler.Poke(); cfg.Save(); });
                mi.Tag = v;
                miInterval.DropDownItems.Add(mi);
            }
            menu.Items.Add(miInterval);

            miHistory = new WF.ToolStripMenuItem("グラフの時間幅");
            foreach (int s in new[] { 30, 60, 120, 300, 600, 1800 })
            {
                int v = s;
                var mi = Item(MeterView.SpanText(v), delegate { cfg.HistorySeconds = v; InvalidateAll(); cfg.Save(); });
                mi.Tag = v;
                miHistory.DropDownItems.Add(mi);
            }
            menu.Items.Add(miHistory);

            miPanels = new WF.ToolStripMenuItem("表示する項目");
            foreach (string id in Settings.PanelIds)
            {
                string pid = id;
                var mi = Item(Settings.PanelName(pid), delegate { cfg.TogglePanel(pid); RelayoutAll(); cfg.Save(); });
                mi.Tag = pid;
                miPanels.DropDownItems.Add(mi);
            }
            miPanels.DropDownItems.Add(new WF.ToolStripSeparator());
            AddToggle(miPanels, "コアを2列で表示 (高さを節約)", "CompactCores");
            menu.Items.Add(miPanels);

            miEdit = Item("配置の編集 (ドラッグで並べ替え)", delegate { SetEditMode(!view.EditMode); });
            menu.Items.Add(miEdit);
            BuildLayoutMenus();

            menu.Items.Add(new WF.ToolStripSeparator());
            miLock = Item("位置を固定", delegate { cfg.Locked = !cfg.Locked; cfg.Save(); });
            miClick = Item("クリックを透過 (解除はトレイアイコンから)", delegate { cfg.ClickThrough = !cfg.ClickThrough; ApplyClickThrough(); cfg.Save(); });
            menu.Items.Add(miLock);
            menu.Items.Add(miClick);
            miVisible = Item("ウィジェットを隠す", ToggleVisible);
            menu.Items.Add(miVisible);
            miHotkey = new WF.ToolStripMenuItem("ホットキー (表示/非表示)");
            foreach (string k in new[] { "", "Ctrl+Alt+D", "Ctrl+Shift+D", "Win+Shift+D" })
            {
                string v = k;
                var mi = Item(v.Length == 0 ? "無効" : v, delegate { cfg.Hotkey = v; ApplyHotkey(); cfg.Save(); });
                mi.Tag = v;
                miHotkey.DropDownItems.Add(mi);
            }
            menu.Items.Add(miHotkey);

            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add(Item("天気を今すぐ更新", delegate { sampler.RefreshWeatherNow(); }));
            menu.Items.Add(Item("天気の場所を設定...", ShowLocationDialog));
            menu.Items.Add(Item("Peak と起動後の通信量をリセット", delegate
            {
                lock (sampler.S) { sampler.S.PeakBps = 0; sampler.S.SessUp = 0; sampler.S.SessDown = 0; }
                InvalidateAll();
            }));
            miAuto = Item("Windows 起動時に開始", ToggleAutoStart);
            menu.Items.Add(miAuto);
            menu.Items.Add(Item("設定ファイルを開く (settings.json)", OpenSettings));
            menu.Items.Add(Item("設定ファイルを再読み込み", ReloadSettings));
            var help = new WF.ToolStripMenuItem("ヘルプ");
            help.DropDownItems.Add(Item("天気の場所を設定する (地名検索 / 緯度・経度)", delegate { OpenHelp("weather-location.html"); }));
            help.DropDownItems.Add(Item("スキンの作り方 (skins フォルダー)", delegate { OpenHelp("custom-skin.html"); }));
            help.DropDownItems.Add(Item("CPU 温度・電力・ファンを表示する (HWiNFO 連携)", delegate { OpenHelp("hwinfo.html"); }));
            help.DropDownItems.Add(Item("ヘルプのフォルダーを開く", delegate { OpenHelp(null); }));
            menu.Items.Add(help);
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add(Item("終了", Close));

            menu.Opening += delegate
            {
                UpdateLayoutMenus();
                miVisible.Text = IsVisible ? "ウィジェットを隠す" : "ウィジェットを表示";
                foreach (WF.ToolStripItem it in miPanels.DropDownItems)
                {
                    var mi = it as WF.ToolStripMenuItem;
                    if (mi == null) continue;
                    if (mi.Tag is FieldInfo) mi.Checked = (bool)((FieldInfo)mi.Tag).GetValue(cfg);
                    else if (mi.Tag is string) mi.Checked = cfg.HasPanel((string)mi.Tag);
                }
                miEdit.Checked = view.EditMode;
                foreach (WF.ToolStripMenuItem mi in miShadow.DropDownItems) mi.Checked = (string)mi.Tag == cfg.TextShadow;
                foreach (WF.ToolStripMenuItem mi in miMascotPlace.DropDownItems) mi.Checked = (string)mi.Tag == cfg.MascotPlacement;
                foreach (WF.ToolStripMenuItem mi in miMascotSize.DropDownItems) mi.Checked = (int)mi.Tag == cfg.MascotSize;
                foreach (WF.ToolStripMenuItem mi in miBgImageOpacity.DropDownItems) mi.Checked = (int)mi.Tag == (int)Math.Round(cfg.BackgroundImageOpacity * 100);
                FillSkinMenu();   // skins フォルダーを見直す (追加・削除をメニューに反映)
                foreach (WF.ToolStripItem it in miSkin.DropDownItems)
                {
                    var mi = it as WF.ToolStripMenuItem;
                    if (mi != null) mi.Checked = (string)mi.Tag == cfg.Skin;
                }
                foreach (WF.ToolStripMenuItem mi in miInterval.DropDownItems) mi.Checked = (int)mi.Tag == cfg.IntervalMs;
                foreach (WF.ToolStripMenuItem mi in miHistory.DropDownItems) mi.Checked = (int)mi.Tag == cfg.HistorySeconds;
                foreach (WF.ToolStripMenuItem mi in miHotkey.DropDownItems) mi.Checked = string.Equals((string)mi.Tag, cfg.Hotkey, StringComparison.OrdinalIgnoreCase);
                miHotkey.Text = !string.IsNullOrEmpty(cfg.Hotkey) && !hotkeyRegistered ? "ホットキー (表示/非表示) - 登録できません" : "ホットキー (表示/非表示)";
                miDesktop.Checked = cfg.Mode == "desktop";
                miTopmost.Checked = cfg.Mode == "topmost";
                miNormal.Checked = cfg.Mode == "normal";
                miLock.Checked = cfg.Locked;
                miClick.Checked = cfg.ClickThrough;
                miAuto.Checked = IsAutoStart();
                foreach (WF.ToolStripMenuItem mi in miScale.DropDownItems) mi.Checked = (int)mi.Tag == (int)Math.Round(cfg.Scale * 100);
                foreach (WF.ToolStripMenuItem mi in miOpacity.DropDownItems) mi.Checked = (int)mi.Tag == (int)Math.Round(cfg.BackgroundOpacity * 100);
                BuildMonitorMenu();
            };
        }

        // モニターは抜き差しされるので、メニューを開くたびに作り直す
        void BuildMonitorMenu()
        {
            miMonitor.DropDownItems.Clear();
            WF.Screen[] all = WF.Screen.AllScreens;
            WF.Screen current = hwnd != IntPtr.Zero ? WF.Screen.FromHandle(hwnd) : null;
            for (int i = 0; i < all.Length; i++)
            {
                WF.Screen s = all[i];
                string dev = s.DeviceName;
                string text = string.Format("モニター {0}  ({1}x{2}{3})", i + 1, s.Bounds.Width, s.Bounds.Height, s.Primary ? ", メイン" : "");
                var mi = Item(text, delegate { MoveToMonitor(dev); });
                mi.Checked = current != null && current.DeviceName == dev;
                miMonitor.DropDownItems.Add(mi);
            }
        }

        // Settings の bool フィールドを切り替えるチェック項目
        void AddToggle(WF.ToolStripMenuItem parent, string text, string field)
        {
            FieldInfo f = typeof(Settings).GetField(field);
            var mi = new WF.ToolStripMenuItem(text);
            mi.Tag = f;
            mi.Click += delegate
            {
                f.SetValue(cfg, !(bool)f.GetValue(cfg));
                RelayoutAll();
                cfg.Save();
            };
            parent.DropDownItems.Add(mi);
        }

        // 隠している間は再描画も計測も止める (省電力)
        // ToggleVisible と UpdatePaused は MainWindowFloating.cs

        void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock) sessionLocked = true;
            else if (e.Reason == SessionSwitchReason.SessionUnlock) sessionLocked = false;
            else return;
            UpdatePaused();
            if (!sessionLocked) sampler.Poke();
        }

        void ReloadSettings()
        {
            if (!cfg.Reload())
            {
                // 壊れたファイルは読まずに現在の設定のまま続ける (Save で初期化しないため)
                if (tray != null) tray.ShowBalloonTip(5000, AppName, "settings.json を読み込めませんでした (JSON の構文エラー)。設定は変更していません。", WF.ToolTipIcon.Warning);
                return;
            }
            ApplyScaleAll();
            ApplySkinAll();
            RelayoutAll();
            ScheduleNextTick();
            sampler.Poke();
            RestorePosition();
            foreach (PanelWindow f in floats) f.RestorePosition();   // FloatPositions の手編集も反映する
            ApplyMode();
            ApplyClickThrough();
            ApplyHotkey();
            sampler.RefreshWeatherNow();
            WarnSettingsProblems();
            WarnSkinProblems();
        }

        // 背景・マスコット画像が読めなかったとき (形式が未対応、ファイルが無い) に知らせる。同じパスは 1 回だけ
        string lastImageFailure;
        void OnImageFailed(string path, string reason)
        {
            if (path == lastImageFailure || tray == null) return;
            lastImageFailure = path;
            tray.ShowBalloonTip(6000, AppName, "画像を読み込めません: " + Path.GetFileName(path) + "\n" + reason, WF.ToolTipIcon.Warning);
        }

        // ---------------------------------------------------------------- hotkey / second instance / panel double-click

        // Settings.Hotkey を RegisterHotKey で登録し直す。他のアプリと競合して登録できなければログに書いて無視
        void ApplyHotkey()
        {
            if (hwnd == IntPtr.Zero || snapshotPath != null) return;
            if (hotkeyRegistered) { Native.UnregisterHotKey(hwnd, HotkeyId); hotkeyRegistered = false; }
            uint mods, vk;
            if (!ParseHotkey(cfg.Hotkey, out mods, out vk)) return;
            if (Native.RegisterHotKey(hwnd, HotkeyId, mods | Native.MOD_NOREPEAT, vk)) hotkeyRegistered = true;
            else
            {
                Log.Write("hotkey " + cfg.Hotkey, new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
                if (tray != null) tray.ShowBalloonTip(5000, AppName, "ホットキー " + cfg.Hotkey + " は他のアプリが使っているため登録できませんでした。", WF.ToolTipIcon.Warning);
            }
        }

        // settings.json がメモ帳などで書き換えられたら、自動で読み直す (再読み込みを忘れて上書きしてしまわないように)
        FileSystemWatcher settingsWatcher;
        readonly DispatcherTimer settingsChangedTimer = new DispatcherTimer();

        void StartSettingsWatcher()
        {
            try
            {
                settingsChangedTimer.Interval = TimeSpan.FromMilliseconds(700);
                settingsChangedTimer.Tick += delegate
                {
                    settingsChangedTimer.Stop();
                    if (!Settings.ExternallyModified()) return;   // 自分の Save によるもの
                    ReloadSettings();
                    if (tray != null) tray.ShowBalloonTip(3000, AppName, "settings.json の変更を反映しました。", WF.ToolTipIcon.Info);
                };
                settingsWatcher = new FileSystemWatcher(AppPaths.Dir, "settings.json");
                settingsWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName;
                FileSystemEventHandler onChange = delegate
                {
                    Dispatcher.BeginInvoke(new Action(delegate { settingsChangedTimer.Stop(); settingsChangedTimer.Start(); }));
                };
                settingsWatcher.Changed += onChange;
                settingsWatcher.Created += onChange;
                settingsWatcher.Renamed += delegate { onChange(null, null); };
                settingsWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) { Log.Write("settings watcher", ex); }
        }

        // "Ctrl+Alt+D" のような表記を修飾キーと仮想キーコードに分ける
        static bool ParseHotkey(string s, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            foreach (string raw in s.Split('+'))
            {
                string p = raw.Trim();
                if (p.Length == 0) continue;
                switch (p.ToLowerInvariant())
                {
                    case "ctrl": case "control": mods |= Native.MOD_CONTROL; continue;
                    case "alt": mods |= Native.MOD_ALT; continue;
                    case "shift": mods |= Native.MOD_SHIFT; continue;
                    case "win": case "windows": mods |= Native.MOD_WIN; continue;
                }
                Key key;
                string name = p.Length == 1 && char.IsDigit(p[0]) ? "D" + p : p;   // "1" → Key.D1
                if (!Enum.TryParse<Key>(name, true, out key)) return false;
                vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            }
            return vk != 0;
        }

        // 2 つ目のプロセスから: 起動中のインスタンスに表示を頼む
        public static void SignalShow()
        {
            try
            {
                using (var ev = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName)) ev.Set();
            }
            catch (Exception ex) { Log.Write("signal show", ex); }
        }

        // 1 つ目のプロセスで: 名前付きイベントを待ち、セットされたら表示する
        void StartShowListener()
        {
            try
            {
                showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
                editEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EditEventName);
                reloadEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ReloadEventName);
                var t = new Thread(delegate()
                {
                    WaitHandle[] hs = { showEvent, editEvent, reloadEvent, showStop };
                    while (true)
                    {
                        int i = WaitHandle.WaitAny(hs);
                        if (i == 0) Dispatcher.BeginInvoke(new Action(delegate { if (userHidden) ToggleVisible(); else if (!hiddenByFloat) { Show(); ApplyMode(); } }));
                        else if (i == 1) Dispatcher.BeginInvoke(new Action(delegate { SetEditMode(!view.EditMode); }));
                        else if (i == 2) Dispatcher.BeginInvoke(new Action(ReloadSettings));
                        else break;
                    }
                });
                t.IsBackground = true;
                t.Name = "DeskMeter show listener";
                t.Start();
            }
            catch (Exception ex) { Log.Write("show listener", ex); }
        }

        // パネルのダブルクリックで関連するアプリを開く
        void OpenPanelApp(string id, string arg)
        {
            try
            {
                switch (id)
                {
                    case "cpu": case "topcpu": case "topmem": case "gpu":
                        Process.Start("taskmgr.exe");
                        break;
                    case "drives":
                        if (arg != null) Process.Start("explorer.exe", arg + "\\");
                        else Process.Start("explorer.exe");
                        break;
                    case "network":
                        Process.Start("control.exe", "ncpa.cpl");
                        break;
                    case "weather":
                        Process.Start(string.Format(System.Globalization.CultureInfo.InvariantCulture, "https://www.windy.com/{0:0.###}/{1:0.###}", cfg.Latitude, cfg.Longitude));
                        break;
                    case "clock":
                        StartFirst("ms-clock:", "outlookcal:");
                        break;
                    case "calendar": case "datetile":
                        StartFirst("outlookcal:", "ms-clock:");
                        break;
                }
            }
            catch (Exception ex) { Log.Write("open " + id, ex); }
        }

        // 先に開けたものを使う (プロトコルが登録されていなければ次へ)。全部失敗しても何もしない
        static void StartFirst(params string[] uris)
        {
            foreach (string u in uris)
            {
                try { Process.Start(u); return; }
                catch { }
            }
        }

        void BuildTray()
        {
            tray = new WF.NotifyIcon();
            tray.Text = AppName + " (ダブルクリックで表示/非表示)";
            tray.Icon = MakeIcon();
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { ToggleVisible(); };
            tray.Visible = true;
        }

        // exe を別の場所へ移したあとも自動起動が効くよう、登録済みのパスを現在の場所に合わせる
        static void SyncAutoStartPath()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    object v = k.GetValue(AppName);
                    string want = LaunchCommand;
                    if (v != null && !string.Equals(Convert.ToString(v), want, StringComparison.OrdinalIgnoreCase)) k.SetValue(AppName, want);
                }
            }
            catch (Exception ex) { Log.Write("autostart sync", ex); }
        }

        static System.Drawing.Icon MakeIcon()
        {
            using (var bmp = new System.Drawing.Bitmap(32, 32))
            {
                using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bmp))
                {
                    g.Clear(System.Drawing.Color.Transparent);
                    using (var bg = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(20, 22, 26)))
                        g.FillRectangle(bg, 1, 1, 30, 30);
                    int[] heights = { 10, 20, 14, 24 };
                    System.Drawing.Color[] colors =
                    {
                        System.Drawing.Color.FromArgb(126, 217, 87), System.Drawing.Color.FromArgb(245, 225, 74),
                        System.Drawing.Color.FromArgb(255, 166, 48), System.Drawing.Color.FromArgb(92, 200, 255)
                    };
                    for (int i = 0; i < 4; i++)
                        using (var b = new System.Drawing.SolidBrush(colors[i]))
                            g.FillRectangle(b, 4 + i * 6.5f, 28 - heights[i], 5, heights[i]);
                }
                IntPtr h = bmp.GetHicon();
                try { return (System.Drawing.Icon)System.Drawing.Icon.FromHandle(h).Clone(); }
                finally { Native.DestroyIcon(h); }
            }
        }

        void ShowMenu()
        {
            if (menu == null) return;
            Native.SetForegroundWindow(hwnd);
            menu.Show(WF.Cursor.Position);
        }

        void SetMode(string mode)
        {
            cfg.Mode = mode;
            ApplyMode();
            cfg.Save();
        }

        // 自動起動に登録するコマンド。スクリプト起動のときは PowerShell 経由で起動する
        static string LaunchCommand
        {
            get
            {
                if (AppPaths.ScriptHosted)
                    return "\"" + Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe") + "\" -STA -WindowStyle Hidden -ExecutionPolicy Bypass -File \"" + Path.Combine(AppPaths.Dir, "DeskMeter.ps1") + "\"";
                return "\"" + Assembly.GetExecutingAssembly().Location + "\"";
            }
        }

        static bool IsAutoStart()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(AppName) != null;
            }
            catch { return false; }
        }

        void ToggleAutoStart()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (k.GetValue(AppName) != null) k.DeleteValue(AppName, false);
                    else k.SetValue(AppName, LaunchCommand);
                }
            }
            catch (Exception ex) { Log.Write("autostart", ex); }
        }

        // help フォルダーの HTML を既定のブラウザーで開く (null ならフォルダー自体)
        // 画像ファイルを選んで設定に入れる (mascot = true ならマスコット、false なら背景)
        void PickImage(bool mascot)
        {
            try
            {
                using (var dlg = new WF.OpenFileDialog())
                {
                    dlg.Title = mascot ? "マスコット画像を選ぶ" : "背景画像を選ぶ";
                    dlg.Filter = "画像 (*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|すべてのファイル (*.*)|*.*";
                    dlg.CheckFileExists = true;
                    if (dlg.ShowDialog() != WF.DialogResult.OK) return;
                    if (mascot) cfg.MascotImage = dlg.FileName; else cfg.BackgroundImage = dlg.FileName;
                    RelayoutAll();
                    cfg.Save();
                }
            }
            catch (Exception ex) { Log.Write("pick image", ex); }
        }

        void OpenHelp(string file)
        {
            try
            {
                string dir = Path.Combine(AppPaths.Dir, "help");
                Process.Start(file == null ? dir : Path.Combine(dir, file));
            }
            catch (Exception ex) { Log.Write("open help", ex); }
        }

        // 天気の場所 (緯度・経度・表示名) を設定するダイアログ
        void ShowLocationDialog()
        {
            try
            {
                var dlg = new LocationDialog(cfg);
                if (dlg.ShowDialog() != true) return;
                cfg.Save();
                sampler.RefreshWeatherNow();
                InvalidateAll();
            }
            catch (Exception ex) { Log.Write("location dialog", ex); }
        }

        void OpenSettings()
        {
            try
            {
                if (!File.Exists(Settings.FilePath)) cfg.Save();
                Process.Start("notepad.exe", "\"" + Settings.FilePath + "\"");
            }
            catch (Exception ex) { Log.Write("open settings", ex); }
        }

        // ---------------------------------------------------------------- lifecycle

        void SaveSnapshot()
        {
            try
            {
                double s = cfg.Scale;
                int w = (int)Math.Ceiling(view.ActualWidth * s), h = (int)Math.Ceiling(view.ActualHeight * s);
                var dv = new DrawingVisual();
                using (DrawingContext dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, w, h));
                    dc.PushTransform(new ScaleTransform(s, s));
                    dc.DrawRectangle(new VisualBrush(view), null, new Rect(0, 0, view.ActualWidth, view.ActualHeight));
                    dc.Pop();
                }
                var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using (FileStream fs = File.Create(snapshotPath)) enc.Save(fs);
                SaveFloatSnapshots();
            }
            catch (Exception ex) { Log.Write("snapshot", ex); }
        }

        void Cleanup()
        {
            timer.Stop();
            foreach (PanelWindow f in floats) { try { f.Close(); } catch { } }
            floats.Clear();
            displayChangeTimer.Stop();
            if (snapshotPath == null)
            {
                SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
                SystemEvents.SessionSwitch -= OnSessionSwitch;
            }
            if (winEventHook != IntPtr.Zero) Native.UnhookWinEvent(winEventHook);
            if (hotkeyRegistered) { Native.UnregisterHotKey(hwnd, HotkeyId); hotkeyRegistered = false; }
            showStop.Set();
            if (showEvent != null) { showEvent.Close(); showEvent = null; }
            if (editEvent != null) { editEvent.Close(); editEvent = null; }
            if (reloadEvent != null) { reloadEvent.Close(); reloadEvent = null; }
            if (settingsWatcher != null) { try { settingsWatcher.EnableRaisingEvents = false; settingsWatcher.Dispose(); } catch { } settingsWatcher = null; }
            if (tray != null)
            {
                tray.Visible = false;
                tray.Dispose();
            }
            sampler.Dispose();
            // 終了直前にファイルが外部で書き換えられていたら (読み直す前なら)、その編集を上書きしない
            if (snapshotPath == null && !Settings.ExternallyModified()) cfg.Save();
        }
    }
}

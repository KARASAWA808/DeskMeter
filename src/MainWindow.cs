using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
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
                if (!created) return;
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

    sealed class MainWindow : Window
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string AppName = "DeskMeter";
        const int EdgeMargin = 8, SnapDistance = 16;
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
        Native.WinEventProc winEventProc;   // GC されないよう保持
        IntPtr winEventHook;

        WF.NotifyIcon tray;
        WF.ContextMenuStrip menu;
        WF.ToolStripMenuItem miDesktop, miTopmost, miNormal, miLock, miClick, miAuto, miOpacity, miScale, miPanels, miVisible,
            miInterval, miSkin, miMonitor, miEdit;

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
            view.LayoutTransform = new ScaleTransform(cfg.Scale, cfg.Scale);
            Content = view;
            view.LayoutChanged += delegate { cfg.Save(); };

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

            timer.Tick += delegate
            {
                view.InvalidateVisual();
                ScheduleNextTick();
            };
            ScheduleNextTick();

            displayChangeTimer.Interval = TimeSpan.FromMilliseconds(1200);
            displayChangeTimer.Tick += delegate { displayChangeTimer.Stop(); RestorePosition(); };

            sampler.Start();
            timer.Start();

            if (snapshotPath == null)
            {
                BuildMenu();
                BuildTray();
                SyncAutoStartPath();
                SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
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

        // 計測間隔が1秒以上なら秒の変わり目で、短ければその間隔で再描画する
        void ScheduleNextTick()
        {
            int ms = Math.Min(1000, Math.Max(250, cfg.IntervalMs));
            timer.Interval = TimeSpan.FromMilliseconds(ms >= 1000 ? 1010 - DateTime.Now.Millisecond : ms);
        }

        void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            // モニターの追加・取り外し・解像度変更の直後は配置が落ち着くまで少し待つ
            displayChangeTimer.Stop();
            displayChangeTimer.Start();
        }

        // ---------------------------------------------------------------- window placement (per monitor)

        static WF.Screen FindScreen(string device)
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
                // 作業領域からはみ出しすぎないように
                x = Math.Max(wa.Left - w + 80, Math.Min(wa.Right - 80, x));
                y = Math.Max(wa.Top, Math.Min(wa.Bottom - 60, y));
                Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            }
            catch (Exception ex) { Log.Write("restore position", ex); }
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
        }

        IntPtr WndProc(IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
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
                if (cfg.Mode != "desktop" || hwnd == IntPtr.Zero || fg == hwnd) return;
                string cls = Native.ClassOf(fg);
                if (!showDesktop)
                {
                    if (cls == "WorkerW" && IsBelow(fg))
                    {
                        showDesktop = true;
                        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                    }
                }
                else if (cls != "WorkerW" && cls != "Progman" && cls != "Shell_TrayWnd" && !cls.StartsWith("WindowsForms", StringComparison.Ordinal))
                {
                    showDesktop = false;
                    Native.SetWindowPos(hwnd, Native.HWND_NOTOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                    Native.SetWindowPos(hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
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
            showDesktop = false;
            Topmost = cfg.Mode == "topmost";
            if (hwnd == IntPtr.Zero) return;
            if (cfg.Mode == "desktop")
                Native.SetWindowPos(hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        void ApplyClickThrough()
        {
            if (hwnd == IntPtr.Zero) return;
            long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
            ex = cfg.ClickThrough && snapshotPath == null ? ex | Native.WS_EX_TRANSPARENT : ex & ~Native.WS_EX_TRANSPARENT;
            Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
        }

        // ---------------------------------------------------------------- menu / tray

        WF.ToolStripMenuItem Item(string text, Action onClick)
        {
            var mi = new WF.ToolStripMenuItem(text);
            mi.Click += delegate { onClick(); };
            return mi;
        }

        void BuildMenu()
        {
            menu = new WF.ContextMenuStrip();

            miSkin = new WF.ToolStripMenuItem("スキン");
            foreach (string name in Skin.Names)
            {
                string n = name;
                var mi = Item(Skin.DisplayName(n), delegate { cfg.Skin = n; view.ApplySkin(); cfg.Save(); });
                mi.Tag = n;
                miSkin.DropDownItems.Add(mi);
            }
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
                var mi = Item(pct + "%", delegate { cfg.Scale = pct / 100.0; view.LayoutTransform = new ScaleTransform(cfg.Scale, cfg.Scale); cfg.Save(); });
                mi.Tag = pct;
                miScale.DropDownItems.Add(mi);
            }
            menu.Items.Add(miScale);

            miOpacity = new WF.ToolStripMenuItem("背景の不透明度");
            foreach (int p in new[] { 100, 90, 80, 70, 60, 50, 30, 0 })
            {
                int pct = p;
                var mi = Item(pct + "%", delegate { cfg.BackgroundOpacity = pct / 100.0; view.InvalidateVisual(); cfg.Save(); });
                mi.Tag = pct;
                miOpacity.DropDownItems.Add(mi);
            }
            menu.Items.Add(miOpacity);

            miInterval = new WF.ToolStripMenuItem("更新間隔");
            foreach (int ms in new[] { 250, 500, 1000, 2000, 3000, 5000, 10000 })
            {
                int v = ms;
                var mi = Item((v / 1000.0).ToString("0.##") + " 秒", delegate { cfg.IntervalMs = v; ScheduleNextTick(); cfg.Save(); });
                mi.Tag = v;
                miInterval.DropDownItems.Add(mi);
            }
            menu.Items.Add(miInterval);

            miPanels = new WF.ToolStripMenuItem("表示する項目");
            foreach (string id in Settings.PanelIds)
            {
                string pid = id;
                var mi = Item(Settings.PanelName(pid), delegate { cfg.TogglePanel(pid); view.Relayout(); cfg.Save(); });
                mi.Tag = pid;
                miPanels.DropDownItems.Add(mi);
            }
            miPanels.DropDownItems.Add(new WF.ToolStripSeparator());
            AddToggle(miPanels, "コアを2列で表示 (高さを節約)", "CompactCores");
            menu.Items.Add(miPanels);

            miEdit = Item("配置の編集 (ドラッグで並べ替え)", delegate { view.EditMode = !view.EditMode; });
            menu.Items.Add(miEdit);
            menu.Items.Add(Item("配置をリセット (標準の3列)", delegate { cfg.Layout = Settings.DefaultLayout(); view.Relayout(); cfg.Save(); }));

            menu.Items.Add(new WF.ToolStripSeparator());
            miLock = Item("位置を固定", delegate { cfg.Locked = !cfg.Locked; cfg.Save(); });
            miClick = Item("クリックを透過 (解除はトレイアイコンから)", delegate { cfg.ClickThrough = !cfg.ClickThrough; ApplyClickThrough(); cfg.Save(); });
            menu.Items.Add(miLock);
            menu.Items.Add(miClick);
            miVisible = Item("ウィジェットを隠す", ToggleVisible);
            menu.Items.Add(miVisible);

            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add(Item("天気を今すぐ更新", delegate { sampler.RefreshWeatherNow(); }));
            miAuto = Item("Windows 起動時に開始", ToggleAutoStart);
            menu.Items.Add(miAuto);
            menu.Items.Add(Item("設定ファイルを開く (settings.json)", OpenSettings));
            menu.Items.Add(Item("設定ファイルを再読み込み", ReloadSettings));
            var help = new WF.ToolStripMenuItem("ヘルプ");
            help.DropDownItems.Add(Item("天気の表示場所を変更する手順", delegate { OpenHelp("weather-location.html"); }));
            help.DropDownItems.Add(Item("ヘルプのフォルダーを開く", delegate { OpenHelp(null); }));
            menu.Items.Add(help);
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add(Item("終了", Close));

            menu.Opening += delegate
            {
                miVisible.Text = IsVisible ? "ウィジェットを隠す" : "ウィジェットを表示";
                foreach (WF.ToolStripItem it in miPanels.DropDownItems)
                {
                    var mi = it as WF.ToolStripMenuItem;
                    if (mi == null) continue;
                    if (mi.Tag is FieldInfo) mi.Checked = (bool)((FieldInfo)mi.Tag).GetValue(cfg);
                    else if (mi.Tag is string) mi.Checked = cfg.HasPanel((string)mi.Tag);
                }
                miEdit.Checked = view.EditMode;
                foreach (WF.ToolStripMenuItem mi in miSkin.DropDownItems) mi.Checked = (string)mi.Tag == cfg.Skin;
                foreach (WF.ToolStripMenuItem mi in miInterval.DropDownItems) mi.Checked = (int)mi.Tag == cfg.IntervalMs;
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
                view.Relayout();
                cfg.Save();
            };
            parent.DropDownItems.Add(mi);
        }

        void ToggleVisible()
        {
            if (IsVisible) Hide();
            else
            {
                Show();
                ApplyMode();
            }
        }

        void ReloadSettings()
        {
            cfg.Reload();
            view.LayoutTransform = new ScaleTransform(cfg.Scale, cfg.Scale);
            view.ApplySkin();
            view.Relayout();
            ScheduleNextTick();
            RestorePosition();
            ApplyMode();
            ApplyClickThrough();
            sampler.RefreshWeatherNow();
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
        void OpenHelp(string file)
        {
            try
            {
                string dir = Path.Combine(AppPaths.Dir, "help");
                Process.Start(file == null ? dir : Path.Combine(dir, file));
            }
            catch (Exception ex) { Log.Write("open help", ex); }
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
            }
            catch (Exception ex) { Log.Write("snapshot", ex); }
        }

        void Cleanup()
        {
            timer.Stop();
            displayChangeTimer.Stop();
            if (snapshotPath == null) SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            if (winEventHook != IntPtr.Zero) Native.UnhookWinEvent(winEventHook);
            if (tray != null)
            {
                tray.Visible = false;
                tray.Dispose();
            }
            sampler.Dispose();
            if (snapshotPath == null) cfg.Save();
        }
    }
}

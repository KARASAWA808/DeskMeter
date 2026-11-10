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
            // --export-settings <zip> / --import-settings <zip> : 設定一式の書き出し / 読み込み (起動中の DeskMeter は読み込み後に自動で再読み込みする)
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] != "--export-settings" && args[i] != "--import-settings") continue;
                try
                {
                    if (args[i] == "--export-settings") SettingsPack.Export(Settings.Load(), args[i + 1]);
                    else SettingsPack.Import(args[i + 1]);
                }
                catch (Exception ex) { Log.Write(args[i], ex); Environment.ExitCode = 1; }
                return;
            }
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
        bool displayOff, fullscreenApp;   // 画面が消えている / 前面がフルスクリーンのアプリ (どちらも計測を止める)
        IntPtr powerNotify;
        readonly DispatcherTimer fullscreenTimer = new DispatcherTimer();
        Native.WinEventProc winEventProc;   // GC されないよう保持
        IntPtr winEventHook;
        bool hotkeyRegistered;
        EventWaitHandle showEvent;
        Thread showThread;
        internal const string EditEventName = @"Local\DeskMeter_Cmd_ToggleEdit";   // --toggle-edit (自動テスト・スクリプト用)
        internal const string ReloadEventName = @"Local\DeskMeter_Cmd_Reload";    // --reload
        EventWaitHandle editEvent, reloadEvent;
        readonly ManualResetEvent showStop = new ManualResetEvent(false);

        WF.NotifyIcon tray;
        WF.ContextMenuStrip menu;
        WF.ToolStripMenuItem miDesktop, miTopmost, miNormal, miLock, miClick, miAuto, miOpacity, miScale, miPanels, miVisible,
            miInterval, miSkin, miMonitor, miEdit, miHistory, miHotkey, miShadow, miImages, miMascotPlace, miMascotSize, miBgImageOpacity, miUndoLayout, miClockCal, miWeatherUnits, miFolders, miHotkeyEdit, miHotkeyClick, miHover, miHideMax, miLook, miWindow, miMeasure, miSettings, miAlerts, miPing;

        public MainWindow(string snapshotPath)
        {
            this.snapshotPath = snapshotPath;
            cfg = Settings.Load();
            if (Settings.IsFirstRun && snapshotPath == null) ApplyFirstRunDefaults();

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
                Native.RECT before, after;
                Native.GetWindowRect(hwnd, out before);
                dragging = true;
                try { DragMove(); } catch { }
                dragging = false;
                Native.GetWindowRect(hwnd, out after);
                if (after.Left == before.Left && after.Top == before.Top) return;   // ただのクリック (動かしていない) なら保存も吸着もしない
                SavePosition();      // 端に近ければ吸着させたうえで、モニター基準の位置として保存
                RestorePosition();
            };
            MouseRightButtonUp += delegate { ShowMenu(); };
            MouseEnter += delegate { if (cfg.HoverOpacity < 100) Opacity = 1; };
            MouseLeave += delegate { if (cfg.HoverOpacity < 100) Opacity = DimOpacity; };
            Closed += delegate { Cleanup(); };
            // 内容の高さが決まった / 変わったときに、右端・下端基準の位置を保つ
            SizeChanged += delegate { if (!dragging) RestorePosition(); };

            // 再描画は計測が終わるたびに 1 回。時計の秒表示のためだけに、間隔が 1 秒より長いときは timer で秒の変わり目にも描く。
            // 計測が 0.5 秒より速いときは描画だけ 0.5 秒ごとに間引く (半透明ウィンドウの合成は面積に比例して重い。グラフの点は計測のたびに積まれる)
            long lastDraw = 0;
            sampler.Sampled += delegate
            {
                long now = (long)Native.GetTickCount64();
                if (cfg.IntervalMs < 500 && now - lastDraw < 480) return;
                lastDraw = now;
                Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(delegate
                {
                    TickAll(false);
                    CheckAlerts();
                    UpdateTrayText();
                    if (!timer.IsEnabled) ScheduleNextTick();
                }));
            };
            timer.Tick += delegate
            {
                TickAll(true);   // 秒の変わり目: 時計だけ描き直す
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
            // Application.Run は未表示のウィンドウを必ず Show するので、全パネルを切り離した配置では表示された直後に隠し直す
            Loaded += delegate { if (hiddenByFloat && !userHidden && IsVisible) Hide(); };

            if (snapshotPath == null)
            {
                BuildMenu();
                BuildTray();
                SyncAutoStartPath();
                SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
                SystemEvents.SessionSwitch += OnSessionSwitch;
                StartShowListener();
                StartSettingsWatcher();
                if (AppPaths.Redirected && tray != null)
                    tray.ShowBalloonTip(10000, AppName, (AppPaths.InTemp ? "zip の中から直接起動しているようです。フォルダーに展開してから起動してください。" : "exe のフォルダーに書き込めないので、") + "設定・ログ・テーマは " + AppPaths.DataDir + " に保存します。", WF.ToolTipIcon.Warning);
                if (Settings.IsFirstRun && tray != null)
                    tray.ShowBalloonTip(15000, AppName + " へようこそ", "右クリックでメニュー、左ドラッグで移動できます。天気の場所は「計測と天気」→「天気の場所を設定...」で変えられます。" + (firstRunScaled ? " 画面が小さいので 80% の大きさで始めました (「見た目」→「サイズ」)。" : ""), WF.ToolTipIcon.Info);
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
            if (cfg.IntervalMs <= 1000 || !ClockShown() || !cfg.ClockSeconds || sampler.Paused)
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

        internal static WF.Screen FindScreen(string device) { return FindScreen(device, null, null); }

        // モニターを探す。優先順は モニター固有の ID (番号の振り直しや配置の入れ替えでも変わらない) → 名前 (\\.\DISPLAYn) → 位置と大きさ。
        // ID の無い古い設定で番号が振り直されたときは別のモニターに出ることがあるが、一度動かせば ID が保存される
        internal static WF.Screen FindScreen(string device, string rect, string id)
        {
            WF.Screen[] all = WF.Screen.AllScreens;
            WF.Screen byId = null, byName = null, byRect = null;
            foreach (WF.Screen s in all)
            {
                if (byId == null && id != null && string.Equals(Native.MonitorId(s.DeviceName), id, StringComparison.OrdinalIgnoreCase)) byId = s;
                if (byName == null && device != null && string.Equals(s.DeviceName, device, StringComparison.OrdinalIgnoreCase)) byName = s;
                if (byRect == null && rect != null && RectKey(s) == rect) byRect = s;
            }
            if (byId == null && byName != null && byRect != null && byName != byRect) return byRect;   // ID の無い古い設定で番号が振り直された
            return byId ?? byName ?? byRect ?? WF.Screen.PrimaryScreen ?? all[0];
        }

        internal static string RectKey(WF.Screen s)
        {
            System.Drawing.Rectangle b = s.Bounds;
            return b.Left + "," + b.Top + "," + b.Width + "," + b.Height;
        }

        // 設定 (モニター + 端からの距離) からウィンドウの位置を決める
        void RestorePosition()
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                MigrateLegacyPosition();
                WF.Screen scr = FindScreen(cfg.Monitor, cfg.MonitorRect, cfg.MonitorId);
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
                cfg.MonitorRect = RectKey(scr);
                cfg.MonitorId = Native.MonitorId(scr.DeviceName);
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
            cfg.MonitorRect = RectKey(scr);
            cfg.MonitorId = Native.MonitorId(scr.DeviceName);
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
            cfg.MonitorRect = RectKey(FindScreen(device));
            cfg.MonitorId = Native.MonitorId(device);
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
            Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE));   // Alt+Tab に出さない。クリックしても活性化しない
            HwndSource.FromHwnd(hwnd).AddHook(WndProc);

            if (snapshotPath == null)
            {
                // 画面が消えている間は計測しない (電源設定の通知を受け取る)
                try { Guid g = Native.GUID_CONSOLE_DISPLAY_STATE; powerNotify = Native.RegisterPowerSettingNotification(hwnd, ref g, 0); }
                catch (Exception pex) { Log.Write("power notify", pex); }
                // フルスクリーンのアプリが DeskMeter のあるモニターを覆っている間も止める (ゲーム中に nvidia-smi で GPU を邪魔しない)。3 秒ごとに見る。最前面モードでは見えているので止めない
                fullscreenTimer.Interval = TimeSpan.FromSeconds(3);
                fullscreenTimer.Tick += delegate
                {
                    bool fs = cfg.Mode != "topmost" && FullscreenCoversUs();
                    if (fs != fullscreenApp) { fullscreenApp = fs; UpdatePaused(); }
                    bool mx = cfg.HideWhenMaximized && !userHidden && !maxOverride && MaximizedCoversUs();
                    if (mx != hiddenByMax) SetAutoHidden(mx);
                };
                fullscreenTimer.Start();
            }

            winEventProc = OnForegroundChanged;
            winEventHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, winEventProc, 0, 0, 0);

            RestorePosition();
            ApplyMode();
            ApplyClickThrough();
            ApplyHotkey();
            ApplyHoverOpacity();
        }

        // 前面のフルスクリーンのウィンドウが、DeskMeter の表示中の全ウィンドウのあるモニターを覆っているか。
        // 別のモニターでゲームを全画面にしても、こちらのモニターに見えている間は止めない
        bool FullscreenCoversUs()
        {
            try
            {
                if (!Native.FullscreenAppActive()) return false;
                IntPtr fg = Native.GetForegroundWindow();
                if (fg == IntPtr.Zero || IsOurWindow(fg)) return false;
                Native.RECT r;
                if (!Native.GetWindowRect(fg, out r)) return false;
                var fgRect = new System.Drawing.Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
                bool any = false;
                foreach (IntPtr wh in AllHwnds())
                {
                    if (wh == hwnd && hiddenByFloat) continue;   // 本体を隠して切り離しだけのときは本体のモニターは見ない
                    any = true;
                    if (!fgRect.Contains(WF.Screen.FromHandle(wh).Bounds)) return false;
                }
                return any;
            }
            catch { return false; }
        }

        // 前面のウィンドウが最大化されていて、DeskMeter の表示中の全ウィンドウと同じモニターにあるか
        bool MaximizedCoversUs()
        {
            try
            {
                IntPtr fg = Native.GetForegroundWindow();
                if (fg == IntPtr.Zero || IsOurWindow(fg) || !Native.IsZoomed(fg)) return false;
                string mon = WF.Screen.FromHandle(fg).DeviceName;
                bool any = false;
                foreach (IntPtr wh in AllHwnds())
                {
                    if (wh == hwnd && hiddenByFloat) continue;
                    any = true;
                    if (WF.Screen.FromHandle(wh).DeviceName != mon) return false;
                }
                return any;
            }
            catch { return false; }
        }

        IntPtr WndProc(IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_MOUSEACTIVATE)
            {
                // クリックしても自分を活性化しない (作業中のアプリからキーボードの入力先を奪わない)。ドラッグは活性化なしでも動く
                handled = true;
                return new IntPtr(Native.MA_NOACTIVATE);
            }
            if (msg == Native.WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == HotkeyId) ToggleVisible();
                else if (id == HotkeyId + 1) SetEditMode(!view.EditMode);
                else if (id == HotkeyId + 2) { cfg.ClickThrough = !cfg.ClickThrough; ApplyClickThrough(); cfg.Save(); }
                else return IntPtr.Zero;
                handled = true;
                return IntPtr.Zero;
            }
            if (msg == Native.WM_POWERBROADCAST && wParam.ToInt32() == Native.PBT_POWERSETTINGCHANGE && lParam != IntPtr.Zero)
            {
                var ps = (Native.POWERBROADCAST_SETTING)Marshal.PtrToStructure(lParam, typeof(Native.POWERBROADCAST_SETTING));
                if (ps.PowerSetting == Native.GUID_CONSOLE_DISPLAY_STATE && ps.DataLength >= 1)
                {
                    bool off = ps.Data == 0;
                    if (off != displayOff) { displayOff = off; UpdatePaused(); }
                }
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
                if (hwnd == IntPtr.Zero || IsOurWindow(fg)) return;
                string cls = Native.ClassOf(fg);
                // 自動非表示をホットキーで解いていたら、別のウィンドウ (メニュー以外) が前面になった時点で解除を終える
                if (maxOverride && !cls.StartsWith("WindowsForms", StringComparison.Ordinal)) maxOverride = false;
                if (cfg.Mode != "desktop") return;
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
            ApplyHoverOpacity();   // 透過中は薄くしない
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
            foreach (string name in Skin.BuiltIn)
            {
                if (Array.IndexOf(Skin.GroupStarts, name) >= 0) miSkin.DropDownItems.Add(new WF.ToolStripSeparator());
                miSkin.DropDownItems.Add(SkinItem(name));
            }
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
            menu.Closed += delegate(object sender, WF.ToolStripDropDownClosedEventArgs e)
            {
                // Esc や自分のウィンドウのクリックで閉じたら、元のアプリに前面を返す。項目を選んだときはダイアログが出ることがあるのでそのまま。
                // 別のアプリをクリックして閉じたときは、そのアプリが前面になっているので触らない
                if (menuPrevFg != IntPtr.Zero && (e.CloseReason == WF.ToolStripDropDownCloseReason.Keyboard || e.CloseReason == WF.ToolStripDropDownCloseReason.AppClicked || e.CloseReason == WF.ToolStripDropDownCloseReason.CloseCalled))
                    Native.SetForegroundWindow(menuPrevFg);
                menuPrevFg = IntPtr.Zero;
            };

            // 項目が多いので、よく使うものを上に置き、残りは 4 つのグループにまとめる (最後に並べ直す)
            miLook = new WF.ToolStripMenuItem("見た目");
            miWindow = new WF.ToolStripMenuItem("ウィンドウ");
            miMeasure = new WF.ToolStripMenuItem("計測と天気");
            miSettings = new WF.ToolStripMenuItem("設定とフォルダー");

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
            miWindow.DropDownItems.Add(mode);

            miMonitor = new WF.ToolStripMenuItem("モニターへ移動");
            miWindow.DropDownItems.Add(miMonitor);

            miScale = new WF.ToolStripMenuItem("サイズ");
            foreach (int p in new[] { 70, 80, 90, 100, 110, 125, 150 })
            {
                int pct = p;
                var mi = Item(pct + "%", delegate { cfg.Scale = pct / 100.0; ApplyScaleAll(); cfg.Save(); });
                mi.Tag = pct;
                miScale.DropDownItems.Add(mi);
            }
            miLook.DropDownItems.Add(miScale);

            miOpacity = new WF.ToolStripMenuItem("背景の不透明度");
            foreach (int p in new[] { 100, 90, 80, 70, 60, 50, 30, 0 })
            {
                int pct = p;
                var mi = Item(pct + "%", delegate { cfg.BackgroundOpacity = pct / 100.0; InvalidateAll(); cfg.Save(); });
                mi.Tag = pct;
                miOpacity.DropDownItems.Add(mi);
            }
            miLook.DropDownItems.Add(miOpacity);

            miShadow = new WF.ToolStripMenuItem("文字の影・縁取り");
            foreach (string[] opt in new[] { new[] { "auto", "自動 (背景が薄いとき)" }, new[] { "on", "常に" }, new[] { "off", "なし" } })
            {
                string v = opt[0];
                var mi = Item(opt[1], delegate { cfg.TextShadow = v; InvalidateAll(); cfg.Save(); });
                mi.Tag = v;
                miShadow.DropDownItems.Add(mi);
            }
            miLook.DropDownItems.Add(miShadow);

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
            miLook.DropDownItems.Add(miImages);

            miInterval = new WF.ToolStripMenuItem("更新間隔");
            foreach (int ms in new[] { 250, 500, 1000, 2000, 3000, 5000, 10000 })
            {
                int v = ms;
                var mi = Item((v / 1000.0).ToString("0.##") + " 秒", delegate { cfg.IntervalMs = v; ScheduleNextTick(); sampler.Poke(); cfg.Save(); });
                mi.Tag = v;
                miInterval.DropDownItems.Add(mi);
            }
            miMeasure.DropDownItems.Add(Item("天気の場所を設定...", ShowLocationDialog));   // いちばん最初に変えたい項目なので先頭
            miMeasure.DropDownItems.Add(Item("負荷を抑える設定にする (3 秒更新・秒なし・影なし・コア 2 列)", ApplyLowLoad));
            miMeasure.DropDownItems.Add(miInterval);

            miHistory = new WF.ToolStripMenuItem("グラフの時間幅");
            foreach (int s in new[] { 30, 60, 120, 300, 600, 1800 })
            {
                int v = s;
                var mi = Item(MeterView.SpanText(v), delegate { cfg.HistorySeconds = v; InvalidateAll(); cfg.Save(); });
                mi.Tag = v;
                miHistory.DropDownItems.Add(mi);
            }
            miMeasure.DropDownItems.Add(miHistory);

            miPanels = new WF.ToolStripMenuItem("表示する項目");
            // 種類ごとに区切る: ハードウェア / プロセス / 通信とドライブ / 時計と情報
            string[][] panelGroups = {
                new[] { "cpu", "memory", "gpu", "rings", "battery" },
                new[] { "topcpu", "topmem" },
                new[] { "network", "drives" },
                new[] { "clock", "calendar", "datetile", "weather", "notes" } };
            foreach (string[] g in panelGroups)
            {
                if (miPanels.DropDownItems.Count > 0) miPanels.DropDownItems.Add(new WF.ToolStripSeparator());
                foreach (string id in g)
                {
                    string pid = id;
                    var mi = Item(Settings.PanelName(pid), delegate { cfg.TogglePanel(pid); RelayoutAll(); cfg.Save(); });
                    mi.Tag = pid;
                    miPanels.DropDownItems.Add(mi);
                }
            }
            miPanels.DropDownItems.Add(new WF.ToolStripSeparator());
            AddToggle(miPanels, "コアを2列で表示 (高さを節約)", "CompactCores");
            menu.Items.Add(miPanels);
            miClockCal = new WF.ToolStripMenuItem("時計・カレンダー・メモ");
            AddToggle(miClockCal, "24 時間制 (オフで 12 時間制)", "Clock24h");
            AddToggle(miClockCal, "週を月曜から始める", "WeekStartMonday");
            AddToggle(miClockCal, "日本の祝日を表示", "ShowHolidays");
            AddToggle(miClockCal, "時計に秒を表示 (オフで毎秒の再描画をやめる)", "ClockSeconds");
            miClockCal.DropDownItems.Add(new WF.ToolStripSeparator());
            miClockCal.DropDownItems.Add(Item("世界時計を編集...", ShowWorldClockDialog));
            miClockCal.DropDownItems.Add(Item("メモとカウントダウンを編集...", ShowNotesDialog));
            miLook.DropDownItems.Add(miClockCal);
            miAlerts = BuildAlertMenu();

            miEdit = Item("配置の編集 (ドラッグで並べ替え)", delegate { SetEditMode(!view.EditMode); });
            menu.Items.Add(miEdit);
            miUndoLayout = Item("配置を元に戻す (直前の移動を取り消す)", delegate { view.UndoDrop(); });
            menu.Items.Add(miUndoLayout);
            BuildLayoutMenus();

            menu.Items.Add(new WF.ToolStripSeparator());
            miLock = Item("位置を固定", delegate { cfg.Locked = !cfg.Locked; cfg.Save(); });
            miClick = Item("クリックを透過 (解除はトレイアイコンから)", delegate { cfg.ClickThrough = !cfg.ClickThrough; ApplyClickThrough(); cfg.Save(); });
            miHideMax = Item("同じモニターで最大化中のウィンドウがあれば隠す", delegate { cfg.HideWhenMaximized = !cfg.HideWhenMaximized; if (!cfg.HideWhenMaximized) SetAutoHidden(false); cfg.Save(); });
            miHover = new WF.ToolStripMenuItem("普段は薄く表示 (マウスを載せると不透明)");
            foreach (int p in new[] { 100, 30, 50, 70 })
            {
                int pct = p;
                var mi = Item(pct == 100 ? "無効" : pct + "%", delegate { cfg.HoverOpacity = pct; ApplyHoverOpacity(); cfg.Save(); });
                mi.Tag = pct;
                miHover.DropDownItems.Add(mi);
            }
            miWindow.DropDownItems.Add(miLock);
            miWindow.DropDownItems.Add(miClick);
            miWindow.DropDownItems.Add(miHideMax);
            miLook.DropDownItems.Add(miHover);
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
            miWindow.DropDownItems.Add(new WF.ToolStripSeparator());
            miWindow.DropDownItems.Add(miHotkey);
            miHotkeyEdit = HotkeyMenu("ホットキー (配置の編集)", "EditHotkey", new[] { "", "Ctrl+Alt+E", "Ctrl+Shift+E", "Win+Shift+E" });
            miWindow.DropDownItems.Add(miHotkeyEdit);
            miHotkeyClick = HotkeyMenu("ホットキー (クリック透過)", "ClickThroughHotkey", new[] { "", "Ctrl+Alt+T", "Ctrl+Shift+T", "Win+Shift+T" });
            miWindow.DropDownItems.Add(miHotkeyClick);

            menu.Items.Add(new WF.ToolStripSeparator());
            miMeasure.DropDownItems.Add(new WF.ToolStripSeparator());
            miMeasure.DropDownItems.Add(Item("天気を今すぐ更新", delegate { sampler.RefreshWeatherNow(); }));
            miWeatherUnits = new WF.ToolStripMenuItem("天気の単位");
            foreach (string[] u in new[] { new[] { "metric", "°C / km/h" }, new[] { "metric-ms", "°C / m/s" }, new[] { "imperial", "°F / mph" } })
            {
                string v = u[0];
                var mi = Item(u[1], delegate { cfg.WeatherUnits = v; cfg.Save(); sampler.RefreshWeatherNow(); });
                mi.Tag = v;
                miWeatherUnits.DropDownItems.Add(mi);
            }
            miMeasure.DropDownItems.Add(miWeatherUnits);
            AddToggle(miMeasure, "3 日予報を表示", "ShowForecast");
            miMeasure.DropDownItems.Add(new WF.ToolStripSeparator());
            AddToggle(miMeasure, "内部 IP を表示", "ShowIp");
            miPing = StringMenu("Ping の宛先", "PingHost", new[] { "", "1.1.1.1", "8.8.8.8" }, delegate { sampler.Poke(); });
            miMeasure.DropDownItems.Add(miPing);
            miMeasure.DropDownItems.Add(new WF.ToolStripSeparator());
            miMeasure.DropDownItems.Add(Item("ピークと起動後の通信量をリセット", delegate
            {
                lock (sampler.S) { sampler.S.PeakBps = 0; sampler.S.SessUp = 0; sampler.S.SessDown = 0; sampler.S.PingSent = 0; sampler.S.PingLost = 0; }
                InvalidateAll();
            }));
            miAuto = Item("Windows 起動時に開始", ToggleAutoStart);
            miSettings.DropDownItems.Add(Item("設定ファイルを開く (settings.json)", OpenSettings));
            miSettings.DropDownItems.Add(Item("設定ファイルを再読み込み", ReloadSettings));
            miSettings.DropDownItems.Add(new WF.ToolStripSeparator());
            miSettings.DropDownItems.Add(Item("設定一式を書き出す (zip)...", ExportSettings));
            miSettings.DropDownItems.Add(Item("設定一式を読み込む (zip)...", ImportSettings));
            miSettings.DropDownItems.Add(Item("設定を初期値に戻す...", ResetSettings));
            miSettings.DropDownItems.Add(new WF.ToolStripSeparator());
            var folders = miFolders = new WF.ToolStripMenuItem("フォルダーとログを開く");
            folders.DropDownItems.Add(Item("DeskMeter のフォルダー (設定・スキン・テーマ)", delegate { OpenPath(AppPaths.Dir); }));
            if (AppPaths.Redirected) folders.DropDownItems.Add(Item("設定の保存先を開く (%APPDATA%\\DeskMeter)", delegate { OpenPath(AppPaths.DataDir); }));
            folders.DropDownItems.Add(Item("ログを開く (deskmeter.log)", OpenLog));
            folders.DropDownItems.Add(new WF.ToolStripSeparator());
            AddToggle(folders, "1 分ごとに CSV へ記録", "LogCsv");
            miSettings.DropDownItems.Add(folders);
            var help = new WF.ToolStripMenuItem("ヘルプ");
            help.DropDownItems.Add(Item("天気の場所の決め方", delegate { OpenHelp("weather-location.html"); }));
            help.DropDownItems.Add(Item("スキンの作り方", delegate { OpenHelp("custom-skin.html"); }));
            help.DropDownItems.Add(Item("HWiNFO 連携 (CPU 温度・電力・ファン)", delegate { OpenHelp("hwinfo.html"); }));
            help.DropDownItems.Add(Item("ヘルプのフォルダーを開く", delegate { OpenHelp(null); }));
            help.DropDownItems.Add(new WF.ToolStripSeparator());
            var ver = new WF.ToolStripMenuItem("DeskMeter v" + AppInfo.Version);
            ver.Enabled = false;
            help.DropDownItems.Add(ver);
            help.DropDownItems.Add(Item("新しい版を確認 (GitHub)", delegate { OpenUrl("https://github.com/KARASAWA808/DeskMeter/releases"); }));
            // 使う順に並べ直す: 見た目の切り替え → 配置 → グループ → 通知 → 隠す → 設定・ヘルプ → 終了
            menu.Items.Clear();
            miLook.DropDownItems.Clear();
            miLook.DropDownItems.AddRange(new WF.ToolStripItem[] { miScale, miOpacity, miHover, miShadow, miImages, miClockCal });
            menu.Items.AddRange(new WF.ToolStripItem[] {
                miAttachThis,
                miSkin, miThemes, miPresets, miPanels, miEdit, miUndoLayout, miDetach,
                new WF.ToolStripSeparator(),
                miLook, miWindow, miMeasure, miAlerts,
                new WF.ToolStripSeparator(),
                miVisible, miAuto, miSettings, help,
                new WF.ToolStripSeparator(),
                Item("終了", Close) });
            visibleItemIndex = menu.Items.IndexOf(miVisible);

            menu.Opening += delegate
            {
                UpdateLayoutMenus();
                miVisible.Text = userHidden || hiddenByMax ? "ウィジェットを表示" : "ウィジェットを隠す";   // 全パネルを切り離して本体だけ隠れているときも「隠す」。最大化中の自動非表示中は「表示」
                foreach (WF.ToolStripItem it in miPanels.DropDownItems)
                {
                    var mi = it as WF.ToolStripMenuItem;
                    if (mi == null) continue;
                    if (mi.Tag is FieldInfo) mi.Checked = (bool)((FieldInfo)mi.Tag).GetValue(cfg);
                    else if (mi.Tag is string) mi.Checked = cfg.HasPanel((string)mi.Tag);
                }
                miEdit.Checked = view.EditMode;
                miUndoLayout.Enabled = view.EditMode && view.CanUndo;
                miUndoLayout.Visible = view.EditMode;
                SyncToggles(miClockCal);
                SyncToggles(miFolders);
                SyncIntMenus();
                SyncHotkeyMenu(miHotkeyEdit, cfg.EditHotkey);
                SyncHotkeyMenu(miHotkeyClick, cfg.ClickThroughHotkey);
                SyncHotkeyMenu(miPing, cfg.PingHost);
                SyncToggles(miMeasure);
                // 隠しているときは「ウィジェットを表示」を先頭に出す (トレイから開いたとき見つけやすい)
                int visIdx = menu.Items.IndexOf(miVisible);
                int wantIdx = userHidden ? 1 : visibleItemIndex;
                if (visIdx != wantIdx) { menu.Items.Remove(miVisible); menu.Items.Insert(Math.Min(wantIdx, menu.Items.Count), miVisible); }
                miHideMax.Checked = cfg.HideWhenMaximized;
                foreach (WF.ToolStripMenuItem mi in miHover.DropDownItems) mi.Checked = (int)mi.Tag == cfg.HoverOpacity;
                MarkCustom(miHover, cfg.HoverOpacity + "%", CustomInt);
                foreach (WF.ToolStripMenuItem mi in miWeatherUnits.DropDownItems) mi.Checked = (string)mi.Tag == cfg.WeatherUnits;
                foreach (WF.ToolStripItem it in miPanels.DropDownItems)
                {
                    var mi = it as WF.ToolStripMenuItem;
                    if (mi != null && (mi.Tag as string) == "battery")
                    {
                        // デスクトップ機では選べない (理由が分かるように書く)
                        bool ok = sampler.S.HasBattery || cfg.HasPanel("battery");
                        mi.Enabled = ok;
                        mi.Text = ok ? Settings.PanelName("battery") : Settings.PanelName("battery") + " (この PC にはありません)";
                    }
                }
                foreach (WF.ToolStripMenuItem mi in miShadow.DropDownItems) mi.Checked = (string)mi.Tag == cfg.TextShadow;
                foreach (WF.ToolStripMenuItem mi in miMascotPlace.DropDownItems) mi.Checked = (string)mi.Tag == cfg.MascotPlacement;
                foreach (WF.ToolStripMenuItem mi in miMascotSize.DropDownItems) mi.Checked = (int)mi.Tag == cfg.MascotSize;
                MarkCustom(miMascotSize, cfg.MascotSize + " px", CustomInt);
                foreach (WF.ToolStripMenuItem mi in miBgImageOpacity.DropDownItems) mi.Checked = (int)mi.Tag == (int)Math.Round(cfg.BackgroundImageOpacity * 100);
                MarkCustom(miBgImageOpacity, (int)Math.Round(cfg.BackgroundImageOpacity * 100) + "%", CustomInt);
                FillSkinMenu();   // skins フォルダーを見直す (追加・削除をメニューに反映)
                foreach (WF.ToolStripItem it in miSkin.DropDownItems)
                {
                    var mi = it as WF.ToolStripMenuItem;
                    if (mi != null) mi.Checked = string.Equals((string)mi.Tag, cfg.Skin, StringComparison.OrdinalIgnoreCase);
                }
                foreach (WF.ToolStripMenuItem mi in miInterval.DropDownItems) mi.Checked = (int)mi.Tag == cfg.IntervalMs;
                MarkCustom(miInterval, (cfg.IntervalMs / 1000.0).ToString("0.##") + " 秒", CustomInt);
                foreach (WF.ToolStripMenuItem mi in miHistory.DropDownItems) mi.Checked = (int)mi.Tag == cfg.HistorySeconds;
                MarkCustom(miHistory, MeterView.SpanText(cfg.HistorySeconds), CustomInt);
                foreach (WF.ToolStripMenuItem mi in miHotkey.DropDownItems) mi.Checked = string.Equals((string)mi.Tag, cfg.Hotkey, StringComparison.OrdinalIgnoreCase);
                MarkCustom(miHotkey, cfg.Hotkey, CustomStr);
                miHotkey.Text = !string.IsNullOrEmpty(cfg.Hotkey) && !hotkeyOk[0] ? "ホットキー (表示/非表示) - 登録できません" : "ホットキー (表示/非表示)";
                miHotkeyEdit.Text = !string.IsNullOrEmpty(cfg.EditHotkey) && !hotkeyOk[1] ? "ホットキー (配置の編集) - 登録できません" : "ホットキー (配置の編集)";
                miHotkeyClick.Text = !string.IsNullOrEmpty(cfg.ClickThroughHotkey) && !hotkeyOk[2] ? "ホットキー (クリック透過) - 登録できません" : "ホットキー (クリック透過)";
                miMonitor.Enabled = WF.Screen.AllScreens.Length > 1;
                miDesktop.Checked = cfg.Mode == "desktop";
                miTopmost.Checked = cfg.Mode == "topmost";
                miNormal.Checked = cfg.Mode == "normal";
                miLock.Checked = cfg.Locked;
                miClick.Checked = cfg.ClickThrough;
                miAuto.Checked = IsAutoStart();
                foreach (WF.ToolStripMenuItem mi in miScale.DropDownItems) mi.Checked = (int)mi.Tag == (int)Math.Round(cfg.Scale * 100);
                MarkCustom(miScale, (int)Math.Round(cfg.Scale * 100) + "%", CustomInt);
                foreach (WF.ToolStripMenuItem mi in miOpacity.DropDownItems) mi.Checked = (int)mi.Tag == (int)Math.Round(cfg.BackgroundOpacity * 100);
                MarkCustom(miOpacity, (int)Math.Round(cfg.BackgroundOpacity * 100) + "%", CustomInt);
                BuildMonitorMenu();
            };
        }

        // プリセットに無い値 (settings.json で直接書いたもの、既定の不透明度 82% など) のときは、現在値の項目を足してチェックを付ける。
        // 目印の Tag は項目の Tag と同じ型にしておく (チェック判定で (int)/(string) にキャストするため)
        static readonly object CustomInt = int.MinValue, CustomStr = "\0custom";

        static void MarkCustom(WF.ToolStripMenuItem menu, string current, object mark)
        {
            WF.ToolStripItem old = null;
            foreach (WF.ToolStripItem it in menu.DropDownItems) if (Equals(it.Tag, mark)) { old = it; break; }
            if (old != null) menu.DropDownItems.Remove(old);
            foreach (WF.ToolStripItem it in menu.DropDownItems)
            {
                var mi = it as WF.ToolStripMenuItem;
                if (mi != null && mi.Checked) return;
            }
            if (string.IsNullOrEmpty(current)) return;
            var c = new WF.ToolStripMenuItem(current + " (settings.json の値)");
            c.Tag = mark;
            c.Checked = true;
            c.Enabled = false;
            menu.DropDownItems.Add(c);
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
        WF.ToolStripMenuItem HotkeyMenu(string title, string field, string[] presets)
        {
            return StringMenu(title, field, presets, ApplyHotkey);
        }

        // 文字列の設定 1 つに対応するサブメニュー (空文字は「無効」)。settings.json で候補に無い値にしたときは MarkCustom が現在値を足す
        WF.ToolStripMenuItem StringMenu(string title, string field, string[] presets, Action onChange)
        {
            FieldInfo f = typeof(Settings).GetField(field);
            var m = new WF.ToolStripMenuItem(title);
            foreach (string k in presets)
            {
                string v = k;
                var mi = Item(v.Length == 0 ? "無効" : v, delegate { f.SetValue(cfg, v); onChange(); cfg.Save(); InvalidateAll(); });
                mi.Tag = v;
                m.DropDownItems.Add(mi);
            }
            return m;
        }

        // 開いているダイアログ。トレイのメニューやホットキーから同じものを 2 枚開かない (後で閉じたほうが先の設定を上書きしてしまう)
        Window openDialog;

        bool DialogBusy()
        {
            if (openDialog == null) return false;
            try { openDialog.Activate(); } catch { }
            return true;
        }

        void ShowNotesDialog()
        {
            if (DialogBusy()) return;
            try
            {
                var dlg = new NotesDialog(cfg);
                openDialog = dlg;
                bool ok;
                try { ok = dlg.ShowDialog() == true; }
                finally { openDialog = null; }
                if (!ok) return;
                if (!cfg.HasPanel("notes")) cfg.TogglePanel("notes");   // 書いたのに出ないと分かりにくいので、パネルをオンにする
                RelayoutAll();
                cfg.Save();
            }
            catch (Exception ex) { Log.Write("notes dialog", ex); }
        }

        void ShowWorldClockDialog()
        {
            if (DialogBusy()) return;
            try
            {
                var dlg = new WorldClockDialog(cfg);
                openDialog = dlg;
                bool ok;
                try { ok = dlg.ShowDialog() == true; }
                finally { openDialog = null; }
                if (!ok) return;
                RelayoutAll();
                cfg.Save();
            }
            catch (Exception ex) { Log.Write("world clock dialog", ex); }
        }

        void SyncHotkeyMenu(WF.ToolStripMenuItem m, string value)
        {
            foreach (WF.ToolStripMenuItem mi in m.DropDownItems) mi.Checked = string.Equals((string)mi.Tag, value, StringComparison.OrdinalIgnoreCase);
            MarkCustom(m, value, CustomStr);
        }

        // AddToggle で作った項目のチェックを設定に合わせる
        void SyncToggles(WF.ToolStripMenuItem parent)
        {
            foreach (WF.ToolStripItem it in parent.DropDownItems)
            {
                var mi = it as WF.ToolStripMenuItem;
                if (mi != null && mi.Tag is FieldInfo) mi.Checked = (bool)((FieldInfo)mi.Tag).GetValue(cfg);
            }
        }

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
            ApplyHoverOpacity();
            ResetAlertState();
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
        // ホットキーは 3 つ: HotkeyId = 表示/非表示, +1 = 配置の編集, +2 = クリック透過
        readonly bool[] hotkeyOk = new bool[3];

        void ApplyHotkey()
        {
            if (hwnd == IntPtr.Zero || snapshotPath != null) return;
            string[] keys = { cfg.Hotkey, cfg.EditHotkey, cfg.ClickThroughHotkey };
            string[] names = { "表示/非表示", "配置の編集", "クリック透過" };
            var used = new Dictionary<uint, int>();   // (mods, vk) → 先に登録した番号
            for (int i = 0; i < keys.Length; i++)
            {
                if (hotkeyOk[i]) { Native.UnregisterHotKey(hwnd, HotkeyId + i); hotkeyOk[i] = false; }
                uint mods, vk;
                if (!ParseHotkey(keys[i], out mods, out vk)) continue;
                uint combo = (mods << 16) | (vk & 0xFFFF);
                int first;
                if (used.TryGetValue(combo, out first))
                {
                    if (tray != null) tray.ShowBalloonTip(5000, AppName, "ホットキー " + keys[i] + " は「" + names[first] + "」と同じなので「" + names[i] + "」には使えません。別のキーにしてください。", WF.ToolTipIcon.Warning);
                    continue;
                }
                used[combo] = i;
                if (Native.RegisterHotKey(hwnd, HotkeyId + i, mods | Native.MOD_NOREPEAT, vk)) hotkeyOk[i] = true;
                else
                {
                    Log.Write("hotkey " + keys[i], new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
                    if (tray != null) tray.ShowBalloonTip(5000, AppName, "ホットキー " + keys[i] + " (" + names[i] + ") は他のアプリが使っているため登録できませんでした。「ウィンドウ」→「ホットキー」で別のキーを選んでください。", WF.ToolTipIcon.Warning);
                }
            }
            hotkeyRegistered = hotkeyOk[0];
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
                    bool merged = Settings.TakeMergedExternal();   // 先に読んで旗を消す (次回に持ち越さない)
                    if (!Settings.ExternallyModified() && !merged) return;   // 自分の Save によるもの
                    ReloadSettings();
                    if (tray != null) tray.ShowBalloonTip(3000, AppName, "settings.json の変更を反映しました。", WF.ToolTipIcon.Info);
                };
                settingsWatcher = new FileSystemWatcher(AppPaths.DataDir, "settings.json");
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
                        if (i == 0) Dispatcher.BeginInvoke(new Action(delegate
                        {
                            if (userHidden) ToggleVisible();
                            else if (hiddenByMax) { maxOverride = true; SetAutoHidden(false); }   // 最大化中の自動非表示も解いて、切り離しパネルも出す
                            else if (!hiddenByFloat) { Show(); ApplyMode(); }
                        }));
                        else if (i == 1) Dispatcher.BeginInvoke(new Action(delegate { SetEditMode(!view.EditMode); }));
                        else if (i == 2) Dispatcher.BeginInvoke(new Action(ReloadSettings));
                        else break;
                    }
                });
                t.IsBackground = true;
                t.Name = "DeskMeter show listener";
                showThread = t;
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
                    case "battery":
                        StartFirst("ms-settings:batterysaver", "powercfg.cpl");
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
            tray.Text = AppName + " v" + AppInfo.Version + " (クリックで表示/非表示)";
            tray.Icon = MakeIcon();
            tray.ContextMenuStrip = menu;
            // 左のシングルクリックで表示/非表示。ダブルクリックは 1 回目の MouseClick も起こすので、少し待って 2 回目が来なければシングルとみなす
            trayClickTimer.Interval = TimeSpan.FromMilliseconds(WF.SystemInformation.DoubleClickTime);
            trayClickTimer.Tick += delegate { trayClickTimer.Stop(); ToggleVisible(); };
            tray.MouseClick += delegate(object s, WF.MouseEventArgs e)
            {
                if (e.Button != WF.MouseButtons.Left) return;
                if (trayClickTimer.IsEnabled) trayClickTimer.Stop();   // 2 回目 = ダブルクリック: 1 回だけ切り替える
                else trayClickTimer.Start();
            };
            tray.MouseDoubleClick += delegate(object s, WF.MouseEventArgs e) { if (e.Button == WF.MouseButtons.Left) { trayClickTimer.Stop(); ToggleVisible(); } };
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

        IntPtr menuPrevFg;   // メニューを開く前に前面だったウィンドウ (選ばずに閉じたら返す)

        bool firstRunScaled;
        int visibleItemIndex;   // 「ウィジェットを隠す」の通常の位置
        readonly DispatcherTimer trayClickTimer = new DispatcherTimer();
        long trayTextAt;

        // トレイのツールチップに CPU と RAM を出す (5 秒ごと。隠したまま様子を見られる)
        void UpdateTrayText()
        {
            if (tray == null) return;
            long now = (long)Native.GetTickCount64();
            if (now - trayTextAt < 5000) return;
            trayTextAt = now;
            float cpu; ulong total, avail; bool gpu; float gpuUtil;
            lock (sampler.S) { cpu = sampler.S.CpuTotal; total = sampler.S.RamTotal; avail = sampler.S.RamAvail; gpu = sampler.S.GpuOk; gpuUtil = sampler.S.GpuUtil; }
            string t = AppName + "  CPU " + Math.Round(cpu) + "%";
            if (total > 0) t += "  RAM " + Math.Round(100.0 * (total - avail) / total) + "%";
            if (gpu) t += "  GPU " + Math.Round(gpuUtil) + "%";
            if (t.Length > 63) t = t.Substring(0, 63);   // NotifyIcon.Text の上限
            try { if (tray.Text != t) tray.Text = t; } catch { }
        }

        // 初回起動: 画面が小さければ (作業領域の幅 1400 px 未満)、既定の 3 列 708 px が半分以上を占めるので 80% で始める
        void ApplyFirstRunDefaults()
        {
            try
            {
                System.Drawing.Rectangle wa = WF.Screen.PrimaryScreen.WorkingArea;
                if (wa.Width < 1400 || wa.Height < 800)
                {
                    cfg.Scale = 0.8;
                    cfg.CompactCores = true;
                    firstRunScaled = true;
                    cfg.Save();
                }
            }
            catch (Exception ex) { Log.Write("first run", ex); }
        }

        // 負荷を抑える組み合わせを一度に適用する (個別に戻せる)
        void ApplyLowLoad()
        {
            cfg.IntervalMs = 3000;
            cfg.ClockSeconds = false;
            cfg.TextShadow = "off";
            cfg.CompactCores = true;
            cfg.LogCsv = false;
            if (cfg.HistorySeconds > 300) cfg.HistorySeconds = 300;
            ApplySkinAll();
            RelayoutAll();
            ScheduleNextTick();
            sampler.Poke();
            cfg.Save();
            if (tray != null) tray.ShowBalloonTip(8000, AppName, "更新間隔 3 秒、時計の秒なし、文字の影なし、コア 2 列にしました。元に戻すには「計測と天気」→「更新間隔」、「見た目」→「時計・カレンダー・メモ」「文字の影・縁取り」、「表示する項目」で個別に変えてください。", WF.ToolTipIcon.Info);
        }

        void ShowMenu()
        {
            if (menu == null) return;
            IntPtr fg = Native.GetForegroundWindow();
            menuPrevFg = fg != IntPtr.Zero && !IsOurWindow(fg) ? fg : IntPtr.Zero;
            Native.SetForegroundWindow(hwnd);   // 外をクリックしたときにメニューが閉じるよう、トレイのメニューと同じく一度前面にする
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

        void OpenUrl(string url)
        {
            try { Process.Start(url); }
            catch (Exception ex) { Log.Write("open url", ex); }
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
                if (DialogBusy()) return;
                var dlg = new LocationDialog(cfg);
                openDialog = dlg;
                bool ok;
                try { ok = dlg.ShowDialog() == true; }
                finally { openDialog = null; }
                if (!ok) return;
                cfg.Save();
                sampler.RefreshWeatherNow();
                InvalidateAll();
            }
            catch (Exception ex) { Log.Write("location dialog", ex); }
        }

        void OpenPath(string path)
        {
            try { Process.Start("explorer.exe", "\"" + path + "\""); }
            catch (Exception ex) { Log.Write("open folder", ex); }
        }

        void OpenLog()
        {
            try
            {
                string path = Path.Combine(AppPaths.DataDir, "deskmeter.log");
                if (!File.Exists(path))
                {
                    if (tray != null) tray.ShowBalloonTip(3000, AppName, "ログはまだありません (問題が起きたときだけ作られます)。", WF.ToolTipIcon.Info);
                    return;
                }
                Process.Start("notepad.exe", "\"" + path + "\"");
            }
            catch (Exception ex) { Log.Write("open log", ex); }
        }

        // 設定を初期値に戻す。元のファイルは settings.backup-日時.json として残す
        void ResetSettings()
        {
            if (WF.MessageBox.Show("設定を初期値に戻しますか?\n位置・配置・スキン・画像・天気の場所などがすべて初期値になります。\n元の settings.json は settings.backup-日時.json として同じフォルダーに残します。",
                AppName, WF.MessageBoxButtons.YesNo, WF.MessageBoxIcon.Warning, WF.MessageBoxDefaultButton.Button2) != WF.DialogResult.Yes) return;
            try
            {
                if (File.Exists(Settings.FilePath))
                {
                    string backup = Path.Combine(AppPaths.DataDir, "settings.backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
                    File.Copy(Settings.FilePath, backup, true);
                    File.Delete(Settings.FilePath);
                }
            }
            catch (Exception ex)
            {
                Log.Write("reset settings", ex);
                if (tray != null) tray.ShowBalloonTip(5000, AppName, "設定を初期化できませんでした。deskmeter.log を確認してください。", WF.ToolTipIcon.Warning);
                return;
            }
            ReloadSettings();   // ファイルが無いので既定値が読み込まれる
            cfg.Save();
            if (tray != null) tray.ShowBalloonTip(6000, AppName, "設定を初期値に戻しました。元の設定は settings.backup-日時.json に残っています (「設定とフォルダー」→「フォルダーとログを開く」)。", WF.ToolTipIcon.Info);
        }

        // 設定一式の書き出し / 読み込み (zip)
        void ExportSettings()
        {
            try
            {
                using (var dlg = new WF.SaveFileDialog())
                {
                    dlg.Filter = "zip (*.zip)|*.zip";
                    dlg.FileName = "DeskMeter-settings-" + DateTime.Now.ToString("yyyyMMdd") + ".zip";
                    if (dlg.ShowDialog() != WF.DialogResult.OK) return;
                    SettingsPack.Export(cfg, dlg.FileName);
                    if (tray != null) tray.ShowBalloonTip(4000, AppName, "設定一式を書き出しました: " + Path.GetFileName(dlg.FileName), WF.ToolTipIcon.Info);
                }
            }
            catch (Exception ex)
            {
                Log.Write("export settings", ex);
                if (tray != null) tray.ShowBalloonTip(5000, AppName, "書き出せませんでした。deskmeter.log を確認してください。", WF.ToolTipIcon.Warning);
            }
        }

        void ImportSettings()
        {
            try
            {
                string path;
                using (var dlg = new WF.OpenFileDialog())
                {
                    dlg.Filter = "zip (*.zip)|*.zip";
                    if (dlg.ShowDialog() != WF.DialogResult.OK) return;
                    path = dlg.FileName;
                }
                if (WF.MessageBox.Show("zip の設定一式を取り込みますか?\n今の settings.json は settings.backup-日時.json として残します。同じ名前のテーマ・スキンは上書きします。",
                    AppName, WF.MessageBoxButtons.YesNo, WF.MessageBoxIcon.Question, WF.MessageBoxDefaultButton.Button2) != WF.DialogResult.Yes) return;
                string what = SettingsPack.Import(path);
                ReloadSettings();
                if (tray != null) tray.ShowBalloonTip(5000, AppName, "取り込みました: " + what, WF.ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                Log.Write("import settings", ex);
                if (tray != null) tray.ShowBalloonTip(5000, AppName, "取り込めませんでした。deskmeter.log を確認してください。", WF.ToolTipIcon.Warning);
            }
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
                    dc.DrawRectangle(MeterView.SnapshotBrush(view, s), null, new Rect(0, 0, view.ActualWidth, view.ActualHeight));
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
            fullscreenTimer.Stop();
            trayClickTimer.Stop();
            settingsChangedTimer.Stop();
            MeterView.ImageFailed -= OnImageFailed;
            if (powerNotify != IntPtr.Zero) { Native.UnregisterPowerSettingNotification(powerNotify); powerNotify = IntPtr.Zero; }
            if (snapshotPath == null)
            {
                SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
                SystemEvents.SessionSwitch -= OnSessionSwitch;
            }
            if (winEventHook != IntPtr.Zero) Native.UnhookWinEvent(winEventHook);
            for (int i = 0; i < hotkeyOk.Length; i++) if (hotkeyOk[i]) { Native.UnregisterHotKey(hwnd, HotkeyId + i); hotkeyOk[i] = false; }
            hotkeyRegistered = false;
            showStop.Set();
            if (showThread != null) { showThread.Join(1000); showThread = null; }   // 待機中のスレッドが抜けてからハンドルを閉じる
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

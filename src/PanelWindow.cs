using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using WF = System.Windows.Forms;

namespace DeskMeter
{
    // 切り離したパネル 1 つ分の独立ウィンドウ。見た目・Z オーダー・クリック透過は本体と同じ設定に従う
    sealed class PanelWindow : Window
    {
        readonly MainWindow owner;
        readonly Settings cfg;
        public readonly string Id;
        public readonly MeterView View;
        public IntPtr Hwnd;
        bool dragging;

        public PanelWindow(MainWindow owner, Settings cfg, MeterState state, string id)
        {
            this.owner = owner;
            this.cfg = cfg;
            Id = id;
            Title = "DeskMeter - " + Settings.PanelName(id);
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

            View = new MeterView(state, cfg);
            View.SinglePanel = id;
            View.LayoutTransform = new ScaleTransform(cfg.Scale, cfg.Scale);
            Content = View;

            MouseLeftButtonDown += delegate
            {
                if (cfg.Locked) return;
                Native.RECT before, after;
                Native.GetWindowRect(Hwnd, out before);
                dragging = true;
                try { DragMove(); } catch { }
                dragging = false;
                Native.GetWindowRect(Hwnd, out after);
                if (after.Left == before.Left && after.Top == before.Top) return;   // ただのクリックなら保存しない
                SavePosition();
            };
            // 行が増えて大きくなったとき (ドライブの追加、GPU の行、倍率の変更) も画面内に収める
            SizeChanged += delegate { if (!dragging) RestorePosition(); };
            MouseRightButtonUp += delegate { owner.ShowMenuFor(Id); };
            MouseDoubleClick += delegate { owner.OpenPanelTarget(Id); };
            Opacity = cfg.ClickThrough ? 1 : cfg.HoverOpacity / 100.0;
            MouseEnter += delegate { if (cfg.HoverOpacity < 100) Opacity = 1; };
            MouseLeave += delegate { if (cfg.HoverOpacity < 100) Opacity = cfg.ClickThrough ? 1 : cfg.HoverOpacity / 100.0; };
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            Hwnd = new WindowInteropHelper(this).Handle;
            long ex = Native.GetWindowLongPtr(Hwnd, Native.GWL_EXSTYLE).ToInt64();
            Native.SetWindowLongPtr(Hwnd, Native.GWL_EXSTYLE, new IntPtr(ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE));
            HwndSource.FromHwnd(Hwnd).AddHook(WndProc);
            RestorePosition();
            ApplyMode();
            ApplyClickThrough();
        }

        IntPtr WndProc(IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_MOUSEACTIVATE)
            {
                handled = true;   // クリックで活性化しない (本体と同じ)
                return new IntPtr(Native.MA_NOACTIVATE);
            }
            if (msg == Native.WM_WINDOWPOSCHANGING && cfg.Mode == "desktop" && !owner.ShowDesktop)
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

        public void ApplyMode()
        {
            Topmost = cfg.Mode == "topmost";
            if (Hwnd == IntPtr.Zero) return;
            if (cfg.Mode != "topmost")
            {
                Native.SetWindowPos(Hwnd, Native.HWND_NOTOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                if (cfg.Mode == "desktop")
                    Native.SetWindowPos(Hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }
        }

        public void ApplyClickThrough()
        {
            if (Hwnd == IntPtr.Zero) return;
            long ex = Native.GetWindowLongPtr(Hwnd, Native.GWL_EXSTYLE).ToInt64();
            ex = cfg.ClickThrough ? ex | Native.WS_EX_TRANSPARENT : ex & ~Native.WS_EX_TRANSPARENT;
            Native.SetWindowLongPtr(Hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
        }

        public void ApplyScale()
        {
            View.LayoutTransform = new ScaleTransform(cfg.Scale, cfg.Scale);
        }

        // 設定 (モニター + 作業領域の左上からの距離) から位置を決める
        public void RestorePosition()
        {
            if (Hwnd == IntPtr.Zero) return;
            try
            {
                Settings.FloatPos p = cfg.EnsureFloatPos(Id);
                WF.Screen scr = MainWindow.FindScreen(p.Monitor, p.Rect, p.MonitorId);
                System.Drawing.Rectangle wa = scr.WorkingArea;
                // 大きさは WPF のレイアウト結果から (SizeChanged の時点では HWND の大きさがまだ古い)
                double scale = Native.DpiScale(scr.Bounds);
                int w = Math.Max(1, (int)Math.Round(ActualWidth * scale)), h = Math.Max(1, (int)Math.Round(ActualHeight * scale));
                int x = MainWindow.ClampToArea(wa.Left + p.X, w, wa.Left, wa.Right);
                int y = MainWindow.ClampToArea(wa.Top + p.Y, h, wa.Top, wa.Bottom);
                Native.SetWindowPos(Hwnd, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            }
            catch (Exception ex) { Log.Write("panel restore " + Id, ex); }
        }

        void SavePosition()
        {
            if (Hwnd == IntPtr.Zero) return;
            try
            {
                Native.RECT r;
                Native.GetWindowRect(Hwnd, out r);
                WF.Screen scr = WF.Screen.FromHandle(Hwnd);
                Settings.FloatPos p = cfg.EnsureFloatPos(Id);
                p.Monitor = scr.DeviceName;
                p.Rect = MainWindow.RectKey(scr);
                p.MonitorId = Native.MonitorId(scr.DeviceName);
                p.X = r.Left - scr.WorkingArea.Left;
                p.Y = r.Top - scr.WorkingArea.Top;
                cfg.Save();
            }
            catch (Exception ex) { Log.Write("panel save " + Id, ex); }
        }
    }
}

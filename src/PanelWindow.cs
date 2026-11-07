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
                try { DragMove(); } catch { }
                SavePosition();
            };
            MouseRightButtonUp += delegate { owner.ShowMenuFor(Id); };
            MouseDoubleClick += delegate { owner.OpenPanelTarget(Id); };
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            Hwnd = new WindowInteropHelper(this).Handle;
            long ex = Native.GetWindowLongPtr(Hwnd, Native.GWL_EXSTYLE).ToInt64();
            Native.SetWindowLongPtr(Hwnd, Native.GWL_EXSTYLE, new IntPtr(ex | Native.WS_EX_TOOLWINDOW));
            HwndSource.FromHwnd(Hwnd).AddHook(WndProc);
            RestorePosition();
            ApplyMode();
            ApplyClickThrough();
        }

        IntPtr WndProc(IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
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
                WF.Screen scr = MainWindow.FindScreen(p.Monitor);
                System.Drawing.Rectangle wa = scr.WorkingArea;
                int x = Math.Max(wa.Left - 100, Math.Min(wa.Right - 60, wa.Left + p.X));
                int y = Math.Max(wa.Top, Math.Min(wa.Bottom - 40, wa.Top + p.Y));
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
                p.X = r.Left - scr.WorkingArea.Left;
                p.Y = r.Top - scr.WorkingArea.Top;
                cfg.Save();
            }
            catch (Exception ex) { Log.Write("panel save " + Id, ex); }
        }
    }
}

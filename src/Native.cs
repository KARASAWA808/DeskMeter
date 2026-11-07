using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DeskMeter
{
    static class Native
    {
        public const int GWL_EXSTYLE = -20;
        public const long WS_EX_TOOLWINDOW = 0x80L;
        public const long WS_EX_TRANSPARENT = 0x20L;
        public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
        public const int WM_WINDOWPOSCHANGING = 0x0046;
        public const uint EVENT_SYSTEM_FOREGROUND = 3;
        public const uint GW_HWNDNEXT = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPOS
        {
            public IntPtr hwnd;
            public IntPtr hwndInsertAfter;
            public int x, y, cx, cy;
            public uint flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile;
            public ulong ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct IO_COUNTERS { public ulong a, b, c, d, e, f; }

        [StructLayout(LayoutKind.Sequential)]
        public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION Basic;
            public IO_COUNTERS Io;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        public delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        public static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        public static extern IntPtr SetWindowLongPtr(IntPtr h, int index, IntPtr value);
        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")]
        public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")]
        public static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr h);
        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")]
        public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("shcore.dll")]
        public static extern int GetDpiForMonitor(IntPtr hmon, int type, out uint dpiX, out uint dpiY);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }
        public const uint MONITOR_DEFAULTTONEAREST = 2;
        public static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

        // そのモニターの拡大率 (100% = 1.0)。取得できなければ 1.0
        public static double DpiScale(System.Drawing.Rectangle bounds)
        {
            try
            {
                IntPtr hmon = MonitorFromPoint(new POINT(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2), MONITOR_DEFAULTTONEAREST);
                uint dx, dy;
                if (GetDpiForMonitor(hmon, 0, out dx, out dy) == 0 && dx > 0) return dx / 96.0;
            }
            catch { }
            return 1.0;
        }

        [DllImport("kernel32.dll")]
        public static extern ulong GetTickCount64();
        [DllImport("kernel32.dll")]
        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateJobObject(IntPtr attr, string name);
        [DllImport("kernel32.dll")]
        public static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, int len);
        [DllImport("kernel32.dll")]
        public static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("ntdll.dll")]
        public static extern int NtQuerySystemInformation(int infoClass, IntPtr buffer, int length, out int returnLength);

        public static string ClassOf(IntPtr h)
        {
            var sb = new StringBuilder(128);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }
    }

    // PDH (Performance Data Helper) の薄いラッパー。カウンター名は英語名で指定できる (ロケール非依存)。
    sealed class PdhQuery : IDisposable
    {
        const uint PDH_FMT_DOUBLE = 0x00000200, PDH_FMT_NOCAP100 = 0x00008000;
        const int PDH_MORE_DATA = unchecked((int)0x800007D2);
        const int ItemSize = 24;   // x64 の PDH_FMT_COUNTERVALUE_ITEM_W

        [StructLayout(LayoutKind.Explicit, Size = 16)]
        struct PDH_FMT_COUNTERVALUE
        {
            [FieldOffset(0)] public uint CStatus;
            [FieldOffset(8)] public double DoubleValue;
        }

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        static extern int PdhOpenQuery(string source, IntPtr userData, out IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        static extern int PdhAddEnglishCounter(IntPtr query, string path, IntPtr userData, out IntPtr counter);
        [DllImport("pdh.dll")]
        static extern int PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll")]
        static extern int PdhGetFormattedCounterValue(IntPtr counter, uint format, IntPtr type, out PDH_FMT_COUNTERVALUE value);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        static extern int PdhGetFormattedCounterArray(IntPtr counter, uint format, ref int bufferSize, out int itemCount, IntPtr buffer);
        [DllImport("pdh.dll")]
        static extern int PdhCloseQuery(IntPtr query);

        IntPtr query, buf;
        int bufLen;

        public bool Open() { return PdhOpenQuery(null, IntPtr.Zero, out query) == 0; }

        public IntPtr Add(string path)
        {
            IntPtr c;
            return query != IntPtr.Zero && PdhAddEnglishCounter(query, path, IntPtr.Zero, out c) == 0 ? c : IntPtr.Zero;
        }

        public bool Collect() { return query != IntPtr.Zero && PdhCollectQueryData(query) == 0; }

        // 値が無いとき (初回の rate 系カウンターなど) は NaN
        public double Value(IntPtr counter)
        {
            if (counter == IntPtr.Zero) return double.NaN;
            PDH_FMT_COUNTERVALUE v;
            if (PdhGetFormattedCounterValue(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, IntPtr.Zero, out v) != 0) return double.NaN;
            return v.CStatus > 1 ? double.NaN : v.DoubleValue;
        }

        // ワイルドカード指定したカウンターの全インスタンスを names / values に入れて件数を返す
        public int Array(IntPtr counter, System.Collections.Generic.List<string> names, System.Collections.Generic.List<double> values)
        {
            names.Clear();
            values.Clear();
            if (counter == IntPtr.Zero) return 0;
            int size = bufLen, count;
            int r = PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out count, buf);
            if (r == PDH_MORE_DATA)
            {
                if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
                bufLen = size + 1024;
                buf = Marshal.AllocHGlobal(bufLen);
                size = bufLen;
                r = PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out count, buf);
            }
            if (r != 0) return 0;
            for (int i = 0; i < count; i++)
            {
                IntPtr item = new IntPtr(buf.ToInt64() + (long)i * ItemSize);
                uint status = (uint)Marshal.ReadInt32(item, 8);
                names.Add(Marshal.PtrToStringUni(Marshal.ReadIntPtr(item)));
                values.Add(status > 1 ? double.NaN : BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, 16)));
            }
            return count;
        }

        public void Dispose()
        {
            if (query != IntPtr.Zero) { PdhCloseQuery(query); query = IntPtr.Zero; }
            if (buf != IntPtr.Zero) { Marshal.FreeHGlobal(buf); buf = IntPtr.Zero; bufLen = 0; }
        }
    }

    // 設定ファイル・ログの置き場所。DeskMeter.ps1 から起動したときは exe が無いので、スクリプトのフォルダーを使う
    static class AppPaths
    {
        public static string Dir
        {
            get
            {
                string o = AppDomain.CurrentDomain.GetData("APPBASE_OVERRIDE") as string;
                return string.IsNullOrEmpty(o) ? AppDomain.CurrentDomain.BaseDirectory : o;
            }
        }
        public static bool ScriptHosted { get { return AppDomain.CurrentDomain.GetData("APPBASE_OVERRIDE") is string; } }
    }

    static class Log
    {
        static readonly object gate = new object();
        static int count;

        public static void Write(string where, Exception ex)
        {
            try
            {
                lock (gate)
                {
                    if (count++ > 200) return;
                    string path = Path.Combine(AppPaths.Dir, "deskmeter.log");
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + where + "] " + ex + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }
    }
}

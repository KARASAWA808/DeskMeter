using System;
using System.Runtime.InteropServices;
using System.Text;

namespace DeskMeter
{
    // HWiNFO64 の共有メモリ (Global\HWiNFO_SENS_SM2, Shared Memory Support をオンにすると作られる) から
    // CPU の温度・パッケージ電力・ファン回転数を取り出す。
    // レイアウトは HWiNFO SDK の HWiNFO_SENSORS_SHARED_MEM2 (パックされた構造体)。
    sealed class HwInfo
    {
        const string MapName = @"Global\HWiNFO_SENS_SM2";
        const uint FILE_MAP_READ = 4;
        const uint SIGNATURE = 0x53695748;        // "HWiS" (リトルエンディアン)
        const int STR_LEN = 128, UNIT_LEN = 16;
        const int HEADER_LEN = 44;
        const int SENSOR_LEN = 4 + 4 + STR_LEN + STR_LEN;                        // 264
        const int READING_LEN = 4 + 4 + 4 + STR_LEN + STR_LEN + UNIT_LEN + 8 * 4; // 316
        const int TYPE_TEMP = 1, TYPE_FAN = 3, TYPE_POWER = 5;
        static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(60);

        [StructLayout(LayoutKind.Sequential)]
        struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress, AllocationBase;
            public uint AllocationProtect;
            public ushort PartitionId;
            public IntPtr RegionSize;
            public uint State, Protect, Type;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr OpenFileMapping(uint access, bool inherit, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr MapViewOfFile(IntPtr h, uint access, uint offsetHigh, uint offsetLow, UIntPtr bytes);
        [DllImport("kernel32.dll")]
        static extern bool UnmapViewOfFile(IntPtr p);
        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")]
        static extern UIntPtr VirtualQuery(IntPtr address, out MEMORY_BASIC_INFORMATION info, UIntPtr length);

        // 直近の読み取り結果。Ok = false のときは全て NaN
        public bool Ok;
        public float CpuTemp = float.NaN, CpuPowerW = float.NaN, CpuFanRpm = float.NaN;

        DateTime retryAt = DateTime.MinValue;

        // 5 秒ごとに呼ぶ。共有メモリが無いときは静かに無効のまま 60 秒ごとに再試行する
        public void Poll()
        {
            if (!Ok && DateTime.Now < retryAt) return;
            IntPtr h = OpenFileMapping(FILE_MAP_READ, false, MapName);
            if (h == IntPtr.Zero) { Fail(); return; }
            IntPtr p = IntPtr.Zero;
            try
            {
                p = MapViewOfFile(h, FILE_MAP_READ, 0, 0, UIntPtr.Zero);   // 0 = セクション全体
                if (p == IntPtr.Zero) { Fail(); return; }
                if (!Read(p)) Fail();
            }
            finally
            {
                if (p != IntPtr.Zero) UnmapViewOfFile(p);
                CloseHandle(h);
            }
        }

        void Fail()
        {
            Ok = false;
            CpuTemp = CpuPowerW = CpuFanRpm = float.NaN;
            // 値が止まっているだけ (センサー画面を閉じた、試用期限が切れた等) のときは早めに見直す
            retryAt = DateTime.Now + (stale ? TimeSpan.FromSeconds(10) : RetryAfter);
            stale = false;
        }

        bool stale;
        static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        bool Read(IntPtr p)
        {
            // マップした領域の大きさを調べ、ヘッダーの言う範囲がその中に収まることを確認してから読む
            MEMORY_BASIC_INFORMATION mbi;
            if (VirtualQuery(p, out mbi, (UIntPtr)Marshal.SizeOf(typeof(MEMORY_BASIC_INFORMATION))) == UIntPtr.Zero) return false;
            long region = mbi.RegionSize.ToInt64();
            if (region < HEADER_LEN) return false;

            if ((uint)Marshal.ReadInt32(p, 0) != SIGNATURE) return false;   // HWiNFO 終了後は "DEAD" になる
            // poll_time (UNIX 秒)。HWiNFO が更新を止めると共有メモリは残ったまま古い値になるので、30 秒以上更新が無ければ表示しない
            long poll = Marshal.ReadInt64(p, 12);
            long nowUnix = (long)(DateTime.UtcNow - Epoch).TotalSeconds;
            if (poll > 0 && nowUnix - poll > 30) { stale = true; return false; }
            long sensOff = (uint)Marshal.ReadInt32(p, 20), sensLen = (uint)Marshal.ReadInt32(p, 24), sensN = (uint)Marshal.ReadInt32(p, 28);
            long readOff = (uint)Marshal.ReadInt32(p, 32), readLen = (uint)Marshal.ReadInt32(p, 36), readN = (uint)Marshal.ReadInt32(p, 40);
            if (sensLen < SENSOR_LEN || readLen < READING_LEN || sensN > 4096 || readN > 65536) return false;
            if (sensOff + sensLen * sensN > region || readOff + readLen * readN > region) return false;

            // センサーのうち名前に "CPU" を含むもの
            var isCpu = new bool[sensN];
            for (long i = 0; i < sensN; i++)
            {
                IntPtr e = new IntPtr(p.ToInt64() + sensOff + i * sensLen);
                string name = Str(e, 8 + STR_LEN);           // szSensorNameUser
                if (name.Length == 0) name = Str(e, 8);      // szSensorNameOrig
                isCpu[i] = name.IndexOf("CPU", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            int tempRank = int.MaxValue, powerRank = int.MaxValue, fanRank = int.MaxValue;
            float temp = float.NaN, power = float.NaN, fan = float.NaN;
            for (long j = 0; j < readN; j++)
            {
                IntPtr e = new IntPtr(p.ToInt64() + readOff + j * readLen);
                int type = Marshal.ReadInt32(e, 0);
                if (type != TYPE_TEMP && type != TYPE_POWER && type != TYPE_FAN) continue;
                long sidx = (uint)Marshal.ReadInt32(e, 4);
                bool cpu = sidx < sensN && isCpu[sidx];
                if (!cpu && type != TYPE_FAN) continue;
                string label = Str(e, 12 + STR_LEN);         // szLabelUser
                if (label.Length == 0) label = Str(e, 12);   // szLabelOrig
                int rank;
                switch (type)
                {
                    case TYPE_TEMP:
                        rank = TempRank(label);
                        if (rank < tempRank) { tempRank = rank; temp = Value(e); }
                        break;
                    case TYPE_POWER:
                        rank = PowerRank(label);
                        if (rank < powerRank) { powerRank = rank; power = Value(e); }
                        break;
                    default:
                        // ファンは CPU センサーではなくマザーボードのセンサーに付くので、ラベルだけで判断する
                        if (label.IndexOf("CPU", StringComparison.OrdinalIgnoreCase) < 0) break;
                        rank = Eq(label, "CPU") ? 0 : label.StartsWith("CPU", StringComparison.OrdinalIgnoreCase) ? 1 : 2;
                        if (rank < fanRank) { fanRank = rank; fan = Value(e); }
                        break;
                }
            }
            Ok = true;
            CpuTemp = temp;
            CpuPowerW = power;
            CpuFanRpm = fan;
            return true;
        }

        static int TempRank(string label)
        {
            if (Eq(label, "CPU Package")) return 0;
            if (Eq(label, "CPU (Tctl/Tdie)")) return 1;
            if (Eq(label, "Core Max")) return 2;
            if (Eq(label, "CPU Die (average)")) return 3;
            return 10;   // CPU センサー内の最初の温度
        }

        static int PowerRank(string label)
        {
            if (Eq(label, "CPU Package Power")) return 0;
            if (label.IndexOf("Package Power", StringComparison.OrdinalIgnoreCase) >= 0) return 1;
            return 10;   // CPU センサー内の最初の電力
        }

        static bool Eq(string a, string b) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }

        static float Value(IntPtr e)
        {
            double v = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(e, 4 + 4 + 4 + STR_LEN + STR_LEN + UNIT_LEN));
            return double.IsNaN(v) || double.IsInfinity(v) ? float.NaN : (float)v;
        }

        // ANSI の固定長文字列 (NUL 終端)
        static string Str(IntPtr e, int offset)
        {
            var bytes = new byte[STR_LEN];
            Marshal.Copy(new IntPtr(e.ToInt64() + offset), bytes, 0, STR_LEN);
            int n = Array.IndexOf(bytes, (byte)0);
            if (n < 0) n = STR_LEN;
            return Encoding.Default.GetString(bytes, 0, n).Trim();
        }
    }
}

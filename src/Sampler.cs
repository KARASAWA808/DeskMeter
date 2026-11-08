using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace DeskMeter
{
    // 固定長のリングバッファ (グラフ用の履歴)
    sealed class Hist
    {
        readonly float[] buf;
        int head;
        public int Count;

        public Hist(int cap) { buf = new float[cap]; }
        public int Cap { get { return buf.Length; } }

        public void Add(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) v = 0;
            buf[head] = v;
            head = (head + 1) % buf.Length;
            if (Count < buf.Length) Count++;
        }

        // i = 0 が最も古い値
        public float At(int i)
        {
            return buf[(head - Count + i + buf.Length * 2) % buf.Length];
        }

        public float Max()
        {
            float m = 0;
            for (int i = 0; i < Count; i++) { float v = At(i); if (v > m) m = v; }
            return m;
        }
    }

    sealed class DriveRow
    {
        public string Name, Label;
        public long Total, Free;
    }

    sealed class ProcRow
    {
        public string Name;
        public long Mem;
        public float Cpu;
    }

    // 1日分の予報 (明日以降)
    sealed class DayForecast
    {
        public DateTime Date;
        public int Code;
        public double Hi, Lo, PrecipProb;
    }

    sealed class Weather
    {
        public double Temp, Feels, Hi, Lo, Humidity, Pressure, Wind, WindDir, Uv, PrecipProb;
        public int Code;
        public bool IsDay;
        public DateTime Sunrise, Sunset, Updated;
        public List<DayForecast> Days = new List<DayForecast>();   // 明日から最大3日分
    }

    // サンプラースレッドと描画スレッドで共有する状態。読み書きは lock(このオブジェクト) で行う。
    sealed class MeterState
    {
        public const int HistLen = 60;
        // いまの履歴の容量 (Sampler が HistorySeconds / IntervalMs から決める)。CoreHist を作り直すときに使う
        public int HistCap = HistLen;

        public string CpuName = "CPU";
        public float CpuTotal, CpuMHz;
        public float[] Cores = new float[0];
        public Hist[] CoreHist = new Hist[0];

        public ulong RamTotal, RamAvail, PageTotal, PageUsed;
        // メモリの詳細 (タスクマネージャーの「メモリ」相当)。NaN = 不明
        public double CommitUsed = double.NaN, CommitLimit = double.NaN, Cached = double.NaN, PoolPaged = double.NaN, PoolNonpaged = double.NaN,
            HwReserved = double.NaN, Standby = double.NaN, Modified = double.NaN, FreeMem = double.NaN, Compressed = double.NaN;
        public int ProcessCount, ThreadCount, HandleCount;
        public Hist RamHist = new Hist(HistLen);
        // WMI から 1 回だけ取る静的な情報 (取れるまで null / 0)
        public string MemType, MemFormFactor;
        public int MemSpeedMHz, MemSlotsUsed, MemSlotsTotal, CpuCores, CpuL3KB;
        public bool HwStaticReady;

        public bool GpuOk;
        public long GpuStamp;   // 最後に nvidia-smi から値を受け取った時刻 (GetTickCount64)
        public string GpuName = "GPU";
        public float GpuUtil, GpuTemp, VramUsedMB, VramTotalMB, GpuPowerW, GpuPowerLimitW, GpuClock, GpuMemClock, GpuFan;
        public Hist GpuUtilHist = new Hist(HistLen), GpuTempHist = new Hist(HistLen);

        public string IntIp;
        public double UpBps, DownBps, PeakBps;
        public long SessUp, SessDown, TotUp, TotDown;
        public long PingMs = -1;
        public Hist PingHist = new Hist(60);   // 直近 60 回 (タイムアウトは 1500 で積む)
        public int PingSent, PingLost;
        public Hist UpHist = new Hist(HistLen), DownHist = new Hist(HistLen);

        public List<DriveRow> Drives = new List<DriveRow>();
        // ドライブ文字 ("C:") ごとの読み/書き bytes/s。取れないドライブは入らない
        public Dictionary<string, double[]> DriveIo = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
        public double ReadBps, WriteBps;
        public float DiskActive;
        public Hist ReadHist = new Hist(HistLen), WriteHist = new Hist(HistLen);

        public List<ProcRow> Procs = new List<ProcRow>();      // CPU 使用率の上位
        public List<ProcRow> MemProcs = new List<ProcRow>();   // メモリ使用量の上位
        public Hist ProcHist = new Hist(HistLen);

        public Weather Weather;
        public string WeatherError;

        // HWiNFO64 の共有メモリから (NaN = 不明)。HwInfoOk = false なら HWiNFO 未起動か Shared Memory Support がオフ
        public bool HwInfoOk;
        public float CpuTemp = float.NaN, CpuPowerW = float.NaN, CpuFanRpm = float.NaN;

        // バッテリー (GetSystemPowerStatus)。HasBattery = false ならデスクトップ機
        public bool HasBattery, AcLine, Charging;
        public int BatteryPercent = -1, BatterySecs = -1;   // -1 = 不明
        public Hist BatteryHist = new Hist(HistLen);

        // タイマー (メニューから)。終了時刻 (GetTickCount64)。0 = なし、-1 = 終了を表示中
        public long TimerEndTick;
        public int TimerMinutes;
    }

    sealed class Sampler : IDisposable
    {
        public readonly MeterState S = new MeterState();

        readonly Settings cfg;
        readonly ManualResetEvent stopEvent = new ManualResetEvent(false);
        readonly AutoResetEvent wake = new AutoResetEvent(false);   // 更新間隔の変更などをすぐ反映させるために起こす
        Thread thread;
        // true の間は計測をスキップする (ウィジェットを隠している間・セッションロック中)
        public volatile bool Paused;

        readonly PdhQuery pdh = new PdhQuery();
        IntPtr hCores, hPerf, hFreq, hRead, hWrite, hIdle, hPage, hStandby1, hStandby2, hStandby3, hModified, hFreeZero;
        IntPtr hDriveRead, hDriveWrite;   // ドライブごとの読み書き (LogicalDisk(*))
        readonly List<string> ioNames = new List<string>(16);
        readonly List<double> ioVals = new List<double>(16);
        long nextCsv;
        readonly List<string> coreNames = new List<string>(64);
        readonly List<double> coreValues = new List<double>(64);
        readonly List<KeyValuePair<int, float>> coreSorted = new List<KeyValuePair<int, float>>(64);
        float nominalMHz;

        // アダプターごと (NetworkInterface.Id) の前回の累計バイト数。切替や追加のときに巨大な差分が出ないようにする
        sealed class NetPrev { public long Rx, Tx; public bool Seen; }
        readonly Dictionary<string, NetPrev> netPrev = new Dictionary<string, NetPrev>();
        bool netPrimed;
        // アダプターの列挙は重いので 30 秒ごと (またはネットワーク構成の変化時) にだけ行い、毎回は統計だけを読む
        sealed class NetAdapter { public NetworkInterface Ni; public string Id, Ip; }
        readonly List<NetAdapter> nics = new List<NetAdapter>();
        volatile bool nicsDirty = true;
        long nicsNext;

        Process smi;
        IntPtr job = IntPtr.Zero;
        long smiRetryAt;               // 次に nvidia-smi を起動してよい時刻 (GetTickCount64 基準)
        int smiRetrySec = 30;          // 1 行も出力しないまま終了するたびに倍にする (最大 10 分)
        int smiIntervalSec = 1;        // 起動中の nvidia-smi に渡した -l の値
        volatile bool smiGotLine;      // いまの nvidia-smi から 1 行でも受け取ったか
        bool smiGaveUp, smiFailLogged; // nvidia-smi が無い環境では一度だけ記録して以後は起動しない
        bool smiPausedKill;            // 非表示・ロック中に止めたので、再開時にすぐ起動し直す
        long smiStartedAt;             // いまの nvidia-smi を起動した時刻 (出力が止まったままの検出用)
        bool smiStallLogged;

        readonly HwInfo hwinfo = new HwInfo();

        // nvidia-smi が使えないときの代用: Windows の GPU カウンター (使用率と VRAM 使用量だけ。温度や電力は取れない)
        IntPtr hGpuEng, hGpuMem;
        bool gpuPdhTried;
        string gpuLuid;                // 見ているアダプター (LUID)。専用メモリの使用量が最も多いものを選び、以後はそれに固定する
        readonly List<string> gpuNames = new List<string>();
        readonly List<double> gpuVals = new List<double>();
        readonly Dictionary<string, double> gpuByLuid = new Dictionary<string, double>();

        IntPtr procBuf = IntPtr.Zero;
        int procBufLen;
        Dictionary<long, long> procTimes = new Dictionary<long, long>();
        long procStamp;

        int pingBusy, weatherBusy, drivesBusy;
        long weatherNext;   // 次に天気を取りに行く時刻 (GetTickCount64。時計合わせで戻されても遅れない)
        int histSeconds = -1, histInterval = -1;   // 履歴の容量を決めたときの設定値

        public Sampler(Settings cfg) { this.cfg = cfg; }

        // 1 回の計測が終わるたびに (サンプラースレッドで) 呼ばれる。UI はこれを合図に再描画する
        public event Action Sampled;

        public void Start()
        {
            // システムイベントの購読は UI スレッドで行う (最初に購読したスレッドが通知スレッドになるため、計測スレッドからは購読しない)
            try
            {
                SystemEvents.PowerModeChanged += OnPowerModeChanged;
                NetworkChange.NetworkAddressChanged += OnNetworkChanged;
                NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
            }
            catch (Exception ex) { Log.Write("init.events", ex); }
            thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Name = "DeskMeter sampler";
            thread.Priority = ThreadPriority.BelowNormal;
            thread.Start();
        }

        public void Dispose()
        {
            stopEvent.Set();
            KillSmi();
            try
            {
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
                NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
            }
            catch { }
            if (thread != null) thread.Join(1500);
        }

        // スリープ復帰の直後は、差分で出す値 (通信速度・プロセスの CPU%) が眠っていた時間を含んで膨らむので一度捨てる
        void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.Resume) return;
            netPrimed = false;
            procStamp = 0;
            smiStartedAt = Tick();   // 眠っていた時間を「出力が止まった」と見なさない
            nicsDirty = true;
            // 天気は眠っていた分だけ古いので、落ち着いたころに取り直す
            weatherNext = Tick() + 20000;
            Poke();
        }

        void OnNetworkChanged(object sender, EventArgs e) { nicsDirty = true; }
        void OnNetworkAvailabilityChanged(object sender, NetworkAvailabilityEventArgs e) { nicsDirty = true; }

        static long Tick() { return (long)Native.GetTickCount64(); }

        // 待ち時間を打ち切って次の計測をすぐ始める (更新間隔の変更時など)
        public void Poke()
        {
            wake.Set();
        }

        public void RefreshWeatherNow()
        {
            lock (S) { S.Weather = null; S.WeatherError = null; }
            weatherNext = 0;
        }

        // ---------------------------------------------------------------- main loop

        void Loop()
        {
            try { Init(); }
            catch (Exception ex) { Log.Write("init", ex); }
            var sw = Stopwatch.StartNew();
            long lastNet = sw.ElapsedMilliseconds;
            long lastLoop = 0;
            long nextProc = 0, nextDrives = 0, nextPing = 2000, nextNet = 0, nextHw = 0;
            var handles = new WaitHandle[] { stopEvent, wake };
            while (!stopEvent.WaitOne(0))
            {
                try
                {
                    if (Paused)
                    {
                        // 隠している間・ロック中は計測しない。nvidia-smi も止めて GPU への問い合わせを無くす。500ms ごとに様子を見る
                        if (smi != null) { KillSmi(); smiPausedKill = true; }
                        if (WaitHandle.WaitAny(handles, 500) == 0) break;
                        continue;
                    }
                    if (smiPausedKill) { smiPausedKill = false; smiRetryAt = 0; }
                    int interval = Math.Max(250, Math.Min(10000, cfg.IntervalMs));
                    // グラフの時間幅 (HistorySeconds) と間隔から履歴の長さを決める
                    int secs = Math.Max(30, Math.Min(1800, cfg.HistorySeconds));
                    if (secs != histSeconds || interval != histInterval) ResizeHist(secs, interval);
                    long t0 = sw.ElapsedMilliseconds;
                    // 前回から間隔の 3 倍以上あいていたら (スリープ復帰、停止からの再開)、差分で出す値の基準を捨てる。復帰のイベントより先にここへ来ることがある
                    if (lastLoop > 0 && t0 - lastLoop > interval * 3L + 2000) { netPrimed = false; procStamp = 0; }
                    long pst = Profiler.Enabled ? Stopwatch.GetTimestamp() : 0;

                    Safe("cpu", SampleCpu);
                    Safe("mem", SampleMemory);
                    Safe("battery", SampleBattery);
                    Safe("disk", SampleDiskIo);
                    // ネットワークは間隔が短くても 1 秒に 1 回まで (dt は前回のネット計測からの経過秒)
                    if (t0 >= nextNet)
                    {
                        double dt = (t0 - lastNet) / 1000.0;
                        lastNet = t0;
                        try { SampleNet(dt); } catch (Exception ex) { Log.Write("net", ex); }
                        nextNet = t0 + Math.Max(1000, interval);
                    }
                    Safe("gpu", WatchSmi);
                    // 重めの処理は間隔が短くても一定時間ごとにしか行わない
                    if (t0 >= nextProc) { Safe("proc", SampleProcs); nextProc = t0 + Math.Max(2000, interval); }
                    if (t0 >= nextDrives) { StartDrives(); nextDrives = t0 + Math.Max(30000, interval); }   // 容量は分単位でしか変わらない
                    if (t0 >= nextPing) { StartPing(); nextPing = t0 + Math.Max(5000, interval); }
                    if (t0 >= nextHw) { Safe("hwinfo", SampleHwInfo); nextHw = t0 + 5000; }
                    if (cfg.WeatherEnabled && Tick() >= weatherNext) StartWeather();   // weatherNext は GetTickCount64 基準 (t0 は Stopwatch なので比べない)
                    if (cfg.LogCsv && t0 >= nextCsv) { Safe("csv", WriteCsv); nextCsv = t0 + 60000; }
                    if (Profiler.Enabled) Profiler.Sample(Stopwatch.GetTimestamp() - pst);
                    lastLoop = sw.ElapsedMilliseconds;   // 空き時間を測る (計測自体に時間がかかった分は数えない)
                    Action h = Sampled;
                    if (h != null) h();

                    int wait = interval - (int)(sw.ElapsedMilliseconds - t0);
                    if (wait < 50) wait = 50;
                    // 停止で抜ける。Poke() で起こされたら待ちを打ち切って次の計測へ
                    if (WaitHandle.WaitAny(handles, wait) == 0) break;
                }
                catch (Exception ex)
                {
                    // 計測ループ自体は落とさない。少し待って続ける
                    Log.Write("loop", ex);
                    if (stopEvent.WaitOne(1000)) break;
                }
            }
            pdh.Dispose();
        }

        static void Safe(string name, Action a)
        {
            try { a(); }
            catch (Exception ex) { Log.Write(name, ex); }
        }

        // グラフが表す時間 (HistorySeconds) が更新間隔に左右されないよう、履歴の容量を決め直す。既存のデータは捨てる
        void ResizeHist(int secs, int interval)
        {
            histSeconds = secs;
            histInterval = interval;
            int cap = Math.Max(2, Math.Min(7200, secs * 1000 / interval));
            // プロセス一覧は最短でも 2 秒、ネットワークは 1 秒ごとにしか取らないので、その間隔で同じ時間幅になる本数にする
            int procCap = Math.Max(2, secs * 1000 / Math.Max(2000, interval));
            int netCap = Math.Max(2, secs * 1000 / Math.Max(1000, interval));
            lock (S)
            {
                S.HistCap = cap;
                S.GpuUtilHist = new Hist(cap); S.GpuTempHist = new Hist(cap);
                S.UpHist = new Hist(netCap); S.DownHist = new Hist(netCap);
                S.ReadHist = new Hist(cap); S.WriteHist = new Hist(cap);
                S.ProcHist = new Hist(procCap);
                S.RamHist = new Hist(cap);
                S.BatteryHist = new Hist(cap);
                for (int i = 0; i < S.CoreHist.Length; i++) S.CoreHist[i] = new Hist(cap);
            }
        }

        // HWiNFO64 の共有メモリ (無ければ HwInfo 側で 60 秒ごとに再試行)
        void SampleHwInfo()
        {
            hwinfo.Poll();
            lock (S)
            {
                S.HwInfoOk = hwinfo.Ok;
                S.CpuTemp = hwinfo.CpuTemp;
                S.CpuPowerW = hwinfo.CpuPowerW;
                S.CpuFanRpm = hwinfo.CpuFanRpm;
            }
        }

        void Init()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                {
                    if (k != null)
                    {
                        string name = Convert.ToString(k.GetValue("ProcessorNameString", "CPU"));
                        name = name.Replace("(R)", "").Replace("(TM)", "").Replace("Intel ", "").Replace(" CPU", "").Trim();
                        while (name.Contains("  ")) name = name.Replace("  ", " ");
                        nominalMHz = Convert.ToSingle(k.GetValue("~MHz", 0), CultureInfo.InvariantCulture);
                        lock (S) { S.CpuName = name; S.CpuMHz = nominalMHz; }
                    }
                }
            }
            catch (Exception ex) { Log.Write("init.cpuname", ex); }

            StartSmi();
            StartStaticInfo();

            // パフォーマンスカウンター (PDH)。"% Processor Utility" はタスクマネージャーと同じ基準の使用率。
            if (pdh.Open())
            {
                hCores = pdh.Add(@"\Processor Information(*)\% Processor Utility");
                if (hCores == IntPtr.Zero) hCores = pdh.Add(@"\Processor Information(*)\% Processor Time");
                if (hCores == IntPtr.Zero) hCores = pdh.Add(@"\Processor(*)\% Processor Time");
                if (hCores == IntPtr.Zero) Log.Write("pdh", new InvalidOperationException("プロセッサのカウンターを追加できません (カウンターが壊れているときは管理者で lodctr /R を実行)"));
                hPerf = pdh.Add(@"\Processor Information(_Total)\% Processor Performance");
                hFreq = pdh.Add(@"\Processor Information(_Total)\Processor Frequency");
                hRead = pdh.Add(@"\PhysicalDisk(_Total)\Disk Read Bytes/sec");
                hWrite = pdh.Add(@"\PhysicalDisk(_Total)\Disk Write Bytes/sec");
                hIdle = pdh.Add(@"\PhysicalDisk(_Total)\% Idle Time");
                hDriveRead = pdh.Add(@"\LogicalDisk(*)\Disk Read Bytes/sec");
                hDriveWrite = pdh.Add(@"\LogicalDisk(*)\Disk Write Bytes/sec");
                hPage = pdh.Add(@"\Paging File(_Total)\% Usage");
                hStandby1 = pdh.Add(@"\Memory\Standby Cache Normal Priority Bytes");
                hStandby2 = pdh.Add(@"\Memory\Standby Cache Reserve Bytes");
                hStandby3 = pdh.Add(@"\Memory\Standby Cache Core Bytes");
                hModified = pdh.Add(@"\Memory\Modified Page List Bytes");
                hFreeZero = pdh.Add(@"\Memory\Free & Zero Page List Bytes");
                pdh.Collect();
            }
            else Log.Write("pdh", new InvalidOperationException("PdhOpenQuery failed"));
        }

        static float Val(double v)
        {
            return double.IsNaN(v) || double.IsInfinity(v) || v < 0 ? 0 : (float)v;
        }

        // ---------------------------------------------------------------- CPU

        void SampleCpu()
        {
            pdh.Collect();   // このあと SampleMemory / SampleDiskIo も同じ収集結果を読む
            int n = pdh.Array(hCores, coreNames, coreValues);
            if (n == 0) return;

            List<KeyValuePair<int, float>> list = coreSorted;
            list.Clear();
            float total = 0;
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                string name = coreNames[i];
                if (!double.IsNaN(coreValues[i])) any = true;
                float v = Pct(Val(coreValues[i]));
                if (string.Equals(name, "_Total", StringComparison.OrdinalIgnoreCase)) { total = v; continue; }
                if (name.IndexOf("_Total", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                int group = 0, num = 0;
                int comma = name.IndexOf(',');
                if (comma >= 0)
                {
                    int.TryParse(name.Substring(0, comma), out group);
                    int.TryParse(name.Substring(comma + 1), out num);
                }
                else int.TryParse(name, out num);
                list.Add(new KeyValuePair<int, float>(group * 10000 + num, v));
            }
            if (!any) return;   // 初回は差分が無いので値が出ない
            list.Sort(delegate(KeyValuePair<int, float> a, KeyValuePair<int, float> b) { return a.Key.CompareTo(b.Key); });

            float mhz = nominalMHz;
            float perf = Val(pdh.Value(hPerf)), freq = Val(pdh.Value(hFreq));
            if (perf > 0 && freq > 0) mhz = perf * freq / 100f;

            lock (S)
            {
                if (S.Cores.Length != list.Count)
                {
                    S.Cores = new float[list.Count];
                    S.CoreHist = new Hist[list.Count];
                    for (int i = 0; i < list.Count; i++) S.CoreHist[i] = new Hist(S.HistCap);
                }
                for (int i = 0; i < list.Count; i++)
                {
                    S.Cores[i] = list[i].Value;
                    S.CoreHist[i].Add(list[i].Value);
                }
                S.CpuTotal = total;
                S.CpuMHz = mhz;
            }
        }

        static float Pct(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v) || v < 0) return 0;
            return v > 100 ? 100 : v;
        }

        // ---------------------------------------------------------------- memory / disk

        void SampleMemory()
        {
            var m = new Native.MEMORYSTATUSEX();
            m.dwLength = (uint)Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX));
            if (!Native.GlobalMemoryStatusEx(ref m)) return;
            ulong pageTotal = m.ullTotalPageFile > m.ullTotalPhys ? m.ullTotalPageFile - m.ullTotalPhys : 0;
            float pagePct = Val(pdh.Value(hPage));

            // タスクマネージャーの「メモリ」と同じ内訳
            var pi = new Native.PERFORMANCE_INFORMATION();
            pi.cb = Marshal.SizeOf(typeof(Native.PERFORMANCE_INFORMATION));
            bool piOk = Native.GetPerformanceInfo(ref pi, pi.cb);
            double page = piOk ? (double)pi.PageSize.ToUInt64() : 4096;
            double standby = Sum3(pdh.Value(hStandby1), pdh.Value(hStandby2), pdh.Value(hStandby3));
            double modified = pdh.Value(hModified), freeZero = pdh.Value(hFreeZero);
            ulong installedKB;
            double hwReserved = double.NaN;
            if (Native.GetPhysicallyInstalledSystemMemory(out installedKB) && installedKB * 1024.0 >= m.ullTotalPhys)
                hwReserved = installedKB * 1024.0 - m.ullTotalPhys;

            lock (S)
            {
                S.RamTotal = m.ullTotalPhys;
                S.RamAvail = m.ullAvailPhys;
                S.PageTotal = pageTotal;
                S.PageUsed = (ulong)(pageTotal * (double)Math.Min(100f, pagePct) / 100.0);
                if (piOk)
                {
                    S.CommitUsed = pi.CommitTotal.ToUInt64() * page;
                    S.CommitLimit = pi.CommitLimit.ToUInt64() * page;
                    S.PoolPaged = pi.KernelPaged.ToUInt64() * page;
                    S.PoolNonpaged = pi.KernelNonpaged.ToUInt64() * page;
                    S.ProcessCount = pi.ProcessCount;
                    S.ThreadCount = pi.ThreadCount;
                    S.HandleCount = pi.HandleCount;
                }
                S.Standby = standby;
                S.Modified = modified;
                S.FreeMem = freeZero;
                S.Cached = double.IsNaN(standby) || double.IsNaN(modified) ? double.NaN : standby + modified;
                S.HwReserved = hwReserved;
                S.RamHist.Add(m.ullTotalPhys > 0 ? (float)(100.0 * (m.ullTotalPhys - m.ullAvailPhys) / m.ullTotalPhys) : 0);
            }
        }

        static double Sum3(double a, double b, double c)
        {
            if (double.IsNaN(a) && double.IsNaN(b) && double.IsNaN(c)) return double.NaN;
            return (double.IsNaN(a) ? 0 : a) + (double.IsNaN(b) ? 0 : b) + (double.IsNaN(c) ? 0 : c);
        }

        // メモリの種類・速度・スロット数と CPU のコア数を WMI から 1 回だけ取る (数百 ms かかるので別スレッド)
        void StartStaticInfo()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                string type = null, form = null;
                int speed = 0, used = 0, total = 0, cores = 0, l3 = 0;
                try
                {
                    using (var s = new System.Management.ManagementObjectSearcher("SELECT Speed, FormFactor, SMBIOSMemoryType, MemoryType FROM Win32_PhysicalMemory"))
                        foreach (System.Management.ManagementObject mo in s.Get())
                        {
                            used++;
                            int sp = ToInt(mo["Speed"]);
                            if (sp > speed) speed = sp;
                            if (type == null) type = MemTypeName(ToInt(mo["SMBIOSMemoryType"]), ToInt(mo["MemoryType"]));
                            if (form == null) form = FormFactorName(ToInt(mo["FormFactor"]));
                        }
                    using (var s = new System.Management.ManagementObjectSearcher("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray"))
                        foreach (System.Management.ManagementObject mo in s.Get()) total += ToInt(mo["MemoryDevices"]);
                    using (var s = new System.Management.ManagementObjectSearcher("SELECT NumberOfCores, L3CacheSize FROM Win32_Processor"))
                        foreach (System.Management.ManagementObject mo in s.Get()) { cores += ToInt(mo["NumberOfCores"]); l3 += ToInt(mo["L3CacheSize"]); }
                }
                catch (Exception ex) { Log.Write("wmi", ex); }
                lock (S)
                {
                    S.MemType = type; S.MemFormFactor = form; S.MemSpeedMHz = speed;
                    S.MemSlotsUsed = used; S.MemSlotsTotal = total; S.CpuCores = cores; S.CpuL3KB = l3;
                    S.HwStaticReady = true;
                }
            });
        }

        static int ToInt(object o)
        {
            try { return o == null ? 0 : Convert.ToInt32(o, CultureInfo.InvariantCulture); }
            catch { return 0; }
        }

        static string MemTypeName(int smbios, int legacy)
        {
            switch (smbios)
            {
                case 20: return "DDR";
                case 21: return "DDR2";
                case 24: return "DDR3";
                case 26: return "DDR4";
                case 27: return "LPDDR";
                case 28: return "LPDDR2";
                case 29: return "LPDDR3";
                case 30: return "LPDDR4";
                case 34: return "DDR5";
                case 35: return "LPDDR5";
            }
            switch (legacy)
            {
                case 20: return "DDR";
                case 21: return "DDR2";
                case 24: return "DDR3";
                case 26: return "DDR4";
            }
            return null;
        }

        static string FormFactorName(int f)
        {
            switch (f)
            {
                case 8: return "DIMM";
                case 12: return "SODIMM";
                case 9: return "TSOP";
                case 11: return "RIMM";
                case 13: return "SRIMM";
                case 0: return null;
                default: return "その他";
            }
        }

        void SampleDiskIo()
        {
            float r = Val(pdh.Value(hRead)), w = Val(pdh.Value(hWrite));
            double idle = pdh.Value(hIdle);
            float active = double.IsNaN(idle) ? 0 : Pct(100f - (float)idle);
            lock (S)
            {
                S.ReadBps = r; S.WriteBps = w; S.DiskActive = active;
                S.ReadHist.Add(r); S.WriteHist.Add(w);
            }
            // ドライブ文字ごと (インスタンス名 "C:" のものだけ。HarddiskVolume や _Total は除く)
            if (hDriveRead != IntPtr.Zero && hDriveWrite != IntPtr.Zero)
            {
                var io = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
                int n = pdh.Array(hDriveRead, ioNames, ioVals);
                for (int i = 0; i < n; i++)
                    if (ioNames[i].Length == 2 && ioNames[i][1] == ':') io[ioNames[i]] = new double[] { Val(ioVals[i]), 0 };
                n = pdh.Array(hDriveWrite, ioNames, ioVals);
                for (int i = 0; i < n; i++)
                {
                    double[] v;
                    if (io.TryGetValue(ioNames[i], out v)) v[1] = Val(ioVals[i]);
                }
                lock (S) S.DriveIo = io;
            }
        }

        void SampleBattery()
        {
            Native.SYSTEM_POWER_STATUS ps;
            if (!Native.PowerStatus(out ps)) return;
            bool has = (ps.BatteryFlag & 128) == 0 && ps.BatteryFlag != 255;
            lock (S)
            {
                S.HasBattery = has;
                S.AcLine = ps.ACLineStatus == 1;
                S.Charging = has && (ps.BatteryFlag & 8) != 0;
                S.BatteryPercent = has && ps.BatteryLifePercent <= 100 ? ps.BatteryLifePercent : -1;
                S.BatterySecs = has && ps.BatteryLifeTime >= 0 ? ps.BatteryLifeTime : -1;
                if (has && S.BatteryPercent >= 0) S.BatteryHist.Add(S.BatteryPercent);
            }
        }

        // 1 分ごとに主な値を CSV に追記する (障害の調査や消費の把握用)
        void WriteCsv()
        {
            string path = Path.Combine(AppPaths.DataDir, "deskmeter-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".csv");
            var sb = new StringBuilder();
            if (!File.Exists(path)) sb.AppendLine("time,cpu_pct,cpu_mhz,mem_used_mb,mem_total_mb,gpu_pct,gpu_temp_c,vram_used_mb,net_up_bps,net_down_bps,disk_read_bps,disk_write_bps,ping_ms,cpu_temp_c,cpu_power_w,battery_pct");
            lock (S)
            {
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                Csv(sb, S.CpuTotal, "0.#"); Csv(sb, S.CpuMHz, "0");
                Csv(sb, S.RamTotal > 0 ? (S.RamTotal - S.RamAvail) / 1048576.0 : double.NaN, "0"); Csv(sb, S.RamTotal > 0 ? S.RamTotal / 1048576.0 : double.NaN, "0");
                Csv(sb, S.GpuOk ? S.GpuUtil : float.NaN, "0"); Csv(sb, S.GpuOk ? S.GpuTemp : float.NaN, "0"); Csv(sb, S.GpuOk ? S.VramUsedMB : float.NaN, "0");
                Csv(sb, S.UpBps, "0"); Csv(sb, S.DownBps, "0"); Csv(sb, S.ReadBps, "0"); Csv(sb, S.WriteBps, "0");
                Csv(sb, S.PingMs >= 0 ? S.PingMs : double.NaN, "0");
                Csv(sb, S.CpuTemp, "0"); Csv(sb, S.CpuPowerW, "0.#");
                Csv(sb, S.HasBattery && S.BatteryPercent >= 0 ? S.BatteryPercent : double.NaN, "0");
            }
            sb.AppendLine();
            File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
        }

        static void Csv(StringBuilder sb, double v, string fmt)
        {
            sb.Append(',');
            if (!double.IsNaN(v) && !double.IsInfinity(v)) sb.Append(v.ToString(fmt, CultureInfo.InvariantCulture));
        }

        // ドライブの問い合わせは応答しないドライブ (切れたネットワーク越しの VHD など) で何十秒も止まることがあるので、計測スレッドから外す
        void StartDrives()
        {
            if (Interlocked.CompareExchange(ref drivesBusy, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { SampleDrives(); }
                catch (Exception ex) { Log.Write("drives", ex); }
                Interlocked.Exchange(ref drivesBusy, 0);
            });
        }

        void SampleDrives()
        {
            var rows = new List<DriveRow>();
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                try
                {
                    if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                    var row = new DriveRow();
                    row.Name = d.Name.TrimEnd('\\');
                    row.Label = d.VolumeLabel;
                    row.Total = d.TotalSize;
                    row.Free = d.TotalFreeSpace;
                    rows.Add(row);
                }
                catch { }
            }
            lock (S) S.Drives = rows;
        }

        // ---------------------------------------------------------------- network

        void SampleNet(double dt)
        {
            long rx = 0, tx = 0, dRx = 0, dTx = 0;
            string ip = null;
            long now = Tick();
            if (nicsDirty || now >= nicsNext) RefreshAdapters();
            foreach (NetPrev v in netPrev.Values) v.Seen = false;
            foreach (NetAdapter a in nics)
            {
                try
                {
                    IPInterfaceStatistics st = a.Ni.GetIPStatistics();   // 統計は呼ぶたびに OS から取り直される
                    long r = st.BytesReceived, t = st.BytesSent;
                    rx += r;
                    tx += t;
                    // 差分はアダプター単位で取る。新しく現れたアダプターは今回は差分なし (次回から)
                    NetPrev prev;
                    if (netPrev.TryGetValue(a.Id, out prev))
                    {
                        if (r >= prev.Rx && t >= prev.Tx) { dRx += r - prev.Rx; dTx += t - prev.Tx; }
                        prev.Rx = r; prev.Tx = t; prev.Seen = true;
                    }
                    else
                    {
                        prev = new NetPrev();
                        prev.Rx = r; prev.Tx = t; prev.Seen = true;
                        netPrev[a.Id] = prev;
                    }
                    if (ip == null) ip = a.Ip;
                }
                catch (Exception ex)
                {
                    // 抜かれたアダプターなど。次回は列挙し直す
                    nicsDirty = true;
                    Log.Write("net." + a.Ni.Name, ex);
                }
            }
            // 消えたアダプターは忘れる (戻ってきたときに巨大な差分を出さないため)
            var gone = new List<string>();
            foreach (KeyValuePair<string, NetPrev> kv in netPrev) if (!kv.Value.Seen) gone.Add(kv.Key);
            foreach (string id in gone) netPrev.Remove(id);

            lock (S)
            {
                double down = 0, up = 0;
                if (netPrimed && dt > 0.05)
                {
                    down = dRx / dt;
                    up = dTx / dt;
                    S.SessDown += dRx;
                    S.SessUp += dTx;
                }
                netPrimed = true;
                S.DownBps = down; S.UpBps = up;
                if (down > S.PeakBps) S.PeakBps = down;
                if (up > S.PeakBps) S.PeakBps = up;
                S.TotDown = rx; S.TotUp = tx;
                S.IntIp = ip;
                S.DownHist.Add((float)down);
                S.UpHist.Add((float)up);
            }
        }

        // 外に出られるアダプター (ゲートウェイがあり、ループバック・トンネル以外) を列挙し直す
        // VPN 中は VPN アダプターと物理アダプターの両方にゲートウェイがあり同じ通信を 2 回数えてしまうので、
        // OS がインターネット向けの経路に使うアダプター (GetBestInterface) だけに絞る。分からなければ従来どおり全部足す
        void RefreshAdapters()
        {
            nicsDirty = false;
            nicsNext = Tick() + 30000;
            uint best = 0;
            bool haveBest = false;
            try { haveBest = Native.GetBestInterface(0x01010101u, out best) == 0; }   // 1.1.1.1 への経路
            catch { }
            CollectAdapters(haveBest, best);
            if (nics.Count == 0 && haveBest) CollectAdapters(false, 0);
        }

        void CollectAdapters(bool onlyBest, uint best)
        {
            nics.Clear();
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                try
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    IPInterfaceProperties props = ni.GetIPProperties();
                    if (props.GatewayAddresses.Count == 0) continue;   // 仮想スイッチなど外に出ないアダプターは除外
                    if (onlyBest)
                    {
                        try { if ((uint)props.GetIPv4Properties().Index != best) continue; }
                        catch { continue; }
                    }
                    var a = new NetAdapter();
                    a.Ni = ni;
                    a.Id = ni.Id;
                    string v6 = null;
                    foreach (UnicastIPAddressInformation u in props.UnicastAddresses)
                    {
                        if (u.Address.AddressFamily == AddressFamily.InterNetwork) { a.Ip = u.Address.ToString(); break; }
                        if (v6 == null && u.Address.AddressFamily == AddressFamily.InterNetworkV6 && !u.Address.IsIPv6LinkLocal && !u.Address.IsIPv6SiteLocal) v6 = u.Address.ToString();
                    }
                    if (a.Ip == null) a.Ip = v6;   // IPv6 だけの回線では「未接続」にしない
                    nics.Add(a);
                }
                catch (Exception ex) { Log.Write("net." + ni.Name, ex); }   // 1 つのアダプターで失敗しても他は数える
            }
        }

        void StartPing()
        {
            string host = cfg.PingHost;
            if (string.IsNullOrWhiteSpace(host)) { lock (S) S.PingMs = -1; return; }
            if (Interlocked.CompareExchange(ref pingBusy, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                long ms = -1;
                try
                {
                    using (var p = new Ping())
                    {
                        PingReply r = p.Send(host, 1500);
                        if (r != null && r.Status == IPStatus.Success) ms = r.RoundtripTime;
                    }
                }
                catch { }
                lock (S)
                {
                    S.PingMs = ms;
                    S.PingSent++;
                    if (ms < 0) S.PingLost++;
                    S.PingHist.Add(ms >= 0 ? ms : 1500);   // タイムアウトは上限値で積んで、グラフでは天井に張り付く
                }
                Interlocked.Exchange(ref pingBusy, 0);
            });
        }

        // ---------------------------------------------------------------- processes

        // NtQuerySystemInformation(SystemProcessInformation) で全プロセスを1回のシステムコールで取得する。
        // オフセットは x64 の SYSTEM_PROCESS_INFORMATION のもの (このアプリは x64 でビルドする)。
        void SampleProcs()
        {
            const int STATUS_INFO_LENGTH_MISMATCH = unchecked((int)0xC0000004);
            if (IntPtr.Size != 8) return;   // 下のオフセットは 64 ビット版の構造体のもの
            if (procBuf == IntPtr.Zero)
            {
                procBufLen = 1 << 20;
                procBuf = Marshal.AllocHGlobal(procBufLen);
            }
            int need, status, guard = 0;
            while ((status = Native.NtQuerySystemInformation(5, procBuf, procBufLen, out need)) == STATUS_INFO_LENGTH_MISMATCH && guard++ < 5)
            {
                Marshal.FreeHGlobal(procBuf);
                procBuf = IntPtr.Zero; procBufLen = 0;   // 確保に失敗しても解放済みの領域を指したままにしない
                procBuf = Marshal.AllocHGlobal(need + (1 << 18));
                procBufLen = need + (1 << 18);
            }
            if (status != 0) return;

            long stamp = Stopwatch.GetTimestamp();
            double elapsed = procStamp == 0 ? 0 : (stamp - procStamp) * 1e7 / Stopwatch.Frequency;   // 100ns 単位
            procStamp = stamp;

            // 論理プロセッサ数はプロセッサグループをまたいで数える (Environment.ProcessorCount は 64 で頭打ち → 65 以上の PC で使用率が 2 倍になる)
            int cpus = S.Cores.Length;
            if (cpus <= 0) cpus = Native.ActiveProcessorCount();
            var times = new Dictionary<long, long>(512);
            var agg = new Dictionary<string, ProcRow>(256, StringComparer.OrdinalIgnoreCase);
            long basePtr = procBuf.ToInt64(), off = 0;
            float total = 0;
            double compressed = double.NaN;
            while (true)
            {
                IntPtr p = new IntPtr(basePtr + off);
                int next = Marshal.ReadInt32(p, 0x00);
                long pid = Marshal.ReadInt64(p, 0x50);
                if (pid != 0)
                {
                    long priv = Marshal.ReadInt64(p, 0x08);
                    long t = Marshal.ReadInt64(p, 0x28) + Marshal.ReadInt64(p, 0x30);
                    int nameLen = (ushort)Marshal.ReadInt16(p, 0x38);
                    IntPtr nameBuf = Marshal.ReadIntPtr(p, 0x40);
                    string name = nameLen > 0 && nameBuf != IntPtr.Zero ? Marshal.PtrToStringUni(nameBuf, nameLen / 2) : "System";
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
                    if (name == "MemCompression") compressed = priv;   // 圧縮メモリの量 (タスクマネージャーの「圧縮」)

                    times[pid] = t;
                    float cpu = 0;
                    long old;
                    if (elapsed > 0 && procTimes.TryGetValue(pid, out old) && t >= old)
                        cpu = (float)((t - old) / elapsed * 100.0 / cpus);

                    ProcRow row;
                    if (!agg.TryGetValue(name, out row))
                    {
                        row = new ProcRow();
                        row.Name = name;
                        agg[name] = row;
                    }
                    row.Cpu += cpu;
                    row.Mem += priv;
                    total += cpu;
                }
                if (next == 0) break;
                off += next;
            }
            procTimes = times;

            var list = new List<ProcRow>(agg.Values);
            list.Sort(delegate(ProcRow a, ProcRow b) { return b.Mem.CompareTo(a.Mem); });
            var byMem = list.GetRange(0, Math.Min(10, list.Count));
            list.Sort(delegate(ProcRow a, ProcRow b)
            {
                int c = b.Cpu.CompareTo(a.Cpu);
                return c != 0 ? c : b.Mem.CompareTo(a.Mem);
            });
            if (list.Count > 10) list.RemoveRange(10, list.Count - 10);
            lock (S)
            {
                S.Procs = list;
                S.MemProcs = byMem;
                S.Compressed = compressed;
                S.ProcHist.Add(Math.Min(100f, total));
            }
        }

        // ---------------------------------------------------------------- GPU (nvidia-smi)

        void StartSmi()
        {
            if (smiGaveUp) return;
            // NVIDIA 以外の環境の表示を NVIDIA 機で確かめる用: nvidia-smi を使わず Windows のカウンターだけにする
            if (Environment.GetEnvironmentVariable("DESKMETER_NO_SMI") == "1") { smiGaveUp = true; return; }
            // 古いプロセスオブジェクトは置き換える前に片付ける
            Process old = smi;
            smi = null;
            if (old != null)
            {
                try { if (!old.HasExited) old.Kill(); } catch { }
                try { old.Dispose(); } catch { }
            }
            try
            {
                // 問い合わせの周期は更新間隔に合わせる (1 秒未満の間隔でも 1 秒)
                smiIntervalSec = SmiInterval();
                smiGotLine = false;
                var psi = new ProcessStartInfo("nvidia-smi.exe",
                    "-i 0 --query-gpu=name,utilization.gpu,temperature.gpu,memory.used,memory.total,power.draw,power.limit,clocks.gr,clocks.mem,fan.speed --format=csv,noheader,nounits -l " + smiIntervalSec);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                var p = new Process();
                p.StartInfo = psi;
                p.OutputDataReceived += OnSmiLine;
                p.Start();
                try
                {
                    // このアプリが異常終了しても nvidia-smi が残らないようにジョブに入れる
                    if (job == IntPtr.Zero)
                    {
                        job = Native.CreateJobObject(IntPtr.Zero, null);
                        var info = new Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                        info.Basic.LimitFlags = 0x2000;   // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                        Native.SetInformationJobObject(job, 9, ref info, Marshal.SizeOf(typeof(Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION)));
                    }
                    Native.AssignProcessToJobObject(job, p.Handle);
                    p.PriorityClass = ProcessPriorityClass.BelowNormal;
                }
                catch { }
                p.BeginOutputReadLine();
                smi = p;
                smiStartedAt = Tick();
            }
            catch (Exception ex)
            {
                smi = null;
                var w32 = ex as System.ComponentModel.Win32Exception;
                if (w32 != null && w32.NativeErrorCode == 2)
                {
                    // nvidia-smi.exe が無い (NVIDIA 以外の環境)。一度だけ記録して以後は試さない
                    smiGaveUp = true;
                    Log.Write("nvidia-smi (not found, GPU panel disabled)", ex);
                    return;
                }
                // それ以外の失敗は記録は初回だけにして、再試行の間隔を倍々で伸ばす (最大 10 分)
                if (!smiFailLogged) { smiFailLogged = true; Log.Write("nvidia-smi", ex); }
                smiRetryAt = Tick() + smiRetrySec * 1000L;
                smiRetrySec = Math.Min(600, smiRetrySec * 2);
                return;
            }
            // 起動できても、出力が無いまま終了した場合は WatchSmi 側で間隔を伸ばす。再起動はこの間隔より速くしない
            smiRetryAt = Tick() + smiRetrySec * 1000L;
        }

        int SmiInterval()
        {
            int s = Math.Max(1, Math.Min(10, cfg.IntervalMs / 1000));
            // バッテリー駆動中は 10 秒ごとにする (ハイブリッド GPU のノートでは nvidia-smi の問い合わせが dGPU を起こし続けて電池を減らす)
            return Native.OnBattery() ? 10 : s;
        }

        void WatchSmi()
        {
            Process p = smi;   // UI スレッドの KillSmi と競合しないようローカルにコピーしてから見る
            bool dead;
            try { dead = p == null || p.HasExited; }
            catch { dead = true; }
            long now = Tick();
            if (dead && p != null)
            {
                // 終了を初めて検出した。1 行も出力しないまま終わった (ドライバーだけ残っている等) なら再試行を倍々で遅らせる
                smi = null;
                try { p.Dispose(); } catch { }
                if (!smiGotLine)
                {
                    smiRetrySec = Math.Min(600, smiRetrySec * 2);
                    if (smiRetrySec >= 600 && !smiFailLogged)
                    {
                        smiFailLogged = true;
                        Log.Write("nvidia-smi exits without output; retrying every 10 minutes", null);
                    }
                }
                smiRetryAt = now + smiRetrySec * 1000L;
            }
            else if (!dead && now - Math.Max(smiStartedAt, GpuStampValue()) > Math.Max(30000, smiIntervalSec * 5000L))
            {
                // 生きているのに出力が止まった (ドライバーのリセット、スリープ復帰後に NVML が固まる等)。終了させて起動し直す。
                // 1 行も出さないまま固まるなら、出力なしで終了したときと同じく再試行を倍々で遅らせる
                KillSmi();
                if (smiGotLine)
                {
                    if (!smiStallLogged) { smiStallLogged = true; Log.Write("nvidia-smi stopped reporting; restarting", null); }
                    smiRetryAt = now;
                }
                else
                {
                    smiRetrySec = Math.Min(600, smiRetrySec * 2);
                    smiRetryAt = now + smiRetrySec * 1000L;
                }
                dead = true;
            }
            else if (!dead && smiIntervalSec != SmiInterval())
            {
                StartSmi();   // 更新間隔が変わったので問い合わせの周期を合わせ直す
                dead = false;
            }
            if (dead && !smiGaveUp && now >= smiRetryAt && !stopEvent.WaitOne(0)) StartSmi();
            // nvidia-smi が動いていない間 (NVIDIA 以外の GPU、または起動に失敗中) は Windows の GPU カウンターで代用する。
            // nvidia-smi が戻って値が来ていれば、プロセスごとに列挙する重いカウンターは外す
            if (smi == null) SampleGpuPdh();
            else if (gpuPdhTried && smiGotLine) ReleaseGpuPdh();
            lock (S)
            {
                if (S.GpuOk && now - S.GpuStamp > smiIntervalSec * 3000L + 3000) S.GpuOk = false;
                // 履歴は他のグラフと同じ間隔で積む
                S.GpuUtilHist.Add(S.GpuOk ? S.GpuUtil : 0);
                S.GpuTempHist.Add(S.GpuOk ? S.GpuTemp : 0);
            }
        }

        // ---------------------------------------------------------------- GPU (PDH。タスクマネージャーの「3D」と同じ数え方)

        long GpuStampValue() { lock (S) return S.GpuStamp; }

        void ReleaseGpuPdh()
        {
            pdh.Remove(hGpuEng);
            pdh.Remove(hGpuMem);
            hGpuEng = hGpuMem = IntPtr.Zero;
            gpuPdhTried = false;
            gpuLuid = null;
        }

        void SampleGpuPdh()
        {
            if (!gpuPdhTried)
            {
                gpuPdhTried = true;
                // インスタンスは pid_1234_luid_0x00000000_0x0000C7F1_phys_0_eng_0_engtype_3D のようにプロセスごと。
                // ワイルドカードで足しておけば Collect のたびに列挙し直される (Windows 10 1709 以降)
                hGpuEng = pdh.Add(@"\GPU Engine(*)\Utilization Percentage");
                hGpuMem = pdh.Add(@"\GPU Adapter Memory(*)\Dedicated Usage");
                if (hGpuEng != IntPtr.Zero) StartGpuStaticInfo();
                else Log.Write("gpu", new InvalidOperationException("GPU のカウンターがありません (nvidia-smi も無いので GPU パネルは表示できません)"));
            }
            if (hGpuEng == IntPtr.Zero) return;

            // アダプターごとの専用メモリ使用量 (インスタンスは luid_0x..._0x..._phys_0)
            gpuByLuid.Clear();
            int m = pdh.Array(hGpuMem, gpuNames, gpuVals);
            for (int i = 0; i < m; i++)
            {
                string luid = Luid(gpuNames[i]);
                if (luid == null || double.IsNaN(gpuVals[i])) continue;
                double v;
                gpuByLuid.TryGetValue(luid, out v);
                gpuByLuid[luid] = Math.Max(v, gpuVals[i]);
            }
            if (gpuLuid == null || !gpuByLuid.ContainsKey(gpuLuid))
            {
                gpuLuid = null;
                double most = -1;
                foreach (KeyValuePair<string, double> kv in gpuByLuid) if (kv.Value > most) { most = kv.Value; gpuLuid = kv.Key; }
            }
            double dedicated = double.NaN;
            if (gpuLuid != null) gpuByLuid.TryGetValue(gpuLuid, out dedicated);

            // 3D エンジンの使用率をプロセス分だけ足す (タスクマネージャーと同じ)。初回は差分が無いので NaN
            double util = double.NaN;
            int n = pdh.Array(hGpuEng, gpuNames, gpuVals);
            for (int i = 0; i < n; i++)
            {
                string name = gpuNames[i];
                if (double.IsNaN(gpuVals[i]) || name.IndexOf("engtype_3D", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (gpuLuid == null || Luid(name) != gpuLuid) continue;
                util = (double.IsNaN(util) ? 0 : util) + gpuVals[i];
            }
            lock (S)
            {
                if (gpuLuid == null || double.IsNaN(util)) { S.GpuOk = false; return; }
                S.GpuUtil = (float)Math.Min(100, util);
                S.VramUsedMB = double.IsNaN(dedicated) ? float.NaN : (float)(dedicated / 1048576.0);
                S.GpuTemp = S.GpuClock = S.GpuMemClock = S.GpuPowerW = S.GpuPowerLimitW = S.GpuFan = float.NaN;
                S.GpuOk = true;
                S.GpuStamp = Tick();
            }
        }

        // インスタンス名から "luid_0x00000000_0x0000C7F1" の部分を取り出す
        static string Luid(string instance)
        {
            int a = instance.IndexOf("luid_", StringComparison.OrdinalIgnoreCase), b = instance.IndexOf("_phys_", StringComparison.OrdinalIgnoreCase);
            return a < 0 || b <= a ? null : instance.Substring(a, b - a).ToLowerInvariant();
        }

        // GPU の名前と VRAM の総量はカウンターに無いので、ドライバーがレジストリに書く値から取る (複数あれば VRAM が最も多いもの)
        void StartGpuStaticInfo()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                string name = null;
                long mem = 0;
                try
                {
                    using (RegistryKey cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                    {
                        if (cls != null)
                            foreach (string sub in cls.GetSubKeyNames())
                            {
                                if (sub.Length != 4 || !char.IsDigit(sub[0])) continue;
                                using (RegistryKey k = cls.OpenSubKey(sub))
                                {
                                    if (k == null) continue;
                                    long sz = RegSize(k.GetValue("HardwareInformation.qwMemorySize"));
                                    if (sz <= 0) sz = RegSize(k.GetValue("HardwareInformation.MemorySize"));
                                    string desc = Convert.ToString(k.GetValue("DriverDesc", ""));
                                    if (desc.Length == 0) continue;
                                    if (name == null || sz > mem) { name = desc; mem = sz; }
                                }
                            }
                    }
                }
                catch (Exception ex) { Log.Write("gpu.registry", ex); }
                lock (S)
                {
                    if (name != null && smi == null) S.GpuName = name;
                    if (mem > 0 && smi == null) S.VramTotalMB = (float)(mem / 1048576.0);
                }
            });
        }

        static long RegSize(object v)
        {
            if (v is long) return (long)v;
            if (v is int) return (uint)(int)v;
            byte[] b = v as byte[];
            if (b != null && b.Length >= 8) return BitConverter.ToInt64(b, 0);
            if (b != null && b.Length >= 4) return BitConverter.ToUInt32(b, 0);
            return 0;
        }

        void KillSmi()
        {
            try
            {
                Process p = smi;
                smi = null;
                if (p != null)
                {
                    try { if (!p.HasExited) p.Kill(); } catch { }
                    p.Dispose();
                }
            }
            catch { }
        }

        void OnSmiLine(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            string[] f = e.Data.Split(',');
            if (f.Length < 10) return;
            if (!smiGotLine) { smiGotLine = true; smiRetrySec = 30; }   // 動いているので再試行の間隔を戻す
            lock (S)
            {
                string name = f[0].Trim();
                if (name.StartsWith("NVIDIA ", StringComparison.OrdinalIgnoreCase)) name = name.Substring(7);
                S.GpuName = name;
                S.GpuUtil = Num(f[1]);
                S.GpuTemp = Num(f[2]);
                S.VramUsedMB = Num(f[3]);
                S.VramTotalMB = Num(f[4]);
                S.GpuPowerW = Num(f[5]);
                S.GpuPowerLimitW = Num(f[6]);
                S.GpuClock = Num(f[7]);
                S.GpuMemClock = Num(f[8]);
                S.GpuFan = Num(f[9]);
                S.GpuOk = !float.IsNaN(S.GpuUtil);
                S.GpuStamp = Tick();
            }
        }

        static float Num(string s)
        {
            float v;
            return float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : float.NaN;
        }

        // ---------------------------------------------------------------- weather (Open-Meteo)

        void StartWeather()
        {
            if (Interlocked.CompareExchange(ref weatherBusy, 1, 0) != 0) return;
            weatherNext = Tick() + 15 * 60000;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    Weather w = FetchWeather();
                    lock (S) { S.Weather = w; S.WeatherError = null; }
                }
                catch (Exception ex)
                {
                    lock (S) S.WeatherError = ex.Message;
                    weatherNext = Tick() + 2 * 60000;
                }
                Interlocked.Exchange(ref weatherBusy, 0);
            });
        }

        // 設定の単位を Open-Meteo の引数に (既定は °C と km/h)
        string WeatherUnitParams()
        {
            if (cfg.WeatherUnits == "imperial") return "&temperature_unit=fahrenheit&wind_speed_unit=mph";
            if (cfg.WeatherUnits == "metric-ms") return "&wind_speed_unit=ms";
            return "";
        }

        internal static string ReadErrorReason(WebException wex)
        {
            try
            {
                if (wex.Response == null) return null;
                string body;
                using (WebResponse resp = wex.Response)
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    body = sr.ReadToEnd();
                var d = new JavaScriptSerializer().DeserializeObject(body) as Dictionary<string, object>;
                object reason;
                if (d != null && d.TryGetValue("reason", out reason) && reason != null) return Convert.ToString(reason);
            }
            catch { }
            return null;
        }

        Weather FetchWeather()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            string url = string.Format(CultureInfo.InvariantCulture,
                "https://api.open-meteo.com/v1/forecast?latitude={0:0.###}&longitude={1:0.###}" +
                "&current=temperature_2m,relative_humidity_2m,apparent_temperature,is_day,weather_code,pressure_msl,wind_speed_10m,wind_direction_10m" +
                "&daily=weather_code,temperature_2m_max,temperature_2m_min,sunrise,sunset,uv_index_max,precipitation_probability_max" +
                "&hourly=precipitation_probability&forecast_hours=1" +
                "&timezone=auto&forecast_days=4", cfg.Latitude, cfg.Longitude) + WeatherUnitParams();
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = 10000;
            req.ReadWriteTimeout = 10000;   // 本文の受信が途中で止まったときも 10 秒で諦める (既定は 5 分)
            req.AutomaticDecompression = DecompressionMethods.GZip;   // deflate は zlib 形式で返るサーバーがあり .NET では展開できない
            req.UserAgent = "DeskMeter/1.0";
            string json;
            try
            {
                using (WebResponse resp = req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    json = sr.ReadToEnd();
            }
            catch (WebException wex)
            {
                // 400 などでも本文に {"error":true,"reason":"..."} が入っている。理由が分かればそれを出す (緯度・経度の間違いなど)
                string reason = ReadErrorReason(wex);
                if (reason != null) throw new InvalidOperationException(reason);
                throw;
            }

            var root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (root == null) throw new InvalidOperationException("応答が JSON ではありません");
            // Open-Meteo はエラー時に {"error":true,"reason":"..."} を返す
            object err;
            if (root.TryGetValue("error", out err) && err is bool && (bool)err)
            {
                object reason;
                string msg = root.TryGetValue("reason", out reason) && reason != null ? Convert.ToString(reason) : "Open-Meteo error";
                throw new InvalidOperationException(msg);
            }
            object curObj, dailyObj;
            root.TryGetValue("current", out curObj);
            root.TryGetValue("daily", out dailyObj);
            var cur = curObj as Dictionary<string, object>;
            var daily = dailyObj as Dictionary<string, object>;
            if (cur == null || daily == null) throw new InvalidOperationException("応答に current / daily がありません");

            var w = new Weather();
            w.Temp = D(Get(cur, "temperature_2m"));
            w.Feels = D(Get(cur, "apparent_temperature"));
            w.Humidity = D(Get(cur, "relative_humidity_2m"));
            w.Pressure = D(Get(cur, "pressure_msl"));
            w.Wind = D(Get(cur, "wind_speed_10m"));
            w.WindDir = D(Get(cur, "wind_direction_10m"));
            double code = D(Get(cur, "weather_code"));
            w.Code = double.IsNaN(code) ? 0 : (int)code;
            w.IsDay = D(Get(cur, "is_day")) != 0;
            w.Hi = D(First(daily, "temperature_2m_max"));
            w.Lo = D(First(daily, "temperature_2m_min"));
            w.Uv = D(First(daily, "uv_index_max"));
            // 降水確率は「今の1時間」の値を優先し、無ければ当日の最大値
            object hourly;
            w.PrecipProb = root.TryGetValue("hourly", out hourly) && hourly is Dictionary<string, object>
                ? D(First((Dictionary<string, object>)hourly, "precipitation_probability")) : double.NaN;
            if (double.IsNaN(w.PrecipProb)) w.PrecipProb = D(First(daily, "precipitation_probability_max"));
            w.Sunrise = Time(First(daily, "sunrise"));
            w.Sunset = Time(First(daily, "sunset"));
            // 明日から3日分の予報 (daily の添字 1〜3)
            object[] dates = Get(daily, "time") as object[];
            for (int i = 1; i <= 3 && dates != null && i < dates.Length; i++)
            {
                var f = new DayForecast();
                f.Date = Time(dates[i]);
                double c = D(At(daily, "weather_code", i));
                f.Code = double.IsNaN(c) ? 0 : (int)c;
                f.Hi = D(At(daily, "temperature_2m_max", i));
                f.Lo = D(At(daily, "temperature_2m_min", i));
                f.PrecipProb = D(At(daily, "precipitation_probability_max", i));
                if (f.Date != DateTime.MinValue) w.Days.Add(f);
            }
            w.Updated = DateTime.Now;
            return w;
        }

        static object At(Dictionary<string, object> d, string key, int i)
        {
            object[] a = Get(d, key) as object[];
            return a != null && i < a.Length ? a[i] : null;
        }

        static object Get(Dictionary<string, object> d, string key)
        {
            object o;
            return d.TryGetValue(key, out o) ? o : null;
        }

        static object First(Dictionary<string, object> d, string key)
        {
            object[] a = Get(d, key) as object[];
            return a != null && a.Length > 0 ? a[0] : null;
        }

        static double D(object o)
        {
            if (o == null) return double.NaN;
            try { return Convert.ToDouble(o, CultureInfo.InvariantCulture); }
            catch { return double.NaN; }
        }

        static DateTime Time(object o)
        {
            DateTime t;
            return o != null && DateTime.TryParse(Convert.ToString(o), CultureInfo.InvariantCulture, DateTimeStyles.None, out t) ? t : DateTime.MinValue;
        }
    }
}

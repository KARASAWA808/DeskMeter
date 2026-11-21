using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;

namespace DeskMeter
{
    sealed class DiagnosisSample
    {
        public double? CpuPercent, CpuCorePercent, MemoryPercent, MemoryAvailableBytes, MemoryTotalBytes;
        public double? NetworkDownBytesPerSecond, NetworkUpBytesPerSecond;
        public DiagnosisProcess[] TopCpu, TopMemory;
    }

    sealed class DiagnosisProcess
    {
        public string Name;
        public double Value;
    }

    // 手動診断 1 回分の基準値。常駐メーターの表示状態や履歴とは独立している。
    sealed class DiagnosisSampler : IDisposable
    {
        struct SystemCpuTime { public long Idle, Kernel, User; }
        struct ProcessTime { public long Created, Total; }
        struct NetworkBytes { public long Received, Sent; }

        [DllImport("kernel32.dll")]
        static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

        readonly PdhQuery cpuQuery = new PdhQuery();
        readonly List<string> cpuNames = new List<string>();
        readonly List<double> cpuValues = new List<double>();
        readonly int processorCount = Native.ActiveProcessorCount();
        IntPtr cpuCounter;
        bool cpuInitialized, cpuPrimed;
        SystemCpuTime? previousSystemCpu;

        IntPtr processBuffer;
        int processBufferSize;
        Dictionary<long, ProcessTime> previousProcesses;
        long processStamp;

        Dictionary<string, NetworkBytes> previousNetwork;
        long networkStamp;

        public DiagnosisSample Read(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var sample = new DiagnosisSample();
            ReadCpu(sample);
            ReadMemory(sample);
            token.ThrowIfCancellationRequested();
            ReadProcesses(sample, token);
            token.ThrowIfCancellationRequested();
            ReadNetwork(sample, token);
            token.ThrowIfCancellationRequested();
            return sample;
        }

        void ReadCpu(DiagnosisSample sample)
        {
            // GetSystemTimes は 64 論理 CPU を超えると呼び出し元のグループだけを返す。
            // その環境では全体も Processor Information のカウンターを使う。
            if (processorCount <= 64) sample.CpuPercent = ReadSystemCpu();
            if (!cpuInitialized)
            {
                cpuInitialized = true;
                if (cpuQuery.Open())
                {
                    cpuCounter = cpuQuery.Add(@"\Processor Information(*)\% Processor Time");
                    if (cpuCounter == IntPtr.Zero && processorCount <= 64)
                        cpuCounter = cpuQuery.Add(@"\Processor(*)\% Processor Time");
                }
            }

            bool collected = cpuCounter != IntPtr.Zero && cpuQuery.Collect();
            bool hasInterval = cpuPrimed && collected;
            cpuPrimed = collected;
            if (!hasInterval) return;

            int count = cpuQuery.Array(cpuCounter, cpuNames, cpuValues);
            for (int i = 0; i < count; i++)
            {
                double value = cpuValues[i];
                if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || string.IsNullOrEmpty(cpuNames[i]))
                    continue;
                value = Math.Min(100, value);
                if (string.Equals(cpuNames[i], "_Total", StringComparison.OrdinalIgnoreCase))
                    sample.CpuPercent = sample.CpuPercent ?? value;
                else if (cpuNames[i].IndexOf("_Total", StringComparison.OrdinalIgnoreCase) < 0 &&
                         (!sample.CpuCorePercent.HasValue || value > sample.CpuCorePercent.Value))
                    sample.CpuCorePercent = value;
            }
        }

        double? ReadSystemCpu()
        {
            var previous = previousSystemCpu;
            previousSystemCpu = null;
            var time = new SystemCpuTime();
            if (!GetSystemTimes(out time.Idle, out time.Kernel, out time.User)) return null;
            previousSystemCpu = time;
            if (!previous.HasValue) return null;

            // カーネル時間にはアイドル時間を含む。
            long idle = time.Idle - previous.Value.Idle;
            long kernel = time.Kernel - previous.Value.Kernel;
            long user = time.User - previous.Value.User;
            long total = kernel + user;
            if (idle < 0 || idle > kernel || user < 0 || total <= 0) return null;
            return 100.0 * (total - idle) / total;
        }

        static void ReadMemory(DiagnosisSample sample)
        {
            var memory = new Native.MEMORYSTATUSEX();
            memory.dwLength = (uint)Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX));
            if (!Native.GlobalMemoryStatusEx(ref memory) || memory.ullTotalPhys == 0 || memory.ullAvailPhys > memory.ullTotalPhys)
                return;

            sample.MemoryTotalBytes = memory.ullTotalPhys;
            sample.MemoryAvailableBytes = memory.ullAvailPhys;
            sample.MemoryPercent = 100.0 * (memory.ullTotalPhys - memory.ullAvailPhys) / memory.ullTotalPhys;
        }

        bool TryQueryProcesses(out int length)
        {
            const int LengthMismatch = unchecked((int)0xC0000004);
            length = 0;
            if (IntPtr.Size != 8) return false;
            if (processBuffer == IntPtr.Zero)
            {
                processBuffer = Marshal.AllocHGlobal(1 << 20);
                processBufferSize = 1 << 20;
            }

            for (int attempt = 0; attempt < 5; attempt++)
            {
                int status = Native.NtQuerySystemInformation(5, processBuffer, processBufferSize, out length);
                if (status == 0) return length > 0 && length <= processBufferSize;
                if (status != LengthMismatch || length <= processBufferSize) return false;
                int newSize = checked(length + (1 << 18));
                processBuffer = Marshal.ReAllocHGlobal(processBuffer, new IntPtr(newSize));
                processBufferSize = newSize;
            }
            return false;
        }

        void ReadProcesses(DiagnosisSample sample, CancellationToken token)
        {
            var previous = previousProcesses;
            previousProcesses = null;
            int length;
            if (!TryQueryProcesses(out length)) return;

            long stamp = Stopwatch.GetTimestamp();
            double elapsedTicks = (stamp - processStamp) * 1e7 / Stopwatch.Frequency; // 100 ns
            var current = new Dictionary<long, ProcessTime>();
            var cpu = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var memory = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            bool compared = false;

            // 常駐メーターと同じ 64 ビットの SYSTEM_PROCESS_INFORMATION を使う。
            const int HeaderSize = 0x58;
            for (int offset = 0; ; )
            {
                token.ThrowIfCancellationRequested();
                if (offset > length - HeaderSize) return;
                IntPtr entry = IntPtr.Add(processBuffer, offset);
                int next = Marshal.ReadInt32(entry);
                long pid = Marshal.ReadInt64(entry, 0x50);
                if (pid != 0)
                {
                    var time = new ProcessTime {
                        Created = Marshal.ReadInt64(entry, 0x20),
                        Total = Marshal.ReadInt64(entry, 0x28) + Marshal.ReadInt64(entry, 0x30)
                    };
                    current[pid] = time;
                    string name = ReadProcessName(entry, length);
                    long privateWorkingSet = Marshal.ReadInt64(entry, 0x08);
                    if (privateWorkingSet > 0) AddValue(memory, name, privateWorkingSet);

                    ProcessTime old;
                    if (previous != null && elapsedTicks > 0 && previous.TryGetValue(pid, out old) &&
                        old.Created == time.Created && time.Total >= old.Total)
                    {
                        compared = true;
                        if (time.Total > old.Total)
                            AddValue(cpu, name, 100.0 * (time.Total - old.Total) / (elapsedTicks * processorCount));
                    }
                }
                if (next == 0) break;
                if (next < HeaderSize || next > length - offset) return;
                offset += next;
            }
            previousProcesses = current;
            processStamp = stamp;
            sample.TopMemory = TopProcesses(memory);
            if (compared)
            {
                sample.TopCpu = TopProcesses(cpu);
                foreach (DiagnosisProcess row in sample.TopCpu)
                    row.Value = Math.Min(row.Value, 100);
            }
        }

        string ReadProcessName(IntPtr entry, int length)
        {
            int nameLength = (ushort)Marshal.ReadInt16(entry, 0x38);
            IntPtr address = Marshal.ReadIntPtr(entry, 0x40);
            long offset = address.ToInt64() - processBuffer.ToInt64();
            if (nameLength <= 0 || offset < 0 || offset > length - nameLength) return "名前不明";

            string name = Marshal.PtrToStringUni(address, nameLength / 2);
            int separator = Math.Max(name.LastIndexOf('\\'), name.LastIndexOf('/'));
            if (separator >= 0) name = name.Substring(separator + 1);
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            return name.Length == 0 ? "名前不明" : name;
        }

        static void AddValue(Dictionary<string, double> values, string name, double value)
        {
            double previous;
            values.TryGetValue(name, out previous);
            values[name] = previous + value;
        }

        static DiagnosisProcess[] TopProcesses(Dictionary<string, double> values)
        {
            var rows = new List<DiagnosisProcess>(values.Count);
            foreach (var value in values)
                rows.Add(new DiagnosisProcess { Name = value.Key, Value = value.Value });
            rows.Sort(delegate(DiagnosisProcess a, DiagnosisProcess b)
            {
                int order = b.Value.CompareTo(a.Value);
                return order != 0 ? order : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
            });
            return rows.GetRange(0, Math.Min(5, rows.Count)).ToArray();
        }

        void ReadNetwork(DiagnosisSample sample, CancellationToken token)
        {
            var previous = previousNetwork;
            previousNetwork = null;
            long stamp = Stopwatch.GetTimestamp();
            double seconds = (stamp - networkStamp) / (double)Stopwatch.Frequency;
            NetworkInterface[] interfaces;
            try { interfaces = NetworkInterface.GetAllNetworkInterfaces(); }
            catch (NetworkInformationException) { return; }

            var current = new Dictionary<string, NetworkBytes>();
            foreach (NetworkInterface nic in interfaces)
            {
                token.ThrowIfCancellationRequested();
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    continue;

                IPInterfaceStatistics stats;
                try { stats = nic.GetIPStatistics(); }
                catch (NetworkInformationException) { continue; }
                var bytes = new NetworkBytes { Received = stats.BytesReceived, Sent = stats.BytesSent };
                if (bytes.Received < 0 || bytes.Sent < 0) continue;
                current[nic.Id] = bytes;

                NetworkBytes before;
                if (previous == null || seconds <= 0 || !previous.TryGetValue(nic.Id, out before) ||
                    bytes.Received < before.Received || bytes.Sent < before.Sent)
                    continue;

                // 方向ごとに最大のアダプターを採用し、仮想・物理間で二重に集計しない。
                sample.NetworkDownBytesPerSecond = Math.Max(sample.NetworkDownBytesPerSecond ?? 0, (bytes.Received - before.Received) / seconds);
                sample.NetworkUpBytesPerSecond = Math.Max(sample.NetworkUpBytesPerSecond ?? 0, (bytes.Sent - before.Sent) / seconds);
            }
            previousNetwork = current;
            networkStamp = stamp;
        }

        public void Dispose()
        {
            cpuQuery.Dispose();
            if (processBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(processBuffer);
                processBuffer = IntPtr.Zero;
                processBufferSize = 0;
            }
            previousProcesses = null;
            previousNetwork = null;
        }
    }
}

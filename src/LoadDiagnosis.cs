using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DeskMeter
{
    static class LoadDiagnosis
    {
        const int SampleCount = 10;
        const int IntervalMs = 1000;
        const int HighIntervals = 5;
        const int CpuThreshold = 80;
        const int CoreThreshold = 90;
        const int GpuThreshold = 80;
        const int MemoryThreshold = 90;

        public static Task<string> RunAsync(CancellationToken token, IProgress<int> progress)
        {
            return Task.Run(async delegate
            {
                var samples = new List<DiagnosisSample>(SampleCount);
                var gpuSamples = new List<double?>(SampleCount);
                var watch = Stopwatch.StartNew();
                using (var sampler = new DiagnosisSampler())
                using (var gpu = new DiagnosisGpu())
                {
                    token.ThrowIfCancellationRequested();
                    // 累計カウンターの差分を取るための初回値。判定には含めない。
                    sampler.Read(token);
                    gpu.Read();
                    for (int i = 0; i < SampleCount; i++)
                    {
                        await Task.Delay(IntervalMs, token).ConfigureAwait(false);
                        samples.Add(sampler.Read(token));
                        token.ThrowIfCancellationRequested();
                        gpuSamples.Add(gpu.Read());
                        token.ThrowIfCancellationRequested();
                        if (progress != null) progress.Report((i + 1) * 100 / SampleCount);
                    }
                }
                return FormatReport(samples, gpuSamples, watch.Elapsed.TotalSeconds);
            }, token);
        }

        internal static string FormatReport(IList<DiagnosisSample> samples, IList<double?> gpuSamples, double seconds)
        {
            var cpu = new Readings(samples.Select(s => s.CpuPercent));
            var core = new Readings(samples.Select(s => s.CpuCorePercent));
            var gpu = new Readings(gpuSamples);
            var memory = new Readings(samples.Select(s => s.MemoryPercent));
            var down = new Readings(samples.Select(s => s.NetworkDownBytesPerSecond));
            var up = new Readings(samples.Select(s => s.NetworkUpBytesPerSecond));
            var candidates = new List<string>();
            if (cpu.CountAtLeast(CpuThreshold) >= HighIntervals) candidates.Add("CPU 全体の高い負荷");
            else if (core.CountAtLeast(CoreThreshold) >= HighIntervals) candidates.Add("一部の論理プロセッサの高い負荷");
            if (gpu.CountAtLeast(GpuThreshold) >= HighIntervals) candidates.Add("GPU エンジンの高い負荷");
            if (memory.CountAtLeast(MemoryThreshold) >= HighIntervals) candidates.Add("物理メモリの空き不足");

            var report = new StringBuilder();
            report.AppendFormat("計測時間 {0:0.0} 秒（{1} 回）", seconds, samples.Count).AppendLine();
            if (candidates.Count > 0) report.AppendLine("負荷の原因候補  " + string.Join("、", candidates));
            else if (Math.Max(Math.Max(cpu.Count, core.Count), Math.Max(gpu.Count, memory.Count)) < HighIntervals)
                report.AppendLine("CPU・GPU・メモリの負荷を判定するには、取得できた値が不足しています。");
            else report.AppendLine("取得できた値の範囲では、繰り返し高い負荷を示す CPU・GPU・メモリは見つかりませんでした。");

            report.AppendLine().AppendLine("CPU");
            AppendPercent(report, "全体", cpu, CpuThreshold, samples.Count);
            AppendPercent(report, "最も使用率の高い論理プロセッサ", core, CoreThreshold, samples.Count);
            report.AppendLine("根拠  Windows の CPU 時間から求めた、アイドル時間以外の割合です。最も使用率の高い論理プロセッサは区間ごとに変わることがあります。");
            AppendProcesses(report, samples, s => s.TopCpu, s => s.CpuPercent, Percent, "CPU", "全論理 CPU の合計を 100% とする割合");
            report.AppendLine("次の確認  タスク マネージャーで CPU 順に並べ、症状と上位アプリの負荷が重なるか確認してください。");

            report.AppendLine().AppendLine("GPU");
            AppendPercent(report, "最も使用率の高い GPU エンジン", gpu, GpuThreshold, samples.Count);
            report.AppendLine("根拠  Windows の GPU Engine カウンターで、プロセス別使用率をエンジンごとに合算し、複数 GPU を含めて最大の値を採用しています。");
            report.AppendLine("次の確認  タスク マネージャーの GPU エンジン別グラフと専用・共有 GPU メモリを確認してください。カウンターが取得できない環境ではドライバーの対応状況も確認してください。");

            report.AppendLine().AppendLine("メモリ");
            AppendPercent(report, "物理メモリ使用率", memory, MemoryThreshold, samples.Count);
            var peakMemory = samples.Where(s => s.MemoryPercent.HasValue).OrderByDescending(s => s.MemoryPercent.Value).FirstOrDefault();
            if (peakMemory != null && peakMemory.MemoryAvailableBytes.HasValue && peakMemory.MemoryTotalBytes.HasValue)
            {
                report.AppendFormat("使用率のピーク時に使用可能  {0} / 全体 {1}", Size(peakMemory.MemoryAvailableBytes.Value), Size(peakMemory.MemoryTotalBytes.Value)).AppendLine();
            }
            AppendProcesses(report, samples, s => s.TopMemory, s => s.MemoryPercent, Size, "メモリ", "専用ワーキング セット");
            report.AppendLine("次の確認  タスク マネージャーでメモリ順に並べ、使用可能なメモリとコミット済みの値も確認してください。");

            report.AppendLine().AppendLine("ネットワーク");
            AppendReadings(report, "下り", down, Rate, samples.Count);
            AppendReadings(report, "上り", up, Rate, samples.Count);
            report.AppendLine("根拠  連続して取得できたアダプターの送受信バイト数の差分です。各区間で下り・上りそれぞれの最大速度を採用しています。遅延・パケット損失・回線容量はこの計測の対象外です。");
            report.AppendLine("次の確認  通信を使うアプリだけが遅い場合は、リソース モニターの「ネットワーク」でプロセス別の通信を確認してください。");

            report.AppendLine().AppendFormat("原因候補の目安は、各しきい値に達した計測が {0} 回以上です。症状が再現している間に計測してください。", HighIntervals);
            return report.ToString();
        }

        static void AppendPercent(StringBuilder report, string label, Readings readings, int threshold, int total)
        {
            AppendReadings(report, label, readings, Percent, total);
            if (readings.Count > 0)
                report.AppendFormat("  {0}% 以上  {1} / {2} 回", threshold, readings.CountAtLeast(threshold), readings.Count).AppendLine();
        }

        static void AppendReadings(StringBuilder report, string label, Readings readings, Func<double, string> format, int total)
        {
            if (readings.Count == 0)
                report.AppendFormat("{0}  取得できませんでした（0 / {1} 回）", label, total).AppendLine();
            else
                report.AppendFormat("{0}  平均 {1}、最大 {2}（取得 {3} / {4} 回）", label, format(readings.Average), format(readings.Max), readings.Count, total).AppendLine();
        }

        static void AppendProcesses(StringBuilder report, IList<DiagnosisSample> samples, Func<DiagnosisSample, DiagnosisProcess[]> processes,
            Func<DiagnosisSample, double?> systemUsage, Func<double, string> format, string label, string unit)
        {
            var measured = samples.Where(s => processes(s) != null).ToList();
            var ranked = measured.Where(s => systemUsage(s).HasValue).OrderByDescending(systemUsage).FirstOrDefault();
            report.AppendFormat("{0} 上位プロセス（{1}、同名を合算）", label, unit).AppendLine();
            if (measured.Count == 0)
            {
                report.AppendLine("  取得できませんでした。");
                return;
            }
            report.AppendLine(ranked != null ? "  上位を取得できた計測のうち、全体使用率が最も高い回" : "  上位を最後に取得できた回");
            var top = processes(ranked ?? measured[measured.Count - 1]);
            if (top.Length == 0) report.AppendLine("  使用量のあるプロセスはありませんでした。");
            else
            {
                foreach (var process in top)
                    report.AppendFormat("  {0}  {1}", process.Name, format(process.Value)).AppendLine();
            }
        }

        static string Percent(double value) { return string.Format("{0:0.0}%", value); }

        static string Size(double bytes)
        {
            return bytes >= 1024 * 1024 * 1024
                ? string.Format("{0:0.00} GiB", bytes / (1024 * 1024 * 1024))
                : string.Format("{0:0.0} MiB", bytes / (1024 * 1024));
        }

        static string Rate(double bytesPerSecond)
        {
            double bits = bytesPerSecond * 8;
            if (bits >= 1000000) return string.Format("{0:0.00} Mbps", bits / 1000000);
            if (bits >= 1000) return string.Format("{0:0.00} kbps", bits / 1000);
            return string.Format("{0:0.#} bit/s", bits);
        }

        sealed class Readings
        {
            readonly double[] values;
            public Readings(IEnumerable<double?> source) { values = source.Where(v => v.HasValue).Select(v => v.Value).ToArray(); }
            public int Count { get { return values.Length; } }
            public double Average { get { return values.Average(); } }
            public double Max { get { return values.Max(); } }
            public int CountAtLeast(int threshold) { return values.Count(v => v >= threshold); }
        }
    }
}

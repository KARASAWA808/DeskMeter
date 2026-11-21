using System;
using System.Collections.Generic;

namespace DeskMeter
{
    // 診断中だけ Windows の GPU Engine カウンターを読む。
    sealed class DiagnosisGpu : IDisposable
    {
        readonly PdhQuery query = new PdhQuery();
        readonly IntPtr utilization;
        readonly List<string> names = new List<string>();
        readonly List<double> values = new List<double>();
        bool hasBaseline;

        public DiagnosisGpu()
        {
            if (query.Open()) utilization = query.Add(@"\GPU Engine(*)\Utilization Percentage");
        }

        public double? Read()
        {
            if (utilization == IntPtr.Zero || !query.Collect())
            {
                hasBaseline = false;
                return null;
            }
            if (!hasBaseline)
            {
                hasBaseline = true;
                return null;
            }

            query.Array(utilization, names, values);
            return BusiestEngine(names, values);
        }

        internal static double? BusiestEngine(IList<string> names, IList<double> values)
        {
            var engineTotals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            double? busiest = null;
            for (int i = 0; i < names.Count; i++)
            {
                double value = values[i];
                if (double.IsNaN(value) || double.IsInfinity(value) || value < 0) continue;

                // pid_…_luid_…_phys_…_eng_…_engtype_… からアダプターとエンジンを識別する。
                string name = names[i];
                if (name == null) continue;
                int adapter = name.IndexOf("_luid_", StringComparison.OrdinalIgnoreCase);
                int engineType = name.IndexOf("_engtype_", StringComparison.OrdinalIgnoreCase);
                if (adapter < 0 || engineType <= adapter) continue;
                string engine = name.Substring(adapter, engineType - adapter);

                // 同じエンジンを使うプロセスの値を合算する。異なるエンジン同士は加算しない。
                double total;
                engineTotals.TryGetValue(engine, out total);
                total = Math.Min(100, total + value);
                engineTotals[engine] = total;
                if (!busiest.HasValue || total > busiest.Value) busiest = total;
            }
            return busiest;
        }

        public void Dispose() { query.Dispose(); }
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using WF = System.Windows.Forms;

namespace DeskMeter
{
    // 通知: CPU / メモリの使用率がしきい値を超えた状態が続いたとき、ドライブの空きが減ったとき、温度が高いとき、
    // バッテリーが減ったときにタスクトレイのバルーンで知らせる。しきい値は設定 (0 で無効)。
    // 同じ通知は条件が解消するまで、または 30 分たつまで繰り返さない
    sealed partial class MainWindow
    {
        const long AlertRepeatMs = 30 * 60000L;
        long cpuOverSince, memOverSince;
        long lastCpuAlert, lastMemAlert, lastDiskAlert, lastTempAlert, lastBatAlert;
        bool diskAlerting, tempAlerting, batAlerting;
        readonly List<KeyValuePair<WF.ToolStripMenuItem, FieldInfo>> intMenus = new List<KeyValuePair<WF.ToolStripMenuItem, FieldInfo>>();

        void CheckAlerts()
        {
            if (tray == null || snapshotPath != null) return;
            if (cfg.AlertCpuPct <= 0 && cfg.AlertMemPct <= 0 && cfg.AlertDiskFreePct <= 0 && cfg.AlertTempC <= 0 && cfg.AlertBatteryPct <= 0) return;
            long now = (long)Native.GetTickCount64();
            float cpu, cpuTemp, gpuTemp;
            double memPct = double.NaN;
            bool gpuOk, hasBat, ac;
            int batPct;
            string lowDrive = null;
            double lowFree = 0;
            MeterState S = sampler.S;
            lock (S)
            {
                cpu = S.CpuTotal;
                if (S.RamTotal > 0) memPct = 100.0 * (S.RamTotal - S.RamAvail) / S.RamTotal;
                cpuTemp = S.CpuTemp;
                gpuOk = S.GpuOk;
                gpuTemp = S.GpuTemp;
                hasBat = S.HasBattery; ac = S.AcLine; batPct = S.BatteryPercent;
                if (cfg.AlertDiskFreePct > 0)
                    foreach (DriveRow d in S.Drives)
                    {
                        if (d.Total <= 0) continue;
                        double free = 100.0 * d.Free / d.Total;
                        if (free < cfg.AlertDiskFreePct && (lowDrive == null || free < lowFree)) { lowDrive = d.Name; lowFree = free; }
                    }
            }

            // CPU / メモリ: しきい値以上が AlertSeconds 続いたら
            if (cfg.AlertCpuPct > 0)
            {
                if (cpu >= cfg.AlertCpuPct)
                {
                    if (cpuOverSince == 0) cpuOverSince = now;
                    else if (now - cpuOverSince >= cfg.AlertSeconds * 1000L && now - lastCpuAlert > AlertRepeatMs)
                    {
                        lastCpuAlert = now;
                        Notify("CPU 使用率が " + cfg.AlertCpuPct + "% 以上の状態が " + MeterView.SpanText(cfg.AlertSeconds) + " 続いています (いま " + Math.Round(cpu) + "%)。");
                    }
                }
                else cpuOverSince = 0;
            }
            if (cfg.AlertMemPct > 0 && !double.IsNaN(memPct))
            {
                if (memPct >= cfg.AlertMemPct)
                {
                    if (memOverSince == 0) memOverSince = now;
                    else if (now - memOverSince >= cfg.AlertSeconds * 1000L && now - lastMemAlert > AlertRepeatMs)
                    {
                        lastMemAlert = now;
                        Notify("メモリ使用率が " + cfg.AlertMemPct + "% 以上の状態が " + MeterView.SpanText(cfg.AlertSeconds) + " 続いています (いま " + Math.Round(memPct) + "%)。");
                    }
                }
                else memOverSince = 0;
            }
            // ドライブの空き: 条件が成り立った最初の 1 回 (解消したらまた知らせる)
            if (cfg.AlertDiskFreePct > 0)
            {
                if (lowDrive != null)
                {
                    if (!diskAlerting || now - lastDiskAlert > AlertRepeatMs)
                    {
                        diskAlerting = true; lastDiskAlert = now;
                        Notify("ドライブ " + lowDrive + " の空きが " + lowFree.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "% です (しきい値 " + cfg.AlertDiskFreePct + "%)。");
                    }
                }
                else diskAlerting = false;
            }
            // 温度: HWiNFO の CPU 温度と (NVIDIA の) GPU 温度の高いほう
            if (cfg.AlertTempC > 0)
            {
                float t = float.NaN;
                if (!float.IsNaN(cpuTemp)) t = cpuTemp;
                if (gpuOk && !float.IsNaN(gpuTemp) && (float.IsNaN(t) || gpuTemp > t)) t = gpuTemp;
                if (!float.IsNaN(t) && t >= cfg.AlertTempC)
                {
                    if (!tempAlerting || now - lastTempAlert > AlertRepeatMs)
                    {
                        tempAlerting = true; lastTempAlert = now;
                        string which = !float.IsNaN(cpuTemp) && cpuTemp >= cfg.AlertTempC ? "CPU" : "GPU";
                        Notify(which + " の温度が " + Math.Round(t) + " °C です (しきい値 " + cfg.AlertTempC + " °C)。");
                    }
                }
                else tempAlerting = false;
            }
            // バッテリー: 駆動中に残量がしきい値以下
            if (cfg.AlertBatteryPct > 0)
            {
                if (hasBat && !ac && batPct >= 0 && batPct <= cfg.AlertBatteryPct)
                {
                    if (!batAlerting || now - lastBatAlert > AlertRepeatMs)
                    {
                        batAlerting = true; lastBatAlert = now;
                        Notify("バッテリー残量が " + batPct + "% です。");
                    }
                }
                else batAlerting = false;
            }
        }

        void Notify(string text)
        {
            try { tray.ShowBalloonTip(8000, AppName + " の通知", text, WF.ToolTipIcon.Warning); }
            catch (Exception ex) { Log.Write("notify", ex); }
        }

        void ResetAlertState()
        {
            cpuOverSince = memOverSince = 0;
            diskAlerting = tempAlerting = batAlerting = false;
        }

        // メニュー「通知」。各項目は整数の設定 1 つに対応し、0 が無効
        WF.ToolStripMenuItem BuildAlertMenu()
        {
            var m = new WF.ToolStripMenuItem("通知 (トレイのバルーン)");
            m.DropDownItems.Add(IntMenu("CPU 使用率が高い状態が続いたら", "AlertCpuPct", new[] { 0, 80, 90, 95 }, "%"));
            m.DropDownItems.Add(IntMenu("メモリ使用率が高い状態が続いたら", "AlertMemPct", new[] { 0, 85, 90, 95 }, "%"));
            m.DropDownItems.Add(IntMenu("ドライブの空きが少ないとき", "AlertDiskFreePct", new[] { 0, 5, 10, 20 }, "% 未満"));
            m.DropDownItems.Add(IntMenu("温度が高いとき (CPU は HWiNFO 連携時、GPU は NVIDIA)", "AlertTempC", new[] { 0, 80, 90 }, " °C"));
            m.DropDownItems.Add(IntMenu("バッテリー残量が少ないとき (駆動中)", "AlertBatteryPct", new[] { 0, 10, 20, 30 }, "% 以下"));
            m.DropDownItems.Add(new WF.ToolStripSeparator());
            m.DropDownItems.Add(IntMenu("「続いたら」の長さ", "AlertSeconds", new[] { 30, 60, 180, 300 }, "s"));
            return m;
        }

        WF.ToolStripMenuItem IntMenu(string title, string field, int[] values, string unit)
        {
            FieldInfo f = typeof(Settings).GetField(field);
            var m = new WF.ToolStripMenuItem(title);
            foreach (int v in values)
            {
                int val = v;
                string label = val == 0 ? "無効" : unit == "s" ? MeterView.SpanText(val) : val + unit;
                var mi = Item(label, delegate { f.SetValue(cfg, val); ResetAlertState(); cfg.Save(); });
                mi.Tag = val;
                m.DropDownItems.Add(mi);
            }
            intMenus.Add(new KeyValuePair<WF.ToolStripMenuItem, FieldInfo>(m, f));
            return m;
        }

        void SyncIntMenus()
        {
            foreach (KeyValuePair<WF.ToolStripMenuItem, FieldInfo> kv in intMenus)
            {
                int cur = (int)kv.Value.GetValue(cfg);
                foreach (WF.ToolStripItem it in kv.Key.DropDownItems)
                {
                    var mi = it as WF.ToolStripMenuItem;
                    if (mi != null && mi.Tag is int) mi.Checked = (int)mi.Tag == cur;
                }
                MarkCustom(kv.Key, cur.ToString(System.Globalization.CultureInfo.InvariantCulture), CustomInt);
            }
        }
    }
}

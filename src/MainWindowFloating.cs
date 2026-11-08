using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WF = System.Windows.Forms;

namespace DeskMeter
{
    // パネルの切り離し (独立ウィンドウ)、配置プリセット、テーマのメニュー
    sealed partial class MainWindow
    {
        readonly List<PanelWindow> floats = new List<PanelWindow>();
        bool userHidden;         // 「ウィジェットを隠す」で隠している
        bool hiddenByMax;        // 同じモニターの最大化ウィンドウが前面なので隠している (設定で有効なとき)
        bool maxOverride;        // 自動非表示中にホットキーやトレイで表示した。前面のウィンドウが変わるまで隠さない
        bool hiddenByFloat;      // 列にパネルが無くなったので本体だけ隠している
        string menuPanelId;      // 独立ウィンドウ上で右クリックしたパネル
        WF.ToolStripMenuItem miDetach, miAttachThis, miPresets, miThemes;

        public bool ShowDesktop { get { return showDesktop; } }

        public void OpenPanelTarget(string id) { OpenPanelApp(id, null); }

        IEnumerable<IntPtr> AllHwnds()
        {
            if (hwnd != IntPtr.Zero) yield return hwnd;
            foreach (PanelWindow f in floats) if (f.Hwnd != IntPtr.Zero) yield return f.Hwnd;
        }

        bool IsOurWindow(IntPtr h)
        {
            foreach (IntPtr wh in AllHwnds()) if (wh == h) return true;
            return false;
        }

        // ---------------------------------------------------------------- まとめて適用

        void InvalidateAll()
        {
            view.InvalidateVisual();
            foreach (PanelWindow f in floats) f.View.InvalidateVisual();
        }

        void RelayoutAll()
        {
            SyncFloating();
            view.ClearUndo();   // プリセット・テーマ・再読み込み・切り離しで配置が変わったので、編集中の取り消し履歴は捨てる
            view.Relayout();
            foreach (PanelWindow f in floats) f.View.Relayout();
        }

        void ApplySkinAll()
        {
            view.ApplySkin();
            foreach (PanelWindow f in floats) f.View.ApplySkin();
        }

        void ApplyScaleAll()
        {
            view.LayoutTransform = new ScaleTransform(cfg.Scale, cfg.Scale);
            foreach (PanelWindow f in floats) f.ApplyScale();
        }

        // 設定 (cfg.Floating) と実際のウィンドウを合わせる
        void SyncFloating()
        {
            for (int i = floats.Count - 1; i >= 0; i--)
            {
                if (!cfg.Floating.Contains(floats[i].Id))
                {
                    floats[i].Close();
                    floats.RemoveAt(i);
                }
            }
            foreach (string id in cfg.Floating)
            {
                bool exists = false;
                foreach (PanelWindow f in floats) if (f.Id == id) { exists = true; break; }
                if (exists) continue;
                var w = new PanelWindow(this, cfg, sampler.S, id);
                floats.Add(w);
                if (!userHidden) w.Show();
            }
            UpdateMainVisibility();
        }

        // 列が空で切り離しだけになったら本体のウィンドウは隠す
        void UpdateMainVisibility()
        {
            if (userHidden) return;
            bool shouldHide = cfg.Layout.Count == 0 && cfg.Floating.Count > 0;
            if (shouldHide && !hiddenByFloat)
            {
                hiddenByFloat = true;
                Hide();
            }
            else if (!shouldHide && hiddenByFloat)
            {
                hiddenByFloat = false;
                Show();
                ApplyMode();
            }
        }

        // 最大化ウィンドウによる自動の非表示/再表示 (ユーザーの「隠す」とは別に数える)
        void SetAutoHidden(bool on)
        {
            if (on == hiddenByMax) return;
            hiddenByMax = on;
            if (on)
            {
                if (IsVisible) Hide();
                foreach (PanelWindow f in floats) f.Hide();
            }
            else if (!userHidden)
            {
                if (!hiddenByFloat) { Show(); ApplyMode(); }
                foreach (PanelWindow f in floats) { f.Show(); f.ApplyMode(); }
                ScheduleNextTick();
                sampler.Poke();
            }
            UpdatePaused();
        }

        // 普段の不透明度 (HoverOpacity)。マウスが載っている窓は 100%。クリック透過中はマウスが入らないので薄くしない
        double DimOpacity { get { return cfg.ClickThrough ? 1 : cfg.HoverOpacity / 100.0; } }

        void ApplyHoverOpacity()
        {
            double dim = DimOpacity;
            Opacity = IsMouseOver ? 1 : dim;
            foreach (PanelWindow f in floats) f.Opacity = f.IsMouseOver ? 1 : dim;
        }

        void ToggleVisible()
        {
            userHidden = !userHidden;
            if (userHidden)
            {
                if (IsVisible) Hide();
                foreach (PanelWindow f in floats) f.Hide();
                timer.Stop();
            }
            else
            {
                if (hiddenByMax) { hiddenByMax = false; maxOverride = true; }   // 手で表示したら、前面のウィンドウが変わるまで自動では隠さない
                if (!hiddenByFloat) { Show(); ApplyMode(); }
                foreach (PanelWindow f in floats) { f.Show(); f.ApplyMode(); }
                ScheduleNextTick();
                timer.Start();
                sampler.Poke();
            }
            UpdatePaused();
        }

        // 隠している間とセッションロック中は計測をスキップする
        void UpdatePaused()
        {
            sampler.Paused = userHidden || sessionLocked || displayOff || fullscreenApp;   // 最大化中の自動非表示では計測を続ける (CSV 記録と通知のため)
            ScheduleNextTick();
            sampler.Poke();   // 止めるときもすぐ反映する (次の計測を待たずに nvidia-smi を止める)
        }

        // 独立ウィンドウ上での右クリック: そのパネルを戻す項目つきで同じメニューを出す
        public void ShowMenuFor(string id)
        {
            menuPanelId = id;
            ShowMenu();
        }

        // ---------------------------------------------------------------- メニュー

        void BuildLayoutMenus()
        {
            miAttachThis = Item("このパネルを本体のウィンドウに戻す", delegate
            {
                if (menuPanelId == null) return;
                cfg.Attach(menuPanelId);
                RelayoutAll();
                cfg.Save();
            });
            menu.Items.Insert(0, miAttachThis);
            menu.Closed += delegate { menuPanelId = null; };

            miDetach = new WF.ToolStripMenuItem("パネルを切り離す (独立ウィンドウ)");
            foreach (string id in Settings.PanelIds)
            {
                string pid = id;
                var mi = Item(Settings.PanelName(pid), delegate
                {
                    if (cfg.Floating.Contains(pid)) cfg.Attach(pid); else cfg.Detach(pid);
                    RelayoutAll();
                    cfg.Save();
                });
                mi.Tag = pid;
                miDetach.DropDownItems.Add(mi);
            }
            miDetach.DropDownItems.Add(new WF.ToolStripSeparator());
            miDetach.DropDownItems.Add(Item("すべて本体に戻す", delegate
            {
                foreach (string id in new List<string>(cfg.Floating)) cfg.Attach(id);
                RelayoutAll();
                cfg.Save();
            }));
            menu.Items.Add(miDetach);

            miPresets = new WF.ToolStripMenuItem("配置プリセット (表示する項目も変わる)");
            foreach (string name in Settings.PresetNames)
            {
                string n = name;
                miPresets.DropDownItems.Add(Item(Settings.PresetDisplayName(n), delegate
                {
                    cfg.ApplyPreset(n);
                    RelayoutAll();
                    cfg.Save();
                }));
            }
            menu.Items.Add(miPresets);

            miThemes = new WF.ToolStripMenuItem("テーマ (見た目一式の保存)");
            menu.Items.Add(miThemes);
        }

        void UpdateLayoutMenus()
        {
            miAttachThis.Visible = menuPanelId != null;
            miAttachThis.Text = menuPanelId == null ? "" : "「" + Settings.PanelName(menuPanelId) + "」を本体のウィンドウに戻す";
            foreach (WF.ToolStripItem it in miDetach.DropDownItems)
            {
                var mi = it as WF.ToolStripMenuItem;
                if (mi == null || !(mi.Tag is string)) continue;
                string id = (string)mi.Tag;
                mi.Checked = cfg.Floating.Contains(id);
                mi.Enabled = cfg.HasPanel(id);   // 非表示のパネルは切り離せない
            }
            BuildThemeMenu();
        }

        void BuildThemeMenu()
        {
            miThemes.DropDownItems.Clear();
            miThemes.DropDownItems.Add(Item("いまの見た目をテーマとして保存...", SaveThemeInteractive));
            miThemes.DropDownItems.Add(Item("テーマのフォルダーを開く", delegate
            {
                try { Directory.CreateDirectory(Settings.ThemeDir); Process.Start(Settings.ThemeDir); }
                catch (Exception ex) { Log.Write("open themes", ex); }
            }));
            string[] names = Settings.ThemeNames();
            if (names.Length == 0) return;
            miThemes.DropDownItems.Add(new WF.ToolStripSeparator());
            foreach (string name in names)
            {
                string n = name;
                miThemes.DropDownItems.Add(Item(n, delegate { ApplyThemeByName(n); }));
            }
        }

        void SaveThemeInteractive()
        {
            try
            {
                string name = Microsoft.VisualBasic.Interaction.InputBox("テーマの名前 (ファイル名になります)", "テーマを保存", "my-theme");
                if (string.IsNullOrWhiteSpace(name)) return;
                name = name.Trim();
                if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Settings.IsReservedName(name))
                {
                    if (tray != null) tray.ShowBalloonTip(4000, AppName, "その名前はファイル名に使えません。", WF.ToolTipIcon.Warning);
                    return;
                }
                if (File.Exists(Path.Combine(Settings.ThemeDir, name + ".json")))
                {
                    if (WF.MessageBox.Show("テーマ「" + name + "」はすでにあります。上書きしますか?", AppName, WF.MessageBoxButtons.YesNo, WF.MessageBoxIcon.Question, WF.MessageBoxDefaultButton.Button2) != WF.DialogResult.Yes) return;
                }
                if (cfg.SaveTheme(name)) { if (tray != null) tray.ShowBalloonTip(3000, AppName, "テーマ「" + name + "」を保存しました。", WF.ToolTipIcon.Info); }
                else if (tray != null) tray.ShowBalloonTip(5000, AppName, "テーマ「" + name + "」を保存できませんでした。deskmeter.log を確認してください。", WF.ToolTipIcon.Warning);
            }
            catch (Exception ex) { Log.Write("save theme", ex); }
        }

        void ApplyThemeByName(string name)
        {
            Settings.ThemeFile t = Settings.LoadTheme(name);
            if (t == null)
            {
                if (tray != null) tray.ShowBalloonTip(4000, AppName, "テーマ「" + name + "」を読み込めませんでした。deskmeter.log を確認してください。", WF.ToolTipIcon.Warning);
                return;
            }
            cfg.ApplyTheme(t);
            if (t.Skin != null && !Skin.Exists(t.Skin) && tray != null)
                tray.ShowBalloonTip(5000, AppName, "テーマのスキン「" + t.Skin + "」が見つからないので Classic で表示します。", WF.ToolTipIcon.Warning);
            ApplyScaleAll();
            ApplySkinAll();
            WarnSkinProblems();
            RelayoutAll();
            foreach (PanelWindow f in floats) f.RestorePosition();
            ScheduleNextTick();
            sampler.Poke();
            ApplyMode();
            ApplyClickThrough();
            cfg.Save();
        }

        // ---------------------------------------------------------------- スナップショット (切り離しパネル)

        void SaveFloatSnapshots()
        {
            foreach (PanelWindow f in floats)
            {
                try
                {
                    string path = Path.Combine(Path.GetDirectoryName(snapshotPath), Path.GetFileNameWithoutExtension(snapshotPath) + "-" + f.Id + ".png");
                    MeterView v = f.View;
                    double s = cfg.Scale;
                    int w = (int)Math.Ceiling(v.ActualWidth * s), h = (int)Math.Ceiling(v.ActualHeight * s);
                    if (w <= 0 || h <= 0) continue;
                    var dv = new DrawingVisual();
                    using (DrawingContext dc = dv.RenderOpen())
                    {
                        dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, w, h));
                        dc.PushTransform(new ScaleTransform(s, s));
                        dc.DrawRectangle(new VisualBrush(v), null, new Rect(0, 0, v.ActualWidth, v.ActualHeight));
                        dc.Pop();
                    }
                    var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(dv);
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(rtb));
                    using (FileStream fs = File.Create(path)) enc.Save(fs);
                }
                catch (Exception ex) { Log.Write("snapshot " + f.Id, ex); }
            }
        }
    }
}

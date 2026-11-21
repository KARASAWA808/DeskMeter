using System;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace DeskMeter
{
    sealed class DiagnosisDialog : Window
    {
        readonly Button start = new Button { Content = "計測を開始", MinWidth = 90, IsDefault = true };
        readonly Button cancel = new Button { Content = "キャンセル", MinWidth = 90, IsEnabled = false };
        readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        readonly ProgressBar progressBar = new ProgressBar { Height = 6, Visibility = Visibility.Hidden };
        readonly TextBox report = new TextBox
        {
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(8),
            Visibility = Visibility.Hidden
        };
        CancellationTokenSource run;
        bool closeAfterCancellation, closed;

        public DiagnosisDialog()
        {
            Title = "PC の重さを診断";
            Width = 620;
            Height = 540;
            MinWidth = 380;
            MinHeight = 290;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontSize = 13;
            UseLayoutRounding = true;

            var root = new DockPanel { Margin = new Thickness(12) };
            Content = root;
            var instructions = new TextBlock
            {
                Text = "PC が重いと感じたときに計測を開始してください。CPU・GPU・メモリ・ネットワークを約 10 秒間計測します。",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
            };
            DockPanel.SetDock(instructions, Dock.Top);
            root.Children.Add(instructions);

            DockPanel.SetDock(status, Dock.Top);
            root.Children.Add(status);
            progressBar.Margin = new Thickness(0, 6, 0, 8);
            DockPanel.SetDock(progressBar, Dock.Top);
            root.Children.Add(progressBar);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var close = new Button { Content = "閉じる", MinWidth = 80 };
            foreach (Button button in new[] { start, cancel, close })
            {
                button.Margin = new Thickness(8, 8, 0, 0);
                button.Padding = new Thickness(8, 4, 8, 4);
                buttons.Children.Add(button);
            }
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);
            root.Children.Add(report);

            AutomationProperties.SetAutomationId(start, "DiagnosisStart");
            AutomationProperties.SetAutomationId(cancel, "DiagnosisCancel");
            AutomationProperties.SetAutomationId(close, "DiagnosisClose");
            AutomationProperties.SetAutomationId(status, "DiagnosisStatus");
            AutomationProperties.SetAutomationId(report, "DiagnosisReport");
            AutomationProperties.SetName(report, "診断結果");
            AutomationProperties.SetName(progressBar, "計測の進み具合");
            start.Click += StartMeasurement;
            cancel.Click += delegate { CancelMeasurement(); };
            close.Click += delegate { Close(); };
            Loaded += delegate { start.Focus(); };
        }

        async void StartMeasurement(object sender, RoutedEventArgs e)
        {
            if (run != null) return;
            var measurement = new CancellationTokenSource();
            run = measurement;
            start.IsEnabled = false;
            cancel.IsEnabled = true;
            cancel.Focus();
            progressBar.Value = 0;
            progressBar.Visibility = Visibility.Visible;
            report.Clear();
            report.Visibility = Visibility.Hidden;
            status.Text = "計測中…";

            try
            {
                var progress = new Progress<int>(delegate(int percent)
                {
                    if (!closed && run == measurement && !measurement.IsCancellationRequested) progressBar.Value = percent;
                });
                string result = await LoadDiagnosis.RunAsync(measurement.Token, progress);
                measurement.Token.ThrowIfCancellationRequested();
                if (!closed)
                {
                    report.Text = result;
                    report.ScrollToHome();
                    report.Visibility = Visibility.Visible;
                    status.Text = "計測が完了しました。";
                }
            }
            catch (Exception)
            {
                if (!closed)
                    status.Text = measurement.IsCancellationRequested ? "計測をキャンセルしました。" : "計測中に問題が発生しました。再計測できます。";
            }
            finally
            {
                run = null;
                measurement.Dispose();
                FinishMeasurement();
            }
        }

        void FinishMeasurement()
        {
            if (closed) return;
            if (closeAfterCancellation)
            {
                Dispatcher.BeginInvoke(new Action(delegate { if (!closed) Close(); }));
                return;
            }
            start.Content = "再計測";
            start.IsEnabled = true;
            cancel.IsEnabled = false;
            progressBar.Visibility = Visibility.Hidden;
        }

        internal void CancelMeasurement()
        {
            if (run == null || run.IsCancellationRequested) return;
            cancel.IsEnabled = false;
            status.Text = "計測をキャンセルしています…";
            run.Cancel();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (run != null)
            {
                // 計測が停止するまで同じウィンドウを保持し、閉じてすぐ開き直しても計測が重複しないようにする。
                closeAfterCancellation = true;
                e.Cancel = true;
                CancelMeasurement();
            }
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            closed = true;
            CancelMeasurement();
            report.Clear();
            base.OnClosed(e);
        }
    }
}

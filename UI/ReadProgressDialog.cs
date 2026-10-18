using System.Diagnostics;
using System.Runtime.ExceptionServices;
using MyAceEditor.PerfectWorld;

namespace MyAceEditor.UI;

/// <summary>Janela modal com laço de mensagens normal; a leitura roda em Task.Run, sem bloquear a thread da interface.</summary>
internal sealed class ReadProgressDialog : Form
{
    private sealed class LatestProgress : IProgress<LoadProgress>
    {
        private LoadProgress? _value;
        public void Report(LoadProgress value) => Interlocked.Exchange(ref _value, value);
        public LoadProgress? Read() => Volatile.Read(ref _value);
    }
    private readonly Label _stage = new() { Dock = DockStyle.Top, Height = 32, Text = Loc.T("progress.preparing", "Preparando leitura…") };
    private readonly Label _detail = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly Label _elapsed = new() { Dock = DockStyle.Bottom, Height = 26 };
    private readonly ProgressBar _bar = new() { Dock = DockStyle.Top, Height = 24, Style = ProgressBarStyle.Marquee };
    private readonly Button _cancel = new() { Text = Loc.T("progress.cancel", "Cancelar leitura"), Width = 110, Height = 32 };
    private readonly System.Windows.Forms.Timer _pulse = new() { Interval = 100 };
    private readonly CancellationTokenSource _cancellation = new();
    private readonly LatestProgress _latest = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private bool _finished;
    private readonly bool _allowCancel;

    private ReadProgressDialog(string title, bool allowCancel)
    {
        _allowCancel = allowCancel;
        _stage.Text = $"{title}…";
        Text = title; Width = 720; Height = 240; MinimumSize = new Size(500, 220);
        StartPosition = FormStartPosition.CenterParent; MinimizeBox = false; MaximizeBox = false;
        ShowInTaskbar = false; Font = new Font("Microsoft YaHei UI", 10); Padding = new Padding(18);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.Add(_cancel);
        _cancel.Visible = allowCancel; ControlBox = allowCancel;
        Controls.Add(_detail); Controls.Add(_bar); Controls.Add(_stage); Controls.Add(_elapsed); Controls.Add(buttons);
        _cancel.Click += (_, _) => RequestCancel();
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { RequestCancel(); e.SuppressKeyPress = true; } };
        _pulse.Tick += (_, _) =>
        {
            var value = _latest.Read();
            _elapsed.Text = Loc.T("progress.elapsed", "Tempo decorrido") + $" {_clock.Elapsed:hh\\:mm\\:ss}";
            if (_cancellation.IsCancellationRequested) return;
            if (value is null) return;
            _stage.Text = $"{value.Percent}% · {value.Stage}";
            _detail.Text = value.Detail;
            _bar.Style = value.Indeterminate ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
            if (!value.Indeterminate) _bar.Value = Math.Clamp(value.Percent, 0, 100);
        };
        FormClosing += (_, e) => { if (!_finished) { e.Cancel = true; RequestCancel(); } };
    }

    private void RequestCancel()
    {
        if (!_allowCancel || _finished || _cancellation.IsCancellationRequested) return;
        _cancel.Enabled = false; _stage.Text = Loc.T("progress.cancelling", "Cancelando leitura…");
        _detail.Text = Loc.T("progress.cancelling.detail", "Encerrando a etapa atual; os dados já abertos serão mantidos.");
        _cancellation.Cancel();
    }

    public static T Run<T>(IWin32Window owner, string title, Func<LoadContext, T> work,
        bool allowCancel = true, string completedStage = "Concluído") where T : class
    {
        using var dialog = new ReadProgressDialog(title, allowCancel);
        T? result = null; Exception? failure = null; bool cancelled = false;
        dialog.Shown += async (_, _) =>
        {
            dialog._pulse.Start();
            try
            {
                result = await Task.Run(() =>
                {
                    var context = new LoadContext(dialog._latest, dialog._cancellation.Token);
                    var loaded = work(context);
                    context.Report(1, completedStage, "atualizando a tela…", force: true);
                    return loaded;
                }, dialog._cancellation.Token);
                dialog._cancellation.Token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (dialog._cancellation.IsCancellationRequested) { cancelled = true; }
            catch (Exception ex) { if (dialog._cancellation.IsCancellationRequested) cancelled = true; else failure = ex; }
            finally
            {
                dialog._finished = true; dialog._pulse.Stop();
                dialog.DialogResult = cancelled ? DialogResult.Cancel : failure is null ? DialogResult.OK : DialogResult.Abort;
                dialog.Close();
            }
        };
        dialog.ShowDialog(owner);
        if (cancelled) throw new OperationCanceledException("Leitura cancelada.", dialog._cancellation.Token);
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        return result ?? throw new InvalidOperationException("A operação não retornou resultado.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _pulse.Dispose(); _cancellation.Dispose(); }
        base.Dispose(disposing);
    }
}

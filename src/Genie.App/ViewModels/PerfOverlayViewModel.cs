using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using Avalonia.Threading;
using Genie.Core.Diagnostics;
using Genie.Core.Scripting.Js;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Genie.App.ViewModels;

/// <summary>One running <c>.js</c> script's row in the performance overlay —
/// name, wall-clock elapsed, and paused state, refreshed live.</summary>
public sealed class JsScriptRowViewModel : ReactiveObject
{
    public JsScriptRowViewModel(string name) => Name = name;
    public string Name { get; }
    [Reactive] public string Elapsed { get; set; } = "0s";
    [Reactive] public bool   Paused  { get; set; }
}

/// <summary>One pipeline stage's row in the performance overlay. Carries its own
/// <see cref="Visible"/> flag so each component can be shown/hidden independently,
/// plus the formatted live numbers refreshed by <see cref="PerfOverlayViewModel"/>.</summary>
public sealed class PerfRowViewModel : ReactiveObject
{
    public PerfRowViewModel(PipelineStage stage)
    {
        Stage = stage;
        Name  = stage.ToString();
    }

    public PipelineStage Stage { get; }
    public string        Name  { get; }

    /// <summary>Per-component visibility in the overlay (independent of safety).</summary>
    [Reactive] public bool   Visible  { get; set; } = true;
    [Reactive] public string Avg      { get; set; } = "0.00";
    [Reactive] public string Peak     { get; set; } = "0.00";
    [Reactive] public string Last     { get; set; } = "0.00";
    [Reactive] public long   Timeouts    { get; set; }
    [Reactive] public bool   HasTimeouts { get; set; }
    /// <summary>True when the stage's peak crossed the frame budget or any regex
    /// timed out — the overlay paints these rows red.</summary>
    [Reactive] public bool   Hot         { get; set; }
}

/// <summary>
/// View-model behind the live performance overlay. Polls
/// <see cref="PipelineMetrics.Snapshot"/> on a UI-thread timer (only while the
/// overlay is shown) and pushes formatted numbers into the per-stage rows.
/// Showing the overlay flips <see cref="PipelineMetrics.Enabled"/> on, so timing
/// is collected only when someone is watching.
/// </summary>
public sealed class PerfOverlayViewModel : ReactiveObject, IDisposable
{
    private const double HotBudgetMs = 16.0;   // one 60 fps frame

    private readonly DispatcherTimer _timer;
    private PipelineMetrics?         _metrics;

    public ObservableCollection<PerfRowViewModel> Rows { get; } = new();

    /// <summary>Live list of running <c>.js</c> scripts (Part B of the JS overlay).</summary>
    public ObservableCollection<JsScriptRowViewModel> JsScripts { get; } = new();

    /// <summary>True while at least one <c>.js</c> is running — gates the
    /// running-.js section's visibility in the overlay.</summary>
    [Reactive] public bool HasJsScripts { get; private set; }

    /// <summary>Set by the host to supply running-<c>.js</c> stats each refresh.
    /// Null = no JS section (e.g. before connect).</summary>
    public Func<System.Collections.Generic.IReadOnlyList<JsScriptStat>>? JsStatsProvider { get; set; }

    /// <summary>Master visibility — drives both the overlay panel and whether
    /// metrics are being collected.</summary>
    [Reactive] public bool OverlayVisible { get; private set; }

    /// <summary>Zero all counters for a clean reading.</summary>
    public ReactiveCommand<Unit, Unit> ResetCommand { get; }

    public PerfOverlayViewModel()
    {
        foreach (var stage in Enum.GetValues<PipelineStage>())
            Rows.Add(new PerfRowViewModel(stage));

        ResetCommand = ReactiveCommand.Create(() => { _metrics?.Reset(); Refresh(); });

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Refresh();
    }

    /// <summary>Bind this session's metrics; re-applies the collection gate.</summary>
    public void Attach(PipelineMetrics metrics)
    {
        _metrics = metrics;
        _metrics.Enabled = OverlayVisible;
        if (OverlayVisible) { Refresh(); _timer.Start(); }
    }

    /// <summary>Session ended — stop polling and stop collecting.</summary>
    public void Detach()
    {
        _timer.Stop();
        if (_metrics is not null) _metrics.Enabled = false;
        _metrics = null;
        JsStatsProvider = null;
        JsScripts.Clear();
        HasJsScripts = false;
    }

    public void Toggle() => SetVisible(!OverlayVisible);

    private void SetVisible(bool on)
    {
        OverlayVisible = on;
        if (_metrics is not null) _metrics.Enabled = on;
        if (on) { Refresh(); _timer.Start(); }
        else      _timer.Stop();
    }

    private void Refresh()
    {
        if (_metrics is null) return;
        foreach (var s in _metrics.Snapshot())
        {
            var row = Rows[(int)s.Stage];
            row.Avg      = s.AvgMs.ToString("0.00");
            row.Peak     = s.PeakMs.ToString("0.00");
            row.Last     = s.LastMs.ToString("0.00");
            row.Timeouts    = s.Timeouts;
            row.HasTimeouts = s.Timeouts > 0;
            row.Hot         = s.PeakMs >= HotBudgetMs || s.Timeouts > 0;
        }
        RefreshJsScripts();
    }

    /// <summary>Reconcile the running-<c>.js</c> list against the live stats —
    /// update elapsed/paused in place, drop finished scripts, add new ones.</summary>
    private void RefreshJsScripts()
    {
        var stats = JsStatsProvider?.Invoke() ?? Array.Empty<JsScriptStat>();

        for (int i = JsScripts.Count - 1; i >= 0; i--)
            if (!stats.Any(s => s.Name == JsScripts[i].Name))
                JsScripts.RemoveAt(i);

        foreach (var s in stats)
        {
            var row = JsScripts.FirstOrDefault(r => r.Name == s.Name);
            if (row is null) { row = new JsScriptRowViewModel(s.Name); JsScripts.Add(row); }
            row.Elapsed = FormatElapsed(s.ElapsedSec);
            row.Paused  = s.Paused;
        }

        HasJsScripts = JsScripts.Count > 0;
    }

    private static string FormatElapsed(double sec)
    {
        if (sec < 60)   return $"{sec:0}s";
        var m = (int)(sec / 60);
        var s = (int)(sec % 60);
        if (m < 60)     return $"{m}:{s:D2}";
        var h = m / 60; m %= 60;
        return $"{h}:{m:D2}:{s:D2}";
    }

    public void Dispose() => _timer.Stop();
}

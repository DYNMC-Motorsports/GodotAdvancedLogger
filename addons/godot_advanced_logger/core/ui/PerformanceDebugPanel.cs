using System;
using System.Collections.Generic;
using System.Text;
using Godot;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.core.ui;

/// <summary>
/// Built-in "Performance" panel: live engine counters sourced from
/// <see cref="Performance.GetMonitor"/>, plus a frame-time sparkline.
/// </summary>
public class PerformanceDebugPanel : IDebugPanel
{
    /// <summary>How often the numbers are rewritten. Every frame would be unreadable.</summary>
    private const float RefreshIntervalSeconds = 0.25f;

    /// <summary>Number of frame-time samples kept for the sparkline and the min/avg/max row.</summary>
    private const int SampleCount = 60;

    /// <summary>Block-drawing characters, lowest to highest bar.</summary>
    private const string SparkChars = "▁▂▃▄▅▆▇█";

    /// <summary>Placeholder for a sample slot not yet filled, so the label width never changes.</summary>
    private const char SparkEmpty = '·';

    /// <summary>Never render fewer sparkline columns than this, however narrow the panel gets.</summary>
    private const int MinSparkColumns = 8;

    private const int LabelColumnWidth = 150;
    private const int ValueColumnWidth = 120;

    private readonly Dictionary<string, Label> _valueLabels = new();

    private readonly float[] _frameSamples = new float[SampleCount];
    private int _sampleIndex;
    private bool _sampleBufferFilled;

    private Label _sparkLabel;
    private Label _sparkRangeLabel;
    private Label _statusLabel;

    private float _lastSparkWidth = -1f;
    private int _visibleSamples = SampleCount;

    private float _timeSinceRefresh;
    private int _refreshCount;
    private float _timeSinceRateSample;
    private int _framesSinceRateSample;

    public string PanelName => "Performance";

    public void OnPanelHide() { }

    /// <summary>Discard the sample window on open so the graph reflects now, not the last visit.</summary>
    public void OnPanelShow()
    {
        _sampleIndex = 0;
        _sampleBufferFilled = false;
        _timeSinceRefresh = RefreshIntervalSeconds;
        Array.Clear(_frameSamples, 0, _frameSamples.Length);
    }

    public Control GetPanelUi()
    {
        var monoFont = new SystemFont { FontNames = ["Consolas", "Courier New", "Monospace"] };

        var root = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };

        // Everything lives inside a ScrollContainer: the counter list is taller than the
        // overlay on smaller windows, and a plain VBoxContainer would compress its children
        // until rows overlap or disappear rather than letting the user scroll to them.
        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,

            // Never scroll sideways. Combined with ClipText on every label whose text
            // changes, the content can no longer demand more width than the panel has.
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        root.AddChild(scroll);

        var content = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        scroll.AddChild(content);

        content.AddChild(new Label
        {
            Text = $"Frame time, last {SampleCount} frames (newest on the right)",

            // Wraps instead of setting a wide floor on the panel's minimum size.
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        });

        // ClipText is what actually stops the panel from resizing itself: without it a
        // Label's minimum width equals its rendered text width, so every value that grew a
        // digit widened the grid, then the content, then produced a horizontal scrollbar.
        _sparkLabel = new Label
        {
            Text = new string(SparkEmpty, SampleCount),
            ClipText = true
        };
        _sparkLabel.AddThemeFontOverride("font", monoFont);
        content.AddChild(_sparkLabel);

        _sparkRangeLabel = new Label { Text = "-", ClipText = true };
        _sparkRangeLabel.AddThemeFontOverride("font", monoFont);
        content.AddChild(_sparkRangeLabel);

        content.AddChild(new HSeparator());

        var grid = new GridContainer
        {
            Columns = 2,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        content.AddChild(grid);

        AddSection(grid, "Timing");
        AddRow(grid, monoFont, "FPS");
        AddRow(grid, monoFont, "Frame time");
        AddRow(grid, monoFont, "Process (script)");
        AddRow(grid, monoFont, "Physics (script)");

        AddSection(grid, "Memory");
        AddRow(grid, monoFont, "Static");
        AddRow(grid, monoFont, "Static peak");
        AddRow(grid, monoFont, "Video");
        AddRow(grid, monoFont, "Texture");

        AddSection(grid, "Rendering");
        AddRow(grid, monoFont, "Draw calls");
        AddRow(grid, monoFont, "Objects drawn");
        AddRow(grid, monoFont, "Primitives");

        AddSection(grid, "Scene");
        AddRow(grid, monoFont, "Nodes");
        AddRow(grid, monoFont, "Orphan nodes");
        AddRow(grid, monoFont, "Objects");
        AddRow(grid, monoFont, "Resources");

        _statusLabel = new Label { Text = string.Empty, ClipText = true };
        root.AddChild(_statusLabel);

        // Populate immediately so the panel is never blank on first open.
        Refresh();

        return root;
    }

    private static void AddSection(GridContainer grid, string title)
    {
        grid.AddChild(new Label { Text = title });
        grid.AddChild(new Label { Text = string.Empty });
    }

    private void AddRow(GridContainer grid, Font font, string label)
    {
        grid.AddChild(new Label
        {
            Text = "  " + label,

            // Fixed column widths keep the grid from re-laying-out every refresh as
            // numbers change width, which reads as the whole panel flickering.
            CustomMinimumSize = new Vector2(LabelColumnWidth, 0)
        });

        var value = new Label
        {
            Text = "-",
            HorizontalAlignment = HorizontalAlignment.Right,
            CustomMinimumSize = new Vector2(ValueColumnWidth, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,

            // The value is the text that changes every refresh, so this is the label that
            // must not report its content as a minimum width.
            ClipText = true
        };
        value.AddThemeFontOverride("font", font);

        grid.AddChild(value);
        _valueLabels[label] = value;
    }

    public void OnPanelUpdate(float delta)
    {
        // Sample every frame so the sparkline shows real spikes, but only rewrite the
        // number labels a few times a second - otherwise they are impossible to read.
        PushSample(delta * 1000.0f);

        _framesSinceRateSample++;
        _timeSinceRateSample += delta;

        _timeSinceRefresh += delta;
        if (_timeSinceRefresh < RefreshIntervalSeconds) return;

        _timeSinceRefresh = 0f;
        _refreshCount++;
        Refresh();
    }

    private void PushSample(float frameMilliseconds)
    {
        _frameSamples[_sampleIndex] = frameMilliseconds;
        _sampleIndex++;

        if (_sampleIndex >= SampleCount)
        {
            _sampleIndex = 0;
            _sampleBufferFilled = true;
        }
    }

    private void Refresh()
    {
        var stats = ComputeStats();

        // Frame time comes from the measured frame delta. The engine's TimeProcess /
        // TimePhysicsProcess monitors are script time only - reporting their sum as
        // "frame time" would understate the real frame budget, sometimes by a lot.
        SetValue("FPS", Monitor(Performance.Monitor.TimeFps, v => v.ToString("F0")));
        SetValue("Frame time", stats.Count > 0 ? $"{stats.Current:F2} ms" : "-");
        SetValue("Process (script)", Monitor(Performance.Monitor.TimeProcess, FormatSecondsAsMilliseconds));
        SetValue("Physics (script)", Monitor(Performance.Monitor.TimePhysicsProcess, FormatSecondsAsMilliseconds));

        SetValue("Static", Monitor(Performance.Monitor.MemoryStatic, FormatBytes));
        SetValue("Static peak", Monitor(Performance.Monitor.MemoryStaticMax, FormatBytes));
        SetValue("Video", Monitor(Performance.Monitor.RenderVideoMemUsed, FormatBytes));
        SetValue("Texture", Monitor(Performance.Monitor.RenderTextureMemUsed, FormatBytes));

        SetValue("Draw calls", Monitor(Performance.Monitor.RenderTotalDrawCallsInFrame, FormatCount));
        SetValue("Objects drawn", Monitor(Performance.Monitor.RenderTotalObjectsInFrame, FormatCount));
        SetValue("Primitives", Monitor(Performance.Monitor.RenderTotalPrimitivesInFrame, FormatCount));

        SetValue("Nodes", Monitor(Performance.Monitor.ObjectNodeCount, FormatCount));
        SetValue("Orphan nodes", Monitor(Performance.Monitor.ObjectOrphanNodeCount, FormatCount));
        SetValue("Objects", Monitor(Performance.Monitor.ObjectCount, FormatCount));
        SetValue("Resources", Monitor(Performance.Monitor.ObjectResourceCount, FormatCount));

        UpdateSparkline(stats);
        UpdateStatus(stats);
    }

    /// <summary>
    /// Reads one engine monitor, containing any failure to that single row.
    /// </summary>
    /// <remarks>
    /// Not every monitor is populated on every platform and renderer. Previously a single
    /// failing read would abort the whole refresh, leaving every row below it stale - so
    /// one unsupported counter could look like "the panel stopped updating".
    /// </remarks>
    private static string Monitor(Performance.Monitor monitor, Func<double, string> format)
    {
        try
        {
            return format(Performance.GetMonitor(monitor));
        }
        catch (Exception e)
        {
            return $"n/a ({e.GetType().Name})";
        }
    }

    private readonly struct FrameStats
    {
        public int Count { get; init; }
        public float Current { get; init; }
        public float Min { get; init; }
        public float Max { get; init; }
        public float Average { get; init; }
    }

    private FrameStats ComputeStats()
    {
        int count = _sampleBufferFilled ? SampleCount : _sampleIndex;
        if (count == 0) return new FrameStats();

        float min = float.MaxValue;
        float max = float.MinValue;
        float total = 0f;

        for (int i = 0; i < count; i++)
        {
            float sample = _frameSamples[i];
            if (sample < min) min = sample;
            if (sample > max) max = sample;
            total += sample;
        }

        // The most recently written slot is the one before the write cursor.
        int newest = (_sampleIndex - 1 + SampleCount) % SampleCount;

        return new FrameStats
        {
            Count = count,
            Current = _frameSamples[newest],
            Min = min,
            Max = max,
            Average = total / count
        };
    }

    /// <summary>
    /// Renders the frame-time history as block characters scaled between the smallest and
    /// largest sample in the window, so a spike is visible regardless of the base frame rate.
    /// </summary>
    private void UpdateSparkline(in FrameStats stats)
    {
        if (_sparkLabel == null) return;

        int columns = GetVisibleSampleCount();

        if (stats.Count == 0)
        {
            _sparkLabel.Text = new string(SparkEmpty, columns);
            return;
        }

        float range = stats.Max - stats.Min;
        int shown = Math.Min(columns, stats.Count);

        var sb = new StringBuilder(columns);

        // Pad on the left while the buffer warms up, so the row keeps a constant length
        // and the newest sample always sits at the right edge.
        sb.Append(SparkEmpty, columns - shown);

        for (int i = 0; i < shown; i++)
        {
            // Walk the ring buffer oldest-first over the newest `shown` samples, so the
            // most recent frame lands on the right.
            int offset = stats.Count - shown + i;
            int index = _sampleBufferFilled ? (_sampleIndex + offset) % SampleCount : offset;

            float normalized = range > 0.0001f ? (_frameSamples[index] - stats.Min) / range : 0f;
            int bucket = Mathf.Clamp((int)(normalized * (SparkChars.Length - 1)), 0, SparkChars.Length - 1);

            sb.Append(SparkChars[bucket]);
        }

        _sparkLabel.Text = sb.ToString();

        if (_sparkRangeLabel != null)
        {
            _sparkRangeLabel.Text = $"min {stats.Min:F2} ms   avg {stats.Average:F2} ms   max {stats.Max:F2} ms";
        }
    }

    /// <summary>
    /// How many sparkline characters fit in the label's current width.
    /// </summary>
    /// <remarks>
    /// The sparkline is the one label whose content is inherently wide. Clipping it would
    /// cut off the right-hand side - which is the newest and most interesting data - so
    /// instead it is rendered to fit, and shows fewer frames on a narrow overlay.
    /// The measurement is cached and only redone when the label's width actually changes.
    /// </remarks>
    private int GetVisibleSampleCount()
    {
        if (_sparkLabel == null) return SampleCount;

        float width = _sparkLabel.Size.X;

        // Before the first layout pass the width is meaningless; fall back to the full set.
        if (width <= 1f) return SampleCount;
        if (Mathf.Abs(width - _lastSparkWidth) < 1f) return _visibleSamples;

        _lastSparkWidth = width;
        _visibleSamples = SampleCount;

        try
        {
            var font = _sparkLabel.GetThemeFont("font");
            int fontSize = _sparkLabel.GetThemeFontSize("font_size");

            if (font != null && fontSize > 0)
            {
                // The font is monospaced, so one representative glyph gives the column width.
                float charWidth = font.GetStringSize("0", HorizontalAlignment.Left, -1, fontSize).X;

                if (charWidth > 0.1f)
                {
                    _visibleSamples = Mathf.Clamp((int)(width / charWidth), MinSparkColumns, SampleCount);
                }
            }
        }
        catch (Exception)
        {
            // Theme lookups can fail before the label is in the tree; the default stands.
        }

        return _visibleSamples;
    }

    /// <summary>
    /// Shows how often this panel is actually being refreshed. If these numbers are far
    /// below the game's frame rate, the panel is not receiving <see cref="OnPanelUpdate"/> -
    /// which is a very different problem from the counters themselves being wrong.
    /// </summary>
    private void UpdateStatus(in FrameStats stats)
    {
        if (_statusLabel == null) return;

        float sampledFps = _timeSinceRateSample > 0.0001f ? _framesSinceRateSample / _timeSinceRateSample : 0f;

        _framesSinceRateSample = 0;
        _timeSinceRateSample = 0f;

        _statusLabel.Text = $"  panel updates: {sampledFps:F0}/s   refreshes: {_refreshCount}   samples: {stats.Count}/{SampleCount}";
    }

    private void SetValue(string label, string value)
    {
        if (_valueLabels.TryGetValue(label, out var control)) control.Text = value + "  ";
    }

    private static string FormatSecondsAsMilliseconds(double seconds) => (seconds * 1000.0).ToString("F2") + " ms";

    private static string FormatCount(double value) => ((long)value).ToString("N0");

    private static string FormatBytes(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        int unit = 0;

        while (value >= 1024.0 && unit < units.Length - 1)
        {
            value /= 1024.0;
            unit++;
        }

        return $"{value:F1} {units[unit]}";
    }
}

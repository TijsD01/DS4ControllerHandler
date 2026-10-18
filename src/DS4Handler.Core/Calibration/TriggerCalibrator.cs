namespace DS4Handler.Core.Calibration;

/// <summary>Tunables for <see cref="TriggerCalibrator"/>. Values are in raw counts (0..255).</summary>
public sealed record TriggerCalibrationOptions
{
    /// <summary>Samples in a row that must stay within <see cref="StableTolerance"/> to count as a held position.</summary>
    public int StableWindow { get; init; } = 6;

    /// <summary>How far (in counts) a held value may wobble and still count as held.</summary>
    public float StableTolerance { get; init; } = 2f;

    /// <summary>A press starts when the value rises this many counts above the rest position.</summary>
    public float PressThreshold { get; init; } = 40f;

    /// <summary>Recent presses kept to find the trigger's real end stop.</summary>
    public int PeakHistory { get; init; } = 16;

    /// <summary>Presses that must end at the same spot before it is trusted as the end stop.</summary>
    public int PeaksForEndStop { get; init; } = 3;

    /// <summary>Stronger evidence needed before the learned end stop is lowered.</summary>
    public int PeaksToLowerEndStop { get; init; } = 5;

    /// <summary>Peaks within this many counts of each other are treated as the same end stop.</summary>
    public float ClusterTolerance { get; init; } = 4f;

    /// <summary>The learned end stop is never set below this (counts), so a bad guess cannot make light presses read as full.</summary>
    public float MinimumEndStop { get; init; } = 128f;

    /// <summary>Deadzone above the learned rest position (counts).</summary>
    public float RestDeadzone { get; init; } = 2f;

    /// <summary>The output reaches 100% this many counts before the end stop, so jitter at full press still reads as full.</summary>
    public float SaturationMargin { get; init; } = 2f;

    /// <summary>Largest rest position the calibrator will accept (counts); a higher resting value is treated as a press.</summary>
    public float MaxRest { get; init; } = 60f;
}

/// <summary>Learned state of one trigger, for saving to a profile.</summary>
public sealed record TriggerCalibrationData
{
    public float Rest { get; init; }
    public float EndStop { get; init; } = 255f;
    public bool EndStopLearned { get; init; }
    public int PressCount { get; init; }
    public float[] RecentPeaks { get; init; } = Array.Empty<float>();
}

/// <summary>
/// Learns the real travel of an analog trigger from normal play and remaps it to the full 0..1 range.
///
/// A worn trigger often stops short (an R2 that never gets past 200 of 255). The calibrator watches
/// each press and records where the trigger comes to a stop. A mechanical end stop is repeatable:
/// press after press lands on the same value, while partial presses land all over the place. Once
/// several presses agree on a value, that value becomes the new 100%.
/// </summary>
public sealed class TriggerCalibrator
{
    private readonly TriggerCalibrationOptions _o;
    private readonly float[] _window;
    private int _windowCount;
    private int _windowIndex;

    private readonly Queue<float> _peaks = new();
    private bool _inPress;
    private float _pressPeak;

    private float _rest;
    private float _endStop = 255f;
    private bool _endStopLearned;
    private int _pressCount;

    public TriggerCalibrator(TriggerCalibrationOptions? options = null)
    {
        _o = options ?? new TriggerCalibrationOptions();
        _window = new float[Math.Max(2, _o.StableWindow)];
    }

    /// <summary>Learned rest position in counts.</summary>
    public float Rest => _rest;

    /// <summary>Learned end stop (full press) in counts; 255 until learned.</summary>
    public float EndStop => _endStop;

    public bool EndStopLearned => _endStopLearned;

    public int PressCount => _pressCount;

    /// <summary>When false, the calibrator only maps values and stops learning.</summary>
    public bool LearningEnabled { get; set; } = true;

    /// <summary>Feeds one raw sample (0..255), updates the model and returns the calibrated value (0..1).</summary>
    public float Process(byte raw)
    {
        if (LearningEnabled) Learn(raw);
        return Map(raw);
    }

    /// <summary>Maps a raw value with the current model, without learning.</summary>
    public float Map(float raw)
    {
        float low = _rest + _o.RestDeadzone;
        float high = Math.Max(low + 1f, _endStop - _o.SaturationMargin);
        return Math.Clamp((raw - low) / (high - low), 0f, 1f);
    }

    /// <summary>Forgets everything learned and goes back to factory 0..255.</summary>
    public void Reset()
    {
        _rest = 0;
        _endStop = 255f;
        _endStopLearned = false;
        _pressCount = 0;
        _peaks.Clear();
        _inPress = false;
        _windowCount = 0;
    }

    public TriggerCalibrationData Export() => new()
    {
        Rest = _rest,
        EndStop = _endStop,
        EndStopLearned = _endStopLearned,
        PressCount = _pressCount,
        RecentPeaks = _peaks.ToArray(),
    };

    public void Import(TriggerCalibrationData data)
    {
        Reset();
        _rest = Math.Clamp(data.Rest, 0f, _o.MaxRest);
        _endStop = Math.Clamp(data.EndStop, _o.MinimumEndStop, 255f);
        _endStopLearned = data.EndStopLearned;
        _pressCount = data.PressCount;
        foreach (var p in data.RecentPeaks.TakeLast(_o.PeakHistory)) _peaks.Enqueue(p);
    }

    private void Learn(byte raw)
    {
        _window[_windowIndex] = raw;
        _windowIndex = (_windowIndex + 1) % _window.Length;
        if (_windowCount < _window.Length) _windowCount++;

        bool stable = TryGetStableValue(out float held);

        if (!_inPress)
        {
            if (raw > _rest + _o.PressThreshold)
            {
                _inPress = true;
                _pressPeak = float.NaN;
            }
            else if (stable && held <= _o.MaxRest)
            {
                // Released and still: follow the rest position slowly, but drop quickly if it goes lower.
                _rest = held < _rest ? held : _rest + (held - _rest) * 0.02f;
            }
        }

        if (_inPress)
        {
            if (stable && (float.IsNaN(_pressPeak) || held > _pressPeak)) _pressPeak = held;

            if (raw < _rest + _o.PressThreshold / 2f)
            {
                _inPress = false;
                if (!float.IsNaN(_pressPeak)) RecordPeak(_pressPeak);
            }
        }
    }

    private bool TryGetStableValue(out float value)
    {
        value = 0;
        if (_windowCount < _window.Length) return false;
        float min = float.MaxValue, max = float.MinValue, sum = 0;
        foreach (var v in _window)
        {
            min = Math.Min(min, v);
            max = Math.Max(max, v);
            sum += v;
        }
        if (max - min > _o.StableTolerance) return false;
        value = sum / _window.Length;
        return true;
    }

    private void RecordPeak(float peak)
    {
        _pressCount++;
        _peaks.Enqueue(peak);
        while (_peaks.Count > _o.PeakHistory) _peaks.Dequeue();

        if (!TryFindEndStop(out float stop, out int support)) return;

        if (stop >= _endStop - _o.ClusterTolerance)
        {
            // The trigger reaches at least as far as we thought: accept right away.
            _endStop = Math.Min(255f, Math.Max(stop, _o.MinimumEndStop));
            _endStopLearned = true;
        }
        else if (support >= (_endStopLearned ? _o.PeaksToLowerEndStop : _o.PeaksForEndStop)
                 && !_peaks.Any(p => p > stop + _o.ClusterTolerance * 2))
        {
            // The trigger stops shorter than we assumed: first time (worn from the start) or it wore
            // further. No recent press may have gone clearly past the new value, and lowering an
            // already learned end stop needs stronger evidence.
            _endStop = Math.Max(stop, _o.MinimumEndStop);
            _endStopLearned = true;
        }
    }

    /// <summary>
    /// Finds the highest value that at least <see cref="TriggerCalibrationOptions.PeaksForEndStop"/>
    /// recent presses agree on. Returns the lower edge of that group so the output reliably hits 100%.
    /// </summary>
    private bool TryFindEndStop(out float stop, out int support)
    {
        stop = 0;
        support = 0;
        var sorted = _peaks.OrderByDescending(p => p).ToArray();
        for (int i = 0; i < sorted.Length; i++)
        {
            float top = sorted[i];
            int j = i;
            while (j + 1 < sorted.Length && top - sorted[j + 1] <= _o.ClusterTolerance) j++;
            int count = j - i + 1;
            if (count >= _o.PeaksForEndStop)
            {
                // Median of the group, nudged down so the bulk of presses saturate.
                var group = sorted[i..(j + 1)];
                stop = Math.Min(group[group.Length / 2], sorted[j] + _o.ClusterTolerance / 2f);
                support = count;
                return true;
            }
        }
        return false;
    }
}

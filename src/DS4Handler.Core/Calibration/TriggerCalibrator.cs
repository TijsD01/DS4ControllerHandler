namespace DS4Handler.Core.Calibration;

/// <summary>
/// How flat a held trigger position was. A mechanical end stop pins the value; a finger holding a
/// partial press always wobbles a little.
/// </summary>
public enum PlateauKind
{
    /// <summary>Held, but noisy (spread up to <see cref="TriggerCalibrationOptions.LoosePlateauSpread"/>).</summary>
    Loose = 0,

    /// <summary>Held steadily (spread up to <see cref="TriggerCalibrationOptions.SteadyPlateauSpread"/>) for a sustained run.</summary>
    Steady = 1,

    /// <summary>Pinned against a hard stop: practically no movement (spread up to <see cref="TriggerCalibrationOptions.PinnedPlateauSpread"/>).</summary>
    Pinned = 2,
}

/// <summary>Tunables for <see cref="TriggerCalibrator"/>. Values are in raw counts (0..255), samples assume ~250 Hz.</summary>
public sealed record TriggerCalibrationOptions
{
    /// <summary>Samples in a row that must stay within <see cref="StableTolerance"/> to count as a still (released) trigger.</summary>
    public int StableWindow { get; init; } = 6;

    /// <summary>How far (in counts) a released trigger may wobble and still count as still.</summary>
    public float StableTolerance { get; init; } = 2f;

    /// <summary>A press starts when the value rises this many counts above the rest position.</summary>
    public float PressThreshold { get; init; } = 40f;

    // ---- plateaus (where a press comes to a stop)

    /// <summary>Largest max-min spread of a loose plateau.</summary>
    public float LoosePlateauSpread { get; init; } = 4f;

    /// <summary>Samples a loose plateau must last.</summary>
    public int LoosePlateauSamples { get; init; } = 30;

    /// <summary>Largest max-min spread of a steady plateau (about ±1 count).</summary>
    public float SteadyPlateauSpread { get; init; } = 2f;

    /// <summary>Samples a steady or pinned plateau must last (about 0.25 s).</summary>
    public int SteadyPlateauSamples { get; init; } = 60;

    /// <summary>Largest max-min spread of a pinned plateau (the value sits on one or two adjacent counts).</summary>
    public float PinnedPlateauSpread { get; init; } = 1f;

    // ---- end stop decisions

    /// <summary>Recent presses kept to find the trigger's real end stop.</summary>
    public int PeakHistory { get; init; } = 16;

    /// <summary>Steady or pinned presses that must agree before an end stop is first learned.</summary>
    public int SteadyPeaksForEndStop { get; init; } = 5;

    /// <summary>Presses of any kind that must agree before an end stop is first learned from loose plateaus only.</summary>
    public int LoosePeaksForEndStop { get; init; } = 8;

    /// <summary>
    /// Agreeing presses needed to lower an already learned end stop. They must also be at least as
    /// flat as the presses that confirmed the current end stop, so a wobbly partial hold can never
    /// lower an end stop that was confirmed by pinned full presses.
    /// </summary>
    public int PeaksToLowerEndStop { get; init; } = 8;

    /// <summary>Agreeing presses needed to raise the end stop (the trigger reaches further than we thought).</summary>
    public int PeaksToRaiseEndStop { get; init; } = 3;

    /// <summary>Peaks within this many counts of each other are treated as the same end stop.</summary>
    public float ClusterTolerance { get; init; } = 4f;

    /// <summary>The learned end stop is never set below this (counts), so a bad guess cannot make light presses read as full.</summary>
    public float MinimumEndStop { get; init; } = 160f;

    // ---- rest position

    /// <summary>Deadzone above the learned rest position (counts).</summary>
    public float RestDeadzone { get; init; } = 2f;

    /// <summary>The output reaches 100% this many counts before the end stop, so jitter at full press still reads as full.</summary>
    public float SaturationMargin { get; init; } = 2f;

    /// <summary>Largest rest position the calibrator will accept (counts).</summary>
    public float MaxRest { get; init; } = 20f;

    /// <summary>The rest position only follows a still value that is at most this many counts above it.</summary>
    public float RestRiseWindow { get; init; } = 4f;

    /// <summary>Largest rise of the rest position per sample (0.004 ≈ 1 count per second at 250 Hz). Dropping is immediate.</summary>
    public float RestRisePerSample { get; init; } = 0.004f;

    /// <summary>Before any rest is learned: samples the trigger must sit still to adopt that value as the initial rest.</summary>
    public int InitialRestSamples { get; init; } = 200;
}

/// <summary>Learned state of one trigger, for saving to a profile.</summary>
public sealed record TriggerCalibrationData
{
    public float Rest { get; init; }
    public bool RestLearned { get; init; }
    public float EndStop { get; init; } = 255f;
    public bool EndStopLearned { get; init; }
    public PlateauKind EndStopKind { get; init; }
    public int PressCount { get; init; }
    public float[] RecentPeaks { get; init; } = Array.Empty<float>();
    public PlateauKind[] RecentPeakKinds { get; init; } = Array.Empty<PlateauKind>();
}

/// <summary>
/// Learns the real travel of an analog trigger from normal play and remaps it to the full 0..1 range.
///
/// A worn trigger often stops short (an R2 that never gets past 200 of 255). The calibrator watches
/// each press and records where the trigger comes to a stop (a plateau) and how flat that plateau
/// was. A mechanical end stop is repeatable and pins the value; partial presses land all over the
/// place and wobble. Once enough presses agree on a value, that value becomes the new 100%.
///
/// Safeguards against learning a partial hold:
/// - a plateau must last a while (a quick tap never counts);
/// - first learning needs several agreeing presses (5 steady/pinned, or 8 if they are all wobbly);
/// - lowering a learned end stop needs 8 agreeing presses that are at least as flat as the ones that
///   confirmed it, and no recent press may have gone clearly further;
/// - the end stop never goes below <see cref="TriggerCalibrationOptions.MinimumEndStop"/>.
/// The rest position only creeps up slowly and only from values right above it, so a light held
/// throttle or a slow squeeze is never learned as "released".
/// </summary>
public sealed class TriggerCalibrator
{
    private readonly TriggerCalibrationOptions _o;
    private readonly float[] _window;
    private int _windowCount;
    private int _windowIndex;

    private readonly Queue<(float Value, PlateauKind Kind)> _peaks = new();
    private bool _inPress;
    private readonly Run[] _runs;
    private readonly float[] _bestPlateau = new float[3];

    private Run _restRun;
    private float _rest;
    private bool _restLearned;
    private float _endStop = 255f;
    private bool _endStopLearned;
    private PlateauKind _endStopKind;
    private int _pressCount;

    public TriggerCalibrator(TriggerCalibrationOptions? options = null)
    {
        _o = options ?? new TriggerCalibrationOptions();
        _window = new float[Math.Max(2, _o.StableWindow)];
        _runs = new[]
        {
            new Run(_o.LoosePlateauSpread, _o.LoosePlateauSamples),
            new Run(_o.SteadyPlateauSpread, _o.SteadyPlateauSamples),
            new Run(_o.PinnedPlateauSpread, _o.SteadyPlateauSamples),
        };
        _restRun = new Run(_o.StableTolerance, _o.InitialRestSamples);
        ClearPress();
    }

    /// <summary>Learned rest position in counts.</summary>
    public float Rest => _rest;

    /// <summary>Learned end stop (full press) in counts; 255 until learned.</summary>
    public float EndStop => _endStop;

    public bool EndStopLearned => _endStopLearned;

    /// <summary>How flat the presses were that confirmed the current end stop.</summary>
    public PlateauKind EndStopKind => _endStopKind;

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
        if (!float.IsFinite(raw)) return 0f;
        float low = _rest + _o.RestDeadzone;
        float high = Math.Max(low + 1f, _endStop - _o.SaturationMargin);
        return Math.Clamp((raw - low) / (high - low), 0f, 1f);
    }

    /// <summary>Forgets everything learned and goes back to factory 0..255.</summary>
    public void Reset()
    {
        _rest = 0;
        _restLearned = false;
        _endStop = 255f;
        _endStopLearned = false;
        _endStopKind = PlateauKind.Loose;
        _pressCount = 0;
        _peaks.Clear();
        _inPress = false;
        _windowCount = 0;
        _restRun.Clear();
        ClearPress();
    }

    public TriggerCalibrationData Export() => new()
    {
        Rest = _rest,
        RestLearned = _restLearned,
        EndStop = _endStop,
        EndStopLearned = _endStopLearned,
        EndStopKind = _endStopKind,
        PressCount = _pressCount,
        RecentPeaks = _peaks.Select(p => p.Value).ToArray(),
        RecentPeakKinds = _peaks.Select(p => p.Kind).ToArray(),
    };

    /// <summary>Restores a saved state. Corrupt values (NaN, infinity, out of range, missing arrays) fall back to defaults.</summary>
    public void Import(TriggerCalibrationData? data)
    {
        Reset();
        if (data == null) return;

        _rest = float.IsFinite(data.Rest) ? Math.Clamp(data.Rest, 0f, _o.MaxRest) : 0f;
        _restLearned = float.IsFinite(data.Rest) && (data.RestLearned || _rest > 0f);

        if (float.IsFinite(data.EndStop))
        {
            _endStop = Math.Clamp(data.EndStop, _o.MinimumEndStop, 255f);
            _endStopLearned = data.EndStopLearned;
            _endStopKind = Enum.IsDefined(data.EndStopKind) ? data.EndStopKind : PlateauKind.Loose;
        }

        _pressCount = Math.Max(0, data.PressCount);

        var peaks = data.RecentPeaks ?? Array.Empty<float>();
        var kinds = data.RecentPeakKinds ?? Array.Empty<PlateauKind>();
        int start = Math.Max(0, peaks.Length - _o.PeakHistory);
        for (int i = start; i < peaks.Length; i++)
        {
            if (!float.IsFinite(peaks[i])) continue;
            var kind = kinds.Length == peaks.Length && Enum.IsDefined(kinds[i]) ? kinds[i] : PlateauKind.Loose;
            _peaks.Enqueue((Math.Clamp(peaks[i], 0f, 255f), kind));
        }
    }

    private void Learn(byte raw)
    {
        _window[_windowIndex] = raw;
        _windowIndex = (_windowIndex + 1) % _window.Length;
        if (_windowCount < _window.Length) _windowCount++;

        if (!_inPress)
        {
            if (raw > _rest + _o.PressThreshold)
            {
                _inPress = true;
                ClearPress();
                _restRun.Clear();
            }
            else
            {
                LearnRest(raw);
            }
        }

        if (_inPress)
        {
            for (int k = 0; k < _runs.Length; k++)
            {
                _runs[k].Add(raw);
                if (_runs[k].IsLongEnough && !(_bestPlateau[k] >= _runs[k].Mean)) _bestPlateau[k] = _runs[k].Mean; // NaN-safe max
            }

            if (raw < _rest + _o.PressThreshold / 2f)
            {
                _inPress = false;
                EndPress();
            }
        }
    }

    private void LearnRest(byte raw)
    {
        if (!_restLearned)
        {
            // Nothing learned yet: adopt a value the trigger sits still on for a long time.
            _restRun.Add(raw);
            if (_restRun.IsLongEnough && _restRun.Mean <= _o.MaxRest)
            {
                _rest = _restRun.Mean;
                _restLearned = true;
            }
        }

        if (!TryGetStableValue(out float held)) return;
        if (held < _rest)
        {
            _rest = held; // a lower still value is always right: follow at once
            _restLearned = true;
        }
        else if (held <= _rest + _o.RestRiseWindow && held <= _o.MaxRest)
        {
            // Only creep up from values right above the rest, about 1 count per second, so a light
            // held throttle or a slow squeeze never becomes the rest position.
            _rest = Math.Min(_o.MaxRest, _rest + Math.Min(held - _rest, _o.RestRisePerSample));
        }
    }

    private void ClearPress()
    {
        foreach (var r in _runs) r.Clear();
        Array.Fill(_bestPlateau, float.NaN);
    }

    private void EndPress()
    {
        // Math.Max with NaN yields NaN, so seed the best values explicitly.
        float peak = float.NaN;
        foreach (var v in _bestPlateau)
            if (!float.IsNaN(v) && (float.IsNaN(peak) || v > peak)) peak = v;
        if (float.IsNaN(peak)) return; // never held still: a tap, not evidence of anything

        var kind = PlateauKind.Loose;
        for (int k = _bestPlateau.Length - 1; k > 0; k--)
        {
            if (!float.IsNaN(_bestPlateau[k]) && _bestPlateau[k] >= peak - _o.ClusterTolerance / 2f)
            {
                kind = (PlateauKind)k;
                break;
            }
        }
        RecordPeak(peak, kind);
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

    private void RecordPeak(float peak, PlateauKind kind)
    {
        _pressCount++;
        _peaks.Enqueue((peak, kind));
        while (_peaks.Count > _o.PeakHistory) _peaks.Dequeue();

        // 1. The trigger reaches further than the learned end stop: raise quickly.
        if (_endStopLearned
            && TryFindCluster(PlateauKind.Loose, _o.PeaksToRaiseEndStop, out float up, out var upKind)
            && up > _endStop)
        {
            _endStop = Math.Min(255f, up);
            _endStopKind = upKind;
            return;
        }

        // 2. First learning, or lowering a learned end stop.
        PlateauKind minKind = _endStopLearned ? _endStopKind : PlateauKind.Loose;
        bool found;
        float stop;
        PlateauKind stopKind;
        if (_endStopLearned)
        {
            found = TryFindCluster(minKind, _o.PeaksToLowerEndStop, out stop, out stopKind);
        }
        else
        {
            found = TryFindCluster(PlateauKind.Steady, _o.SteadyPeaksForEndStop, out stop, out stopKind);
            if (TryFindCluster(PlateauKind.Loose, _o.LoosePeaksForEndStop, out float loose, out var looseKind)
                && (!found || loose > stop))
            {
                (found, stop, stopKind) = (true, loose, looseKind);
            }
        }
        if (!found) return;

        // A learned end stop only moves down, and never while a recent press went clearly further.
        if (_endStopLearned && stop >= _endStop) return;
        if (_peaks.Any(p => p.Value > stop + _o.ClusterTolerance * 2)) return;

        _endStop = Math.Clamp(stop, _o.MinimumEndStop, 255f);
        _endStopKind = stopKind;
        _endStopLearned = true;
    }

    /// <summary>
    /// Finds the highest value that at least <paramref name="minSupport"/> recent presses of at least
    /// <paramref name="minKind"/> flatness agree on. Returns a value at the lower part of that group so
    /// the output reliably hits 100%, and the flatness most of the group had.
    /// </summary>
    private bool TryFindCluster(PlateauKind minKind, int minSupport, out float stop, out PlateauKind kind)
    {
        stop = 0;
        kind = PlateauKind.Loose;
        var sorted = _peaks.Where(p => p.Kind >= minKind).OrderByDescending(p => p.Value).ToArray();
        for (int i = 0; i < sorted.Length; i++)
        {
            float top = sorted[i].Value;
            int j = i;
            while (j + 1 < sorted.Length && top - sorted[j + 1].Value <= _o.ClusterTolerance) j++;
            int count = j - i + 1;
            if (count < minSupport) continue;

            var group = sorted[i..(j + 1)];
            stop = Math.Min(group[group.Length / 2].Value, group[^1].Value + _o.ClusterTolerance / 2f);
            // The weakest flatness shared by the majority of the group.
            var kinds = group.Select(g => g.Kind).OrderByDescending(k => k).ToArray();
            kind = kinds[kinds.Length / 2];
            return true;
        }
        return false;
    }

    /// <summary>A run of samples whose max-min spread stays within a limit.</summary>
    private sealed class Run
    {
        private readonly float _maxSpread;
        private readonly int _minSamples;
        private float _min, _max, _sum;
        private int _count;

        public Run(float maxSpread, int minSamples)
        {
            _maxSpread = maxSpread;
            _minSamples = Math.Max(1, minSamples);
        }

        public bool IsLongEnough => _count >= _minSamples;
        public float Mean => _count == 0 ? 0f : _sum / _count;

        public void Clear() => _count = 0;

        public void Add(float v)
        {
            if (_count > 0 && Math.Max(_max, v) - Math.Min(_min, v) <= _maxSpread)
            {
                _min = Math.Min(_min, v);
                _max = Math.Max(_max, v);
                _sum += v;
                _count++;
                return;
            }
            _min = _max = _sum = v;
            _count = 1;
        }
    }
}

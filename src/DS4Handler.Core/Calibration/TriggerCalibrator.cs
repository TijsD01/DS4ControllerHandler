namespace DS4Handler.Core.Calibration;

/// <summary>
/// How flat a held trigger position was. A mechanical end stop pins the value; a finger holding a
/// partial press always wobbles a little. <see cref="Guided"/> marks an end stop set by the guided
/// calibration (the user was asked to press fully), which automatic learning never lowers.
/// </summary>
public enum PlateauKind
{
    /// <summary>Held, but noisy.</summary>
    Loose = 0,

    /// <summary>Held steadily (about ±1 count) for a sustained run.</summary>
    Steady = 1,

    /// <summary>Pinned: the value sits on one or two adjacent counts.</summary>
    Pinned = 2,

    /// <summary>End stop set by <see cref="TriggerCalibrator.StartGuidedEndStop"/>.</summary>
    Guided = 3,
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

    /// <summary>Largest spread of a rough plateau: a noisy end stop (±3) that is held for a long time.</summary>
    public float RoughPlateauSpread { get; init; } = 6f;

    /// <summary>Samples a rough plateau must last (about 1 s); a human hold drifts more than that over so long.</summary>
    public int RoughPlateauSamples { get; init; } = 250;

    /// <summary>Largest max-min spread of a steady plateau (about ±1 count).</summary>
    public float SteadyPlateauSpread { get; init; } = 2f;

    /// <summary>Samples a steady or pinned plateau must last (about 0.25 s).</summary>
    public int SteadyPlateauSamples { get; init; } = 60;

    /// <summary>Largest max-min spread of a pinned plateau.</summary>
    public float PinnedPlateauSpread { get; init; } = 1f;

    /// <summary>
    /// Rise per sample (counts) that counts as hitting the plateau hard. A trigger slammed into its
    /// mechanical stop arrives fast and stops dead; a finger easing to a partial position decelerates.
    /// </summary>
    public float ImpactVelocity { get; init; } = 4f;

    /// <summary>Samples before a plateau starts in which the impact velocity must be seen.</summary>
    public int ImpactLookback { get; init; } = 3;

    // ---- end stop decisions

    /// <summary>Recent presses kept to find the trigger's real end stop.</summary>
    public int PeakHistory { get; init; } = 16;

    /// <summary>Pinned presses (with impact) that must agree before an end stop is first learned.</summary>
    public int PinnedPeaksForEndStop { get; init; } = 5;

    /// <summary>Presses of any flatness (with impact) that must agree before an end stop is first learned.</summary>
    public int PeaksForEndStop { get; init; } = 8;

    /// <summary>
    /// Agreeing presses needed to lower an already learned end stop. They must have impact and be at
    /// least as flat as the presses that confirmed the current end stop.
    /// </summary>
    public int PeaksToLowerEndStop { get; init; } = 8;

    /// <summary>
    /// Automatic learning lowers a learned end stop by at most this many counts at a time (gradual wear).
    /// A sudden big drop is far more likely a steady partial hold; use the guided calibration for that.
    /// </summary>
    public float MaxLowerStep { get; init; } = 12f;

    /// <summary>Agreeing presses needed to raise the end stop (the trigger reaches further than we thought).</summary>
    public int PeaksToRaiseEndStop { get; init; } = 3;

    /// <summary>Peaks within this many counts of each other are treated as the same end stop.</summary>
    public float ClusterTolerance { get; init; } = 4f;

    /// <summary>The learned end stop is never set below this (counts), so a bad guess cannot make light presses read as full.</summary>
    public float MinimumEndStop { get; init; } = 160f;

    // ---- guided calibration

    /// <summary>Full presses the guided calibration uses.</summary>
    public int GuidedPresses { get; init; } = 3;

    /// <summary>Largest spread of a plateau during guided calibration.</summary>
    public float GuidedPlateauSpread { get; init; } = 6f;

    /// <summary>Samples a plateau must last during guided calibration.</summary>
    public int GuidedPlateauSamples { get; init; } = 30;

    // ---- rest position

    /// <summary>Deadzone above the learned rest position (counts).</summary>
    public float RestDeadzone { get; init; } = 2f;

    /// <summary>The output reaches 100% this many counts before the end stop, so jitter at full press still reads as full.</summary>
    public float SaturationMargin { get; init; } = 2f;

    /// <summary>Largest rest position the calibrator will accept (counts), e.g. a sticky trigger that no longer returns fully.</summary>
    public float MaxRest { get; init; } = 40f;

    /// <summary>The rest position creeps up only from a still value at most this many counts above it.</summary>
    public float RestRiseWindow { get; init; } = 4f;

    /// <summary>Largest creep of the rest position per sample (0.004 ≈ 1 count per second at 250 Hz). Dropping is immediate.</summary>
    public float RestRisePerSample { get; init; } = 0.004f;

    /// <summary>Before any rest is learned: samples the trigger must sit still to adopt that value as the initial rest.</summary>
    public int InitialRestSamples { get; init; } = 375;

    /// <summary>
    /// After a release: samples the trigger must sit still (starting right after it came down) for that
    /// value to become the new rest, even if it is more than <see cref="RestRiseWindow"/> above the old one.
    /// </summary>
    public int ReleasedRestSamples { get; init; } = 750;

    /// <summary>Samples after a press ends within which the trigger must have settled for <see cref="ReleasedRestSamples"/> to apply.</summary>
    public int ReleaseSettleSamples { get; init; } = 100;
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

    /// <summary>Highest plateau ever seen on this trigger; a first-learned end stop is never below it.</summary>
    public float HighestPlateau { get; init; }

    public float[] RecentPeaks { get; init; } = Array.Empty<float>();
    public PlateauKind[] RecentPeakKinds { get; init; } = Array.Empty<PlateauKind>();
    public bool[] RecentPeakImpacts { get; init; } = Array.Empty<bool>();
}

/// <summary>
/// Learns the real travel of an analog trigger from normal play and remaps it to the full 0..1 range.
///
/// A worn trigger often stops short (an R2 that never gets past 200 of 255). The calibrator watches
/// each press and records where the trigger comes to a stop (a plateau), how flat that plateau was
/// and whether it was hit hard (impact). A mechanical end stop is repeatable, hit fast and pins the
/// value; partial presses land all over the place, are eased into and wobble. Once enough presses
/// agree on a value, that value becomes the new 100%.
///
/// Safeguards against learning a partial hold:
/// - a plateau must last a while (a quick tap never counts) and only presses with impact count;
/// - first learning needs 5 agreeing pinned presses, or 8 of any flatness, and may not be below the
///   highest plateau ever seen on this trigger;
/// - lowering a learned end stop needs 8 agreeing presses at least as flat as the ones that confirmed
///   it, no recent press may have gone clearly further, and it moves at most 12 counts at a time;
/// - a guided end stop (<see cref="StartGuidedEndStop"/>) is never lowered automatically;
/// - the end stop never goes below <see cref="TriggerCalibrationOptions.MinimumEndStop"/>.
///
/// The rest position creeps up only slowly from values right above it, or jumps to a value the trigger
/// settles on for several seconds right after a release (a sticky trigger), so a light held throttle
/// or a slow squeeze is never learned as "released".
/// </summary>
public sealed class TriggerCalibrator
{
    private const int LooseRun = 0, RoughRun = 1, SteadyRun = 2, PinnedRun = 3, GuidedRun = 4;

    private readonly TriggerCalibrationOptions _o;
    private readonly float[] _window;
    private int _windowCount;
    private int _windowIndex;

    private readonly Queue<Peak> _peaks = new();
    private readonly Peak[] _clusterScratch;
    private bool _inPress;
    private readonly Run[] _runs;
    private readonly float[] _best = new float[5];
    private readonly bool[] _bestImpact = new bool[5];
    private readonly float[] _recent;
    private int _recentCount;
    private int _recentIndex;

    private readonly Run _initialRestRun;
    private readonly Run _releasedRestRun;
    private bool _afterRelease;
    private int _samplesSinceRelease;

    private float _rest;
    private bool _restLearned;
    private float _lowestStill = float.MaxValue;
    private float _endStop = 255f;
    private bool _endStopLearned;
    private PlateauKind _endStopKind;
    private float _highestPlateau;
    private int _pressCount;

    private int _guidedRemaining;
    private readonly List<float> _guidedPeaks = new();

    private readonly record struct Peak(float Value, PlateauKind Kind, bool Impact);

    public TriggerCalibrator(TriggerCalibrationOptions? options = null)
    {
        _o = options ?? new TriggerCalibrationOptions();
        _window = new float[Math.Max(2, _o.StableWindow)];
        _recent = new float[Math.Max(2, _o.ImpactLookback + 1)];
        _clusterScratch = new Peak[Math.Max(1, _o.PeakHistory) + 1];
        _runs = new[]
        {
            new Run(_o.LoosePlateauSpread, _o.LoosePlateauSamples),
            new Run(_o.RoughPlateauSpread, _o.RoughPlateauSamples),
            new Run(_o.SteadyPlateauSpread, _o.SteadyPlateauSamples),
            new Run(_o.PinnedPlateauSpread, _o.SteadyPlateauSamples),
            new Run(_o.GuidedPlateauSpread, _o.GuidedPlateauSamples),
        };
        _initialRestRun = new Run(_o.StableTolerance, _o.InitialRestSamples);
        _releasedRestRun = new Run(_o.StableTolerance, _o.ReleasedRestSamples);
        ClearPress();
    }

    /// <summary>Learned rest position in counts.</summary>
    public float Rest => _rest;

    /// <summary>Learned end stop (full press) in counts; 255 until learned.</summary>
    public float EndStop => _endStop;

    public bool EndStopLearned => _endStopLearned;

    /// <summary>How the current end stop was confirmed (flatness of its presses, or guided).</summary>
    public PlateauKind EndStopKind => _endStopKind;

    /// <summary>Highest plateau ever seen on this trigger (counts).</summary>
    public float HighestPlateau => _highestPlateau;

    public int PressCount => _pressCount;

    /// <summary>When false, the calibrator only maps values and stops learning (a guided calibration still runs).</summary>
    public bool LearningEnabled { get; set; } = true;

    /// <summary>True while the guided calibration waits for full presses.</summary>
    public bool GuidedInProgress => _guidedRemaining > 0;

    /// <summary>Full presses the guided calibration still needs.</summary>
    public int GuidedPressesRemaining => _guidedRemaining;

    /// <summary>
    /// Starts the guided end stop calibration: the next full presses (the UI asks the user to press the
    /// trigger all the way and hold it briefly) decide the end stop with relaxed rules. The result is
    /// marked <see cref="PlateauKind.Guided"/> and is never lowered by automatic learning, only raised.
    /// </summary>
    public void StartGuidedEndStop()
    {
        _guidedRemaining = Math.Max(1, _o.GuidedPresses);
        _guidedPeaks.Clear();
    }

    public void CancelGuided()
    {
        _guidedRemaining = 0;
        _guidedPeaks.Clear();
    }

    /// <summary>Feeds one raw sample (0..255), updates the model and returns the calibrated value (0..1).</summary>
    public float Process(byte raw)
    {
        if (LearningEnabled || GuidedInProgress) Learn(raw);
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
        _lowestStill = float.MaxValue;
        _endStop = 255f;
        _endStopLearned = false;
        _endStopKind = PlateauKind.Loose;
        _highestPlateau = 0;
        _pressCount = 0;
        _peaks.Clear();
        _inPress = false;
        _windowCount = 0;
        _initialRestRun.Clear();
        _releasedRestRun.Clear();
        _afterRelease = false;
        _recentCount = 0;
        _recentIndex = 0;
        CancelGuided();
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
        HighestPlateau = _highestPlateau,
        RecentPeaks = _peaks.Select(p => p.Value).ToArray(),
        RecentPeakKinds = _peaks.Select(p => p.Kind).ToArray(),
        RecentPeakImpacts = _peaks.Select(p => p.Impact).ToArray(),
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

        _highestPlateau = float.IsFinite(data.HighestPlateau) ? Math.Clamp(data.HighestPlateau, 0f, 255f) : 0f;
        _pressCount = Math.Max(0, data.PressCount);

        var peaks = data.RecentPeaks ?? Array.Empty<float>();
        var kinds = data.RecentPeakKinds ?? Array.Empty<PlateauKind>();
        var impacts = data.RecentPeakImpacts ?? Array.Empty<bool>();
        int start = Math.Max(0, peaks.Length - _o.PeakHistory);
        for (int i = start; i < peaks.Length; i++)
        {
            if (!float.IsFinite(peaks[i])) continue;
            var kind = kinds.Length == peaks.Length && Enum.IsDefined(kinds[i]) && kinds[i] != PlateauKind.Guided
                ? kinds[i] : PlateauKind.Loose;
            bool impact = impacts.Length == peaks.Length && impacts[i];
            float value = Math.Clamp(peaks[i], 0f, 255f);
            _peaks.Enqueue(new Peak(value, kind, impact));
            _highestPlateau = Math.Max(_highestPlateau, value);
        }
    }

    // ------------------------------------------------------------------ learning

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
                _initialRestRun.Clear();
                _afterRelease = false;
            }
            else if (LearningEnabled)
            {
                LearnRest(raw);
            }
        }

        if (_inPress)
        {
            // Largest rise per sample over the last few samples: how hard the press is arriving.
            float velocity = 0f;
            float next = raw;
            for (int i = 1; i <= _recentCount; i++)
            {
                float prev = _recent[(_recentIndex - i + _recent.Length) % _recent.Length];
                velocity = Math.Max(velocity, next - prev);
                next = prev;
            }
            bool impactNow = velocity >= _o.ImpactVelocity;

            for (int k = 0; k < _runs.Length; k++)
            {
                _runs[k].Add(raw, impactNow);
                if (_runs[k].IsLongEnough && !(_best[k] >= _runs[k].Mean)) // NaN-safe max
                {
                    _best[k] = _runs[k].Mean;
                    _bestImpact[k] = _runs[k].Impact;
                }
            }

            if (raw < _rest + _o.PressThreshold / 2f)
            {
                _inPress = false;
                EndPress();
                _afterRelease = true;
                _samplesSinceRelease = 0;
                _releasedRestRun.Clear();
            }
        }

        _recent[_recentIndex] = raw;
        _recentIndex = (_recentIndex + 1) % _recent.Length;
        if (_recentCount < _recent.Length) _recentCount++;
    }

    private void LearnRest(byte raw)
    {
        if (!_restLearned)
        {
            // Nothing learned yet: adopt a value the trigger sits still on for a long time.
            // Never higher than a still value seen before (resting at 0, then holding 35, is a press).
            _initialRestRun.Add(raw, false);
            if (TryGetStableValue(out float still)) _lowestStill = Math.Min(_lowestStill, still);
            if (_initialRestRun.IsLongEnough && _initialRestRun.Mean <= _o.MaxRest
                && _initialRestRun.Mean <= _lowestStill + _o.StableTolerance)
            {
                _rest = _initialRestRun.Mean;
                _restLearned = true;
            }
        }

        if (_afterRelease)
        {
            // Right after a release: if the trigger settles and then sits still for a few seconds,
            // that is where it now rests (a sticky trigger), even well above the old rest.
            _samplesSinceRelease++;
            bool restarted = _releasedRestRun.Add(raw, false);
            if (restarted && _samplesSinceRelease > _o.ReleaseSettleSamples)
            {
                _afterRelease = false; // it moved again after settling: not a release rest
            }
            else if (_releasedRestRun.IsLongEnough)
            {
                float mean = _releasedRestRun.Mean;
                if (mean <= _o.MaxRest && mean > _rest)
                {
                    _rest = mean;
                    _restLearned = true;
                }
                _afterRelease = false;
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
        Array.Fill(_best, float.NaN);
        Array.Fill(_bestImpact, false);
    }

    private void EndPress()
    {
        if (GuidedInProgress && !float.IsNaN(_best[GuidedRun]))
        {
            float g = _best[GuidedRun];
            for (int k = 0; k < GuidedRun; k++)
                if (!float.IsNaN(_best[k]) && _best[k] > g) g = _best[k];
            _guidedPeaks.Add(g);
            _guidedRemaining--;
            if (_guidedRemaining == 0) FinishGuided();
        }

        float peak = float.NaN;
        int bestRun = -1;
        for (int k = 0; k < GuidedRun; k++)
        {
            if (!float.IsNaN(_best[k]) && (float.IsNaN(peak) || _best[k] > peak))
            {
                peak = _best[k];
                bestRun = k;
            }
        }
        if (bestRun < 0) return; // never held still: a tap, not evidence of anything

        var kind = PlateauKind.Loose;
        bool impact = _bestImpact[bestRun];
        if (!float.IsNaN(_best[PinnedRun]) && _best[PinnedRun] >= peak - _o.ClusterTolerance / 2f)
        {
            kind = PlateauKind.Pinned;
            impact |= _bestImpact[PinnedRun];
        }
        else if (!float.IsNaN(_best[SteadyRun]) && _best[SteadyRun] >= peak - _o.ClusterTolerance / 2f)
        {
            kind = PlateauKind.Steady;
            impact |= _bestImpact[SteadyRun];
        }

        if (LearningEnabled) RecordPeak(peak, kind, impact);
    }

    private void FinishGuided()
    {
        var sorted = _guidedPeaks.OrderBy(v => v).ToArray();
        _guidedPeaks.Clear();
        if (sorted.Length == 0) return;
        // Median, but not more than a little above the lowest press so every full press reads 100%.
        float stop = Math.Min(sorted[sorted.Length / 2], sorted[0] + _o.ClusterTolerance / 2f);
        _endStop = Math.Clamp(stop, _o.MinimumEndStop, 255f);
        _endStopKind = PlateauKind.Guided;
        _endStopLearned = true;
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

    private void RecordPeak(float peak, PlateauKind kind, bool impact)
    {
        _pressCount++;
        _highestPlateau = Math.Max(_highestPlateau, peak);
        _peaks.Enqueue(new Peak(peak, kind, impact));
        while (_peaks.Count > _o.PeakHistory) _peaks.Dequeue();
        if (GuidedInProgress) return; // the guided calibration decides

        // 1. The trigger reaches further than the learned end stop: raise quickly.
        if (_endStopLearned
            && TryFindCluster(PlateauKind.Loose, false, _o.PeaksToRaiseEndStop, out float up, out var upKind)
            && up > _endStop)
        {
            _endStop = Math.Min(255f, up);
            if (_endStopKind != PlateauKind.Guided) _endStopKind = upKind;
            return;
        }

        float stop;
        PlateauKind stopKind;
        if (_endStopLearned)
        {
            // 2. Lowering (wear): never below a guided end stop, only small steps, strong evidence.
            if (_endStopKind == PlateauKind.Guided) return;
            if (!TryFindCluster(_endStopKind, true, _o.PeaksToLowerEndStop, out stop, out stopKind)) return;
            if (stop >= _endStop || stop < _endStop - _o.MaxLowerStep) return;
        }
        else
        {
            // 3. First learning.
            bool found = TryFindCluster(PlateauKind.Pinned, true, _o.PinnedPeaksForEndStop, out stop, out stopKind);
            if (TryFindCluster(PlateauKind.Loose, true, _o.PeaksForEndStop, out float any, out var anyKind) && (!found || any > stop))
            {
                (found, stop, stopKind) = (true, any, anyKind);
            }
            if (!found) return;
            // Never below a plateau this trigger has ever reached.
            if (stop < _highestPlateau - _o.ClusterTolerance * 2) return;
        }

        // No recent press may have gone clearly further.
        float ceiling = stop + _o.ClusterTolerance * 2;
        foreach (var p in _peaks)
            if (p.Value > ceiling) return;

        _endStop = Math.Clamp(stop, _o.MinimumEndStop, 255f);
        _endStopKind = stopKind;
        _endStopLearned = true;
    }

    /// <summary>
    /// Finds the highest value that at least <paramref name="minSupport"/> recent presses of at least
    /// <paramref name="minKind"/> flatness (and with impact, if required) agree on. Returns a value at the
    /// lower part of that group so the output reliably hits 100%, and the flatness most of the group had.
    /// </summary>
    private bool TryFindCluster(PlateauKind minKind, bool needImpact, int minSupport, out float stop, out PlateauKind kind)
    {
        stop = 0;
        kind = PlateauKind.Loose;
        // Filter into a preallocated buffer and insertion-sort it by value, highest first (stable).
        var sorted = _clusterScratch;
        int n = 0;
        foreach (var p in _peaks)
        {
            if (p.Kind < minKind || (needImpact && !p.Impact)) continue;
            if (n == sorted.Length) break;
            int pos = n++;
            while (pos > 0 && sorted[pos - 1].Value < p.Value)
            {
                sorted[pos] = sorted[pos - 1];
                pos--;
            }
            sorted[pos] = p;
        }
        for (int i = 0; i < n; i++)
        {
            float top = sorted[i].Value;
            int j = i;
            while (j + 1 < n && top - sorted[j + 1].Value <= _o.ClusterTolerance) j++;
            int count = j - i + 1;
            if (count < minSupport) continue;

            stop = Math.Min(sorted[i + count / 2].Value, sorted[j].Value + _o.ClusterTolerance / 2f);
            // Median flatness of the group, highest kind first.
            Span<int> kindCounts = stackalloc int[4];
            for (int g = i; g <= j; g++) kindCounts[(int)sorted[g].Kind]++;
            int target = count / 2, seen = 0;
            for (int k = 3; k >= 0; k--)
            {
                seen += kindCounts[k];
                if (seen > target)
                {
                    kind = (PlateauKind)k;
                    break;
                }
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// A run of samples whose max-min spread stays within a limit. <see cref="Impact"/> tells whether
    /// the run was entered with a hard rise; a run that restarts because of noise at the same level
    /// keeps the impact of the run before it.
    /// </summary>
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
        public bool Impact { get; private set; }

        public void Clear()
        {
            _count = 0;
            Impact = false;
        }

        /// <summary>Adds a sample; returns true when the run had to restart.</summary>
        public bool Add(float v, bool impactNow)
        {
            if (_count > 0 && Math.Max(_max, v) - Math.Min(_min, v) <= _maxSpread)
            {
                _min = Math.Min(_min, v);
                _max = Math.Max(_max, v);
                _sum += v;
                _count++;
                return false;
            }
            bool continuation = _count > 0 && Impact && Math.Abs(v - Mean) <= _maxSpread * 1.5f;
            Impact = impactNow || continuation;
            _min = _max = _sum = v;
            _count = 1;
            return true;
        }
    }
}
